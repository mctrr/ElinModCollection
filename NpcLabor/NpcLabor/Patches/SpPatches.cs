using HarmonyLib;
using NpcLabor.CoCraft;

namespace NpcLabor.Patches;

[HarmonyPatch(typeof(TraitCrafter), nameof(TraitCrafter.GetCostSp))]
internal static class SpPatches
{
    [HarmonyPostfix]
    static void Postfix(AI_UseCrafter ai, ref int __result)
    {
        if (!CoCraftSession.Active)
        {
            return;
        }

        int adjusted = CoCraftSession.AdjustCostSp(__result);
        __result = adjusted;
        int duration = CoCraftSession.Duration;
        int num = ai != null ? ai.num : CoCraftSession.BatchNum;
        CoCraftSession.RememberTiming(adjusted, duration > 0 ? duration : CoCraftSession.Duration, num);
    }
}

[HarmonyPatch(typeof(TraitCrafter), nameof(TraitCrafter.GetDuration))]
internal static class DurationRememberPatch
{
    [HarmonyPostfix]
    static void Postfix(AI_UseCrafter ai, int costSp, ref int __result)
    {
        if (!CoCraftSession.Active)
        {
            return;
        }

        // Duration already used eff via Evalue rewrite; just remember for NPC exp.
        int num = ai != null ? ai.num : CoCraftSession.BatchNum;
        CoCraftSession.RememberTiming(costSp, __result, num);
    }
}
