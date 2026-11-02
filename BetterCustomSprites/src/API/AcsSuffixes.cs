using System.Collections.Generic;

namespace ACS.API;

/// <summary>
/// suffixes 上的<b>键词汇表</b>：真实 <c>_acs_*</c> clip 的存在性/计数/名字拆解。
/// 皮肤查找（哪个角色用哪套皮肤）不在这里 —— 那在 <c>ACS.Skin.SkinIndex</c>。
/// </summary>
public static class AcsSuffixes
{
    /// <summary>底图（原始片）的 suffixes 键。</summary>
    public const string BaseKey = "";

    /// <summary>静态战斗图后缀键 <c>_bcs_combat</c>（全工程唯一定义）。</summary>
    public const string StaticCombat = "_bcs_combat";

    /// <summary>是否存在至少一条真实 _acs_* 数据（Value 非 null）。</summary>
    public static bool HasRealClip(Dictionary<string, SpriteData>? suffixes)
    {
        if (suffixes is null || suffixes.Count == 0) {
            return false;
        }

        foreach (var kv in suffixes) {
            if (kv.Key is null || kv.Value is null) {
                continue;
            }

            // ReservedSuffix 只是占位（Value 常为 null），这里已被 Value 检查跳过
            if (kv.Key.StartsWith("_acs_")) {
                return true;
            }
        }

        return false;
    }

    public static bool HasRealClip(SpriteReplacer? replacer)
        => HasRealClip(replacer?.suffixes);

    /// <summary>
    /// ACS 状态名全集。资源名格式是 <c>_acs_[&lt;前缀&gt;.]&lt;状态&gt;[.&lt;武器&gt;]</c>，
    /// 这里列的是「状态」那一段，与 <c>FrameChoice</c> 的 Key* 常量一一对应。
    /// </summary>
    private static readonly string[] ClipStates = {
        "idle", "sleep", "greet", "greet1", "greet2", "greet3", "greet4",
        "move", "cmove", "combat", "combat_begin", "atk", "magic", "melee", "range",
        "hurt", "revive", "eat", "wet", "drunk", "confused", "blind", "emote",
    };

    /// <summary>状态后面的主手武器细分（<c>combat.sword</c>）。</summary>
    private static readonly string[] ClipWeapons = { "sword", "polearm", "staff" };

    /// <summary>这一段是不是 ACS 状态名。</summary>
    public static bool IsClipState(string? seg)
    {
        if (seg.IsEmpty()) {
            return false;
        }

        for (int i = 0; i < ClipStates.Length; i++) {
            if (string.Equals(seg, ClipStates[i], System.StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>这一段是不是主手武器细分。</summary>
    public static bool IsClipWeapon(string? seg)
    {
        if (seg.IsEmpty()) {
            return false;
        }

        for (int i = 0; i < ClipWeapons.Length; i++) {
            if (string.Equals(seg, ClipWeapons[i], System.StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 把 ACS clip 名拆成「前缀 + 状态」。
    ///
    /// <b>前缀不是状态</b>：<c>yongzhuang</c> / <c>xn</c> / <c>xingnian</c> / <c>snow</c> / <c>married</c>
    /// 全是作者给差分起的名字（<c>acs_override</c> 选哪套就播哪套），换个人换个包名字就不同。
    /// 状态认不出来时 <paramref name="state"/> 为空，整段名都算前缀 ——
    /// 那是「该前缀的旧式通用片」，语义上等价于 idle（见 <c>FrameChoice</c> 的查找规则 3）。
    /// </summary>
    public static void SplitClipName(string? clipName, out string prefix, out string state)
    {
        prefix = string.Empty;
        state = string.Empty;
        if (clipName.IsEmpty()) {
            return;
        }

        string name = clipName!;
        string[] segs = name.Split('.');
        if (segs.Length == 0) {
            prefix = name;
            return;
        }

        // 首段就是状态：无前缀（combat / combat.sword / emote.wave …）
        if (IsClipState(segs[0])) {
            state = segs[0];
            return;
        }

        // 末段是状态（可能还带武器细分）：前面全是前缀（combat.sword / <前缀>.combat.sword）
        int last = segs.Length - 1;
        if (IsClipWeapon(segs[last])) {
            last--;
        }

        if (last > 0 && IsClipState(segs[last])) {
            state = segs[last];
            prefix = string.Join(".", segs, 0, last);
            return;
        }

        prefix = name;
    }

    public static int CountClips(Dictionary<string, SpriteData>? suffixes)
    {
        if (suffixes is null) {
            return 0;
        }

        int n = 0;
        foreach (var key in suffixes.Keys) {
            if (key != null && key.StartsWith("_acs_")) {
                n++;
            }
        }

        return n;
    }

    public static int CountClips(SpriteReplacer? replacer)
        => CountClips(replacer?.suffixes);
}
