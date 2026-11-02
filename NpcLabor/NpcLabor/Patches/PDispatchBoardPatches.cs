using System;
using System.Reflection;
using HarmonyLib;
using NpcLabor.PermanentDispatch;

namespace NpcLabor.Patches;

/// <summary>
/// Permanent postings live on the quest board as ordinary rows, under the town
/// labor rows (player spec 2026-10-31: one small town has one or two postings, a
/// dedicated button was overkill). Priority.Last keeps us after the town labor
/// postfix so the order on screen is vanilla quests -> shop labor -> postings.
/// </summary>
[HarmonyPatch(typeof(LayerQuestBoard), nameof(LayerQuestBoard.RefreshQuest))]
internal static class PDispatchBoardPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    static void Postfix(LayerQuestBoard __instance)
    {
        try
        {
            PermanentDispatchUi.AppendBoardRows(__instance);
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("pdispatch board rows: " + ex.Message);
        }
    }
}

/// <summary>
/// PC quest journal (任务) rows for running postings: remaining time comes from
/// the row's deadline, cancelling comes from the vanilla 放弃 button. The rows
/// are display-only, so nothing of ours is serialized into the save.
///
/// ContentQuest is internal to the character info layer, hence the runtime type
/// lookup: if a game update renames it the patch simply stops applying.
/// </summary>
[HarmonyPatch]
internal static class PDispatchJournalPatch
{
    [HarmonyPrepare]
    static bool Prepare() => FindType() != null;

    [HarmonyTargetMethod]
    static MethodBase? TargetMethod()
    {
        Type? t = FindType();
        return t == null ? null : AccessTools.Method(t, "OnSwitchContent", new[] { typeof(int) });
    }

    [HarmonyPostfix]
    static void Postfix(object __instance)
    {
        try
        {
            PermanentDispatchUi.AppendJournalRows(__instance);
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("pdispatch journal rows: " + ex.Message);
        }
    }

    static Type? FindType()
    {
        try
        {
            return AccessTools.TypeByName("ContentQuest");
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// 放弃 on a posting means "recall and settle", not "fail a quest". Vanilla
/// Quest.Fail docks fame by 10% and cannot be overridden, so it is short-circuited
/// for our row type.
/// </summary>
[HarmonyPatch(typeof(Quest), nameof(Quest.Fail))]
internal static class PDispatchCancelPatch
{
    [HarmonyPrefix]
    static bool Prefix(Quest __instance)
    {
        if (__instance is not QuestNpcLaborPDispatch row || !row.active)
        {
            return true;
        }

        try
        {
            PermanentDispatchManager.Cancel(row.defId);
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("pdispatch cancel: " + ex.Message);
        }

        return false;
    }
}
