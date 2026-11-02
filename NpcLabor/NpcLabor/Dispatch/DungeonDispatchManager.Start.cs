using NpcLabor.TownLabor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace NpcLabor.Dispatch;

/// <summary>
/// PARTIAL: Mission start paths, dispatch flag set/clear, recall.
/// Split from DungeonDispatchManager.cs (2026-10-02).
/// </summary>
internal static partial class DungeonDispatchManager
{
    internal static string? TryStart(IList<Chara> workers, DungeonDispatchTarget target, int exploreWeeks = 1)
    {
        if (target == null)
        {
            return NpcLabor.LaborText.T("dis.error.invalidTarget");
        }

        if (target.IsRegion)
        {
            return TryStartRegion(workers, target, exploreWeeks);
        }

        if (target.Zone == null)
        {
            return NpcLabor.LaborText.T("dis.error.invalidTarget");
        }

        return TryStart(workers, target.Zone);
    }

    internal static string? TryStartRegion(IList<Chara> workers, DungeonDispatchTarget target, int exploreWeeks = 1)
    {
        try
        {
            if (workers == null || workers.Count == 0 || target == null || !target.IsRegion)
            {
                return NpcLabor.LaborText.T("dis.error.invalidTarget");
            }

            if (!NpcLabor.LaborTerms.CanRegionDispatch(out string? regionDeny))
            {
                return regionDeny ?? NpcLabor.LaborText.T("dis.error.notUnlocked", NpcLabor.LaborTerms.RegionDispatch);
            }

            if (workers.Count > DungeonDispatchMission.MaxMembersPerDungeon)
            {
                return NpcLabor.LaborText.T("dis.error.tooManyTarget", DungeonDispatchMission.MaxMembersPerDungeon);
            }

            var members = new List<Chara>();
            var seen = new HashSet<int>();
            foreach (Chara w in workers)
            {
                if (w == null || !seen.Add(w.uid))
                {
                    continue;
                }

                if (!IsCandidate(w))
                {
                    return NpcLabor.LaborText.T("dis.error.cannotSend", w.NameSimple ?? w.Name ?? ("#" + w.uid));
                }

                members.Add(w);
            }

            if (members.Count == 0)
            {
                return NpcLabor.LaborText.T("dis.error.noOne");
            }

            if (IsRegionBusy(target.RegionKind))
            {
                return NpcLabor.LaborText.T("dis.error.regionBusy", NpcLabor.LaborTerms.RegionDispatch);
            }

            FactionBranch? branch = EClass.BranchOrHomeBranch ?? EClass.Branch ?? members[0].homeBranch;
            Zone? home = branch?.owner ?? members[0].homeZone ?? EClass.pc?.homeZone;
            if (branch == null || home == null)
            {
                return NpcLabor.LaborText.T("dis.error.needBase");
            }

            foreach (Chara worker in members)
            {
                if (IsParty(worker))
                {
                    try
                    {
                        EClass.pc?.party?.RemoveMember(worker);
                    }
                    catch (Exception ex)
                    {
                        Plugin.LogWarn("dispatch remove party failed: " + ex.Message);
                    }
                }
            }

            int power = DungeonDispatchTargets.AggregateCombatPower(members);
            int explore = DungeonDispatchTargets.AggregateSkill(members, DungeonDispatchMission.SkillExplore);
            int lockpick = DungeonDispatchTargets.AggregateSkill(members, DungeonDispatchMission.SkillLockpick);
            int gather = DungeonDispatchTargets.AggregateRegionGather(members, target.RegionKind);

            int distDays = Math.Max(1, target.DistDays);
            int weeks = Mathf.Clamp(exploreWeeks <= 0 ? 1 : exploreWeeks, 1, 4);
            // Travel leg (half distance) + chosen explore weeks. No danger days / success roll.
            int travelHours = DungeonDispatchTargets.GetRegionTravelHours(distDays);
            int exploreHours = weeks * 7 * 24;
            int baseHours = travelHours + exploreHours;
            int hours = baseHours;
            try
            {
                int tMin = Mathf.Max(6, Mathf.FloorToInt(travelHours * 0.8f));
                int tMax = Mathf.Max(tMin + 1, Mathf.FloorToInt(travelHours * 1.2f));
                hours = (tMin + EClass.rnd(Math.Max(1, tMax - tMin + 1))) + exploreHours;
            }
            catch
            {
                hours = baseHours;
            }

            if (target.RegionGx == int.MinValue || target.RegionGy == int.MinValue)
            {
                return NpcLabor.LaborText.T("dis.error.regionTile");
            }

            Zone? field = DungeonDispatchTargets.EnsureRegionFieldZone(target.RegionGx, target.RegionGy);
            if (field == null || !DungeonDispatchTargets.IsReusableFieldZone(field))
            {
                return NpcLabor.LaborText.T("dis.error.regionTile");
            }

            int fieldUid = 0;
            string fieldId = "region:" + DungeonDispatchTargets.NormalizeRegionKind(target.RegionKind);
            try { fieldUid = field.uid; } catch { fieldUid = 0; }
            try
            {
                if (!string.IsNullOrEmpty(field.id))
                {
                    fieldId = field.id;
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }

            var mission = new DungeonDispatchMission
            {
                missionId = _nextMissionId++,
                uidMembers = members.Select(m => m.uid).ToList(),
                uidChara = members[0].uid,
                uidZone = fieldUid,
                zoneId = fieldId,
                zoneName = target.Name,
                dangerLv = 1, // unused for region
                isRandomSite = false,
                isRegion = true,
                regionKind = DungeonDispatchTargets.NormalizeRegionKind(target.RegionKind),
                regionGx = target.RegionGx,
                regionGy = target.RegionGy,
                exploreWeeks = weeks,
                hoursTotal = hours,
                hoursLeft = hours,
                combatPower = power,
                exploreSkill = explore,
                lockpickSkill = lockpick,
                gatherSkill = gather,
                successChance = 100, // region has no combat success roll
                distDays = distDays,
                travelHours = Math.Max(DungeonDispatchTargets.GetRegionTravelHours(distDays), Math.Max(0, hours - exploreHours)),
                dangerDays = 0,
                homeZoneUid = home.uid,
                currentFloorLv = 0,
                deepestFloorLv = 0,
                lootLog = new List<string>(),
            };

            foreach (Chara worker in members)
            {
                try
                {
                    PrepareDispatchedChara(worker);
                    if (field != null)
                    {
                        try
                        {
                            worker.MoveZone(field, ZoneTransition.EnterState.RandomVisit);
                        }
                        catch
                        {
                            worker.MoveZone(field);
                        }
                    }
                    else
                    {
                        // Last resort if field creation failed — invisible but mission still ticks.
                        worker.MoveZone("somewhere");
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogWarn("dispatch region MoveZone failed: " + ex.Message);
                    try { PrepareDispatchedChara(worker); } catch { }
                }

                try { PrepareDispatchedChara(worker); } catch { }
            }

            Missions.Add(mission);
            RememberLastDispatch(mission);
            // Lock region haul immediately so quest tracker shows real harvest from hour 0.
            try { DungeonDispatchRewards.TickPartialLoot(branch, mission); } catch { }
            TryStartTrackerQuest(mission);
            RefreshRegionMapMarkers();
            try { Save(); } catch { }

            string names = mission.MemberNames();
            string msg = NpcLabor.LaborText.T(
                "dis.start.region",
                names,
                NpcLabor.LaborTerms.RegionDispatch,
                mission.zoneName,
                members.Count,
                weeks);
            try { Msg.Say(msg); } catch { }
            try { branch.LogRaw(msg); } catch { }

            Plugin.LogInfo("dispatch start region mission=" + mission.missionId
                + " kind=" + mission.regionKind + " members=" + members.Count + " hours=" + hours);
            return null;
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("dispatch TryStartRegion failed: " + ex);
            return NpcLabor.LaborText.T("dis.error.startFail", ex.Message);
        }
    }

    internal static string? TryStart(IList<Chara> workers, Zone target)
    {
        try
        {
            if (workers == null || workers.Count == 0 || target == null)
            {
                return NpcLabor.LaborText.T("dis.error.invalidTarget");
            }

            if (!NpcLabor.LaborTerms.CanDungeonExplore(out string? dungeonDeny))
            {
                return dungeonDeny ?? NpcLabor.LaborText.T("dis.error.notUnlocked", NpcLabor.LaborTerms.DungeonExplore);
            }

            if (workers.Count > DungeonDispatchMission.MaxMembersPerDungeon)
            {
                return NpcLabor.LaborText.T("dis.error.tooManyDungeon", DungeonDispatchMission.MaxMembersPerDungeon);
            }

            // Dedup + validate.
            var members = new List<Chara>();
            var seen = new HashSet<int>();
            foreach (Chara w in workers)
            {
                if (w == null || !seen.Add(w.uid))
                {
                    continue;
                }

                if (!IsCandidate(w))
                {
                    return NpcLabor.LaborText.T("dis.error.cannotSend", w.NameSimple ?? w.Name ?? ("#" + w.uid));
                }

                members.Add(w);
            }

            if (members.Count == 0)
            {
                return NpcLabor.LaborText.T("dis.error.noOne");
            }

            if (IsZoneBusy(target.uid))
            {
                return NpcLabor.LaborText.T("dis.error.dungeonBusy", NpcLabor.LaborTerms.DungeonExplore);
            }

            // Also block same top dungeon.
            try
            {
                Zone top = target.GetTopZone() ?? target;
                if (FindByZone(top.uid) != null)
                {
                    return NpcLabor.LaborText.T("dis.error.dungeonBusy", NpcLabor.LaborTerms.DungeonExplore);
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
FactionBranch? branch = EClass.BranchOrHomeBranch ?? EClass.Branch ?? members[0].homeBranch;
            Zone? home = branch?.owner ?? members[0].homeZone ?? EClass.pc?.homeZone;
            if (branch == null || home == null)
            {
                return NpcLabor.LaborText.T("dis.error.needBase");
            }

            if (!DungeonDispatchTargets.IsDispatchable(target, home))
            {
                return NpcLabor.LaborText.T("dis.error.dungeonNo", NpcLabor.LaborTerms.DungeonExplore);
            }

            foreach (Chara worker in members)
            {
                if (IsParty(worker))
                {
                    try
                    {
                        EClass.pc?.party?.RemoveMember(worker);
                    }
                    catch (Exception ex)
                    {
                        Plugin.LogWarn("dispatch remove party failed: " + ex.Message);
                    }
                }
            }

            int power = DungeonDispatchTargets.AggregateCombatPower(members);
            int explore = DungeonDispatchTargets.AggregateSkill(members, DungeonDispatchMission.SkillExplore);
            int lockpick = DungeonDispatchTargets.AggregateSkill(members, DungeonDispatchMission.SkillLockpick);
            int gather = DungeonDispatchTargets.AggregateGatherSkill(members, target);

            int distDays = DungeonDispatchTargets.ZoneDistDays(home, target);
            int dangerDays = DungeonDispatchTargets.GetDangerDays(target.DangerLv, power);
            int baseHours = DungeonDispatchTargets.GetBaseHours(distDays, dangerDays);
            int hours = DungeonDispatchTargets.RollMissionHours(baseHours);
            int chance = DungeonDispatchTargets.GetSuccessChance(power, target.DangerLv, explore, lockpick, gather);

            Zone root = target;
            try
            {
                root = target.GetTopZone() ?? target;
            }
            catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
int entranceLv = 0;
            try
            {
                entranceLv = root.lv;
            }
            catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
var mission = new DungeonDispatchMission
            {
                missionId = _nextMissionId++,
                uidMembers = members.Select(m => m.uid).ToList(),
                uidChara = members[0].uid,
                uidZone = root.uid,
                zoneId = root.id ?? target.id ?? "",
                zoneName = DungeonDispatchTargets.SafeZoneName(root),
                dangerLv = Math.Max(1, root.DangerLv > 0 ? root.DangerLv : target.DangerLv),
                isRandomSite = root.isRandomSite || target.isRandomSite,
                hoursTotal = hours,
                hoursLeft = hours,
                combatPower = power,
                exploreSkill = explore,
                lockpickSkill = lockpick,
                gatherSkill = gather,
                successChance = chance,
                distDays = distDays,
                travelHours = Math.Max(DungeonDispatchTargets.GetRegionTravelHours(distDays), Math.Max(6, distDays * 12)),
                dangerDays = dangerDays,
                homeZoneUid = home.uid,
                currentFloorLv = entranceLv,
                deepestFloorLv = entranceLv,
                lootLog = new List<string>(),
            };

            // Move all members into the real target zone (friendly non-interactive stakeout).
            Zone startFloor = DungeonDispatchTargets.ResolveFloorForProgress(root, 0) ?? root;
            mission.currentFloorLv = SafeLv(startFloor);
            foreach (Chara worker in members)
            {
                try
                {
                    PrepareDispatchedChara(worker);
                    worker.MoveZone(startFloor, ZoneTransition.EnterState.RandomVisit);
                }
                catch (Exception ex)
                {
                    Plugin.LogWarn("dispatch MoveZone failed: " + ex.Message);
                    try
                    {
                        worker.MoveZone(startFloor);
                    }
                    catch
                    {
                        try
                        {
                            worker.MoveZone(root);
                        }
                        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
}
                }

                try
                {
                    PrepareDispatchedChara(worker);
                }
                catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
}

            Missions.Add(mission);
            RememberLastDispatch(mission);
            TryStartTrackerQuest(mission);
            try { Save(); } catch { }

            string names = mission.MemberNames();
            string msg = NpcLabor.LaborText.T(
                "dis.start.dungeon",
                names,
                NpcLabor.LaborTerms.DungeonExplore,
                mission.zoneName,
                members.Count,
                Mathf.CeilToInt(hours / 24f));
            try
            {
                Msg.Say(msg);
            }
            catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
            try
            {
                branch.LogRaw(msg);
            }
            catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
Plugin.LogInfo("dispatch start mission=" + mission.missionId + " members=" + members.Count
                + " zone=" + root.uid + " hours=" + hours);
            return null;
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("dispatch TryStart failed: " + ex);
            return NpcLabor.LaborText.T("dis.error.startFail", ex.Message);
        }
    }

    static int SafeLv(Zone z)
    {
        try
        {
            return z.lv;
        }
        catch
        {
            return 0;
        }
    }

    static int FloorDepthAbs(int entranceLv, int floorLv)
    {
        return Math.Abs(floorLv - entranceLv);
    }

    internal static void PrepareDispatchedChara(Chara c)
    {
        if (c == null)
        {
            return;
        }

        try
        {
            c.SetHostility(Hostility.Friend);
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }

        try
        {
            c.noMove = true;
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }

        try
        {
            c.enemy = null;
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }

        try
        {
            // Keep them from normal resident talk trees while exploring.
            c.isRestrained = true;
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }

        try
        {
            c.SetAIIdle();
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
    }

    internal static void ClearDispatchedFlags(Chara? c)
    {
        if (c == null)
        {
            return;
        }

        try
        {
            c.noMove = false;
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }

        try
        {
            c.isRestrained = false;
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }

        try
        {
            // PrepareDispatchedChara cleared the target. Keep it cleared so a returning
            // worker does not charge straight back into whatever it was fighting.
            c.enemy = null;
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }

        try
        {
            // Hand the worker back to the normal resident AI instead of leaving it
            // parked on whatever state the dispatch left behind.
            c.SetAIIdle();
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
    }

    internal static bool TryRecall(int missionIdOrCharaUid)
    {
        DungeonDispatchMission? m = FindByMissionId(missionIdOrCharaUid) ?? FindByChara(missionIdOrCharaUid);
        if (m == null)
        {
            return false;
        }

        Settle(m, DispatchSettleKind.Recall, destroyRandom: false);
        return true;
    }
}
