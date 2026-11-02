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
/// PARTIAL: The PC-works-themselves flow: approach AI, arrival/cancel hooks,
/// hand-off, abort, resume.
/// Split from TownLaborManager.cs (2026-10-02).
/// </summary>
internal static partial class TownLaborManager
{
    internal static bool HasActivePcSelfLabor()
    {
        for (int i = 0; i < Missions.Count; i++)
        {
            TownLaborMission m = Missions[i];
            if (m != null && m.isPcSelf)
            {
                return true;
            }
        }

        return false;
    }

    internal static TownLaborMission? FindActivePcSelf()
    {
        for (int i = 0; i < Missions.Count; i++)
        {
            TownLaborMission m = Missions[i];
            if (m != null && m.isPcSelf)
            {
                return m;
            }
        }

        return null;
    }

    /// <summary>
    /// Abort all PC self-work missions as Failed (fame hit via Quest.Fail).
    /// </summary>
    internal static bool AbortAllPcSelfLabor()
    {
        var list = new List<TownLaborMission>();
        for (int i = 0; i < Missions.Count; i++)
        {
            TownLaborMission m = Missions[i];
            if (m != null && m.isPcSelf)
            {
                list.Add(m);
            }
        }

        if (list.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < list.Count; i++)
        {
            try { Settle(list[i], TownLaborSettleKind.Failed); }
            catch (Exception ex) { Plugin.LogWarn("townlabor abort self: " + ex.Message); }
        }

        return true;
    }

    /// <summary>
    /// Hand PC self-work to a companion mid-job. Keeps remaining hours; switches to NPC labor.
    /// Locked reward pins stay (accept contract); workerSkill updates for invest-raise gate.
    /// </summary>
    internal static string? TryHandOffPcSelfTo(int missionId, Chara worker)
    {
        TownLaborMission? m = FindByMissionId(missionId);
        if (m == null || !m.isPcSelf)
        {
            return LaborText.T("town.msg.endedShort", LaborTerms.TownWork);
        }

        if (worker == null || worker.isDead)
        {
            return LaborText.T("town.msg.invalidChoice");
        }

        try
        {
            if (worker.IsPC)
            {
                return LaborText.T("town.msg.invalidChoice");
            }
        }
        catch { }

        if (LaborBusy.IsBusy(worker.uid))
        {
            return LaborBusy.BusyReason(worker) ?? LaborText.T("town.error.busy");
        }

        TownLaborJobDef? def = m.Def ?? TownLaborJobs.GetById(m.jobId);
        if (def == null)
        {
            return LaborText.T("town.error.invalidJob");
        }

        if (!IsCandidate(worker, def))
        {
            return LaborText.T("town.msg.invalidChoice");
        }

        Chara? client = m.GetClient() ?? ResolveChara(m.uidClient);
        if (client == null || client.isDead)
        {
            return LaborText.T("town.msg.clientGone");
        }

        // Clear PC self AI first.
        try { ClearPcSelfApproachAi(); } catch { }

        bool wasParty = false;
        try
        {
            wasParty = worker.IsPCParty
                || (EClass.pc?.party?.members?.Contains(worker) ?? false);
        }
        catch { wasParty = false; }

        if (wasParty)
        {
            try
            {
                try
                {
                    if (!worker.IsGlobal)
                    {
                        worker.SetGlobal();
                    }
                }
                catch { }
                EClass.pc?.party?.RemoveMember(worker);
            }
            catch (Exception ex)
            {
                Plugin.LogWarn("townlabor handoff remove party: " + ex.Message);
            }

            try { worker.c_wasInPcParty = true; } catch { }
        }

        // Convert mission identity to companion labor.
        m.isPcSelf = false;
        m.workStarted = true;
        m.uidWorker = worker.uid;
        try { m.workerName = worker.NameSimple ?? worker.Name ?? ("#" + worker.uid); }
        catch { m.workerName = "#" + worker.uid; }
        m.wasPartyMember = wasParty;
        try { m.workerSkill = TownLaborJobs.GetWorkerSkill(worker, m.skillId); } catch { }
        // Keep rewardMoney/plat/ticket locked at accept so board contract stays honest.

        try
        {
            PlaceWorkerNearClient(worker, client, force: true);
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("townlabor handoff place: " + ex.Message);
        }

        try
        {
            PrepareWorkingChara(worker);
            worker.SetAI(new AI_TownLabor { missionId = m.missionId });
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("townlabor handoff AI: " + ex.Message);
            try { worker.SetAIIdle(); } catch { }
        }

        try { RefreshTrackerQuests(); } catch { }
        try { Save(); } catch { }
        Plugin.LogInfo("townlabor handoff self->npc id=" + m.missionId + " worker=" + m.uidWorker
            + " hoursLeft=" + m.hoursLeft);
        return null;
    }

    /// <summary>Start PC walk-to-client AI. No teleport.</summary>
    static void StartPcSelfApproach(TownLaborMission mission, Chara? pc)
    {
        if (mission == null || pc == null || pc.isDead)
        {
            return;
        }

        try
        {
            // Already next to client: start work immediately.
            Chara? client = mission.GetClient();
            if (client != null
                && client.ExistsOnMap
                && pc.ExistsOnMap
                && EClass._zone != null
                && EClass._zone.uid == mission.uidZone)
            {
                int dist = 99;
                try { dist = pc.Dist(client); } catch { dist = 99; }
                if (dist <= AI_TownLaborPcSelf.ArriveDist)
                {
                    MarkPcSelfWorkStarted(mission, firstHourSp: true);
                    return;
                }
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
        try
        {
            pc.SetAIImmediate(new AI_TownLaborPcSelf { missionId = mission.missionId });
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("townlabor SetAIImmediate approach: " + ex.Message);
            try
            {
                pc.SetAI(new AI_TownLaborPcSelf { missionId = mission.missionId });
            }
            catch (Exception ex2)
            {
                Plugin.LogWarn("townlabor SetAI approach: " + ex2.Message);
            }
        }
    }

    /// <summary>Called by AI when PC reaches the shop client.</summary>
    internal static void NotifyPcSelfArrived(int missionId)
    {
        TownLaborMission? m = FindByMissionId(missionId);
        if (m == null || !m.isPcSelf || m.workStarted)
        {
            return;
        }

        MarkPcSelfWorkStarted(m, firstHourSp: true);
    }

    /// <summary>Approach cancelled (manual cancel / damage / path fail) => real fail.</summary>
    internal static void NotifyPcSelfApproachCancelled(int missionId)
    {
        TownLaborMission? m = FindByMissionId(missionId);
        if (m == null || !m.isPcSelf)
        {
            return;
        }

        // Work already started: the shift is only PAUSED, not given up. Moving the
        // PC cancels the work AI; settling on that silently threw the whole shift
        // away, which is what players were hitting (2026-10-30 report: "玩家走动会
        // 脱离自动流逝时间"). The shift keeps ticking (same rule as leaving town),
        // and the player can walk back to the shopkeeper and resume from the
        // middle-click action. Giving up explicitly is still 停下（无主奖）.
        if (m.workStarted)
        {
            Plugin.LogInfo("townlabor self work paused id=" + m.missionId);
            try
            {
                Msg.Say(NpcLabor.LaborText.T("town.msg.paused",
                    string.IsNullOrEmpty(m.clientName) ? NpcLabor.LaborTerms.TownWork : m.clientName));
            }
            catch { }
            return;
        }

        Plugin.LogInfo("townlabor self approach cancel id=" + m.missionId);
        Settle(m, TownLaborSettleKind.Failed);
    }

    /// <summary>
    /// Whether a resume entry should be offered for this shopkeeper: a running
    /// PC-self shift on them that the work AI is not currently driving.
    /// </summary>
    internal static bool CanResumePcSelf(Chara? client)
    {
        if (client == null)
        {
            return false;
        }

        TownLaborMission? m = FindByClient(client.uid);
        if (m == null || !m.isPcSelf)
        {
            return false;
        }

        Chara? pc = null;
        try { pc = EClass.pc; } catch { pc = null; }
        if (pc == null || pc.isDead)
        {
            return false;
        }

        try
        {
            if (pc.ai is AI_TownLaborPcSelf running && running.missionId == m.missionId && running.IsRunning)
            {
                return false;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }

        return true;
    }

    /// <summary>
    /// Re-attach the self-work AI for a mission that is still running but whose
    /// worker is no longer driven by it (player walked away, AI was cancelled).
    /// Returns false when there is nothing to resume.
    /// </summary>
    internal static bool TryResumePcSelf(Chara? client)
    {
        if (client == null)
        {
            return false;
        }

        TownLaborMission? m = FindByClient(client.uid);
        if (m == null || !m.isPcSelf)
        {
            return false;
        }

        Chara? pc = null;
        try { pc = EClass.pc; } catch { pc = null; }
        if (pc == null || pc.isDead)
        {
            return false;
        }

        try
        {
            if (pc.ai is AI_TownLaborPcSelf running && running.missionId == m.missionId && running.IsRunning)
            {
                return false;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }

        try
        {
            pc.SetAIImmediate(new AI_TownLaborPcSelf { missionId = m.missionId });
            Plugin.LogInfo("townlabor self work resumed id=" + m.missionId);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("townlabor resume: " + ex.Message);
            return false;
        }
    }

    static void MarkPcSelfWorkStarted(TownLaborMission m, bool firstHourSp)
    {
        if (m == null || m.workStarted)
        {
            return;
        }

        m.workStarted = true;

        Chara? pc = null;
        try { pc = EClass.pc; } catch { pc = null; }

        if (firstHourSp && pc != null && !pc.isDead)
        {
            int spMax = 1;
            try { spMax = pc.stamina != null ? Math.Max(1, pc.stamina.max) : 1; } catch { spMax = 1; }
            int startSp = SelfWorkSpCost(spMax, firstHour: true);
            try
            {
                if (pc.stamina != null && startSp > 0)
                {
                    pc.stamina.Mod(-startSp);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("townlabor self arrive sp: " + ex.Message);
            }
        }

        string job = string.IsNullOrEmpty(m.jobTitle) ? NpcLabor.LaborTerms.TownWork : m.jobTitle;
        string msg = NpcLabor.LaborText.T("town.msg.workStart", m.clientName, job);
        try { Msg.Say(msg); } catch { }
        Plugin.LogInfo("townlabor self work started id=" + m.missionId);

        try { Save(); } catch { }
        try { RefreshTrackerQuests(); } catch { }
    }

    static void MaybeResumePcSelfApproach(TownLaborMission m)
    {
        if (m == null || !m.isPcSelf || m.workStarted)
        {
            return;
        }

        Chara? pc = null;
        try { pc = EClass.pc; } catch { pc = null; }
        if (pc == null || pc.isDead)
        {
            return;
        }

        // Only resume when PC is on the work map.
        try
        {
            if (EClass._zone == null || EClass._zone.uid != m.uidZone)
            {
                return;
            }
        }
        catch
        {
            return;
        }

        Chara? client = m.GetClient();
        if (client != null && client.ExistsOnMap && pc.ExistsOnMap)
        {
            int dist = 99;
            try { dist = pc.Dist(client); } catch { dist = 99; }
            if (dist <= AI_TownLaborPcSelf.ArriveDist)
            {
                MarkPcSelfWorkStarted(m, firstHourSp: true);
                return;
            }
        }

        // Already walking for this mission.
        try
        {
            if (pc.ai is AI_TownLaborPcSelf a && a.missionId == m.missionId && a.IsRunning)
            {
                return;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
StartPcSelfApproach(m, pc);
    }

    static void ClearPcSelfApproachAi()
    {
        Chara? pc = null;
        try { pc = EClass.pc; } catch { pc = null; }
        if (pc == null)
        {
            return;
        }

        try
        {
            if (pc.ai is AI_TownLaborPcSelf)
            {
                // Success path already ends AI; cancel only if still running.
                if (pc.ai.IsRunning)
                {
                    pc.ai.Success();
                }
            }
        }
        catch
        {
            try
            {
                if (pc.ai is AI_TownLaborPcSelf)
                {
                    pc.SetAIIdle();
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
}
    }
}
