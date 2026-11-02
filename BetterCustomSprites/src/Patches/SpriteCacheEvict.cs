using System;
using System.Collections.Generic;
using UnityEngine;
using ACS.API;
using ACS.Skin;
using ACS.Tiling;

namespace ACS.Patches;

/// <summary>
/// 按需释放皮肤贴图：只保留「当前地图在用的」+「宽限期内刚渲染/刚取过图标的」，
/// 其余把贴图交还，让会话里的显存占用从「单调累积」变成「跟随当前地图」。
///
/// 背景：原版 <c>SpriteData.GetSprites()</c> 是懒加载（渲染到才 <c>Load()</c>），
/// 但<b>没有任何卸载路径</b> —— <c>SpriteReplacer.dictSkins</c> 是静态表，
/// <c>SpriteData.tex/sprites</c> 只写不清，所以一张图载进来就再也不会还。
/// 长局 + 多地图会一路涨到底。
///
/// <b>为什么安全</b>：这里只断托管引用，从不 <c>Destroy</c> 任何 Unity 对象。
/// 正在屏上的 <c>Sprite</c> 自己持有贴图（<c>SpriteRenderer.sprite</c> / <c>mpb._MainTex</c>），
/// 所以释放既不会出现 null sprite / 花屏，也不会 NRE；最坏只是下次用到时重新解码的一次卡顿。
/// 工程内对 <c>data.sprites</c> 的直接索引都带 null 保护，其余一律走 <c>GetSprites()</c>（内部按需重载）。
///
/// <b>释放点有三处</b>（少一处就收不回显存）：
///   ① <c>dictSkins[key].data</c>；
///   ② 同一个 <c>SpriteReplacer</c> 的 <c>suffixes[*]</c> —— <c>_acs_*</c> 状态片住在这里，
///      而高清化的成本几乎全在它们身上；
///   ③ <c>TiledSprite</c> 的分片缓存 —— 超限文件在那边另有一份 sprites/tex 强引用。
///
/// <b>pin 规则</b>（任一命中即保留）：
///   A. 当前地图（<c>Map.charas</c> / <c>deadCharas</c> / <c>EClass.pc</c>）上角色装备的皮肤 id
///      （<c>c_idSpriteReplacer</c>）与其角色行自带的 <c>sourceCard.idSprite</c>
///      —— 覆盖「在图上但出屏」的角色，以及皮肤写在角色行上的 NPC；
///   B. 宽限期内被 <see cref="Touch"/> 过的 <c>SpriteData</c> —— 覆盖正在渲染的角色与换肤列表图标。
///
/// 不在这里管的：立绘（<c>PortraitAnimator</c> / <c>FullPortraitPatch</c> 走 Portrait 目录）、
/// 道具动画（<c>SpriteReplacerAnimation.dict</c> 是另一张静态表）。
/// </summary>
internal static class SpriteCacheEvict
{
    /// <summary>总开关（<c>ACS.SpriteCache off</c> 可关）。</summary>
    internal static bool Enabled = true;

    internal const float DefaultGraceSeconds = 30f;
    internal const float DefaultIntervalSeconds = 3f;

    /// <summary>默认显存预算（MB）；<c>0</c> = 不限。</summary>
    internal const int DefaultMemoryBudgetMb = 2048;

    /// <summary>停止使用后仍保留贴图的秒数。太小 → 出屏/切图就重解码（卡顿）；太大 → 回收不及时。</summary>
    internal static float GraceSeconds = DefaultGraceSeconds;

    /// <summary>扫描间隔（秒）。</summary>
    internal static float IntervalSeconds = DefaultIntervalSeconds;

    /// <summary>
    /// 贴图显存预算（MB）；<c>0</c> = 不限。宽限期回收之后仍超预算时，
    /// 按「最久没用到」顺序释放一半。
    /// <b>为什么按字节而不是按张数</b>：同样一张，128×128 与 24576×512 差 700 倍，
    /// 按张数根本约不住内存。
    /// </summary>
    internal static int MemoryBudgetMb = DefaultMemoryBudgetMb;

    /// <summary>当前地图在用的 base id 集合（每次扫描重建）。</summary>
    private static readonly HashSet<string> Pinned = new(StringComparer.Ordinal);

    /// <summary>
    /// 宽限期内被取用过的皮肤，key 是<b>归一后的 base id</b>（不是 SpriteData 引用）。
    ///
    /// 为什么按 id 而不是按引用：渲染路径拿到的 SpriteData 可能是
    /// <c>chara.spriteReplacer</c> 上的绑定实例，与 <c>dictSkins</c> 里那个<b>不是同一个对象</b>
    /// （<c>SkinIndex.ResolveSuffixes</c> 在 base id 没命中 dictSkins 时会走 <c>owner.spriteReplacer</c> 兜底）。
    /// 按引用记的话，Sweep 查的是 dictSkins 那份，永远查不到渲染记的那份 →
    /// 正在播的动画被判成冷数据 → 每轮释放 → 下一步重新解码（实测 move 片重载 11 次 ×150ms）。
    /// 归一后两者都是同一个 base id，才能对上。
    /// </summary>
    private static readonly Dictionary<string, float> Touched = new(StringComparer.Ordinal);

    private static readonly List<string> PruneScratch = new();

    /// <summary>显存预算排序用的临时表（避免每次扫描分配）。</summary>
    private sealed class BudgetEntry
    {
        public SpriteReplacer Skin = null!;
        public long Bytes;
        public float LastUsed;
    }

    private static readonly List<BudgetEntry> BudgetScratch = new();

    private static float _nextSweep;
    private static float _lastSweepAt = -1f;

    private static int _evictedSkins;
    private static int _evictedTiles;
    private static int _lastEvictedSkins;
    private static int _lastEvictedTiles;

    /// <summary>pin 采集抛异常只报一次，避免刷屏。</summary>
    private static bool _pinWarned;

    /// <summary>记录一次「这个 data 正在被用」——渲染路径与换肤列表图标路径都会调。</summary>
    internal static void Touch(SpriteData? data)
    {
        if (!Enabled || data is null) {
            return;
        }

        string path = data.path;
        if (path.IsEmpty()) {
            return;
        }

        // 归一到 base id：`xxx_acs_move#41_0-24` 与 `xxx.png` 都归到 `xxx`，
        // 这样底图 / 状态片 / 兜底绑定实例共用同一条续期记录。
        string id = SkinIndex.NormalizeId(path);
        if (id.IsEmpty()) {
            return;
        }

        Touched[id] = Time.realtimeSinceStartup;
    }

    /// <summary>每帧调用（<c>AcsMod.Update</c>），内部按 <see cref="IntervalSeconds"/> 限流。</summary>
    internal static void Tick()
    {
        if (!Enabled) {
            return;
        }

        float now = Time.realtimeSinceStartup;
        if (now < _nextSweep) {
            return;
        }

        _nextSweep = now + Mathf.Max(0.5f, IntervalSeconds);
        Sweep(now);
    }

    /// <summary>紧急救援钩子：换图 / 重载时调用，重置节奏（不释放，只让下一次扫描尽快发生）。</summary>
    internal static void Invalidate()
    {
        _nextSweep = 0f;
    }

    private static void Sweep(float now)
    {
        try
        {
            // ---- 安全阀 ----
            // 游戏没开始 / 地图还没建好 / 这张图上一个人都没有时，一律不释放。
            // 读档与换图途中 charas 会是空的，此时释放等于把马上要用的贴图全丢掉。
            if (EClass.core is null || !EClass.core.IsGameStarted)
            {
                return;
            }

            var map = EClass._map;
            if (map?.charas is not { Count: > 0 })
            {
                return;
            }

            var dict = SpriteReplacer.dictSkins;
            if (dict is null || dict.Count == 0)
            {
                return;
            }

            Pinned.Clear();
            CollectPinned(EClass.pc);
            CollectPinned(map.deadCharas);
            CollectPinned(map.charas);

            // 一个角色取不到 id 就把整批 pin 收集打断 → pinned 只剩零星几个，
            // 正在画面上用的贴图会被误判成冷数据释放掉（下一帧重新解码，表现为卡顿）。
            // 所以必须确认本轮真的覆盖到了人，否则整轮放弃。
            if (Pinned.Count == 0 && map.charas.Count > 0)
            {
                _pinWarned = true;
                AcsMod.Warn(
                    "[BCS] sprite cache: pinned 0 with " + map.charas.Count
                    + " chara(s) on map — 本轮跳过释放，避免误收正在用的贴图");
                return;
            }

            _lastSweepAt = now;
            _evictedSkins = 0;
            _evictedTiles = 0;

            foreach (var kv in dict)
            {
                string? key = kv.Key;
                var skin = kv.Value;
                if (key is null || key.Length == 0 || skin is null)
                {
                    continue;
                }

                if (IsAlive(key, skin, now))
                {
                    continue;
                }

                Release(skin.data);

                var subs = skin.suffixes;
                if (subs is not null && subs.Count > 0)
                {
                    foreach (var sub in subs.Values)
                    {
                        Release(sub);
                    }
                }
            }

            EnforceBudget(now);

            if (_evictedSkins > 0 || _evictedTiles > 0)
            {
                _lastEvictedSkins = _evictedSkins;
                _lastEvictedTiles = _evictedTiles;
                AcsMod.Log(
                    "[BCS] sprite cache: released " + _evictedSkins + " skin data"
                    + (_evictedTiles > 0 ? " + " + _evictedTiles + " tiled sheets" : "")
                    + "; pinned " + Pinned.Count + " on map; grace " + GraceSeconds.ToString("0") + "s");
            }

            PruneTouched(now);
        }
        catch (Exception ex)
        {
            AcsMod.Warn("[BCS] sprite cache sweep failed: " + ex.Message);
        }
    }

    /// <summary>返回 true = 保留（地图 pin、或宽限期内被动过）。</summary>
    private static bool IsAlive(string key, SpriteReplacer skin, float now)
    {
        string baseId = SkinIndex.NormalizeId(key);
        if (baseId.IsEmpty())
        {
            baseId = key;
        }

        // 刚被取用过（渲染 / 列表图标）：Touch 记录的 key 也是归一 base id，可直接比。
        if (TouchedRecently(baseId, now) || TouchedRecently(key, now))
        {
            return true;
        }

        // 地图 pin：先按归一键命中；归一失败再按原 key 试一次。
        if (Pinned.Contains(baseId) || Pinned.Contains(key))
        {
            return true;
        }

        // acs 片与 _bcs_combat 有自己的 SpriteData：它们归一后通常就是 baseId，
        // 但路径形态特殊时可能落不上去，这里逐片再兜一次（只有上面没命中才走到）。
        var subs = skin.suffixes;
        if (subs is not null && subs.Count > 0)
        {
            foreach (var sub in subs.Values)
            {
                string? subPath = sub?.path;
                if (subPath.IsEmpty())
                {
                    continue;
                }

                if (TouchedRecently(SkinIndex.NormalizeId(subPath!), now))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TouchedRecently(string id, float now)
        => !id.IsEmpty()
           && Touched.TryGetValue(id, out float t)
           && now - t < GraceSeconds;

    /// <summary>
    /// 把一个角色（或一批）装备的皮肤 id 加进 pin 集合。
    /// 皮肤查找只做一次：<see cref="SkinIndex.Get"/> 把归一后的装备 id、角色行 id、
    /// 以及三层实例（编目 / 绑定 / 种族）一起给出来，这里只消费结果、不再重读卡片。
    /// 主依据是 <c>c_idSpriteReplacer</c>；已绑定但 id 还没写上去时用绑定实例的路径兜底。
    /// </summary>
    private static void CollectPinned(Chara? chara)
    {
        if (chara is null)
        {
            return;
        }

        try
        {
            var skin = SkinIndex.Get(chara);

            if (!skin.Id.IsEmpty())
            {
                Pinned.Add(skin.Id);
            }

            // 皮肤写在角色行上时（NPC 常见）：渲染走的是 sourceCard.replacer，
            // 而它的 data 要等首次渲染 Reload 之后才有 path —— 必须认 sourceCard.idSprite
            // （原版 CardActor.OnRender / RenderRow.GetSprite 用的就是这个 id）。
            // 少这一条，这类皮肤永远不在 Pinned 里，预热刚解好的分片会被下一轮 sweep
            // 当冷数据释放、然后重新解码（实测 greet 601ms、报告里标成 x2）。
            AddPath(skin.Race?.data?.path);
            if (!skin.RowId.IsEmpty())
            {
                Pinned.Add(skin.RowId);
            }

            // 兜底：换肤同一帧里 c_id 可能还没落到 Charas 的绑定实例上。
            if (skin.Bound is not null && !ReferenceEquals(skin.Bound, skin.Race))
            {
                AddReplacerPaths(skin.Bound);
            }

            // 编目实例与绑定实例通常不是同一个对象：两边的路径都要认。
            if (skin.Equipped is not null
                && !ReferenceEquals(skin.Equipped, skin.Bound)
                && !ReferenceEquals(skin.Equipped, skin.Race))
            {
                AddReplacerPaths(skin.Equipped);
            }
        }
        catch (Exception ex)
        {
            // 单个角色取不到不影响其余角色的 pin。
            // 但静默吞会让「pinned 0」查不出原因，首次发生时记一条。
            if (!_pinWarned)
            {
                _pinWarned = true;
                AcsMod.Log("[BCS] sprite cache: pin lookup threw on one chara: " + ex.Message);
            }
        }
    }

    private static void CollectPinned(List<Chara>? list)
    {
        if (list is null)
        {
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            // CollectPinned(Chara) 内部已 catch 单角色异常。
            CollectPinned(list[i]);
        }
    }

    /// <summary>把一套 replacer 的底图 + 全部状态片的路径归一后收进 pin 集合。</summary>
    private static void AddReplacerPaths(SpriteReplacer replacer)
    {
        AddPath(replacer.data?.path);
        var subs = replacer.suffixes;
        if (subs is null)
        {
            return;
        }

        foreach (var sub in subs.Values)
        {
            AddPath(sub?.path);
        }
    }

    private static void AddPath(string? path)
    {
        if (path.IsEmpty())
        {
            return;
        }

        string id = SkinIndex.NormalizeId(path!);
        if (!id.IsEmpty())
        {
            Pinned.Add(id);
        }
    }

    /// <summary>
    /// 断掉一个 SpriteData 的贴图引用 + 对应分片缓存。
    /// 只清 <c>tex</c>/<c>sprites</c>：<c>frame</c>/<c>time</c>/<c>scale</c> 来自 ini，
    /// 保留它们才能让重载后立刻按正确的帧数切回来。
    /// </summary>
    private static void Release(SpriteData? data)
    {
        if (data is null)
        {
            return;
        }

        bool held = data.tex is not null || data.sprites is not null;
        data.sprites = null;
        data.tex = null;

        if (held)
        {
            _evictedSkins++;
        }

        string path = data.path;
        if (!path.IsEmpty() && TiledSprite.Release(path))
        {
            _evictedTiles++;
        }

        // 探针：把「释放了谁」记下来。与 BcsPerf 的加载次数对照 ——
        // 同一张图「释放 N 次 + 加载 N+1 次」即坐实是反复重载，而不是首帧解码贵。
        if (held)
        {
            BcsPerf.NoteEvict(data.path);
        }
    }

    /// <summary>
    /// 显存预算兜底：宽限期回收之后仍然超预算时，按「最久没用到」顺序释放一半。
    /// <b>候选里不放 pin / 宽限期内的条目</b>：预算再紧也不动正在用的贴图。
    /// 少了这条约束，超预算时被挑中的恰好是「在图上、但最近没渲染过」的角色
    /// （它们的 LastUsed 是 0、排最前），一放一载反复解码 —— 表现就是「进视野就卡」。
    /// </summary>
    private static void EnforceBudget(float now)
    {
        long budget = MemoryBudgetMb <= 0 ? 0L : (long)MemoryBudgetMb * 1024 * 1024;
        if (budget <= 0)
        {
            return;
        }

        var dict = SpriteReplacer.dictSkins;
        if (dict is null || dict.Count == 0)
        {
            return;
        }

        BudgetScratch.Clear();
        long total = 0;
        foreach (var kv in dict)
        {
            string? key = kv.Key;
            var skin = kv.Value;
            if (key is null || key.Length == 0 || skin is null)
            {
                continue;
            }

            long bytes = BytesHeldBy(skin);
            if (bytes <= 0)
            {
                continue;
            }

            total += bytes;

            // 正在用的一律不进候选：当前地图 pin 的，以及宽限期内刚渲染/刚取过图标的。
            if (IsAlive(key, skin, now))
            {
                continue;
            }

            string id = SkinIndex.NormalizeId(key);
            BudgetScratch.Add(new BudgetEntry
            {
                Skin = skin,
                Bytes = bytes,
                LastUsed = !id.IsEmpty() && Touched.TryGetValue(id, out float touchedAt) ? touchedAt : 0f,
            });
        }

        if (total <= budget || BudgetScratch.Count == 0)
        {
            return;
        }

        BudgetScratch.Sort((a, b) => a.LastUsed.CompareTo(b.LastUsed));

        long before = total;
        int half = Math.Max(1, BudgetScratch.Count / 2);
        long freed = 0;
        int released = 0;
        for (int i = 0; i < half && total > budget; i++)
        {
            BudgetEntry e = BudgetScratch[i];
            Release(e.Skin.data);

            var subs = e.Skin.suffixes;
            if (subs is not null && subs.Count > 0)
            {
                foreach (var sub in subs.Values)
                {
                    Release(sub);
                }
            }

            freed += e.Bytes;
            total -= e.Bytes;
            released++;
        }

        AcsMod.Log(
            "[BCS] sprite cache: 显存 " + (before / 1048576) + "MB 超预算 " + (budget / 1048576)
            + "MB → 释放最久未用的 " + released + " 套（-" + (freed / 1048576)
            + "MB，剩 " + (total / 1048576) + "MB）");
    }

    /// <summary>一个皮肤条目（底图 + 全部状态片）当前持有的贴图字节数。</summary>
    private static long BytesHeldBy(SpriteReplacer skin)
    {
        long sum = BytesOfData(skin.data);
        var subs = skin.suffixes;
        if (subs is not null && subs.Count > 0)
        {
            foreach (var sub in subs.Values)
            {
                sum += BytesOfData(sub);
            }
        }

        return sum;
    }

    /// <summary>
    /// 单个 SpriteData 持有的字节数。走过分片的图由 <see cref="TiledSprite"/> 记账
    /// （一次可能多片，只认第一片会少算），普通图直接按 <c>tex</c> 尺寸算。
    /// </summary>
    private static long BytesOfData(SpriteData? data)
    {
        if (data is null)
        {
            return 0;
        }

        string path = data.path;
        if (path.IsEmpty())
        {
            return 0;
        }

        long tiled = TiledSprite.BytesHeld(path);
        if (tiled > 0)
        {
            return tiled;
        }

        var tex = data.tex;
        return tex is null ? 0L : (long)tex.width * tex.height * 4;
    }

    /// <summary>清掉过期条目，防止 dictSkins 重建后旧 SpriteData 在 Touched 里越积越多。</summary>
    private static void PruneTouched(float now)
    {
        if (Touched.Count < 256)
        {
            return;
        }

        PruneScratch.Clear();
        foreach (var kv in Touched)
        {
            if (now - kv.Value >= GraceSeconds)
            {
                PruneScratch.Add(kv.Key);
            }
        }

        for (int i = 0; i < PruneScratch.Count; i++)
        {
            Touched.Remove(PruneScratch[i]);
        }
    }

    // ---------------- 命令行支持 ----------------

    internal static string Status()
    {
        var dict = SpriteReplacer.dictSkins;
        int total = 0;
        int loaded = 0;
        if (dict is not null)
        {
            foreach (var kv in dict)
            {
                var d = kv.Value?.data;
                if (d is null)
                {
                    continue;
                }

                total++;
                if (d.tex is not null || d.sprites is not null)
                {
                    loaded++;
                }
            }
        }

        string swept = _lastSweepAt < 0f
            ? "not yet"
            : (Time.realtimeSinceStartup - _lastSweepAt).ToString("0") + "s ago";

        return "sprite cache evict = " + (Enabled ? "ON" : "OFF")
               + "; grace " + GraceSeconds.ToString("0") + "s"
               + "; interval " + IntervalSeconds.ToString("0.#") + "s"
               + "; pinned " + Pinned.Count + " on map"
               + "; base skins loaded " + loaded + "/" + total
               + "; budget " + (MemoryBudgetMb <= 0 ? "off" : MemoryBudgetMb + "MB")
               + "; tiled held " + (TiledSprite.TotalBytes / 1048576) + "MB"
               + "; touched " + Touched.Count
               + "; last sweep " + swept
               + " (released " + _lastEvictedSkins + " + " + _lastEvictedTiles + ")";
    }

    internal static string Reset()
    {
        GraceSeconds = DefaultGraceSeconds;
        IntervalSeconds = DefaultIntervalSeconds;
        return "reset: grace " + GraceSeconds.ToString("0") + "s / interval " + IntervalSeconds.ToString("0.#") + "s.";
    }

    /// <summary>立刻扫一次（跳过宽限期，只保地图 pin）。</summary>
    internal static string Flush()
    {
        Sweep(Time.realtimeSinceStartup);
        _nextSweep = Time.realtimeSinceStartup + Mathf.Max(0.5f, IntervalSeconds);
        return "flushed: released " + _evictedSkins + " skin data + " + _evictedTiles + " tiled sheets"
               + "; pinned " + Pinned.Count + " on map.";
    }

    /// <summary>
    /// 强制一次 GC + <c>UnloadUnusedAssets</c>（诊断 / 手动归还显存）。
    ///
    /// <see cref="Release"/> 只断托管引用：跑图用的贴图都是运行时 <c>new Texture2D</c> 出来的，
    /// 托管壳被 GC 掉时原生纹理才真正释放。抓不到那一刻就用这个命令强制一次。
    /// </summary>
    internal static string Collect()
    {
        try
        {
            GC.Collect();
            Resources.UnloadUnusedAssets();
            return "requested GC + UnloadUnusedAssets (last sweep released "
                   + _lastEvictedSkins + " skin data + " + _lastEvictedTiles + " tiled sheets).";
        }
        catch (Exception ex)
        {
            return "collect failed: " + ex.Message;
        }
    }
}
