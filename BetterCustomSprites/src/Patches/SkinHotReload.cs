using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using ACS.API;
using ACS.Tiling;

namespace ACS.Patches;

/// <summary>
/// 皮肤热加载：监视贴图目录，检测到玩家在游戏外（Aseprite / 资源管理器 / 脚本）改了 PNG，
/// 就把对应的 <c>SpriteData</c> 的贴图丢掉，<b>下一帧渲染自动重新读盘解码</b>。
///
/// <b>为什么只清不重载</b>：清完 <c>sprites/tex</c> 之后，原版 <c>SpriteData.GetSprites()</c>
/// 的懒加载会把整条链路自己接回去（读盘 → decode → 上传 → 切帧）。
/// 也就是说「什么时候重载」由角色下一次出现在画面里决定 —— 不在画面上的皮肤不浪费解码。
///
/// <b>怎么做到不卡</b>：<see cref="FileSystemWatcher"/> 的回调只做一件事 ——
/// 把路径塞进一个加锁的 <c>HashSet</c>（后台线程，绝不碰 Unity API）。
/// 真正的清理由 <see cref="Tick"/> 在主线程做，并且要等<b>最后一次改动之后
/// <see cref="DebounceSeconds"/> 秒</b>才动手：导出工具写文件往往是「先 truncate 再分段写」，
/// 不防抖就会读到半张图。
///
/// <b>边界</b>：只处理「改已有图」。新增 / 删除 / 改名会让贴图路径与 <c>dictSkins</c> 对不上，
/// 那种情况会打日志提示敲 <c>ACS.ReloadAcs</c>，不在这里自动重建整张表。
/// </summary>
internal static class SkinHotReload
{
    /// <summary>总开关，默认关（由 <c>热加载</c> 设置项 / <c>ACS.HotReload</c> 命令驱动）。</summary>
    internal static bool Enabled;

    /// <summary>最后一次收到文件事件的时刻（<see cref="Environment.TickCount64"/> 毫秒）。</summary>
    private const long DebounceMilliseconds = 1500L;

    private const string Usage = " Usage: ACS.HotReload on|off";

    private static readonly List<FileSystemWatcher> Watchers = new();
    private static readonly HashSet<string> Dirty = new(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>打开监视（幂等）。目录来源复用 <see cref="TextureSkinCatalog"/> 的那套：Custom + 非 builtin 的 Package/*/Texture。</summary>
    internal static void Start()
    {
        lock (Gate)
        {
            if (Watchers.Count > 0) {
                return;
            }
        }

        var roots = new List<string>();

        try {
            string custom = CorePath.custom + "Texture";
            if (Directory.Exists(custom)) {
                roots.Add(custom);
            }
        }
        catch (Exception ex) {
            AcsMod.Warn("[BCS] 热加载: Custom 目录不可读 — " + ex.Message);
        }

        try {
            foreach (var (dir, _) in PackageIterator.GetDirectoriesEx("Texture", useCache: true)) {
                if (dir is not null && dir.Exists) {
                    roots.Add(dir.FullName);
                }
            }
        }
        catch (Exception ex) {
            AcsMod.Warn("[BCS] 热加载: 枚举 Package 失败 — " + ex.Message);
        }

        int ok = 0;
        foreach (string root in roots)
        {
            try {
                var w = new FileSystemWatcher(root, "*.png") {
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

        AcsMod.Log("[BCS] 热加载：已监视 " + ok + "/" + roots.Count + " 个贴图目录"
            + (ok > 0 ? "（在游戏外改 PNG 会自动重载）" : string.Empty));
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

    private static void Mark(string? fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) {
            return;
        }

        // SpriteData.path 不带扩展名，事件带 .png —— 统一去掉扩展名再比。
        string key = fullPath!;
        if (key.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) {
            key = key.Substring(0, key.Length - 4);
        }

        lock (Gate) {
            Dirty.Add(key);

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

        string[] pending;
        lock (Gate) {
            if (Dirty.Count == 0) {
                return;
            }

            if (NowMs() - _lastChangeMs < DebounceMilliseconds) {
                return;
            }

            pending = new string[Dirty.Count];
            Dirty.CopyTo(pending);
            Dirty.Clear();
        }

        Apply(pending);
    }

    private static void Apply(string[] pending)
    {
        try {
            var dict = SpriteReplacer.dictSkins;
            if (dict is null || dict.Count == 0) {
                AcsMod.Log("[BCS] 热加载：检测到 " + pending.Length + " 个 PNG 变更，但皮肤表还没建好，忽略。");
                return;
            }

            var wanted = new HashSet<string>(pending, StringComparer.OrdinalIgnoreCase);

            // 刚把贴图作废，跨重建复用必须让路，否则会把作废的旧贴图搬回来。
            SkinTextureReuse.Suppress();

            // 之前加载失败过的（半张图 / 尺寸超限）现在文件改好了，允许重试。
            SpriteDataLoadSpritesPatch.ClearFailureCaches();
            SkinEditUiPatch.ClearThumbs();

            int hit = 0;
            int tiles = 0;

            foreach (var kv in dict)
            {
                var skin = kv.Value;
                if (skin is null) {
                    continue;
                }

                if (Clear(skin.data, wanted, ref tiles)) {
                    hit++;
                }

                var subs = skin.suffixes;
                if (subs is null || subs.Count == 0) {
                    continue;
                }

                foreach (var sub in subs.Values) {
                    if (Clear(sub, wanted, ref tiles)) {
                        hit++;
                    }
                }
            }

            _reloadCount++;
            AcsMod.Log("[BCS] 热加载：检测到 " + pending.Length + " 个 PNG 变更，作废 " + hit + " 张贴图"
                + (tiles > 0 ? "（含 " + tiles + " 个分片）" : string.Empty)
                + "。角色下次进画面时会自动重新解码（第 " + _reloadCount + " 次）。");

            int miss = pending.Length - hit;
            if (miss > 0) {
                AcsMod.Log("[BCS] 热加载：另有 " + miss + " 个路径不在当前皮肤表里（新增/删除/改名），"
                    + "这类要敲一次 ACS.ReloadAcs 才会生效。");
            }
        }
        catch (Exception ex) {
            AcsMod.Warn("[BCS] 热加载失败: " + ex.Message);
        }
    }

    private static bool Clear(SpriteData? data, HashSet<string> wanted, ref int tiles)
    {
        if (data is null) {
            return false;
        }

        string path = data.path;
        if (path.IsEmpty() || !wanted.Contains(path)) {
            return false;
        }

        data.sprites = null;
        data.tex = null;

        if (TiledSprite.Release(path)) {
            tiles++;
        }

        return true;
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
