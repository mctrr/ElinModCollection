using HarmonyLib;
using NpcLabor.CoCraft;
using UnityEngine;

namespace NpcLabor.Patches;

/// <summary>
/// Vanilla BaseListPeople.OnInstantiate fades off-map residents with:
///   gameObject.AddComponent&lt;CanvasGroup&gt;().alpha = 0.6f
/// Dispatch moves workers off the home zone, so they hit this path.
/// DynamicScrollView recycles ItemGeneralPeople rows; the leftover
/// CanvasGroup makes AddComponent return null and NRE on .alpha.
/// Recycled home rows would also stay faded. Drop the leftover first.
/// </summary>
[HarmonyPatch(typeof(BaseListPeople), nameof(BaseListPeople.OnInstantiate))]
internal static class ResidentBoardCanvasGroupPatch
{
    [HarmonyPrefix]
    static void Prefix(ItemGeneral b)
    {
        if (b == null)
        {
            return;
        }

        try
        {
            CanvasGroup existing = b.GetComponent<CanvasGroup>();
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("resident board CanvasGroup: " + ex.Message);
        }
    }
}

/// <summary>
/// Vanilla Msg.Say("isIn", chara, zoneName) fills #1=chara, #2=zone.
/// CN LangGame isIn is "#2在#1那里", which prints the zone as the person.
/// Say our own ordered line and skip the vanilla message.
/// </summary>
[HarmonyPatch(typeof(BaseListPeople), nameof(BaseListPeople.OnClick))]
internal static class ResidentBoardAwayTextPatch
{
    [HarmonyPrefix]
    static bool Prefix(Chara c)
    {
        try
        {
            if (c == null || c.IsAliveInCurrentZone)
            {
                return true;
            }

            if (c.currentZone != EClass._zone)
            {
                string who = AssistantResolver.NameOf(c);
                string where = c.currentZone == null ? "???" : (c.currentZone.Name ?? "???");
                Msg.SayRaw(NpcLabor.LaborText.T("dis.msg.isIn", who, where));
            }

            SE.BeepSmall();
            return false;
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("resident board away text: " + ex.Message);
            return true;
        }
    }
}
