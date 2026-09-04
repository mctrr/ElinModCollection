using System;
using System.Collections.Generic;
using System.IO;
using ACS.API;
using UnityEngine;

namespace ACS.Patches;

/// <summary>
/// 皮肤文件热更检测（作者调试用）：只在“游戏切回前台”时扫一次受管皮肤 png/ini 的
/// LastWriteTime（工作流 = 切出去改图 → 切回看效果）。检测到变化后等待“稳定窗口”
/// （文件写完不再变化）再触发重载。兜底命令 ACS.ReloadAcs。
///
/// 重载分两级，避免每次全量 ListSkins 卡 1-2 秒：
///  - 纯 PNG 内容变化（最常见：改图重导）→ 只失效被改的 SpriteData + 只刷新用了
///    该皮肤的在场角色（轻量，几乎无感）
///  - 结构性变化（ini 变更 / 文件被删，影响 clip 解析或 key 集合）→ 走全量
///    ReloadAcsCore（ListSkins + 全场 RefreshSprite）
///
/// 为什么等稳定窗口：导出器写文件是分步的，若在写入中途触发重载会读到半截 PNG/ini。
/// </summary>
internal static class SkinChangeWatcher
{
    private const long StableWindowTicks = TimeSpan.TicksPerSecond * 3; // 稳定 3 秒才重载
    private const int MaxTrackedFiles = 3000; // 防御上限

    private static readonly Dictionary<string, long> LastStamp = new(); // 文件全路径 → LastWriteTimeUtc.Ticks
    private static long _pendingSinceTicks;
    private static bool _wasFocused = true; // 上次 Tick 时的前台状态（true=启动即前台）
    private static bool _pendingStructural;
    private static readonly HashSet<string> PendingFiles = new();

    /// <summary>重载完成后清除“待重载”状态。
    /// 文件基线不清空：全量重载的 ListSkins 会经 TrackSkin 把 LastStamp 更新为磁盘
    /// 最新值；轻量重载不清基线（懒加载读的就是新文件）。清空反而失去跟踪目标。</summary>
    internal static void ResetBaseline()
    {
        _pendingSinceTicks = 0;
        _pendingStructural = false;
        PendingFiles.Clear();
    }

    internal static void Tick(int frameCount)
    {
        bool focused = Application.isFocused;

        if (focused) {
            // 切回前台边沿：扫一次。后台期间不轮询（作者在改图时无 Update / 无需检测）
            if (!_wasFocused) {
                _wasFocused = true;
                ScanNow();
            }

            TryFireStable();
            return;
        }

        _wasFocused = false; // 后台：什么都不做，等切回前台那一下
    }

    /// <summary>扫一次磁盘基线；有变化则记录变更集并重置稳定窗口起点。</summary>
    private static void ScanNow()
    {
        if (ScanChanged(out var files, out bool structural)) {
            _pendingStructural = _pendingStructural || structural;
            foreach (string f in files) {
                PendingFiles.Add(f);
            }

            _pendingSinceTicks = DateTime.UtcNow.Ticks;
        }
    }

    /// <summary>文件已稳定超过窗口则触发重载（前台/后台共用）。</summary>
    private static void TryFireStable()
    {
        if (_pendingSinceTicks == 0) {
            return;
        }

        if (DateTime.UtcNow.Ticks - _pendingSinceTicks < StableWindowTicks) {
            return;
        }

        string[] files = new List<string>(PendingFiles).ToArray();
        bool structural = _pendingStructural;
        _pendingSinceTicks = 0;
        _pendingStructural = false;
        PendingFiles.Clear();

        try {
            AcsMod.Log($"[AutoReload] {files.Length} skin file(s) changed"
                + (structural ? " (structural)" : "") + "; reloading...");
            string r = PcSkinCommands.ReloadChangedSkins(structural, files);
            AcsMod.Log("[AutoReload] " + r);
        }
        catch (Exception ex) {
            AcsMod.Warn($"[AutoReload] failed: {ex.Message}");
        }
        finally {
            _pendingSinceTicks = 0;
            _pendingStructural = false;
            PendingFiles.Clear();
        }
    }

    /// <summary>ListSkins 后记录整个 replacer 的 png 文件（data + 各 suffix clip）。</summary>
    internal static void TrackSkin(SpriteReplacer? skin)
    {
        if (skin is null || LastStamp.Count >= MaxTrackedFiles) {
            return;
        }

        Track(skin.data);
        if (skin.suffixes is null) {
            return;
        }

        foreach (var sd in skin.suffixes.Values) {
            if (sd is not null) {
                Track(sd);
            }
        }
    }

    /// <summary>记录一份 skin 数据所属目录，供轮询。幂等、带文件数上限。</summary>
    internal static void Track(SpriteData? data)
    {
        if (data is null || LastStamp.Count >= MaxTrackedFiles) {
            return;
        }

        string? path = data.path;
        if (path.IsEmpty()) {
            return;
        }

        TrackPng(path);
    }

    /// <summary>
    /// ListSkins 后补 Track 不并进 ListSkins 的 Texture 系皮肤（NPC 用的贴图目录：
    /// 底图 png + 同目录 {baseId}*.png 的 _acs_ / _bcs_combat clip）。否则这些文件
    /// 不在基线里，改了磁盘也不会触发热加载——这正是“给 NPC 换的 skin 不热更”的根因。
    /// </summary>
    internal static void TrackTextureSkins()
    {
        try {
            foreach (var entry in TextureSkinCatalog.ListBaseItems()) {
                TrackPng(entry.Path); // 底图（无 .png 后缀）
                try {
                    string? dir = Path.GetDirectoryName(entry.Path + ".png");
                    if (dir.IsEmpty() || !Directory.Exists(dir)) {
                        continue;
                    }

                    // 复用 TextureSkinCatalog 的目录扫描缓存（同 CollectFilesForBase 的候选口径），
                    // 不再对每个 base 重复 GetFiles。
                    foreach (var f in TextureSkinCatalog.GetBasePngCandidates(dir, entry.Id)) {
                        TrackPng(f);
                    }
                }
                catch (Exception) {
                    // 单目录枚举失败不影响其它条目
                }
            }
        }
        catch (Exception ex) {
            ACS.AcsMod.Warn($"TrackTextureSkins: {ex.Message}");
        }
    }

    /// <summary>把单个皮肤 png（或裸路径）记入基线：路径幂等 + 上限保护。</summary>
    private static void TrackPng(string pngOrBarePath)
    {
        if (LastStamp.Count >= MaxTrackedFiles) {
            return;
        }

        string key = pngOrBarePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? pngOrBarePath
            : pngOrBarePath + ".png";
        if (!File.Exists(key)) {
            return;
        }

        try {
            long t = File.GetLastWriteTimeUtc(key).Ticks;
            if (LastStamp.TryGetValue(key, out long old) && old == t) {
                return;
            }

            LastStamp[key] = t;
        }
        catch (Exception) {
            // 文件瞬时被占用/删除，跳过本轮
        }
    }

    /// <summary>
    /// 扫描基线：返回是否有变化；<paramref name="changedFiles"/> = mtime 变化的文件；
    /// <paramref name="structural"/> = 含 ini 变更或文件被删（需要全量重建）。
    /// </summary>
    private static bool ScanChanged(out HashSet<string> changedFiles, out bool structural)
    {
        changedFiles = new HashSet<string>();
        structural = false;

        // 被删除的文件：从基线移除并视为结构性变化（key 集合变化 → 全量）
        if (LastStamp.Count > 0) {
            List<string>? gone = null;
            foreach (var kv in LastStamp) {
                if (!File.Exists(kv.Key)) {
                    (gone ??= new List<string>()).Add(kv.Key);
                }
            }

            if (gone is not null) {
                foreach (string g in gone) {
                    LastStamp.Remove(g);
                }

                structural = true;
            }
        }

        // 键在扫描中可能被 Track 增删，复制一份避免枚举时修改
        foreach (string key in new List<string>(LastStamp.Keys)) {
            try {
                long t = File.GetLastWriteTimeUtc(key).Ticks;
                if (LastStamp.TryGetValue(key, out long old) && old != t) {
                    LastStamp[key] = t;
                    changedFiles.Add(key);
                    // ini = clip meta（帧数/时长/名字解析），会影响 key 集 → 全量
                    if (key.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) {
                        structural = true;
                    }
                }
            }
            catch (Exception) {
            }
        }

        return structural || changedFiles.Count > 0;
    }
}
