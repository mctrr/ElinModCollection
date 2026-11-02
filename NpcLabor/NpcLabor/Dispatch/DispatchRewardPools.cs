using System;
using System.Collections.Generic;
using UnityEngine;

namespace NpcLabor.Dispatch;

/// <summary>
/// PARTIAL: region/dungeon loot pools + rolls.
/// Split from DungeonDispatchRewards.cs (2026-08-07).
/// </summary>
internal static partial class DungeonDispatchRewards
{
    static readonly string[] PlainFlowerIds =
    {
        "flower",
        "flower_white",
        "flower_yellow",
        "flower_blue",
        "flower_tulip",
        "flower_rose",
        "flower_ajisai",
        "flower_himawari",
    };

    static readonly string[] PlainHerbIds =
    {
        "herb",
        "herb_green",
        "herb_blue",
        "herb_red",
        "herb_purple",
    };

    // Domestic / farmyard egg sources only. Never ash/alchemy/monster random.
    static readonly string[] PlainEggSpeciesIds =
    {
        "chicken",
        "chicken_wild",
        "bird",
        "sheep",
        "cow",
        "pig",
    };

    // Plain meat sources. Mission lock picks 2-4 of these.
    static readonly string[] PlainMeatSpeciesIds =
    {
        "chicken",
        "sheep",
        "cow",
        "putty",
        "bird",
        "pig",
    };

    // Even forest food mix: mushrooms + common fruits (berry id is blueberry).
    static readonly string[] ForestFoodIds =
    {
        "mushroom",
        "699", // white mushroom
        "700", // red mushroom
        "berry",
        "grape",
        "apple",
        "banana",
        "palulu",
        "orange",
        "peach",
    };

    static readonly string[] CommonMushroomIds =
    {
        "mushroom",
        "699",
        "700",
        "white mushroom",
        "red mushroom",
    };

    static readonly string[] RareMushroomIds =
    {
        // Food-only rare mushrooms. Never include 1122 / chaos mushroom (furniture).
        "mushroom_rare",
        "699",
        "700",
    };

    sealed class RegionLootEntry
    {
        public string id = "";
        public int weight = 1;
        public int matId = -1;
    }

    sealed class RegionLootPool
    {
        public readonly List<RegionLootEntry> materials = new List<RegionLootEntry>();
        public readonly List<RegionLootEntry> harvestIds = new List<RegionLootEntry>();
        public readonly List<RegionLootEntry> fishIds = new List<RegionLootEntry>();
        public readonly List<RegionLootEntry> otherIds = new List<RegionLootEntry>();
        public bool IsEmpty =>
            materials.Count == 0 && harvestIds.Count == 0 && fishIds.Count == 0 && otherIds.Count == 0;
    }

    static readonly Dictionary<string, RegionLootPool> DungeonPoolCache = new Dictionary<string, RegionLootPool>();

    // Mission-scoped flower id subset (2-4 kinds). Null = free roll across all.
    [ThreadStatic]
    static string[]? _missionFlowerIds;

    // Mission-scoped herb id subset (2-4 kinds). Null = free roll across all.
    [ThreadStatic]
    static string[]? _missionHerbIds;

    // Mission-scoped meat species subset (2-4 kinds). Null = free roll across PlainMeatSpeciesIds.
    [ThreadStatic]
    static string[]? _missionMeatSpecies;

    static void BeginMissionFlowerLock(DungeonDispatchMission? mission)
    {
        _missionFlowerIds = null;
        _missionHerbIds = null;
        _missionMeatSpecies = null;
        if (PlainFlowerIds == null || PlainFlowerIds.Length == 0)
        {
            BeginMissionHerbLock(mission, seed: 17);
            BeginMissionMeatLock(mission, seed: 19);
            return;
        }

        // Stable-ish seed from mission identity so reload keeps the same 2-4 kinds.
        int seed = 17;
        try
        {
            seed ^= (mission?.missionId ?? 0) * 397;
            seed ^= (mission?.regionKind ?? "").GetHashCode();
            seed ^= Math.Max(0, mission?.exploreSkill ?? 0) * 13;
            seed ^= Math.Max(0, mission?.gatherSkill ?? 0) * 29;
        }
        catch
        {
            seed = 17;
        }

        _missionFlowerIds = PickMissionIdSubset(PlainFlowerIds, seed, minKinds: 2, maxKinds: 4);
        BeginMissionHerbLock(mission, seed ^ unchecked((int)0x5f3759df));
        BeginMissionMeatLock(mission, seed ^ unchecked((int)0x9e3779b9));
    }

    static void EndMissionFlowerLock()
    {
        _missionFlowerIds = null;
        _missionHerbIds = null;
        _missionMeatSpecies = null;
    }

    static void BeginMissionHerbLock(DungeonDispatchMission? mission, int seed = 0)
    {
        _missionHerbIds = null;
        if (PlainHerbIds == null || PlainHerbIds.Length == 0)
        {
            return;
        }

        if (seed == 0)
        {
            seed = 31;
            try
            {
                seed ^= (mission?.missionId ?? 0) * 911;
                seed ^= (mission?.regionKind ?? "").GetHashCode();
                seed ^= Math.Max(0, mission?.gatherSkill ?? 0) * 17;
            }
            catch
            {
                seed = 31;
            }
        }

        _missionHerbIds = PickMissionIdSubset(PlainHerbIds, seed, minKinds: 2, maxKinds: 4);
    }

    static void BeginMissionMeatLock(DungeonDispatchMission? mission, int seed = 0)
    {
        _missionMeatSpecies = null;
        if (PlainMeatSpeciesIds == null || PlainMeatSpeciesIds.Length == 0)
        {
            return;
        }

        if (seed == 0)
        {
            seed = 41;
            try
            {
                seed ^= (mission?.missionId ?? 0) * 733;
                seed ^= (mission?.regionKind ?? "").GetHashCode();
                seed ^= Math.Max(0, mission?.gatherSkill ?? 0) * 23;
            }
            catch
            {
                seed = 41;
            }
        }

        _missionMeatSpecies = PickMissionIdSubset(PlainMeatSpeciesIds, seed, minKinds: 2, maxKinds: 4);
    }

    static bool IsAllowedPlainMeatSpecies(string? species)
    {
        if (string.IsNullOrWhiteSpace(species))
        {
            return false;
        }

        string low = (species ?? "").Trim().ToLowerInvariant();
        if (low.StartsWith("meat:"))
        {
            low = low.Substring(5);
        }

        for (int i = 0; i < PlainMeatSpeciesIds.Length; i++)
        {
            if (string.Equals(PlainMeatSpeciesIds[i], low, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Accept common aliases that CreateNamedMeat already maps.
        return low == "chicken_wild"
            || low == "lamb"
            || low == "goat"
            || low == "bull"
            || low == "ox"
            || low == "cattle"
            || low == "beef"
            || low == "ball"
            || low == "slime"
            || low == "yeek";
    }

    static string[]? PickMissionIdSubset(string[] source, int seed, int minKinds, int maxKinds)
    {
        if (source == null || source.Length == 0)
        {
            return null;
        }

        int lo = Math.Max(1, Math.Min(minKinds, source.Length));
        int hi = Math.Max(lo, Math.Min(maxKinds, source.Length));
        int span = Math.Max(1, hi - lo + 1);
        int want = lo + ((seed & 0x7fffffff) % span);
        want = Math.Min(want, source.Length);

        var picked = new List<string>(want);
        int start = (seed & 0x7fffffff) % source.Length;
        for (int i = 0; i < source.Length && picked.Count < want; i++)
        {
            string id = source[(start + i) % source.Length];
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            bool dup = false;
            for (int j = 0; j < picked.Count; j++)
            {
                if (string.Equals(picked[j], id, StringComparison.OrdinalIgnoreCase))
                {
                    dup = true;
                    break;
                }
            }

            if (!dup)
            {
                picked.Add(id);
            }
        }

        return picked.Count > 0 ? picked.ToArray() : null;
    }

    static Thing? CreatePlainFlower()
    {
        string[] ids = (_missionFlowerIds != null && _missionFlowerIds.Length > 0)
            ? _missionFlowerIds
            : PlainFlowerIds;

        // Pick a common wildflower; fall back through the locked/mission list.
        int start = ids.Length > 0 ? EClass.rnd(ids.Length) : 0;
        for (int i = 0; i < ids.Length; i++)
        {
            string id = ids[(start + i) % ids.Length];
            Thing? t = TryCreate(id, 1);
            if (t != null)
            {
                ForceFreshProduceMaterial(t);
                KeepFoodFresh(t);
                return t;
            }
        }

        // Never free-roll the whole flower category: that sprays every flower id.
        return TryCreate("flower", 1);
    }

    static Thing? CreatePlainHerb()
    {
        string[] ids = (_missionHerbIds != null && _missionHerbIds.Length > 0)
            ? _missionHerbIds
            : PlainHerbIds;

        int start = ids.Length > 0 ? EClass.rnd(ids.Length) : 0;
        for (int i = 0; i < ids.Length; i++)
        {
            string id = ids[(start + i) % ids.Length];
            Thing? t = TryCreate(id, 1);
            if (t != null)
            {
                ForceFreshProduceMaterial(t);
                KeepFoodFresh(t);
                return t;
            }
        }

        // Keep herb kinds closed; do not fall into plant/flower category lottery.
        return TryCreate("herb", 1);
    }


    static Thing? CreateForestMushroom(bool common)
    {
        string[] ids = common ? CommonMushroomIds : RareMushroomIds;
        int start = ids.Length > 0 ? EClass.rnd(ids.Length) : 0;
        for (int i = 0; i < ids.Length; i++)
        {
            string id = ids[(start + i) % ids.Length];
            if (IsBannedRewardMushroomId(id))
            {
                continue;
            }

            Thing? t = TryCreate(id, 1);
            if (t != null)
            {
                if (IsBannedRewardMushroom(t))
                {
                    if (t.parent == null) t.Destroy();
                    continue;
                }

                ForceFreshProduceMaterial(t);
                KeepFoodFresh(t);
                return t;
            }
        }

        if (!common)
        {
            // If rare ids missing, still give a common mushroom rather than nothing.
            return CreateForestMushroom(common: true);
        }

        Thing? fallback = TryCreateFromCategorySafe("mushroom", 1) ?? TryCreate("mushroom", 1);
        if (fallback != null && IsBannedRewardMushroom(fallback))
        {
            if (fallback.parent == null) fallback.Destroy();
            return null;
        }

        return fallback;
    }

    static bool IsBannedRewardMushroomId(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        string low = (id ?? "").Trim().ToLowerInvariant();
        return low == "1122"
            || low == "chaos mushroom"
            || low.Contains("chaos");
    }

    static bool IsBannedRewardMushroom(Thing? t)
    {
        if (t == null)
        {
            return true;
        }

        try
        {
            string id = (t.id ?? "").ToLowerInvariant();
            if (IsBannedRewardMushroomId(id))
            {
                return true;
            }

            string nm = "";
            try { nm = (t.NameOne ?? t.NameSimple ?? t.Name ?? "").ToLowerInvariant(); } catch { nm = ""; }
            if (nm.Contains("chaos") || nm.Contains("混沌"))
            {
                return true;
            }

            // Chaos mushroom is furniture, not food.
            try
            {
                if (t.IsFood)
                {
                    return false;
                }
            }
            catch
            {
            }

            try
            {
                string cat = t.category != null ? (t.category.id ?? "").ToLowerInvariant() : "";
                if (cat.Contains("furniture") || cat.Contains("decor") || cat.Contains("tool"))
                {
                    return true;
                }
            }
            catch
            {
            }
        }
        catch
        {
        }

        return false;
    }

    /// <summary>
    /// Named egg like CreateNamedMeat: MakeFoodFrom species so the name is not anonymous.
    /// Plain region eggs stay domestic-only (no ash/alchemy/monster lottery).
    /// </summary>
    static Thing? CreateNamedEgg(string? charaId = null)
    {
        string[] picks;
        if (!string.IsNullOrWhiteSpace(charaId))
        {
            string wanted = (charaId ?? "").Trim();
            if (!IsAllowedPlainEggSpecies(wanted))
            {
                wanted = "chicken";
            }

            picks = new[] { wanted, "chicken", "bird", "chicken_wild" };
        }
        else
        {
            picks = PlainEggSpeciesIds;
            try
            {
                string first = picks[EClass.rnd(picks.Length)];
                picks = new[] { first, "chicken", "bird", "chicken_wild", "sheep", "cow", "pig" };
            }
            catch { }
        }

        for (int i = 0; i < picks.Length; i++)
        {
            string refId = picks[i];
            if (string.IsNullOrEmpty(refId))
            {
                continue;
            }

            if (!IsAllowedPlainEggSpecies(refId))
            {
                continue;
            }

            Thing? t = TryCreate("_egg", 1) ?? TryCreate("egg", 1) ?? TryCreate("egg_fertilized", 1);
            if (t == null)
            {
                continue;
            }

            bool ok = false;
            try
            {
                t.MakeFoodFrom(refId);
                ok = true;
            }
            catch
            {
                try
                {
                    t.MakeRefFrom(refId);
                    ok = true;
                }
                catch
                {
                    ok = false;
                }
            }

            if (!ok)
            {
                if (t.parent == null) t.Destroy();
                continue;
            }

            try
            {
                if (string.IsNullOrEmpty(t.c_idRefCard))
                {
                    t.c_idRefCard = refId;
                }
            }
            catch
            {
            }

            // Always force produce material first so MakeFoodFrom ash bleed cannot stick.
            ForceFreshProduceMaterial(t);

            // Reject ash/alchemy/monster material bleed after MakeFoodFrom.
            if (IsForbiddenPlainEgg(t))
            {
                if (t.parent == null) t.Destroy();
                continue;
            }

            KeepFoodFresh(t);
            return t;
        }

        // Never TryMakeRandomItem on eggs: that can name ash/alchemy monsters.
        Thing? plain = TryCreate("_egg", 1) ?? TryCreate("egg", 1);
        if (plain != null)
        {
            try
            {
                plain.MakeFoodFrom("chicken");
                plain.c_idRefCard = "chicken";
            }
            catch
            {
            }

            ForceFreshProduceMaterial(plain);
            if (!IsForbiddenPlainEgg(plain))
            {
                KeepFoodFresh(plain);
                return plain;
            }

            if (plain.parent == null) plain.Destroy();
        }

        return null;
    }

    static bool IsAllowedPlainEggSpecies(string? species)
    {
        if (string.IsNullOrWhiteSpace(species))
        {
            return false;
        }

        string low = (species ?? "").Trim().ToLowerInvariant();
        if (low.StartsWith("egg:"))
        {
            low = low.Substring(4);
        }

        for (int i = 0; i < PlainEggSpeciesIds.Length; i++)
        {
            if (string.Equals(PlainEggSpeciesIds[i], low, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Extra domestic-ish farm animals if future refs arrive via plan lines.
        return low == "goat"
            || low == "lamb"
            || low == "bull"
            || low == "ox"
            || low == "cattle";
    }

    static bool IsForbiddenPlainEgg(Thing? t)
    {
        if (t == null)
        {
            return true;
        }

        try
        {
            string refId = "";
            try { refId = (t.c_idRefCard ?? "").ToLowerInvariant(); } catch { refId = ""; }
            if (!string.IsNullOrEmpty(refId) && !IsAllowedPlainEggSpecies(refId))
            {
                return true;
            }

            string mat = "";
            try { mat = t.material != null ? (t.material.alias ?? "").ToLowerInvariant() : ""; } catch { mat = ""; }
            if (mat == "ash" || mat.Contains("ash") || mat.Contains("alchemy") || mat.Contains("mana")
                || mat.Contains("ether") || mat.Contains("chaos") || mat.Contains("sulf")
                || mat.Contains("bone") || mat.Contains("spirit") || mat.Contains("demon"))
            {
                return true;
            }

            string nm = "";
            try { nm = t.NameOne ?? t.NameSimple ?? t.Name ?? ""; } catch { nm = ""; }
            string low = nm.ToLowerInvariant();
            if (nm.Contains("炼金灰") || nm.Contains("炼金") || low.Contains("alchemy")
                || low.Contains("ash")
                || low.Contains("chaos") || low.Contains("slime") || low.Contains("putty") || nm.Contains("波球")
                || low.Contains("monster") || low.Contains("undead"))
            {
                return true;
            }

            return false;
        }
        catch
        {
            return true;
        }
    }


    static Thing? RollRegionScanned(DungeonDispatchMission mission, bool soft)
    {
        // Region live biome scan retired (v28). Manual pools own full region haul.
        // Keep this only as a soft single-find fallback for mid-run notes.
        return FallbackRegionBasic(mission?.regionKind ?? "");
    }


    static Thing? RollRegionDrop(string kind, DungeonDispatchMission mission)
    {
        // kind kept for call-site clarity; biome comes from mission.regionKind.
        return RollRegionScanned(mission, soft: true);
    }


    static Thing? FallbackRegionBasic(string kind)
    {
        switch ((kind ?? "").ToLowerInvariant())
        {
            case "beach":
                return CreateBeachSand()
                    ?? CreateBeachSalt()
                    ?? CreateMaterialThing("sand")
                    ?? TryCreate("rock", 1);
            case "forest":
                return CreateWoodLog("wood")
                    ?? CreateForestFruit()
                    ?? CreateForestVine()
                    ?? TryCreate("log", 1)
                    ?? TryCreate("mushroom", 1);
            case "mountain":
                return CreateMaterialThing("copper")
                    ?? CreateMaterialThing("iron")
                    ?? CreatePlasticOre()
                    ?? CreateWeightedOre(common: true)
                    ?? CreateMountainSulfur()
                    ?? CreateMaterialThing("stone")
                    ?? TryCreate("rock", 1);
            default:
                return TryCreate("pasture", 1)
                    ?? CreatePlainFlower()
                    ?? CreateNamedMeat("chicken")
                    ?? TryCreate("grass", 1);
        }
    }


    static void SeedMatAlias(RegionLootPool pool, string alias, int weight)
    {
        if (pool == null || string.IsNullOrEmpty(alias) || weight <= 0)
        {
            return;
        }


            alias = ResolveMaterialAlias(alias);
            if (EClass.sources?.materials?.alias == null
                || string.IsNullOrEmpty(alias)
                || !EClass.sources.materials.alias.ContainsKey(alias))
            {
                return;
            }

            SourceMaterial.Row row = EClass.sources.materials.alias[alias];
            if (row == null)
            {
                return;
            }

            // Mineral rows often have empty thing; still seed by matId so CreateFromMaterialEntry works.
            AddMat(pool, row, weight);

}


    static string[] KindBiomeIds(string kind)
    {
        switch ((kind ?? "").ToLowerInvariant())
        {
            case "beach":
                return new[] { "Sand", "Water" };
            case "forest":
                return new[] { "Forest" };
            case "mountain":
                return new[] { "Barren", "Snow", "Default" };
            case "plain":
                return new[] { "Plain", "Mud" };
            default:
                return new[] { "Plain" };
        }
    }



    static BiomeProfile? LookupBiome(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        try
        {
            var biomes = EClass.core?.refs?.biomes;
            if (biomes == null)
            {
                return null;
            }


                if (id.Equals("Plain", StringComparison.OrdinalIgnoreCase) && biomes.Plain != null)
                {
                    return biomes.Plain;
                }

                if (id.Equals("Sand", StringComparison.OrdinalIgnoreCase) && biomes.Sand != null)
                {
                    return biomes.Sand;
                }

                if (id.Equals("Water", StringComparison.OrdinalIgnoreCase) && biomes.Water != null)
                {
                    return biomes.Water;
                }

            try
            {
                if (biomes.dict != null)
                {
                    foreach (var kv in biomes.dict)
                    {

                            string k = kv.Key != null ? kv.Key.ToString() : "";
                            if (k.Equals(id, StringComparison.OrdinalIgnoreCase)
                                || k.EndsWith(id, StringComparison.OrdinalIgnoreCase))
                            {
                                return kv.Value;
                            }

}


                        // UD_Biome is Dictionary<string, BiomeProfile>
                        if (biomes.dict.ContainsKey(id))
                        {
                            return biomes.dict[id];
                        }

                        // Try enum name form.
                        string enumName = System.Enum.Parse(typeof(BiomeID), id, true).ToString();
                        if (!string.IsNullOrEmpty(enumName) && biomes.dict.ContainsKey(enumName))
                        {
                            return biomes.dict[enumName];
                        }

}
            }
            catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchRewards.cs silent catch: " + __e.Message); }
}
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchRewards.cs silent catch: " + __e.Message); }
return null;
    }


    static void ScanBiomeIntoPool(RegionLootPool pool, List<BiomeProfile> profiles)
    {
        if (profiles == null)
        {
            return;
        }

        foreach (BiomeProfile bp in profiles)
        {
            if (bp == null)
            {
                continue;
            }


                AddMat(pool, bp.MatFloor, 6);
                AddMat(pool, bp.MatSub, 4);


                if (bp.spawn != null && bp.spawn.thing != null)
                {
                    foreach (BiomeProfile.SpawnListThing s in bp.spawn.thing)
                    {
                        if (s == null || string.IsNullOrEmpty(s.id))
                        {
                            continue;
                        }

                        int w = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(0.1f, s.chance) * 10f));
                        ClassifyThingId(pool, s.id, w);
                    }
                }

            try
            {
                if (bp.cluster != null && bp.cluster.thing != null)
                {
                    foreach (BiomeProfile.ClusterThing cluster in bp.cluster.thing)
                    {
                        if (cluster == null || cluster.items == null)
                        {
                            continue;
                        }

                        foreach (BiomeProfile.Cluster.ItemThing item in cluster.items)
                        {
                            if (item == null || string.IsNullOrEmpty(item.id))
                            {
                                continue;
                            }

                            int w = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(0.1f, item.chance) * 10f));
                            ClassifyThingId(pool, item.id, w);

                                if (!string.IsNullOrEmpty(item.material)
                                    && EClass.sources != null
                                    && EClass.sources.materials != null
                                    && EClass.sources.materials.alias != null
                                    && EClass.sources.materials.alias.ContainsKey(item.material))
                                {
                                    AddMat(pool, EClass.sources.materials.alias[item.material], w + 2);
                                }

}
                    }
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchRewards.cs silent catch: " + __e.Message); }
            try
            {
                if (bp.cluster != null && bp.cluster.obj != null)
                {
                    foreach (BiomeProfile.ClusterObj cluster in bp.cluster.obj)
                    {
                        if (cluster == null || cluster.items == null)
                        {
                            continue;
                        }

                        foreach (BiomeProfile.Cluster.Item item in cluster.items)
                        {
                            if (item == null)
                            {
                                continue;
                            }

                            int w = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(0.1f, item.chance) * 8f));
                            try
                            {
                                SourceObj.Row? row = null;
                                if (item.idObj > 0
                                    && EClass.sources != null
                                    && EClass.sources.objs != null
                                    && EClass.sources.objs.map != null
                                    && EClass.sources.objs.map.ContainsKey(item.idObj))
                                {
                                    row = EClass.sources.objs.map[item.idObj];
                                }

                                if (row == null)
                                {
                                    continue;
                                }

                                string harvestId = "";

                                    if (row.growth != null && !string.IsNullOrEmpty(row.growth.idHarvestThing))
                                    {
                                        harvestId = row.growth.idHarvestThing;
                                    }

if (!string.IsNullOrEmpty(harvestId))
                                {
                                    ClassifyThingId(pool, harvestId, w + 3);
                                }


                                    if (!string.IsNullOrEmpty(row.defMat)
                                        && EClass.sources?.materials?.alias != null
                                        && EClass.sources.materials.alias.ContainsKey(row.defMat))
                                    {
                                        AddMat(pool, EClass.sources.materials.alias[row.defMat], w);
                                    }


                                    if (!string.IsNullOrEmpty(item.material)
                                        && EClass.sources?.materials?.alias != null
                                        && EClass.sources.materials.alias.ContainsKey(item.material))
                                    {
                                        AddMat(pool, EClass.sources.materials.alias[item.material], w + 1);
                                    }

}
                            catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchRewards.cs silent catch: " + __e.Message); }
}
                    }
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchRewards.cs silent catch: " + __e.Message); }
}
    }



    static void AddMat(RegionLootPool pool, SourceMaterial.Row row, int weight)
    {
        if (pool == null || row == null || weight <= 0)
        {
            return;
        }


            string cat = (row.category ?? "").ToLowerInvariant();
            string alias = (row.alias ?? "").ToLowerInvariant();
            string thing = (row.thing ?? "").ToLowerInvariant();
            string name = "";
            try { name = (row.name ?? "").ToLowerInvariant(); } catch { name = ""; }

            // Global mud/soil ban in region material pools.
            if (IsMudLikeMaterial(alias, thing, name) || alias == "soil" || alias == "mud")
            {
                return;
            }

            // Skip pure water liquids.
            if (cat.Contains("liquid") && alias.Contains("water"))
            {
                return;
            }

            // Dye etc stay out. Bone is allowed but heavily downranked (useful, not a flood).
            if (IsJunkRegionMaterial(alias, thing, name, cat))
            {
                return;
            }

            // Prefer sand/common ores/wood; downrank gold/adamantite/grass; bone low.
            if (alias == "sand" || alias.Contains("sand"))
            {
                weight = Math.Max(weight, 18);
            }
            else if (alias == "copper")
            {
                weight = Math.Max(weight, 16);
            }
            else if (alias == "iron")
            {
                weight = Math.Max(weight, 14);
            }
            else if (alias == "bronze" || alias == "stone")
            {
                weight = Math.Max(weight, 8);
            }
            else if (alias == "gold" || alias == "platinum")
            {
                weight = Math.Min(weight, 2);
            }
            else if (alias == "adamantite")
            {
                weight = 1;
            }
            else if (IsGemAlias(alias))
            {
                weight = Math.Min(Math.Max(weight, 2), 4);
            }
            else if (IsBoneLike(alias, thing, name, cat))
            {
                // Keep bone in pool with small weight so it can appear without dominating.
                weight = Mathf.Clamp(Math.Max(1, weight / 5), 1, 4);
            }
            else if (alias.Contains("grass") || thing.Contains("grass") || alias.Contains("weed"))
            {
                weight = Math.Max(1, weight / 3);
            }
            else if (IsMudLikeMaterial(alias, thing, name))
            {
                // Defensive: already returned above; keep dead branch safe.
                return;
            }

            for (int i = 0; i < pool.materials.Count; i++)
            {
                if (pool.materials[i].matId == row.id)
                {
                    pool.materials[i].weight += weight;
                    return;
                }
            }

            pool.materials.Add(new RegionLootEntry
            {
                id = row.thing ?? "",
                matId = row.id,
                weight = weight,
            });

}


    static bool IsJunkRegionMaterial(string alias, string thing, string name, string cat)
    {
        string s = (alias + " " + thing + " " + name + " " + cat).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(s))
        {
            return true;
        }

        // Dye / blood / rubber / money stay banned. Bone is useful — kept with low weight in AddMat.
        if (s.Contains("dye") || s.Contains("染料")
            || s.Contains("blood") || s.Contains("血")
            || s.Contains("rubber") || s.Contains("money")
            || s.Contains("cash") || s.Contains("gold"))
        {
            return true;
        }

        // Thing id patterns for dye products only.
        if (thing.Contains("dye"))
        {
            return true;
        }

        return false;
    }


    static bool IsBoneLike(string alias, string thing, string name, string cat, string id = "")
    {
        string s = ((alias ?? "") + " " + (thing ?? "") + " " + (name ?? "") + " " + (cat ?? "") + " " + (id ?? "")).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        return s.Contains("bone") || s.Contains("骨") || s.Contains("skull") || s == " bone" || id == "bone" || id == "skull";
    }


    static bool IsBoneThing(Thing t)
    {
        if (t == null)
        {
            return false;
        }

        try
        {
            string id = (t.id ?? "").ToLowerInvariant();
            string nm = "";
            try { nm = (t.NameOne ?? t.NameSimple ?? t.Name ?? "").ToLowerInvariant(); } catch { nm = ""; }
            string cat = "";
            try
            {
                cat = t.category != null ? (t.category.id ?? "") : "";
                if (string.IsNullOrEmpty(cat) && t.source != null)
                {
                    cat = t.source.category ?? "";
                }
            }
            catch
            {
                cat = "";
            }

            string mat = "";
            try { mat = t.material != null ? (t.material.alias ?? "") : ""; } catch { mat = ""; }
            return IsBoneLike(mat, id, nm, cat, id);
        }
        catch
        {
            return false;
        }
    }


    static void ClassifyThingId(RegionLootPool pool, string id, int weight)
    {
        if (pool == null || string.IsNullOrEmpty(id) || weight <= 0)
        {
            return;
        }

        string low = id.ToLowerInvariant();
        if (IsForbiddenRegionId(low))
        {
            return;
        }

        // Bone is useful: keep it, but never let biome chance stack into a flood.
        if (IsBoneLike("", low, "", "", low))
        {
            weight = Mathf.Clamp(Math.Max(1, weight / 4), 1, 3);
        }

        bool fish = low.Contains("fish") || low.Contains("seafood") || low == "bait";
        bool harvest = low.Contains("bone") || low.Contains("skull")
            || low.Contains("herb") || low.Contains("mushroom") || low.Contains("berry")
            || low.Contains("fruit") || low.Contains("flower") || low.Contains("grass")
            || low.Contains("leaf") || low.Contains("seed") || low.Contains("nut")
            || low.Contains("ore") || low.Contains("log") || low.Contains("branch")
            || low.Contains("resin") || low.Contains("bark")
            || low.Contains("shell") || low.Contains("coral") || low.Contains("seaweed")
            || low.Contains("salt") || low.Contains("sand") || low.Contains("rock")
            || low.Contains("stone") || low.Contains("crystal") || low.Contains("gem")
            || low.Contains("wheat") || low.Contains("potato") || low.Contains("carrot")
            || low.Contains("cabbage") || low.Contains("corn") || low.Contains("rice")
            || low.Contains("egg") || low.Contains("meat") || low.Contains("feather")
            || low.Contains("hide") || low.Contains("skin") || low.Contains("wool")
            || low.Contains("cotton") || low.Contains("vine") || low.Contains("moss")
            || low.Contains("clay") || low.Contains("soil") || low.Contains("dirt")
            || low.Contains("pebble") || low.Contains("junk") || low.Contains("scrap")
            || low.Contains("material") || low.Contains("raw");

        List<RegionLootEntry> target;
        if (fish)
        {
            target = pool.fishIds;
        }
        else if (harvest)
        {
            target = pool.harvestIds;
        }
        else
        {
            if (low.Contains("sword") || low.Contains("armor") || low.Contains("chest")
                || low.Contains("statue") || low.Contains("furniture") || low.Contains("book")
                || low.Contains("spell") || low.Contains("potion") || low.Contains("bottle")
                || low.Contains("rubber") || low.Contains("duck") || low.Contains("toy")
                || low.Contains("doll") || low.Contains("ticket") || low.Contains("wine")
                || low.Contains("ale"))
            {
                return;
            }

            target = pool.otherIds;
        }

        for (int i = 0; i < target.Count; i++)
        {
            if (string.Equals(target[i].id, id, StringComparison.OrdinalIgnoreCase))
            {
                target[i].weight += weight;
                return;
            }
        }

        target.Add(new RegionLootEntry { id = id, weight = weight });
    }


    static bool IsForbiddenRegionId(string low)
    {
        if (string.IsNullOrEmpty(low))
        {
            return true;
        }

        if (low == "water" || low == "bottle" || low == "potion" || low.StartsWith("potion_")
            || low.Contains("rubber") || low.Contains("duck") || low.Contains("doll")
            || low == "cash" || low == "goldbar"
            || low.Contains("bill") || low.Contains("map") || low.Contains("deed")
            || low.Contains("ticket")
            || low.Contains("dye") || low.Contains("染料")
            || low.Contains("blood")
            // Cooked / processed food never belongs in wild region haul.
            || low.Contains("bread") || low.Contains("cake") || low.Contains("pie")
            || low.Contains("noodle") || low.Contains("soup") || low.Contains("stew")
            || low.Contains("cheese") || low.Contains("butter") || low.Contains("wine")
            || low.Contains("ale") || low.Contains("beer") || low.Contains("meal")
            || low.Contains("cooked") || low.Contains("dish") || low.Contains("jerky")
            || low.Contains("sausage"))
        {
            return true;
        }

        return false;
    }


    static bool IsForbiddenRegionThing(Thing t)
    {
        if (t == null)
        {
            return true;
        }

        try
        {
            // Lockpick currencies are intentional region/field chest loot.
            if (IsMoneyThing(t) && !IsLockpickCurrencyThing(t))
            {
                return true;
            }

            string id = (t.id ?? "").ToLowerInvariant();
            if (IsForbiddenRegionId(id))
            {
                return true;
            }

            // Protected region staples must never be treated as junk.

                int mid = LiveMaterialId(t);
                int seaId = SeaSandMaterialId();
                int copperId = CopperMaterialId();
                int ironId = IronMaterialId();
                if ((seaId > 0 && mid == seaId)
                    || (copperId > 0 && mid == copperId)
                    || (ironId > 0 && mid == ironId)
                    || IsSeaSandThing(t)
                    || IsSaltLikeThing(t)
                    || IsSulfurLikeThing(t))
                {
                    return false;
                }


            // Mud/soil is free diggable junk - never a region reward.
            if (IsMudLikeThing(t))
            {
                return true;
            }

            // Cooked food is never wild forage.
            try
            {
                string nmCook = (t.NameOne ?? t.NameSimple ?? t.Name ?? "").ToLowerInvariant();
                if (nmCook.Contains("面包") || nmCook.Contains("bread") || nmCook.Contains("蛋糕")
                    || nmCook.Contains("cheese") || nmCook.Contains("奶酪") || nmCook.Contains("料理")
                    || nmCook.Contains("wine") || nmCook.Contains("酒"))
                {
                    return true;
                }
            }
            catch
            {
            }

            if (IsScrapLikeThing(t))
            {
                return true;
            }


                string nm2 = (t.NameOne ?? t.NameSimple ?? t.Name ?? "").ToLowerInvariant();
                if (nm2.Contains("橡皮鸭") || (nm2.Contains("rubber") && nm2.Contains("duck")))
                {
                    return true;
                }


                string cat = t.category != null ? (t.category.id ?? "") : "";
                if (string.IsNullOrEmpty(cat) && t.source != null)
                {
                    cat = t.source.category ?? "";
                }

                string c = cat.ToLowerInvariant();
                if (c.Contains("drink") || c.Contains("potion") || c.Contains("bottle")
                    || c.Contains("dye"))
                {
                    return true;
                }


                string nm = (t.NameOne ?? t.NameSimple ?? t.Name ?? "").ToLowerInvariant();
                if (nm.Contains("染料") || nm.Contains("dye"))
                {
                    return true;
                }


                if (t.rarity >= Rarity.Legendary)
                {
                    return true;
                }

}
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchRewards.cs silent catch: " + __e.Message); }
return false;
    }


    static RegionLootEntry PickWeighted(List<RegionLootEntry> list)
    {
        if (list == null || list.Count == 0)
        {
            return new RegionLootEntry();
        }

        int total = 0;
        for (int i = 0; i < list.Count; i++)
        {
            total += Math.Max(1, list[i].weight);
        }

        int r = 0;
        try
        {
            r = EClass.rnd(Math.Max(1, total));
        }
        catch
        {
            r = 0;
        }

        int acc = 0;
        for (int i = 0; i < list.Count; i++)
        {
            acc += Math.Max(1, list[i].weight);
            if (r < acc)
            {
                return list[i];
            }
        }

        return list[list.Count - 1];
    }


    static Thing? CreateFromMaterialEntry(RegionLootEntry entry, int weeks, DungeonDispatchMission mission)
    {
        if (entry == null)
        {
            return null;
        }

        try
        {
            Thing? t = null;
            if (entry.matId >= 0
                && EClass.sources != null
                && EClass.sources.materials != null
                && EClass.sources.materials.map != null
                && EClass.sources.materials.map.ContainsKey(entry.matId))
            {
                SourceMaterial.Row row = EClass.sources.materials.map[entry.matId];
                string alias = row.alias ?? "";
                // Skip mud/soil materials entirely.
                if (!string.IsNullOrEmpty(alias) && IsMudLikeMaterial(alias, row.thing ?? "", row.name ?? ""))
                {
                    return null;
                }

                t = CreateMaterialThing(alias);
                if (t == null)
                {
                    try { t = ThingGen.CreateRawMaterial(row); }
                    catch { t = null; }
                }
            }

            if (t == null && !string.IsNullOrEmpty(entry.id))
            {
                // Never Create("ore") bare - high lv randomizes to gold.
                if (string.Equals(entry.id, "ore", StringComparison.OrdinalIgnoreCase))
                {
                    t = CreateWeightedOre(common: true);
                }
                else if (string.Equals(entry.id, "ore_gem", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(entry.id, "gem", StringComparison.OrdinalIgnoreCase))
                {
                    t = CreateMountainGem();
                }
                else
                {
                    t = TryCreate(entry.id, 1);
                }
            }

            if (t != null && IsMudLikeThing(t))
            {
                 if (t.parent == null) t.Destroy();
                return null;
            }

            if (t == null)
            {
                return null;
            }

            if (IsForbiddenRegionThing(t))
            {
                 if (t.parent == null) t.Destroy();
                return null;
            }

            ApplyRegionStack(t, weeks, mission, basic: true);
            return t;
        }
        catch
        {
            return null;
        }
    }


    static Thing? CreateFromThingId(RegionLootEntry entry, int weeks, DungeonDispatchMission mission, bool preferSpoil)
    {
        if (entry == null || string.IsNullOrEmpty(entry.id))
        {
            return null;
        }

        try
        {
            Thing? t = null;
            string idLow = (entry.id ?? "").ToLowerInvariant();
            if (idLow.StartsWith("meat:"))
            {
                t = CreateNamedMeat(idLow.Length > 5 ? idLow.Substring(5) : "putty");
            }
            else if (idLow == "sand" || idLow == "sand_sea" || idLow == "sea sand" || idLow == "seasand"
                || idLow == "sea_sand" || idLow == "sand_white" || idLow == "white sand"
                || idLow == "quartz sand" || idLow == "quartz_sand")
            {
                t = CreateBeachSand();
            }
            else if (idLow == "salt")
            {
                t = CreateBeachSalt();
            }
            else if (idLow == "sulfur" || idLow == "sulphur")
            {
                t = CreateMountainSulfur();
            }
            else if (idLow == "plastic")
            {
                t = CreatePlasticOre();
            }
            else if (idLow == "ore" || (idLow.StartsWith("ore_") && idLow != "ore_gem"))
            {
                t = CreateWeightedOre(common: true);
            }
            else if (idLow == "ore_gem" || idLow == "gem")
            {
                t = CreateMountainGem();
            }
            else if (idLow == "scrap" || idLow == "junk")
            {
                return null;
            }
            else if (idLow == "_meat" || idLow == "meat" || idLow == "meat_marble")
            {
                t = CreateNamedMeat("putty") ?? TryCreate(entry.id, 1);
            }
            else
            {
                t = TryCreate(entry.id, 1);
            }

            if (t == null)
            {
                return null;
            }

            if (IsForbiddenRegionThing(t))
            {
                 if (t.parent == null) t.Destroy();
                return null;
            }

            // Harvest produce should match PC-picked fresh default material, not oak/wood.
            ForceFreshProduceMaterial(t);

            // Region rewards: keep food/harvest fresh. preferSpoil is legacy and ignored here.
            ApplyRegionStack(t, weeks, mission, basic: true);
            KeepFoodFresh(t);

            return t;
        }
        catch
        {
            return null;
        }
    }


    static void SeedThingId(List<RegionLootEntry> list, string id, int weight)
    {
        if (list == null || string.IsNullOrEmpty(id) || weight <= 0)
        {
            return;
        }


            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].id, id, StringComparison.OrdinalIgnoreCase))
                {
                    list[i].weight = Math.Max(list[i].weight, weight);
                    return;
                }
            }

            list.Add(new RegionLootEntry { id = id, weight = weight, matId = -1 });

}


    static Thing? RollGatherDrop(DungeonDispatchMission mission, int lv)
    {
        return RollDungeonGatherNodeDrop(mission, lv)
            ?? TryCreate("mushroom", 1)
            ?? TryCreateFromCategorySafe("food", lv);
    }


    /// <summary>
    /// Simulated dungeon gather-node drop from the zone biome harvest/obj pool.
    /// </summary>
    static Thing? RollGatherNodeDrop(DungeonDispatchMission mission, int lv)
    {
        return RollDungeonGatherNodeDrop(mission, lv);
    }


    /// <summary>
    /// Dungeon ground scatter: target-zone biome floor item/material pool.
    /// No forced sulfur / gold ore / scrap staples.
    /// </summary>
    static Thing? RollDungeonGroundDrop(DungeonDispatchMission mission, int lv)
    {
        try
        {
            // Vanilla dungeon floor scatter: GenRoom uses
            // ThingGen.CreateFromFilter(zone.biome.spawn.GetRandomThingId(), DangerLv).
            for (int attempt = 0; attempt < 6; attempt++)
            {
                Thing? vanilla = CreateVanillaDungeonGroundThing(mission, lv);
                if (vanilla == null)
                {
                    continue;
                }

                if (IsBannedDungeonStapleThing(vanilla) || IsBannedRewardMushroom(vanilla))
                {
                    if (vanilla.parent == null) vanilla.Destroy();
                    continue;
                }

                ForceFreshProduceMaterial(vanilla);
                KeepFoodFresh(vanilla);
                return vanilla;
            }

            // Soft secondary: biome harvest/other still ok if spawn list is thin.
            RegionLootPool pool = GetDungeonLootPool(mission);
            if (pool != null && !pool.IsEmpty)
            {
                if (pool.otherIds.Count > 0)
                {
                    Thing? other = CreateDungeonFromThingId(PickWeighted(pool.otherIds), lv);
                    if (other != null && !IsBannedDungeonStapleThing(other) && !IsBannedRewardMushroom(other))
                    {
                        return other;
                    }
                }

                if (pool.materials.Count > 0)
                {
                    Thing? mat = CreateDungeonFromMaterialEntry(PickWeighted(pool.materials), lv);
                    if (mat != null && !IsBannedDungeonStapleThing(mat))
                    {
                        return mat;
                    }
                }
            }

            return FallbackDungeonGround(lv);
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("roll dungeon ground: " + ex.Message);
            return FallbackDungeonGround(lv);
        }
    }

    /// <summary>
    /// Mirror GenRoom.OnPopulate ground loot: CreateFromFilter(biome.spawn.GetRandomThingId(), lv).
    /// </summary>
    static Thing? CreateVanillaDungeonGroundThing(DungeonDispatchMission? mission, int lv)
    {
        try
        {
            int danger = Math.Max(1, lv);
            string? filterId = null;

            try
            {
                Zone? zone = null;
                try { zone = mission?.GetZone(); } catch { zone = null; }
                if (zone != null)
                {
                    try { zone = zone.GetTopZone() ?? zone; } catch { }
                }

                BiomeProfile? biome = null;
                try { biome = zone?.biome; } catch { biome = null; }
                if (biome == null)
                {
                    var profiles = ResolveDungeonBiomeProfiles(mission);
                    if (profiles != null && profiles.Count > 0)
                    {
                        biome = profiles[0];
                    }
                }

                if (biome?.spawn != null)
                {
                    try { filterId = biome.spawn.GetRandomThingId(); } catch { filterId = null; }
                }
            }
            catch
            {
                filterId = null;
            }

            if (string.IsNullOrEmpty(filterId))
            {
                filterId = "dungeon";
            }

            // Avoid pure junk filter as the primary path.
            if (string.Equals(filterId, "junk", StringComparison.OrdinalIgnoreCase))
            {
                filterId = "dungeon";
            }

            Thing? t = null;
            try { t = ThingGen.CreateFromFilter(filterId, danger); } catch { t = null; }
            if (t == null)
            {
                try { t = ThingGen.CreateFromFilter("dungeon", danger); } catch { t = null; }
            }

            if (t == null)
            {
                // Equipment / material filters still look like real floor finds.
                t = TryCreateFromFilter("eq", danger)
                    ?? TryCreateFromFilter("equipment", danger)
                    ?? TryCreateFromFilter("material", danger);
            }

            return t;
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("vanilla dungeon ground: " + ex.Message);
            return null;
        }
    }


    /// <summary>
    /// Dungeon gather nodes: harvest/obj pool from the target zone biome.
    /// </summary>
    static Thing? RollDungeonGatherNodeDrop(DungeonDispatchMission mission, int lv)
    {
        try
        {
            RegionLootPool pool = GetDungeonLootPool(mission);
            if (pool != null && pool.harvestIds.Count > 0)
            {
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    Thing? t = CreateDungeonFromThingId(PickWeighted(pool.harvestIds), lv);
                    if (t == null || IsBannedDungeonStapleThing(t))
                    {
                        continue;
                    }

                     KeepFoodFresh(t);
                    return t;
                }
            }

            // Soft plant/food only; never scrap/ore fallback.
            return TryCreateFromCategorySafe("plant", lv)
                ?? TryCreateFromCategorySafe("food", lv)
                ?? TryCreate("mushroom", 1)
                ?? TryCreate("berry", 1);
        }
        catch
        {
            return TryCreate("mushroom", 1) ?? TryCreate("berry", 1);
        }
    }


    static Thing? FallbackDungeonGround(int lv)
    {
        // Mild non-staple fallback only. Never sulfur/gold ore/scrap/junk lottery.
        int danger = Math.Max(1, lv);
        Thing? t = TryCreateFromFilter("dungeon", danger)
            ?? TryCreateFromFilter("eq", danger)
            ?? TryCreateFromFilter("equipment", danger)
            ?? TryCreateFromFilter("material", danger)
            ?? TryCreate("mushroom", 1)
            ?? TryCreateFromCategorySafe("food", danger);
        if (t != null && (IsBannedDungeonStapleThing(t) || IsBannedRewardMushroom(t)))
        {
            if (t.parent == null) t.Destroy();
            t = TryCreate("mushroom", 1) ?? TryCreate("berry", 1);
        }

        return t;
    }


    static RegionLootPool GetDungeonLootPool(DungeonDispatchMission mission)
    {
        string cacheKey = BuildDungeonPoolCacheKey(mission);
        if (DungeonPoolCache.TryGetValue(cacheKey, out RegionLootPool? cached) && cached != null)
        {
            return cached;
        }

        var pool = new RegionLootPool();

            ScanBiomeIntoPool(pool, ResolveDungeonBiomeProfiles(mission));


        SeedDungeonFallback(pool);
        PruneDungeonPool(pool);
        DungeonPoolCache[cacheKey] = pool;
        return pool;
    }


    static string BuildDungeonPoolCacheKey(DungeonDispatchMission mission)
    {
        try
        {
            string biome = "";
            try
            {
                Zone? z = mission?.GetZone();
                biome = z?.IdBiome ?? z?.biome?.name ?? "";
            }
            catch
            {
                biome = "";
            }

            return "d23|"
                + (mission?.uidZone ?? 0) + "|"
                + (mission?.zoneId ?? "") + "|"
                + biome + "|"
                + Math.Max(1, mission?.dangerLv ?? 1);
        }
        catch
        {
            return "d22|0";
        }
    }


    static List<BiomeProfile> ResolveDungeonBiomeProfiles(DungeonDispatchMission mission)
    {
        var list = new List<BiomeProfile>();
        try
        {
            Zone? zone = null;
            try { zone = mission?.GetZone(); } catch { zone = null; }
            if (zone != null)
            {

                    zone = zone.GetTopZone() ?? zone;

}

            BiomeProfile? fromZone = null;
            try { fromZone = zone?.biome; } catch { fromZone = null; }
            if (fromZone != null)
            {
                list.Add(fromZone);
            }


                string idBiome = zone?.IdBiome ?? "";
                BiomeProfile? bp = LookupBiome(idBiome);
                if (bp != null && !list.Contains(bp))
                {
                    list.Add(bp);
                }


                string srcBiome = zone?.source?.idBiome ?? "";
                BiomeProfile? bp2 = LookupBiome(srcBiome);
                if (bp2 != null && !list.Contains(bp2))
                {
                    list.Add(bp2);
                }

}
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchRewards.cs silent catch: " + __e.Message); }
// Mild generic dungeon fallbacks if zone biome is missing.
        foreach (string id in new[] { "Default", "Cave", "Barren", "Forest" })
        {
            BiomeProfile? bp = LookupBiome(id);
            if (bp != null && !list.Contains(bp))
            {
                list.Add(bp);
            }
        }

        return list;
    }


    static void SeedDungeonFallback(RegionLootPool pool)
    {
        if (pool == null)
        {
            return;
        }

        // Light common dungeon fill only when biome scan is thin.
        // Intentionally NO sulfur / gold / scrap seeds.
        SeedMatAlias(pool, "stone", 8);
        SeedMatAlias(pool, "copper", 4);
        SeedMatAlias(pool, "iron", 3);
        SeedMatAlias(pool, "crystal", 2);
        SeedThingId(pool.harvestIds, "mushroom", 8);
        SeedThingId(pool.harvestIds, "berry", 4);
        SeedThingId(pool.harvestIds, "herb", 3);
        SeedThingId(pool.otherIds, "bone", 2);
    }


    static void PruneDungeonPool(RegionLootPool pool)
    {
        if (pool == null)
        {
            return;
        }


            void PruneList(List<RegionLootEntry> list, bool materials)
            {
                if (list == null)
                {
                    return;
                }

                for (int i = list.Count - 1; i >= 0; i--)
                {
                    RegionLootEntry e = list[i];
                    if (e == null)
                    {
                        list.RemoveAt(i);
                        continue;
                    }

                    string id = (e.id ?? "").ToLowerInvariant();
                    string alias = id;
                    if (materials && e.matId >= 0
                        && EClass.sources?.materials?.map != null
                        && EClass.sources.materials.map.ContainsKey(e.matId))
                    {
                        try { alias = (EClass.sources.materials.map[e.matId].alias ?? id).ToLowerInvariant(); }
                        catch { alias = id; }
                    }

                    bool drop = false;
                    if (id == "scrap" || id.Contains("scrap") || id == "microchip"
                        || id.Contains("rubber") || id.Contains("duck") || id.Contains("doll")
                        || id.Contains("toy"))
                    {
                        drop = true;
                    }

                    // These were the broken fixed dungeon staples from generic material/ore filters.
                    if (alias == "sulfur" || alias == "sulphur" || id == "sulfur" || id == "sulphur"
                        || alias == "gold" || alias == "platinum" || alias == "adamantite"
                        || alias == "elder gold" || id.Contains("goldbar")
                        || id == "scrap")
                    {
                        drop = true;
                    }

                    if (drop)
                    {
                        list.RemoveAt(i);
                    }
                }
            }

            PruneList(pool.materials, materials: true);
            PruneList(pool.harvestIds, materials: false);
            PruneList(pool.otherIds, materials: false);
            PruneList(pool.fishIds, materials: false);

}


    static bool IsBannedDungeonStapleThing(Thing t)
    {
        if (t == null)
        {
            return true;
        }

        try
        {
            if (IsScrapLikeThing(t) || IsGoldLikeThing(t) || IsMudLikeThing(t))
            {
                return true;
            }

            string id = "";
            try { id = (t.id ?? "").ToLowerInvariant(); } catch { id = ""; }
            string mat = "";
            try { mat = t.material != null ? (t.material.alias ?? "").ToLowerInvariant() : ""; } catch { mat = ""; }
            string nm = "";
            try { nm = (t.NameOne ?? t.NameSimple ?? t.Name ?? "").ToLowerInvariant(); } catch { nm = ""; }

            // Explicit ban for the three broken fixed staples.
            if (id == "sulfur" || id == "sulphur" || mat == "sulfur" || mat == "sulphur"
                || nm.Contains("\u786b\u78fa") || nm.Contains("sulfur") || nm.Contains("sulphur"))
            {
                return true;
            }

            if (nm.Contains("\u91d1\u77ff") || nm.Contains("gold ore") || nm.Contains("\u5e9f\u94c1"))
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


    static Thing? CreateDungeonFromMaterialEntry(RegionLootEntry entry, int lv)
    {
        if (entry == null)
        {
            return null;
        }

        try
        {
            Thing? t = null;
            if (entry.matId >= 0
                && EClass.sources?.materials?.map != null
                && EClass.sources.materials.map.ContainsKey(entry.matId))
            {
                SourceMaterial.Row row = EClass.sources.materials.map[entry.matId];
                string alias = row.alias ?? "";
                if (!string.IsNullOrEmpty(alias) && IsMudLikeMaterial(alias, row.thing ?? "", row.name ?? ""))
                {
                    return null;
                }

                if (IsForbiddenHighOreMaterial(alias)
                    || alias.Equals("sulfur", StringComparison.OrdinalIgnoreCase)
                    || alias.Equals("sulphur", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                t = CreateMaterialThing(alias);
                if (t == null)
                {
                    try { t = ThingGen.CreateRawMaterial(row); } catch { t = null; }
                }
            }

            if (t == null && !string.IsNullOrEmpty(entry.id))
            {
                string idLow = entry.id.ToLowerInvariant();
                if (idLow == "ore" || (idLow.StartsWith("ore_") && idLow != "ore_gem"))
                {
                    t = CreateWeightedOre(common: true);
                }
                else if (idLow == "ore_gem" || idLow == "gem")
                {
                    t = CreateMountainGem();
                }
                else if (idLow == "scrap" || idLow == "junk" || idLow == "sulfur" || idLow == "sulphur")
                {
                    return null;
                }
                else
                {
                    t = TryCreate(entry.id, Math.Max(1, lv));
                }
            }

            if (t == null || IsBannedDungeonStapleThing(t))
            {
                 if (t != null && t.parent == null) t.Destroy();
                return null;
            }

            return t;
        }
        catch
        {
            return null;
        }
    }


    static Thing? CreateDungeonFromThingId(RegionLootEntry entry, int lv)
    {
        if (entry == null || string.IsNullOrEmpty(entry.id))
        {
            return null;
        }

        try
        {
            string idLow = entry.id.ToLowerInvariant();
            if (idLow == "scrap" || idLow == "junk" || idLow == "sulfur" || idLow == "sulphur"
                || idLow.Contains("goldbar"))
            {
                return null;
            }

            if (idLow == "ore" || (idLow.StartsWith("ore_") && idLow != "ore_gem"))
            {
                Thing? ore = CreateWeightedOre(common: true);
                if (ore != null && !IsBannedDungeonStapleThing(ore))
                {
                    return ore;
                }

                return null;
            }

            if (idLow == "ore_gem" || idLow == "gem")
            {
                return CreateMountainGem();
            }

            Thing? t = TryCreate(entry.id, Math.Max(1, lv));
            if (t == null || IsBannedDungeonStapleThing(t))
            {
                 if (t != null && t.parent == null) t.Destroy();
                return null;
            }

             ForceFreshProduceMaterial(t);
             KeepFoodFresh(t);
            return t;
        }
        catch
        {
            return null;
        }
    }


    static Thing? RollLockpickChestCurrency(DungeonDispatchMission mission)
    {
        // Kept as a thin last-resort helper. Prefer treasure re-roll at call sites.
        try
        {
            int lockpick = Math.Max(0, mission?.lockpickSkill ?? 0);
            int n = Mathf.Clamp(8 + lockpick / 6, 6, 40);
            try
            {
                Thing? c = ThingGen.CreateCurrency(n);
                if (c != null) return c;
            }
            catch
            {
            }

            Thing? t = TryCreate("money", 1);
            if (t != null)
            {
                try { t.SetNum(n); } catch { }
            }
            return t;
        }
        catch
        {
            return TryCreate("money", 1);
        }
    }


    static bool IsRegionFoodishThing(Thing t)
    {
        if (t == null)
        {
            return false;
        }

        if (IsForestFoodThing(t))
        {
            return true;
        }


            string id = (t.id ?? "").ToLowerInvariant();
            string nm = "";
            try { nm = (t.NameOne ?? t.NameSimple ?? t.Name ?? "").ToLowerInvariant(); } catch { nm = ""; }
            if (id.Contains("meat") || id.Contains("fish") || id.Contains("egg")
                || id.Contains("milk") || id.Contains("bread") || id.Contains("cheese")
                || id.Contains("fruit") || id.Contains("mushroom") || id.Contains("berry")
                || id.Contains("grape") || id.Contains("apple") || id.Contains("banana")
                || id.Contains("orange") || id.Contains("peach") || id.Contains("palulu")
                || id == "699" || id == "700")
            {
                return true;
            }

            if (nm.Contains("meat") || nm.Contains("肉") || nm.Contains("菜")
                || nm.Contains("果") || nm.Contains("蘑") || nm.Contains("food"))
            {
                return true;
            }

            // Flowers count as foodish forage for plain gather budget.
            if (id.Contains("flower") || nm.Contains("花") || nm.Contains("flower"))
            {
                return true;
            }

return false;
    }
}
