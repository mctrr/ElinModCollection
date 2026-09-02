using System;
using System.Collections.Generic;
using System.IO;
using ACS.API;
using UnityEngine;

namespace ACS.Patches;

/// <summary>
/// 皮肤文件热更检测（作者调试用）：只在“游戏切回前台”时扫一次受管皮肤 png/ini 的
/// LastWriteTime（工作流 = 切出去改图 → 切回看效果）。检测到变化后等待“稳定窗口”
/// （文件写完不再变化）再自动执行 ReloadAcsCore；兜底命令 ACS.ReloadAcs。
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

    /// <summary>重载完成后清除“待重载”状态。
    /// 文件基线不清空：ReloadAcsCore 内部 ListSkins 会经 TrackSkin 把 LastStamp 更新为
    /// 磁盘最新值，自动对齐基线；清空反而会让 watcher 失去跟踪目标。</summary>
    internal static void ResetBaseline()
    {
        _pendingSinceTicks = 0;
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

    /// <summary>扫一次磁盘基线；有变化则重置稳定窗口起点（等文件写完不再变）。</summary>
    private static void ScanNow()
    {
        if (ScanChanged()) {
            _pendingSinceTicks = DateTime.UtcNow.Ticks;
        }
    }

    /// <summary>文件已稳定超过窗口则执行自动重载（前台/后台共用）。</summary>
    private static void TryFireStable()
    {
        if (_pendingSinceTicks == 0) {
            return;
        }

        if (DateTime.UtcNow.Ticks - _pendingSinceTicks < StableWindowTicks) {
            return;
        }

        _pendingSinceTicks = 0;
        try {
            AcsMod.Log("[AutoReload] skin files changed on disk; reloading textures...");
            string r = PcSkinCommands.ReloadAcsCore();
            AcsMod.Log("[AutoReload] " + r);
        }
        catch (Exception ex) {
            AcsMod.Warn($"[AutoReload] failed: {ex.Message}");
        }
        finally {
            _pendingSinceTicks = 0;
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
    {        if (data is null || LastStamp.Count >= MaxTrackedFiles) {
            return;
        }

        string? path = data.path;
        if (path.IsEmpty()) {
            return;
        }

        string key = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? path
            : path + ".png";
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

    private static bool ScanChanged()
    {
        // 删除的文件：从基线移除并视为变化（保守处理）
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

                return true;
            }
        }

        bool changed = false;
        // 键在扫描中可能被 Track 增删，复制一份避免枚举时修改
        foreach (string key in new List<string>(LastStamp.Keys)) {
            try {
                long t = File.GetLastWriteTimeUtc(key).Ticks;
                if (LastStamp.TryGetValue(key, out long old)) {
                    if (old != t) {
                        changed = true;
                        LastStamp[key] = t;
                    }
                }
            }
            catch (Exception) {
            }
        }

        return changed;
    }
}
