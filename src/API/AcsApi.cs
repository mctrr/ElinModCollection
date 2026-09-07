using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ACS.API;

/// <summary>
/// 解析 ACS 文件名格式：_acs_{name}#{interval}_{begin}-{end}[@{scaleTex}]
/// 例：_acs_idle#66_0-3 → name=idle, 帧间隔 66ms, 帧 0..3
///      _acs_idle#41_0-31@400 → 同上，但渲染 PPU=400（图放大 4 倍，世界尺寸不变）
///
/// @N 是「贴图密度」标注：贴图放大了几倍就写几倍 PPU（100=1x，400=4x），
/// 让高分辨率图在游戏里占据与 1x 完全相同的世界尺寸（对齐 nightly 的 scaleTex 语义）。
/// 这是「文件名 → 结构化数据」的工具类；真正选哪一段播放见 <see cref="AcsController"/>。
/// </summary>
public class AcsClip(string name, int interval, int begin, int end, int scaleTex = 100)
{
    /// <summary>
    /// Compiled 正则：只编译一次，多次 Match 更快。
    /// (?&lt;name&gt;...) 是命名捕获组，后面用 Groups["name"] 取。
    /// </summary>
    private static readonly Regex _acsFormat = new(
        @"^_acs_(?<name>[^#]+)#(?<interval>\d+)_(?<begin>\d+)-(?<end>\d+)(?:@(?<scaleTex>\d+))?$",
        RegexOptions.Compiled);

    public readonly int Begin = begin;
    public readonly int End = end;
    public readonly int Interval = interval;
    public readonly string Name = name;

    /// <summary>渲染 PPU（像素/世界单位）；文件名 @N 标注，缺省 100（=1x）。</summary>
    public readonly int ScaleTex = scaleTex > 0 ? scaleTex : 100;

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

        int scaleTex = 100;
        var scaleGroup = match.Groups["scaleTex"];
        if (scaleGroup.Success
            && int.TryParse(scaleGroup.Value, out var parsed)
            && parsed > 0) {
            scaleTex = parsed;
        }

        return new(match.Groups["name"].Value, interval, begin, end, scaleTex);
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
    private const string KeyGreet1 = "_acs_greet1";
    private const string KeyGreet2 = "_acs_greet2";
    private const string KeyGreet3 = "_acs_greet3";
    private const string KeyGreet4 = "_acs_greet4";
    private const string KeyEat = "_acs_eat";
    private const string KeyMagic = "_acs_magic";
    private const string KeyMelee = "_acs_melee";
    private const string KeyRanged = "_acs_range";
    private const string KeyAtk = "_acs_atk";
    private const string KeyCmove = "_acs_cmove";
    private const string KeyHurt = "_acs_hurt";
    private const string KeyRevive = "_acs_revive";
    private const string KeyEmote = "_acs_emote";

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
    private const string KeySnowGreet1 = "_acs_snow.greet1";
    private const string KeySnowGreet2 = "_acs_snow.greet2";
    private const string KeySnowGreet3 = "_acs_snow.greet3";
    private const string KeySnowGreet4 = "_acs_snow.greet4";
    private const string KeySnowEat = "_acs_snow.eat";
    private const string KeySnowMagic = "_acs_snow.magic";
    private const string KeySnowMelee = "_acs_snow.melee";
    private const string KeySnowRanged = "_acs_snow.range";
    private const string KeySnowCmove = "_acs_snow.cmove";
    private const string KeySnowHurt = "_acs_snow.hurt";
    private const string KeySnowRevive = "_acs_snow.revive";
    private const string KeySnowEmote = "_acs_snow.emote";

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
    private const string KeyMarriedGreet1 = "_acs_married.greet1";
    private const string KeyMarriedGreet2 = "_acs_married.greet2";
    private const string KeyMarriedGreet3 = "_acs_married.greet3";
    private const string KeyMarriedGreet4 = "_acs_married.greet4";
    private const string KeyMarriedEat = "_acs_married.eat";
    private const string KeyMarriedMagic = "_acs_married.magic";
    private const string KeyMarriedMelee = "_acs_married.melee";
    private const string KeyMarriedRanged = "_acs_married.range";
    private const string KeyMarriedCmove = "_acs_married.cmove";
    private const string KeyMarriedHurt = "_acs_married.hurt";
    private const string KeyMarriedRevive = "_acs_married.revive";
    private const string KeyMarriedEmote = "_acs_married.emote";

    /// <summary>便捷重载：从角色种族默认 replacer.suffixes 取片。</summary>
    public static SpriteData? GetAcsClip(this Chara chara, bool snow = false)
    {
        var suffixes = AcsSuffixes.Resolve(chara);
        if (suffixes is null) {
            return null;
        }

        return GetAcsClip(chara, snow, suffixes);
    }

    /// <summary>
    /// 从指定 suffixes 查找 ACS 片段（PC 的 Package Skin 与种族字典可能不同）。
    ///
    /// 查找顺序（简化）：
    /// 1) combat：_acs_{prefix}.combat.{weapon} → _acs_{prefix}.combat → 无前缀同序
    ///    weapon 仅 sword/polearm/staff（主手）；单套战斗动画只用 _acs_combat
    /// 1b) magic：施法窗口（优先级高于 combat）先取 _acs_{prefix}.magic，
    ///    缺片时按 combat 选片回退
    /// 1c) melee：近战窗口（低于 magic、高于 combat）先取 _acs_{prefix}.melee，
    ///    缺片时按 combat 选片回退
    /// 1d) range：远程窗口先取 _acs_{prefix}.range，缺片回退 combat
    /// 1e) cmove：战斗中移动先取 _acs_{prefix}.cmove，缺片依次回退 move → combat
    /// 2) 其它状态：_acs_{prefix}.{state}
    /// 3) 仅当 state==idle 时，才允许旧式通用片 _acs_{prefix}
    /// 4) 再找无前缀 _acs_{state}（acs_override 前缀不走这一步，避免缺 move 时播原皮行走）
    /// 5) greet / greetN / revive / emote 找不到 → 清一次性标记并重新解析
    /// 6) 其它非 idle 找不到 → 回退 idle（当前皮），绝不回落原皮
    /// </summary>
    public static SpriteData? GetAcsClip(this Chara chara, bool snow, Dictionary<string, SpriteData> suffixes)
    {
        string state = AcsStateResolver.GetState(chara);
        string? prefix = AcsStateResolver.GetPrefix(chara, snow);
        // snow/married 是状态修饰；acs_override 是整套换肤，缺片只能回当前前缀 idle
        bool lockToPrefix = prefix != null && prefix != "snow" && prefix != "married";

        // magic 低于 revive/emote/hurt，高于 melee/combat；缺专属片时回退通用 atk，再按 combat 选片回退
        if (state == "magic") {
            if (prefix != null
                && TryGetPrefixed(suffixes, prefix, "magic", out var prefixedMagic)) {
                return prefixedMagic;
            }

            if (!lockToPrefix && suffixes.TryGetValue(KeyMagic, out var baseMagic)) {
                return baseMagic;
            }

            // 缺 magic 专属片：先试通用攻击片 atk（魔法也是攻击），再落 combat
            if (TryGetAtk(suffixes, prefix, lockToPrefix, out var magicAtk)) {
                return magicAtk;
            }

            state = "combat";
        }

        // melee 低于 magic、高于 combat；缺专属片时回退通用 atk，再按 combat 选片回退
        if (state == "melee") {
            if (prefix != null
                && TryGetPrefixed(suffixes, prefix, "melee", out var prefixedMelee)) {
                return prefixedMelee;
            }

            if (!lockToPrefix && suffixes.TryGetValue(KeyMelee, out var baseMelee)) {
                return baseMelee;
            }

            // 缺 melee 专属片：先试通用攻击片 atk（挥砍也是攻击），再落 combat
            if (TryGetAtk(suffixes, prefix, lockToPrefix, out var meleeAtk)) {
                return meleeAtk;
            }

            state = "combat";
        }

        if (state == "range") {
            if (prefix != null
                && TryGetPrefixed(suffixes, prefix, "range", out var prefixedRanged)) {
                return prefixedRanged;
            }

            if (!lockToPrefix && suffixes.TryGetValue(KeyRanged, out var baseRanged)) {
                return baseRanged;
            }

            state = "combat";
        }

        if (state == "cmove") {
            if (prefix != null
                && TryGetPrefixed(suffixes, prefix, "cmove", out var prefixedCmove)) {
                return prefixedCmove;
            }

            if (!lockToPrefix && suffixes.TryGetValue(KeyCmove, out var baseCmove)) {
                return baseCmove;
            }

            // 缺 cmove 片：优先用 move 替代（战斗中移动仍是移动，步态比静止 combat 合理）。
            // 与 cmove 同规则：先试前缀专属片，再试无前缀通用片。
            if (prefix != null
                && TryGetPrefixed(suffixes, prefix, "move", out var prefixedMove)) {
                return prefixedMove;
            }

            if (!lockToPrefix && suffixes.TryGetValue(KeyMove, out var baseMove)) {
                return baseMove;
            }

            // move 也没有 → 落 combat（持械姿态兜底，保证战斗中不空片）
            state = "combat";
        }

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

        // override 前缀缺片：不要拿无前缀 _acs_move/_acs_combat（那是原皮）
        if (!lockToPrefix) {
            if (state == "combat") {
                if (TryGetCombat(suffixes, prefix: null, weaponKey, out var baseCombat)) {
                    return baseCombat;
                }
            }
            else if (suffixes.TryGetValue(StateKey(state), out var baseState)) {
                return baseState;
            }
        }

        // greet / greetN 无资源：停掉问候，递归一次走非 greet 状态
        // PC emote may lock greet / greetN; missing clip must stop emote, not greet.
        if (AcsStateResolver.IsEmoteActive(chara)
            && AcsStateResolver.IsEmoteClipState(state)) {
            AcsStateResolver.StopEmote(chara);
            return GetAcsClip(chara, snow, suffixes);
        }

        if (state == "greet" || state.StartsWith("greet")) {
            AcsStateResolver.StopGreet(chara);
            return GetAcsClip(chara, snow, suffixes);
        }

        // revive 无资源：停掉复活动作，递归一次走其它状态
        if (state == "revive") {
            AcsStateResolver.StopRevive(chara);
            return GetAcsClip(chara, snow, suffixes);
        }

        if (state == "hurt") {
            AcsStateResolver.StopHurt(chara);
            return GetAcsClip(chara, snow, suffixes);
        }

        // emote / emote.* missing: stop one-shot and resolve again
        if (AcsStateResolver.IsEmoteState(state)) {
            AcsStateResolver.StopEmote(chara);
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

    /// <summary>
    /// 通用攻击片 atk：magic/melee 缺专属片时的共享回退。
    /// 同其余状态：先试前缀专属片（snow/married/自定义 override），再试无前缀通用片。
    /// </summary>
    private static bool TryGetAtk(
        Dictionary<string, SpriteData> suffixes,
        string? prefix,
        bool lockToPrefix,
        out SpriteData? data)
    {
        if (prefix != null && TryGetPrefixed(suffixes, prefix, "atk", out data)) {
            return true;
        }

        if (!lockToPrefix && suffixes.TryGetValue(KeyAtk, out data)) {
            return true;
        }

        data = null;
        return false;
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
            "greet1" => KeyGreet1,
            "greet2" => KeyGreet2,
            "greet3" => KeyGreet3,
            "greet4" => KeyGreet4,
            "eat" => KeyEat,
            "magic" => KeyMagic,
            "melee" => KeyMelee,
            "range" => KeyRanged,
            "cmove" => KeyCmove,
            "hurt" => KeyHurt,
            "revive" => KeyRevive,
            "emote" => KeyEmote,
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
            "greet1" => KeySnowGreet1,
            "greet2" => KeySnowGreet2,
            "greet3" => KeySnowGreet3,
            "greet4" => KeySnowGreet4,
            "eat" => KeySnowEat,
            "magic" => KeySnowMagic,
            "melee" => KeySnowMelee,
            "range" => KeySnowRanged,
            "cmove" => KeySnowCmove,
            "hurt" => KeySnowHurt,
            "revive" => KeySnowRevive,
            "emote" => KeySnowEmote,
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
            "greet1" => KeyMarriedGreet1,
            "greet2" => KeyMarriedGreet2,
            "greet3" => KeyMarriedGreet3,
            "greet4" => KeyMarriedGreet4,
            "eat" => KeyMarriedEat,
            "magic" => KeyMarriedMagic,
            "melee" => KeyMarriedMelee,
            "range" => KeyMarriedRanged,
            "cmove" => KeyMarriedCmove,
            "hurt" => KeyMarriedHurt,
            "revive" => KeyMarriedRevive,
            "emote" => KeyMarriedEmote,
            _ => "_acs_married." + state,
        };
}
