using System;
using System.Collections.Generic;
using UnityEngine;

namespace NpcLabor.Dispatch;

/// <summary>
/// PARTIAL: haul formulas + locked region plans/contracts.
/// Split from DungeonDispatchRewards.cs (2026-08-07).
/// </summary>
internal static partial class DungeonDispatchRewards
{

    /// <summary>
    /// Split total qty across ids as evenly as possible (remainder +1 on first ids).
    /// Used so forest foods / plain flowers do not collapse to one lucky id.
    /// </summary>

    /// <summary>
    /// After top-up, redistribute all forest food counts evenly across mushroom/fruit ids.
    /// Keeps total food qty, replaces lopsided berry floods.
    /// </summary>
    static void RebalanceEvenForestFoods(List<Thing> things)
    {
        if (things == null || things.Count == 0)
        {
            return;
        }

        int foodTotal = 0;
        for (int i = things.Count - 1; i >= 0; i--)
        {
            Thing t = things[i];
            if (t == null)
            {
                things.RemoveAt(i);
                continue;
            }

            if (!IsForestFoodThing(t))
            {
                continue;
            }

            int n = 1;
            try { n = Math.Max(1, t.Num); } catch { n = 1; }
            foodTotal += n;
             if (t.parent == null) t.Destroy();
            things.RemoveAt(i);
        }

        if (foodTotal <= 0)
        {
            return;
        }

        // Keep at least one of each creatable food when budget allows.
        int minBudget = ForestFoodIds.Length;
        if (foodTotal < minBudget)
        {
            foodTotal = minBudget;
        }

        AddEvenThingStacks(things, ForestFoodIds, foodTotal, minEach: 1, maxEach: Math.Max(8, foodTotal));
    }


    static bool IsForestFoodThing(Thing t)
    {
        if (t == null)
        {
            return false;
        }


            string id = (t.id ?? "").ToLowerInvariant();
            string nm = "";
            try { nm = (t.NameOne ?? t.NameSimple ?? t.Name ?? "").ToLowerInvariant(); } catch { nm = ""; }
            if (id == "699" || id == "700")
            {
                return true;
            }

            if (id.Contains("mushroom") || id.Contains("berry") || id.Contains("grape")
                || id.Contains("apple") || id.Contains("banana") || id.Contains("palulu")
                || id.Contains("orange") || id.Contains("peach") || id.Contains("fruit"))
            {
                return true;
            }

            if (nm.Contains("\u8611\u83c7") || nm.Contains("\u84dd\u8393") || nm.Contains("mushroom")
                || nm.Contains("berry") || nm.Contains("grape") || nm.Contains("apple"))
            {
                return true;
            }

return false;
    }


    /// <summary>
    /// After top-up, redistribute plain flower counts evenly across the mission lock
    /// (2-4 kinds). Never expand back to every flower id.
    /// </summary>
    static void RebalanceEvenPlainFlowers(List<Thing> things)
    {
        if (things == null || things.Count == 0)
        {
            return;
        }

        int flowerTotal = 0;
        for (int i = things.Count - 1; i >= 0; i--)
        {
            Thing t = things[i];
            if (t == null)
            {
                things.RemoveAt(i);
                continue;
            }

            string id = "";
            try { id = (t.id ?? "").ToLowerInvariant(); } catch { id = ""; }
            if (!id.Contains("flower"))
            {
                continue;
            }

            int n = 1;
            try { n = Math.Max(1, t.Num); } catch { n = 1; }
            flowerTotal += n;
             if (t.parent == null) t.Destroy();
            things.RemoveAt(i);
        }

        if (flowerTotal <= 0)
        {
            return;
        }

        string[] ids = (_missionFlowerIds != null && _missionFlowerIds.Length > 0)
            ? _missionFlowerIds
            : PlainFlowerIds;
        if (ids == null || ids.Length == 0)
        {
            return;
        }

        // Cap kind count at 4 even if lock is missing for some reason.
        if (ids.Length > 4)
        {
            var clipped = new string[4];
            for (int i = 0; i < 4; i++)
            {
                clipped[i] = ids[i];
            }

            ids = clipped;
        }

        int minBudget = Math.Min(ids.Length, Math.Max(2, ids.Length));
        // Absolute qty half (explore+food flowers combined), then redistribute to lock kinds.
        flowerTotal = Math.Max(1, flowerTotal / 2);
        if (flowerTotal < minBudget)
        {
            flowerTotal = minBudget;
        }

        AddEvenThingStacks(things, ids, flowerTotal, minEach: 1, maxEach: Math.Max(8, flowerTotal));
    }

    /// <summary>
    /// After top-up, redistribute plain/forest herb counts evenly across the mission lock
    /// (2-4 kinds). Never expand back to every herb id.
    /// </summary>
    static void RebalanceEvenPlainHerbs(List<Thing> things)
    {
        if (things == null || things.Count == 0)
        {
            return;
        }

        int herbTotal = 0;
        for (int i = things.Count - 1; i >= 0; i--)
        {
            Thing t = things[i];
            if (t == null)
            {
                things.RemoveAt(i);
                continue;
            }

            string id = "";
            string nm = "";
            try { id = (t.id ?? "").ToLowerInvariant(); } catch { id = ""; }
            try { nm = t.NameOne ?? t.NameSimple ?? t.Name ?? ""; } catch { nm = ""; }
            bool isHerb = id == "herb" || id.StartsWith("herb_") || id.Contains("herb")
                || nm.Contains("药草") || nm.Contains("香草");
            if (!isHerb)
            {
                continue;
            }

            int n = 1;
            try { n = Math.Max(1, t.Num); } catch { n = 1; }
            herbTotal += n;
            if (t.parent == null) t.Destroy();
            things.RemoveAt(i);
        }

        if (herbTotal <= 0)
        {
            return;
        }

        string[] ids = (_missionHerbIds != null && _missionHerbIds.Length > 0)
            ? _missionHerbIds
            : PlainHerbIds;
        if (ids == null || ids.Length == 0)
        {
            return;
        }

        // Cap kind count at 4 even if lock is missing for some reason.
        if (ids.Length > 4)
        {
            var clipped = new string[4];
            for (int i = 0; i < 4; i++)
            {
                clipped[i] = ids[i];
            }

            ids = clipped;
        }

        int minBudget = Math.Min(ids.Length, Math.Max(2, ids.Length));
        if (herbTotal < minBudget)
        {
            herbTotal = minBudget;
        }

        AddEvenThingStacks(things, ids, herbTotal, minEach: 1, maxEach: Math.Max(8, herbTotal));
    }

    /// <summary>
    /// Plain meat: lock 2-4 species and cut total qty in half (same shape as flower pass).
    /// </summary>
    static void RebalanceEvenPlainMeats(List<Thing> things)
    {
        if (things == null || things.Count == 0)
        {
            return;
        }

        int meatTotal = 0;
        for (int i = things.Count - 1; i >= 0; i--)
        {
            Thing t = things[i];
            if (t == null)
            {
                things.RemoveAt(i);
                continue;
            }

            string id = "";
            string nm = "";
            try { id = (t.id ?? "").ToLowerInvariant(); } catch { id = ""; }
            try { nm = t.NameOne ?? t.NameSimple ?? t.Name ?? ""; } catch { nm = ""; }
            bool isMeat = id == "meat" || id == "_meat" || id == "meat_marble"
                || id.StartsWith("meat:") || id.Contains("meat")
                || nm.Contains("肉") || nm.ToLowerInvariant().Contains("meat");
            if (!isMeat)
            {
                continue;
            }

            int n = 1;
            try { n = Math.Max(1, t.Num); } catch { n = 1; }
            meatTotal += n;
            if (t.parent == null) t.Destroy();
            things.RemoveAt(i);
        }

        if (meatTotal <= 0)
        {
            return;
        }

        string[] species = (_missionMeatSpecies != null && _missionMeatSpecies.Length > 0)
            ? _missionMeatSpecies
            : PlainMeatSpeciesIds;
        if (species == null || species.Length == 0)
        {
            return;
        }

        if (species.Length > 4)
        {
            var clipped = new string[4];
            for (int i = 0; i < 4; i++)
            {
                clipped[i] = species[i];
            }

            species = clipped;
        }

        int minBudget = Math.Min(species.Length, Math.Max(2, species.Length));
        // Absolute qty half, then redistribute across 2-4 locked species.
        meatTotal = Math.Max(1, meatTotal / 2);
        if (meatTotal < minBudget)
        {
            meatTotal = minBudget;
        }

        // CreateNamedMeat needs species tokens; build stacks via meat:species ids.
        var meatIds = new string[species.Length];
        for (int i = 0; i < species.Length; i++)
        {
            meatIds[i] = "meat:" + species[i];
        }

        AddEvenMeatStacks(things, meatIds, meatTotal);
    }

    static void AddEvenMeatStacks(List<Thing> things, string[] meatIds, int total)
    {
        if (things == null || meatIds == null || meatIds.Length == 0 || total <= 0)
        {
            return;
        }

        var created = new List<Thing>();
        var keys = new List<string>();
        for (int i = 0; i < meatIds.Length; i++)
        {
            string raw = meatIds[i];
            if (string.IsNullOrEmpty(raw) || keys.Contains(raw))
            {
                continue;
            }

            string species = raw;
            if (raw.StartsWith("meat:", StringComparison.OrdinalIgnoreCase) && raw.Length > 5)
            {
                species = raw.Substring(5);
            }

            Thing? t = CreateNamedMeat(species) ?? CreateNamedMeat("chicken");
            if (t == null)
            {
                continue;
            }

            KeepFoodFresh(t);
            if (IsMudLikeThing(t) || IsScrapLikeThing(t) || IsGoldLikeThing(t) || IsForbiddenRegionThing(t))
            {
                if (t.parent == null) t.Destroy();
                continue;
            }

            created.Add(t);
            keys.Add(raw);
        }

        if (created.Count == 0)
        {
            return;
        }

        int kinds = created.Count;
        int baseEach = Math.Max(1, total / kinds);
        int rem = Math.Max(0, total - baseEach * kinds);
        for (int i = 0; i < created.Count; i++)
        {
            int n = baseEach + (i < rem ? 1 : 0);
            AddManualStack(things, created[i], n);
        }
    }


    static void AddEvenThingStacks(List<Thing> things, string[] ids, int total, int minEach, int maxEach)
    {
        if (things == null || ids == null || ids.Length == 0 || total <= 0)
        {
            return;
        }

        var created = new List<Thing>();
        var keys = new List<string>();
        for (int i = 0; i < ids.Length; i++)
        {
            string id = ids[i];
            if (string.IsNullOrEmpty(id) || keys.Contains(id))
            {
                continue;
            }

            Thing? t = TryCreate(id, 1);
            string low = id.ToLowerInvariant();
            if (t == null && low == "699")
            {
                t = TryCreate("white mushroom", 1) ?? TryCreate("mushroom", 1);
            }
            if (t == null && low == "700")
            {
                t = TryCreate("red mushroom", 1) ?? TryCreate("mushroom", 1);
            }
            if (t == null && (low == "1122" || low.Contains("chaos")))
            {
                t = TryCreate("chaos mushroom", 1) ?? TryCreate("mushroom_rare", 1);
            }

            if (t == null)
            {
                continue;
            }

             ForceFreshProduceMaterial(t);
             KeepFoodFresh(t);
            if (IsMudLikeThing(t) || IsScrapLikeThing(t) || IsGoldLikeThing(t) || IsForbiddenRegionThing(t))
            {
                 if (t.parent == null) t.Destroy();
                continue;
            }

            created.Add(t);
            keys.Add(id);
        }

        if (created.Count == 0)
        {
            return;
        }

        int n = created.Count;
        int baseQty = Math.Max(minEach, total / n);
        int rem = Math.Max(0, total - baseQty * n);
        for (int i = 0; i < n; i++)
        {
            int qty = baseQty + (i < rem ? 1 : 0);
            qty = Mathf.Clamp(qty, minEach, Math.Max(minEach, maxEach));
             created[i].SetNum(qty);
            things.Add(created[i]);
        }
    }


    /// <summary>
    /// Dungeon drop pool quality level. Vanilla DangerLv (no doubling).
    /// </summary>
    internal static int RewardLv(DungeonDispatchMission? mission)
    {
        int danger = mission == null ? 1 : Math.Max(1, mission.dangerLv);
        return Math.Max(1, danger);
    }

    /// <summary>Boss / success extras use a small bump over dangerLv (not doubled).</summary>
    internal static int BossRewardLv(DungeonDispatchMission? mission)
    {
        int danger = mission == null ? 1 : Math.Max(1, mission.dangerLv);
        return Math.Max(1, Mathf.RoundToInt(danger * NpcLabor.LaborConfig.BossRewardMult));
    }


    /// <summary>
    /// Explore skill only. Used for material/floor item quantity.
    /// </summary>
    internal static int ExploreSkillScore(DungeonDispatchMission? mission)
    {
        if (mission == null)
        {
            return 0;
        }

        return Math.Max(0, mission.exploreSkill);
    }


    /// <summary>
    /// Gather skill only. Region food / dungeon gather-node quantity.
    /// </summary>
    internal static int GatherSkillScore(DungeonDispatchMission? mission)
    {
        if (mission == null)
        {
            return 0;
        }

        return Math.Max(0, mission.gatherSkill);
    }


    /// <summary>
    /// Lockpick skill only. Chest simulation count.
    /// </summary>
    internal static int LockpickSkillScore(DungeonDispatchMission? mission)
    {
        if (mission == null)
        {
            return 0;
        }

        return Math.Max(0, mission.lockpickSkill);
    }


    /// <summary>
    /// Legacy combined score kept for auto-pick ranking compatibility.
    /// </summary>
    internal static int SkillScore(DungeonDispatchMission? mission)
    {
        return ExploreSkillScore(mission) + GatherSkillScore(mission) + LockpickSkillScore(mission);
    }


    static float MissionVariance(DungeonDispatchMission? mission)
    {
        if (mission == null)
        {
            return 1f;
        }

        EnsureHaulVariance(mission);
        return mission.haulVariance > 0.01f ? mission.haulVariance : 1f;
    }


    static int MissionWeeks(DungeonDispatchMission? mission)
    {
        if (mission == null)
        {
            return 1;
        }

        return Mathf.Clamp(mission.exploreWeeks <= 0 ? 1 : mission.exploreWeeks, 1, 4);
    }


    /// <summary>
    /// Explore haul total (materials / floor piles).
    /// Region: explore * weeks * variance.
    /// Dungeon: explore * variance.
    /// </summary>
    internal static int PlannedExploreHaulTotal(DungeonDispatchMission? mission)
    {
        if (mission == null)
        {
            return 1;
        }

        float variance = MissionVariance(mission);
        float raw;
        if (mission.isRegion)
        {
            raw = ExploreSkillScore(mission) * MissionWeeks(mission) * variance;
        }
        else
        {
            raw = ExploreSkillScore(mission) * variance;
        }

        int total = Mathf.RoundToInt(raw);
        return Mathf.Clamp(total, 1, 2000);
    }


    /// <summary>
    /// Gather haul total (region food append / dungeon gather nodes).
    /// Not mixed into explore material budget.
    /// Region: Round(gather * weeks * variance * 0.35).
    /// Dungeon: Round(gather * variance * 0.55).
    /// </summary>
    internal static int PlannedGatherHaulTotal(DungeonDispatchMission? mission)
    {
        if (mission == null)
        {
            return 0;
        }

        int gather = GatherSkillScore(mission);
        if (gather <= 0)
        {
            return 0;
        }

        float variance = MissionVariance(mission);
        float raw;
        if (mission.isRegion)
        {
            raw = gather * MissionWeeks(mission) * variance * 0.35f;
        }
        else
        {
            raw = gather * variance * 0.55f;
        }

        int total = Mathf.RoundToInt(raw);
        return Mathf.Clamp(total, 0, 800);
    }


    /// <summary>
    /// Simulated chest opens from lockpick.
    /// Unified: lockpick/lockpickPerChest (default 20 => 100 gives 5). Weeks do NOT multiply chest count.
    /// Clamp 0..lockpickMaxChests for both region and dungeon.
    /// </summary>
    internal static int PlannedLockpickChestCount(DungeonDispatchMission? mission)
    {
        if (mission == null)
        {
            return 0;
        }

        int lockpick = LockpickSkillScore(mission);
        if (lockpick <= 0)
        {
            return 0;
        }

        int per = LaborConfig.LockpickPerChest;
        int max = LaborConfig.LockpickMaxChests;
        return Mathf.Clamp(lockpick / per, 0, max);
    }


    /// <summary>
    /// Total planned item count for UI/debug:
    /// explore materials + gather food/nodes + expected lockpick currency stacks.
    /// </summary>
    internal static int PlannedHaulTotal(DungeonDispatchMission? mission)
    {
        if (mission == null)
        {
            return 1;
        }

        int total = PlannedExploreHaulTotal(mission)
            + PlannedGatherHaulTotal(mission)
            + PlannedLockpickChestCount(mission);
        return Mathf.Clamp(Math.Max(1, total), 1, 2800);
    }


    /// <summary>
    /// Compatibility alias used by stack caps / dungeon formulas.
    /// 1 haul unit ~= 100 planned items under the old /100 scaling.
    /// </summary>
    static float HaulUnits(DungeonDispatchMission? mission)
    {
        float units = PlannedHaulTotal(mission) / 100f;
        return Mathf.Clamp(units, 0.5f, 48f);
    }

    static float RegionHaulUnits(DungeonDispatchMission? mission) => HaulUnits(mission);

    internal static int RegionSkillScore(DungeonDispatchMission? mission) => ExploreSkillScore(mission);


    /// <summary>Roll once per mission: uniform 0.80 .. 1.20.</summary>
    static void EnsureHaulVariance(DungeonDispatchMission mission)
    {
        if (mission == null)
        {
            return;
        }

        if (mission.haulVariance >= 0.8f && mission.haulVariance <= 1.2f)
        {
            return;
        }

        try
        {
            mission.haulVariance = 0.80f + (EClass.rnd(41) / 100f);
        }
        catch
        {
            mission.haulVariance = 1f;
        }

        if (mission.haulVariance < 0.8f || mission.haulVariance > 1.2f)
        {
            mission.haulVariance = 1f;
        }
    }


    /// <summary>Debug/UI: planned full success haul text (not progress-scaled).</summary>
    /// Uses the same CreateFromPlanEntry path as Deliver so dump matches mail identity.
    internal static string FormatPlannedSuccessHarvest(DungeonDispatchMission mission, int max = 12)
    {
        if (mission == null)
        {
            return NpcLabor.LaborText.T("dis.loot.noMission");
        }

        try
        {
            EnsureRegionPlan(mission);
            mission.EnsureMemberList();
            if (mission.plannedLootEntries == null || mission.plannedLootEntries.Count == 0)
            {
                return NpcLabor.LaborText.T("dis.loot.noPlan");
            }

            var rebuilt = new List<Thing>();
            for (int i = 0; i < mission.plannedLootEntries.Count; i++)
            {
                PlannedLootEntry e = mission.plannedLootEntries[i];
                if (e == null)
                {
                    continue;
                }

                Thing? t = CreateFromPlanEntry(e, 1f);
                if (t == null)
                {
                    continue;
                }

                rebuilt.Add(t);
            }

            CompactStacks(rebuilt);

            var parts = new List<string>();
            int show = Math.Min(max, rebuilt.Count);
            for (int i = 0; i < show; i++)
            {
                string entry = FormatThingEntry(rebuilt[i]);
                if (!string.IsNullOrEmpty(entry))
                {
                    parts.Add(entry);
                }
            }

            if (rebuilt.Count > max)
            {
                parts.Add(NpcLabor.LaborText.T("dis.loot.moreKinds", rebuilt.Count));
            }

            int total = CountThingNums(rebuilt);
            for (int i = 0; i < rebuilt.Count; i++)
            {

                    Thing rt = rebuilt[i];
                    if (rt != null && rt.parent == null && !rt.isDestroyed)
                    {
                        rt.Destroy();
                    }

}

            // If recreate failed entirely, show locked plan names.
            if (parts.Count == 0)
            {
                for (int i = 0; i < Math.Min(max, mission.plannedLootEntries.Count); i++)
                {
                    PlannedLootEntry e = mission.plannedLootEntries[i];
                    if (e == null)
                    {
                        continue;
                    }

                    string name = !string.IsNullOrEmpty(e.name) ? e.name : (e.id ?? "?");
                    int qty = Math.Max(1, e.num);
                    parts.Add(qty > 1 ? (name + "x" + qty) : name);
                }

                total = 0;
                for (int ti = 0; ti < mission.plannedLootEntries.Count; ti++)
                {
                    try { total += Math.Max(1, mission.plannedLootEntries[ti].num); } catch { total++; }
                }
            }

            string body = parts.Count == 0 ? NpcLabor.LaborText.T("dis.loot.noPlan") : string.Join("、", parts);
            string varTxt = mission.haulVariance > 0.01f ? mission.haulVariance.ToString("0.00") : "1.00";
            return NpcLabor.LaborText.T("dis.loot.totalLine", body, total, varTxt);
        }
        catch (Exception ex)
        {
            return NpcLabor.LaborText.T("dis.loot.planFail", ex.Message);
        }
    }


    internal static void EnsureRegionPlanPublic(DungeonDispatchMission mission)
        => EnsureRegionPlan(mission);

    static void EnsureRegionPlan(DungeonDispatchMission mission)
    {
        if (mission == null || !mission.isRegion)
        {
            return;
        }

        mission.EnsureMemberList();
        // v11 composition: force rebuild — pin copper/sea sand after create; bad v10 plans re-roll.
        const int CurrentPlanGenVersion = ManualRegionPlanGenVersion; // 26: fixed 4-biome pools
        if (mission.plannedLootEntries != null
            && mission.plannedLootEntries.Count > 0
            && mission.planGenVersion >= CurrentPlanGenVersion)
        {
            // Still run lightweight repairs for mud/gold leaks on current-gen plans.
            RepairRegionPlanEntries(mission);

            mission.plannedLootReady = true;
            // Always re-scale tracker to current progress (plan stays fixed).
            RefreshRegionProgressLog(mission);
            return;
        }

        // Drop stale plan so CollectRegionThings rebuilds under current rules.
        if (mission.plannedLootEntries != null && mission.plannedLootEntries.Count > 0)
        {
            mission.plannedLootEntries = new List<PlannedLootEntry>();
            mission.plannedLootReady = false;
        }

        try
        {
            var planned = new List<Thing>();
            CollectRegionThings(mission, planned);
            StripRegionJunk(planned);
            CompactStacks(planned);
            // Do not soft-cap planned stacks: planned total is the economy contract.
            mission.plannedHaulTotal = CountThingNums(planned);
            mission.plannedLootEntries = ThingsToPlanEntries(planned);
            mission.plannedLootReady = true;
            mission.planGenVersion = CurrentPlanGenVersion;

            // Free temporary things; entries are the save-safe source of truth.
            for (int i = 0; i < planned.Count; i++)
            {

                    Thing t = planned[i];
                    if (t != null && t.parent == null && !t.isDestroyed)
                    {
                        t.Destroy();
                    }

}

            RefreshRegionProgressLog(mission);
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("ensure region plan: " + ex.Message);
        }
    }


    static void RefreshRegionProgressLog(DungeonDispatchMission mission)
    {
        if (mission == null)
        {
            return;
        }


            mission.EnsureMemberList();
            if (mission.plannedLootEntries == null || mission.plannedLootEntries.Count == 0)
            {
                return;
            }

            // Planned final haul is locked; tracker shows harvest-earned portion only.
            // Travel phase => 0. Mail uses the exact same HarvestProgressPercent scale.
            float progress = Mathf.Clamp01(mission.HarvestProgressPercent / 100f);
            mission.lootLog.Clear();
            if (progress <= 0.0001f)
            {
                return;
            }

            for (int i = 0; i < mission.plannedLootEntries.Count; i++)
            {
                PlannedLootEntry e = mission.plannedLootEntries[i];
                if (e == null || (string.IsNullOrEmpty(e.name) && string.IsNullOrEmpty(e.id)))
                {
                    continue;
                }

                int planned = Math.Max(1, e.num);
                // Strict floor so details == mail (no half-unit early 1).
                int shown = Mathf.FloorToInt(planned * progress + 0.0001f);
                if (shown <= 0)
                {
                    continue;
                }

                string name = !string.IsNullOrEmpty(e.name) ? e.name : e.id;
                mission.lootLog.Add(shown > 1 ? (name + "x" + shown) : name);
            }

    }



    /// <summary>
    /// Last-resort rebuild from tracker lines already shown to the player.
    /// Ensures recall mail cannot invent items the details never listed.
    /// </summary>
    static bool TryBuildFromLootLog(DungeonDispatchMission mission, List<Thing> dest)
    {
        if (mission == null || dest == null)
        {
            return false;
        }

        mission.EnsureMemberList();
        if (mission.lootLog == null || mission.lootLog.Count == 0)
        {
            return false;
        }

        int before = dest.Count;
        for (int i = 0; i < mission.lootLog.Count; i++)
        {
            string raw = mission.lootLog[i] ?? "";
            string key = raw.Trim();
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            int bracket = key.LastIndexOf(']');
            if (key.StartsWith("[") && bracket > 0 && bracket + 1 < key.Length)
            {
                key = key.Substring(bracket + 1).Trim();
            }

            int qty = 1;
            int xAt = key.LastIndexOf('x');
            int multAt = key.LastIndexOf('\u00d7');
            int cut = Math.Max(xAt, multAt);
            string name = key;
            if (cut > 0 && cut < key.Length - 1)
            {
                string tail = key.Substring(cut + 1).Trim();
                int n;
                if (int.TryParse(tail, out n) && n > 0)
                {
                    qty = n;
                    name = key.Substring(0, cut).Trim();
                }
            }

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            // Prefer matching a still-known plan entry by name.
            PlannedLootEntry? match = null;
            if (mission.plannedLootEntries != null)
            {
                for (int j = 0; j < mission.plannedLootEntries.Count; j++)
                {
                    PlannedLootEntry e = mission.plannedLootEntries[j];
                    if (e == null)
                    {
                        continue;
                    }

                    string en = !string.IsNullOrEmpty(e.name) ? e.name : e.id;
                    if (string.Equals(en, name, StringComparison.OrdinalIgnoreCase))
                    {
                        match = e;
                        break;
                    }
                }
            }

            Thing? t = null;
            if (match != null)
            {
                t = CreateFromPlanEntry(match, 1f);
                if (t != null)
                {
                     t.SetNum(Math.Max(1, qty));
                }
            }

            if (t == null)
            {
                // Cannot safely invent from display name alone.
                continue;
            }

            dest.Add(t);
        }

        return dest.Count > before;
    }


    static bool TryTakeRegionPlanThings(DungeonDispatchMission mission, List<Thing> dest, float scale = 1f)
    {
        if (mission == null || dest == null || !mission.isRegion)
        {
            return false;
        }

        EnsureRegionPlan(mission);
        // Force tracker to the same harvest scale we are about to pay.
        RefreshRegionProgressLog(mission);
        if (mission.plannedLootEntries == null || mission.plannedLootEntries.Count == 0)
        {
            return false;
        }

                float s = Mathf.Clamp01(scale);
        // Always derive qty the same way as RefreshRegionProgressLog:
        // floor(plan * harvest). Tracker text is only a display mirror.
        for (int i = 0; i < mission.plannedLootEntries.Count; i++)
        {
            PlannedLootEntry e = mission.plannedLootEntries[i];
            if (e == null)
            {
                continue;
            }

            int planned = Math.Max(1, e.num);
            int qty = Mathf.FloorToInt(planned * s + 0.0001f);
            if (qty <= 0)
            {
                continue;
            }

            Thing? t = CreateFromPlanEntry(e, 1f);
            if (t == null)
            {
                continue;
            }

             t.SetNum(Math.Max(1, qty));
            dest.Add(t);
        }

        // Keep plan until a non-empty take succeeds so retries still match details.
        if (dest.Count > 0)
        {
            mission.plannedLootEntries = new List<PlannedLootEntry>();
            mission.plannedLootReady = false;
        }

        return dest.Count > 0;
    }


    static Dictionary<string, int> ParseLootLogQtys(DungeonDispatchMission mission)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (mission?.lootLog == null)
        {
            return counts;
        }

        for (int i = 0; i < mission.lootLog.Count; i++)
        {
            string raw = mission.lootLog[i] ?? "";
            string key = raw.Trim();
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            int bracket = key.LastIndexOf(']');
            if (key.StartsWith("[") && bracket > 0 && bracket + 1 < key.Length)
            {
                key = key.Substring(bracket + 1).Trim();
            }

            int qty = 1;
            int xAt = key.LastIndexOf('x');
            int multAt = key.LastIndexOf('\u00d7');
            int cut = Math.Max(xAt, multAt);
            if (cut > 0 && cut < key.Length - 1)
            {
                string tail = key.Substring(cut + 1).Trim();
                int n;
                if (int.TryParse(tail, out n) && n > 0)
                {
                    qty = n;
                    key = key.Substring(0, cut).Trim();
                }
            }

            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            if (!counts.ContainsKey(key))
            {
                counts[key] = 0;
            }

            counts[key] += Math.Max(1, qty);
        }

        return counts;
    }


    static List<PlannedLootEntry> ThingsToPlanEntries(List<Thing> things)
    {
        var list = new List<PlannedLootEntry>();
        if (things == null)
        {
            return list;
        }

        for (int i = 0; i < things.Count; i++)
        {
            Thing t = things[i];
            if (t == null)
            {
                continue;
            }

            try
            {
                var e = new PlannedLootEntry();
                e.id = t.id ?? "";
                try { e.num = Math.Max(1, t.Num); } catch { e.num = 1; }
                try
                {
                    e.matAlias = t.material != null ? (t.material.alias ?? "") : "";
                    e.matId = t.material != null ? t.material.id : -1;
                }
                catch
                {
                    e.matAlias = "";
                    e.matId = -1;
                }

                try
                {
                    e.refCard = t.c_idRefCard ?? "";
                }
                catch
                {
                    e.refCard = "";
                }

                try
                {
                    e.name = t.NameOne ?? t.NameSimple ?? t.Name ?? e.id;
                }
                catch
                {
                    e.name = e.id;
                }

                // Named meat/egg: keep stable meat:ref / egg:ref tokens so recreate never loses species.
                string idLow = (e.id ?? "").ToLowerInvariant();
                if ((idLow == "_meat" || idLow == "meat_marble" || idLow.Contains("meat"))
                    && !string.IsNullOrEmpty(e.refCard))
                {
                    e.id = "meat:" + e.refCard;
                }
                else if ((idLow == "egg" || idLow == "_egg" || idLow == "egg_fertilized" || idLow.StartsWith("egg:"))
                    && !string.IsNullOrEmpty(e.refCard))
                {
                    string sp = e.refCard;
                    if (!IsAllowedPlainEggSpecies(sp))
                    {
                        sp = "chicken";
                        e.refCard = sp;
                    }
                    e.id = "egg:" + sp;
                    e.matAlias = "fresh";
                }

                // v11: lock beach/mountain identity aliases into the plan so recreate cannot drift.

                    if (IsSeaSandThing(t) || IsSeaSandMaterialAlias(e.matAlias) || (e.matAlias ?? "").ToLowerInvariant().Contains("sand"))
                    {
                        string sea = ResolveMaterialAlias("sand_sea");
                        if (string.IsNullOrEmpty(sea)) sea = "sand_sea";
                        if (IsSeaSandThing(t) || IsSeaSandMaterialAlias(e.matAlias) || (e.name ?? "").Contains("海沙")
                            || (e.name ?? "").ToLowerInvariant().Contains("sea sand"))
                        {
                            e.matAlias = sea;
                            e.matId = SeaSandMaterialId();
                            if (string.IsNullOrEmpty(e.id) || e.id == "sand" || e.id == "scrap")
                            {
                                e.id = t.id ?? "chunk";
                            }

                            e.name = "sea sand";
                        }
                    }

                    string liveMat = LiveMaterialAlias(t).ToLowerInvariant();
                    if (IsForbiddenHighOreMaterial(liveMat))
                    {
                        // Should have been stripped earlier; rewrite plan to copper.
                        e.matAlias = "copper";
                        e.matId = CopperMaterialId();
                        e.id = string.IsNullOrEmpty(e.id) ? "ore" : (e.id ?? "ore");
                        e.name = "copper";
                    }
                    else if (!string.IsNullOrEmpty(liveMat))
                    {
                        e.matAlias = liveMat;
                        // Persist mountain metals as ore cards, not finished ingots.
                        string idNow = (e.id ?? "").ToLowerInvariant();
                        if ((idNow == "ingot" || idNow.Contains("ingot") || idNow == "bar")
                            && (IsOreMetalAlias(liveMat) || liveMat == "copper" || liveMat == "iron"
                                || liveMat == "bronze" || liveMat == "steel" || liveMat == "silver"))
                        {
                            e.id = "ore";
                        }
                    }

if (!string.IsNullOrEmpty(e.id) || !string.IsNullOrEmpty(e.name))
                {
                    list.Add(e);
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchRewards.cs silent catch: " + __e.Message); }
}

        return list;
    }


    static Thing? CreateFromPlanEntry(PlannedLootEntry entry, float scale)
    {
        if (entry == null)
        {
            return null;
        }

        try
        {
            Thing? t = null;
            string mat = entry.matAlias ?? "";
            if (!string.IsNullOrEmpty(mat))
            {
                string resolvedMat = ResolveMaterialAlias(mat);
                if (!string.IsNullOrEmpty(resolvedMat))
                {
                    mat = resolvedMat;
                }
            }
            int matId = entry.matId;
            if (matId <= 0 && !string.IsNullOrEmpty(mat)
                && EClass.sources?.materials?.alias != null
                && EClass.sources.materials.alias.ContainsKey(mat))
            {
                try { matId = EClass.sources.materials.alias[mat].id; } catch { matId = -1; }
            }
            string id = entry.id ?? "";
            string idLow = id.ToLowerInvariant();
            string refCard = entry.refCard ?? "";

            // v15 identity-first by runtime-resolved material id (alias / MATERIAL.*).
            int seaSandId = SeaSandMaterialId();
            bool wantSeaSand = (seaSandId > 0 && matId == seaSandId)
                || IsSeaSandMaterialAlias(mat)
                || mat == "sand_sea"
                || mat == "sea sand"
                || idLow == "sand"
                || idLow == "sand_sea"
                || idLow == "seasand"
                || idLow == "sea_sand"
                || (entry.name ?? "").Contains("海沙")
                || (entry.name ?? "").Contains("海砂")
                || (entry.name ?? "").ToLowerInvariant().Contains("sea sand");
            if (wantSeaSand)
            {
                int sid = seaSandId > 0 ? seaSandId : ResolveMaterialId("sand_sea");
                t = (sid > 0 ? CreateByMaterialId(sid, preferredCarriers: new[] { "chunk", "rock", "pebble", "stone" }) : null)
                    ?? CreateBeachSand();
            }
            else if (idLow == "sulfur" || idLow == "sulphur" || idLow == "brimstone"
                || ((entry.matAlias ?? "").IndexOf("sulf", StringComparison.OrdinalIgnoreCase) >= 0)
                || ((entry.name ?? "").IndexOf("sulf", StringComparison.OrdinalIgnoreCase) >= 0)
                || ((entry.name ?? "").Contains("硫黄"))
                || ((entry.name ?? "").Contains("硫磺"))
                || ((entry.name ?? "").Contains("硫")))
            {
                t = CreateMountainSulfur();
            }
            else if (matId > 0 && (idLow == "ore" || idLow == "rock" || idLow == "stone" || idLow == "chunk"
                || idLow == "ingot" || idLow == "cutstone" || idLow == "scrap" || string.IsNullOrEmpty(id)
                || idLow == "ore_gem" || idLow == "gem"))
            {
                string[] carriers;
                if (idLow == "ingot" || idLow == "cutstone")
                {
                    // Planned metal rewards should still prefer raw ore over finished bars.
                    carriers = new[] { "ore", "rock", idLow, "chunk" };
                }
                else
                {
                    carriers = new[] { string.IsNullOrEmpty(idLow) ? "ore" : idLow, "ore", "rock", "chunk", "ingot" };
                }
                t = CreateByMaterialId(matId, preferredCarriers: carriers);
            }

            // Only invent from id/meat when material-id create did not already succeed.
            if (t == null && idLow.StartsWith("meat:"))
            {
                string species = id.Substring(5);
                t = CreateNamedMeat(species);
                if (t == null && !string.IsNullOrEmpty(refCard))
                {
                    t = CreateNamedMeat(refCard);
                }
            }
            else if (t == null && (idLow.StartsWith("egg:") || idLow == "egg" || idLow == "_egg" || idLow == "egg_fertilized"))
            {
                string species = idLow.StartsWith("egg:") && id.Length > 4 ? id.Substring(4) : refCard;
                t = CreateNamedEgg(string.IsNullOrEmpty(species) ? null : species);
                if (t == null && !string.IsNullOrEmpty(refCard))
                {
                    t = CreateNamedEgg(refCard);
                }
            }
            else if (t == null && !string.IsNullOrEmpty(refCard)
                && (idLow == "_meat" || idLow == "meat_marble" || idLow.Contains("meat")))
            {
                t = CreateNamedMeat(refCard);
            }
            else if (t == null && !string.IsNullOrEmpty(id))
            {
                // Bare ore without mat becomes copper/iron, never gold lottery.
                if (idLow == "ore" && string.IsNullOrEmpty(mat))
                {
                    t = CreateWeightedOre(common: true);
                }
                else if (idLow == "ore" || idLow == "rock" || idLow == "stone" || idLow == "chunk"
                    || idLow == "ore_gem" || idLow == "gem")
                {
                    if (!string.IsNullOrEmpty(mat)
                        && EClass.sources?.materials?.alias != null
                        && EClass.sources.materials.alias.ContainsKey(mat))
                    {
                        try { t = ThingGen.Create(id, mat, 1); } catch { t = null; }
                        if (t == null)
                        {
                            t = CreateMaterialThing(mat);
                        }
                    }
                    else if (idLow == "ore_gem" || idLow == "gem")
                    {
                        t = CreateMountainGem();
                    }
                    else if (idLow == "ore")
                    {
                        t = CreateWeightedOre(common: true);
                    }
                }

                if (t == null
                    && !string.IsNullOrEmpty(mat)
                    && EClass.sources?.materials?.alias != null
                    && EClass.sources.materials.alias.ContainsKey(mat))
                {
                    try
                    {
                        t = ThingGen.Create(id, mat, 1);
                    }
                    catch
                    {
                        t = null;
                    }
                }

                if (t == null)
                {
                    // Special aliases that are not raw cards.
                    if (idLow == "sand" || idLow == "sand_sea" || idLow == "sea sand" || idLow == "seasand"
                        || idLow == "sea_sand" || idLow == "sand_white" || idLow == "white sand")
                    {
                        t = CreateBeachSand();
                    }
                    else if (idLow == "salt")
                    {
                        t = CreateBeachSalt() ?? TryCreate("salt", 1);
                    }
                    else if (idLow == "sulfur" || idLow == "sulphur" || idLow == "brimstone")
                    {
                        t = CreateMountainSulfur() ?? TryCreate("sulfur", 1);
                    }
                    else if (idLow == "plastic" || idLow == "resin")
                    {
                        t = CreatePlasticOre()
                            ?? TryCreate("resin", 1)
                            ?? TryCreate("plastic", 1)
                            ?? CreateMaterialThing("plastic");
                    }
                    else if (idLow == "seaweed" || idLow == "seaweed2")
                    {
                        t = TryCreate("seaweed", 1)
                            ?? TryCreate("seaweed2", 1)
                            ?? TryCreateFromCategorySafe("plant", 1)
                            ?? TryCreate("bait", 1);
                    }
                    else if (idLow == "bait")
                    {
                        t = TryCreate("bait", 1) ?? TryCreate("seaweed", 1);
                    }
                    else if (idLow == "fish" || idLow == "65")
                    {
                        t = TryCreate(id, 1) ?? TryCreate("fish", 1) ?? TryCreate("65", 1);
                        if (t != null)
                        {
                            KeepFoodFresh(t);
                        }
                    }
                    else if (idLow == "vine")
                    {
                        t = CreateForestVine() ?? TryCreate("vine", 1) ?? TryCreate("weed", 1);
                    }
                    else if (idLow == "branch")
                    {
                        t = TryCreate("branch", 1)
                            ?? CreateWoodLog("wood")
                            ?? TryCreate("log", 1);
                    }
                    else if (idLow == "bark")
                    {
                        t = TryCreate("bark", 1)
                            ?? CreateMaterialThing("bark")
                            ?? TryCreate("branch", 1)
                            ?? CreateForestVine();
                    }
                    else if (idLow == "log" || idLow == "wood")
                    {
                        t = CreateWoodLog("wood") ?? TryCreate("log", 1);
                    }
                    else if (idLow == "mushroom")
                    {
                        t = CreateForestMushroom(common: true)
                            ?? TryCreate("mushroom", 1)
                            ?? TryCreate("699", 1)
                            ?? TryCreate("700", 1)
                            ?? TryCreateFromCategorySafe("mushroom", 1);
                    }
                    else if (idLow == "berry")
                    {
                        t = TryCreate("berry", 1)
                            ?? TryCreateFromCategorySafe("fruit", 1)
                            ?? CreateForestFruit();
                    }
                    else if (idLow == "fruit" || idLow == "apple" || idLow == "grape"
                        || idLow == "banana" || idLow == "palulu" || idLow == "orange" || idLow == "peach")
                    {
                        t = TryCreate(id, 1);
                        if (t != null)
                        {
                            KeepFoodFresh(t);
                        }
                        else
                        {
                            t = CreateForestFruit();
                        }
                    }
                    else if (idLow == "flower" || idLow.StartsWith("flower_"))
                    {
                        // Prefer exact planned id if it is a flower; else mission lock.
                        t = (idLow.StartsWith("flower_") ? TryCreate(id, 1) : null)
                            ?? CreatePlainFlower()
                            ?? TryCreate("flower", 1);
                    }
                    else if (idLow == "herb" || idLow.StartsWith("herb_"))
                    {
                        t = (idLow.StartsWith("herb_") ? TryCreate(id, 1) : null)
                            ?? CreatePlainHerb()
                            ?? TryCreate("herb", 1);
                    }
                    else if (idLow == "pasture")
                    {
                        t = TryCreate("pasture", 1) ?? TryCreate("grass", 1);
                    }
                    else if (idLow == "grass" || idLow == "weed")
                    {
                        t = TryCreate("grass", 1)
                            ?? TryCreate("weed", 1)
                            ?? TryCreate("pasture", 1)
                            ?? TryCreateFromCategorySafe("plant", 1);
                    }
                    else if (idLow == "egg" || idLow == "_egg" || idLow == "egg_fertilized")
                    {
                        t = CreateNamedEgg()
                            ?? TryCreate("_egg", 1)
                            ?? TryCreate("egg", 1);
                    }
                    else if (idLow == "gem" || idLow == "ore_gem")
                    {
                        t = CreateMountainGem();
                    }
                    else
                    {
                        t = TryCreate(id, 1) ?? CreateMaterialThing(id);
                    }
                }

                if (t != null && !string.IsNullOrEmpty(mat))
                {

                        if (mat == "fresh")
                        {
                            ForceFreshProduceMaterial(t);
                        }
                        else if (t.material == null
                            || !string.Equals(t.material.alias, mat, StringComparison.OrdinalIgnoreCase))
                        {
                            if (EClass.sources?.materials?.alias != null
                                && EClass.sources.materials.alias.ContainsKey(mat))
                            {
                                t.ChangeMaterial(mat, ignoreFixedMaterial: true);
                            }
                        }

}
            }

            // Fallback only when id create failed (old saves / missing cards).
            if (t == null && !string.IsNullOrEmpty(mat))
            {
                if (mat.Equals("sand", StringComparison.OrdinalIgnoreCase) || mat.Contains("sand"))
                {
                    t = CreateBeachSand();
                }
                else if (mat.Equals("salt", StringComparison.OrdinalIgnoreCase))
                {
                    t = CreateBeachSalt();
                }
                else if (mat.Equals("sulfur", StringComparison.OrdinalIgnoreCase)
                    || mat.Equals("sulphur", StringComparison.OrdinalIgnoreCase))
                {
                    t = CreateMountainSulfur();
                }
                else if (mat.Equals("plastic", StringComparison.OrdinalIgnoreCase))
                {
                    t = CreatePlasticOre();
                }
                else
                {
                    t = TryCreateRawByMatAlias(mat);
                }
            }

            if (t == null)
            {
                return null;
            }

            // Restore named-meat ref if still missing.
            if (!string.IsNullOrEmpty(refCard))
            {
                try
                {
                    string cur = t.c_idRefCard ?? "";
                    if (string.IsNullOrEmpty(cur))
                    {
                        try { t.MakeFoodFrom(refCard); }
                        catch
                        {
                             t.MakeRefFrom(refCard);
                        }
                    }
                }
                catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchRewards.cs silent catch: " + __e.Message); }
}

             KeepFoodFresh(t);

            // v11: re-pin planned material; ore cards often ignore Create(id, mat).
            try
            {
                string pinMat = entry.matAlias ?? "";
                if (!string.IsNullOrEmpty(pinMat) && pinMat != "fresh")
                {
                    string resolved = ResolveMaterialAlias(pinMat);
                    if (!string.IsNullOrEmpty(resolved)
                        && EClass.sources?.materials?.alias != null
                        && EClass.sources.materials.alias.ContainsKey(resolved))
                    {
                        ForcePinnedMaterial(t, resolved);
                    }
                }

                // Beach sand plan lines must resolve to 海沙.
                string idCheck = (entry.id ?? "").ToLowerInvariant();
                string matCheck = (entry.matAlias ?? "").ToLowerInvariant();
                string nameCheck = (entry.name ?? "").ToLowerInvariant();
                bool sandLine = matCheck.Contains("sand") || idCheck.Contains("sand")
                    || nameCheck.Contains("沙") || nameCheck.Contains("sand")
                    || IsSeaSandMaterialAlias(matCheck);
                if (sandLine && !IsSeaSandThing(t))
                {
                    int keepN = 1;
                    try { keepN = Math.Max(1, t.Num); } catch { keepN = 1; }
                     if (t.parent == null) t.Destroy();
                    t = CreateBeachSand();
                    if (t == null)
                    {
                        return null;
                    }

                     t.SetNum(keepN);
                }

                // Mountain/common ores: never deliver gold-family when plan said copper/iron/etc.
                if (IsGoldLikeThing(t) || IsForbiddenHighOreMaterial(LiveMaterialAlias(t)))
                {
                    string wantOre = ResolveMaterialAlias(string.IsNullOrEmpty(entry.matAlias) ? "copper" : entry.matAlias);
                    if (IsForbiddenHighOreMaterial(wantOre) || string.IsNullOrEmpty(wantOre))
                    {
                        wantOre = "copper";
                    }

                    int keepN = 1;
                    try { keepN = Math.Max(1, t.Num); } catch { keepN = 1; }
                     if (t.parent == null) t.Destroy();
                    t = CreateMetalHard(wantOre)
                        ?? CreateMaterialThing(wantOre)
                        ?? CreateMetalHard("copper")
                        ?? CreateMaterialThing("copper")
                        ?? CreateWeightedOre(common: true);
                    if (t == null)
                    {
                        return null;
                    }

                     t.SetNum(keepN);
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchRewards.cs silent catch: " + __e.Message); }
if (t == null)
            {
                return null;
            }

            // Match RefreshRegionProgressLog exactly: floor(plan * harvestProgress).
            float s = Mathf.Clamp01(scale);
            int planned = Math.Max(1, entry.num);
            int n = Mathf.FloorToInt(planned * s + 0.0001f);
            if (n <= 0)
            {
                 if (t.parent == null) t.Destroy();
                return null;
            }

             t.SetNum(n);
             KeepFoodFresh(t);
            return t;
        }
        catch
        {
            return null;
        }
    }


    /// <summary>Migrates already-locked region plans from older builds.</summary>
    static void RepairRegionPlanEntries(DungeonDispatchMission mission)
        => RepairBeachPlanEntries(mission);

    static void RepairBeachPlanEntries(DungeonDispatchMission mission)
    {
        if (mission?.plannedLootEntries == null || mission.plannedLootEntries.Count == 0)
        {
            return;
        }

        try
        {
            int mudQty = 0;
            int sandQty = 0;
            int saltQty = 0;
            PlannedLootEntry? sandEntry = null;
            PlannedLootEntry? saltEntry = null;

            for (int i = mission.plannedLootEntries.Count - 1; i >= 0; i--)
            {
                PlannedLootEntry e = mission.plannedLootEntries[i];
                if (e == null)
                {
                    mission.plannedLootEntries.RemoveAt(i);
                    continue;
                }

                string id = (e.id ?? "").ToLowerInvariant();
                string mat = (e.matAlias ?? "").ToLowerInvariant();
                string name = (e.name ?? "").ToLowerInvariant();
                int n = Math.Max(1, e.num);

                bool isSand = mat.Contains("sand") || id.Contains("sand") || name.Contains("沙") || name.Contains("sand");
                bool isSalt = mat == "salt" || id.Contains("salt") || name.Contains("盐") || name.Contains("salt");
                bool isMud = (!isSand && !isSalt) && (
                    mat == "mud" || mat.Contains("mud") || mat == "soil" || mat.Contains("silt")
                    || id.Contains("mud") || id.Contains("soil") || id.Contains("dirt") || id.Contains("clay")
                    || name == "泥" || name.Contains("泥") || name.Contains("土"));

                if (isMud)
                {
                    mudQty += n;
                    mission.plannedLootEntries.RemoveAt(i);
                    continue;
                }

                if (isSand)
                {
                    sandQty += n;
                    // Force beach sand plan lines onto runtime sea-sand alias.
                    if (string.Equals(mission.regionKind, "beach", StringComparison.OrdinalIgnoreCase))
                    {
                        string live = ResolveMaterialAlias("sand_sea");
                        if (string.IsNullOrEmpty(live))
                        {
                            live = "sand_sea";
                        }

                        e.matAlias = live;
                        if (string.IsNullOrEmpty(e.id) || e.id == "scrap" || e.id == "sand")
                        {
                            e.id = "chunk";
                        }

                        if (string.IsNullOrEmpty(e.name) || e.name == "sand" || e.name == "沙")
                        {
                            e.name = "sea sand";
                        }
                    }

                    sandEntry = e;
                    continue;
                }

                if (isSalt)
                {
                    saltQty += n;
                    saltEntry = e;
                }
            }

            // Beach: scrap carriers from older sand creates become sand qty.
            if (string.Equals(mission.regionKind, "beach", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = mission.plannedLootEntries.Count - 1; i >= 0; i--)
                {
                    PlannedLootEntry e = mission.plannedLootEntries[i];
                    if (e == null)
                    {
                        mission.plannedLootEntries.RemoveAt(i);
                        continue;
                    }

                    string idS = (e.id ?? "").ToLowerInvariant();
                    string nameS = (e.name ?? "").ToLowerInvariant();
                    string matS = (e.matAlias ?? "").ToLowerInvariant();
                    bool scrap = idS == "scrap" || idS.Contains("scrap")
                        || nameS.Contains("废铁") || nameS.Contains("scrap");
                    bool alreadySand = matS.Contains("sand") || idS.Contains("sand") || nameS.Contains("沙");
                    if (scrap && !alreadySand)
                    {
                        mudQty += Math.Max(1, e.num);
                        mission.plannedLootEntries.RemoveAt(i);
                    }
                }
            }

            if (mudQty > 0)
            {
                if (sandEntry != null)
                {
                    sandEntry.num = Math.Max(1, sandEntry.num + mudQty);
                    if (string.IsNullOrEmpty(sandEntry.matAlias)
                        || sandEntry.matAlias == "sand")
                    {
                        sandEntry.matAlias = ResolveMaterialAlias("sand_sea");
                        if (string.IsNullOrEmpty(sandEntry.matAlias))
                        {
                            sandEntry.matAlias = "sand_sea";
                        }
                    }

                    if (string.IsNullOrEmpty(sandEntry.name) || sandEntry.name == "泥")
                    {
                        sandEntry.name = "沙";
                    }
                }
                else
                {
                    // Build a real sand entry from live create so id/name are correct.
                    Thing? s = CreateBeachSand();
                    var e = new PlannedLootEntry();
                    if (s != null)
                    {
                        e.id = s.id ?? "";
                        try { e.matAlias = s.material != null ? (s.material.alias ?? "sand_sea") : "sand_sea"; } catch { e.matAlias = "sand_sea"; e.matId = SeaSandMaterialId(); }
                        try { e.name = s.NameOne ?? s.NameSimple ?? s.Name ?? "沙"; } catch { e.name = "沙"; }
                         if (s.parent == null) s.Destroy();
                    }
                    else
                    {
                        // Salt-style hard plan line even if live create failed this frame.
                        e.id = "chunk";
                        e.matAlias = "sand_sea"; e.matId = SeaSandMaterialId();
                        e.name = "海沙";
                    }

                    e.num = mudQty;
                    mission.plannedLootEntries.Insert(0, e);
                    sandQty += mudQty;
                }
            }

            if (saltQty <= 0
                && string.Equals(mission.regionKind, "beach", StringComparison.OrdinalIgnoreCase))
            {
                Thing? sal = CreateBeachSalt();
                var e = new PlannedLootEntry();
                if (sal != null)
                {
                    e.id = sal.id ?? "";
                    try { e.matAlias = sal.material != null ? (sal.material.alias ?? "salt") : "salt"; } catch { e.matAlias = "salt"; }
                    try { e.name = sal.NameOne ?? sal.NameSimple ?? sal.Name ?? "?"; } catch { e.name = "?"; }
                     if (sal.parent == null) sal.Destroy();
                }
                else
                {
                    e.id = "scrap";
                    e.matAlias = "salt";
                    e.name = "?";
                }

                int target = PlannedHaulTotal(mission);
                e.num = Math.Max(2, Math.Min(24, target / 8));
                mission.plannedLootEntries.Insert(Math.Min(1, mission.plannedLootEntries.Count), e);
            }

            // Non-beach: drop residual mud/soil plan lines without converting to sand.
            string kind = DungeonDispatchTargets.NormalizeRegionKind(mission.regionKind);
            if (kind != "beach")
            {
                for (int i = mission.plannedLootEntries.Count - 1; i >= 0; i--)
                {
                    PlannedLootEntry e = mission.plannedLootEntries[i];
                    if (e == null)
                    {
                        mission.plannedLootEntries.RemoveAt(i);
                        continue;
                    }

                    string id2 = (e.id ?? "").ToLowerInvariant();
                    string mat2 = (e.matAlias ?? "").ToLowerInvariant();
                    string name2 = (e.name ?? "").ToLowerInvariant();
                    if (IsMudLikeMaterial(mat2, id2, name2)
                        || mat2 == "soil" || mat2 == "mud"
                        || id2.Contains("soil") || id2.Contains("mud")
                        || id2.Contains("dirt") || id2.Contains("clay"))
                    {
                        mission.plannedLootEntries.RemoveAt(i);
                    }
                }
            }

            // Mountain: rebalance gold-heavy locked ore stacks toward copper/iron.
            if (kind == "mountain")
            {
                int goldQty = 0;
                for (int i = mission.plannedLootEntries.Count - 1; i >= 0; i--)
                {
                    PlannedLootEntry e = mission.plannedLootEntries[i];
                    if (e == null)
                    {
                        continue;
                    }

                    string mat2 = (e.matAlias ?? "").ToLowerInvariant();
                    string id2 = (e.id ?? "").ToLowerInvariant();
                    string nameG = (e.name ?? "").ToLowerInvariant();
                    bool isGold = mat2 == "gold" || mat2 == "platinum" || mat2 == "adamantite"
                        || (id2 == "ore" && (mat2 == "gold" || mat2 == "platinum" || mat2 == "adamantite" || string.IsNullOrEmpty(mat2)))
                        || nameG.Contains("金的") || nameG.Contains("gold");
                    if (nameG.Contains("copper") || nameG.Contains("铜")
                        || ((nameG.Contains("iron") || nameG.Contains("铁")) && !nameG.Contains("废铁") && !nameG.Contains("金")))
                    {
                        isGold = false;
                    }

                    if (!isGold)
                    {
                        continue;
                    }

                    goldQty += Math.Max(1, e.num);
                    mission.plannedLootEntries.RemoveAt(i);
                }

                if (goldQty > 0)
                {
                    int copperN = Math.Max(1, (goldQty * 2) / 3);
                    int ironN = Math.Max(1, goldQty - copperN);
                    void AddOre(string alias, int n)
                    {
                        Thing? ore = CreateMaterialThing(alias);
                        var e = new PlannedLootEntry();
                        if (ore != null)
                        {
                            e.id = ore.id ?? "ore";
                            try { e.matAlias = ore.material != null ? (ore.material.alias ?? alias) : alias; } catch { e.matAlias = alias; }
                            try { e.name = ore.NameOne ?? ore.NameSimple ?? ore.Name ?? alias; } catch { e.name = alias; }
                             if (ore.parent == null) ore.Destroy();
                        }
                        else
                        {
                            e.id = "ore";
                            e.matAlias = alias;
                            e.name = alias;
                        }

                        e.num = Math.Max(1, n);
                        mission.plannedLootEntries.Insert(0, e);
                    }

                    AddOre("copper", copperN);
                    AddOre("iron", ironN);
                }
            }

            // Non-beach: strip salt plan lines entirely (never convert to beach salt).
            if (kind != "beach")
            {
                for (int i = mission.plannedLootEntries.Count - 1; i >= 0; i--)
                {
                    PlannedLootEntry e = mission.plannedLootEntries[i];
                    if (e == null)
                    {
                        mission.plannedLootEntries.RemoveAt(i);
                        continue;
                    }

                    string idS = (e.id ?? "").ToLowerInvariant();
                    string matS = (e.matAlias ?? "").ToLowerInvariant();
                    string nameS = (e.name ?? "").ToLowerInvariant();
                    bool salt = matS == "salt" || idS.Contains("salt")
                        || nameS.Contains("\u76d0") || nameS.Contains("salt");
                    if (salt)
                    {
                        mission.plannedLootEntries.RemoveAt(i);
                    }
                }
            }

            // Beach: strip gold plan lines and ensure sand.
            if (kind == "beach")
            {
                for (int i = mission.plannedLootEntries.Count - 1; i >= 0; i--)
                {
                    PlannedLootEntry e = mission.plannedLootEntries[i];
                    if (e == null)
                    {
                        mission.plannedLootEntries.RemoveAt(i);
                        continue;
                    }

                    string matG = (e.matAlias ?? "").ToLowerInvariant();
                    string idG = (e.id ?? "").ToLowerInvariant();
                    string nameG = (e.name ?? "").ToLowerInvariant();
                    if (matG == "gold" || matG == "platinum" || matG == "adamantite"
                        || nameG.Contains("gold") || (idG == "ore" && matG == "gold"))
                    {
                        mission.plannedLootEntries.RemoveAt(i);
                    }
                }

                if (sandQty <= 0 && sandEntry == null)
                {
                    Thing? s = CreateBeachSand();
                    var e = new PlannedLootEntry();
                    if (s != null)
                    {
                        e.id = s.id ?? "rock";
                        try { e.matAlias = s.material != null ? (s.material.alias ?? "sand_sea") : "sand_sea"; } catch { e.matAlias = "sand_sea"; e.matId = SeaSandMaterialId(); }
                        try { e.name = s.NameOne ?? s.NameSimple ?? s.Name ?? "sand"; } catch { e.name = "sand"; }
                         if (s.parent == null) s.Destroy();
                    }
                    else
                    {
                        e.id = "chunk";
                        e.matAlias = "sand_sea"; e.matId = SeaSandMaterialId();
                        e.name = "海沙";
                    }

                    e.num = Math.Max(4, PlannedHaulTotal(mission) / 3);
                    mission.plannedLootEntries.Insert(0, e);
                }
            }

            // Mountain: force copper/iron/plastic/sulfur/gem presence; ban residual gold.
            if (kind == "mountain")
            {
                bool hasCopper = false, hasIron = false, hasPlastic = false, hasSulfur = false, hasGem = false;
                for (int i = mission.plannedLootEntries.Count - 1; i >= 0; i--)
                {
                    PlannedLootEntry e = mission.plannedLootEntries[i];
                    if (e == null)
                    {
                        mission.plannedLootEntries.RemoveAt(i);
                        continue;
                    }

                    string mat2 = (e.matAlias ?? "").ToLowerInvariant();
                    string id2 = (e.id ?? "").ToLowerInvariant();
                    string name2 = (e.name ?? "").ToLowerInvariant();
                    if (mat2 == "gold" || mat2 == "platinum" || mat2 == "adamantite"
                        || name2.Contains("gold")
                        || (id2 == "ore" && mat2 == "gold"))
                    {
                        e.id = "ore";
                        e.matAlias = "copper";
                        e.name = "copper";
                        hasCopper = true;
                        continue;
                    }

                    if (mat2 == "copper") hasCopper = true;
                    if (mat2 == "iron") hasIron = true;
                    if (mat2.Contains("plast") || id2.Contains("plast") || id2 == "plastic") hasPlastic = true;
                    if (id2.Contains("sulf") || mat2.Contains("sulf") || id2 == "sulfur" || id2 == "sulphur"
                        || name2.Contains("sulf") || name2.Contains("硫黄") || name2.Contains("硫磺") || name2.Contains("硫")) hasSulfur = true;
                    if (id2.Contains("gem") || id2 == "ore_gem" || IsGemAlias(mat2)) hasGem = true;
                }

                void EnsureOre(string alias, string idHint)
                {
                    Thing? ore = alias == "plastic" ? CreatePlasticOre()
                        : alias == "sulfur" ? CreateMountainSulfur()
                        : alias == "gem" ? CreateMountainGem()
                        : CreateMaterialThing(alias);
                    var e = new PlannedLootEntry();
                    if (ore != null)
                    {
                        e.id = ore.id ?? idHint;
                        try { e.matAlias = ore.material != null ? (ore.material.alias ?? alias) : alias; } catch { e.matAlias = alias; }
                        try { e.name = ore.NameOne ?? ore.NameSimple ?? ore.Name ?? alias; } catch { e.name = alias; }
                         if (ore.parent == null) ore.Destroy();
                    }
                    else
                    {
                        e.id = idHint;
                        e.matAlias = alias == "gem" ? "crystal" : alias;
                        e.name = alias;
                    }

                    e.num = Math.Max(2, PlannedHaulTotal(mission) / 12);
                    mission.plannedLootEntries.Insert(0, e);
                }

                if (!hasCopper) EnsureOre("copper", "ore");
                if (!hasIron) EnsureOre("iron", "ore");
                if (!hasPlastic) EnsureOre("plastic", "plastic");
                if (!hasSulfur) EnsureOre("sulfur", "sulfur");
                if (!hasGem) EnsureOre("gem", "ore_gem");
            }

            // Plain: strip salt; rewrite anonymous meat plan lines to named meats.
            if (kind == "plain" || kind == "field" || kind == "")
            {
                int anonMeat = 0;
                for (int i = mission.plannedLootEntries.Count - 1; i >= 0; i--)
                {
                    PlannedLootEntry e = mission.plannedLootEntries[i];
                    if (e == null)
                    {
                        mission.plannedLootEntries.RemoveAt(i);
                        continue;
                    }

                    string id2 = (e.id ?? "").ToLowerInvariant();
                    string mat2 = (e.matAlias ?? "").ToLowerInvariant();
                    string name2 = e.name ?? "";
                    if (mat2 == "salt" || id2.Contains("salt"))
                    {
                        mission.plannedLootEntries.RemoveAt(i);
                        continue;
                    }

                    bool meatLine = id2.StartsWith("meat:") || id2 == "_meat" || id2 == "meat"
                        || id2 == "meat_marble" || id2.Contains("meat");
                    if (!meatLine)
                    {
                        continue;
                    }

                    if (id2.StartsWith("meat:") && !string.IsNullOrEmpty(e.refCard))
                    {
                        continue;
                    }

                    bool anon = string.IsNullOrEmpty(e.refCard)
                        && (string.IsNullOrEmpty(name2)
                            || name2.Contains("\u66fe\u4e3a\u751f\u547d")
                            || !id2.StartsWith("meat:"));
                    if (anon)
                    {
                        anonMeat += Math.Max(1, e.num);
                        mission.plannedLootEntries.RemoveAt(i);
                    }
                }

                if (anonMeat > 0)
                {
                    string[] sp = (_missionMeatSpecies != null && _missionMeatSpecies.Length > 0)
                        ? _missionMeatSpecies
                        : PlainMeatSpeciesIds;
                    if (sp == null || sp.Length == 0)
                    {
                        sp = new[] { "chicken", "sheep", "cow", "putty" };
                    }
                    if (sp.Length > 4)
                    {
                        var clipped = new string[4];
                        for (int ci = 0; ci < 4; ci++) clipped[ci] = sp[ci];
                        sp = clipped;
                    }

                                        int each = Math.Max(1, anonMeat / sp.Length);
                    for (int si = 0; si < sp.Length; si++)
                    {
                        var e = new PlannedLootEntry();
                        e.id = "meat:" + sp[si];
                        e.refCard = sp[si];
                        e.matAlias = "meat";
                        e.name = sp[si];
                        e.num = each + (si < anonMeat % sp.Length ? 1 : 0);
                        mission.plannedLootEntries.Insert(0, e);
                    }
                }
            }

            // Forest: strip salt/gold/rubber plan lines.
            if (kind == "forest")
            {
                for (int i = mission.plannedLootEntries.Count - 1; i >= 0; i--)
                {
                    PlannedLootEntry e = mission.plannedLootEntries[i];
                    if (e == null)
                    {
                        mission.plannedLootEntries.RemoveAt(i);
                        continue;
                    }

                    string id2 = (e.id ?? "").ToLowerInvariant();
                    string mat2 = (e.matAlias ?? "").ToLowerInvariant();
                    string name2 = (e.name ?? "").ToLowerInvariant();
                    if (mat2 == "salt" || id2.Contains("salt")
                        || mat2 == "gold" || mat2 == "platinum"
                        || id2.Contains("rubber") || id2.Contains("duck")
                        || name2.Contains("rubber") || name2.Contains("duck"))
                    {
                        mission.plannedLootEntries.RemoveAt(i);
                    }
                }
            }

            // Plain/forest: collapse flower & herb plan lines to at most 4 kinds.
            if (kind == "plain" || kind == "forest")
            {
                CollapsePlanIdKinds(mission, "flower", maxKinds: 4);
                CollapsePlanIdKinds(mission, "herb", maxKinds: 4);
            }

            // Plain: meat kinds 2-4; rewrite forbidden eggs.
            // Qty half is applied on full rebuild (CollectRegionThings rebalance), not re-halved here.
            if (kind == "plain")
            {
                CollapsePlanMeatKinds(mission, maxKinds: 4);
                SanitizePlanEggs(mission);
            }

            // Beach: force 帕露露 into the locked plan (fixed coastal fruit).
            if (kind == "beach")
            {
                int paluluQty = 0;
                PlannedLootEntry? paluluEntry = null;
                for (int i = 0; i < mission.plannedLootEntries.Count; i++)
                {
                    PlannedLootEntry e = mission.plannedLootEntries[i];
                    if (e == null)
                    {
                        continue;
                    }

                    string idP = (e.id ?? "").ToLowerInvariant();
                    string nameP = (e.name ?? "");
                    if (idP == "palulu" || idP == "790" || nameP.Contains("帕露露") || nameP.ToLowerInvariant().Contains("palulu"))
                    {
                        paluluQty += Math.Max(1, e.num);
                        paluluEntry = e;
                    }
                }

                int wantPalulu = Math.Max(2, paluluQty > 0 ? paluluQty : 2);
                if (paluluQty < wantPalulu)
                {
                    int need = wantPalulu - paluluQty;
                    if (paluluEntry != null)
                    {
                        paluluEntry.id = "palulu";
                        paluluEntry.num = Math.Max(1, paluluEntry.num + need);
                        if (string.IsNullOrEmpty(paluluEntry.name))
                        {
                            paluluEntry.name = "帕露露";
                        }
                    }
                    else
                    {
                        Thing? pal = CreateBeachPaluluFruit() ?? TryCreate("palulu", 1) ?? TryCreate("790", 1);
                        var e = new PlannedLootEntry();
                        if (pal != null)
                        {
                            e.id = pal.id ?? "palulu";
                            try { e.name = pal.NameOne ?? pal.NameSimple ?? pal.Name ?? "帕露露"; } catch { e.name = "帕露露"; }
                            if (pal.parent == null) pal.Destroy();
                        }
                        else
                        {
                            e.id = "palulu";
                            e.name = "帕露露";
                        }

                        e.num = need;
                        mission.plannedLootEntries.Add(e);
                    }
                }
                else if (paluluEntry != null)
                {
                    paluluEntry.id = "palulu";
                    if (string.IsNullOrEmpty(paluluEntry.name))
                    {
                        paluluEntry.name = "帕露露";
                    }
                }
            }

            // Mountain: force sulfur qty roughly equal to stone/rock qty.
            if (kind == "mountain")
            {
                int stoneQty = 0;
                int sulfurQty = 0;
                PlannedLootEntry? sulfurEntry = null;
                for (int i = 0; i < mission.plannedLootEntries.Count; i++)
                {
                    PlannedLootEntry e = mission.plannedLootEntries[i];
                    if (e == null)
                    {
                        continue;
                    }

                    string id2 = (e.id ?? "").ToLowerInvariant();
                    string mat2 = (e.matAlias ?? "").ToLowerInvariant();
                    string name2 = e.name ?? "";
                    int n = Math.Max(1, e.num);
                    bool isSulfur = id2 == "sulfur" || id2 == "sulphur" || mat2.Contains("sulf")
                        || name2.Contains("硫黄") || name2.Contains("硫磺") || name2.Contains("硫");
                    if (isSulfur)
                    {
                        sulfurQty += n;
                        sulfurEntry = e;
                        continue;
                    }

                    bool isStone = id2 == "stone" || id2 == "rock" || mat2 == "stone" || mat2 == "rock"
                        || name2.Contains("石头") || name2.Contains("岩石");
                    if (isStone && !id2.Contains("ore") && !mat2.Contains("copper") && !mat2.Contains("iron"))
                    {
                        stoneQty += n;
                    }
                }

                int wantS = Math.Max(stoneQty, 1);
                if (sulfurQty < wantS)
                {
                    int need = wantS - sulfurQty;
                    if (sulfurEntry != null)
                    {
                        sulfurEntry.num = Math.Max(1, sulfurEntry.num + need);
                    }
                    else
                    {
                        Thing? sul = CreateMountainSulfur() ?? TryCreate("sulfur", 1);
                        var e = new PlannedLootEntry();
                        if (sul != null)
                        {
                            e.id = sul.id ?? "sulfur";
                            try { e.matAlias = sul.material != null ? (sul.material.alias ?? "sulfur") : "sulfur"; } catch { e.matAlias = "sulfur"; }
                            try { e.name = sul.NameOne ?? sul.NameSimple ?? sul.Name ?? "硫磺"; } catch { e.name = "硫磺"; }
                            if (sul.parent == null) sul.Destroy();
                        }
                        else
                        {
                            e.id = "sulfur";
                            e.matAlias = "sulfur";
                            e.name = "硫磺";
                        }

                        e.num = need;
                        mission.plannedLootEntries.Insert(0, e);
                    }
                }
            }

            int total = 0;
            for (int i = 0; i < mission.plannedLootEntries.Count; i++)
            {
                try { total += Math.Max(1, mission.plannedLootEntries[i].num); } catch { total++; }
            }

            mission.plannedHaulTotal = total;
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("repair beach plan: " + ex.Message);
        }
    }


    /// <summary>
    /// Final mail-time contract. Ignores fragile mid-plan state and guarantees the
    /// player-facing staples that v11/v12 still lost to mud-false-positives / gold ore.
    /// </summary>
    static void EnforceRegionDeliverContract(DungeonDispatchMission mission, List<Thing> things, float scale)
    {
        if (mission == null || things == null || !mission.isRegion)
        {
            return;
        }

        string kind = DungeonDispatchTargets.NormalizeRegionKind(mission.regionKind);
        // Staple contract is material/explore-side only; food/lockpick are append-only.
        int target = Math.Max(1, PlannedExploreHaulTotal(mission));
        int sN = Mathf.Max(1, Mathf.RoundToInt(Mathf.Clamp01(scale) * target));

        try
        {
            if (kind == "beach")
            {
                // Salt-style mandatory: always inject 海沙 via runtime sand_sea material id.
                int seaId = SeaSandMaterialId();
                int sand = 0;
                int salt = 0;
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    Thing t = things[i];
                    if (t == null)
                    {
                        things.RemoveAt(i);
                        continue;
                    }

                    int n = 1;
                    try { n = Math.Max(1, t.Num); } catch { n = 1; }

                    if (IsSeaSandThing(t) || (seaId > 0 && LiveMaterialId(t) == seaId))
                    {
                        sand += n;
                        continue;
                    }

                    if (IsSandLikeThing(t) || IsMudLikeThing(t) || IsGoldLikeThing(t) || IsScrapLikeThing(t))
                    {
                        sand += n;
                         if (t.parent == null) t.Destroy();
                        things.RemoveAt(i);
                        continue;
                    }

                    if (IsSaltLikeThing(t))
                    {
                        salt += n;
                    }
                }

                if (sand <= 0)
                {
                    int qty = Math.Max(4, sN / 2);
                    Thing? s = CreateBeachSand()
                        ?? (seaId > 0 ? CreateByMaterialId(seaId, preferredCarriers: new[] { "chunk", "rock", "pebble", "stone" }) : null);
                    if (s != null)
                    {
                         s.SetNum(Math.Max(1, qty));
                        things.Add(s);
                        Plugin.LogInfo("dispatch beach hard-sand matId=" + seaId + " qty=" + qty);
                    }
                    else
                    {
                        Plugin.LogWarn("dispatch beach hard-sand FAILED matId=" + seaId);
                    }
                }
                else
                {
                    int need = Math.Max(4, sN / 2);
                    if (sand < need)
                    {
                        int qty = need - sand;
                        Thing? s = CreateBeachSand()
                            ?? (seaId > 0 ? CreateByMaterialId(seaId, preferredCarriers: new[] { "chunk", "rock", "pebble", "stone" }) : null);
                        if (s != null)
                        {
                             s.SetNum(Math.Max(1, qty));
                            things.Add(s);
                            Plugin.LogInfo("dispatch beach topup-sand matId=" + seaId + " qty=" + qty);
                        }
                    }
                }

                if (salt <= 0)
                {
                    Thing? sal = CreateBeachSalt();
                    if (sal != null)
                    {
                        int sn = Math.Max(2, Math.Min(24, sN / 5));
                         sal.SetNum(sn);
                        things.Add(sal);
                    }
                }

                StripGoldLikeThings(things);
            }
            else if (kind == "mountain")
            {
                StripGoldLikeThings(things);
                // Mountain must never keep beach sea sand (material mis-create / fallback leak).
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    Thing t = things[i];
                    if (t == null)
                    {
                        things.RemoveAt(i);
                        continue;
                    }

                    if (IsSeaSandThing(t))
                    {
                        try { if (t.parent == null) t.Destroy(); } catch { }
                        things.RemoveAt(i);
                    }
                }

                int copper = 0, iron = 0, sulfur = 0;
                int copperId = CopperMaterialId();
                int ironId = IronMaterialId();
                int goldId = ResolveMaterialId("gold");
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    Thing t = things[i];
                    if (t == null)
                    {
                        things.RemoveAt(i);
                        continue;
                    }

                    int n = 1;
                    try { n = Math.Max(1, t.Num); } catch { n = 1; }
                    int mid = LiveMaterialId(t);
                    string mat = LiveMaterialAlias(t).ToLowerInvariant();
                    string id = "";
                    try { id = (t.id ?? "").ToLowerInvariant(); } catch { id = ""; }

                    if ((copperId > 0 && mid == copperId) || mat == "copper") copper += n;
                    else if ((ironId > 0 && mid == ironId) || mat == "iron") iron += n;
                    else if (IsSulfurLikeThing(t) || id.Contains("sulf") || mat.Contains("sulf") || mat.Contains("硫")) sulfur += n;
                    else if (IsGoldLikeThing(t) || (goldId > 0 && mid == goldId) || mat == "gold")
                    {
                        copper += n;
                         if (t.parent == null) t.Destroy();
                        things.RemoveAt(i);
                    }
                }

                void AddMetal(string alias, int qty)
                {
                    if (qty <= 0) return;
                    int matId = ResolveMaterialId(alias);
                    Thing? t = CreateMetalHard(alias)
                        ?? CreateMaterialThing(alias, new[] { "ore", "rock", "chunk", "cutstone" });
                    if (t == null) return;
                     t.SetNum(qty);
                    things.Add(t);
                    Plugin.LogInfo("dispatch mountain hard-" + alias + " matId=" + matId + " qty=" + qty);
                }

                if (copper <= 0) AddMetal("copper", Math.Max(3, sN / 5));
                if (iron <= 0) AddMetal("iron", Math.Max(2, sN / 8));

                // Count stone/rock so sulfur can match it 1:1.
                int stone = 0;
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    if (t == null)
                    {
                        continue;
                    }

                    string idS = "";
                    string matS = "";
                    try { idS = (t.id ?? "").ToLowerInvariant(); } catch { idS = ""; }
                    try { matS = t.material != null ? (t.material.alias ?? "").ToLowerInvariant() : ""; } catch { matS = ""; }
                    if (IsSulfurLikeThing(t) || idS.Contains("sulf"))
                    {
                        continue;
                    }

                    if (idS == "stone" || idS == "rock" || matS == "stone" || matS == "rock")
                    {
                        try { stone += Math.Max(1, t.Num); } catch { stone += 1; }
                    }
                }

                int wantSulfur = Math.Max(stone, Math.Max(2, sN / 10));
                if (sulfur < wantSulfur)
                {
                    int n = wantSulfur - sulfur;
                    Thing? sul = CreateMountainSulfur();
                    if (sul != null)
                    {
                         sul.SetNum(n);
                        things.Add(sul);
                        Plugin.LogInfo("dispatch mountain hard-sulfur id=" + (sul.id ?? "?")
                            + " name=" + (sul.NameOne ?? sul.NameSimple ?? sul.Name ?? "?")
                            + " qty=" + n + " stone=" + stone);
                    }
                    else
                    {
                        Plugin.LogWarn("dispatch mountain hard-sulfur FAILED");
                    }
                }
            }

            // Non-beach regions: drop any sea-sand that slipped through create fallbacks.
            if (kind != "beach")
            {
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    Thing t = things[i];
                    if (t == null)
                    {
                        things.RemoveAt(i);
                        continue;
                    }

                    if (IsSeaSandThing(t))
                    {
                        try { if (t.parent == null) t.Destroy(); } catch { }
                        things.RemoveAt(i);
                    }
                }
            }

            CompactStacks(things);
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("EnforceRegionDeliverContract: " + ex.Message);
        }
    }


    static void ApplyRegionStack(Thing t, int weeks, DungeonDispatchMission mission, bool basic)
    {
        if (t == null)
        {
            return;
        }

        try
        {
            int w = Mathf.Clamp(weeks, 1, 4);
            int members = mission != null ? Math.Max(1, mission.MemberCount) : 1;
            int target = PlannedHaulTotal(mission);
            int n;
            bool staple = IsRegionStapleThing(t);
            if (basic)
            {
                // Specialty/scanned stacks: small slices of the locked total.
                float slice = staple ? 0.08f : 0.035f;
                float baseN = 1f + target * slice + members * 0.15f + EClass.rnd(3) * 0.25f;
                n = Mathf.RoundToInt(baseN);
                int max = staple ? Math.Max(8, target / 4) : Math.Max(4, target / 10);
                n = Mathf.Clamp(n, 1, max);
            }
            else
            {
                n = Mathf.RoundToInt(1f + target * 0.01f + (EClass.rnd(100) < 30 ? 1 : 0));
                n = Mathf.Clamp(n, 1, Math.Max(3, target / 20));
            }


                t.SetNum(n);

}
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchRewards.cs silent catch: " + __e.Message); }
}


    static bool IsCookedOrProcessedFood(Thing t)
    {
        if (t == null)
        {
            return true;
        }

        try
        {
            string id = (t.id ?? "").ToLowerInvariant();
            string nm = "";
            try { nm = (t.NameOne ?? t.NameSimple ?? t.Name ?? "").ToLowerInvariant(); } catch { nm = ""; }
            string cat = "";
            try { cat = t.category != null ? (t.category.id ?? "") : ""; } catch { cat = ""; }
            if (string.IsNullOrEmpty(cat))
            {
                try { cat = t.source != null ? (t.source.category ?? "") : ""; } catch { cat = ""; }
            }
            cat = (cat ?? "").ToLowerInvariant();

            // Bread / cooked dishes / dairy / alcohol are never wild region forage.
            if (id.Contains("bread") || id.Contains("cake") || id.Contains("pie")
                || id.Contains("noodle") || id.Contains("soup") || id.Contains("stew")
                || id.Contains("cheese") || id.Contains("butter") || id.Contains("wine")
                || id.Contains("ale") || id.Contains("beer") || id.Contains("meal")
                || id.Contains("cooked") || id.Contains("dish") || id.Contains("jerky")
                || id.Contains("sausage") || id.Contains("baked"))
            {
                return true;
            }

            if (nm.Contains("面包") || nm.Contains("bread") || nm.Contains("蛋糕")
                || nm.Contains("饼") || nm.Contains("汤") || nm.Contains("酒")
                || nm.Contains("奶酪") || nm.Contains("cheese") || nm.Contains("料理")
                || nm.Contains("熟") || nm.Contains("烤"))
            {
                return true;
            }

            if (cat.Contains("meal") || cat.Contains("dish") || cat.Contains("cooked")
                || cat.Contains("drink") || cat == "food_processed")
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }


    static void CollapsePlanIdKinds(DungeonDispatchMission mission, string kindKey, int maxKinds)
    {
        if (mission?.plannedLootEntries == null || mission.plannedLootEntries.Count == 0 || maxKinds <= 0)
        {
            return;
        }

        string key = (kindKey ?? "").ToLowerInvariant();
        var kindEntries = new List<PlannedLootEntry>();
        for (int i = 0; i < mission.plannedLootEntries.Count; i++)
        {
            PlannedLootEntry e = mission.plannedLootEntries[i];
            if (e == null)
            {
                continue;
            }

            string id = (e.id ?? "").ToLowerInvariant();
            string nm = (e.name ?? "").ToLowerInvariant();
            bool match = id.Contains(key) || nm.Contains(key);
            if (!match && key == "flower")
            {
                match = (e.name ?? "").Contains("花");
            }
            if (!match && key == "herb")
            {
                match = (e.name ?? "").Contains("药草") || (e.name ?? "").Contains("香草");
            }
            if (match)
            {
                kindEntries.Add(e);
            }
        }

        if (kindEntries.Count <= maxKinds)
        {
            return;
        }

        // Keep first maxKinds distinct ids; merge extras into them round-robin.
        var keep = new List<PlannedLootEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < kindEntries.Count; i++)
        {
            PlannedLootEntry e = kindEntries[i];
            string id = e.id ?? ("#" + i);
            if (seen.Count < maxKinds && seen.Add(id))
            {
                keep.Add(e);
            }
        }

        if (keep.Count == 0)
        {
            return;
        }

        int ki = 0;
        for (int i = 0; i < kindEntries.Count; i++)
        {
            PlannedLootEntry e = kindEntries[i];
            if (keep.Contains(e))
            {
                continue;
            }

            PlannedLootEntry dest = keep[ki % keep.Count];
            ki++;
            dest.num = Math.Max(1, dest.num) + Math.Max(1, e.num);
            mission.plannedLootEntries.Remove(e);
        }
    }

    static void CollapsePlanMeatKinds(DungeonDispatchMission mission, int maxKinds)
    {
        if (mission?.plannedLootEntries == null || mission.plannedLootEntries.Count == 0 || maxKinds <= 0)
        {
            return;
        }

        var kindEntries = new List<PlannedLootEntry>();
        for (int i = 0; i < mission.plannedLootEntries.Count; i++)
        {
            PlannedLootEntry e = mission.plannedLootEntries[i];
            if (e == null)
            {
                continue;
            }

            string id = (e.id ?? "").ToLowerInvariant();
            string nm = e.name ?? "";
            bool isMeat = id.StartsWith("meat:") || id == "meat" || id == "_meat" || id == "meat_marble"
                || id.Contains("meat") || nm.Contains("肉") || nm.ToLowerInvariant().Contains("meat");
            if (isMeat)
            {
                kindEntries.Add(e);
            }
        }

        if (kindEntries.Count <= maxKinds)
        {
            // Still normalize bare meat lines onto chicken if ref missing.
            for (int i = 0; i < kindEntries.Count; i++)
            {
                PlannedLootEntry e = kindEntries[i];
                string id = (e.id ?? "").ToLowerInvariant();
                if (!id.StartsWith("meat:") || string.IsNullOrEmpty(e.refCard))
                {
                    string sp = e.refCard;
                    if (string.IsNullOrEmpty(sp) && id.StartsWith("meat:") && id.Length > 5)
                    {
                        sp = id.Substring(5);
                    }
                    if (string.IsNullOrEmpty(sp) || !IsAllowedPlainMeatSpecies(sp))
                    {
                        sp = "chicken";
                    }
                    e.id = "meat:" + sp;
                    e.refCard = sp;
                    e.matAlias = "meat";
                }
            }
            return;
        }

        var keep = new List<PlannedLootEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < kindEntries.Count; i++)
        {
            PlannedLootEntry e = kindEntries[i];
            string key = e.refCard;
            if (string.IsNullOrEmpty(key))
            {
                string id = (e.id ?? "");
                key = id.StartsWith("meat:", StringComparison.OrdinalIgnoreCase) && id.Length > 5
                    ? id.Substring(5)
                    : (e.id ?? ("#" + i));
            }
            if (seen.Count < maxKinds && seen.Add(key))
            {
                keep.Add(e);
            }
        }

        if (keep.Count == 0)
        {
            return;
        }

        int ki = 0;
        for (int i = 0; i < kindEntries.Count; i++)
        {
            PlannedLootEntry e = kindEntries[i];
            if (keep.Contains(e))
            {
                continue;
            }

            PlannedLootEntry dest = keep[ki % keep.Count];
            ki++;
            dest.num = Math.Max(1, dest.num) + Math.Max(1, e.num);
            mission.plannedLootEntries.Remove(e);
        }
    }


    static void SanitizePlanEggs(DungeonDispatchMission mission)
    {
        if (mission?.plannedLootEntries == null || mission.plannedLootEntries.Count == 0)
        {
            return;
        }

        for (int i = mission.plannedLootEntries.Count - 1; i >= 0; i--)
        {
            PlannedLootEntry e = mission.plannedLootEntries[i];
            if (e == null)
            {
                mission.plannedLootEntries.RemoveAt(i);
                continue;
            }

            string id = (e.id ?? "").ToLowerInvariant();
            string nm = e.name ?? "";
            bool isEgg = id.StartsWith("egg:") || id == "egg" || id == "_egg" || id == "egg_fertilized"
                || id.Contains("egg") || nm.Contains("蛋") || nm.Contains("卵") || nm.ToLowerInvariant().Contains("egg");
            if (!isEgg)
            {
                continue;
            }

            string species = e.refCard ?? "";
            if (id.StartsWith("egg:") && id.Length > 4)
            {
                species = id.Substring(4);
            }

            bool bad = !IsAllowedPlainEggSpecies(species)
                || (e.matAlias ?? "").ToLowerInvariant().Contains("ash")
                || (e.matAlias ?? "").ToLowerInvariant().Contains("alchemy")
                || nm.Contains("炼金") || nm.Contains("炼金灰");
            if (!bad && !string.IsNullOrEmpty(species) && IsAllowedPlainEggSpecies(species))
            {
                // Still normalize id token.
                e.id = "egg:" + species.Trim();
                e.refCard = species.Trim();
                e.matAlias = "fresh";
                continue;
            }

            e.id = "egg:chicken";
            e.refCard = "chicken";
            e.matAlias = "fresh";
            if (string.IsNullOrEmpty(e.name) || bad)
            {
                e.name = "鸡蛋";
            }
        }
    }


    static void StripRegionJunk(List<Thing> things)
    {
        if (things == null || things.Count == 0)
        {
            return;
        }

        for (int i = things.Count - 1; i >= 0; i--)
        {
            Thing t = things[i];
            if (t == null)
            {
                things.RemoveAt(i);
                continue;
            }

            if (!IsForbiddenRegionThing(t) && !IsCookedOrProcessedFood(t))
            {
                continue;
            }


                if (t.parent == null)
                {
                    t.Destroy();
                }

things.RemoveAt(i);
        }
    }


    /// <summary>
    /// Soft-cap non-staple region stacks after merge (dye stripped; bone tightly capped).
    /// </summary>
    static void CapRegionOddStacks(List<Thing> things, DungeonDispatchMission? mission = null)
    {
        if (things == null)
        {
            return;
        }

        float haul = RegionHaulUnits(mission);

        for (int i = 0; i < things.Count; i++)
        {
            Thing t = things[i];
            if (t == null || IsRegionStapleThing(t))
            {
                continue;
            }

            try
            {
                int n = t.Num;
                int cap = Mathf.RoundToInt(4f + haul * 0.35f);

                    string id = (t.id ?? "").ToLowerInvariant();
                    string nm = (t.NameOne ?? t.Name ?? "").ToLowerInvariant();
                    if (IsBoneThing(t) || id.Contains("bone") || id.Contains("skull")
                        || nm.Contains("骨") || nm.Contains("bone"))
                    {
                        // Useful but never bone x39 again.
                        cap = Mathf.RoundToInt(2f + haul * 0.12f);
                        cap = Mathf.Clamp(cap, 2, 8);
                    }
                    else if (id.Contains("grass") || nm.Contains("草") || id.Contains("weed")
                        || id.Contains("flower") || nm.Contains("花"))
                    {
                        cap = Mathf.RoundToInt(3f + haul * 0.25f);
                    }
                    else if (id.Contains("fruit") || id.Contains("palulu") || nm.Contains("帕露露")
                        || id.Contains("berry") || id.Contains("cactus"))
                    {
                        cap = Mathf.RoundToInt(3f + haul * 0.3f);
                    }

cap = Mathf.Clamp(cap, 2, 24);
                if (n > cap)
                {
                    t.SetNum(cap);
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchRewards.cs silent catch: " + __e.Message); }
}
    }


    /// <summary>
    /// PC harvest uses the card default material (fresh). Force that after create so
    /// biome wood mats / bad Create idMat never leave fruit as oak/pine.
    /// </summary>
    static void ForceFreshProduceMaterial(Thing t)
    {
        if (t == null)
        {
            return;
        }

        try
        {
            if (EClass.sources?.materials?.alias != null
                && EClass.sources.materials.alias.ContainsKey("fresh"))
            {
                t.ChangeMaterial("fresh", ignoreFixedMaterial: true);
                return;
            }
        }
        catch
        {
        }

        try
        {
            SourceMaterial.Row? def = t.DefaultMaterial;
            if (def != null)
            {
                t.ChangeMaterial(def, ignoreFixedMaterial: true);
            }
        }
        catch
        {
        }
    }
}
