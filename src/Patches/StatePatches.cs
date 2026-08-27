using ACS.API;
using HarmonyLib;

namespace ACS.Patches;

/// <summary>
/// 状态触发：PC 主动攻击 / 换主手 combat 窗口、问候动画、复活动作。
/// combat 倒计时复用原版 combatCount（Tick 每回合 -1）。
/// </summary>
[HarmonyPatch]
internal class CombatPatch
{
    /// <summary>
    /// 近战攻击入口：原版 ActMelee.Attack 开头写 combatCount，但多数路径不调 DoHostileAction。
    /// 秒杀时目标已死，DoHostileAction 会 early-return，必须在此打 acs_combat 标记。
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ActMelee), nameof(ActMelee.Attack))]
    internal static void OnMeleeAttack()
    {
        var cc = Act.CC;
        if (cc is null || !cc.IsPC) {
            return;
        }

        AcsStateResolver.MarkActiveCombat(cc);
    }

    /// <summary>
    /// PC 造成伤害（远程/法术/投掷等）：覆盖非近战主动攻击与秒杀。
    /// origin 为攻击者；被打时 origin 是敌人，不会误开 combat。
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Card), nameof(Card.DamageHP), new[] {
        typeof(long), typeof(int), typeof(int), typeof(AttackSource), typeof(Card),
        typeof(bool), typeof(Thing), typeof(Chara), typeof(int)
    })]
    internal static void OnDamageHP(Card origin)
    {
        if (origin is not Chara attacker || !attacker.IsPC) {
            return;
        }

        AcsStateResolver.MarkActiveCombat(attacker);
    }

    /// <summary>
    /// 其它主动敌对动作（部分技能/交互仍走此路径）。
    /// 被打 / SetEnemy 不触发。
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.DoHostileAction))]
    internal static void OnDoHostileAction(Chara __instance, Card _tg)
    {
        if (__instance is null || !__instance.IsPC) {
            return;
        }

        AcsStateResolver.MarkActiveCombat(__instance);
    }

    /// <summary>
    /// Main-hand equip/swap: refresh combat weapon-key cache; PC also opens 5-turn combat window.
    /// Empty main hand does not open combat; non-main-hand equip does not either.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CharaBody), nameof(CharaBody.Equip))]
    internal static void OnEquipPrefix(CharaBody __instance, ref Thing? __state)
    {
        __state = __instance?.slotMainHand?.thing;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CharaBody), nameof(CharaBody.Equip))]
    internal static void OnEquipPostfix(CharaBody __instance, Thing? __state)
    {
        if (__instance?.owner is not Chara chara) {
            return;
        }

        Thing? mainHandAfter = __instance.slotMainHand?.thing;
        if (!ReferenceEquals(__state, mainHandAfter)) {
            AcsStateResolver.RefreshMainHandCombatWeaponKey(chara);
        }

        // PC active combat window: only when main hand becomes a different non-null thing
        if (!chara.IsPC) {
            return;
        }

        if (mainHandAfter is null || ReferenceEquals(__state, mainHandAfter)) {
            return;
        }

        AcsStateResolver.MarkActiveCombat(chara);
    }

    /// <summary>Refresh main-hand combat weapon key after unequip (null if empty).</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(CharaBody), nameof(CharaBody.Unequip), new[] { typeof(Thing), typeof(bool) })]
    internal static void OnUnequipThing(CharaBody __instance)
    {
        RefreshMainHandCombatWeaponKey(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CharaBody), nameof(CharaBody.Unequip), new[] { typeof(BodySlot), typeof(bool) })]
    internal static void OnUnequipSlot(CharaBody __instance)
    {
        RefreshMainHandCombatWeaponKey(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CharaBody), nameof(CharaBody.UnequipAll))]
    internal static void OnUnequipAll(CharaBody __instance)
    {
        RefreshMainHandCombatWeaponKey(__instance);
    }

    private static void RefreshMainHandCombatWeaponKey(CharaBody? body)
    {
        if (body?.owner is not Chara chara) {
            return;
        }

        AcsStateResolver.RefreshMainHandCombatWeaponKey(chara);
    }

    /// <summary>
    /// Tick 已 combatCount-- 后：主动攻击窗口到期时检测 1 次视野敌对。
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.Tick))]
    internal static void OnTick(Chara __instance)
    {
        if (__instance is null || !__instance.IsPC) {
            return;
        }

        AcsStateResolver.OnPcTickCombatWindow(__instance);
    }
}


[HarmonyPatch]
internal class MagicCombatPatch
{
    /// <summary>
    /// 魔法攻击（mcombat）：施放**敌对法术**时开 mcombat 窗口，优先级高于普通 combat。
    ///
    /// 判定：<c>a is Spell</c>（是真法术，排除普通技能/能力）且
    /// <c>a.IsTargetHostileParty()</c>（原版 abilityType 标为 attack/debuff/dot，
    /// 即打敌人的魔法攻击；治疗/增益/位移等非攻击法术不触发）。
    ///
    /// 走 <see cref="Chara.UseAbility(Act, Card, Point, bool)"/> 的
    /// int/string 重载与 AI（GoalCombat.TryUseAbility、ButtonAbility、HotItemAct）
    /// 都会汇聚到这里，PC 与 NPC 一网打尽。
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.UseAbility), new[] {
        typeof(Act), typeof(Card), typeof(Point), typeof(bool)
    })]
    internal static void OnUseHostileSpell(Chara __instance, Act a, Card tc, Point pos, bool __result)
    {
        if (!__result || __instance is null) {
            return;
        }

        if (a is Spell && a.IsTargetHostileParty()) {
            AcsStateResolver.MarkActiveMagicCombat(__instance);
            if (__instance.IsPC) {
                AcsStateResolver.SetMagicFaceFromTarget(__instance, tc, pos);
            }
        }
    }
}


[HarmonyPatch]
internal class GreetPatch
{
    /// <summary>
    /// NPC 触发 fov 对话气泡时，延迟后播放一次 greet 动画。
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.TalkTopic))]
    internal static void Postfix(Chara __instance, string topic)
    {
        if (topic != "fov") {
            return;
        }

        if (AcsStateResolver.HasGreetFlag(__instance)) {
            return;
        }

        AcsStateResolver.StartGreet(__instance);
    }
}


[HarmonyPatch]
internal class RevivePatch
{
    /// <summary>
    /// 角色真正从死亡复活时播一次 revive 动作（_acs_revive），播完一轮回其它状态。
    /// Prefix 记下调用前已死，避免对活人 Revive 空调用误触发。
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.Revive))]
    internal static void Prefix(Chara __instance, ref bool __state)
    {
        __state = __instance is not null && __instance.isDead;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.Revive))]
    internal static void Postfix(Chara __instance, bool __state)
    {
        if (!__state || __instance is null || __instance.isDead) {
            return;
        }

        AcsStateResolver.StartRevive(__instance);
    }
}
