namespace ACS.API;

using System.Collections.Generic;
using UnityEngine;

public static class AcsStateResolver
{

    // ==================== 1. 基础状态入口（GetState 优先级） ====================

    /// <summary>
    /// 基础状态总入口。判定分支顺序即优先级（由高到低）：
    /// revive > emote(仅非移动时) > hurt > magic > melee > range
    /// > cmove(combat 状态下移动) > combat > move > greet(NPC) > eat
    /// > wet > drunk > confused > blind > idle。
    /// 此注释须与下方 if 分支逐条一致；调整优先级时先改分支再同步此处。
    /// </summary>
    public static string GetState(Chara chara)
    {
        // 复活是一次性动作，优先于战斗/移动，播完一轮自动清除
        if (IsReviveActive(chara)) {
            return "revive";
        }

        // 按钮触发的 emote 优先级高于 combat，保证按钮姿势可见。
        // 走动会打断它；战斗不会（战斗中点表情仍能播）。
        if (IsEmoteActive(chara)) {
            if (IsMoving(chara)) {
                StopEmote(chara);
            }
            else {
                return GetEmoteState(chara);
            }
        }

        if (IsHurtActive(chara)) {
            return "hurt";
        }

        // 魔法 / 近战 / 远程窗口 > 战斗中移动 > 普通 combat
        if (IsMagicCombatActive(chara)) {
            return "magic";
        }

        if (IsMeleeActive(chara)) {
            return "melee";
        }

        if (IsRangedActive(chara)) {
            return "range";
        }

        if (IsCombatVisual(chara)) {
            if (IsMoving(chara)) {
                return "cmove";
            }

            return "combat";
        }

        // 资源名 _acs_move；PC 用轴/寻路粘滞，NPC 只看格子插值 isMoving
        if (IsMoving(chara)) {
            return "move";
        }

        if (!chara.IsPC && IsGreetActive(chara)) {
            return GetGreetState(chara);
        }

        if (IsEatActive(chara)) {
            return "eat";
        }

        if (chara.isWet || chara.wasInWater) {
            return "wet";
        }

        if (chara.isDrunk) {
            return "drunk";
        }

        if (chara.isConfused) {
            return "confused";
        }

        if (chara.isBlind) {
            return "blind";
        }

        return "idle";
    }

    // ==================== 2. combat：主动攻击窗口与敌对扫描 ====================

    /// <summary>
    /// 是否应播 combat ACS。
    /// <list type="bullet">
    /// <item>自动战斗：原版 <see cref="Chara.IsInCombat"/>（ai is GoalCombat）</item>
    /// <item>PC 主动窗口：攻击或换主手写入 combatCount=5 + 标记；到期后 Tick 用 CanSee/IsHostile 检测 1 次视野</item>
    /// <item>NPC：原版 combatCount（近战等会写 10）</item>
    /// </list>
    /// </summary>
    public static bool IsCombatVisual(Chara chara)
    {
        if (chara is null) {
            return false;
        }

        // 自动战斗（GoalCombat）
        if (chara.IsInCombat) {
            return true;
        }

        // PC：仅主动窗口（combatCount 由原版 Tick 递减）
        if (chara.IsPC) {
            return IsActiveCombatWindow(chara);
        }

        // NPC：跟原版武器显隐同一信号
        return chara.combatCount > 0;
    }

    /// <summary>
    /// PC 主动攻击后 combat ACS 保持回合数。
    /// 复用原版 <see cref="Chara.combatCount"/>（Chara.Tick 每回合 -1）。
    /// </summary>
    public const int CombatHoldTurns = 5;

    /// <summary>
    /// 标记本段 combatCount 来自 ACS 主动窗口（攻击或换主手），非被动挨打。
    /// 仅作开关，倒计时用 combatCount。
    /// </summary>
    public const string CombatFlagKey = "acs_combat";

    /// <summary>PC 主动攻击或换主手：写入 combatCount=5 并打标记。</summary>
    public static void MarkActiveCombat(Chara chara)
    {
        if (chara is null || !chara.IsPC) {
            return;
        }

        chara.combatCount = CombatHoldTurns;
        chara.mapStr.Set(CombatFlagKey, "1");
    }

    /// <summary>结束主动攻击 combat 窗口。</summary>
    public static void ClearCombat(Chara chara)
    {
        chara?.mapStr.Remove(CombatFlagKey);
    }

    /// <summary>
    /// Chara.Tick 已把 combatCount-- 之后调用：
    /// 主动攻击窗口到期时，用原版 CanSee + IsHostile 检测 1 次视野内敌对；
    /// 有则再续 5 回合，无则清标记。
    /// </summary>
    public static void OnPcTickCombatWindow(Chara pc)
    {
        if (pc is null || !pc.IsPC) {
            return;
        }

        if (!pc.mapStr.ContainsKey(CombatFlagKey)) {
            return;
        }

        // 窗口内：继续播 combat
        if (pc.combatCount > 0) {
            return;
        }

        // 刚好耗尽：检测 1 次视野
        if (HasHostileInFov(pc)) {
            pc.combatCount = CombatHoldTurns;
            ClearMelee(pc);
            ClearRanged(pc);
            return;
        }

        ClearCombat(pc);
        ClearMelee(pc);
    }

    private static bool IsActiveCombatWindow(Chara pc)
    {
        if (!pc.mapStr.ContainsKey(CombatFlagKey)) {
            return false;
        }

        return pc.combatCount > 0;
    }

    /// <summary>
    /// 视野内是否有敌对。原版接口：
    /// <see cref="Chara.CanSee"/>（PC 用 fov.lastPoints）、
    /// <see cref="Chara.IsHostile(Chara)"/>。
    /// </summary>
    private static bool HasHostileInFov(Chara pc)
    {
        try {
            var map = EClass._map;
            if (map?.charas is null) {
                return false;
            }

            foreach (Chara c in map.charas) {
                if (c is null || c == pc || c.isDead) {
                    continue;
                }

                if (!c.IsAliveInCurrentZone || !c.ExistsOnMap) {
                    continue;
                }

                if (!pc.CanSee(c)) {
                    continue;
                }

                if (pc.IsHostile(c) || c.IsHostile(pc)) {
                    return true;
                }
            }
        }
        catch {
            // 主菜单 / 地图未就绪
        }

        return false;
    }

    // ==================== 3. magic（魔法攻击窗口） ====================

    /// <summary>
    /// magic（魔法攻击）手动标记键（mapStr）。与 combat 窗口同机制：
    /// <see cref="MarkActiveMagicCombat"/> 写入 combatCount + 该标记，
    /// 倒计时复用原版 <see cref="Chara.combatCount"/>（Chara.Tick 每回合 -1），
    /// 耗尽即自动清标记（见 <see cref="IsMagicCombatActive"/>）。
    /// </summary>
    public const string MagicCombatFlagKey = "acs_magic";

    /// <summary>
    /// 是否处于魔法攻击（magic）窗口。**优先级高于普通 combat**：
    /// <see cref="GetState"/> 先判 magic 再判 combat。
    ///
    /// 触发：<see cref="MarkActiveMagicCombat"/>（原版施放敌对法术时，见
    /// StatePatches 的 Chara.UseAbility 补丁）。非施法状态 / 窗口耗尽自动退出，
    /// 回落到普通 combat 或 idle。
    /// </summary>
    public static bool IsMagicCombatActive(Chara chara)
    {
        // 纯 flag 判定：一次性施法动画只由渲染播完清理（TryFinishOneShot）。
        // 不再依赖 combatCount——它会被脱离战斗 / 受击等原版逻辑清零，
        // 动画播到一半被掐断（表现为只播 2~3 帧）。
        if (chara is null) {
            return false;
        }

        return chara.mapStr.ContainsKey(MagicCombatFlagKey);
    }

    /// <summary>
    /// 开 magic 窗口：写入 combatCount=5 + 标记（PC / NPC 通用）。
    /// 持续施法会不断续期；停止施法 5 回合后自动结束。
    /// </summary>
    public static void MarkActiveMagicCombat(Chara chara)
    {
        if (chara is null || chara.isDead) {
            return;
        }

        chara.combatCount = CombatHoldTurns;
        // 值存触发时刻（版本戳）：渲染端据此识别“新的一次施法”，即使 clip 与上次同一
        // 对象（combat 回退共用图）也从第 0 帧重播，不会续旧帧序号造成秒播完/加速。
        chara.mapStr.Set(MagicCombatFlagKey, Time.realtimeSinceStartup.ToString("F3"));
        ClearMelee(chara);
        ClearRanged(chara);
    }

    /// <summary>立即结束 magic 窗口（测试 / 外部调用）。</summary>
    public static void ClearMagicCombat(Chara chara)
    {
        chara?.mapStr.Remove(MagicCombatFlagKey);
        ClearMagicFace(chara);
    }

    // ==================== 4. melee（近战窗口） ====================

    /// <summary>
    /// melee（近战攻击）手动标记键（mapStr）。与 combat 窗口同机制：
    /// <see cref="MarkActiveMelee"/> 写入 combatCount + 该标记，
    /// 倒计时复用原版 <see cref="Chara.combatCount"/>（Chara.Tick 每回合 -1），
    /// 耗尽即自动清标记（见 <see cref="IsMeleeActive"/>）。
    /// </summary>
    public const string MeleeFlagKey = "acs_melee";

    /// <summary>
    /// 是否处于近战（melee）攻击窗口。优先级低于 magic、高于 combat：
    /// <see cref="GetState"/> 先判 magic 再判 melee 再判 combat。
    ///
    /// 触发：<see cref="MarkActiveMelee"/>（原版 ActMelee.Attack，PC / NPC 通用）。
    /// 窗口耗尽后回落到普通 combat 或 idle。
    /// </summary>
    public static bool IsMeleeActive(Chara chara)
    {
        // 纯 flag 判定：一次性近战动画只由渲染播完清理（TryFinishOneShot）。
        // 不再依赖 combatCount——它会被脱离战斗 / 受击等原版逻辑清零，
        // 动画播到一半被掐断（表现为只播 2~3 帧）。
        if (chara is null) {
            return false;
        }

        return chara.mapStr.ContainsKey(MeleeFlagKey);
    }

    /// <summary>
    /// 开 melee 窗口：写入 combatCount=5 + 标记（PC / NPC 通用）。
    /// 连续攻击会不断续期；停止攻击 5 回合后自动结束。
    /// </summary>
    public static void MarkActiveMelee(Chara chara)
    {
        if (chara is null || chara.isDead) {
            return;
        }

        chara.combatCount = CombatHoldTurns;
        // 值存触发时刻（版本戳）：渲染端据此识别“新的一次攻击”，即使 clip 与上次同一
        // 对象（combat 回退共用图）也从第 0 帧重播，不会续旧帧序号造成秒播完/加速。
        chara.mapStr.Set(MeleeFlagKey, Time.realtimeSinceStartup.ToString("F3"));
        ClearMagicCombat(chara);
        ClearRanged(chara);
        if (chara.IsPC) {
            MarkActiveCombat(chara);
        }
    }

    /// <summary>立即结束 melee 窗口（测试 / 外部调用）。</summary>
    public static void ClearMelee(Chara chara)
    {
        chara?.mapStr.Remove(MeleeFlagKey);
        ClearMeleeFace(chara);
    }

    // ==================== 5. ranged（远程窗口） ====================

    public const string RangedFlagKey = "acs_ranged";

    public static bool IsRangedActive(Chara chara)
    {
        if (chara is null) {
            return false;
        }

        if (!chara.mapStr.ContainsKey(RangedFlagKey)) {
            return false;
        }

        if (chara.combatCount <= 0) {
            chara.mapStr.Remove(RangedFlagKey);
            ClearRangedFace(chara);
            return false;
        }

        return true;
    }

    public static void MarkActiveRanged(Chara chara)
    {
        if (chara is null || chara.isDead) {
            return;
        }

        chara.combatCount = CombatHoldTurns;
        chara.mapStr.Set(RangedFlagKey, "1");
        ClearMelee(chara);
        ClearMagicCombat(chara);
        if (chara.IsPC) {
            MarkActiveCombat(chara);
        }
    }

    public static void ClearRanged(Chara chara)
    {
        chara?.mapStr.Remove(RangedFlagKey);
        ClearRangedFace(chara);
    }

    // ==================== 6. hurt（受击一次性动作） ====================

    /// <summary>hurt 一次性动作标记（mapStr 时间戳），与 revive 同机制。</summary>
    private const string HurtFlagKey = "acs_hurt";

    public static bool IsHurtActive(Chara chara)
    {
        if (chara is null || !chara.mapStr.TryGetValue(HurtFlagKey, out string value)) {
            return false;
        }

        if (!float.TryParse(value, out float activateAt)) {
            return false;
        }

        return Time.realtimeSinceStartup >= activateAt;
    }

    public static void StartHurt(Chara chara)
    {
        if (chara is null || chara.isDead) {
            return;
        }

        // 受击打断当前攻击/施法动作：hurt 优先级最高，冲突时只播 hurt。
        // 若不清 melee/magic，hurt 播完 TryFinishOneShot 后会续播攻击（“先攻击又立刻受伤”）。
        ClearMagicCombat(chara);
        ClearMelee(chara);
        ClearRanged(chara);

        chara.mapStr.Set(HurtFlagKey, Time.realtimeSinceStartup.ToString());
    }

    public static void StopHurt(Chara chara)
    {
        chara?.mapStr.Remove(HurtFlagKey);
    }

    // ==================== 7. revive（复活一次性动作） ====================

    /// <summary>revive 一次性动作标记（mapStr 时间戳）。</summary>
    private const string ReviveFlagKey = "acs_revive";

    /// <summary>acs_revive 存激活时间戳；当前时间 >= 该值才算激活。</summary>
    public static bool IsReviveActive(Chara chara)
    {
        if (chara is null || !chara.mapStr.TryGetValue(ReviveFlagKey, out string value)) {
            return false;
        }

        if (!float.TryParse(value, out float activateAt)) {
            return false;
        }

        return Time.realtimeSinceStartup >= activateAt;
    }

    /// <summary>开始复活动作（立即激活，播完一轮自动清除）。</summary>
    public static void StartRevive(Chara chara)
    {
        if (chara is null) {
            return;
        }

        // 清掉可能残留的 emote/受击/攻击/施法标记：死亡后 mapStr 不清空，
        // 若残留会在 revive 播完后立刻补播一次攻击/受击动作。
        StopEmote(chara);
        StopHurt(chara);
        ClearMagicCombat(chara);
        ClearMelee(chara);
        ClearRanged(chara);
        StopGreet(chara);
        chara.mapStr.Set(ReviveFlagKey, Time.realtimeSinceStartup.ToString());
    }

    public static void StopRevive(Chara chara)
    {
        chara?.mapStr.Remove(ReviveFlagKey);
    }

    // ==================== 8. one-shot 动作播放生命周期（渲染端共用） ====================

    public static bool IsOneShotActive(Chara chara)
        => IsReviveActive(chara) || IsEmoteActive(chara)
           || IsHurtActive(chara) || IsMagicCombatActive(chara) || IsMeleeActive(chara)
           || IsGreetActive(chara);

    /// <summary>
    /// 当前激活的一次性动作“版本签名”（按 GetState 优先级取最高一个）。
    /// 渲染端用它判断是否“新一轮动作”：即使命中同一个 SpriteData（如 magic/melee
    /// 回退共用 combat 图），只要签名变化就从第 0 帧重播；否则续旧帧序号会瞬间播完。
    /// </summary>
    public static string? GetOneShotSignature(Chara? chara)
    {
        if (chara is null || chara.mapStr is null) {
            return null;
        }

        string? v;
        if (chara.mapStr.TryGetValue(ReviveFlagKey, out v) && !v.IsEmpty()) {
            return "rev:" + v;
        }

        // 顺序与 GetState 一致：emote(按钮) 高于 hurt/magic/melee
        if (chara.mapStr.TryGetValue(EmoteFlagKey, out v) && !v.IsEmpty()) {
            return "emo:" + v;
        }

        if (chara.mapStr.TryGetValue(HurtFlagKey, out v) && !v.IsEmpty()) {
            return "hurt:" + v;
        }

        if (chara.mapStr.TryGetValue(MagicCombatFlagKey, out v) && !v.IsEmpty()) {
            return "mag:" + v;
        }

        if (chara.mapStr.TryGetValue(MeleeFlagKey, out v) && !v.IsEmpty()) {
            return "mel:" + v;
        }

        if (chara.mapStr.TryGetValue(GreetVariantKey, out v) && !v.IsEmpty()) {
            return "greet:" + v;
        }

        return null;
    }

    public static bool TryFinishOneShot(Chara chara)
    {
        if (chara is null) {
            return false;
        }

        if (IsReviveActive(chara)) {
            StopRevive(chara);
            return true;
        }

        // emote(按钮) 与 GetState 同序：先清自己，避免把并发的 hurt/magic/melee 误清。
        if (IsEmoteActive(chara)) {
            StopEmote(chara);
            return true;
        }

        if (IsHurtActive(chara)) {
            StopHurt(chara);
            return true;
        }

        // magic（施法起手）：播完一轮即停、不循环；随后回 combat / idle。
        // 直接查 flag（纯标记，值=触发时刻版本戳）。
        if (chara.mapStr.ContainsKey(MagicCombatFlagKey)) {
            ClearMagicCombat(chara);
            return true;
        }

        // melee（近战 atk）：播完一轮即停、不循环；随后回 combat / idle。
        // 直接查 flag（纯标记，值=触发时刻版本戳）。
        if (chara.mapStr.ContainsKey(MeleeFlagKey)) {
            ClearMelee(chara);
            return true;
        }

        if (IsGreetActive(chara)) {
            StopGreet(chara);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 帧推进。revive / emote / greet 播到末帧时停在末帧并清除，不要绕回第 0 帧
    /// （否则会再闪开头几帧）。
    /// </summary>
    public static int AdvanceClipIndex(Chara chara, int frame, int index)
    {
        index++;
        if (frame <= 0) {
            return 0;
        }

        if (index < frame) {
            return index;
        }

        if (TryFinishOneShot(chara)) {
            return frame - 1;
        }

        return 0;
    }

    /// <summary>
    /// 原版 CardActor 在拿到 clip 后还会 spriteIndex++ / wrap。
    /// 若本帧会从末帧绕回 0，先结束一次性动作并冻在末帧。
    /// </summary>
    public static bool FreezeOneShotBeforeWrap(Chara chara, int frame, int index, float timer, float time)
    {
        if (chara is null || frame <= 1 || time <= 0f || !IsOneShotActive(chara)) {
            return false;
        }

        if (index < frame - 1 || timer + Core.delta < time) {
            return false;
        }

        TryFinishOneShot(chara);
        return true;
    }

    /// <summary>单帧 clip：立刻结束一次性动作。</summary>
    public static void StopOneShotIfLastFrame(Chara chara, int frame, int index)
    {
        if (chara is null || frame <= 0) {
            return;
        }

        if (frame <= 1 || index >= frame - 1) {
            TryFinishOneShot(chara);
        }
    }

    // ==================== 9. emote（表情一次性动作） ====================

    /// <summary>emote 一次性动作标记（mapStr 时间戳）。</summary>
    private const string EmoteFlagKey = "acs_emote";

    /// <summary>本次 emote 锁定的 clip 状态名：emote / emote.wave / ...</summary>
    private const string EmoteVariantKey = "acs_emote_n";

    public static bool IsEmoteState(string? state)
        => state == "emote" || (state is not null && state.StartsWith("emote."));

    public static bool IsGreetClipState(string? state)
        => state == "greet"
            || state == "greet1"
            || state == "greet2"
            || state == "greet3"
            || state == "greet4";

    public static bool IsEmoteClipState(string? state)
        => IsEmoteState(state) || IsGreetClipState(state);

    public static bool IsEmoteActive(Chara chara)
    {
        if (chara is null || !chara.mapStr.TryGetValue(EmoteFlagKey, out string value)) {
            return false;
        }

        if (!float.TryParse(value, out float activateAt)) {
            return false;
        }

        return Time.realtimeSinceStartup >= activateAt;
    }

    public static string GetEmoteState(Chara chara)
    {
        if (chara.mapStr.TryGetValue(EmoteVariantKey, out string variant)
            && !variant.IsEmpty()
            && IsEmoteState(variant)) {
            return variant;
        }

        return "emote";
    }

    /// <summary>
    /// 播放一次性 emote。state 为空时自动选唯一存在的片；
    /// 传 clip 名（emote / emote.wave / greet）则指定播放该片。
    /// </summary>
    public static bool StartEmote(Chara chara, string? state = null)
    {
        if (chara is null) {
            return false;
        }

        var states = ListEmoteStates(chara);
        if (states.Count == 0) {
            return false;
        }

        if (state.IsEmpty()) {
            if (states.Count != 1) {
                return false;
            }

            state = states[0];
        }
        else if (!states.Contains(state)) {
            return false;
        }

        chara.mapStr.Set(EmoteFlagKey, Time.realtimeSinceStartup.ToString());
        chara.mapStr.Set(EmoteVariantKey, state);
        return true;
    }

    public static void StopEmote(Chara chara)
    {
        if (chara is null) {
            return;
        }

        chara.mapStr.Remove(EmoteFlagKey);
        chara.mapStr.Remove(EmoteVariantKey);
    }

    public static bool HasEmoteClip(Chara? chara)
        => ListEmoteStates(chara, stopAtFirst: true).Count > 0;

    public static List<string> ListEmoteStates(Chara? chara, bool stopAtFirst = false)
    {
        var suffixes = AcsSuffixes.Resolve(chara);
        if (suffixes is null || suffixes.Count == 0) {
            return new List<string>();
        }

        return CollectEmoteStates(suffixes, CurrentPrefix(chara), stopAtFirst);
    }

    private static string? CurrentPrefix(Chara? chara)
    {
        if (chara is null) {
            return null;
        }

        bool snow = EClass.core is not null
            && EClass.core.IsGameStarted
            && EClass._zone is not null
            && EClass._zone.IsSnowCovered;
        return GetPrefix(chara, snow);
    }

    /// <summary>
    /// 该皮肤上互不重复的 emote 状态名。若当前前缀存在任一 emote* 片，
    /// 无前缀片会被忽略（与其它 ACS 前缀查找规则一致）。
    /// </summary>
    private static List<string> CollectEmoteStates(
        Dictionary<string, SpriteData> suffixes,
        string? prefix,
        bool stopAtFirst)
    {
        var prefixed = new List<string>();
        var unprefixed = new List<string>();
        string prefixHead = prefix is null ? "" : "_acs_" + prefix + ".";

        foreach (var kv in suffixes) {
            if (kv.Key is null || kv.Value is null || !kv.Key.StartsWith("_acs_")) {
                continue;
            }

            string name = kv.Key.Substring("_acs_".Length);
            if (prefix is not null && kv.Key.StartsWith(prefixHead)) {
                string rest = kv.Key.Substring(prefixHead.Length);
                if (IsEmoteState(rest) && !prefixed.Contains(rest)) {
                    prefixed.Add(rest);
                    if (stopAtFirst) {
                        return prefixed;
                    }
                }

                continue;
            }

            if (!IsEmoteState(name)) {
                continue;
            }

            if (!unprefixed.Contains(name)) {
                unprefixed.Add(name);
                if (stopAtFirst && prefix is null) {
                    return unprefixed;
                }
            }
        }

        var result = prefixed.Count > 0 ? prefixed : unprefixed;
        result.Sort(System.StringComparer.Ordinal);
        return result;
    }

    // ==================== 10. greet（问候） ====================

    // greet 触发后延迟再激活，避免气泡一出就切动画
    private const float GreetDelay = 0.2f;

    /// <summary>本次 greet 锁定的状态名：greet / greet1 / greet2 / greet3 / greet4。</summary>
    private const string GreetVariantKey = "acs_greet_n";

    /// <summary>
    /// acs_greet 存激活时间戳；当前时间 >= 该值才算激活（实现 GreetDelay 延迟）。
    /// </summary>
    public static bool IsGreetActive(Chara chara)
    {
        if (!chara.mapStr.TryGetValue("acs_greet", out string value)) {
            return false;
        }

        if (!float.TryParse(value, out float activateAt)) {
            return false;
        }

        return Time.realtimeSinceStartup >= activateAt;
    }

    /// <summary>已写入 greet 标记（含 0.2s 延迟尚未激活）。</summary>
    public static bool HasGreetFlag(Chara chara)
        => chara is not null && chara.mapStr.ContainsKey("acs_greet");

    /// <summary>本次问候锁定的 clip 状态名。</summary>
    public static string GetGreetState(Chara chara)
    {
        if (chara.mapStr.TryGetValue(GreetVariantKey, out string variant)
            && !variant.IsEmpty()) {
            return variant;
        }

        return "greet";
    }

    /// <summary>触发问候（延迟 <see cref="GreetDelay"/> 秒后激活）。随机锁定 greet1-4 中实际存在的片。</summary>
    public static void StartGreet(Chara chara)
    {
        chara.mapStr.Set("acs_greet",
            (Time.realtimeSinceStartup + GreetDelay)
            .ToString());
        chara.mapStr.Set(GreetVariantKey, PickGreetState(chara));
    }

    public static void StopGreet(Chara chara)
    {
        chara.mapStr.Remove("acs_greet");
        chara.mapStr.Remove(GreetVariantKey);
    }

    /// <summary>
    /// 在 greet / greet1-4 里随机挑一个当前 suffixes 里存在的片。
    /// 无编号 greet 与 greetN 同等参与抽取，避免只有 greet2 时永远播不到 greet。
    /// </summary>
    private static string PickGreetState(Chara chara)
    {
        var suffixes = AcsSuffixes.Resolve(chara);
        if (suffixes is null || suffixes.Count == 0) {
            return "greet";
        }

        bool snow = EClass.core is not null
            && EClass.core.IsGameStarted
            && EClass._zone is not null
            && EClass._zone.IsSnowCovered;
        string? prefix = GetPrefix(chara, snow);

        // bit0 = greet, bit1-4 = greet1-4
        int mask = 0;
        int count = 0;
        for (int n = 0; n <= 4; n++) {
            string state = n == 0 ? "greet" : "greet" + n;
            if (!HasGreetClip(suffixes, prefix, state)) {
                continue;
            }

            mask |= 1 << n;
            count++;
        }

        if (count == 0) {
            return "greet";
        }

        int pick = EClass.rnd(count);
        for (int n = 0; n <= 4; n++) {
            if ((mask & (1 << n)) == 0) {
                continue;
            }

            if (pick == 0) {
                return n == 0 ? "greet" : "greet" + n;
            }

            pick--;
        }

        return "greet";
    }

    private static bool HasGreetClip(Dictionary<string, SpriteData> suffixes, string? prefix, string state)
    {
        if (prefix != null
            && suffixes.TryGetValue("_acs_" + prefix + "." + state, out var prefixed)
            && prefixed is not null) {
            return true;
        }

        return suffixes.TryGetValue("_acs_" + state, out var data) && data is not null;
    }

    // ==================== 11. eat（进食） ====================

    /// <summary>
    /// eat 手动标记键（mapStr）。值为激活时间戳（realtimeSinceStartup）。
    /// 仅供 StartEat / StopEat 手动 override；主判定见 <see cref="IsEatActive"/>。
    /// </summary>
    private const string EatFlagKey = "acs_eat";

    /// <summary>
    /// 是否正在播放 eat 动画。
    ///
    /// 主判定（无状态、无需外部调用）：原版 AI_Eat 正跑在“进食”进度子行为上——
    /// ai is AI_Eat 且当前子行为是 AIProgress 且手中持有卡片。
    ///
    /// 为什么精确：AI_Eat.Run 内部，手里持卡时唯一会跑的进度子行为就是进食进度本身
    /// （canProgress 要求 owner.held == target）。AI_Cook 进度期间食物在灶上
    /// （held == null），走去餐桌是 AI_Goto（不是 AIProgress），两者都不会误触 eat。
    ///
    /// 进食结束（进度播完或被取消：移动 / 受伤 / 吃饱 / 食物没了）后进度子行为结束，
    /// Current 不再是 AIProgress，下一帧自动回 idle——无需调用 StopEat。
    ///
    /// 兜底：<see cref="StartEat"/> / <see cref="StopEat"/> 手动标记
    /// （测试 / 外部 override）。
    /// </summary>
    public static bool IsEatActive(Chara chara)
    {
        if (chara is null) {
            return false;
        }

        if (chara.ai is AI_Eat eatAi
            && eatAi.Current is AIProgress
            && chara.held != null) {
            return true;
        }

        return IsEatFlagActive(chara);
    }

    /// <summary>eat 手动标记（mapStr 时间戳，与 greet 同机制）。</summary>
    public static bool IsEatFlagActive(Chara chara)
    {
        if (!chara.mapStr.TryGetValue(EatFlagKey, out string value)) {
            return false;
        }

        return float.TryParse(value, out float activateAt)
            && Time.realtimeSinceStartup >= activateAt;
    }

    /// <summary>
    /// 手动开始 eat 动画（循环播放直至 <see cref="StopEat"/> 或游戏状态结束）。
    /// 通常无需调用：<see cref="IsEatActive"/> 会自动检测。
    /// </summary>
    public static void StartEat(Chara chara)
    {
        chara.mapStr.Set(EatFlagKey, Time.realtimeSinceStartup.ToString());
    }

    /// <summary>手动结束 eat 动画。</summary>
    public static void StopEat(Chara chara)
    {
        chara.mapStr.Remove(EatFlagKey);
    }

    // ==================== 12. 朝向锁定缓存（magic / melee / ranged） ====================

    // PC 施法朝向缓存：true = 屏幕右侧（flip），false = 屏幕左侧
    private static readonly Dictionary<int, bool> MagicFaceRightByUid = new();

    // PC 近战朝向缓存：true = 屏幕右侧（flip），false = 屏幕左侧
    private static readonly Dictionary<int, bool> MeleeFaceRightByUid = new();

    // PC 远程朝向缓存：true = 屏幕右侧（flip），false = 屏幕左侧
    private static readonly Dictionary<int, bool> RangedFaceRightByUid = new();

    /// <summary>
    /// PC 施放敌对法术时，按目标相对位置锁左右朝向（等距 screenX ∝ mapX+mapZ）。
    /// 纯屏竖方向不改朝向。窗口结束自动清除。
    /// </summary>
    public static void SetMagicFaceFromTarget(Chara chara, Card? tc, Point? pos)
    {
        if (chara is null || chara.pos is null) {
            return;
        }

        Point? target = ResolveMagicTargetPoint(chara, tc, pos);
        if (target is null) {
            return;
        }

        float screenH = (target.x - chara.pos.x) + (target.z - chara.pos.z);
        if (Mathf.Abs(screenH) <= 0.01f) {
            return;
        }

        MagicFaceRightByUid[chara.uid] = screenH > 0f;
    }

    public static bool TryGetMagicFaceRight(Chara chara, out bool faceRight)
    {
        faceRight = false;
        if (chara is null || !IsMagicCombatActive(chara)) {
            if (chara is not null) {
                ClearMagicFace(chara);
            }

            return false;
        }

        return MagicFaceRightByUid.TryGetValue(chara.uid, out faceRight);
    }

    public static void ClearMagicFace(Chara? chara)
    {
        if (chara is null) {
            return;
        }

        MagicFaceRightByUid.Remove(chara.uid);
    }

    /// <summary>
    /// PC 近战攻击时，按目标相对位置锁左右朝向（等距 screenX ∝ mapX+mapZ）。
    /// 纯屏竖方向不改朝向。窗口结束自动清除。
    /// </summary>
    public static void SetMeleeFaceFromTarget(Chara chara, Card? tc, Point? pos)
    {
        if (chara is null || chara.pos is null) {
            return;
        }

        Point? target = ResolveMagicTargetPoint(chara, tc, pos);
        if (target is null) {
            return;
        }

        float screenH = (target.x - chara.pos.x) + (target.z - chara.pos.z);
        if (Mathf.Abs(screenH) <= 0.01f) {
            return;
        }

        MeleeFaceRightByUid[chara.uid] = screenH > 0f;
    }

    public static bool TryGetMeleeFaceRight(Chara chara, out bool faceRight)
    {
        faceRight = false;
        if (chara is null || !IsMeleeActive(chara)) {
            if (chara is not null) {
                ClearMeleeFace(chara);
            }

            return false;
        }

        return MeleeFaceRightByUid.TryGetValue(chara.uid, out faceRight);
    }

    public static void ClearMeleeFace(Chara? chara)
    {
        if (chara is null) {
            return;
        }

        MeleeFaceRightByUid.Remove(chara.uid);
    }

    public static void SetRangedFaceFromTarget(Chara chara, Card? tc, Point? pos)
    {
        if (chara is null || chara.pos is null) {
            return;
        }

        Point? target = ResolveMagicTargetPoint(chara, tc, pos);
        if (target is null) {
            return;
        }

        float screenH = (target.x - chara.pos.x) + (target.z - chara.pos.z);
        if (Mathf.Abs(screenH) <= 0.01f) {
            return;
        }

        RangedFaceRightByUid[chara.uid] = screenH > 0f;
    }

    public static bool TryGetRangedFaceRight(Chara chara, out bool faceRight)
    {
        faceRight = false;
        if (chara is null || !IsRangedActive(chara)) {
            if (chara is not null) {
                ClearRangedFace(chara);
            }

            return false;
        }

        return RangedFaceRightByUid.TryGetValue(chara.uid, out faceRight);
    }

    public static void ClearRangedFace(Chara? chara)
    {
        if (chara is null) {
            return;
        }

        RangedFaceRightByUid.Remove(chara.uid);
    }

    private static Point? ResolveMagicTargetPoint(Chara chara, Card? tc, Point? pos)
    {
        if (tc is not null && tc.pos is not null && !ReferenceEquals(tc, chara)) {
            return tc.pos;
        }

        if (IsUsablePoint(pos, chara)) {
            return pos;
        }

        try {
            Card actTc = Act.TC;
            if (actTc is not null && actTc.pos is not null && !ReferenceEquals(actTc, chara)) {
                return actTc.pos;
            }
        }
        catch {
        }

        try {
            Point actTp = Act.TP;
            if (IsUsablePoint(actTp, chara)) {
                return actTp;
            }
        }
        catch {
        }

        return null;
    }

    private static bool IsUsablePoint(Point? p, Chara chara)
    {
        if (p is null || chara.pos is null) {
            return false;
        }

        try {
            if (!p.IsValid) {
                return false;
            }
        }
        catch {
        }

        return p.x != chara.pos.x || p.z != chara.pos.z;
    }

    // ==================== 13. 主手武器键缓存（含缓存清理） ====================

    // 原版 SKILL.weapon*（与 SourceCategory.Row.skill 对齐）
    private const int SkillSword = 101;

    private const int SkillStaff = 103;

    private const int SkillPolearm = 106;

    // combat 主手武器键缓存：Equip/Unequip 时刷新，避免每帧读 body/category
    private static readonly Dictionary<int, string?> MainHandCombatWeaponKeyByUid = new();

    /// <summary>
    /// combat 状态按主手武器拆键：
    /// 持 <c>sword</c> / <c>polearm</c> / <c>staff</c> 时取对应后缀键；
    /// 空手/其它武器为 null，回退无武器的 _acs_combat。按 chara.uid 缓存，Equip/Unequip 时失效。
    /// </summary>
    public static string? GetMainHandCombatWeaponKey(Chara chara)
    {
        if (chara is null) {
            return null;
        }

        int uid = chara.uid;
        if (MainHandCombatWeaponKeyByUid.TryGetValue(uid, out string? cached)) {
            return cached;
        }

        string? key = ComputeMainHandCombatWeaponKey(chara);
        MainHandCombatWeaponKeyByUid[uid] = key;
        return key;
    }

    /// <summary>重新计算并缓存主手武器键（换主手/Equip 后调用）。</summary>
    public static void RefreshMainHandCombatWeaponKey(Chara chara)
    {
        if (chara is null) {
            return;
        }

        MainHandCombatWeaponKeyByUid[chara.uid] = ComputeMainHandCombatWeaponKey(chara);
    }

    /// <summary>使主手武器键缓存失效（卸下/Unequip 后调用）。</summary>
    public static void InvalidateMainHandCombatWeaponKey(Chara chara)
    {
        if (chara is null) {
            return;
        }

        MainHandCombatWeaponKeyByUid.Remove(chara.uid);
    }

    /// <summary>清空全部武器键缓存（ReloadAcs 等用）。</summary>
    public static void ClearMainHandCombatWeaponKeyCache()
    {
        MainHandCombatWeaponKeyByUid.Clear();
    }

    private static string? ComputeMainHandCombatWeaponKey(Chara chara)
    {
        try {
            Thing? w = chara.body?.slotMainHand?.thing;
            if (w is null) {
                return null;
            }

            SourceCategory.Row? cat = w.category;
            if (cat is null) {
                return null;
            }

            // 按技能类型映射武器键
            switch (cat.skill) {
                case SkillSword:
                    return "sword";
                case SkillPolearm:
                    return "polearm";
                case SkillStaff:
                    return "staff";
            }

            // 子技能树节点也归到主武器键（如剑专精下的变体）
            if (cat.IsChildOf("sword")) {
                return "sword";
            }

            if (cat.IsChildOf("polearm")) {
                return "polearm";
            }

            if (cat.IsChildOf("staff")) {
                return "staff";
            }
        }
        catch {
            // body / slotMainHand / 分类读取异常时安全回退
        }

        return null;
    }

    /// <summary>清空主手武器键 / 魔法·近战·远程朝向缓存，并把 PC 残留的一次性动作窗口一并清掉（切图 / Reload 用）。</summary>
    public static void ClearHostileScanCache()
    {
        ClearMainHandCombatWeaponKeyCache();
        MagicFaceRightByUid.Clear();
        MeleeFaceRightByUid.Clear();
        RangedFaceRightByUid.Clear();
        LastNpcMoveTime.Clear();

        try {
            var pc = EClass.pc;
            if (pc != null) {
                ClearCombat(pc);
                ClearMelee(pc);
                ClearRanged(pc);
                StopHurt(pc);
            }
        }
        catch {
            // 主菜单
        }
    }

    // ==================== 14. PC 移动判定 ====================

    /// <summary>
    /// 默认粘滞窗口（秒）。
    public const float DefaultNpcMoveHysteresis = 0.1f;

    /// <summary>
    /// NPC 移动粘滞窗口（秒，运行时可用控制台命令 <c>ACS.MoveHysteresis &lt;sec&gt;</c> 调整）。
    /// 快队友跟队时是“冲一小段→停→再冲”的橡皮筋节拍，isMoving 在每次短停时闪 false，
    /// 切 idle 再切回 move 会从第 0 帧重播，短冲每次只见开头 1-2 帧（表现为走走停停/卡顿）。
    /// 这里给 NPC 端补上 PC 端已有的同类粘滞：isMoving 刚变 false 的
    /// <see cref="NpcMoveHysteresis"/> 秒内仍按“移动中”处理，短停被平滑掉；
    /// 真正停稳（超过窗口）才落 idle。纯视觉：不改位移/AI/速度，不影响任何游戏性判定。
    /// </summary>
    public static float NpcMoveHysteresis = DefaultNpcMoveHysteresis;

    /// <summary>NPC 最近一次 isMoving 为 true 的时刻（realtimeSinceStartup，按 uid）。</summary>
    private static readonly Dictionary<int, float> LastNpcMoveTime = new();

    /// <summary>
    /// 是否应播 move ACS。PC 走轴/寻路粘滞；NPC 用 isMoving + 短停粘滞。
    /// </summary>
    public static bool IsMoving(Chara chara)
    {
        if (chara is null) {
            return false;
        }

        if (chara.IsPC) {
            return IsPcMoving(chara);
        }

        if (chara.renderer is CharaRenderer { IsMoving: true }) {
            LastNpcMoveTime[chara.uid] = Time.realtimeSinceStartup;
            return true;
        }

        // 短停粘滞：isMoving 刚变 false 不久（跟队冲-停节拍 / 格子衔接抖动）
        // 仍保持 move，避免每段冲刺都从第 0 帧重播（视觉上“只走 1-2 帧就站住”）。
        if (LastNpcMoveTime.TryGetValue(chara.uid, out float lastMove)
            && Time.realtimeSinceStartup - lastMove <= NpcMoveHysteresis) {
            return true;
        }

        return false;
    }

    /// <summary>
    /// PC 是否应播 move ACS。
    /// 仅看 CharaRenderer.isMoving 会在格子衔接瞬间变 false，长按 WASD 会闪 idle。
    /// 近点/按住：GoalManualMove；键位：EInput.axis（勿 ConvertAxis）。
    /// 远点 AI_Goto：目标未到达就保持 move（跨格 repath 时 path.HasPath 会空一拍，
    /// 不能只靠 HasPath；也绝不能 ai is AI_Goto 一律 true——到站后 AI 换掉前会粘 move）。
    /// 不要用 idleTimer / 裸 nextMove / 裸 path：停步后残留会一直播 move。
    /// </summary>
    public static bool IsPcMoving(Chara chara)
    {
        if (chara.renderer is CharaRenderer cr && cr.IsMoving) {
            return true;
        }

        if (chara.ai is GoalManualMove) {
            return true;
        }

        // 格子衔接时 isMoving/AI 可能瞬时 false，轴仍非零则保持 move
        if (EInput.axis != Vector2.zero) {
            return true;
        }

        // 远点：AI_Goto 且尚未到站 = 寻路/迈步/repath 全过程。
        // 到站后 IsDestinationReached 为 true，即使 ai 尚未换掉也不再播 move。
        if (chara.ai is AI_Goto gotoAi) {
            try {
                if (!gotoAi.IsDestinationReached()) {
                    return true;
                }
            }
            catch {
                // 主菜单 / dest 未就绪
            }

            // 极少：到站判定异常时仍用有效 path 兜底（仍限制在 AI_Goto 内，避免残留 path 粘死）
            try {
                PathProgress path = chara.path;
                if (path is not null
                    && path.HasPath
                    && path.nodes is not null
                    && path.nodeIndex >= 0
                    && path.nodeIndex < path.nodes.Count
                    && !path.IsDestinationReached(chara.pos)) {
                    return true;
                }
            }
            catch {
                // 主菜单 / 地图未就绪
            }
        }

        return false;
    }

    // ==================== 15. 工具函数：特殊前缀（override / snow / married） ====================

    /// <summary>
    /// 特殊状态前缀优先级：手动 override > 雪地 > 已婚
    /// </summary>
    public static string? GetPrefix(Chara chara, bool snow = false)
    {
        if (chara.mapStr.TryGetValue("acs_override", out string overrideClip)) {
            return overrideClip;
        }

        if (snow) {
            return "snow";
        }

        if (chara.IsMarried) {
            return "married";
        }

        return null;
    }

}
