using HarmonyLib;
using NpcLabor.CoCraft;
using UnityEngine;

namespace NpcLabor.Patches;

/// <summary>
/// While co-craft session is active, PC skill reads for the recipe/duration req skill
/// return eff = pc + Floor(npc * 0.5). Covers GetDuration, GetQualityBonus, RecipeCard.Craft fail.
/// </summary>
[HarmonyPatch]
internal static class SkillPatches
{
    internal static bool BypassEvalue;

    static bool ShouldRewrite(Card? card, int skillId)
    {
        if (BypassEvalue || !CoCraftSession.Active || card == null)
        {
            return false;
        }

        if (EClass.pc == null || card != EClass.pc)
        {
            return false;
        }

        return skillId == CoCraftSession.ReqSkillId || skillId == CoCraftSession.DurationSkillId;
    }

    static int Rewrite(Card card, int skillId)
    {
        BypassEvalue = true;
        try
        {
            int pc = card.Evalue(skillId);
            int npcSkill = skillId == CoCraftSession.DurationSkillId &&
                           CoCraftSession.DurationSkillId != CoCraftSession.ReqSkillId
                ? GetDurationNpcSkill()
                : CoCraftSession.NpcSkill;
            return pc + Mathf.FloorToInt(npcSkill * CoCraftSession.NpcSkillWeight);
        }
        finally
        {
            BypassEvalue = false;
        }
    }

    // Duration skill: read it straight off the pinned assistant so a differing skill id
    // still resolves, and fall back to the session mirror the duration patch writes.
    static int GetDurationNpcSkill()
    {
        Chara? npc = CoCraftSession.GetAssistant();
        if (npc == null)
        {
            return CoCraftSession.NpcSkill;
        }

        return npc.Evalue(CoCraftSession.DurationSkillId);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Card), nameof(Card.Evalue), typeof(int))]
    static bool EvalueIntPrefix(Card __instance, int ele, ref int __result)
    {
        if (!ShouldRewrite(__instance, ele))
        {
            return true;
        }

        __result = Rewrite(__instance, ele);
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Card), nameof(Card.Evalue), typeof(int), typeof(bool))]
    static bool EvalueIntBoolPrefix(Card __instance, int ele, ref int __result)
    {
        if (!ShouldRewrite(__instance, ele))
        {
            return true;
        }

        __result = Rewrite(__instance, ele);
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Card), nameof(Card.Evalue), typeof(string))]
    static bool EvalueStringPrefix(Card __instance, string alias, ref int __result)
    {
        if (BypassEvalue || !CoCraftSession.Active || __instance == null || EClass.pc == null || __instance != EClass.pc)
        {
            return true;
        }

        if (alias.IsEmpty())
        {
            return true;
        }

        int id;
        try
        {
            id = EClass.sources.elements.alias[alias].id;
        }
        catch
        {
            return true;
        }

        if (!ShouldRewrite(__instance, id))
        {
            return true;
        }

        __result = Rewrite(__instance, id);
        return false;
    }
}
