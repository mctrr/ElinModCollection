using HarmonyLib;

namespace NpcLabor.Patches;

/// <summary>
/// Last-resort guard on the vanilla quest tracker for a destroyed instance.
///
/// <c>Widget.Deactivate()</c> is <c>DestroyImmediate(gameObject)</c>, but the static
/// <c>WidgetQuestTracker.Instance</c> field keeps pointing at the corpse until some
/// later <c>OnActivate</c> overwrites it. Vanilla's row 追踪 button
/// (<c>ContentQuest.OnSwitchContent</c> -> button3) does:
///
/// <code>
/// if (!WidgetQuestTracker.Instance) { ActivateWidget("QuestTracker"); }
/// WidgetQuestTracker.Instance.Refresh();
/// </code>
///
/// When nothing in <c>game.quests.list</c> is pinned, activating the widget runs
/// <c>OnActivate -> Refresh</c>, which ends in <c>items.Count == 0</c> ->
/// <c>DeactivateWidget(this)</c> — the widget destroys itself mid-flight. The next
/// statement then calls <c>Refresh()</c> on that destroyed object, and
/// <c>this.RebuildLayout()</c> NREs inside <c>Component.get_transform</c>.
///
/// NpcLabor hits this through display-only rows that never enter
/// <c>game.quests.list</c> (their 追踪 button is now suppressed in
/// <c>PermanentDispatchUi</c>), but the vanilla defect is reachable from any caller,
/// so the tracker itself gets a cheap check: a dead instance has nothing to refresh.
/// Skipping is exactly the behaviour of the "widget not active" path.
/// </summary>
[HarmonyPatch(typeof(WidgetQuestTracker), nameof(WidgetQuestTracker.Refresh))]
internal static class QuestTrackerDeadInstanceGuard
{
    [HarmonyPrefix]
    static bool Prefix(WidgetQuestTracker __instance)
    {
        try
        {
            if (__instance != null)
            {
                return true;
            }

            Plugin.LogDebug("quest tracker refresh skipped: instance already destroyed");
            return false;
        }
        catch
        {
            return true;
        }
    }
}
