using System;
using System.Collections.Generic;
using UnityEngine;

namespace NpcLabor.Dispatch;

/// <summary>
/// Fixed 4-region item pools. Edit the id arrays below only.
/// Quantity still scales from explore/gather skill * weeks * variance.
/// Lockpick chests stay separate (not listed here) and dump their contents into the same parcel.
///
/// Kind notes for CreateManualThing:
///   plain id     -> ThingGen.Create(id) or dedicated helper
///   mat:alias    -> material chunk/ore via CreateMaterialThing / CreateMetalHard
///   wood:alias   -> wood log via CreateWoodLog
///   fish         -> ThingGen.Create("fish", -1, skillLv); optional weight filter
///   sand         -> beach sea sand
///   salt         -> beach salt
///   vine         -> forest vine helper
///   gem          -> mountain gem helper
///   mushroom/flower/egg/... dedicated helpers when plain create fails
/// </summary>
internal static partial class DungeonDispatchRewards
{
    // Bump so old plans rebuild under this table.
    internal const int ManualRegionPlanGenVersion = 36;

    // =====================================================================
    // EDIT THESE LISTS. Weight is relative share of that region's total qty.
    // =====================================================================

    // Beach: sea sand / salt / palm wood / multi-fish / palulu / seaweed / bait.
    static readonly ManualPoolEntry[] BeachExplore =
    {
        E("sand", 48),            // 海沙
        E("salt", 22),            // 盐
        E("wood:palm", 14),       // 帕露露木
        E("seaweed", 10),         // 海藻
        E("bait", 6),             // 鱼饵
    };

    static readonly ManualPoolEntry[] BeachFood =
    {
        E("fish", 78),            // multi fish; heavy fish dampened in DistributeFishPool
        E("palulu", 22),          // 帕露露
    };

    // Forest explore materials
    static readonly ManualPoolEntry[] ForestExplore =
    {
        E("wood:wood", 34),         // 普通原木
        E("vine", 34),              // 藤蔓 ≈ 木头
        E("wood:wood_birch", 12),   // 白桦原木
        E("flower", 10),            // 花
        E("resin", 5),              // 树脂/塑料矿
        E("branch", 3),             // 树枝
        E("bark", 2),               // 树皮
    };

    static readonly ManualPoolEntry[] ForestFood =
    {
        E("mushroom", 36),          // common mushrooms
        E("mushroom_rare", 6),      // low-rate rare / other mushrooms
        E("berry", 20),
        E("fruit", 12),
        E("apple", 10),
        E("grape", 8),
        E("flower", 8),
    };

    // Mountain explore materials
    static readonly ManualPoolEntry[] MountainExplore =
    {
        E("mat:stone", 30),     // 石头 (primary)
        E("sulfur", 30),        // 硫磺 ≈ 石头
        E("mat:copper", 14),    // 铜矿 (cut)
        E("mat:iron", 10),      // 铁矿 (cut)
        E("rock", 12),          // 岩石
        E("gem", 6),            // 宝石
    };

    static readonly ManualPoolEntry[] MountainFood =
    {
        E("mushroom", 60),
        E("berry", 40),
    };

    // Plain explore materials
    static readonly ManualPoolEntry[] PlainExplore =
    {
        E("pasture", 70),       // 牧草 x2 share vs old mix
        E("flower", 16),
        E("grass", 8),
        E("herb", 6),
    };

    static readonly ManualPoolEntry[] PlainFood =
    {
        E("meat", 34),          // 肉; kinds lock 2-4 + qty half in rebalance
        E("flower", 28),        // flower kinds lock 2-4 + qty half in rebalance
        E("egg", 22),
        E("herb", 16),
    };

    // =====================================================================
    // Runtime builder
    // =====================================================================

    sealed class ManualPoolEntry
    {
        public string Id = "";
        public int Weight = 1;
    }

    static ManualPoolEntry E(string id, int weight)
        => new ManualPoolEntry { Id = id, Weight = Math.Max(1, weight) };

    static void CollectRegionThingsManual(DungeonDispatchMission mission, List<Thing> things)
    {
        if (mission == null || things == null)
        {
            return;
        }

        EnsureHaulVariance(mission);
        int exploreTarget = PlannedExploreHaulTotal(mission);
        int foodTarget = PlannedGatherHaulTotal(mission);
        mission.plannedHaulTotal = exploreTarget;

        // Lock 2-4 flower/herb kinds for this whole mission so forest/plain don't spray every id.
        BeginMissionFlowerLock(mission);

        string kind = DungeonDispatchTargets.NormalizeRegionKind(mission.regionKind);
        ManualPoolEntry[] explore = kind switch
        {
            "beach" => BeachExplore,
            "forest" => ForestExplore,
            "mountain" => MountainExplore,
            _ => PlainExplore,
        };
        ManualPoolEntry[] food = kind switch
        {
            "beach" => BeachFood,
            "forest" => ForestFood,
            "mountain" => MountainFood,
            _ => PlainFood,
        };

        DistributePool(things, explore, exploreTarget, kind, mission);
        CompactStacks(things);

        // Top-up remainder onto first entry so total matches explore contract.
        int left = exploreTarget - CountThingNums(things);
        if (left > 0 && explore.Length > 0)
        {
            AddManualStack(things, CreateManualThing(explore[0].Id, kind), left);
            CompactStacks(things);
        }

        if (foodTarget > 0 && food.Length > 0)
        {
            // Mountain food stays intentionally light.
            int n = kind == "mountain" ? Math.Max(1, foodTarget / 2) : foodTarget;
            DistributePool(things, food, n, kind, mission);
            CompactStacks(things);
        }

        // Beach: 帕露露 is a fixed coastal fruit staple, independent of gather budget.
        if (kind == "beach")
        {
            EnsureBeachPalulu(things, foodTarget);
            CompactStacks(things);
        }

        // Mountain: sulfur should land roughly as common as stone.
        if (kind == "mountain")
        {
            EnsureMountainSulfurParity(things);
            CompactStacks(things);
        }

        // Collapse flower/herb diversity to the mission lock (2-4 kinds).
        // CreatePlain* alone is not enough when many unit rolls land on different ids
        // or when create falls back outside the lock.
        if (kind == "plain" || kind == "forest")
        {
            RebalanceEvenPlainFlowers(things);
            RebalanceEvenPlainHerbs(things);
            if (kind == "plain")
            {
                RebalanceEvenPlainMeats(things);
            }
            CompactStacks(things);
        }

        // Plain: never keep alchemy-ash / monster eggs in the final plan.
        if (kind == "plain")
        {
            SanitizePlainEggs(things);
            CompactStacks(things);
        }

        // Lockpick: open chests and dump contents into the same parcel (no chest body).
        CollectLockpickChestThings(mission, 1f, things);
        CompactStacks(things);
        mission.plannedHaulTotal = CountThingNums(things);
        EndMissionFlowerLock();
    }

    static void EnsureBeachPalulu(List<Thing> things, int foodTarget)
    {
        if (things == null)
        {
            return;
        }

        int have = CountThingId(things, "palulu");
        // Fixed coastal staple: always at least 2, scale gently with gather budget.
        int want = Math.Max(2, foodTarget > 0 ? Math.Max(2, foodTarget / 4) : 2);
        if (have >= want)
        {
            return;
        }

        int need = want - have;
        Thing? palulu = CreateBeachPaluluFruit();
        if (palulu == null)
        {
            Plugin.LogWarn("dispatch beach hard-palulu FAILED");
            return;
        }

        KeepFoodFresh(palulu);
        AddManualStack(things, palulu, need);
        Plugin.LogInfo("dispatch beach hard-palulu qty=" + need);
    }

    /// <summary>
    /// Create real palulu only. Never fall into CreateForestFruit (apple/grape lottery).
    /// </summary>
    static Thing? CreateBeachPaluluFruit()
    {
        string[] ids = { "palulu", "790" };
        for (int i = 0; i < ids.Length; i++)
        {
            Thing? t = null;
            try { t = TryCreate(ids[i], 1); } catch { t = null; }
            if (t == null)
            {
                continue;
            }

            string id = "";
            string nm = "";
            try { id = (t.id ?? "").ToLowerInvariant(); } catch { id = ""; }
            try { nm = t.NameOne ?? t.NameSimple ?? t.Name ?? ""; } catch { nm = ""; }
            bool ok = id == "palulu" || id == "790"
                || nm.Contains("帕露露")
                || nm.ToLowerInvariant().Contains("palulu");
            if (!ok)
            {
                try { if (t.parent == null && !t.isDestroyed) t.Destroy(); } catch { }
                continue;
            }

            KeepFoodFresh(t);
            return t;
        }

        return null;
    }

    static void SanitizePlainEggs(List<Thing> things)
    {
        if (things == null || things.Count == 0)
        {
            return;
        }

        int lost = 0;
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
            bool isEgg = id == "egg" || id == "_egg" || id == "egg_fertilized" || id.StartsWith("egg")
                || id.Contains("egg");
            if (!isEgg)
            {
                string nm = "";
                try { nm = t.NameOne ?? t.NameSimple ?? t.Name ?? ""; } catch { nm = ""; }
                isEgg = nm.Contains("蛋") || nm.Contains("卵") || nm.ToLowerInvariant().Contains("egg");
            }

            if (!isEgg)
            {
                continue;
            }

            if (!IsForbiddenPlainEgg(t))
            {
                continue;
            }

            int n = 1;
            try { n = Math.Max(1, t.Num); } catch { n = 1; }
            try { if (t.parent == null && !t.isDestroyed) t.Destroy(); } catch { }
            things.RemoveAt(i);

            Thing? clean = CreateNamedEgg("chicken");
            if (clean == null || IsForbiddenPlainEgg(clean))
            {
                try { if (clean != null && clean.parent == null && !clean.isDestroyed) clean.Destroy(); } catch { }
                lost += n;
                continue;
            }

            KeepFoodFresh(clean);
            AddManualStack(things, clean, n);
        }

        if (lost > 0)
        {
            Plugin.LogWarn("dispatch plain egg sanitize dropped qty=" + lost);
        }
    }

    static void EnsureMountainSulfurParity(List<Thing> things)
    {
        if (things == null || things.Count == 0)
        {
            return;
        }

        int stone = 0;
        int sulfur = 0;
        for (int i = 0; i < things.Count; i++)
        {
            Thing t = things[i];
            if (t == null)
            {
                continue;
            }

            int n = 1;
            try { n = Math.Max(1, t.Num); } catch { n = 1; }

            string id = "";
            string mat = "";
            string nm = "";
            try { id = (t.id ?? "").ToLowerInvariant(); } catch { id = ""; }
            try { mat = t.material != null ? (t.material.alias ?? "").ToLowerInvariant() : ""; } catch { mat = ""; }
            try { nm = t.NameOne ?? t.NameSimple ?? t.Name ?? ""; } catch { nm = ""; }

            if (IsSulfurLikeThing(t) || id == "sulfur" || id == "sulphur" || mat.Contains("sulf")
                || nm.Contains("硫黄") || nm.Contains("硫磺") || nm.Contains("硫"))
            {
                sulfur += n;
                continue;
            }

            if (id == "rock" || id == "stone" || mat == "stone" || mat == "rock"
                || nm.Contains("石头") || nm.Contains("岩石") || nm.Contains("stone") || nm.Contains("rock"))
            {
                // Skip ore/metal carriers already counted elsewhere.
                if (id.Contains("ore") || mat.Contains("copper") || mat.Contains("iron") || mat.Contains("gold"))
                {
                    continue;
                }

                stone += n;
            }
        }

        int want = Math.Max(stone, 1);
        if (sulfur >= want)
        {
            return;
        }

        int need = want - sulfur;
        Thing? sul = CreateMountainSulfur() ?? TryCreate("sulfur", 1);
        if (sul == null)
        {
            Plugin.LogWarn("dispatch mountain sulfur-parity FAILED need=" + need);
            return;
        }

        AddManualStack(things, sul, need);
        Plugin.LogInfo("dispatch mountain sulfur-parity stone=" + stone + " sulfur+=" + need);
    }

    static int CountThingId(List<Thing> things, string id)
    {
        if (things == null || string.IsNullOrEmpty(id))
        {
            return 0;
        }

        string want = id.Trim();
        int n = 0;
        for (int i = 0; i < things.Count; i++)
        {
            Thing t = things[i];
            if (t == null)
            {
                continue;
            }

            try
            {
                if (string.Equals(t.id, want, StringComparison.OrdinalIgnoreCase))
                {
                    try { n += Math.Max(1, t.Num); } catch { n += 1; }
                }
            }
            catch
            {
            }
        }

        return n;
    }

    static void DistributePool(List<Thing> things, ManualPoolEntry[] pool, int total, string kind, DungeonDispatchMission? mission = null)
    {
        if (things == null || pool == null || pool.Length == 0 || total <= 0)
        {
            return;
        }

        // Fish is multi-id: roll each unit independently (heavy fish dampened).
        if (pool.Length == 1 && string.Equals(pool[0].Id, "fish", StringComparison.OrdinalIgnoreCase))
        {
            DistributeFishPool(things, total, mission);
            return;
        }

        // Beach food mix may include fish + palulu; fish still needs per-unit rolls.
        if (pool.Length > 1)
        {
            bool hasFish = false;
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i] != null && string.Equals(pool[i].Id, "fish", StringComparison.OrdinalIgnoreCase))
                {
                    hasFish = true;
                    break;
                }
            }

            if (hasFish)
            {
                int weightSumFish = 0;
                for (int i = 0; i < pool.Length; i++)
                {
                    if (pool[i] != null)
                    {
                        weightSumFish += Math.Max(1, pool[i].Weight);
                    }
                }

                int assignedFish = 0;
                for (int i = 0; i < pool.Length; i++)
                {
                    ManualPoolEntry e = pool[i];
                    if (e == null || string.IsNullOrEmpty(e.Id))
                    {
                        continue;
                    }

                    int n;
                    if (i == pool.Length - 1)
                    {
                        n = Math.Max(0, total - assignedFish);
                    }
                    else
                    {
                        n = Mathf.RoundToInt(total * (Math.Max(1, e.Weight) / (float)Math.Max(1, weightSumFish)));
                        n = Math.Max(0, n);
                        if (assignedFish + n > total)
                        {
                            n = Math.Max(0, total - assignedFish);
                        }
                    }

                    if (n <= 0)
                    {
                        continue;
                    }

                    if (string.Equals(e.Id, "fish", StringComparison.OrdinalIgnoreCase))
                    {
                        DistributeFishPool(things, n, mission);
                    }
                    else if (NeedsPerUnitRoll(e.Id))
                    {
                        for (int u = 0; u < n; u++)
                        {
                            Thing? unit = CreateManualThing(e.Id, kind);
                            if (unit != null)
                            {
                                AddManualStack(things, unit, 1);
                            }
                        }
                    }
                    else
                    {
                        Thing? t = CreateManualThing(e.Id, kind);
                        if (t != null)
                        {
                            AddManualStack(things, t, n);
                        }
                    }

                    assignedFish += n;
                }

                return;
            }
        }

        int weightSum = 0;
        for (int i = 0; i < pool.Length; i++)
        {
            if (pool[i] != null)
            {
                weightSum += Math.Max(1, pool[i].Weight);
            }
        }

        if (weightSum <= 0)
        {
            return;
        }

        int assigned = 0;
        for (int i = 0; i < pool.Length; i++)
        {
            ManualPoolEntry e = pool[i];
            if (e == null || string.IsNullOrEmpty(e.Id))
            {
                continue;
            }

            int n;
            if (i == pool.Length - 1)
            {
                n = Math.Max(0, total - assigned);
            }
            else
            {
                n = Mathf.RoundToInt(total * (Math.Max(1, e.Weight) / (float)weightSum));
                n = Math.Max(0, n);
                if (assigned + n > total)
                {
                    n = Math.Max(0, total - assigned);
                }
            }

            if (n <= 0)
            {
                continue;
            }

            if (NeedsPerUnitRoll(e.Id))
            {
                int added = 0;
                for (int u = 0; u < n; u++)
                {
                    Thing? unit = CreateManualThing(e.Id, kind);
                    if (unit == null)
                    {
                        continue;
                    }

                    AddManualStack(things, unit, 1);
                    added++;
                }

                assigned += added;
            }
            else
            {
                Thing? t = CreateManualThing(e.Id, kind);
                if (t == null)
                {
                    continue;
                }

                AddManualStack(things, t, n);
                assigned += n;
            }
        }
    }

    static bool NeedsPerUnitRoll(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        string low = id.Trim().ToLowerInvariant();
        return low == "flower"
            || low == "mushroom"
            || low == "mushroom_rare"
            || low == "meat"
            || low == "_meat"
            || low.StartsWith("meat:")
            || low == "egg" || low == "_egg"
            || low == "fruit"
            || low == "berry"
            || low == "herb";
    }

    /// <summary>
    /// Roll N fish via vanilla origin: ThingGen.Create("fish", -1, skillLv).
    /// lv is capped modestly; heavy/high-LV fish are re-rolled so big fish appear ~1/10 as often.
    /// Optional post-filter drops non-fish junk if Create ever yields one.
    /// </summary>
    static void DistributeFishPool(List<Thing> things, int total, DungeonDispatchMission? mission = null)
    {
        if (things == null || total <= 0)
        {
            return;
        }

        int skill = 0;
        try
        {
            if (mission != null)
            {
                skill = Math.Max(0, mission.gatherSkill);
                if (skill <= 0)
                {
                    skill = Math.Max(0, mission.exploreSkill);
                }
            }
        }
        catch
        {
            skill = 0;
        }

        skill = Math.Max(1, Math.Min(skill, 40));
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // Keep one prototype per rolled id so stack add still works.
        var prototypes = new Dictionary<string, Thing>(StringComparer.OrdinalIgnoreCase);

        for (int n = 0; n < total; n++)
        {
            Thing? t = null;
            try
            {
                t = RollBeachFish(skill);
                if (t == null)
                {
                    continue;
                }

                string id = t.id ?? "fish";
                if (!prototypes.ContainsKey(id))
                {
                    prototypes[id] = t;
                    counts[id] = 1;
                    t = null; // owned by prototypes
                }
                else
                {
                    counts[id] = counts[id] + 1;
                    if (t.parent == null && !t.isDestroyed) t.Destroy();
                    t = null;
                }
            }
            catch
            {
                try
                {
                    if (t != null && t.parent == null && !t.isDestroyed) t.Destroy();
                }
                catch
                {
                }
            }
        }

        foreach (var kv in counts)
        {
            if (kv.Value <= 0)
            {
                continue;
            }

            Thing? t = null;
            if (prototypes.TryGetValue(kv.Key, out Thing? proto) && proto != null)
            {
                t = proto;
                prototypes.Remove(kv.Key);
            }
            else
            {
                t = TryCreate(kv.Key, 1) ?? TryCreate("fish", 1);
            }

            if (t == null)
            {
                continue;
            }

            KeepFoodFresh(t);
            AddManualStack(things, t, kv.Value);
        }

        // Destroy unused prototypes (should be none).
        foreach (var kv in prototypes)
        {
            try
            {
                Thing p = kv.Value;
                if (p != null && p.parent == null && !p.isDestroyed)
                {
                    p.Destroy();
                }
            }
            catch
            {
            }
        }
    }

    static Thing? RollBeachFish(int skill)
    {
        Thing? bestLight = null;
        int bestLightW = int.MaxValue;
        Thing? t = null;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                if (t != null && t.parent == null && !t.isDestroyed)
                {
                    t.Destroy();
                }
            }
            catch
            {
            }

            t = null;
            int lv = 1;
            try { lv = EClass.rnd(Math.Max(1, skill) * 2) + 1; }
            catch { lv = Math.Max(1, skill); }
            // Cap lv so Create-by-lv does not flood high-quality heavy fish.
            // Player complaint: max-weight fish still common. Cap harder + rare-keep below.
            lv = Math.Max(1, Math.Min(lv, 12));

            try { t = ThingGen.Create("fish", -1, lv); }
            catch { t = null; }

            if (t == null)
            {
                t = TryCreate("fish", 1) ?? TryCreate("65", 1);
            }

            if (t == null)
            {
                continue;
            }

            if (!IsBeachFishCandidate(t))
            {
                try
                {
                    if (t.parent == null && !t.isDestroyed) t.Destroy();
                }
                catch
                {
                }
                t = null;
                continue;
            }

            if (!IsHeavyBeachFish(t))
            {
                try
                {
                    if (bestLight != null && bestLight.parent == null && !bestLight.isDestroyed)
                    {
                        bestLight.Destroy();
                    }
                }
                catch
                {
                }
                return t;
            }

            // Keep the lightest heavy candidate as last resort.
            int w = FishWeightScore(t);
            if (w < bestLightW)
            {
                try
                {
                    if (bestLight != null && bestLight.parent == null && !bestLight.isDestroyed)
                    {
                        bestLight.Destroy();
                    }
                }
                catch
                {
                }
                bestLight = t;
                bestLightW = w;
                t = null;
            }
            else
            {
                try
                {
                    if (t.parent == null && !t.isDestroyed) t.Destroy();
                }
                catch
                {
                }
                t = null;
            }
        }

        // ~1/20 keep a heavy fish; otherwise fall back to plain fish id.
        try
        {
            if (bestLight != null && EClass.rnd(20) == 0)
            {
                return bestLight;
            }
        }
        catch
        {
            if (bestLight != null)
            {
                return bestLight;
            }
        }

        try
        {
            if (bestLight != null && bestLight.parent == null && !bestLight.isDestroyed)
            {
                bestLight.Destroy();
            }
        }
        catch
        {
        }

        Thing? plain = TryCreate("fish", 1) ?? TryCreate("65", 1);
        if (plain != null && IsBeachFishCandidate(plain))
        {
            return plain;
        }

        try
        {
            if (plain != null && plain.parent == null && !plain.isDestroyed)
            {
                plain.Destroy();
            }
        }
        catch
        {
        }

        return null;
    }

    static bool IsHeavyBeachFish(Thing t)
    {
        if (t == null)
        {
            return false;
        }

        int w = FishWeightScore(t);
        int lv = 1;
        try { lv = Math.Max(1, t.LV); } catch { lv = 1; }
        // Weight roughly tracks quality; keep common light fish, rare heavy ones.
        return w >= 12 || lv >= 10;
    }

    static int FishWeightScore(Thing t)
    {
        if (t == null)
        {
            return 0;
        }

        try
        {
            return Math.Max(0, t.SelfWeight);
        }
        catch
        {
            try
            {
                return Math.Max(0, t.source != null ? t.source.weight : 0);
            }
            catch
            {
                return 0;
            }
        }
    }

    static bool IsBeachFishCandidate(Thing t)
    {
        if (t == null)
        {
            return false;
        }

        try
        {
            if (IsFishLike(t))
            {
                return true;
            }

            string id = (t.id ?? "").ToLowerInvariant();
            if (id == "fish" || id == "65" || id == "65_gold" || id.Contains("fish"))
            {
                return true;
            }

            string cat = t.category != null ? (t.category.id ?? "") : "";
            if (cat.ToLowerInvariant().Contains("fish"))
            {
                return true;
            }

            // Fishing junk table can roll non-fish (junk ids). Reject those.
            return false;
        }
        catch
        {
            return false;
        }
    }

    static void AddManualStack(List<Thing> things, Thing? t, int n)
    {
        if (things == null || t == null || n <= 0)
        {
            try
            {
                if (t != null && t.parent == null && !t.isDestroyed)
                {
                    t.Destroy();
                }
            }
            catch
            {
            }

            return;
        }

        try
        {
            t.SetNum(Math.Max(1, n));
            KeepFoodFresh(t);
            things.Add(t);
        }
        catch
        {
            try
            {
                if (t.parent == null && !t.isDestroyed)
                {
                    t.Destroy();
                }
            }
            catch
            {
            }
        }
    }

    static Thing? CreateManualThing(string id, string kind = "")
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        string raw = id.Trim();
        string low = raw.ToLowerInvariant();
        try
        {
            if (low.StartsWith("mat:"))
            {
                string alias = raw.Substring(4).Trim();
                if (alias.Equals("copper", StringComparison.OrdinalIgnoreCase)
                    || alias.Equals("iron", StringComparison.OrdinalIgnoreCase))
                {
                    return CreateMetalHard(alias) ?? CreateMaterialThing(alias) ?? CreateWeightedOre(common: true);
                }

                return CreateMaterialThing(alias) ?? TryCreate(alias, 1);
            }

            if (low.StartsWith("wood:"))
            {
                string alias = raw.Substring(5).Trim();
                return CreateWoodLog(alias) ?? TryCreate("log", 1);
            }

            if (low == "sand" || low == "sea_sand" || low == "sand_sea" || low.Contains("sand"))
            {
                // Never create beach sea sand outside beach region.
                string rk = (kind ?? "").Trim().ToLowerInvariant();
                if (rk != "beach")
                {
                    return CreateMaterialThing("stone")
                        ?? TryCreate("rock", 1)
                        ?? TryCreate("stone", 1);
                }

                return CreateBeachSand()
                    ?? CreateMaterialThing("sand");
            }

            if (low == "salt")
            {
                return CreateBeachSalt() ?? TryCreate("salt", 1);
            }

            if (low == "seaweed" || low == "seaweed2")
            {
                // User-facing 海藻 is seaweed; seaweed2 is deep seaweed fallback only.
                return TryCreate("seaweed", 1)
                    ?? TryCreate("seaweed2", 1)
                    ?? TryCreateFromCategorySafe("plant", 1)
                    ?? TryCreate("bait", 1);
            }

            if (low == "bait")
            {
                return TryCreate("bait", 1) ?? TryCreate("seaweed", 1);
            }

            if (low == "fish")
            {
                // Single-create path: vanilla fish origin at modest lv.
                Thing? f = null;
                try { f = ThingGen.Create("fish", -1, 10); } catch { f = null; }
                if (f == null)
                {
                    f = TryCreate("fish", 1) ?? TryCreate("65", 1);
                }
                if (f != null)
                {
                    KeepFoodFresh(f);
                }
                return f;
            }

            if (low == "vine")
            {
                return CreateForestVine() ?? TryCreate("vine", 1) ?? TryCreate("weed", 1);
            }

            if (low == "resin" || low == "plastic")
            {
                return CreatePlasticOre()
                    ?? TryCreate("resin", 1)
                    ?? TryCreate("plastic", 1)
                    ?? CreateMaterialThing("plastic");
            }

            if (low == "branch")
            {
                return TryCreate("branch", 1)
                    ?? CreateWoodLog("wood")
                    ?? TryCreate("log", 1);
            }

            if (low == "bark")
            {
                return TryCreate("bark", 1)
                    ?? CreateMaterialThing("bark")
                    ?? TryCreate("branch", 1)
                    ?? CreateForestVine();
            }

            if (low == "log" || low == "wood")
            {
                return CreateWoodLog("wood") ?? TryCreate("log", 1);
            }

            if (low == "sulfur")
            {
                return CreateMountainSulfur() ?? TryCreate("sulfur", 1);
            }

            if (low == "gem")
            {
                return CreateMountainGem();
            }

            if (low == "ore" || low == "copper")
            {
                return CreateMetalHard("copper") ?? CreateMaterialThing("copper") ?? CreateWeightedOre(common: true);
            }

            if (low == "iron")
            {
                return CreateMetalHard("iron") ?? CreateMaterialThing("iron");
            }

            if (low == "stone" || low == "rock")
            {
                return CreateMaterialThing(low == "rock" ? "stone" : low)
                    ?? TryCreate("rock", 1)
                    ?? TryCreate("stone", 1);
            }

            if (low == "pasture")
            {
                return TryCreate("pasture", 1) ?? TryCreate("grass", 1);
            }

            if (low == "grass" || low == "weed")
            {
                return TryCreate("grass", 1)
                    ?? TryCreate("weed", 1)
                    ?? TryCreate("pasture", 1)
                    ?? TryCreateFromCategorySafe("plant", 1);
            }

            if (low == "flower" || low.StartsWith("flower_"))
            {
                // Stay inside mission lock / plain list. No category lottery.
                return CreatePlainFlower()
                    ?? TryCreate("flower", 1);
            }

            if (low == "herb" || low.StartsWith("herb_"))
            {
                return CreatePlainHerb()
                    ?? TryCreate("herb", 1);
            }

            if (low == "mushroom" || low == "mushroom_rare")
            {
                bool rare = low == "mushroom_rare" || low.Contains("rare");
                // Occasionally upgrade common mushroom rolls to rare (~8%).
                if (!rare)
                {
                    try { rare = EClass.rnd(12) == 0; } catch { rare = false; }
                }

                Thing? mush = CreateForestMushroom(common: !rare)
                    ?? TryCreate(rare ? "mushroom_rare" : "mushroom", 1)
                    ?? TryCreate("699", 1)
                    ?? TryCreate("700", 1)
                    ?? TryCreateFromCategorySafe("mushroom", 1);
                if (mush != null && IsBannedRewardMushroom(mush))
                {
                    if (mush.parent == null) mush.Destroy();
                    mush = CreateForestMushroom(common: true) ?? TryCreate("mushroom", 1);
                }
                if (mush != null)
                {
                    KeepFoodFresh(mush);
                }
                return mush;
            }

            if (low == "berry")
            {
                return TryCreate("berry", 1)
                    ?? TryCreateFromCategorySafe("fruit", 1)
                    ?? CreateForestFruit();
            }

            if (low == "fruit" || low == "apple" || low == "grape" || low == "banana"
                || low == "palulu" || low == "orange" || low == "peach")
            {
                // Beach staple: never substitute another fruit for palulu.
                if (low == "palulu")
                {
                    return CreateBeachPaluluFruit();
                }

                Thing? fr = TryCreate(raw, 1);
                if (fr != null)
                {
                    KeepFoodFresh(fr);
                    return fr;
                }
                return CreateForestFruit();
            }

            if (low == "egg" || low == "_egg" || low == "egg_fertilized")
            {
                Thing? egg = CreateNamedEgg()
                    ?? CreateNamedEgg("chicken");
                if (egg != null && !IsForbiddenPlainEgg(egg))
                {
                    KeepFoodFresh(egg);
                    return egg;
                }

                try { if (egg != null && egg.parent == null && !egg.isDestroyed) egg.Destroy(); } catch { }
                return null;
            }

            if (low == "meat" || low == "_meat" || low == "meat_marble" || low.StartsWith("meat:"))
            {
                string species = "chicken";
                if (low.StartsWith("meat:") && low.Length > 5)
                {
                    species = raw.Substring(5).Trim();
                }
                else
                {
                    string[] picks = (_missionMeatSpecies != null && _missionMeatSpecies.Length > 0)
                        ? _missionMeatSpecies
                        : PlainMeatSpeciesIds;
                    if (picks != null && picks.Length > 0)
                    {
                        try { species = picks[EClass.rnd(picks.Length)]; }
                        catch { species = picks[0]; }
                    }
                }

                if (!IsAllowedPlainMeatSpecies(species))
                {
                    species = "chicken";
                }

                Thing? meat = CreateNamedMeat(species)
                    ?? CreateNamedMeat("chicken")
                    ?? TryCreate("meat", 1)
                    ?? TryCreate("_meat", 1);
                if (meat != null)
                {
                    KeepFoodFresh(meat);
                }
                return meat;
            }

            Thing? t = TryCreate(raw, 1);
            if (t != null)
            {
                KeepFoodFresh(t);
            }

            return t;
        }
        catch
        {
            return null;
        }
    }

    static bool HasThingId(List<Thing> things, string id)
    {
        if (things == null || string.IsNullOrEmpty(id))
        {
            return false;
        }

        string want = id.Trim();
        for (int i = 0; i < things.Count; i++)
        {
            Thing t = things[i];
            if (t == null)
            {
                continue;
            }

            try
            {
                if (string.Equals(t.id, want, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }

}
