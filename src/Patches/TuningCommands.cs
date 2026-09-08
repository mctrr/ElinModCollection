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

    /// <summary>
    /// 查询 / 开关对话全身立绘兜底（FullPortraitPatch）。
    /// 用法：ACS.FullPortrait        → 查询状态
    ///      ACS.FullPortrait off   → 关闭补丁（完全恢复原版行为，用来验证原版自己能不能显示）
    ///      ACS.FullPortrait on    → 打开（默认）
    /// 关闭后立绘仍出现 ⇒ 原版本来就支持，补丁只是补摆位/保留小头像。
    /// </summary>
    [ConsoleCommand]
    public static string FullPortrait(string arg = "")
    {
        string status = "Full-body portrait fallback = " + (FullPortraitPatch.Enabled ? "ON" : "OFF")
            + " (default ON; off = vanilla behaviour). Usage: ACS.FullPortrait on|off";

        if (arg.IsEmpty()) {
            return status;
        }

        string a = arg.Trim().ToLowerInvariant();
        if (a is "on" or "1" or "true") {
            FullPortraitPatch.Enabled = true;
            return "Full-body portrait fallback enabled (re-enter dialogue to see it).";
        }

        if (a is "off" or "0" or "false") {
            FullPortraitPatch.Enabled = false;
            return "Full-body portrait fallback disabled: vanilla behaviour only.";
        }

        return "Invalid argument '" + arg + "'. Usage: ACS.FullPortrait on|off";
    }

    /// <summary>
    /// 查询 / 调整对话全身立绘的布局。
    /// 用法：ACS.PortraitLayout                  → 查询当前布局
    ///      ACS.PortraitLayout 0.9              → 高度 = 屏高 × 0.9
    ///      ACS.PortraitLayout 0.9 120          → 高度 0.9、水平偏移 120px
    ///      ACS.PortraitLayout 0.9 120 30       → 再加垂直偏移 30px（距底边）
    ///      ACS.PortraitLayout 0.9 120 30 right → 锚到右下角（left = 左下，默认）
    ///      ACS.PortraitLayout reset            → 复位默认（0.85 / 8 / 0 / left）
    /// 锚点：父级（对话面板根，铺满屏）的左下或右下角，pivot 同角，偏移按像素。
    /// 改完重新打开对话生效。
    /// </summary>
    [ConsoleCommand]
    public static string PortraitLayout(string arg = "")
    {
        string Cur()
            => $"heightRatio={FullPortraitPatch.HeightRatio:0.00}"
               + $" x={FullPortraitPatch.OffsetX:0} y={FullPortraitPatch.OffsetY:0}"
               + $" anchor={(FullPortraitPatch.AnchorRight ? "right" : "left")}";

        if (arg.IsEmpty()) {
            return "Portrait layout: " + Cur()
                + ". Usage: ACS.PortraitLayout <heightRatio> [x] [y] [left|right] | reset";
        }

        string a = arg.Trim().ToLowerInvariant();
        if (a == "reset") {
            FullPortraitPatch.ResetLayout();
            return "Portrait layout reset: " + Cur();
        }

        string[] parts = a.Split(new[] { ' ', ',', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float hr)) {
            if (hr < 0.1f || hr > 2f) {
                return "heightRatio out of range (0.10 ~ 2.00).";
            }

            FullPortraitPatch.HeightRatio = hr;
        }
        else {
            return "Invalid heightRatio '" + parts[0] + "'.";
        }

        if (parts.Length > 1
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) {
            FullPortraitPatch.OffsetX = x;
        }

        if (parts.Length > 2
            && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) {
            FullPortraitPatch.OffsetY = y;
        }

        for (int i = 1; i < parts.Length; i++) {
            if (parts[i] == "right") {
                FullPortraitPatch.AnchorRight = true;
            }
            else if (parts[i] == "left") {
                FullPortraitPatch.AnchorRight = false;
            }
        }

        return "Portrait layout: " + Cur() + " (re-open dialogue to apply)";
    }
}
