using NpcLabor.TownLabor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace NpcLabor.Dispatch;

/// <summary>
/// PARTIAL: Hourly tick, success roll, settlement, worker death, zone entry.
/// Split from DungeonDispatchManager.cs (2026-10-02).
/// </summary>
internal static partial class DungeonDispatchManager
{
    internal static void OnSimulateHour()
    {
        if (Missions.Count == 0)
        {
            return;
        }

        // Do not RefreshTrackerQuests every hour. That path can QuestManager.Start a pin
        // (journal spam / widget reorder). Tracker text reads live mission state already.
        // Pin recovery is load / mission-start only.
        var snapshot = Missions.ToList();
        foreach (DungeonDispatchMission m in snapshot)
        {
            try
            {
                if (!Missions.Contains(m))
                {
                    continue;
                }

                m.EnsureMemberList();
                var alive = new List<Chara>();
                foreach (int uid in m.uidMembers.ToList())
                {
                    Chara? c = null;
                    try
                    {
                        c = RefChara.Get(uid);
                    }
                    catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
if (c == null || c.isDead)
                    {
                        m.uidMembers.Remove(uid);
                        continue;
                    }

                    alive.Add(c);
                }

                if (alive.Count == 0)
                {
                    Plugin.LogInfo("dispatch drop empty mission id=" + m.missionId);
                    Missions.Remove(m);
                    RemoveTrackerQuest(m);
                    continue;
                }

                // Keep first uid for legacy field.
                m.uidChara = m.uidMembers[0];

                // Stakeout: region stays on field tile; dungeon may advance through existing floors.
                try
                {
                    if (m.isRegion)
                    {
                        Zone? field = m.GetZone();
                        if (field == null || field.destryoed)
                        {
                            field = DungeonDispatchTargets.EnsureRegionFieldZone(m.regionGx, m.regionGy);
                            if (field != null)
                            {
                                m.uidZone = field.uid;
                                try
                                {
                                    if (!string.IsNullOrEmpty(field.id))
                                    {
                                        m.zoneId = field.id;
                                    }
                                }
                                catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
                            }
                        }

                        // Keep the stakeout field alive for the whole mission
                        // (vanilla field default expiry is 7 days; Region.OnActivate
                        // would otherwise reap it and leave the player an empty field).
                        try
                        {
                            if (field != null)
                            {
                                field.dateExpire = 0;
                            }
                        }
                        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }

                        foreach (Chara c in alive)
                        {
                            try
                            {
                                if (field != null && (c.currentZone == null || c.currentZone.uid != field.uid))
                                {
                                    try
                                    {
                                        c.MoveZone(field, ZoneTransition.EnterState.RandomVisit);
                                    }
                                    catch
                                    {
                                        c.MoveZone(field);
                                    }
                                }
                            }
                            catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
PrepareDispatchedChara(c);
                        }

                        m.currentFloorLv = 0;
                    }
                    else
                    {
                        Zone? root = m.GetZone();
                        if (root != null)
                        {
                            try { root = root.GetTopZone() ?? root; } catch { }
                            Zone? floor = DungeonDispatchTargets.ResolveFloorForProgress(root, m.ProgressPercent) ?? root;
                            int flv = SafeLv(floor);
                            m.currentFloorLv = flv;
                            if (FloorDepthAbs(SafeLv(root), flv) > FloorDepthAbs(SafeLv(root), m.deepestFloorLv))
                            {
                                m.deepestFloorLv = flv;
                            }

                            foreach (Chara c in alive)
                            {
                                try
                                {
                                    bool needMove = c.currentZone == null;
                                    if (!needMove && floor != null && c.currentZone != null
                                        && c.currentZone.uid != floor.uid)
                                    {
                                        // Only remount when target floor already exists and differs.
                                        needMove = true;
                                    }

                                    if (needMove && floor != null)
                                    {
                                        try
                                        {
                                            c.MoveZone(floor, ZoneTransition.EnterState.RandomVisit);
                                        }
                                        catch
                                        {
                                            c.MoveZone(floor);
                                        }
                                    }
                                }
                                catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
PrepareDispatchedChara(c);
                            }
                        }
                        else
                        {
                            foreach (Chara c in alive)
                            {
                                PrepareDispatchedChara(c);
                            }
                        }
                    }
                }
                catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
// Spend the hour first so harvest gate / progress see the new clock.
                m.hoursLeft--;

                // Partial loot log while exploring (only after travelHours).
                try
                {
                    FactionBranch? branch = EClass.BranchOrHomeBranch ?? EClass.Branch ?? alive[0].homeBranch;
                    DungeonDispatchRewards.TickPartialLoot(branch, m);
                }
                catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
if (m.hoursLeft <= 0)
                {
                    bool success = RollSuccess(m);
                    Settle(
                        m,
                        success ? DispatchSettleKind.Success : DispatchSettleKind.Failure,
                        destroyRandom: success && m.isRandomSite);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogWarn("dispatch hour tick failed: " + ex.Message);
            }
        }

        // Hour ticks can burst (sleep/wait). Mark dirty and flush once after AdvanceHour.
        if (Missions.Count > 0)
        {
            _hourSavePending = true;
        }
    }

    static bool RollSuccess(DungeonDispatchMission m)
    {
        if (m == null)
        {
            return false;
        }

        // Region outing has no combat success gate.
        if (m.isRegion)
        {
            return true;
        }

        try
        {
            int roll = EClass.rnd(100);
            return roll < Mathf.Clamp(m.successChance, 15, 100);
        }
        catch
        {
            return m.successChance >= 50;
        }
    }

    static void Settle(DungeonDispatchMission m, DispatchSettleKind kind, bool destroyRandom)
    {
        if (m == null)
        {
            return;
        }

        m.EnsureMemberList();
        var members = m.GetMembers();

        FactionBranch? branch = null;
        try
        {
            if (members.Count > 0)
            {
                branch = members[0].homeBranch;
            }

            branch ??= EClass.BranchOrHomeBranch ?? EClass.Branch;
        }
        catch
        {
            branch = EClass.BranchOrHomeBranch ?? EClass.Branch;
        }

        Zone? home = m.GetHomeZone();

        // Sync tracker to current harvest gate, then deliver (mail qty == details).
        try
        {
            if (m.isRegion)
            {
                DungeonDispatchRewards.TickPartialLoot(branch, m);
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
// Deliver first so lootLog is rewritten from real stacks while mission still exists.
        try
        {
            if (branch != null)
            {
                DungeonDispatchRewards.Deliver(branch, m, kind);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("dispatch deliver: " + ex.Message);
        }

        // Then drop mission + tracker pin (force untrack + WidgetQuestTracker.Refresh).
        Missions.Remove(m);
        RemoveTrackerQuest(m);
        RefreshRegionMapMarkers();

        try
        {
            DungeonDispatchRewards.GrantExp(members, kind, m);
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
        try
        {
            DungeonDispatchRewards.GrantFameOnDungeonSuccess(m, kind);
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
// Homecoming for all members.
        foreach (Chara c in members)
        {
            try
            {
                ClearDispatchedFlags(c);
                if (home != null)
                {
                    c.MoveZone(home, ZoneTransition.EnterState.Return);
                }
                else if (c.homeZone != null)
                {
                    c.MoveZone(c.homeZone);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogWarn("dispatch home MoveZone: " + ex.Message);
            }
        }

        if (destroyRandom && m.isRandomSite)
        {
            try
            {
                Zone? z = m.GetZone();
                if (z != null)
                {
                    try
                    {
                        z = z.GetTopZone() ?? z;
                    }
                    catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
}

                if (z != null && z.isRandomSite && !z.IsPCFaction && !z.destryoed)
                {
                    if (EClass._zone != null && (EClass._zone.uid == z.uid
                        || ((EClass._zone.GetTopZone()?.uid ?? -1) == z.uid)))
                    {
                        z.dateExpire = 1;
                        Plugin.LogInfo("dispatch expire active random site uid=" + z.uid);
                    }
                    else
                    {
                        z.Destroy();
                        Plugin.LogInfo("dispatch destroy random site uid=" + z.uid);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogWarn("dispatch destroy site: " + ex.Message);
            }
        }

        string outcome = kind switch
        {
            DispatchSettleKind.Success => LaborText.T("dispatch.settle.success"),
            DispatchSettleKind.Failure => LaborText.T("dispatch.settle.fail"),
            DispatchSettleKind.Recall => LaborText.T("dispatch.settle.recall"),
            _ => LaborText.T("dispatch.settle.end"),
        };
        string who = m.MemberNames();
        string loot = m.LootSummary(8);
        string feature = m.isRegion ? LaborTerms.RegionDispatch : LaborTerms.DungeonExplore;
        string msg = LaborText.T("dispatch.settle.line", who, feature, m.zoneName, outcome);
        if (!string.IsNullOrEmpty(loot) && loot != NpcLabor.LaborText.T("dis.loot.none"))
        {
            msg += " " + loot;
        }

        // Failure educational beat: what success might have added (boss/fame or fuller haul).
        // Fixed-dungeon reward math stays unchanged; this is flavor only.
        if (kind == DispatchSettleKind.Failure)
        {
            string hint = m.isRegion
                ? LaborText.T("dispatch.fail.hint.region")
                : LaborText.T("dispatch.fail.hint.dungeon");
            if (!string.IsNullOrEmpty(hint))
            {
                msg += " " + hint;
            }
        }
        try
        {
            Msg.Say(msg);
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
        try
        {
            branch?.LogRaw(msg);
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
Plugin.LogInfo("dispatch settle kind=" + kind + " mission=" + m.missionId + " zone=" + m.uidZone
            + " loot=" + loot);
        try { Save(); } catch { }
    }

    internal static void OnWorkerDied(Chara c)
    {
        if (c == null)
        {
            return;
        }

        DungeonDispatchMission? m = FindByChara(c.uid);
        if (m == null)
        {
            return;
        }

        m.EnsureMemberList();
        m.uidMembers.Remove(c.uid);

        // Nothing else runs for a dead worker, so drop the dispatch flags here —
        // otherwise noMove / isRestrained stay stuck to the body.
        ClearDispatchedFlags(c);

        if (m.uidMembers.Count == 0)
        {
            Missions.Remove(m);
            RemoveTrackerQuest(m);
            Plugin.LogInfo("dispatch cleared on death (empty) uid=" + c.uid);
        }
        else
        {
            m.uidChara = m.uidMembers[0];
            Plugin.LogInfo("dispatch member death uid=" + c.uid + " remaining=" + m.uidMembers.Count);
        }
    }

    internal static void OnZoneEntered(Zone zone)
    {
        if (zone == null || Missions.Count == 0)
        {
            return;
        }

        try
        {
            DungeonDispatchMission? m = FindByZone(zone.uid);
            if (m == null)
            {
                // Region fallback by tile coords.
                for (int i = 0; i < Missions.Count; i++)
                {
                    DungeonDispatchMission cand = Missions[i];
                    if (!cand.isRegion)
                    {
                        continue;
                    }

                    try
                    {
                        if (zone.x == cand.regionGx && zone.y == cand.regionGy)
                        {
                            m = cand;
                            break;
                        }
                    }
                    catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
}
            }

            if (m == null)
            {
                return;
            }

            m.EnsureMemberList();

            Zone? stake = zone;
            if (m.isRegion)
            {
                // Ensure mission points at this field and members stand here with PC.
                m.uidZone = zone.uid;
                try
                {
                    if (!string.IsNullOrEmpty(zone.id))
                    {
                        m.zoneId = zone.id;
                    }
                }
                catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }

                // This is the live stakeout field: keep it non-destructible for the mission.
                try
                {
                    zone.dateExpire = 0;
                }
                catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
m.currentFloorLv = 0;
            }
            else
            {
                try
                {
                    Zone root = m.GetZone()?.GetTopZone() ?? m.GetZone() ?? zone;
                    stake = DungeonDispatchTargets.ResolveFloorForProgress(root, m.ProgressPercent) ?? zone;
                    m.currentFloorLv = SafeLv(stake);
                }
                catch
                {
                    stake = zone;
                    m.currentFloorLv = SafeLv(zone);
                }
            }

            foreach (Chara c in m.GetMembers())
            {
                try
                {
                    if (stake != null && (c.currentZone == null || c.currentZone.uid != stake.uid))
                    {
                        try
                        {
                            c.MoveZone(stake, ZoneTransition.EnterState.RandomVisit);
                        }
                        catch
                        {
                            try { c.MoveZone(stake); } catch { }
                        }
                    }
                }
                catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
PrepareDispatchedChara(c);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("dispatch OnZoneEntered: " + ex.Message);
        }
    }
}
