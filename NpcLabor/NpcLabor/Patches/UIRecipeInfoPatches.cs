using HarmonyLib;
using NpcLabor.CoCraft;
using UnityEngine;

namespace NpcLabor.Patches;

/// <summary>
/// Craft panel skill line uses elements.Value, not Card.Evalue, so co-craft eff never shows.
/// Rewrite textReqSkill to a short "need / 有效 eff" line when assist is available.
/// </summary>
[HarmonyPatch(typeof(UIRecipeInfo))]
internal static class UIRecipeInfoPatches
{
    // The craft panel refreshes on every hover, so a naive log here would flood
    // LogOutput.log. Only emit when the rendered diagnostic actually changes, and
    // only while verbose logging is on (so turning the switch on re-arms it).
    static string _lastReqDiag = "";
    static string _lastSpDiag = "";

    static void DiagOnce(ref string slot, string msg)
    {
        if (!Plugin.VerboseLog || slot == msg)
        {
            return;
        }

        slot = msg;
        Plugin.LogDebug(msg);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(UIRecipeInfo.RefreshList))]
    static void RefreshListPostfix(UIRecipeInfo __instance)
    {
        try
        {
            RewriteReqSkill(__instance);
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug($"UIRecipeInfo skill rewrite skipped: {ex.Message}");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(UIRecipeInfo.RefreshBalance))]
    static void RefreshBalancePostfix(UIRecipeInfo __instance)
    {
        try
        {
            RewriteReqSkill(__instance);
            RewriteCostSpPreview(__instance);
        }
        catch
        {
            // never break craft UI
        }
    }

    static void RewriteReqSkill(UIRecipeInfo info)
    {
        if (info?.textReqSkill == null || info.recipe?.source == null || EClass.pc == null)
        {
            return;
        }

        Element reqSkill = info.recipe.source.GetReqSkill();
        if (reqSkill == null || reqSkill.id <= 0)
        {
            return;
        }

        if (!AssistantResolver.TryPreviewEff(reqSkill.id, out _, out _, out int eff, out _))
        {
            // Off / no assistant: leave vanilla text (pc only).
            DiagOnce(ref _lastReqDiag,
                $"[co] ui reqSkill id={reqSkill.id}: preview FAILED (assist off / no valid assistant) - vanilla text kept");
            return;
        }

        string skillName = reqSkill.Name ?? "";
        int need = reqSkill.Value;
        // Keep short: "制造 6 / 有效 88"
        string str = skillName.IsEmpty()
            ? NpcLabor.LaborText.T("craft.effLine", need, eff)
            : NpcLabor.LaborText.T("craft.effLineSkill", skillName, need, eff);
        FontColor c = eff < need ? FontColor.Warning : FontColor.Good;
        info.textReqSkill.SetText(str, c);
        DiagOnce(ref _lastReqDiag,
            $"[co] ui reqSkill id={reqSkill.id} need={need} eff={eff} color={c} text=\"{str}\"");
    }

    static void RewriteCostSpPreview(UIRecipeInfo info)
    {
        if (info?.textCostSP == null || info.recipe?.source == null)
        {
            return;
        }

        Element reqSkill = info.recipe.source.GetReqSkill();
        if (reqSkill == null || reqSkill.id <= 0)
        {
            return;
        }

        if (!AssistantResolver.TryPreviewEff(reqSkill.id, out _, out int npcSkill, out _, out _))
        {
            DiagOnce(ref _lastSpDiag, "[co] ui spPreview: FAILED (assist off / no valid assistant) - vanilla SP kept");
            return;
        }

        string raw = info.textCostSP.text;
        if (raw.IsEmpty() || raw == "-")
        {
            DiagOnce(ref _lastSpDiag, $"[co] ui spPreview: raw=\"{raw}\" not a number, skipped");
            return;
        }

        if (!int.TryParse(StripRich(raw), out int baseSp) || baseSp <= 0)
        {
            DiagOnce(ref _lastSpDiag, $"[co] ui spPreview: raw=\"{raw}\" parsed to {baseSp}, skipped");
            return;
        }

        int count = 1;
        try
        {
            if (info.summary != null && info.summary.countValid > 0)
            {
                count = info.summary.countValid;
            }
        }
        catch
        {
            count = 1;
        }

        int perUnit = baseSp / Mathf.Max(1, count);
        int reducedPer = Mathf.Max(1, perUnit - Mathf.FloorToInt(npcSkill * CoCraftSession.NpcSpPerSkill));
        // Same per-unit cut as CoCraftSession.AdjustCostSp, expanded back over the
        // batch because this panel shows a total. Never advertise a cost above vanilla.
        int total = Mathf.Clamp(reducedPer * Mathf.Max(1, count), 1, Mathf.Max(1, baseSp));
        bool warn = EClass.pc != null && total >= EClass.pc.stamina.value;
        FontColor c = warn ? FontColor.Warning : FontColor.Good;
        info.textCostSP.SetText(total.ToString(), c);
        DiagOnce(ref _lastSpDiag,
            $"[co] ui spPreview baseSp={baseSp} perUnit={perUnit} npcSkill={npcSkill} " +
            $"cut={Mathf.FloorToInt(npcSkill * CoCraftSession.NpcSpPerSkill)} " +
            $"reducedPer={reducedPer} count={count} -> total={total} color={c}");
    }

    static string StripRich(string s)
    {
        if (s.IsEmpty())
        {
            return s;
        }

        var sb = new System.Text.StringBuilder(s.Length);
        bool inTag = false;
        foreach (char ch in s)
        {
            if (ch == '<')
            {
                inTag = true;
                continue;
            }

            if (ch == '>')
            {
                inTag = false;
                continue;
            }

            if (!inTag)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Trim();
    }
}
