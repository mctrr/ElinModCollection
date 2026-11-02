using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using ACS.API;
using ACS.Skin;

namespace ACS.Frame;

/// <summary>
/// 解析 ACS 文件名格式：_acs_{name}#{interval}_{begin}-{end}[@{scaleTex}]
/// 例：_acs_idle#66_0-3 → name=idle, 帧间隔 66ms, 帧 0..3
///      _acs_idle#41_0-31@400 → 同上，但渲染 PPU=400（图放大 4 倍，世界尺寸不变）
///
/// @N 是「贴图密度」标注：贴图放大了几倍就写几倍 PPU（100=1x，400=4x），
/// 让高分辨率图在游戏里占据与 1x 完全相同的世界尺寸（对齐 nightly 的 scaleTex 语义）。
/// 这是「文件名 → 结构化数据」的工具类；真正选哪一段播放见 <see cref="FrameChoice"/>。
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
public static class FrameChoice
{
    /// <summary>
    /// 内部占位后缀：用来标记「这个 replacer 已走 ACS 规范化」，
    /// 本身不是可播放 clip（Value 常为 null）。查询真实 clip 时要忽略它。
    /// </summary>
    public const string ReservedSuffix = "__acs_internal_reserved__";

    // ----- 基础状态键（无前缀）-----
    private const string KeyIdle = "_acs_idle";
    private const string KeySleep = "_acs_sleep";
    private const string KeyMove = "_acs_move";
    private const string KeyCombat = "_acs_combat";
    private const string KeyCombatBegin = "_acs_combat_begin";
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

    /// <summary>便捷重载：从角色当前皮肤的 suffixes 取片。</summary>
    public static SpriteData? GetAcsClip(this Chara chara, bool snow = false)
    {
        var suffixes = SkinIndex.ResolveSuffixes(chara);
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
    /// 1a) combat_begin：进入战斗的一次性起手，先于 magic/melee/range/combat；
    ///    缺片时清标记直接落 combat（不回 idle）
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
        string state = PlayState.GetState(chara);
        string? prefix = PlayState.GetPrefix(chara, snow);
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

        // combat_begin：进入战斗的一次性起手，播完一轮由 TryFinishOneShot 清除。
        // 缺片时清标记直接落 combat —— 不能走通用的「非 idle 缺片回 idle」兜底，
        // 否则没做起手的皮肤会在进战斗瞬间掉回待机。
        if (state == "combat_begin") {
            if (prefix != null
                && TryGetPrefixed(suffixes, prefix, "combat_begin", out var prefixedBegin)) {
                return prefixedBegin;
            }

            if (!lockToPrefix && suffixes.TryGetValue(KeyCombatBegin, out var baseBegin)) {
                return baseBegin;
            }

            // 只清播放标记、保留 seen：本次战斗不再重播，也不会每帧重跑这段选片
            PlayState.StopCombatBegin(chara);
            state = "combat";
        }

        string? weaponKey = state == "combat"
            ? PlayState.GetMainHandCombatWeaponKey(chara)
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
        if (PlayState.IsEmoteActive(chara)
            && PlayState.IsEmoteClipState(state)) {
            PlayState.StopEmote(chara);
            return GetAcsClip(chara, snow, suffixes);
        }

        if (state == "greet" || state.StartsWith("greet")) {
            PlayState.StopGreet(chara);
            return GetAcsClip(chara, snow, suffixes);
        }

        // revive 无资源：停掉复活动作，递归一次走其它状态
        if (state == "revive") {
            PlayState.StopRevive(chara);
            return GetAcsClip(chara, snow, suffixes);
        }

        if (state == "hurt") {
            PlayState.StopHurt(chara);
            return GetAcsClip(chara, snow, suffixes);
        }

        // emote / emote.* missing: stop one-shot and resolve again
        if (PlayState.IsEmoteState(state)) {
            PlayState.StopEmote(chara);
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
            "sleep" => KeySleep,
            "move" => KeyMove,
            "combat" => KeyCombat,
            "combat_begin" => KeyCombatBegin,
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

    // ==================== 静态战斗图 _bcs_combat ====================

    private const string StaticMapFileName = "bcs_static_map.ini";
    private const string StaticMapSection = "combat";

    /// <summary>
    /// <b>唯一一张</b>映射表：base skin id → 外来静态战斗皮肤 id。
    /// 以前「建映射」（StaticCombatMap）与「应用映射」（StaticCombatMapApply）是两个类、
    /// 两张表、两个 Invalidate —— 没有第二种实现，就不该单开一层，现在并回这里。
    /// </summary>
    private static readonly Dictionary<string, string> StaticCombatByBase =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>已读入的 ini 路径 → 上次读到的写入时间（判要不要重读）。</summary>
    private static readonly Dictionary<string, DateTime> StaticCombatFiles =
        new(StringComparer.OrdinalIgnoreCase);

    private static bool _staticCombatHadFile;

    /// <summary>
    /// 同一个 source 查不到时<b>只报一次</b>。<c>ListSkins()</c> 在读档 / 切区域 / 每次打开
    /// 换肤界面都会重跑，一次坏映射原本会刷几十行同样的告警。
    /// </summary>
    private static readonly Dictionary<string, string> StaticCombatWarned = new(StringComparer.Ordinal);

    /// <summary>素材换了 / <c>ACS.ReloadAcs</c> 后重置：让 bad 映射能重新报一次、ini 重新读。</summary>
    internal static void InvalidateStaticCombatMap()
    {
        StaticCombatFiles.Clear();
        _staticCombatHadFile = false;
        StaticCombatWarned.Clear();
    }

    /// <summary>
    /// 把 <c>[combat]</c> 映射写进 <paramref name="dictSkins"/> 的 <c>_bcs_combat</c> 槽。
    /// 原生 <c>{base}_bcs_combat</c> 永远优先，映射只补缺。
    /// </summary>
    /// <returns>新注入的 (baseId, combatData) 对，供 ListSkins 广播给已加载的 NPC 运行时实例。</returns>
    internal static List<(string BaseId, SpriteData Data)> ApplyStaticCombatMap(
        Dictionary<string, SpriteReplacer> dictSkins)
    {
        var applied = new List<(string BaseId, SpriteData Data)>();
        if (dictSkins is null) {
            return applied;
        }

        RefreshStaticCombatMap(CollectStaticSkinDirs(dictSkins));
        if (StaticCombatByBase.Count == 0) {
            return applied;
        }

        foreach (var pair in StaticCombatByBase) {
            string baseId = pair.Key;
            string sourceId = pair.Value;
            if (baseId.IsEmpty() || sourceId.IsEmpty()) {
                continue;
            }

            try {
                if (dictSkins.TryGetValue(baseId, out var existingBase)
                    && HasStaticCombat(existingBase)) {
                    continue;
                }

                if (!dictSkins.TryGetValue(sourceId, out var sourceSkin) || sourceSkin is null) {
                    WarnStaticCombatSourceMissingOnce(sourceId, baseId);
                    continue;
                }

                SpriteData? combatData = GetBaseSpriteData(sourceSkin);
                if (combatData is null || combatData.path.IsEmpty()) {
                    AcsMod.Warn($"static combat map source '{sourceId}' has no sprite data for base '{baseId}'");
                    continue;
                }

                if (!dictSkins.TryGetValue(baseId, out var baseSkin) || baseSkin is null) {
                    baseSkin = new SpriteReplacer {
                        data = combatData,
                    };
                    baseSkin.suffixes[AcsSuffixes.BaseKey] = combatData;
                    baseSkin.suffixes[AcsSuffixes.StaticCombat] = combatData;
                    dictSkins[baseId] = baseSkin;
                    applied.Add((baseId, combatData));
                    AcsMod.Log($"skin '{baseId}' created from map source '{sourceId}' with static combat");
                    continue;
                }

                if (baseSkin.suffixes is null || HasStaticCombat(baseSkin)) {
                    continue;
                }

                baseSkin.suffixes[AcsSuffixes.StaticCombat] = combatData;
                applied.Add((baseId, combatData));
                AcsMod.Log($"skin '{baseId}' static combat from map source '{sourceId}'");
            }
            catch (Exception ex) {
                AcsMod.Warn($"static combat map apply '{baseId}'<-'{sourceId}': {ex.Message}");
            }
        }

        return applied;
    }

    /// <summary>
    /// ini 变了才重读。<paramref name="extraSkinDirs"/> = 当前皮肤表里出现过的目录
    /// （Package 的 <c>Skin\</c> 等），用来兜住不在 Custom / 包根下的皮肤。
    /// </summary>
    private static void RefreshStaticCombatMap(IEnumerable<string>? extraSkinDirs)
    {
        try {
            var paths = CollectStaticMapPaths(extraSkinDirs);
            if (paths.Count == 0) {
                if (StaticCombatByBase.Count > 0 || _staticCombatHadFile) {
                    StaticCombatByBase.Clear();
                    StaticCombatFiles.Clear();
                    _staticCombatHadFile = false;
                    AcsMod.Log($"static combat map cleared (no {StaticMapFileName})");
                }

                return;
            }

            if (!StaticCombatNeedsReload(paths)) {
                return;
            }

            StaticCombatByBase.Clear();
            StaticCombatFiles.Clear();
            int totalRows = 0;
            int skippedDup = 0;

            foreach (var path in paths) {
                var writeUtc = File.GetLastWriteTimeUtc(path);
                skippedDup += ParseStaticCombatFile(path, ref totalRows);
                StaticCombatFiles[path] = writeUtc;
            }

            _staticCombatHadFile = true;
            AcsMod.Log($"static combat map loaded: {totalRows} combat entrie(s) from {paths.Count} file(s)"
                + (skippedDup > 0 ? $", {skippedDup} duplicate(s) skipped" : string.Empty));
        }
        catch (Exception ex) {
            AcsMod.Warn($"static combat map refresh: {ex.Message}");
        }
    }

    private static bool StaticCombatNeedsReload(List<string> paths)
    {
        if (StaticCombatFiles.Count != paths.Count) {
            return true;
        }

        foreach (var path in paths) {
            if (!StaticCombatFiles.TryGetValue(path, out var prevUtc)) {
                return true;
            }

            if (File.GetLastWriteTimeUtc(path) != prevUtc) {
                return true;
            }
        }

        return false;
    }

    private static List<string> CollectStaticMapPaths(IEnumerable<string>? extraSkinDirs)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAddDir(string? dir)
        {
            if (dir.IsEmpty()) {
                return;
            }

            string full;
            try {
                full = Path.GetFullPath(Path.Combine(dir, StaticMapFileName));
            }
            catch (Exception) {
                return;
            }

            if (!seen.Add(full)) {
                return;
            }

            if (File.Exists(full)) {
                paths.Add(full);
            }
        }

        // 1) Custom/Skin 先加载：同名 base 首个写入者胜，所以 Custom 排在包前面。
        try {
            string custom = CorePath.custom;
            if (!custom.IsEmpty()) {
                TryAddDir(Path.Combine(custom, "Skin"));
            }
        }
        catch (Exception) {
            // ignore
        }

        // 2) 已激活包的 Skin 目录（只有 ini 的兼容包在 dictSkins 里没有 png）。
        try {
            var mods = ModManager.Instance;
            var packages = mods?.packages;
            if (packages is not null) {
                foreach (var pkg in packages) {
                    if (pkg is null || !pkg.activated || pkg.dirInfo is null) {
                        continue;
                    }

                    TryAddDir(Path.Combine(pkg.dirInfo.FullName, "Skin"));
                }
            }
        }
        catch (Exception) {
            // ModManager 未就绪
        }

        // 3) 从当前皮肤表推出来的目录。
        if (extraSkinDirs is not null) {
            foreach (var dir in extraSkinDirs) {
                TryAddDir(dir);
            }
        }

        return paths;
    }

    /// <returns>本文件里被跳过的重复 base 行数。</returns>
    private static int ParseStaticCombatFile(string path, ref int totalRows)
    {
        string? section = null;
        int skippedDup = 0;
        int fileRows = 0;

        foreach (var raw in File.ReadAllLines(path)) {
            string line = raw.Trim();
            if (line.Length == 0) {
                continue;
            }

            if (line[0] == ';' || line[0] == '#' || line.StartsWith("//", StringComparison.Ordinal)) {
                continue;
            }

            if (line[0] == '[' && line.EndsWith("]", StringComparison.Ordinal)) {
                section = line.Substring(1, line.Length - 2).Trim();
                continue;
            }

            // v1 只吃 [combat]；snow/wet 等留给以后。
            if (!string.Equals(section, StaticMapSection, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq <= 0) {
                continue;
            }

            string baseId = NormalizeStaticMapId(line.Substring(0, eq));
            string sourceId = NormalizeStaticMapId(line.Substring(eq + 1));
            if (baseId.IsEmpty() || sourceId.IsEmpty()) {
                continue;
            }

            if (string.Equals(baseId, sourceId, StringComparison.OrdinalIgnoreCase)) {
                AcsMod.Warn($"static combat map skip self-map '{baseId}' in {path}");
                continue;
            }

            if (StaticCombatByBase.ContainsKey(baseId)) {
                skippedDup++;
                AcsMod.Warn($"static combat map duplicate base '{baseId}' -> keep '{StaticCombatByBase[baseId]}', skip '{sourceId}' ({path})");
                continue;
            }

            StaticCombatByBase[baseId] = sourceId;
            fileRows++;
            totalRows++;
        }

        if (fileRows > 0) {
            AcsMod.Log($"static combat map: {fileRows} from {path}");
        }

        return skippedDup;
    }

    /// <summary>
    /// ini 里手写的皮肤名：去引号/空白，再走 <see cref="SkinIndex.NormalizeId"/> 的<b>同一套</b>
    /// 归一（去目录 / 扩展名 / <c>#N</c> / <c>_acs_*</c> / <c>_bcs_combat</c>）。
    /// 以前这里另有一份弱化版归一（只去目录和扩展名），是 F6 的第二份实现。
    /// </summary>
    private static string NormalizeStaticMapId(string id)
    {
        if (id.IsEmpty()) {
            return string.Empty;
        }

        return SkinIndex.NormalizeId(id.Trim().Trim('"'));
    }

    /// <summary>从当前皮肤表推出所有皮肤 png 所在目录（去重）。</summary>
    private static List<string> CollectStaticSkinDirs(Dictionary<string, SpriteReplacer> dictSkins)
    {
        var dirs = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in dictSkins) {
            var skin = kv.Value;
            SpriteData? baseData = SkinIndex.BaseSheet(skin);
            string? path = baseData?.path ?? skin?.data?.path;
            if (path.IsEmpty()) {
                continue;
            }

            try {
                string? dir = Path.GetDirectoryName(path);
                if (dir.IsEmpty()) {
                    continue;
                }

                string full = Path.GetFullPath(dir);
                if (seen.Add(full)) {
                    dirs.Add(full);
                }
            }
            catch (Exception) {
                // 坏路径跳过
            }
        }

        return dirs;
    }

    private static bool HasStaticCombat(SpriteReplacer? skin)
    {
        if (skin?.suffixes is null) {
            return false;
        }

        return skin.suffixes.TryGetValue(AcsSuffixes.StaticCombat, out var data)
            && data is not null
            && !data.path.IsEmpty();
    }

    private static SpriteData? GetBaseSpriteData(SpriteReplacer skin)
    {
        if (skin.suffixes is not null
            && skin.suffixes.TryGetValue(AcsSuffixes.BaseKey, out var baseData)
            && baseData is not null
            && !baseData.path.IsEmpty()) {
            return baseData;
        }

        if (skin.data is not null && !skin.data.path.IsEmpty()) {
            return skin.data;
        }

        return null;
    }

    private static void WarnStaticCombatSourceMissingOnce(string sourceId, string firstBaseId)
    {
        if (StaticCombatWarned.ContainsKey(sourceId)) {
            return;
        }

        StaticCombatWarned[sourceId] = firstBaseId;
        AcsMod.Warn(
            $"static combat map source '{sourceId}' not found (first base '{firstBaseId}')"
            + " — 源皮肤名与 bcs_static_map.ini 不一致；请核对名字（含多余空格）"
            + " 或把该行从 ini 里去掉");
    }
}
