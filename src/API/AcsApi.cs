using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ACS.API;

/// <summary>
/// 解析 ACS 文件名格式：_acs_{name}#{interval}_{begin}-{end}
/// 例：_acs_idle#66_0-3 → name=idle, 帧间隔 66ms, 帧 0..3
///
/// 这是「文件名 → 结构化数据」的工具类；真正选哪一段播放见 <see cref="AcsController"/>。
/// </summary>
public class AcsClip(string name, int interval, int begin, int end)
{
    /// <summary>
    /// Compiled 正则：只编译一次，多次 Match 更快。
    /// (?&lt;name&gt;...) 是命名捕获组，后面用 Groups["name"] 取。
    /// </summary>
    private static readonly Regex _acsFormat = new(
        @"^_acs_(?<name>[^#]+)#(?<interval>\d+)_(?<begin>\d+)-(?<end>\d+)$",
        RegexOptions.Compiled);

    public readonly int Begin = begin;
    public readonly int End = end;
    public readonly int Interval = interval;
    public readonly string Name = name;

    /// <summary>包含两端的帧数，例如 0-3 → 4 帧。</summary>
    public int Length => End - Begin + 1;

    /// <summary>解析失败返回 null（格式不符）。</summary>
    public static AcsClip? CreateFromFormat(string format)
    {
        var match = _acsFormat.Match(format);
        if (!match.Success) {
            return null;
        }

        // TryParse 失败时给合理默认，避免坏文件名直接崩
        if (!int.TryParse(match.Groups["interval"].Value, out var interval)) {
            interval = 66;
        }

        if (!int.TryParse(match.Groups["begin"].Value, out var begin)) {
            begin = 0;
        }

        if (!int.TryParse(match.Groups["end"].Value, out var end)) {
            end = begin;
        }

        return new(match.Groups["name"].Value, interval, begin, end);
    }
}

/// <summary>
/// 从角色的 suffixes 字典里，选出当前应播放的 ACS <see cref="SpriteData"/>。
///
/// 热路径优化：常用键（idle/move/combat/… 以及 snow.* / married.*）全部是 const 字符串，
/// 避免 $"_acs_{state}" 这类每帧插值分配。
/// 自定义 override 前缀仍会拼接一次（少见路径）。
/// </summary>
public static class AcsController
{
    /// <summary>
    /// 内部占位后缀：用来标记「这个 replacer 已走 ACS 规范化」，
    /// 本身不是可播放 clip（Value 常为 null）。查询真实 clip 时要忽略它。
    /// </summary>
    public const string ReservedSuffix = "__acs_internal_reserved__";

    // ----- 基础状态键（无前缀）-----
    private const string KeyIdle = "_acs_idle";
    private const string KeyMove = "_acs_move";
    private const string KeyCombat = "_acs_combat";
    private const string KeyCombatSword = "_acs_combat.sword";
    private const string KeyCombatPolearm = "_acs_combat.polearm";
    private const string KeyCombatStaff = "_acs_combat.staff";
    private const string KeyWet = "_acs_wet";
    private const string KeyDrunk = "_acs_drunk";
    private const string KeyConfused = "_acs_confused";
    private const string KeyBlind = "_acs_blind";
    private const string KeyGreet = "_acs_greet";

    // ----- snow 前缀 -----
    private const string KeySnow = "_acs_snow";
    private const string KeySnowIdle = "_acs_snow.idle";
    private const string KeySnowMove = "_acs_snow.move";
    private const string KeySnowCombat = "_acs_snow.combat";
    private const string KeySnowCombatSword = "_acs_snow.combat.sword";
    private const string KeySnowCombatPolearm = "_acs_snow.combat.polearm";
    private const string KeySnowCombatStaff = "_acs_snow.combat.staff";
    private const string KeySnowWet = "_acs_snow.wet";
    private const string KeySnowDrunk = "_acs_snow.drunk";
    private const string KeySnowConfused = "_acs_snow.confused";
    private const string KeySnowBlind = "_acs_snow.blind";
    private const string KeySnowGreet = "_acs_snow.greet";

    // ----- married 前缀 -----
    private const string KeyMarried = "_acs_married";
    private const string KeyMarriedIdle = "_acs_married.idle";
    private const string KeyMarriedMove = "_acs_married.move";
    private const string KeyMarriedCombat = "_acs_married.combat";
    private const string KeyMarriedCombatSword = "_acs_married.combat.sword";
    private const string KeyMarriedCombatPolearm = "_acs_married.combat.polearm";
    private const string KeyMarriedCombatStaff = "_acs_married.combat.staff";
    private const string KeyMarriedWet = "_acs_married.wet";
    private const string KeyMarriedDrunk = "_acs_married.drunk";
    private const string KeyMarriedConfused = "_acs_married.confused";
    private const string KeyMarriedBlind = "_acs_married.blind";
    private const string KeyMarriedGreet = "_acs_married.greet";

    /// <summary>便捷重载：从角色种族默认 replacer.suffixes 取片。</summary>
    public static SpriteData? GetAcsClip(this Chara chara, bool snow = false)
    {
        return GetAcsClip(chara, snow, chara.sourceCard.replacer.suffixes);
    }

    /// <summary>
    /// 从指定 suffixes 查找 ACS 片段（PC 的 Package Skin 与种族字典可能不同）。
    ///
    /// 查找顺序（简化）：
    /// 1) combat：_acs_{prefix}.combat.{weapon} → _acs_{prefix}.combat → 无前缀同序
    ///    weapon 仅 sword/polearm/staff（主手）；单套战斗动画只用 _acs_combat
    /// 2) 其它状态：_acs_{prefix}.{state}
    /// 3) 仅当 state==idle 时，才允许旧式通用片 _acs_{prefix}
    /// 4) 再找无前缀 _acs_{state}
    /// 5) greet 找不到 → 清 greet 标记并重新解析
    /// 6) 其它非 idle 找不到 → 回退 idle
    /// </summary>
    public static SpriteData? GetAcsClip(this Chara chara, bool snow, Dictionary<string, SpriteData> suffixes)
    {
        string state = AcsStateResolver.GetState(chara);
        string? prefix = AcsStateResolver.GetPrefix(chara, snow);
        string? weaponKey = state == "combat"
            ? AcsStateResolver.GetMainHandCombatWeaponKey(chara)
            : null;

        if (prefix != null) {
            if (state == "combat") {
                if (TryGetCombat(suffixes, prefix, weaponKey, out var prefixedCombat)) {
                    return prefixedCombat;
                }
            }
            else if (TryGetPrefixed(suffixes, prefix, state, out var prefixedState)) {
                return prefixedState;
            }

            // 旧资源只有 _acs_snow 没有 _acs_snow.idle 时，仅 idle 可回退到通用前缀片
            if (state == "idle" && TryGetPrefixGeneric(suffixes, prefix, out var prefixGeneric)) {
                return prefixGeneric;
            }
        }

        if (state == "combat") {
            if (TryGetCombat(suffixes, prefix: null, weaponKey, out var baseCombat)) {
                return baseCombat;
            }
        }
        else if (suffixes.TryGetValue(StateKey(state), out var baseState)) {
            return baseState;
        }

        // greet 无资源：停掉问候，递归一次走非 greet 状态（StopGreet 后不会再进 greet）
        if (state == "greet") {
            AcsStateResolver.StopGreet(chara);
            return GetAcsClip(chara, snow, suffixes);
        }

        // 非 idle 缺专属片 → 回退 idle（含前缀 idle）
        if (state != "idle") {
            if (prefix != null) {
                if (TryGetPrefixed(suffixes, prefix, "idle", out var prefixedIdle)) {
                    return prefixedIdle;
                }

                if (TryGetPrefixGeneric(suffixes, prefix, out var prefixAsIdle)) {
                    return prefixAsIdle;
                }
            }

            if (suffixes.TryGetValue(KeyIdle, out var idleClip)) {
                return idleClip;
            }
        }

        return null;
    }

    /// <summary>外部 API：强制使用某 override 前缀（写入 mapStr）。</summary>
    public static void PlayAcsClip(this Card owner, string clipName)
    {
        owner.mapStr.Set("acs_override", clipName);
    }

    /// <summary>外部 API：取消 override。</summary>
    public static void StopAcsClip(this Card owner)
    {
        owner.mapStr.Remove("acs_override");
    }

    /// <summary>
    /// combat 选片：专用武器键 → 通用 combat。
    /// 单套战斗动画只放 <c>_acs_combat</c>；多武器再加 <c>_acs_combat.sword</c> 等。
    /// </summary>
    private static bool TryGetCombat(
        Dictionary<string, SpriteData> suffixes,
        string? prefix,
        string? weaponKey,
        out SpriteData? data)
    {
        if (weaponKey != null) {
            if (TryGetCombatWeapon(suffixes, prefix, weaponKey, out data)) {
                return true;
            }
        }

        if (prefix != null) {
            return TryGetPrefixed(suffixes, prefix, "combat", out data);
        }

        return suffixes.TryGetValue(KeyCombat, out data);
    }

    private static bool TryGetCombatWeapon(
        Dictionary<string, SpriteData> suffixes,
        string? prefix,
        string weaponKey,
        out SpriteData? data)
    {
        if (prefix == null) {
            return suffixes.TryGetValue(CombatWeaponKey(weaponKey), out data);
        }

        if (prefix == "snow") {
            return suffixes.TryGetValue(SnowCombatWeaponKey(weaponKey), out data);
        }

        if (prefix == "married") {
            return suffixes.TryGetValue(MarriedCombatWeaponKey(weaponKey), out data);
        }

        // 自定义 override：_acs_{prefix}.combat.{weapon}
        return suffixes.TryGetValue("_acs_" + prefix + ".combat." + weaponKey, out data);
    }

    private static string CombatWeaponKey(string weaponKey)
        => weaponKey switch {
            "sword" => KeyCombatSword,
            "polearm" => KeyCombatPolearm,
            "staff" => KeyCombatStaff,
            _ => KeyCombat,
        };

    private static string SnowCombatWeaponKey(string weaponKey)
        => weaponKey switch {
            "sword" => KeySnowCombatSword,
            "polearm" => KeySnowCombatPolearm,
            "staff" => KeySnowCombatStaff,
            _ => KeySnowCombat,
        };

    private static string MarriedCombatWeaponKey(string weaponKey)
        => weaponKey switch {
            "sword" => KeyMarriedCombatSword,
            "polearm" => KeyMarriedCombatPolearm,
            "staff" => KeyMarriedCombatStaff,
            _ => KeyMarriedCombat,
        };

    /// <summary>状态名 → 预建键；未知状态才拼接（极少走）。</summary>
    private static string StateKey(string state)
        => state switch {
            "idle" => KeyIdle,
            "move" => KeyMove,
            "combat" => KeyCombat,
            "wet" => KeyWet,
            "drunk" => KeyDrunk,
            "confused" => KeyConfused,
            "blind" => KeyBlind,
            "greet" => KeyGreet,
            _ => "_acs_" + state,
        };

    /// <summary>旧式「仅前缀」片：_acs_snow / _acs_married / _acs_{custom}。</summary>
    private static bool TryGetPrefixGeneric(
        Dictionary<string, SpriteData> suffixes,
        string prefix,
        out SpriteData? data)
    {
        if (prefix == "snow") {
            return suffixes.TryGetValue(KeySnow, out data);
        }

        if (prefix == "married") {
            return suffixes.TryGetValue(KeyMarried, out data);
        }

        return suffixes.TryGetValue("_acs_" + prefix, out data);
    }

    /// <summary>前缀 + 状态：_acs_snow.combat 等。</summary>
    private static bool TryGetPrefixed(
        Dictionary<string, SpriteData> suffixes,
        string prefix,
        string state,
        out SpriteData? data)
    {
        if (prefix == "snow") {
            return suffixes.TryGetValue(SnowStateKey(state), out data);
        }

        if (prefix == "married") {
            return suffixes.TryGetValue(MarriedStateKey(state), out data);
        }

        // 自定义 override：热路径外，允许一次字符串拼接
        return suffixes.TryGetValue("_acs_" + prefix + "." + state, out data);
    }

    private static string SnowStateKey(string state)
        => state switch {
            "idle" => KeySnowIdle,
            "move" => KeySnowMove,
            "combat" => KeySnowCombat,
            "wet" => KeySnowWet,
            "drunk" => KeySnowDrunk,
            "confused" => KeySnowConfused,
            "blind" => KeySnowBlind,
            "greet" => KeySnowGreet,
            _ => "_acs_snow." + state,
        };

    private static string MarriedStateKey(string state)
        => state switch {
            "idle" => KeyMarriedIdle,
            "move" => KeyMarriedMove,
            "combat" => KeyMarriedCombat,
            "wet" => KeyMarriedWet,
            "drunk" => KeyMarriedDrunk,
            "confused" => KeyMarriedConfused,
            "blind" => KeyMarriedBlind,
            "greet" => KeyMarriedGreet,
            _ => "_acs_married." + state,
        };
}
