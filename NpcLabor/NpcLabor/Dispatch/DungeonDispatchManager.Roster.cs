using NpcLabor.TownLabor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace NpcLabor.Dispatch;

/// <summary>
/// PARTIAL: Candidate listing, eligibility checks, last-team presets, auto pick.
/// Split from DungeonDispatchManager.cs (2026-10-02).
/// </summary>
internal static partial class DungeonDispatchManager
{
    internal static List<Chara> ListCandidates()
    {
        var list = new List<Chara>();
        var seen = new HashSet<int>();

        try
        {
            Party? party = EClass.pc?.party;
            if (party?.members != null)
            {
                foreach (Chara m in party.members)
                {
                    if (!IsCandidate(m) || !seen.Add(m.uid))
                    {
                        continue;
                    }

                    list.Add(m);
                }
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
        try
        {
            FactionBranch? branch = EClass.BranchOrHomeBranch ?? EClass.Branch;
            if (branch?.members != null)
            {
                foreach (Chara m in branch.members)
                {
                    if (!IsCandidate(m) || !seen.Add(m.uid))
                    {
                        continue;
                    }

                    list.Add(m);
                }
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
list.Sort((a, b) =>
        {
            int pa = IsParty(a) ? 0 : 1;
            int pb = IsParty(b) ? 0 : 1;
            int cmp = pa.CompareTo(pb);
            if (cmp != 0)
            {
                return cmp;
            }

            int ca = DungeonDispatchTargets.GetCombatPower(a);
            int cb = DungeonDispatchTargets.GetCombatPower(b);
            cmp = cb.CompareTo(ca);
            return cmp != 0 ? cmp : a.uid.CompareTo(b.uid);
        });
        return list;
    }

    static bool IsParty(Chara c) => LaborRoster.IsPartyMember(c);

    internal static bool IsCandidate(Chara? c)
    {
        if (c == null)
        {
            return false;
        }

        try
        {
            if (CoCraft.PersonPickerUi.IsPcLike(c) || c.isDead)
            {
                return false;
            }

            if (CoCraft.PersonPickerUi.IsStayHomeUnique(c))
            {
                return false;
            }

            // Shared busy across A/B/D/E.
            if (LaborBusy.IsBusy(c.uid) || IsBusy(c.uid))
            {
                return false;
            }

            return LaborRoster.IsInPool(c);
        }
        catch
        {
            return false;
        }
    }

    static string TargetKey(DungeonDispatchTarget target)
    {
        if (target == null)
        {
            return "";
        }

        if (target.IsRegion)
        {
            return "region:" + DungeonDispatchTargets.NormalizeRegionKind(target.RegionKind);
        }

        try
        {
            if (target.Zone != null && target.Zone.uid > 0)
            {
                return "zone:" + target.Zone.uid;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
        try
        {
            if (target.Zone != null && !string.IsNullOrEmpty(target.Zone.id))
            {
                return "zoneid:" + target.Zone.id;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
return "name:" + (target.Name ?? "");
    }

    static string TargetKey(DungeonDispatchMission mission)
    {
        if (mission == null)
        {
            return "";
        }

        if (mission.isRegion)
        {
            return "region:" + DungeonDispatchTargets.NormalizeRegionKind(mission.regionKind);
        }

        if (mission.uidZone > 0)
        {
            return "zone:" + mission.uidZone;
        }

        if (!string.IsNullOrEmpty(mission.zoneId))
        {
            return "zoneid:" + mission.zoneId;
        }

        return "name:" + (mission.zoneName ?? "");
    }

    internal static void RememberLastDispatch(DungeonDispatchMission mission)
    {
        if (mission == null)
        {
            return;
        }

        try
        {
            mission.EnsureMemberList();
            string key = TargetKey(mission);
            if (string.IsNullOrEmpty(key) || mission.uidMembers == null || mission.uidMembers.Count == 0)
            {
                return;
            }

            var preset = new DungeonDispatchLastPreset
            {
                key = key,
                isRegion = mission.isRegion,
                regionKind = mission.regionKind ?? "",
                uidZone = mission.uidZone,
                zoneId = mission.zoneId ?? "",
                exploreWeeks = Mathf.Clamp(mission.exploreWeeks <= 0 ? 1 : mission.exploreWeeks, 1, 4),
                uidMembers = mission.uidMembers.ToList(),
            };

            for (int i = LastPresets.Count - 1; i >= 0; i--)
            {
                if (LastPresets[i] != null
                    && string.Equals(LastPresets[i].key, key, StringComparison.OrdinalIgnoreCase))
                {
                    LastPresets.RemoveAt(i);
                }
            }

            LastPresets.Insert(0, preset);
            while (LastPresets.Count > 24)
            {
                LastPresets.RemoveAt(LastPresets.Count - 1);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("dispatch remember last: " + ex.Message);
        }
    }

    internal static DungeonDispatchLastPreset? GetLastPreset(DungeonDispatchTarget target)
    {
        if (target == null)
        {
            return null;
        }

        string key = TargetKey(target);
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        for (int i = 0; i < LastPresets.Count; i++)
        {
            DungeonDispatchLastPreset p = LastPresets[i];
            if (p != null && string.Equals(p.key, key, StringComparison.OrdinalIgnoreCase))
            {
                return p;
            }
        }

        return null;
    }

    internal static List<Chara> ResolveAvailableMembers(IEnumerable<int> uids, int max = DungeonDispatchMission.MaxMembersPerDungeon)
    {
        var list = new List<Chara>();
        if (uids == null)
        {
            return list;
        }

        var seen = new HashSet<int>();
        foreach (int uid in uids)
        {
            if (uid <= 0 || !seen.Add(uid) || list.Count >= max)
            {
                continue;
            }

            if (IsBusy(uid))
            {
                continue;
            }

            Chara? c = null;
            try { c = RefChara.Get(uid); } catch { c = null; }
            if (c == null)
            {
                continue;
            }

            try
            {
                if (!IsCandidate(c))
                {
                    continue;
                }
            }
            catch
            {
                continue;
            }

            list.Add(c);
        }

        return list;
    }

    internal static List<Chara> GetLastDispatchMembers(DungeonDispatchTarget target)
    {
        DungeonDispatchLastPreset? preset = GetLastPreset(target);
        if (preset?.uidMembers == null || preset.uidMembers.Count == 0)
        {
            return new List<Chara>();
        }

        return ResolveAvailableMembers(preset.uidMembers);
    }

    internal static int GetLastExploreWeeks(DungeonDispatchTarget target)
    {
        DungeonDispatchLastPreset? preset = GetLastPreset(target);
        if (preset == null)
        {
            return 1;
        }

        return Mathf.Clamp(preset.exploreWeeks <= 0 ? 1 : preset.exploreWeeks, 1, 4);
    }

    /// <summary>
    /// Greedy pick of up to 4 available members maximizing expected haul score.
    /// Region: explore + gather (+ region gather bias). Dungeon: combat power first, then skills.
    /// </summary>
    internal static List<Chara> PickMaxHarvestMembers(DungeonDispatchTarget target)
    {
        var picked = new List<Chara>();
        if (target == null)
        {
            return picked;
        }

        List<Chara> candidates = ListCandidates();
        if (candidates.Count == 0)
        {
            return picked;
        }

        bool region = target.IsRegion;
        string rk = DungeonDispatchTargets.NormalizeRegionKind(target.RegionKind);
        var scored = new List<(Chara c, int score, int power)>();
        for (int i = 0; i < candidates.Count; i++)
        {
            Chara c = candidates[i];
            int explore = DungeonDispatchTargets.SafeSkill(c, DungeonDispatchMission.SkillExplore);
            int lockpick = DungeonDispatchTargets.SafeSkill(c, DungeonDispatchMission.SkillLockpick);
            int gather = DungeonDispatchTargets.SafeSkill(c, DungeonDispatchMission.SkillGather);
            int specialty = 0;
            if (region)
            {
                // Per-region specialty: beach dig/fish, forest lumber, mountain mine.
                // Plain has none, so it falls back to the plain gathering skill.
                specialty = DungeonDispatchTargets.RegionSpecialtySkill(c, rk, gather);
                gather = Math.Max(gather, specialty);
            }
            else
            {
                gather = Math.Max(gather, DungeonDispatchTargets.GetRelevantGatherSkill(c, target.Zone));
            }

            int power = DungeonDispatchTargets.GetCombatPower(c);
            int score;
            if (region)
            {
                // Region haul is skill-driven; weight local specialty over flat lockpick.
                // explore materials + specialty food/mats + light lockpick.
                score = explore * 2 + specialty * 3 + gather + lockpick;
            }
            else
            {
                // Dungeon haul is smaller; combat success gates mail, so weight power heavily.
                score = power * 8 + explore + gather + lockpick;
            }

            scored.Add((c, score, power));
        }

        scored.Sort((a, b) =>
        {
            int cmp = b.score.CompareTo(a.score);
            if (cmp != 0) return cmp;
            cmp = b.power.CompareTo(a.power);
            if (cmp != 0) return cmp;
            return string.Compare(a.c.NameSimple ?? a.c.Name, b.c.NameSimple ?? b.c.Name, StringComparison.Ordinal);
        });

        for (int i = 0; i < scored.Count && picked.Count < DungeonDispatchMission.MaxMembersPerDungeon; i++)
        {
            picked.Add(scored[i].c);
        }

        return picked;
    }
}
