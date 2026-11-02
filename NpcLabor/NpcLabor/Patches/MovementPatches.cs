using HarmonyLib;

using NpcLabor.CoCraft;
using NpcLabor.Process;

namespace NpcLabor.Patches;

/// <summary>
/// Allow labor NPCs to walk through other characters (equivalent to vanilla
/// party-follow behavior): processor workers (AI_NpcProcess), co-craft
/// assistants walking over to the PC, and base-production residents walking
/// to a workbench. A waiting NPC at a door must not block the assistant —
/// the snap-teleport stays as a last-resort fallback only.
/// </summary>
[HarmonyPatch(typeof(Chara))]
public static class MovementPatches
{
    [HarmonyPatch(nameof(Chara.CanMoveTo))]
    [HarmonyPrefix]
    public static bool CanMoveTo_Prefix(Chara __instance, Point p, ref bool __result)
    {
        if (__instance == null || p == null)
            return true;

        bool isProcessorWorker = ProcessorJobSession.ForWorker(__instance.uid) != null;

        bool isCoCraftAssistant = CoCraftSession.Active
            && CoCraftSession.NpcUid != 0
            && __instance.uid == CoCraftSession.NpcUid;

        // Base production: a resident with a production order is either walking to a bench
        // or standing at one. Without this the same blockage that stops an assistant stops
        // the whole shift — the worker never gets within reach of the station.
        bool isCraftWorker = NpcLabor.Craft.CraftManager.Has(__instance.uid);

        if (!isProcessorWorker && !isCoCraftAssistant && !isCraftWorker)
            return true;

        // Allow the worker/assistant to enter tiles occupied by other characters
        // (same behavior as party members following the PC)
        if (p.HasChara && !__instance.IsMultisize && !__instance.CanReplace(p.FirstChara))
        {
            __result = true;
            return false; // Skip original HasChara block
        }

        return true;
    }
}
