using HarmonyLib;
using NpcLabor.CoCraft;

namespace NpcLabor.Patches;

[HarmonyPatch(typeof(TraitCrafter), nameof(TraitCrafter.GetCostSp))]
internal static class SpPatches
{
    [HarmonyPostfix]
    internal static void Postfix(AI_UseCrafter ai, ref int __result)
    {
        if (!CoCraftSession.Active)
        {
            CoCraftSession.DiagSpSkips++;
            Plugin.LogDebug($"[co] GetCostSp SKIP: session inactive, raw={__result} unchanged");
            return;
        }

        int raw = __result;
        int adjusted = CoCraftSession.AdjustCostSp(raw);
        __result = adjusted;
        int duration = CoCraftSession.Duration;
        int num = ai != null ? ai.num : CoCraftSession.BatchNum;
        CoCraftSession.RememberTiming(adjusted, duration > 0 ? duration : CoCraftSession.Duration, num);
        CoCraftSession.DiagSpHits++;
        CoCraftSession.DiagLastSpRaw = raw;
        CoCraftSession.DiagLastSpAdjusted = adjusted;
        Plugin.LogDebug(
            $"[co] GetCostSp raw={raw} -> {adjusted} cut={raw - adjusted} " +
            $"npcSkill={CoCraftSession.NpcSkill} perSkill={CoCraftSession.NpcSpPerSkill} " +
            $"dur={duration} num={num}");
    }
}

[HarmonyPatch(typeof(TraitCrafter), nameof(TraitCrafter.GetDuration))]
internal static class DurationRememberPatch
{
    [HarmonyPostfix]
    internal static void Postfix(AI_UseCrafter ai, int costSp, ref int __result)
    {
        if (!CoCraftSession.Active)
        {
            CoCraftSession.DiagSpSkips++;
            Plugin.LogDebug($"[co] GetDuration SKIP: session inactive");
            return;
        }

        // Duration already used eff via Evalue rewrite; just remember for NPC exp.
        int num = ai != null ? ai.num : CoCraftSession.BatchNum;
        CoCraftSession.RememberTiming(costSp, __result, num);
        // AI_UseCrafter.Run charges stamina.Mod(-costSp) with this exact value.
        Plugin.LogDebug($"[co] GetDuration costSpCharged={costSp} duration={__result} num={num}");
    }
}
