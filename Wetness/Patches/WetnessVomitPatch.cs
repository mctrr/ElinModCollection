using HarmonyLib;

namespace Wetness.Patches;

/// <summary>
/// 呕吐会带走水分。Elin 里呕吐是 Chara.Vomit()，
/// 喝脏水（EffectId.DrinkWaterDirty）等多种情况都会触发它。
/// </summary>
[HarmonyPatch(typeof(Chara), nameof(Chara.Vomit))]
internal static class WetnessVomitPatch
{
    [HarmonyPostfix]
    private static void Postfix(Chara __instance)
    {
        if (!__instance.TracksWetness())
        {
            return;
        }

        __instance.wetness().Mod(-WetnessDef.VomitCost);
    }
}
