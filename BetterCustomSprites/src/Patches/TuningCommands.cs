using System;
using System.Globalization;
using ACS.API;
using ACS.Frame;
using ReflexCLI.Attributes;

namespace ACS.Patches;

/// <summary>
/// 运行时调参命令（不用改代码/重启即可微调视觉参数）。
/// 前缀 <c>ACS</c>（与 PcSkinCommands 同前缀，命令名不冲突即可）。
///
/// 这些命令与游戏内模组配置页共用同一份 <see cref="BcsConfig"/> 设置项：
/// 命令改的值会写进配置文件，配置页滑杆改的值也会被命令读到，两者不会互相覆盖。
/// </summary>
[ConsoleCommandClassCustomizer("ACS")]
internal static class TuningCommands
{
    /// <summary>
    /// 查询 / 设置 NPC 移动粘滞窗口（秒）。
    /// 用法：ACS.MoveHysteresis            → 查询当前值
    ///      ACS.MoveHysteresis 0.25       → 设为 0.25s
    ///      ACS.MoveHysteresis 0          → 恢复默认（0.16s）
    /// 含义：NPC isMoving 刚变 false 后，窗口内仍按“移动中”处理（move 动画继续
    /// 正常播放），平滑跟队“冲一小段→停→再冲”的节拍，避免短停就切 idle 导致
    /// move 从第 0 帧反复重播。窗口越大 → 停稳后原地“空走”越多；越小 → 越接近
    /// 短停即切 idle 的抖动。0.2s 为实测甜点。
    /// </summary>
    [ConsoleCommand]
    public static string MoveHysteresis(string sec = "")
    {
        if (sec.IsEmpty()) {
            return $"NPC move hysteresis = {PlayState.NpcMoveHysteresis:0.00}s"
                   + $" (default {PlayState.DefaultNpcMoveHysteresis:0.00}s;"
                   + " usage: ACS.MoveHysteresis <sec>, 0 = reset)";
        }

        if (sec == "0") {
            BcsConfig.SetMoveHysteresis(PlayState.DefaultNpcMoveHysteresis);
            return $"NPC move hysteresis reset to default {PlayState.NpcMoveHysteresis:0.00}s.";
        }

        if (float.TryParse(sec, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) {
            if (v < 0f || v > 2f) {
                return "Out of range (0.00 ~ 2.00s). Use 0 to reset to default.";
            }

            BcsConfig.SetMoveHysteresis(v);
            return $"NPC move hysteresis set to {v:0.00}s.";
        }

        return "Invalid number '" + sec + "'. Usage: ACS.MoveHysteresis <sec> (0 = reset).";
    }

    /// <summary>
    /// 查询 / 调整「皮肤贴图按需释放」（<see cref="SpriteCacheEvict"/>）。
    /// 用法：ACS.SpriteCache            → 查询状态（在用 pin 数、已载入/总底图数、上次释放量）
    ///      ACS.SpriteCache off        → 关闭，恢复原版行为（只懒加载、从不卸载）
    ///      ACS.SpriteCache on         → 打开（默认）
    ///      ACS.SpriteCache 30         → 宽限期 = 30s（角色离图后仍保留贴图的时长）
    ///      ACS.SpriteCache 0          → 宽限期复位默认（30s）
    ///      ACS.SpriteCache flush      → 立刻释放一次（只保当前地图在用的，跳过宽限）
    ///      ACS.SpriteCache gc         → 强制一次 GC + UnloadUnusedAssets（手动归还显存，诊断用）
    /// 宽限期调小的代价：出屏 / 换地图来回走会触发重新解码，可能看到卡顿。
    /// </summary>
    [ConsoleCommand]
    public static string SpriteCache(string arg = "")
    {
        const string usage = " Usage: ACS.SpriteCache on|off | <graceSec> | 0=reset | flush | gc";

        if (arg.IsEmpty()) {
            return SpriteCacheEvict.Status() + usage;
        }

        string a = arg.Trim().ToLowerInvariant();

        if (a is "on" or "true") {
            BcsConfig.SetCacheEnabled(true);
            return "sprite cache evict enabled. " + SpriteCacheEvict.Status();
        }

        if (a is "off" or "false") {
            BcsConfig.SetCacheEnabled(false);
            return "sprite cache evict disabled: vanilla behaviour (load on demand, never release).";
        }

        if (a == "flush") {
            return SpriteCacheEvict.Flush();
        }

        if (a == "gc") {
            return SpriteCacheEvict.Collect();
        }

        if (a == "reset") {
            return ResetCache();
        }

        if (float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) {
            if (v <= 0f) {
                return ResetCache();
            }

            if (v > 600f) {
                return "Out of range (1 ~ 600s). Use 0 to reset to default.";
            }

            BcsConfig.SetGraceSeconds(v);
            return "sprite cache grace set to " + v.ToString("0") + "s (0 = reset to default).";
        }

        return "Invalid argument '" + arg + "'." + usage;
    }

    /// <summary>查询 / 开关「贴图跨重建复用」（<see cref="SkinTextureReuse"/>，默认开）。
    /// 用法：ACS.KeepTextures          → 查询状态
    ///      ACS.KeepTextures on       → 打开（默认）：重建皮肤表时沿用已解码的贴图，不再重新读盘
    ///      ACS.KeepTextures off      → 关闭，恢复原版「每次重建都重读一遍」
    /// 关掉它治不了帧数（每帧开销靠渲染路径续期），治的是读档 / 切区域回来那一下的重解码。
    /// </summary>
    [ConsoleCommand]
    public static string KeepTextures(string arg = "")
    {
        string a = arg.Trim().ToLowerInvariant();

        if (a is "on" or "true") {
            BcsConfig.SetKeepOnRebuild(true);
            return "贴图跨重建复用 = 开（重建皮肤表时沿用已解码贴图，切区域/读档回来不再重解码）。";
        }

        if (a is "off" or "false") {
            BcsConfig.SetKeepOnRebuild(false);
            return "贴图跨重建复用 = 关（原版行为：每次重建都重新读盘解码，切区域回来会卡一下）。";
        }

        return "贴图跨重建复用 = " + (SkinTextureReuse.Enabled ? "开" : "关")
            + "  Usage: ACS.KeepTextures on|off";
    }

    /// <summary>查询 / 开关「皮肤热加载」（<see cref="SkinHotReload"/>，默认关）。
    /// 用法：ACS.HotReload          → 查询状态（监视目录数 / 待处理 / 已作废次数）
    ///      ACS.HotReload on       → 打开：监视贴图目录，游戏外改完 PNG 自动重载
    ///      ACS.HotReload off      → 关闭
    /// 打开后用 Aseprite 之类改完 PNG 存盘，1.5 秒后游戏内自动重读（角色下次进画面时生效）。
    /// 只处理「改已有图」；新增 / 删除 / 改名要敲一次 ACS.ReloadAcs。
    /// </summary>
    [ConsoleCommand]
    public static string HotReload(string arg = "")
    {
        string a = arg.Trim().ToLowerInvariant();

        if (a is "on" or "true") {
            BcsConfig.SetHotReload(true);
            return SkinHotReload.Status();
        }

        if (a is "off" or "false") {
            BcsConfig.SetHotReload(false);
            return SkinHotReload.Status();
        }

        return SkinHotReload.Status();
    }

    /// <summary>查询 / 开关「诊断日志」（<see cref="AcsMod.DiagnosticLog"/>，默认关）。
    /// 用法：ACS.DiagLog          → 查询状态
    ///      ACS.DiagLog on       → 打开：输出皮肤表枚举、密度注入、分片结果、缓存释放、性能探针…
    ///      ACS.DiagLog off      → 关闭
    /// 只门控 Info 级输出；**警告和错误永远输出**（那才是出问题的线索）。
    /// 打开时性能探针（ACS.Perf）会一起开；也可以之后用 ACS.Perf 单独切。
    /// </summary>
    [ConsoleCommand]
    public static string DiagLog(string arg = "")
    {
        string a = arg.Trim().ToLowerInvariant();

        if (a is "on" or "true") {
            BcsConfig.SetDiagnosticLog(true);
            return "诊断日志 = 开（性能探针同步开启）。用完记得 ACS.DiagLog off。";
        }

        if (a is "off" or "false") {
            BcsConfig.SetDiagnosticLog(false);
            return "诊断日志 = 关（只留警告和错误）。";
        }

        return "诊断日志 = " + (AcsMod.DiagnosticLog ? "开" : "关")
            + "; 性能探针 = " + (BcsPerf.Enabled ? "开" : "关")
            + "  Usage: ACS.DiagLog on|off";
    }

    /// <summary>宽限期复位：先让设置项回到默认值（写回配置），再复位扫描间隔等运行时状态。</summary>
    private static string ResetCache()
    {
        BcsConfig.SetGraceSeconds(SpriteCacheEvict.DefaultGraceSeconds);
        return SpriteCacheEvict.Reset();
    }

    /// <summary>
    /// 性能探针（只读记录，不改任何行为）。用来回答「高清动态皮肤卡一下到底卡在哪」。
    /// 用法：ACS.Perf             → 查询状态
    ///      ACS.Perf dump        → 完整报表：每张图的加载次数 / 总耗时 / 最大耗时（cpu 解码 vs gpu 上传）
    ///      ACS.Perf clear       → 清空记录（重新开始统计）
    ///      ACS.Perf on | off    → 开关
    ///      ACS.Perf load &lt;ms&gt;   → 单次加载 ≥ 该毫秒才打日志（0 = 恢复默认）
    ///      ACS.Perf frame &lt;ms&gt;  → 单帧 patch 合计 ≥ 该毫秒才打日志（0 = 恢复默认）
    ///
    /// <b>重点看报表里的 n</b>：同一张 sheet 被加载了几次。
    ///   n = 1 → 只是首帧解码贵（该考虑异步解码 / 预热）；
    ///   n &gt; 1 → 被释放又要重新解码（该考虑预热 / 延长 grace）。这时候去优化解码是错的，
    ///            因为问题在于它被解了太多遍，而不是一遍太贵。
    /// </summary>
    [ConsoleCommand]
    public static string Perf(string arg = "")
    {
        string usage = " usage: ACS.Perf [on|off|clear|dump|load <ms>|frame <ms>]";
        string a = arg.Trim();

        if (a.IsEmpty()) {
            return BcsPerf.Status() + usage;
        }

        // 探针已并入「诊断日志」开关（BcsConfig.Apply 里 BcsPerf.Enabled = DiagnosticLog），
        // 所以 on/off 都只动那一个开关，不存在「探针开着但日志关着看不到输出」的状态。
        if (a == "on") {
            BcsConfig.SetDiagnosticLog(true);
            return BcsPerf.Status();
        }

        if (a == "off") {
            BcsConfig.SetDiagnosticLog(false);
            return BcsPerf.Status();
        }

        if (a == "clear") {
            BcsPerf.Clear();
            return "perf: cleared. Walk around, then ACS.Perf dump.";
        }

        if (a == "dump") {
            return BcsPerf.Dump();
        }

        if (a.StartsWith("load", StringComparison.OrdinalIgnoreCase)) {
            return SetPerfThreshold(a.Substring(4), true);
        }

        if (a.StartsWith("frame", StringComparison.OrdinalIgnoreCase)) {
            return SetPerfThreshold(a.Substring(5), false);
        }

        return "Invalid argument '" + arg + "'." + usage;
    }

    private static string SetPerfThreshold(string raw, bool isLoadMs)
    {
        string v = raw.Trim();
        string name = isLoadMs ? "load" : "frame";

        if (v.IsEmpty()) {
            return "usage: ACS.Perf " + name + " <ms> (0 = default).";
        }

        if (!float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float ms) || ms < 0f) {
            return "Invalid number '" + v + "'. usage: ACS.Perf " + name + " <ms> (0 = default).";
        }

        if (ms == 0f) {
            BcsPerf.LoadLogMs = BcsPerf.DefaultLoadLogMs;
            BcsPerf.FrameLogMs = BcsPerf.DefaultFrameLogMs;
            return "perf thresholds reset to default (load "
                   + BcsPerf.DefaultLoadLogMs.ToString("0.#", CultureInfo.InvariantCulture) + "ms / frame "
                   + BcsPerf.DefaultFrameLogMs.ToString("0.#", CultureInfo.InvariantCulture) + "ms).";
        }

        if (isLoadMs) {
            BcsPerf.LoadLogMs = ms;
        } else {
            BcsPerf.FrameLogMs = ms;
        }

        return "perf " + name + " log threshold set to " + ms.ToString("0.#", CultureInfo.InvariantCulture) + "ms.";
    }
}
