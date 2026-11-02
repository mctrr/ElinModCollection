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
/// PARTIAL: Hourly tick, worker death, zone entry, presence/pose upkeep,
/// placement, settlement and worker restore.
/// Split from TownLaborManager.cs (2026-10-02).
/// </summary>
internal static partial class TownLaborManager
{
    internal static void OnSimulateHour()
    {
        if (Missions.Count == 0)
        {
            return;
        }

        // Do not RefreshTrackerQuests every hour (QuestManager.Start / journal spam / dups).
        // Tracker text already reads live mission progress.
        var snapshot = Missions.ToList();
        foreach (TownLaborMission m in snapshot)
        {
            try
            {
                if (!Missions.Contains(m))
                {
                    continue;
                }

                Chara? worker = m.GetWorker();
                bool pcSelf = false;
                try { pcSelf = m.isPcSelf; } catch { pcSelf = false; }

                if (!pcSelf && worker != null && worker.isDead)
                {
                    Plugin.LogInfo("townlabor drop dead worker id=" + m.missionId);
                    Settle(m, TownLaborSettleKind.Failed);
                    continue;
                }

                // Worker may be temporarily unresolved off-map; keep ticking hours.
                // Only fail when we positively see a dead client. Map-local merchants
                // unload when PC leaves town — that must NOT cancel labor.
                Chara? client = m.GetClient();
                if (client != null && client.isDead)
                {
                    Plugin.LogInfo("townlabor drop dead client id=" + m.missionId);
                    Settle(m, TownLaborSettleKind.Failed);
                    continue;
                }

                // Slice X: leaving a work zone recalls companion labor on the
                // spot. A companion mission whose zone is not the PC's current
                // one can only survive from an older save (leaving used to keep
                // it ticking) — recall it instead of letting hours run off-map
                // toward a success the player is not there to see.
                if (!pcSelf && !IsWorkZoneActive(m))
                {
                    Plugin.LogInfo("townlabor recall off-map mission id=" + m.missionId);
                    Settle(m, TownLaborSettleKind.Recall);
                    continue;
                }

                // Soft presence only for NPC workers while PC is on the work map.
                if (!pcSelf && worker != null && IsWorkZoneActive(m))
                {
                    try
                    {
                        MaintainPresence(m, worker, client);
                    }
                    catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
}

                // PC self-work: wait until arrived at client before hour/SP ticks.
                if (pcSelf && !m.workStarted)
                {
                    try
                    {
                        MaybeResumePcSelfApproach(m);
                    }
                    catch (Exception ex)
                    {
                        Plugin.LogDebug("townlabor self approach resume: " + ex.Message);
                    }

                    continue;
                }

                // PC self-work: drain SP each hour after arrival. Exhaustion = Quest.Fail path.
                if (pcSelf)
                {
                    try
                    {
                        Chara? pc = EClass.pc;
                        if (pc == null || pc.isDead)
                        {
                            Settle(m, TownLaborSettleKind.Failed);
                            continue;
                        }

                        int spMax = 1;
                        try { spMax = pc.stamina != null ? Math.Max(1, pc.stamina.max) : 1; } catch { spMax = 1; }
                        int cost = SelfWorkSpCost(spMax, firstHour: false);
                        try
                        {
                            if (pc.stamina != null && cost > 0)
                            {
                                pc.stamina.Mod(-cost);
                            }
                        }
                        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
int spLeft = 1;
                        try { spLeft = pc.stamina != null ? pc.stamina.value : 0; } catch { spLeft = 0; }
                        if (spLeft <= 0)
                        {
                            try
                            {
                                Msg.Say(NpcLabor.LaborText.T(
                                    "town.msg.youTired",
                                    string.IsNullOrEmpty(m.jobTitle) ? NpcLabor.LaborTerms.TownWork : m.jobTitle));
                            }
                            catch { }
                            Settle(m, TownLaborSettleKind.Failed);
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.LogWarn("townlabor self sp tick: " + ex.Message);
                    }
                }

                // Hourly life-skill exp: PC self gets half the settle rate each hour.
                // Companion workers keep full exp on settle only (no hourly drip).
                if (pcSelf && m.skillId > 0)
                {
                    try
                    {
                        Chara? expTarget = EClass.pc;
                        if (expTarget != null && !expTarget.isDead)
                        {
                            int hourly = Math.Max(0, TownLaborRewards.ExpPerHour / 2);
                            if (hourly > 0)
                            {
                                expTarget.ModExp(m.skillId, hourly);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.LogDebug("townlabor self exp tick: " + ex.Message);
                    }
                }

                m.hoursLeft = Math.Max(0, m.hoursLeft - 1);
                if (m.hoursLeft <= 0)
                {
                    Settle(m, TownLaborSettleKind.Success);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogWarn("townlabor hour tick id=" + m.missionId + ": " + ex.Message);
            }
        }

        // Cosmetic pose refresh occasionally while PC is in a labor zone.
        _poseTick++;
        if (_poseTick % 2 == 0)
        {
            try
            {
                RefreshWorkingPoses();
            }
            catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
}

        // Hour ticks can burst (sleep/wait). Mark dirty and flush once after AdvanceHour.
        if (Missions.Count > 0)
        {
            _hourSavePending = true;
        }
    }

    internal static void OnWorkerDied(Chara c)
    {
        if (c == null)
        {
            return;
        }

        TownLaborMission? m = FindByWorker(c.uid);
        if (m != null)
        {
            Settle(m, TownLaborSettleKind.Failed);
        }
    }

    internal static void OnZoneEntered(Zone zone)
    {
        if (zone == null || Missions.Count == 0)
        {
            return;
        }

        foreach (TownLaborMission m in Missions)
        {
            if (m.uidZone != zone.uid)
            {
                continue;
            }

            try
            {
                if (m.isPcSelf)
                {
                    if (!m.workStarted)
                    {
                        MaybeResumePcSelfApproach(m);
                    }

                    continue;
                }

                Chara? worker = m.GetWorker();
                if (worker == null || worker.isDead)
                {
                    continue;
                }

                Chara? client = m.GetClient();
                MaintainPresence(m, worker, client);
            }
            catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
}
    }

    static bool IsWorkZoneActive(TownLaborMission m)
    {
        try
        {
            return m != null && EClass._zone != null && EClass._zone.uid == m.uidZone;
        }
        catch
        {
            return false;
        }
    }

    static void MaintainPresence(TownLaborMission m, Chara worker, Chara? client)
    {
        if (m == null || worker == null || worker.isDead)
        {
            return;
        }

        Zone? workZone = m.GetZone();
        if (workZone == null)
        {
            try { workZone = EClass._zone; } catch { workZone = null; }
        }

        if (workZone == null)
        {
            return;
        }

        // Only act when the work map is the active map. Off-map labor just ticks hours.
        try
        {
            if (EClass._zone == null || EClass._zone.uid != workZone.uid)
            {
                return;
            }
        }
        catch
        {
            return;
        }

        // Bring missing worker onto the loaded work map once.
        // Repeated MoveZone while already present can create invisible clones.
        bool onWorkMap = false;
        try
        {
            onWorkMap = worker.ExistsOnMap
                && worker.currentZone != null
                && worker.currentZone.uid == workZone.uid;
        }
        catch
        {
            onWorkMap = false;
        }

        if (!onWorkMap)
        {
            try
            {
                if (worker.currentZone == null || worker.currentZone.uid != workZone.uid)
                {
                    worker.MoveZone(workZone, ZoneTransition.EnterState.RandomVisit);
                }
            }
            catch
            {
                try { worker.MoveZone(workZone); } catch { }
            }
        }

        // Rare lost-worker guard only: normal drift is walked back by
        // AI_TownLabor itself (it re-approaches beyond ComfortRadius), so a
        // mid-range gap must never teleport the worker in front of the player.
        // Keep the soft re-place for extreme cases (e.g. stuck across the map).
        try
        {
            if (client != null
                && client.ExistsOnMap
                && worker.ExistsOnMap
                && worker.Dist(client) > 24)
            {
                PlaceWorkerNearClient(worker, client, force: false);
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
        try
        {
            if (!(worker.ai is AI_TownLabor labor) || labor.missionId != m.missionId)
            {
                PrepareWorkingChara(worker);
                worker.SetAI(new AI_TownLabor { missionId = m.missionId });
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
}

    static void RefreshWorkingPoses()
    {
        if (EClass._zone == null)
        {
            return;
        }

        foreach (TownLaborMission m in Missions)
        {
            if (m.uidZone != EClass._zone.uid)
            {
                continue;
            }

            if (m.isPcSelf)
            {
                continue;
            }

            Chara? worker = m.GetWorker();
            if (worker == null || !worker.ExistsOnMap)
            {
                continue;
            }

            TownLaborJobDef? def = m.Def;
            try
            {
                worker.ShowEmo(def?.WorkEmo ?? Emo.happy);
            }
            catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
}
    }

    internal static void PlaceWorkerNearClient(Chara worker, Chara client, bool force = true)
    {
        if (worker == null || client == null)
        {
            return;
        }

        try
        {
            if (worker.ExistsOnMap && client.ExistsOnMap)
            {
                int dist = worker.Dist(client);
                if (!force && dist <= 12)
                {
                    return;
                }

                if (force && dist <= 4)
                {
                    try { worker.LookAt(client); } catch { }
                    return;
                }
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
Point? dest = null;
        try
        {
            if (client.ExistsOnMap && client.pos != null)
            {
                dest = FindNearPoint(client.pos, worker);
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
if (dest == null)
        {
            return;
        }

        try
        {
            if (EClass.pc != null && EClass.pc.pos != null && dest.Equals(EClass.pc.pos))
            {
                return;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
        try
        {
            if (client.pos != null && dest.Equals(client.pos))
            {
                return;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
bool teleported = false;
        if (force)
        {
            try
            {
                worker.Teleport(dest, silent: true, force: true);
                teleported = true;
            }
            catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
}

        if (!teleported)
        {
            try
            {
                worker.MoveImmediate(dest, focus: false, cancelAI: false);
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("townlabor place: " + ex.Message);
            }
        }

        try
        {
            worker.LookAt(client);
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
}

    static Point? FindNearPoint(Point origin, Chara? worker = null)
    {
        if (origin == null)
        {
            return null;
        }

        int[] dx = { 2, -2, 0, 0, 2, 2, -2, -2, 1, -1, 0, 0, 1, 1, -1, -1, 3, -3, 0, 0 };
        int[] dy = { 0, 0, 2, -2, 2, -2, 2, -2, 0, 0, 1, -1, 1, -1, 1, -1, 0, 0, 3, -3 };
        Point? pcPos = null;
        try { pcPos = EClass.pc?.pos; } catch { pcPos = null; }

        for (int i = 0; i < dx.Length; i++)
        {
            try
            {
                var p = new Point(origin.x + dx[i], origin.z + dy[i]);
                if (!p.IsValid || !p.IsInBounds)
                {
                    continue;
                }

                if (p.IsBlocked)
                {
                    continue;
                }

                if (pcPos != null && p.Equals(pcPos))
                {
                    continue;
                }

                if (p.Equals(origin))
                {
                    continue;
                }

                if (p.HasChara)
                {
                    bool self = false;
                    try
                    {
                        if (worker != null && worker.pos != null && p.Equals(worker.pos))
                        {
                            self = true;
                        }
                    }
                    catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
if (!self)
                    {
                        continue;
                    }
                }

                return p;
            }
            catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
}

        return null;
    }

    internal static void PrepareWorkingChara(Chara c)
    {
        if (c == null)
        {
            return;
        }

        try { c.SetHostility(Hostility.Friend); } catch { }
        try { c.enemy = null; } catch { }
        // Do NOT set isRestrained — town labor should look like free work, not shackles.
        // noMove false so DoGoto can step around the shop.
        try { c.noMove = false; } catch { }
    }

    internal static void ClearWorkingFlags(Chara? c)
    {
        if (c == null)
        {
            return;
        }

        try { c.noMove = false; } catch { }
        try
        {
            if (c.ai is AI_TownLabor)
            {
                c.SetAIIdle();
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
}

    static void Settle(TownLaborMission m, TownLaborSettleKind kind)
    {
        if (m == null)
        {
            return;
        }

        if (!Missions.Contains(m))
        {
            return;
        }

        Chara? worker = m.GetWorker() ?? ResolveChara(m.uidWorker);
        Chara? client = m.GetClient() ?? ResolveChara(m.uidClient);

        // Last-chance global lookup for party restore.
        if (worker == null && m.uidWorker > 0)
        {
            try { worker = RefChara.Get(m.uidWorker); } catch { }
        }

        // Deliver first while mission still known.
        try
        {
            TownLaborRewards.Deliver(m, kind, worker, client);
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("townlabor deliver: " + ex.Message);
        }

        Missions.Remove(m);

        bool pcSelf = false;
        try { pcSelf = m.isPcSelf; } catch { pcSelf = false; }

        if (pcSelf)
        {
            try { ClearPcSelfApproachAi(); } catch { }
        }
        else if (worker != null && !worker.isDead)
        {
            try
            {
                ClearWorkingFlags(worker);
            }
            catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
RestoreWorkerAfterLabor(m, worker);
        }
        else if (m.wasPartyMember)
        {
            Plugin.LogWarn("townlabor settle: worker unresolved, cannot rejoin party uid=" + m.uidWorker);
        }

        // Hard fail uses vanilla Quest.Fail (sound + fame formula + karma).
        // Success/recall keep quiet tracker remove (custom rewards already delivered).
        if (kind == TownLaborSettleKind.Failed)
        {
            FailTrackerQuest(m);
        }
        else
        {
            RemoveTrackerQuest(m);
        }

        string outcome = kind switch
        {
            TownLaborSettleKind.Success => LaborText.T("town.settle.success"),
            TownLaborSettleKind.Recall => LaborText.T("town.settle.recall"),
            _ => LaborText.T("town.settle.fail"),
        };

        string who;
        if (pcSelf)
        {
            who = LaborText.T("town.you");
        }
        else
        {
            who = string.IsNullOrEmpty(m.workerName) ? ("#" + m.uidWorker) : m.workerName;
        }

        string job = string.IsNullOrEmpty(m.jobTitle) ? LaborTerms.TownWork : m.jobTitle;
        // One-line outcome only. Rewards are physically dropped near the PC (or
        // mailed home) exactly like a vanilla quest hand-in, so listing them here
        // was redundant — and the invest line used to be appended a second time
        // (once standalone at grant time, once through rewardLog) which is why it
        // showed twice.
        string msg = LaborText.T("town.settle.line", who, job, outcome);

        // Quest.Fail already plays questFail + fame; keep settle line lean.
        try { Msg.Say(msg); } catch { }
        Plugin.LogInfo("townlabor settle id=" + m.missionId + " kind=" + kind);
        try { Save(); } catch { }
    }

    static void RestoreWorkerAfterLabor(TownLaborMission m, Chara worker)
    {
        if (worker == null || worker.isDead)
        {
            return;
        }

        bool wantParty = false;
        try { wantParty = m != null && m.wasPartyMember; } catch { wantParty = false; }
        if (!wantParty)
        {
            try { wantParty = worker.c_wasInPcParty; } catch { }
        }

        if (wantParty)
        {
            bool rejoined = false;
            try
            {
                try
                {
                    if (!worker.IsGlobal)
                    {
                        worker.SetGlobal();
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogWarn("townlabor SetGlobal: " + ex.Message);
                }

                // Idle AI before party ops.
                try
                {
                    if (worker.ai is AI_TownLabor)
                    {
                        worker.SetAIIdle();
                    }
                }
                catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
Party? party = EClass.pc?.party;
                if (party != null)
                {
                    bool already = false;
                    try
                    {
                        already = worker.IsPCParty
                            || worker.party == party
                            || (party.uidMembers != null && party.uidMembers.Contains(worker.uid));
                    }
                    catch
                    {
                        already = false;
                    }

                    if (!already)
                    {
                        try
                        {
                            // Force-clear stale party link then add.
                            try { worker.party = null; } catch { }
                            party.AddMemeber(worker, showMsg: true);
                        }
                        catch (Exception ex)
                        {
                            Plugin.LogWarn("townlabor AddMemeber: " + ex.Message);
                        }

                        // Verify / force uid membership.
                        try
                        {
                            if (party.uidMembers != null && !party.uidMembers.Contains(worker.uid))
                            {
                                party.uidMembers.Add(worker.uid);
                            }
                            try { party._members = null; } catch { }
                            try { worker.party = party; } catch { }
                            try { WidgetRoster.SetDirty(); } catch { }
                        }
                        catch (Exception ex2)
                        {
                            Plugin.LogWarn("townlabor party force: " + ex2.Message);
                        }
                    }

                    try
                    {
                        rejoined = worker.IsPCParty
                            || worker.party == party
                            || (party.uidMembers != null && party.uidMembers.Contains(worker.uid));
                    }
                    catch
                    {
                        rejoined = false;
                    }
                }

                // Only clear the auto-rejoin flag once membership looks good.
                if (rejoined)
                {
                    try { worker.c_wasInPcParty = false; } catch { }
                }
                else
                {
                    try { worker.c_wasInPcParty = true; } catch { }
                    Plugin.LogWarn("townlabor party restore incomplete uid=" + worker.uid);
                }

                // Bring teammate to PC map / nearby.
                try
                {
                    Zone? pcZone = EClass._zone ?? EClass.pc?.currentZone;
                    if (pcZone != null
                        && (worker.currentZone == null || worker.currentZone.uid != pcZone.uid))
                    {
                        worker.MoveZone(pcZone, ZoneTransition.EnterState.Return);
                    }

                    if (EClass.pc != null && EClass.pc.ExistsOnMap)
                    {
                        try
                        {
                            if (!worker.ExistsOnMap || worker.Dist(EClass.pc) > 2)
                            {
                                Point? nearPc = FindNearPoint(EClass.pc.pos, worker);
                                if (nearPc != null)
                                {
                                    try { worker.Teleport(nearPc, silent: true, force: true); }
                                    catch
                                    {
                                        try { worker.MoveImmediate(nearPc, focus: false, cancelAI: true); } catch { }
                                    }
                                }
                            }
                        }
                        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
}
                }
                catch (Exception ex)
                {
                    Plugin.LogWarn("townlabor party return: " + ex.Message);
                }

                Plugin.LogInfo("townlabor party restore worker=" + worker.uid + " rejoined=" + rejoined);
                return;
            }
            catch (Exception ex)
            {
                Plugin.LogWarn("townlabor party restore: " + ex.Message);
            }
        }

        try
        {
            Zone? home = m?.GetHomeZone();
            if (home != null && (worker.currentZone == null || worker.currentZone.uid != home.uid))
            {
                worker.MoveZone(home, ZoneTransition.EnterState.Return);
            }
            else if (home == null && worker.homeZone != null
                && (worker.currentZone == null || worker.currentZone.uid != worker.homeZone.uid))
            {
                worker.MoveZone(worker.homeZone);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("townlabor home MoveZone: " + ex.Message);
        }
    }
}
