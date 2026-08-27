namespace ACS.API;

using System.Collections.Generic;
using UnityEngine;

public static class AcsStateResolver
{
    // greet 触发后延迟再激活，避免气泡一出就切动画
    private const float GreetDelay = 0.2f;

    /// <summary>
    /// eat 手动标记键（mapStr）。值为激活时间戳（realtimeSinceStartup）。
    /// 仅供 StartEat / StopEat 手动 override；主判定见 <see cref="IsEatActive"/>。
    /// </summary>
    private const string EatFlagKey = "acs_eat";

    /// <summary>revive 一次性动作标记（mapStr 时间戳）。</summary>
    private const string ReviveFlagKey = "acs_revive";

    /// <summary>本次 greet 锁定的状态名：greet / greet1 / greet2 / greet3 / greet4。</summary>
    private const string GreetVariantKey = "acs_greet_n";

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

    // 原版 SKILL.weapon*（与 SourceCategory.Row.skill 对齐）
    private const int SkillSword = 101;
    private const int SkillStaff = 103;
    private const int SkillPolearm = 106;

    // combat weapon key cache: Equip/Unequip refresh, avoid per-frame body/category
    private static readonly Dictionary<int, string?> MainHandCombatWeaponKeyByUid = new();

    // PC mcombat facing: true = screen-right (flip), false = screen-left
    private static readonly Dictionary<int, bool> MagicFaceRightByUid = new();

    /// <summary>
    /// 基础状态优先级：revive(动作) > 战斗 > 移动(仅 PC) > greet(NPC) > 异常 > idle
    /// </summary>
    public static string GetState(Chara chara)
    {
        // 复活是一次性动作，优先于战斗/移动，播完一轮自动清除
        if (IsReviveActive(chara)) {
            return "revive";
        }

        // 魔法攻击窗口 > 普通 combat
        if (IsMagicCombatActive(chara)) {
            return "mcombat";
        }

        if (IsCombatVisual(chara)) {
            return "combat";
        }

        // 资源名 _acs_move；NPC 格子移动不需要 walk ACS
        if (chara.IsPC && IsPcMoving(chara)) {
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
    /// combat ????????????????????????
    /// ?? <c>sword</c> / <c>polearm</c> / <c>staff</c>???????? null???? _acs_combat??
    /// ??? chara.uid ???Equip/Unequip ????
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

    /// <summary>??????????? null????/?????</summary>
    public static void RefreshMainHandCombatWeaponKey(Chara chara)
    {
        if (chara is null) {
            return;
        }

        MainHandCombatWeaponKeyByUid[chara.uid] = ComputeMainHandCombatWeaponKey(chara);
    }

    /// <summary>??????????????????</summary>
    public static void InvalidateMainHandCombatWeaponKey(Chara chara)
    {
        if (chara is null) {
            return;
        }

        MainHandCombatWeaponKeyByUid.Remove(chara.uid);
    }

    /// <summary>?? ACS / ??????</summary>
    public static void ClearMainHandCombatWeaponKeyCache()
    {
        MainHandCombatWeaponKeyByUid.Clear();
    }


    /// <summary>
    /// mcombat（魔法攻击）手动标记键（mapStr）。与 combat 窗口同机制：
    /// <see cref="MarkActiveMagicCombat"/> 写入 combatCount + 该标记，
    /// 倒计时复用原版 <see cref="Chara.combatCount"/>（Chara.Tick 每回合 -1），
    /// 耗尽即自动清标记（见 <see cref="IsMagicCombatActive"/>）。
    /// </summary>
    public const string MagicCombatFlagKey = "acs_mcombat";

    /// <summary>
    /// 是否处于魔法攻击（mcombat）窗口。**优先级高于普通 combat**：
    /// <see cref="GetState"/> 先判 mcombat 再判 combat。
    ///
    /// 触发：<see cref="MarkActiveMagicCombat"/>（原版施放敌对法术时，见
    /// StatePatches 的 Chara.UseAbility 补丁）。非施法状态 / 窗口耗尽自动退出，
    /// 回落到普通 combat 或 idle。
    /// </summary>
    public static bool IsMagicCombatActive(Chara chara)
    {
        if (chara is null) {
            return false;
        }

        if (!chara.mapStr.ContainsKey(MagicCombatFlagKey)) {
            return false;
        }

        // 窗口耗尽：清标记并退出（下次渲染自动回落到 combat / idle）
        if (chara.combatCount <= 0) {
            chara.mapStr.Remove(MagicCombatFlagKey);
            ClearMagicFace(chara);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 开 mcombat 窗口：写入 combatCount=5 + 标记（PC / NPC 通用）。
    /// 持续施法会不断续期；停止施法 5 回合后自动结束。
    /// </summary>
    public static void MarkActiveMagicCombat(Chara chara)
    {
        if (chara is null || chara.isDead) {
            return;
        }

        chara.combatCount = CombatHoldTurns;
        chara.mapStr.Set(MagicCombatFlagKey, "1");
    }

    /// <summary>手动强制 mcombat（测试 / 外部调用）。</summary>
    public static void ForceMagicCombat(Chara chara) => MarkActiveMagicCombat(chara);

    /// <summary>立即结束 mcombat 窗口（测试 / 外部调用）。</summary>
    public static void ClearMagicCombat(Chara chara)
    {
        chara?.mapStr.Remove(MagicCombatFlagKey);
        ClearMagicFace(chara);
    }

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

            // skill ????????????
            switch (cat.skill) {
                case SkillSword:
                    return "sword";
                case SkillPolearm:
                    return "polearm";
                case SkillStaff:
                    return "staff";
            }

            // ???????????/?? skill ????
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
            // ??? / body ???
        }

        return null;
    }

    /// <summary>PC 主动攻击或换主手：写入 combatCount=5 并打标记。</summary>
    public static void MarkActiveCombat(Chara chara)
    {
        if (chara is null || !chara.IsPC) {
            return;
        }

        chara.combatCount = CombatHoldTurns;
        chara.mapStr.Set(CombatFlagKey, "1");
    }

    /// <summary>兼容旧调用名。</summary>
    public static void MarkCombat(Chara chara) => MarkActiveCombat(chara);

    /// <summary>结束主动攻击 combat 窗口。</summary>
    public static void ClearCombat(Chara chara)
    {
        chara?.mapStr.Remove(CombatFlagKey);
    }

    /// <summary>???????? PC ????????? combat ?????</summary>
    public static void ClearHostileScanCache()
    {
        ClearMainHandCombatWeaponKeyCache();
        MagicFaceRightByUid.Clear();

        try {
            var pc = EClass.pc;
            if (pc != null) {
                ClearCombat(pc);
            }
        }
        catch {
            // 主菜单
        }
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
            return;
        }

        ClearCombat(pc);
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

    // ---------------- revive ----------------

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

        chara.mapStr.Set(ReviveFlagKey, Time.realtimeSinceStartup.ToString());
    }

    public static void StopRevive(Chara chara)
    {
        chara?.mapStr.Remove(ReviveFlagKey);
    }

    public static bool IsOneShotActive(Chara chara)
        => IsReviveActive(chara) || IsGreetActive(chara);

    public static bool TryFinishOneShot(Chara chara)
    {
        if (chara is null) {
            return false;
        }

        if (IsReviveActive(chara)) {
            StopRevive(chara);
            return true;
        }

        if (IsGreetActive(chara)) {
            StopGreet(chara);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 推进帧。revive / greet 播完最后一帧后停在末帧并清标记，
    /// 不要 wrap 回第 0 帧（否则会再闪开头几帧）。
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

    // ---------------- eat ----------------

    /// <summary>
    /// Is eat animation active?
    ///
    /// Primary detection (stateless, no external calls needed): vanilla AI_Eat is
    /// running its "eating" progress child -- ai is AI_Eat AND the current child act
    /// is AIProgress AND the hand holds a card.
    ///
    /// Why this is precise: inside AI_Eat.Run the only progress child that runs while
    /// a card is held is the eating progress itself (canProgress requires
    /// owner.held == target). During the AI_Cook progress the food sits on the cooker
    /// (held == null), and walking to the dining spot is an AI_Goto (not AIProgress),
    /// so neither triggers eat.
    ///
    /// When eating ends (progress complete or cancelled: moved / damaged / full /
    /// food gone) the progress child is done, Current stops being an AIProgress and
    /// the next frame falls back to idle automatically -- no StopEat required.
    ///
    /// Fallback: manual flag via <see cref="StartEat"/> / <see cref="StopEat"/>
    /// (testing / external override).
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

    /// <summary>Manual eat flag (mapStr timestamp, same pattern as greet).</summary>
    public static bool IsEatFlagActive(Chara chara)
    {
        if (!chara.mapStr.TryGetValue(EatFlagKey, out string value)) {
            return false;
        }

        return float.TryParse(value, out float activateAt)
            && Time.realtimeSinceStartup >= activateAt;
    }

    /// <summary>
    /// Manually start the eat animation (loops until <see cref="StopEat"/> or the
    /// game state ends). Normally not needed: <see cref="IsEatActive"/> auto-detects.
    /// </summary>
    public static void StartEat(Chara chara)
    {
        chara.mapStr.Set(EatFlagKey, Time.realtimeSinceStartup.ToString());
    }

    /// <summary>Manually stop the eat animation.</summary>
    public static void StopEat(Chara chara)
    {
        chara.mapStr.Remove(EatFlagKey);
    }
}
