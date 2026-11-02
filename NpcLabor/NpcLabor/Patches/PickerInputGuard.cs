using HarmonyLib;
using NpcLabor.CoCraft;

namespace NpcLabor.Patches;

/// <summary>
/// The co-craft assistant picker is a <c>LayerList</c> floating on top of the craft
/// window. Closing it any way it goes — the X button, ESC, a right-click outside, or
/// <c>SetList2</c> auto-closing after a pick — hands the dismissing click to the craft
/// button underneath and starts a craft nobody asked for.
///
/// Why it happens: Unity's GraphicRaycaster hands one click to every Graphic it hits,
/// and <c>UIButton</c> does not dispatch a press where it was received. It queues the
/// press in <c>OnPointerDown</c> and fires it from a <c>WaitForEndOfFrame</c>
/// callback. By the time that callback runs, the picker is already gone, so the press
/// lands on the craft button below. The stack in the log is exactly that:
/// <c>UIButton.&lt;OnPointerDown&gt;b__68_1 → _OnPress → OnClickCraft</c>.
///
/// Why the hook is on <c>Close</c> and not <c>_Close</c>: <c>Layer.Close</c> plays the
/// close-out tween and only calls <c>_Close</c> from the tween's OnComplete, which is
/// frames later — long after the press was dispatched. A hook on <c>_Close</c> was
/// mounted, audited as healthy, and never fired once (logged "picker closed" zero times
/// while <c>OnClickCraft</c> fired five). <c>Close</c> runs synchronously while the
/// dismissing click is still the current one, which is the only moment late enough to
/// matter and early enough to work.
///
/// The fix itself is one call — <c>EInput.Consume</c>, which raises
/// <c>EInput.skipFrame</c>. Every deferred press re-checks that flag before firing
/// (<c>UIButton.&lt;OnPointerDown&gt;b__68_1</c>: <c>if (EInput.skipFrame &gt; 0) return;</c>),
/// so the press is dropped. That is the engine's own "this input is already handled"
/// contract, not a timing guess. Vanilla already does this inside <c>ELayer._Close</c>;
/// it just does it too late to help.
/// </summary>
[HarmonyPatch(typeof(Layer), nameof(Layer.Close))]
internal static class PickerCloseGuard
{
    [HarmonyPrefix]
    static void Prefix(Layer __instance)
    {
        try
        {
            PersonPickerUi.NotePickerDismissed(__instance);
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("PickerCloseGuard: " + ex.Message);
        }
    }
}
