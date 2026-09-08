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
    ///      ACS.MoveHysteresis 0          → 恢复默认（0.1s）
    /// 含义：NPC isMoving 刚变 false 后，窗口内仍按“移动中”处理，平滑跟队
    /// “冲一小段→停→再冲”的节拍，避免短停就切 idle 导致 move 从第 0 帧反复重播。
    /// 窗口越大 → 停稳后原地“空走”越多；越小 → 越接近改前（短停即切 idle）的抖动。
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
}
