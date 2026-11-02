using ACS.API;
using ACS.Frame;
using HarmonyLib;
using UnityEngine;

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
    /// PC / NPC 同时开 melee 窗口（优先级高于 combat）；PC 还会续 combat 主动窗口。
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ActMelee), nameof(ActMelee.Attack))]
    internal static void OnMeleeAttack()
    {
        var cc = Act.CC;
        if (cc is null) {
            return;
        }

        PlayState.MarkActiveMelee(cc);
        if (cc.IsPC) {
            PlayState.SetMeleeFaceFromTarget(cc, Act.TC, Act.TP);
        }
    }

    /// <summary>
    /// 受到真实伤害时开 hurt（受伤）动画窗口，PC / NPC 都触发。
    ///
    /// <b>只有「由角色造成」的伤害才算挨打。</b>环境 / 状态伤害（中毒、燃烧、出血、过食、
    /// 挨饿、疲劳、负重、陷阱、摔落）的 origin 一律为 null。这类伤害每回合结算（Elin 走一格
    /// 推进一回合），一旦切 hurt 就会把 <c>combat_begin</c> / <c>magic</c> / <c>melee</c> 打断，
    /// 被打断的一次性动作按新签名从第 0 帧重播 —— 玩家看到的就是「一边走一边不停掏书」。
    /// 血照常扣（<see cref="Card.DamageHP(long, int, int, AttackSource, Card, bool, Thing, Chara, int)"/>
    /// 本身不受影响），只是不切受击动作。
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Card), nameof(Card.DamageHP), new[] {
        typeof(long), typeof(int), typeof(int), typeof(AttackSource), typeof(Card),
        typeof(bool), typeof(Thing), typeof(Chara), typeof(int)
    })]
    internal static void OnDamageHP(Card __instance, long dmg, AttackSource attackSource, Card? origin)
    {
        if (dmg <= 0 || origin is not Chara || __instance is not Chara victim || victim.isDead) {
            return;
        }

        // 带 origin 的状态伤害（元素反射 / 寄生 / 反弹）同样不算挨打
        if (attackSource is AttackSource.Condition or AttackSource.Hunger or AttackSource.Fatigue) {
            return;
        }

        PlayState.StartHurt(victim);
    }

    /// <summary>
    /// 其它主动敌对动作（部分技能/交互仍走此路径）。
    /// 被打 / SetEnemy 不触发。
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.DoHostileAction))]
    internal static void OnDoHostileAction(Chara __instance, Card _tg)
    {
        if (!__instance.IsPC) {
            return;
        }

        PlayState.MarkActiveCombat(__instance);
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
            PlayState.RefreshMainHandCombatWeaponKey(chara);
        }

        // PC active combat window: only when main hand becomes a different non-null thing
        if (!chara.IsPC) {
            return;
        }

        if (mainHandAfter is null || ReferenceEquals(__state, mainHandAfter)) {
            return;
        }

        PlayState.MarkActiveCombat(chara);
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

        PlayState.RefreshMainHandCombatWeaponKey(chara);
    }

    /// <summary>
    /// Tick 已 combatCount-- 后：主动攻击窗口到期时检测 1 次视野敌对。
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.Tick))]
    internal static void OnTick(Chara __instance)
    {
        if (!__instance.IsPC) {
            return;
        }

        PlayState.OnPcTickCombatWindow(__instance);
    }
}


[HarmonyPatch]
internal class MagicCombatPatch
{
    /// <summary>
    /// 魔法（magic）：施放**任何法术**（Spell）都开 magic 窗口，优先级高于普通 combat。
    ///
    /// 判定：<c>a is Spell</c>（是真法术，排除普通技能/能力）。不区分攻击/召唤/治疗/
    /// 增益——只要是施法动作（读条起手）都播 _acs_magic。
    ///
    /// 走 <see cref="Chara.UseAbility(Act, Card, Point, bool)"/> 的
    /// int/string 重载与 AI（GoalCombat.TryUseAbility、ButtonAbility、HotItemAct）
    /// 都会汇聚到这里，PC 与 NPC 一网打尽。
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.UseAbility), new[] {
        typeof(Act), typeof(Card), typeof(Point), typeof(bool)
    })]
    internal static void OnUseSpell(Chara __instance, Act a, Card tc, Point pos, bool __result)
    {
        if (!__result) {
            return;
        }

        if (a is Spell) {
            PlayState.MarkActiveMagicCombat(__instance);
            if (__instance.IsPC) {
                PlayState.SetMagicFaceFromTarget(__instance, tc, pos);
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

        if (PlayState.HasGreetFlag(__instance)) {
            return;
        }

        PlayState.StartGreet(__instance);
    }
}


[HarmonyPatch]
internal class RevivePatch
{
    /// <summary>
    /// 角色真正从死亡复活时播一次 revive 动作（_acs_revive），播完一轮回其它状态。
    /// Prefix 在角色**仍处于死亡（倒地）状态时**就打好 revive 标记，这样复活站立
    /// 的第一帧渲染直接进 revive clip，不会先闪 1 帧 idle 站姿再播动画。
    /// 复活失败（调用后仍死）由 Postfix 清掉标记。
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.Revive))]
    internal static void Prefix(Chara __instance, ref bool __state)
    {
        __state = __instance is not null && __instance.isDead;
        if (__state) {
            PlayState.StartRevive(__instance);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.Revive))]
    internal static void Postfix(Chara __instance, bool __state)
    {
        if (!__state) {
            return;
        }

        // 复活失败（仍死）：清掉 Prefix 打上的 revive 标记，避免残留。
        if (__instance.isDead) {
            PlayState.StopRevive(__instance);
            return;
        }

        // 复活成功：确保 revive 标记仍在。若死亡期间渲染已把标记消费/清掉
        // （AdvanceClip 播完一轮），这里重设 → 站立第一帧即进 revive，无 idle 闪帧。
        PlayState.StartRevive(__instance);
    }
}
