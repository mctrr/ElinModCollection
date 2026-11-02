using System;
using System.Collections.Generic;

namespace ElinPeek;

/// <summary>
/// Lightweight player-facing strings — same pattern as the NpcLabor mod:
/// CN default, EN / JP picked when the game language matches.
/// </summary>
internal static class PeekText
{
    private static readonly Dictionary<string, string> Cn = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["peek.button"] = "窥视",
        ["peek.hint.enter"] = "窥视中：移动鼠标环顾四周，右键 / Esc / 左键退出。"
    };

    private static readonly Dictionary<string, string> En = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["peek.button"] = "Peek",
        ["peek.hint.enter"] = "Peeking: move the mouse to look around. RMB / Esc / LMB to stop."
    };

    private static readonly Dictionary<string, string> Ja = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["peek.button"] = "覗き見",
        ["peek.hint.enter"] = "覗き見中：マウスを動かして周囲を見渡せます。右クリック / Esc / 左クリックで終了。"
    };

    internal static string T(string key)
    {
        Dictionary<string, string> primary = PickTable();
        if (primary.TryGetValue(key, out string s) && !string.IsNullOrEmpty(s))
        {
            return s;
        }
        if (primary != En && En.TryGetValue(key, out s) && !string.IsNullOrEmpty(s))
        {
            return s;
        }
        if (Cn.TryGetValue(key, out s) && !string.IsNullOrEmpty(s))
        {
            return s;
        }
        return key;
    }

    /// <summary>按游戏当前语言选表：英文→En，日文→Ja，其余（中文等）→Cn。</summary>
    private static Dictionary<string, string> PickTable()
    {
        try
        {
            if (Lang.isEN)
            {
                return En;
            }
            if (Lang.isJP)
            {
                return Ja;
            }
            string code = (Lang.langCode ?? string.Empty).Trim().ToLowerInvariant();
            if (code == "en" || code.StartsWith("en"))
            {
                return En;
            }
            if (code == "jp" || code == "ja" || code.StartsWith("jp") || code.StartsWith("ja"))
            {
                return Ja;
            }
        }
        catch
        {
            // fall through to CN
        }
        return Cn;
    }
}
