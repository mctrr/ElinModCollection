using HarmonyLib;
using NpcLabor.CoCraft;
using UnityEngine;

namespace NpcLabor.Patches;

/// <summary>
/// After each successful PC craft ModExp (per item in batch / per repeat),
/// grant the assistant raw * 0.5 on the same skill id.
/// Skips chain/parent drip calls so NPC ModExp applies its own parent factor once.
/// </summary>
[HarmonyPatch(typeof(ElementContainer), nameof(ElementContainer.ModExp))]
internal static class ExpPatches
{
    static bool _granting;

    [HarmonyPostfix]
    static void Postfix(ElementContainer __instance, int ele, float a, bool chain)
    {
        if (_granting || chain || !CoCraftSession.Active || a <= 0f)
        {
            return;
        }

        Card? card = __instance?.Card;
        if (card == null || !card.IsPC)
        {
            return;
        }

        if (ele != CoCraftSession.ReqSkillId && ele != CoCraftSession.DurationSkillId)
        {
            return;
        }

        Chara? npc = CoCraftSession.GetAssistant();
        if (npc?.elements == null)
        {
            return;
        }

        int give = Mathf.Max(1, Mathf.RoundToInt(a * CoCraftSession.NpcExpShare));
        _granting = true;
        try
        {
            npc.elements.ModExp(ele, give);
            CoCraftSession.AddNpcExpGranted(give);
            Plugin.LogDebug($"npc exp +{give} skill={ele} from pc raw={a:0.#}");
        }
        catch (System.Exception ex)
        {
            Plugin.LogWarn($"npc exp share failed: {ex.Message}");
        }
        finally
        {
            _granting = false;
        }
    }
}
