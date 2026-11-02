using HarmonyLib;

namespace ElonaBeLike;

/// <summary>
/// 命令战略经验：被命令的队友（守住 / 巡逻）在交战时，按原版自动战斗的数值与节奏给 PC 加战略技能经验。
///
/// 原版出处 Elin/GoalCombat.cs，Run() 的战斗循环里：
///     if (this is GoalAutoCombat)
///     {
///         if (owner.IsPC) { ActionMode.Adv.SetTurbo(...); }
///         owner.ModExp(135, 20);      // SKILL.strategy = 135
///     }
/// 也就是说「自动战斗每回合 +20 战略经验」是原版里战略经验的唯一来源，而且它只在
/// PC 自己跑 GoalAutoCombat 时触发。队友作战跑的是普通 GoalCombat（this is GoalAutoCombat 为 false），
/// 所以被命令的队友打得再凶，PC 的战略技能也一分不涨。
///
/// 这里把断掉的那一段接上：PC 每回合检查一次「有没有被命令的队友正在交战」，有就照原版发同样的 20 点。
/// 挂在 Chara.Tick 上而不是队友身上，是为了让收益有上界——无论派出几个队友，
/// PC 每回合最多只拿一次，和原版 PC 自己自动战斗时的速率一致。
/// </summary>
[HarmonyPatch]
internal static class CommandStrategyExp
{
    /// <summary>照搬 GoalCombat.Run 里自动战斗那一行的数值，不做额外放大。</summary>
    private const int StrategyExpPerTurn = 20;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.Tick))]
    private static void PcTurnPostfix(Chara __instance)
    {
        if (!__instance.IsPC || !__instance.IsAliveInCurrentZone)
        {
            return;
        }

        // PC 自己开着自动战斗时，原版那一行已经在发这 20 点，不重复发。
        if (__instance.ai is GoalAutoCombat)
        {
            return;
        }

        if (!CommandRegistry.AnyCommandedInCombat())
        {
            return;
        }

        __instance.ModExp(SKILL.strategy, StrategyExpPerTurn);
        Plugin.LogDebug($"命令交战 -> PC 战略经验 +{StrategyExpPerTurn}");
    }
}
