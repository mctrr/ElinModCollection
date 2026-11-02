using System;
using System.Collections.Generic;
using System.Threading;
using ACS.API;
using ACS.Frame;
using ACS.Skin;
using UnityEngine;
using ACS.Tiling;

namespace ACS.Patches;

/// <summary>
/// 场景加载时的贴图预热：把当前地图里 NPC 会用到的高清动态贴图
/// 先丢到后台线程解成像素，再回主线程建好贴图放进 <see cref="TiledSprite"/>。
/// 这样 NPC 第一次进视野走的是缓存命中，而不是在主线程同步解码（卡顿来源）。
///
/// 贴图来源两条：
///   ① <c>SpriteReplacer.dictSkins</c> —— 原版 ListSkins 收的（来自各包的 <c>Skin\</c> 目录）；
///   ② <c>TextureSkinCatalog</c> —— 只按目录编目的（<c>Texture\&lt;id&gt;\</c> 子目录布局）。
///      原版 ListSkins 不收这些；它们会在首个渲染帧由 <c>EnsureSkin</c> 主线程同步解码。
///
/// 时机：<c>Scene.Init</c> 挂标记，之后每帧重试直到地图就绪；就绪后每
/// <see cref="RescanFrames"/> 帧补扫一次（NPC 会晚于首次扫描进场）。
/// </summary>
internal static class GreetPrewarm
{
    private const int MaxParallel = 6;
    private const int RescanFrames = 30;

    // —— 以下各项由 BcsConfig（配置页「场景预热」组）写入；未初始化时用这里的默认值。 ——


    internal static bool WarmBase = true;
    internal static bool WarmIdle = true;
    internal static bool WarmGreet = true;
    internal static bool WarmMove = true;
    internal static bool WarmAtk;
    internal static bool WarmCombat;
    internal static bool WarmHurt;
    internal static bool WarmRevive;
    internal static bool WarmEat;
    internal static bool WarmCmove;

    /// <summary>
    /// 本场景真有角色在用的 ACS 前缀（<c>acs_override</c> / <c>snow</c> / <c>married</c>）。
    /// 前缀是作者给差分包起的名字（yongzhuang / xn / xingnian …），不是状态；
    /// 只有「正在被某个角色选中的前缀」才预热，没在用的一律不碰。
    /// </summary>
    private static readonly HashSet<string> ActivePrefixes = new(StringComparer.Ordinal);

    /// <summary>
    /// 每帧用来「建贴图 + 上传显存」的时间预算（毫秒）。一张 16384×512 的 RGBA32 分片
    /// 上传就是 ~32MB，一次把解好的全建完会把进场那一帧堵成几百毫秒 —— 按预算摊到
    /// 后续若干帧，进场从「一堵墙」变成「一条缓坡」。至少保证每帧建一张，不会饿死。
    /// </summary>
    private const int AdoptBudgetMs = 4;

    /// <summary>已解好、还没建贴图的最大积压张数；到顶就暂停派发新的解码任务，避免像素吃内存。</summary>
    private const int MaxPendingPixels = 8;

    /// <summary>阻塞预热的兜底上限：个别坏图/异常不能让读档永远卡住，超时就退回按帧预热。</summary>
    private const int WarmTimeoutMs = 15000;

    /// <summary>
    /// 一个预热任务 = 「这张 png 提前走一遍取图接口」。
    /// 解出来的像素去哪、什么时候建贴图、占多少字节，全在 <see cref="TiledSprite"/> 内部 ——
    /// 这里不再持有 <c>DecodedImage</c>，也不再看缓存键与待建数量。
    /// </summary>
    private sealed class Job
    {
        public string Png = string.Empty;
        public int Frames;
        public int Ppu;
        public volatile bool IsDone;
    }

    private readonly struct Need
    {
        public readonly string Png;
        public readonly int Frames;
        public readonly int Ppu;
        public readonly int Order;

        public Need(string png, int frames, int ppu, int order)
        {
            Png = png;
            Frames = frames;
            Ppu = ppu;
            Order = order;
        }
    }

    private static readonly object Gate = new();
    private static readonly List<Job> Pending = new();
    private static readonly List<Job> InFlight = new();
    private static readonly HashSet<string> Handled = new(StringComparer.OrdinalIgnoreCase);
    private static int _lastScanFrame = -1;

    /// <summary>本轮收集到的待预热张数（累计；出报告后清零）。</summary>
    private static int _warmed;
    internal static void OnSceneChanged()
    {
        lock (Gate) {
            Pending.Clear();
            InFlight.Clear();
            Handled.Clear();
            _lastScanFrame = -1;
            _warmed = 0;
        }

        PrewarmProbe.Active = false;
        PrewarmProbe.Reset();
    }

    internal static void Tick()
    {
        long begin = System.Diagnostics.Stopwatch.GetTimestamp();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool probe = PrewarmProbe.Enabled;

        Scan();
        if (probe) {
            PrewarmProbe.TScan += sw.ElapsedTicks;
        }

        sw.Restart();
        DrainJobs();
        if (probe) {
            PrewarmProbe.TDrain += sw.ElapsedTicks;
        }

        sw.Restart();
        Dispatch();
        if (probe) {
            PrewarmProbe.TDispatch += sw.ElapsedTicks;
        }

        sw.Restart();
        AdoptPending(AdoptBudgetMs);
        if (probe) {
            PrewarmProbe.TAdopt += sw.ElapsedTicks;
        }

        if (probe) {
            PrewarmProbe.MarkActivity(begin, System.Diagnostics.Stopwatch.GetTimestamp(), false);
        }

        ReportWarmDone();
    }

    /// <summary>
    /// 场景切换时的<b>阻塞式</b>预热：在 <c>Scene.Init</c> 返回前，把本场景 NPC 会用到的
    /// 贴图全部解好、建好、传上显存。
    ///
    /// <b>为什么要阻塞</b>：切图/读档本来就是一段过渡（原版这时还在 <c>HideCover</c> 淡入），
    /// 把这段耗时塞进过渡里，玩家看到的是「读条久一点」；否则主线程的建贴图/上传会漏到
    /// 进图之后，变成「刚进图卡一两秒」。
    ///
    /// <b>主线程在这里干两件事</b>：① 把后台已经解好的像素建贴图、上传显存（
    /// <c>new Texture2D</c> / <c>LoadRawTextureData</c> / <c>Apply</c> / <c>Sprite.Create</c>，
    /// 这些是 Unity 引擎对象，只能主线程，搬不走）；② 剩下的间隙 <see cref="Thread.Sleep"/>
    /// 把 CPU 让给解码线程（读盘 + inflate + 反 filter 那 ~90%，已经在
    /// <see cref="MaxParallel"/> 条后台线程上跑）。
    ///
    /// 之所以要「等」，不是为了等待而等待：不等就得从 <c>Scene.Init</c> 返回，那没建完的贴图
    /// 会落到进图后的每一帧上 —— 正是要避免的那种卡。等，就是把这段耗时留在读条里。
    /// 墙钟时间 ≈ 总解码量 ÷ 并发数 + 主线程上传量。
    /// </summary>
    internal static void WarmBlocking()
    {
        long begin = System.Diagnostics.Stopwatch.GetTimestamp();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool probe = PrewarmProbe.Enabled;

        Scan();
        if (probe) {
            PrewarmProbe.TScan += sw.ElapsedTicks;
        }

        var total = System.Diagnostics.Stopwatch.StartNew();
        bool busy = true;
        while (busy && total.ElapsedMilliseconds < WarmTimeoutMs) {
            sw.Restart();
            busy = DrainJobs();
            if (probe) {
                PrewarmProbe.TDrain += sw.ElapsedTicks;
            }

            sw.Restart();
            Dispatch();
            if (probe) {
                PrewarmProbe.TDispatch += sw.ElapsedTicks;
            }

            sw.Restart();
            AdoptPending(int.MaxValue);
            if (probe) {
                PrewarmProbe.TAdopt += sw.ElapsedTicks;
            }

            sw.Restart();
            Thread.Sleep(1);
            if (probe) {
                PrewarmProbe.TSleep += sw.ElapsedTicks;
                PrewarmProbe.Loops++;
            }
        }

        sw.Restart();
        AdoptPending(int.MaxValue);
        if (probe) {
            PrewarmProbe.TAdopt += sw.ElapsedTicks;
        }

        if (probe) {
            PrewarmProbe.MarkActivity(begin, System.Diagnostics.Stopwatch.GetTimestamp(), true);
        }

        ReportWarmDone();
    }

    /// <summary>收后台结果：完成的 job 从在飞表里摘掉。返回 true = 还有活没干完。</summary>
    private static bool DrainJobs()
    {
        lock (Gate) {
            for (int i = InFlight.Count - 1; i >= 0; i--) {
                if (InFlight[i].IsDone) {
                    InFlight.RemoveAt(i);
                }
            }

            if (Pending.Count > 0 || InFlight.Count > 0) {
                return true;
            }
        }

        return TiledSprite.PendingCount > 0;
    }

    /// <summary>
    /// 分批建贴图。一张 16384×512 分片上传显存就是 ~32MB，一次全建会把一帧堵成几百毫秒，
    /// 所以常规渲染期按 <paramref name="budgetMs"/> 摊到多帧；阻塞预热期传 <c>int.MaxValue</c>
    /// 一次做完（那段本来就是过渡，不留到进图后）。
    /// 渲染若先用到某张，<see cref="TiledSprite.Get"/> 会就地为那一张补建。
    /// </summary>
    private static void AdoptPending(int budgetMs) => TiledSprite.AdoptPending(budgetMs);

    private static void Dispatch()
    {
        List<Job>? start = null;
        lock (Gate) {
            int room = MaxParallel - InFlight.Count;
            int backlog = MaxPendingPixels - TiledSprite.PendingCount;
            if (backlog < room) {
                room = backlog;
            }

            if (Pending.Count > 0 && room > 0) {
                int take = Math.Min(Pending.Count, room);
                start = Pending.GetRange(0, take);
                Pending.RemoveRange(0, take);
                InFlight.AddRange(start);
            }
        }

        if (start is null) {
            return;
        }

        PrewarmProbe.Active = true;
        foreach (Job job in start) {
            StartJob(job);
        }
    }

    /// <summary>
    /// 预热队列排空时打出性能报告：主线程各阶段 / 建贴图细分 / 后台解码，
    /// 用来判断该加并发（解码慢）还是该改时机（上传堵主线程）。打完清零，下一批重新计。
    /// </summary>
    private static void ReportWarmDone()
    {
        bool busy;
        lock (Gate) {
            busy = Pending.Count > 0 || InFlight.Count > 0;
        }

        busy = busy || TiledSprite.PendingCount > 0;

        if (busy || _warmed == 0) {
            return;
        }

        PrewarmProbe.Active = false;
        PrewarmProbe.Log(_warmed, MaxParallel);
        PrewarmProbe.Reset();
        _warmed = 0;
    }

    private static void Scan()
    {
        int frame = Time.frameCount;
        if (_lastScanFrame >= 0 && frame - _lastScanFrame < RescanFrames) {
            return;
        }

        // EClass._map 的 getter 在读档 / 切图 / 模组刷新窗口内会直接抛 NRE（不是返回 null），
        // 取它必须从第一行起就在 try 内；抛了就本帧跳过、下一帧重试。
        Map? map;
        try {
            map = EClass._map;
        }
        catch (Exception) {
            return;
        }

        if (map?.charas is null || map.charas.Count == 0) {
            return;
        }

        _lastScanFrame = frame;

        List<Need> needs;
        try {
            needs = Collect(new List<Chara>(map.charas));
        }
        catch (Exception ex) {
            AcsMod.Warn("[BCS] 预热扫描失败: " + ex.Message);
            return;
        }

        int queued = 0;
        lock (Gate) {
            needs.Sort((a, b) => a.Order.CompareTo(b.Order));
            foreach (Need need in needs) {
                if (!Handled.Add(need.Png)) {
                    continue;
                }

                Pending.Add(new Job { Png = need.Png, Frames = need.Frames, Ppu = need.Ppu });
                queued++;
            }
        }

        if (queued > 0) {
            _warmed += queued;
            AcsMod.Log("[BCS] 预热：本场景新增 " + queued + " 张（并行 " + MaxParallel + "）");
        }
    }

    /// <summary>
    /// 收本场景「真有角色在用」的 ACS 前缀，来自 <see cref="PlayState.GetPrefix"/>：
    /// <c>acs_override</c> 手动指定 &gt; 雪地 &gt; 已婚；取不到就是无前缀皮肤。
    /// </summary>
    private static void CollectActivePrefixes(List<Chara> charas)
    {
        ActivePrefixes.Clear();

        // EClass 的 getter（core / _zone）在读档、切图窗口内会直接抛 NRE，取它必须包住。
        bool snow = false;
        try {
            snow = EClass.core is not null
                && EClass.core.IsGameStarted
                && EClass._zone is not null
                && EClass._zone.IsSnowCovered;
        }
        catch (Exception) {
            snow = false;
        }

        for (int i = 0; i < charas.Count; i++) {
            try {
                string? prefix = PlayState.GetPrefix(charas[i], snow);
                if (!prefix.IsEmpty()) {
                    ActivePrefixes.Add(prefix!);
                }
            }
            catch (Exception) {
            }
        }
    }

    private static List<Need> Collect(List<Chara> charas)
    {
        CollectActivePrefixes(charas);

        var result = new List<Need>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dict = SpriteReplacer.dictSkins;

        foreach (Chara chara in charas) {
            // 一次皮肤查找拿全：归一装备 id + 角色行 id + 三层实例快照。
            // 以前这里要自己读 c_idSpriteReplacer、自己归一、再分别摸 chara.spriteReplacer
            // 与 sourceCard.replacer、sourceCard.idSprite —— 每条都是对同一张卡片的重复读。
            var skin = SkinIndex.Get(chara);

            // ① 装备的皮肤 id（c_idSpriteReplacer）。
            AddIdNeeds(skin.Id, dict, seen, result);

            // ② 角色行自带的皮肤 id。原版 CardActor.OnRender / RenderRow.GetSprite 就是拿
            //    sourceCard.idSprite 去 replacer.HasSprite() 找图的 —— NPC 的皮肤多写在这。
            //    sourceCard.replacer.data 要等首次渲染 Reload 之后才有值，按 replacer 的
            //    路径收集必然晚一步，只能在这里按 id 收。
            AddIdNeeds(skin.RowId, dict, seen, result);

            // ③ 兜底：已经绑好的 replacer（换装同一帧 c_id 还没落到绑定实例上的情况）。
            AddReplacerNeeds(skin.Bound, seen, result);
            if (!ReferenceEquals(skin.Race, skin.Bound)) {
                AddReplacerNeeds(skin.Race, seen, result);
            }
        }

        return result;
    }

    /// <summary>
    /// 按一个皮肤 id 收集要预热的 png。三条路互补：
    ///   ① <c>dictSkins</c>（各包 <c>Skin\</c> 目录，原版 ListSkins 收的那套）；
    ///   ② Texture 编目（<c>Texture\&lt;id&gt;\</c> 子目录布局）；
    ///   ③ <c>dictModItems[id]</c> 登记的自定义底图路径 —— 底图高于编目上限
    ///      <see cref="TextureSkinCatalog.MaxSpriteHeight"/> 时不进编目（例如 768 高的
    ///      yadianna），只有这条能拿到它和同目录的状态片。
    /// </summary>
    private static void AddIdNeeds(
        string? rawId, Dictionary<string, SpriteReplacer>? dict, HashSet<string> seen, List<Need> result)
    {
        if (rawId.IsEmpty()) {
            return;
        }

        string baseId = SkinIndex.NormalizeId(rawId);
        if (!baseId.IsEmpty() && dict is not null
            && dict.TryGetValue(baseId, out var skin) && skin is not null) {
            AddNeed(skin, seen, result);
        }

        foreach (string png in TextureSkinCatalog.ListSkinPngs(rawId)) {
            AddNeedByPath(png, seen, result);
        }

        if (!baseId.IsEmpty()
            && SpriteReplacer.dictModItems is { } modItems
            && modItems.TryGetValue(baseId, out string basePath)
            && IsModSkinPath(basePath)) {
            foreach (string png in TextureSkinCatalog.ListSkinPngsByBasePath(basePath)) {
                AddNeedByPath(png, seen, result);
            }
        }
    }

    /// <summary>
    /// 预热一个 replacer 会用到的贴图：它自带的底图与 _acs_ 片，以及「按该底图所在
    /// 目录、以 id 枚举」出的全部片 —— 后者覆盖编目因高度上限（MaxSpriteHeight）漏收的皮肤。
    /// 只处理 Package\ / Custom\ 下的自定义高清图，原版种族图不预热。
    /// </summary>
    private static void AddReplacerNeeds(SpriteReplacer? replacer, HashSet<string> seen, List<Need> result)
    {
        SpriteData? baseData = replacer?.data;
        if (baseData is null || !IsModSkinPath(baseData.path)) {
            return;
        }

        AddNeed(replacer!, seen, result);
        foreach (string png in TextureSkinCatalog.ListSkinPngsByBasePath(baseData.path)) {
            AddNeedByPath(png, seen, result);
        }
    }

    /// <summary>clip 名里可能带的装饰起始字符（<c>_acs_greet#41_0-38</c> / <c>_acs_move@400</c>）。</summary>
    private static readonly char[] ClipCutChars = { '#', '@' };

    /// <summary>
    /// 从「后缀键」或「文件名」里取出 clip 名（可能带 <c>.</c> 前缀，如 <c>yongzhuang.greet</c>）；
    /// 没有 <c>_acs_</c> 就是底图（返回空串）。按文件名解析时结尾的 <c>.png</c> 要剥掉。
    /// </summary>
    private static string ClipOf(string text)
    {
        int acs = text.LastIndexOf("_acs_", StringComparison.Ordinal);
        if (acs < 0) {
            return string.Empty;
        }

        string clip = text.Substring(acs + 5);
        int cut = clip.IndexOfAny(ClipCutChars);
        if (cut >= 0) {
            clip = clip.Substring(0, cut);
        }

        return clip.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? clip.Substring(0, clip.Length - 4)
            : clip;
    }


    /// <summary>
    /// 预热白名单（可在配置页「场景预热」组逐项开关）：底图 0、greet 2、move 3、其余 4、idle 1。
    /// <b>底图就是新格式的 idle</b>；<c>_acs_idle</c> 是 ACS 老格式，仅老资源才有。
    /// 带前缀的差分片（<c>yongzhuang.greet</c> / <c>snow.move</c> …）按它后面的<b>状态</b>
    /// 归到同一项；前缀只决定「预热谁」—— 只有在用的前缀（<see cref="ActivePrefixes"/>）才预热。
    /// 没有状态段的差分包（<c>_acs_yongzhuang</c>）是该前缀的旧式 idle，归「预热idle」。
    /// 返回 <c>-1</c> = 这个 clip 不预热，第一次用到时才解码。
    /// </summary>
    private static int ClipOrder(string text)
    {
        string clip = ClipOf(text);
        if (clip.Length == 0) {
            return WarmBase ? 0 : -1;
        }

        AcsSuffixes.SplitClipName(clip, out string prefix, out string state);
        if (prefix.Length > 0 && !ActivePrefixes.Contains(prefix)) {
            return -1;
        }

        switch (state) {
            case "":
            case "idle": return WarmIdle ? 1 : -1;
            case "greet":
            case "greet1":
            case "greet2":
            case "greet3":
            case "greet4": return WarmGreet ? 2 : -1;
            case "move": return WarmMove ? 3 : -1;
            case "atk": return WarmAtk ? 4 : -1;
            case "combat": return WarmCombat ? 4 : -1;
            case "hurt": return WarmHurt ? 4 : -1;
            case "revive": return WarmRevive ? 4 : -1;
            case "eat": return WarmEat ? 4 : -1;
            case "cmove": return WarmCmove ? 4 : -1;
            default: return -1;
        }
    }

    private static bool IsModSkinPath(string? path)
    {
        if (path.IsEmpty()) {
            return false;
        }

        string p = path!;
        return p.IndexOf("\\Package\\", StringComparison.OrdinalIgnoreCase) >= 0
            || p.IndexOf("\\Custom\\", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void AddNeed(SpriteReplacer skin, HashSet<string> seen, List<Need> result)
    {
        SpriteData? baseData = skin.data;
        if (baseData is null && skin.suffixes is not null) {
            skin.suffixes.TryGetValue(string.Empty, out baseData);
        }

        if (WarmBase) {
            AddDataNeed(baseData, seen, result, 0);
        }

        if (skin.suffixes is null) {
            return;
        }

        foreach (var kv in skin.suffixes) {
            if (kv.Key is null || kv.Value is null) {
                continue;
            }

            // 只预热配置页勾选的那几类（默认：底图 / greet / move）；其余第一次用到时才解码。
            int order = ClipOrder(kv.Key);
            if (order >= 0) {
                AddDataNeed(kv.Value, seen, result, order);
            }
        }
    }

    private static void AddDataNeed(SpriteData? data, HashSet<string> seen, List<Need> result, int order)
    {
        if (data is null || data.path.IsEmpty()) {
            return;
        }

        string png = data.path + ".png";
        if (Handled.Contains(png) || !seen.Add(png)) {
            return;
        }

        int frame = data.frame;
        if (frame <= 0) {
            frame = SkinListPatch.ReadIniInt(data.path + ".ini", "frame");
            if (frame <= 0) {
                frame = 1;
            }
        }

        int ppu = PngTiler.ResolvePpu(data);
        if (TiledSprite.Fresh(png, frame, ppu)) {
            return;
        }

        result.Add(new Need(png, frame, ppu, order));
    }

    private static void AddNeedByPath(string png, HashSet<string> seen, List<Need> result)
    {
        if (png.IsEmpty() || Handled.Contains(png) || !seen.Add(png)) {
            return;
        }

        // 只预热配置页勾选的那几类（默认：底图 / greet / move）。
        int order = ClipOrder(System.IO.Path.GetFileName(png));
        if (order < 0) {
            return;
        }

        string path = png.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? png.Substring(0, png.Length - 4)
            : png;

        var probe = new SpriteData { path = path };
        var clip = SkinListPatch.ResolveClipFromPath(probe);

        int frame;
        if (clip is { Length: > 0 }) {
            frame = clip.Length;
        }
        else {
            frame = SkinListPatch.ReadIniInt(path + ".ini", "frame");
            if (frame <= 0) {
                frame = 1;
            }
        }

        int ppu = PngTiler.ResolvePpu(probe);
        if (TiledSprite.Fresh(png, frame, ppu)) {
            return;
        }

        result.Add(new Need(png, frame, ppu, order));
    }

    /// <summary>
    /// 派发一个解码任务。<b>预热就是提前调用取图接口</b>：线程体只有一句
    /// <see cref="TiledSprite.Prewarm"/>（解码 → 存纯像素 → 等主线程建贴图），
    /// 解码耗时/失败由那边记进探针，这里不再转手任何产物。
    /// </summary>
    private static void StartJob(Job job)
    {
        ThreadPool.UnsafeQueueUserWorkItem(state => {
            try {
                TiledSprite.Prewarm(job.Png, job.Frames, job.Ppu);
            }
            catch (Exception) {
                // Prewarm 内部已吞并记账；异常也不能让线程池回调炸出去。
            }
            finally {
                job.IsDone = true;
            }
        }, null);
    }
}