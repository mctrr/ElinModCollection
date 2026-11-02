using HarmonyLib;
using NpcLabor.Process;

namespace NpcLabor.Patches;

/// <summary>
/// When the player picks up a parked ingredient belonging to the active processor
/// job, immediately abort the NPC job. Finished products on the same cell are
/// ignored. Uses a prefix so the ingredient is still on the map when we match it.
/// </summary>
[HarmonyPatch(typeof(Chara), nameof(Chara.Pick))]
internal static class ProcessorPickPatch
{
    [HarmonyPrefix]
    static bool Prefix(Chara __instance, Thing t)
    {
        try
        {
            if (ProcessorJobSession.Count == 0 || __instance == null || t == null || t.isDestroyed)
            {
                return true;
            }

            if (!__instance.IsPC)
            {
                return true;
            }

            // Abort whichever live job claims the picked item (any machine, not just Current).
            var jobs = ProcessorJobSession.Jobs;
            for (int j = 0; j < jobs.Count; j++)
            {
                ProcessorJob job = jobs[j];
                if (job == null || !job.Active)
                {
                    continue;
                }

                // Guard against reentrancy while Clear/ReturnIngredients places leftovers.
                if (job.IsReturningIngredients)
                {
                    continue;
                }

                bool isIngredient = false;
                var ings = job.Ingredients;
                for (int i = 0; i < ings.Count; i++)
                {
                    Thing? ing = ings[i];
                    if (ing != null && !ing.isDestroyed && ing.uid == t.uid)
                    {
                        isIngredient = true;
                        break;
                    }
                }

                if (isIngredient)
                {
                    ProcessorJobSession.Clear(job, "pc-pick");
                    return false;
                }
            }
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("processor pick abort: " + ex.Message);
        }

        return true;
    }
}
