namespace ACS.API;

using UnityEngine;

public static class AcsStateResolver
{
    // greet 触发后延迟再激活，避免气泡一出就切动画
    private const float GreetDelay = 0.2f;

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

    /// <summary>
    /// 基础状态优先级：战斗 > 移动(仅 PC) > greet(NPC) > 异常 > idle
    /// </summary>
    public static string GetState(Chara chara)
    {
        if (IsCombatVisual(chara)) {
            return "combat";
        }

        // 资源名 _acs_move；NPC 格子移动不需要 walk ACS
        if (chara.IsPC && IsPcMoving(chara)) {
            return "move";
        }

        if (!chara.IsPC && IsGreetActive(chara)) {
            return "greet";
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
    /// combat 资源子键：仅看主手装备类型（不看本次攻击方式）。
    /// 返回 <c>sword</c> / <c>polearm</c> / <c>staff</c>；无专用映射返回 null（用通用 _acs_combat）。
    /// </summary>
    public static string? GetMainHandCombatWeaponKey(Chara chara)
    {
        if (chara is null) {
            return null;
        }

        try {
            Thing? w = chara.body?.slotMainHand?.thing;
            if (w is null) {
                return null;
            }

            SourceCategory.Row? cat = w.category;
            if (cat is null) {
                return null;
            }

            // skill 与原版武器熟练一致，优先
            switch (cat.skill) {
                case SkillSword:
                    return "sword";
                case SkillPolearm:
                    return "polearm";
                case SkillStaff:
                    return "staff";
            }

            // 兜底：分类树（部分模组/数据 skill 异常时）
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
            // 主菜单 / body 未就绪
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

    /// <summary>控制台重载等：清 PC 主动战斗标记。</summary>
    public static void ClearHostileScanCache()
    {
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
    /// 远点 AI_Goto：HasPath(state==PathReady 且 nodes≥1) 且 nodeIndex 仍有效且未到 dest。
    /// 勿用裸 DestDist/裸 HasPath：容差与取消后残留 path 都会粘死 move。
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

        // 远点：仅在仍跑 AI_Goto 时认 path（Cancel 后 state/nodes 可能残留，勿裸 HasPath）
        // nodeIndex 与 TryGoTo 一致：&lt;0 视为路径耗尽/应取消，不算移动
        // IsDestinationReached = Distance(pos,destPoint) &lt;= destDist（点地 ctor destDist=0）
        if (chara.ai is not AI_Goto) {
            return false;
        }

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

    /// <summary>触发问候（延迟 <see cref="GreetDelay"/> 秒后激活）。</summary>
    public static void StartGreet(Chara chara)
    {
        chara.mapStr.Set("acs_greet",
            (Time.realtimeSinceStartup + GreetDelay)
            .ToString());
    }

    public static void StopGreet(Chara chara)
    {
        chara.mapStr.Remove("acs_greet");
    }
}
