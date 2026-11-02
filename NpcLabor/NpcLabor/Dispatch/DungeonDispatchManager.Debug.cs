using NpcLabor.TownLabor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace NpcLabor.Dispatch;

/// <summary>
/// PARTIAL: Console debug helpers and full runtime clear.
/// Split from DungeonDispatchManager.cs (2026-10-02).
/// </summary>
internal static partial class DungeonDispatchManager
{
    /// <summary>
    /// Debug: dump planned full success harvest for every active mission.
    /// Region missions ensure plan first; non-region notes explore-scale only.
    /// </summary>
    internal static string DebugDumpSuccessHarvests()
    {
        try
        {
            if (Missions.Count == 0)
            {
                string empty = NpcLabor.LaborText.T(
                    "dis.msg.empty",
                    NpcLabor.LaborTerms.DungeonExplore,
                    NpcLabor.LaborTerms.RegionDispatch);
                try { Msg.Say(empty); } catch { }
                Plugin.LogInfo("dispatch debug dump: no missions");
                return empty;
            }

            var lines = new List<string>();
            for (int i = 0; i < Missions.Count; i++)
            {
                DungeonDispatchMission m = Missions[i];
                if (m == null)
                {
                    continue;
                }

                m.EnsureMemberList();
                string head = "#" + m.missionId + " " + (m.zoneName ?? "?")
                    + " [" + m.MemberNames() + "]"
                    + " progress=" + m.ProgressPercent + "%"
                    + " weeks=" + Math.Max(1, m.exploreWeeks)
                    + " skill=E" + m.exploreSkill + "/G" + m.gatherSkill + "/L" + m.lockpickSkill
                    + " haul=" + DungeonDispatchRewards.PlannedHaulTotal(m)
                    + " (ex" + DungeonDispatchRewards.PlannedExploreHaulTotal(m)
                    + "+fd" + DungeonDispatchRewards.PlannedGatherHaulTotal(m)
                    + "+lk" + DungeonDispatchRewards.PlannedLockpickChestCount(m) + ")"
                    + " x" + (m.haulVariance > 0.01f ? m.haulVariance.ToString("0.00") : "1.00");

                string haul;
                if (m.isRegion)
                {
                    haul = DungeonDispatchRewards.FormatPlannedSuccessHarvest(m, 16);
                }
                else
                {
                    haul = NpcLabor.LaborText.T("dis.debug.nonRegion")
                        + m.LootSummary(8);
                }

                string line = head + " => " + haul;
                lines.Add(line);
                Plugin.LogInfo("dispatch debug harvest " + line);
            }

            string msg = NpcLabor.LaborText.T("dis.debug.harvestDump", lines.Count) + string.Join(" | ", lines);
            try { Msg.Say(msg); } catch { }
            return msg;
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("dispatch debug dump failed: " + ex.Message);
            return ex.Message;
        }
    }

    /// <summary>
    /// Debug: force-complete all active missions as Success and deliver mail.
    /// </summary>
    internal static int DebugCompleteAllSuccess()
    {
        int n = 0;
        try
        {
            // Copy list because Settle removes from Missions.
            var list = Missions.ToList();
            if (list.Count == 0)
            {
                try { Msg.Say(NpcLabor.LaborText.T("dis.debug.noMission")); } catch { }
                return 0;
            }

            for (int i = 0; i < list.Count; i++)
            {
                DungeonDispatchMission m = list[i];
                if (m == null)
                {
                    continue;
                }

                try
                {
                    // Ensure region plan exists so success mail uses locked haul.
                    if (m.isRegion)
                    {
                        DungeonDispatchRewards.EnsureRegionPlanPublic(m);
                    }

                    Settle(m, DispatchSettleKind.Success, destroyRandom: m.isRandomSite);
                    n++;
                }
                catch (Exception ex)
                {
                    Plugin.LogWarn("dispatch debug complete mission " + m.missionId + ": " + ex.Message);
                }
            }

            string msg = NpcLabor.LaborText.T("dis.debug.forceDone", n);
            try { Msg.Say(msg); } catch { }
            Plugin.LogInfo("dispatch debug complete-all n=" + n);
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("dispatch debug complete-all failed: " + ex.Message);
        }

        return n;
    }

    /// <summary>Debug: force-complete one mission by id or member uid.</summary>
    internal static bool DebugCompleteOne(int missionIdOrCharaUid)
    {
        try
        {
            DungeonDispatchMission? m = FindByMissionId(missionIdOrCharaUid);
            if (m == null)
            {
                m = FindByChara(missionIdOrCharaUid);
            }

            if (m == null)
            {
                try { Msg.Say(NpcLabor.LaborText.T("dis.debug.notFound", missionIdOrCharaUid)); } catch { }
                return false;
            }

            if (m.isRegion)
            {
                DungeonDispatchRewards.EnsureRegionPlanPublic(m);
            }

            Settle(m, DispatchSettleKind.Success, destroyRandom: m.isRandomSite);
            try { Msg.Say(NpcLabor.LaborText.T("dis.debug.forceDoneOne", m.missionId, m.zoneName)); } catch { }
            return true;
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("dispatch debug complete-one failed: " + ex.Message);
            return false;
        }
    }

    internal static void ClearAllRuntime()
    {
        Missions.Clear();
        try { ClearRegionMapMarkers(); } catch { }
    }

    /// <summary>
    /// Pure visual overworld pins for active region dispatches.
    /// Uses the 旅行商人营地 (tinkerCamp) zone icon tile so the outing is easy to
    /// spot on EloMap. Never pass "iconFlag" as an AddLight prefab id — that is a
    /// zone tile tag and leaves null SpriteRenderers that NRE in OnChangeHour.
    /// Old elolight pins are still scrubbed/removed for save/session safety.
    /// </summary>
}
