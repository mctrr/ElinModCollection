using HarmonyLib;

namespace Wetness.Patches;

/// <summary>
/// 「吃」补水。
///
/// 入口是 FoodEffect.Proc —— 吃下去的统一出口（Chara.Eat、AI_Eat、
/// 食堂开饭 FactionBranch 全走它）。与「喝」的 ProcDrink 是两个并列的出口，
/// 所以两边各挂一次，别指望其中一个能覆盖另一个：
/// 之前只挂了 ProcDrink，于是「直接喝治疗药水不补水、倒进水囊再喝就补」
/// 这种同一件东西两套结果的事就出现了。
///
/// consume = false 的调用（AI_Fuck、剧情里的「假装吃」）不算数 ——
/// 东西根本没被消耗，不该补水。所以 Postfix 把原方法的这个参数一并收下。
/// </summary>
// 类级 [HarmonyPatch] 必需：缺了它整个类会被 HarmonyX 静默跳过（见 WetnessDrinkPatch 注释）。
[HarmonyPatch]
internal static class WetnessEatPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(FoodEffect), nameof(FoodEffect.Proc))]
    private static void ProcPostfix(Chara c, Thing food, bool consume)
    {
        if (!consume || food == null || !c.TracksWetness())
        {
            return;
        }

        int gain = WetnessWater.GetFoodWater(food);
        if (gain > 0)
        {
            c.wetness().Mod(gain);
        }
    }
}
