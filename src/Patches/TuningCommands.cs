using System.Globalization;
using ACS.API;
using ReflexCLI.Attributes;

namespace ACS.Patches;

/// <summary>
/// 运行时调参命令（不用改代码/重启即可微调视觉参数）。
/// 前缀 <c>ACS</c>（与 PcSkinCommands 同前缀，命令名不冲突即可）。
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
            return $"NPC move hysteresis = {AcsStateResolver.NpcMoveHysteresis:0.00}s"
                   + $" (default {AcsStateResolver.DefaultNpcMoveHysteresis:0.00}s;"
                   + " usage: ACS.MoveHysteresis <sec>, 0 = reset)";
        }

        if (sec == "0") {
            AcsStateResolver.NpcMoveHysteresis = AcsStateResolver.DefaultNpcMoveHysteresis;
            return $"NPC move hysteresis reset to default {AcsStateResolver.NpcMoveHysteresis:0.00}s.";
        }

        if (float.TryParse(sec, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) {
            if (v < 0f || v > 2f) {
                return "Out of range (0.00 ~ 2.00s). Use 0 to reset to default.";
            }

            AcsStateResolver.NpcMoveHysteresis = v;
            return $"NPC move hysteresis set to {v:0.00}s.";
        }

        return "Invalid number '" + sec + "'. Usage: ACS.MoveHysteresis <sec> (0 = reset).";
    }

    /// <summary>
    /// 查询 / 开关皮肤热更（SkinChangeWatcher）。
    /// 用法：ACS.AutoReload         → 查询状态
    ///      ACS.AutoReload on      → 打开（此后每次切回前台会检测皮肤文件变化并自动重载）
    ///      ACS.AutoReload off     → 关闭（默认状态；切回游戏不再扫磁盘，避免卡 1-2 秒）
    /// 手动全量重载任何时候可用：ACS.ReloadAcs
    /// </summary>
    [ConsoleCommand]
    public static string AutoReload(string arg = "")
    {
        string status = "Auto-reload = " + (SkinChangeWatcher.Enabled ? "ON" : "OFF")
            + " (default OFF; skin file changes are NOT watched)."
            + " Usage: ACS.AutoReload on|off";

        if (arg.IsEmpty()) {
            return status;
        }

        string a = arg.Trim().ToLowerInvariant();
        if (a is "on" or "1" or "true") {
            SkinChangeWatcher.Enabled = true;
            return "Auto-reload enabled: next focus-return will scan skin files and reload if changed.";
        }

        if (a is "off" or "0" or "false") {
            SkinChangeWatcher.Enabled = false;
            return "Auto-reload disabled: focus-return no longer scans skin files.";
        }

        return "Invalid argument '" + arg + "'. Usage: ACS.AutoReload on|off";
    }
}
