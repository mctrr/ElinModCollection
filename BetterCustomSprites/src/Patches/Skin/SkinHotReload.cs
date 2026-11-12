using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ACS.API;
using ACS.Tiling;
using UnityEngine;

namespace ACS.Patches;

/// <summary>
/// 皮肤热加载：监视贴图目录，检测到玩家在游戏外（Aseprite / 资源管理器 / 脚本）改了
/// PNG、<c>.ini</c> 或 <c>.pref</c>，就把对应的 <c>SpriteData</c> 缓存丢掉，
/// <b>下一帧渲染自动重新读盘解码</b>。
///
/// <b>为什么原版不管这件事</b>：23.359.0 的 <c>Game.OnApplicationFocus</c> 切回游戏时确实会刷一遍
/// 贴图，但只刷三套 —— <c>TextureData</c>（拖放替换）、<c>RenderRow.replacer</c>（原版卡图覆盖）、
/// <c>dictOverrides</c>（<c>sprite_override</c>）。对 <c>SpriteReplacer.dictSkins</c> 只调了
/// <c>data?.LoadPrefAndFixDensity()</c>，<b>没有 <c>Validate()</c></b>，所以自定义皮肤
/// （动态底图 + <c>_acs_*</c> 状态片）改图后不会自己刷新 —— 这里就是唯一通道。
///
/// <b>为什么只清不重载</b>：清完 <c>sprites/tex</c> 之后，原版 <c>SpriteData.GetSprites()</c>
/// 的懒加载会把整条链路自己接回去（读盘 → decode → 上传 → 切帧）。
/// 也就是说「什么时候重载」由角色下一次出现在画面里决定 —— 不在画面上的皮肤不浪费解码。
///
/// <b>怎么做到不卡</b>：<see cref="FileSystemWatcher"/> 的回调只做一件事 —— 把
/// 「路径 + 改了哪类文件」塞进一个加锁的字典（后台线程，绝不碰 Unity API）。
/// 真正的清理由 <see cref="Tick"/> 在主线程做，并且要等<b>最后一次改动之后
/// <see cref="DebounceMilliseconds"/> 毫秒</b>才动手：导出工具写文件往往是「先 truncate 再分段写」，
/// 不防抖就会读到半张图。
///
/// <b>边界</b>：只处理「改已有文件」。新增 / 删除 / 改名会让贴图路径与 <c>dictSkins</c> 对不上，
/// 那种情况会打日志提示敲 <c>ACS.ReloadAcs</c>，不在这里自动重建整张表。
/// </summary>
internal static class SkinHotReload
{
    /// <summary>一次改动动到的文件类型。三类文件的失效代价不同，不能一律重解码。</summary>
    [Flags]
    private enum AssetKind
    {
        None = 0,

        /// <summary>贴图像素：作废 <c>tex</c>/<c>sprites</c>，必须重新读盘解码。</summary>
        Tex = 1,

        /// <summary><c>.ini</c>：帧数 / 帧间隔 / scale。只有帧数或 scale 变了才影响切分。</summary>
        Ini = 2,

        /// <summary><c>.pref</c>：密度（<c>scaleTex</c> → PPU）。变了才要重建 sprites。</summary>
        Pref = 4,
    }

    /// <summary>总开关，默认关（由 <c>热加载</c> 设置项 / <c>ACS.HotReload</c> 命令驱动）。</summary>
    internal static bool Enabled;

    /// <summary>最后一次收到文件事件的时刻（<see cref="NowMs"/> 毫秒）。</summary>
    private const long DebounceMilliseconds = 1500L;

    private const string Usage = " Usage: ACS.HotReload on|off";

    private static readonly List<FileSystemWatcher> Watchers = new();

    /// <summary>待处理：<c>SpriteData.path</c>（不带扩展名）→ 这次改动了哪几类文件。</summary>
    private static readonly Dictionary<string, AssetKind> Dirty = new(StringComparer.OrdinalIgnoreCase);

    private static readonly object Gate = new();

    private static long _lastChangeMs;
    private static int _reloadCount;

    /// <summary>
    /// 单调毫秒计时。不能用 <c>Environment.TickCount64</c>（net472 没有），
    /// 也不用 <c>Environment.TickCount</c>（int 每 24.9 天溢出）。
    /// <see cref="Stopwatch.GetTimestamp"/> 既单调又线程安全。
    /// </summary>
    private static long NowMs() => Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency;

    // ---------------- 启停 ----------------

    /// <summary>打开监视（幂等）。目录来源与 <see cref="SpriteReplacer.ListSkins"/> 保持一致：Custom 与各非 builtin 包的 <c>Texture</c> + <c>Skin</c>。</summary>
    internal static void Start()
    {
        lock (Gate) {
            if (Watchers.Count > 0) {
                return;
            }
        }

        var roots = new List<string>();
        AddRoots(roots, "Texture");
        AddRoots(roots, "Skin");

        int ok = 0;
        foreach (string root in roots)
        {
            try {
                var w = new FileSystemWatcher(root) {
                    // 一个 watcher 管 png / ini / pref：扩展名在 Mark 里过滤。
                    // 传 "*.png" 会把改 ini/pref 的事件也一起漏掉。
                    Filter = "*.*",
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite
                                   | NotifyFilters.FileName
                                   | NotifyFilters.Size
                                   | NotifyFilters.CreationTime,

                    // 默认 8KB 缓冲在批量导出时会丢事件，放大到 64KB。
                    InternalBufferSize = 64 * 1024,
                };
                w.Changed += OnFileEvent;
                w.Created += OnFileEvent;
                w.Deleted += OnFileEvent;
                w.Renamed += OnRenamed;
                w.EnableRaisingEvents = true;

                lock (Gate) {
                    Watchers.Add(w);
                }

                ok++;
            }
            catch (Exception ex) {
                AcsMod.Warn("[BCS] 热加载: 监视失败 " + root + " — " + ex.Message);
            }
        }

        AcsMod.Log("[BCS] 热加载：已监视 " + ok + "/" + roots.Count + " 个素材目录"
            + (ok > 0 ? "（在游戏外改 PNG / ini / pref 会自动重载）" : string.Empty));
    }

    /// <summary>
    /// 收一处素材根目录：<c>Custom/&lt;sub&gt;</c> + 各非 builtin 包的 <c>&lt;sub&gt;</c>。
    /// <b>Texture 与 Skin 都要收</b>：动态底图常放 <c>Texture/</c>，而原版 <c>ListSkins</c> 的皮肤主来源是
    /// <c>Skin/</c>（<c>PCR/Skin</c>、<c>zwfz2/Skin</c> 里全是 <c>_acs_*</c> 状态片）——
    /// 只监视 Texture 会让状态片改动永远刷不出来。
    /// </summary>
    private static void AddRoots(List<string> roots, string sub)
    {
        try {
            string custom = CorePath.custom + sub;
            if (Directory.Exists(custom)) {
                roots.Add(custom);
            }
        }
        catch (Exception ex) {
            AcsMod.Warn("[BCS] 热加载: Custom/" + sub + " 不可读 — " + ex.Message);
        }

        try {
            // 与 ListSkins 一致用非缓存枚举：新装的包不必重启就能被监视到。
            foreach (var (dir, _) in PackageIterator.GetDirectoriesEx(sub, useCache: false)) {
                if (dir is not null && dir.Exists) {
                    roots.Add(dir.FullName);
                }
            }
        }
        catch (Exception ex) {
            AcsMod.Warn("[BCS] 热加载: 枚举 Package/" + sub + " 失败 — " + ex.Message);
        }
    }

    /// <summary>停止监视并清空待办（幂等）。</summary>
    internal static void Stop()
    {
        List<FileSystemWatcher> copy;
        lock (Gate) {
            copy = new List<FileSystemWatcher>(Watchers);
            Watchers.Clear();
            Dirty.Clear();
        }

        foreach (var w in copy)
        {
            try {
                w.EnableRaisingEvents = false;
                w.Changed -= OnFileEvent;
                w.Created -= OnFileEvent;
                w.Deleted -= OnFileEvent;
                w.Renamed -= OnRenamed;
                w.Dispose();
            }
            catch (Exception) {
                // 关不掉就算了，它只是不再被读取。
            }
        }
    }

    /// <summary>按当前 <see cref="Enabled"/> 启停（<c>BcsConfig.Apply</c> 调，幂等）。</summary>
    internal static void Sync()
    {
        if (Enabled) {
            Start();
        }
        else {
            Stop();
        }
    }

    // ---------------- 事件（后台线程） ----------------

    private static void OnFileEvent(object sender, FileSystemEventArgs e) => Mark(e.FullPath);

    private static void OnRenamed(object sender, RenamedEventArgs e) => Mark(e.FullPath);

    /// <summary>只认 png / ini / pref；其它文件（.aseprite 工程、缩略图缓存…）直接忽略。</summary>
    private static AssetKind KindOf(string path)
    {
        string ext = Path.GetExtension(path);
        if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase)) {
            return AssetKind.Tex;
        }

        if (ext.Equals(".ini", StringComparison.OrdinalIgnoreCase)) {
            return AssetKind.Ini;
        }

        if (ext.Equals(".pref", StringComparison.OrdinalIgnoreCase)) {
            return AssetKind.Pref;
        }

        return AssetKind.None;
    }

    private static void Mark(string? fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) {
            return;
        }

        AssetKind kind = KindOf(fullPath!);
        if (kind == AssetKind.None) {
            return;
        }

        // SpriteData.path 不带扩展名，事件带扩展名 —— 统一去掉再比。
        // 同一张图先改 png 再改 ini 会合并成同一条待办（位或）。
        string key = fullPath!.Substring(0, fullPath.Length - Path.GetExtension(fullPath).Length);

        lock (Gate) {
            Dirty.TryGetValue(key, out AssetKind prev);
            Dirty[key] = prev | kind;

            // 跨线程只碰这个纯数值，不碰 Unity API。
            _lastChangeMs = NowMs();
        }
    }

    // ---------------- 主线程（Tick） ----------------

    internal static void Tick()
    {
        if (!Enabled) {
            return;
        }

        KeyValuePair<string, AssetKind>[] pending;
        lock (Gate) {
            if (Dirty.Count == 0) {
                return;
            }

            if (NowMs() - _lastChangeMs < DebounceMilliseconds) {
                return;
            }

            pending = new KeyValuePair<string, AssetKind>[Dirty.Count];
            int i = 0;
            foreach (var kv in Dirty) {
                pending[i++] = kv;
            }

            Dirty.Clear();
        }

        Apply(pending);
    }

    /// <summary>一次 Tick 的统计，只用来打日志。</summary>
    private sealed class Stats
    {
        internal int Tex;
        internal int Ini;
        internal int Pref;
        internal int Tiles;
        internal int Reloaded;

        /// <summary>真正命中的路径（用来算「有多少个改动不在皮肤表里」）。</summary>
        internal readonly HashSet<string> Hit = new(StringComparer.OrdinalIgnoreCase);
    }

    private static void Apply(KeyValuePair<string, AssetKind>[] pending)
    {
        try {
            var dict = SpriteReplacer.dictSkins;
            if (dict is null || dict.Count == 0) {
                AcsMod.Log("[BCS] 热加载：检测到 " + pending.Length + " 个文件变更，但皮肤表还没建好，忽略。");
                return;
            }

            var wanted = new Dictionary<string, AssetKind>(pending.Length, StringComparer.OrdinalIgnoreCase);
            var stats = new Stats();
            foreach (var kv in pending)
            {
                wanted.TryGetValue(kv.Key, out AssetKind prev);
                wanted[kv.Key] = prev | kv.Value;

                if ((kv.Value & AssetKind.Tex) != 0) {
                    stats.Tex++;
                }

                if ((kv.Value & AssetKind.Ini) != 0) {
                    stats.Ini++;
                }

                if ((kv.Value & AssetKind.Pref) != 0) {
                    stats.Pref++;
                }
            }

            // 刚把贴图作废，跨重建复用必须让路，否则会把作废的旧贴图搬回来。
            SkinTextureReuse.Suppress();

            // 之前加载失败过的（半张图 / 尺寸超限）现在文件改好了，允许重试。
            SpriteDataLoadSpritesPatch.ClearFailureCaches();
            SkinEditUiPatch.ClearThumbs();

            int hit = 0;

            foreach (var kv in dict)
            {
                var skin = kv.Value;
                if (skin is null) {
                    continue;
                }

                if (Clear(skin.data, wanted, stats)) {
                    hit++;
                }

                var subs = skin.suffixes;
                if (subs is null || subs.Count == 0) {
                    continue;
                }

                foreach (var sub in subs.Values) {
                    if (Clear(sub, wanted, stats)) {
                        hit++;
                    }
                }
            }

            // 23.359.0：贴图作废后令在场 actor 重新问 GetSpriteData，
            // 否则 sd == spriteData 时本体不会触发 ReloadSprite，单帧皮肤一直显示旧图。
            if (stats.Reloaded > 0) {
                SpriteDataInvalidation.InvalidateOnScreenActors();
            }

            _reloadCount++;
            AcsMod.Log("[BCS] 热加载：PNG " + stats.Tex + " / INI " + stats.Ini + " / PREF " + stats.Pref
                + " 变更 → 失效 " + hit + " 个 SpriteData"
                + (stats.Tiles > 0 ? "（含 " + stats.Tiles + " 个分片）" : string.Empty)
                + (stats.Reloaded > 0 ? "，重解码 " + stats.Reloaded + " 张" : "，只更新元数据")
                + "。角色下次进画面时生效（第 " + _reloadCount + " 次）。");

            int miss = wanted.Count - stats.Hit.Count;
            if (miss > 0) {
                AcsMod.Log("[BCS] 热加载：另有 " + miss + " 个路径不在当前皮肤表里（新增/删除/改名），"
                    + "这类要敲一次 ACS.ReloadAcs 才会生效。");
            }
        }
        catch (Exception ex) {
            AcsMod.Warn("[BCS] 热加载失败: " + ex.Message);
        }
    }

    private static bool Clear(SpriteData? data, Dictionary<string, AssetKind> wanted, Stats stats)
    {
        if (data is null) {
            return false;
        }

        string path = data.path;
        if (path.IsEmpty() || !wanted.TryGetValue(path, out AssetKind kind)) {
            return false;
        }

        stats.Hit.Add(path);

        if ((kind & AssetKind.Tex) != 0) {
            Invalidate(data, stats);
        }

        if ((kind & AssetKind.Ini) != 0)
        {
            // 只有 frame / scale 改变切分方式；单纯改 time（播放速度）动元数据就够，
            // 下一帧推进自然按新 time 走，不必重新解码整张图。
            int frame = data.frame;
            int scale = data.scale;
            data.LoadAnimationIni();
            if (data.frame != frame || data.scale != scale) {
                Invalidate(data, stats);
            }
        }

        if ((kind & AssetKind.Pref) != 0)
        {
            // 原版按 PPU = 100 * scaleTex 切 sprite；密度没变就不用动贴图。
            float scaleTex = data.pref?.ScaleTex ?? 1f;
            data.LoadPref();
            if (!Mathf.Approximately(scaleTex, data.pref?.ScaleTex ?? 1f)) {
                Invalidate(data, stats);
            }
        }

        return true;
    }

    /// <summary>丢缓存 + 归还分片。不动 <c>path</c>，下次 <c>GetSprites()</c> 会自己重新读盘。</summary>
    private static void Invalidate(SpriteData data, Stats stats)
    {
        data.sprites = null;
        data.tex = null;

        if (TiledSprite.Release(data.path)) {
            stats.Tiles++;
        }

        stats.Reloaded++;
    }

    // ---------------- 命令行 ----------------

    internal static string Status()
    {
        int dirty;
        lock (Gate) {
            dirty = Dirty.Count;
        }

        return "热加载 = " + (Enabled ? "ON" : "OFF")
            + "; 监视目录 " + Watchers.Count
            + "; 待处理 " + dirty
            + "; 已作废 " + _reloadCount + " 次"
            + (Enabled ? string.Empty : Usage);
    }
}
