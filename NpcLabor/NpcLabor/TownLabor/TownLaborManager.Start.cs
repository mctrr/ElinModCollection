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
/// PARTIAL: Mission start (companion + PC self), SP cost, recall.
/// Split from TownLaborManager.cs (2026-10-02).
/// </summary>
internal static partial class TownLaborManager
{
    /// <summary>
    /// Recall every companion mission whose work zone is the current zone.
    /// Leaving a work zone ends companion labor on the spot (Slice X: no
    /// off-map ticking). PC self-work is never touched here - the leave
    /// menu handles it. Returns how many missions were recalled (0 when
    /// nothing was active or the zone was null).
    /// </summary>
    internal static int RecallCompanionLaborInCurrentZone()
    {
        var list = new List<TownLaborMission>();
        try
        {
            Zone? cur = EClass._zone;
            if (cur == null)
            {
                return 0;
            }

            for (int i = 0; i < Missions.Count; i++)
            {
                TownLaborMission m = Missions[i];
                if (m == null || m.isPcSelf)
                {
                    continue;
                }

                try
                {
                    if (m.uidZone == cur.uid)
                    {
                        list.Add(m);
                    }
                }
                catch { }
            }
        }
        catch
        {
            return 0;
        }

        for (int i = 0; i < list.Count; i++)
        {
            try { Settle(list[i], TownLaborSettleKind.Recall); }
            catch (Exception ex) { Plugin.LogWarn("townlabor leave recall: " + ex.Message); }
        }

        return list.Count;
    }

    /// <summary>
    /// PC self-work: walk to client (no teleport), then same hours/rewards as companion labor.
    /// SP drains each hour only after arrival. No party hop.
    /// </summary>
    internal static string? TryStartSelf(Chara client, TownLaborJobDef def, int offeredHours = 0)
    {
        Chara? pc = null;
        try { pc = EClass.pc; } catch { pc = null; }
        if (pc == null || client == null || def == null)
        {
            return NpcLabor.LaborText.T("town.error.invalidJob");
        }

        Zone? zone = EClass._zone;
        if (!CanHostTownLabor(zone))
        {
            return NpcLabor.LaborText.T("town.error.noTownWork", NpcLabor.LaborTerms.TownWork);
        }

        if (zone == null)
        {
            return NpcLabor.LaborText.T("town.error.invalidPlace");
        }

        if (LaborBusy.IsBusy(pc.uid))
        {
            return LaborBusy.BusyReason(pc) ?? NpcLabor.LaborText.T("town.error.busy");
        }

        // Co-craft / process / dispatch already covered by LaborBusy above.
        try
        {
            if (CoCraftSession.Active)
            {
                return NpcLabor.LaborText.T("town.error.busyCoCraft");
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
        if (IsClientBusy(client.uid))
        {
            return NpcLabor.LaborText.T("town.error.clientBusy", NpcLabor.LaborTerms.TownWork);
        }

        if (CountInZone(zone.uid) >= TownLaborJobs.MaxConcurrentPerTown)
        {
            return NpcLabor.LaborText.T("town.error.maxConcurrent", TownLaborJobs.MaxConcurrentPerTown, NpcLabor.LaborTerms.TownWork);
        }

        if (TownLaborJobs.MatchJob(client) == null)
        {
            return NpcLabor.LaborText.T("town.error.jobGone");
        }

        // Need some stamina to start.
        int spNow = 0;
        int spMax = 1;
        try
        {
            spNow = pc.stamina != null ? pc.stamina.value : 0;
            spMax = pc.stamina != null ? Math.Max(1, pc.stamina.max) : 1;
        }
        catch
        {
            spNow = 0;
            spMax = 1;
        }

        if (spNow <= 0)
        {
            return NpcLabor.LaborText.T("town.error.noSp");
        }

        int hours = TownLaborJobs.ResolveWorkHours(offeredHours);
        int shopLv = TownLaborJobs.GetShopLv(client);
        int workerSkill = TownLaborJobs.GetWorkerSkill(pc, def.SkillId);
        int clientInvest = 0;
        try { clientInvest = Math.Max(0, client.c_invest); } catch { clientInvest = 0; }
        Zone? home = null;
        try
        {
            home = pc.homeZone
                ?? EClass.BranchOrHomeBranch?.owner
                ?? EClass.Branch?.owner;
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
string pcName;
        try { pcName = pc.NameSimple ?? pc.Name ?? NpcLabor.LaborText.T("town.you"); }
        catch { pcName = NpcLabor.LaborText.T("town.you"); }

        var mission = new TownLaborMission
        {
            missionId = _nextMissionId++,
            jobId = def.Id,
            Kind = def.Kind,
            uidWorker = pc.uid,
            uidClient = client.uid,
            uidZone = zone.uid,
            zoneId = zone.id ?? "",
            zoneName = SafeZoneName(zone),
            clientName = client.NameSimple ?? client.Name ?? ("#" + client.uid),
            workerName = pcName,
            jobTitle = TownLaborJobs.ResolveBoardTitle(client, def),
            skillId = def.SkillId,
            hoursTotal = hours,
            hoursLeft = hours,
            wasPartyMember = false,
            isPcSelf = true,
            workStarted = false,
            homeZoneUid = home?.uid ?? 0,
            shopLv = shopLv,
            workerSkill = workerSkill,
            clientInvest = clientInvest,
            rewardLog = new List<string>(),
        };

        TownLaborRewards.LockRewardPreview(mission);
        Missions.Add(mission);
        TryStartTrackerQuest(mission);

        // Walk to client first; hours/SP start only after arrival.
        try
        {
            StartPcSelfApproach(mission, pc);
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("townlabor self approach AI: " + ex.Message);
        }

        // If already adjacent, StartPcSelfApproach marks workStarted and says "开始".
        if (!mission.workStarted)
        {
            int startHours = Math.Max(1, hours);
            string msg = NpcLabor.LaborText.T("town.msg.youStart", mission.clientName, mission.jobTitle, startHours);
            try { Msg.Say(msg); } catch { }
        }

        Plugin.LogInfo("townlabor start SELF id=" + mission.missionId + " job=" + def.Id
            + " client=" + mission.uidClient + " hours=" + hours
            + " workStarted=" + mission.workStarted);

        try { Save(); } catch { }
        return null;
    }

    /// <summary>Hourly SP drain for PC self-work. Fixed light cost; never lethal.</summary>
    internal static int SelfWorkSpCost(int spMax, bool firstHour = false)
    {
        // Base 2, later hours 3; soft-cap vs max so low-stamina chars still finish.
        int cost = firstHour ? 2 : 3;
        int cap = Math.Max(1, spMax / 8);
        if (cost > cap)
        {
            cost = cap;
        }

        return Math.Max(1, cost);
    }

    internal static string? TryStart(Chara worker, Chara client, TownLaborJobDef def, int offeredHours = 0)
    {
        if (worker == null || client == null || def == null)
        {
            return NpcLabor.LaborText.T("town.error.invalidJob");
        }

        Zone? zone = EClass._zone;
        if (!CanHostTownLabor(zone))
        {
            return NpcLabor.LaborText.T("town.error.noTownWork", NpcLabor.LaborTerms.TownWork);
        }

        if (zone == null)
        {
            return NpcLabor.LaborText.T("town.error.invalidPlace");
        }

        if (LaborBusy.IsBusy(worker))
        {
            return LaborBusy.BusyReason(worker) ?? NpcLabor.LaborText.T("town.error.workerBusy");
        }

        if (IsClientBusy(client.uid))
        {
            return NpcLabor.LaborText.T("town.error.clientBusy", NpcLabor.LaborTerms.TownWork);
        }

        if (CountInZone(zone.uid) >= TownLaborJobs.MaxConcurrentPerTown)
        {
            return NpcLabor.LaborText.T("town.error.maxConcurrent", TownLaborJobs.MaxConcurrentPerTown, NpcLabor.LaborTerms.TownWork);
        }

        if (!IsCandidate(worker, def))
        {
            return NpcLabor.LaborText.T("town.error.cannotAssign", worker.NameSimple ?? worker.Name ?? ("#" + worker.uid));
        }

        // Soft: a Kind mismatch is tolerated (the board may have offered a different
        // role for the same shop), but a client whose job vanished outright cannot be
        // started. Written as a plain null check because that is all it ever rejected —
        // the old form also called MatchJob a second time on every mismatch.
        if (TownLaborJobs.MatchJob(client) == null)
        {
            return NpcLabor.LaborText.T("town.error.jobGone");
        }

        bool wasParty = IsParty(worker);
        if (wasParty)
        {
            try
            {
                // Keep global so later AddMemeber can succeed.
                try
                {
                    if (!worker.IsGlobal)
                    {
                        worker.SetGlobal();
                    }
                }
                catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
EClass.pc?.party?.RemoveMember(worker);
            }
            catch (Exception ex)
            {
                Plugin.LogWarn("townlabor remove party failed: " + ex.Message);
            }

            // Vanilla branch hour-tick can auto-rejoin if this stays true;
            // also used as a restore fallback if mission flag is lost.
            try { worker.c_wasInPcParty = true; } catch { }
        }

        int hours = TownLaborJobs.ResolveWorkHours(offeredHours);
        int shopLv = TownLaborJobs.GetShopLv(client);
        int workerSkill = TownLaborJobs.GetWorkerSkill(worker, def.SkillId);
        int clientInvest = 0;
        try { clientInvest = Math.Max(0, client.c_invest); } catch { clientInvest = 0; }
        Zone? home = null;
        try
        {
            home = worker.homeZone
                ?? EClass.BranchOrHomeBranch?.owner
                ?? EClass.Branch?.owner
                ?? EClass.pc?.homeZone;
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
var mission = new TownLaborMission
        {
            missionId = _nextMissionId++,
            jobId = def.Id,
            Kind = def.Kind,
            uidWorker = worker.uid,
            uidClient = client.uid,
            uidZone = zone.uid,
            zoneId = zone.id ?? "",
            zoneName = SafeZoneName(zone),
            clientName = client.NameSimple ?? client.Name ?? ("#" + client.uid),
            workerName = worker.NameSimple ?? worker.Name ?? ("#" + worker.uid),
            jobTitle = TownLaborJobs.ResolveBoardTitle(client, def),
            skillId = def.SkillId,
            hoursTotal = hours,
            hoursLeft = hours,
            wasPartyMember = wasParty,
            isPcSelf = false,
            workStarted = true,
            homeZoneUid = home?.uid ?? 0,
            shopLv = shopLv,
            workerSkill = workerSkill,
            clientInvest = clientInvest,
            rewardLog = new List<string>(),
        };

        // Walk-to-work: an on-map worker approaches the client under
        // AI_TownLabor, so never teleport a worker the player can see. Only an
        // off-map worker (summoned from home / another zone) is placed here.
        try
        {
            bool onMap = false;
            try { onMap = worker.ExistsOnMap && client.ExistsOnMap; } catch { onMap = false; }
            if (!onMap)
            {
                PlaceWorkerNearClient(worker, client);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("townlabor place worker: " + ex.Message);
        }

        try
        {
            PrepareWorkingChara(worker);
            worker.SetAI(new AI_TownLabor { missionId = mission.missionId });
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("townlabor set AI: " + ex.Message);
            try { worker.SetAIIdle(); } catch { }
        }

        TownLaborRewards.LockRewardPreview(mission);
        Missions.Add(mission);
        TryStartTrackerQuest(mission);

        int startHours = Math.Max(1, mission.hoursTotal > 0 ? mission.hoursTotal : (int)Math.Ceiling(Math.Max(0f, mission.hoursLeft)));
        string msg = NpcLabor.LaborText.T(
            "town.msg.workerStart",
            mission.workerName,
            mission.clientName,
            mission.jobTitle,
            startHours);
        try { Msg.Say(msg); } catch { }
        Plugin.LogInfo("townlabor start id=" + mission.missionId + " job=" + def.Id
            + " worker=" + mission.uidWorker + " client=" + mission.uidClient
            + " hours=" + hours);

        return null;
    }

    internal static bool TryRecall(int missionIdOrWorkerUid)
    {
        TownLaborMission? m = FindByMissionId(missionIdOrWorkerUid) ?? FindByWorker(missionIdOrWorkerUid);
        if (m == null)
        {
            return false;
        }

        Settle(m, TownLaborSettleKind.Recall);
        return true;
    }
}
