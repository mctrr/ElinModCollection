using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using NpcLabor.CoCraft;
using NpcLabor.Dispatch;
using NpcLabor.Process;
using UnityEngine;

namespace NpcLabor.TownLabor;

/// <summary>
/// PARTIAL: Worker candidate listing and eligibility.
/// Split from TownLaborManager.cs (2026-10-02).
/// </summary>
internal static partial class TownLaborManager
{
    internal static List<Chara> ListCandidates(TownLaborJobDef? def = null)
    {
        var list = new List<Chara>();
        var seen = new HashSet<int>();

        void Consider(Chara? c)
        {
            if (c == null || !seen.Add(c.uid))
            {
                return;
            }

            if (!IsCandidate(c, def))
            {
                return;
            }

            list.Add(c);
        }

        try
        {
            // Prefer currently-loaded eligible pool (party + residents).
            foreach (Chara c in AssistantResolver.EnumerateEligible())
            {
                Consider(c);
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
// Also accept home-branch members not currently in zone (dispatch-style).
        try
        {
            FactionBranch? branch = EClass.BranchOrHomeBranch ?? EClass.Branch;
            if (branch?.members != null)
            {
                foreach (Chara m in branch.members)
                {
                    Consider(m);
                }
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
list.Sort((a, b) =>
        {
            int pa = IsParty(a) ? 0 : 1;
            int pb = IsParty(b) ? 0 : 1;
            int cmp = pa.CompareTo(pb);
            if (cmp != 0)
            {
                return cmp;
            }

            int sa = def != null && def.SkillId > 0 ? SafeSkill(a, def.SkillId) : 0;
            int sb = def != null && def.SkillId > 0 ? SafeSkill(b, def.SkillId) : 0;
            cmp = sb.CompareTo(sa);
            return cmp != 0 ? cmp : a.uid.CompareTo(b.uid);
        });
        return list;
    }

    internal static bool IsCandidate(Chara? c, TownLaborJobDef? def = null)
    {
        if (c == null || PersonPickerUi.IsPcLike(c) || c.isDead)
        {
            return false;
        }

        if (PersonPickerUi.IsStayHomeUnique(c))
        {
            return false;
        }

        if (LaborBusy.IsBusy(c.uid))
        {
            return false;
        }

        try
        {
            return LaborRoster.IsInPool(c);
        }
        catch
        {
            return false;
        }
    }

    static bool IsParty(Chara c) => LaborRoster.IsPartyMember(c);

    static int SafeSkill(Chara c, int skillId)
    {
        try
        {
            return skillId > 0 ? c.Evalue(skillId) : 0;
        }
        catch
        {
            return 0;
        }
    }
}
