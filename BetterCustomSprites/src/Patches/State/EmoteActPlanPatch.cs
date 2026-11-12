using ACS.API;
using ACS.Frame;
using HarmonyLib;

namespace ACS.Patches;

/// <summary>
/// Inject an emote button onto the PC's own tile, same as Deux ActPeek:
/// LeftMouse HUD when hovering self, and AllAction (default middle-click menu).
/// Patches _Update so the act is in the list before Update writes HUD text.
/// Only appears when the current PC skin has an emote / emote.* ACS clip.
/// </summary>
[HarmonyPatch(typeof(ActPlan), nameof(ActPlan._Update))]
internal static class EmoteActPlanPatch
{
    [HarmonyPostfix]
    private static void Postfix(ActPlan __instance)
    {
        if (EClass.core is null || !EClass.core.IsGameStarted || EClass.pc is null) {
            return;
        }

        if (__instance.input != ActInput.LeftMouse && __instance.input != ActInput.AllAction) {
            return;
        }

        if (__instance.dist != 0) {
            return;
        }

        if (!PlayState.HasEmoteClip(EClass.pc)) {
            return;
        }

        foreach (ActPlan.Item item in __instance.list) {
            if (item.act is ActEmote) {
                return;
            }
        }

        __instance.TrySetAct(new ActEmote(), EClass.pc);
    }
}
