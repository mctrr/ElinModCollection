using System;
using System.Collections.Generic;
using UnityEngine;

namespace NpcLabor.Dispatch;

internal enum DispatchSettleKind
{
    Success,
    Failure,
    Recall,
}

/// <summary>
/// PARTIAL: reward settlement + shared helpers.
/// Split from DungeonDispatchRewards.cs (2026-08-07).
/// </summary>
internal static partial class DungeonDispatchRewards
{
    internal static void Deliver(
        FactionBranch branch,
        DungeonDispatchMission mission,
        DispatchSettleKind kind)
    {
        if (branch == null || mission == null)
        {
            return;
        }

        try
        {
            mission.EnsureMemberList();
            // Shared rule: no loot until spent hours > travel/?? hours.
            // Region mail qty must equal task-details qty (HarvestProgressPercent).
            float harvest = Mathf.Clamp01(mission.HarvestProgressPercent / 100f);
            // Region: success full plan; fail/recall harvest only.
            // Dungeon: natural complete always pays full ground haul; success/fail only gates boss or fixed extras.
            // Manual recall still pays current harvest only.
            float exploreScale = kind switch
            {
                DispatchSettleKind.Success => 1f,
                DispatchSettleKind.Failure => mission.isRegion ? harvest : 1f,
                DispatchSettleKind.Recall => harvest,
                _ => harvest,
            };

            var all = new List<Thing>();

            if (mission.isRegion)
            {
                float regionScale = kind == DispatchSettleKind.Success ? 1f : harvest;
                // Travel phase + recall/fail => empty. Success always full locked plan.
                if (regionScale > 0.0001f)
                {
                    if (!TryTakeRegionPlanThings(mission, all, regionScale))
                    {
                        // Last resort: rebuild exact tracker lines only (never re-roll plan).
                        TryBuildFromLootLog(mission, all);
                    }
                }
            }
            else
            {
                // Ground scatter always on natural complete (success or fail).
                // Recall still scales by harvest progress / travel gate.
                if (exploreScale > 0.0001f)
                {
                    CollectExploreThings(mission, exploreScale, all);
                    CollectDungeonGatherThings(mission, exploreScale, all);
                    CollectLockpickChestThings(mission, exploreScale, all);
                    // Small danger-scaled mineral packet (not sulfur/gold/scrap flood).
                    AddDungeonOrePacket(mission, all, exploreScale);
                }

                // Boss / fixed-dungeon extras only on success.
                if (kind == DispatchSettleKind.Success)
                {
                    if (mission.isRandomSite)
                    {
                        Thing? boss = BuildBossSimChest(mission);
                        if (boss != null)
                        {
                            FlattenInto(all, boss, keepContainer: false);
                            NoteLoot(mission, boss, "Boss");
                        }
                    }
                    else
                    {
                        CollectFixedDungeonThings(mission, all);
                    }
                }
            }

            // Keep lockpick currencies (money / money2 / medal); strip accidental cash/goldbar.
            StripMoney(all, keepLockpickCurrency: true);
            if (mission.isRegion)
            {
                StripRegionJunk(all);
            }

            // v15: hard beach/mountain staples AFTER junk strips (alias-resolved mat ids) so created items cannot be eaten.
            if (mission.isRegion)
            {
                float regionScaleFinal = kind == DispatchSettleKind.Success
                    ? 1f
                    : Mathf.Clamp01(mission.HarvestProgressPercent / 100f);
                if (regionScaleFinal > 0.0001f)
                {
                    EnforceRegionDeliverContract(mission, all, regionScaleFinal);
                    // Contract items must survive final strip too (still keep lockpick currency).
                    StripMoney(all, keepLockpickCurrency: true);
                    StripRegionJunk(all);
                }
            }

            // Only non-region empty parcels get a mild consolation from the dungeon pool.
            // Region early-recall during travel must stay empty (no free loot).
            if (all.Count == 0 && !mission.isRegion)
            {
                Thing? fallback = RollDungeonGroundDrop(mission, RewardLv(mission))
                    ?? TryCreate("mushroom", 1)
                    ?? TryCreateFromFilter("dungeon", RewardLv(mission)) ?? TryCreateFromFilter("eq", RewardLv(mission));
                if (fallback != null)
                {
                    all.Add(fallback);
                }
            }

            // Merge identical stacks so "10+9 帕露露" becomes one 19 stack in the parcel.
            CompactStacks(all);
            // Region planned totals are the contract; do not soft-cap after plan rebuild.

            if (all.Count == 0)
            {
                // Region travel-phase recall/fail: nothing earned yet. Still OK.
                if (mission.isRegion)
                {
                    mission.EnsureMemberList();
                    mission.lootLog.Clear();
                    Plugin.LogInfo("dispatch deliver empty (no harvest yet) mission=" + mission.missionId
                        + " harvestPct=" + mission.HarvestProgressPercent);
                    return;
                }

                Plugin.LogWarn("dispatch deliver empty after strip mission=" + mission.missionId);
                return;
            }

            // Final harvest list must match mailbox contents (not mid-run fake previews).
            ReplaceLootLogFromThings(mission, all);

            // Fixed player-facing package name (region + dungeon): 派遣收获 / Dispatch harvest.
            string title = kind switch
            {
                DispatchSettleKind.Success => NpcLabor.LaborText.T("dis.parcel.harvest"),
                DispatchSettleKind.Failure => NpcLabor.LaborText.T("dis.parcel.partial"),
                DispatchSettleKind.Recall => NpcLabor.LaborText.T("dis.parcel.recall"),
                _ => NpcLabor.LaborText.T("dis.parcel.harvest"),
            };

            Thing? pack = BuildRewardParcel(title, all);
            if (pack == null)
            {
                Plugin.LogWarn("dispatch deliver parcel null mission=" + mission.missionId + " items=" + all.Count);
                return;
            }

            if (!TryPutInHomeMail(branch, mission, pack))
            {
                Plugin.LogWarn("dispatch deliver mailbox failed mission=" + mission.missionId);
                return;
            }

            Plugin.LogInfo("dispatch deliver ok kind=" + kind + " mission=" + mission.missionId
                + " items=" + all.Count + " pack=" + SafeThingName(pack));
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("dispatch reward deliver failed: " + ex.Message);
        }
    }


    /// <summary>
    /// Hourly hook while mission runs.
    /// Region missions lock a planned final haul once, but tracker only shows the
    /// progress-scaled portion. Full plan is delivered only on settle.
    /// </summary>
    internal static void TickPartialLoot(FactionBranch? branch, DungeonDispatchMission mission)
    {
        if (mission == null)
        {
            return;
        }


            mission.EnsureMemberList();
            mission.partialLootTicks++;
            if (!mission.isRegion)
            {
                return;
            }

            // Keep the locked plan warm, but do not rewrite lootLog every hour.
            // Region tracker no longer displays live haul; mail settles from planned entries.
            EnsureRegionPlan(mission);

    }


    static void NoteLoot(DungeonDispatchMission mission, Thing thing, string tag)
    {

            mission.EnsureMemberList();
            string name = FormatThingEntry(thing);
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            mission.lootLog.Add("[" + tag + "] " + name);
            if (mission.lootLog.Count > 40)
            {
                mission.lootLog.RemoveAt(0);
            }

}


    /// <summary>
    /// Replace tracker harvest with the exact compacted stacks that go into the mailbox.
    /// </summary>
    static void ReplaceLootLogFromThings(DungeonDispatchMission mission, List<Thing> things)
    {
        if (mission == null)
        {
            return;
        }


            mission.EnsureMemberList();
            mission.lootLog.Clear();
            if (things == null)
            {
                return;
            }

            for (int i = 0; i < things.Count; i++)
            {
                string entry = FormatThingEntry(things[i]);
                if (!string.IsNullOrEmpty(entry))
                {
                    mission.lootLog.Add(entry);
                }
            }

    }


    static string FormatThingEntry(Thing t)
    {
        if (t == null)
        {
            return "";
        }

        try
        {
            string name = "";
            try
            {
                // NameOne omits stack suffix so LootSummary can append xN cleanly.
                name = t.NameOne ?? "";
            }
            catch
            {
                name = "";
            }

            if (string.IsNullOrEmpty(name))
            {
                try { name = t.NameSimple ?? ""; } catch { name = ""; }
            }

            if (string.IsNullOrEmpty(name))
            {
                try { name = t.Name ?? t.id ?? NpcLabor.LaborText.T("dis.loot.item"); } catch { name = NpcLabor.LaborText.T("dis.loot.item"); }
            }

            int num = 1;
            try
            {
                num = Math.Max(1, t.Num);
            }
            catch
            {
                num = 1;
            }

            return num > 1 ? (name + "x" + num) : name;
        }
        catch
        {
            return SafeThingName(t);
        }
    }


    static string SafeThingName(Thing t)
    {
        try
        {
            return t?.Name ?? t?.id ?? NpcLabor.LaborText.T("dis.loot.item");
        }
        catch
        {
            return NpcLabor.LaborText.T("dis.loot.item");
        }
    }


    static void CollectExploreThings(DungeonDispatchMission mission, float scale, List<Thing> things)
    {
        try
        {
            if (mission.isRegion)
            {
                // Region haul is fully owned by the planned CollectRegionThings path.
                return;
            }

            // Dungeon explore = ground-item pool of the target zone biome.
            // Quantity from explore skill; never force sulfur/gold ore/scrap staples.
            int danger = Math.Max(1, mission.dangerLv);
            int lv = RewardLv(mission);
            float s = Mathf.Clamp(scale, 0f, 1f);
            if (s <= 0.0001f)
            {
                return;
            }

            int members = Math.Max(1, mission.MemberCount);
            int explore = Math.Max(0, mission.exploreSkill);
            int target = PlannedExploreHaulTotal(mission);
            // Weak parties still leave a few piles; strong explore fills the floor.
            target = Mathf.Max(target, 8 + members * 3 + explore / 10);
            int want = Mathf.Max(1, Mathf.RoundToInt(target * s));
            // Player wants a light haul, not a backpack: ~1/8 of a large bag.
            // danger adds a little headroom but stays a handful of piles.
            int haulCap = Mathf.Clamp(4 + danger / 30, 4, 8);
            want = Mathf.Clamp(want, 1, haulCap);

            int safety = 0;
            int maxRolls = Mathf.Clamp(want * 2 + 16, 16, 240);
            int before = things.Count;
            while ((CountThingNums(things) - CountThingNumsRange(things, before)) < want && safety < maxRolls)
            {
                safety++;
                int have = CountThingNums(things) - CountThingNumsRange(things, before);
                int left = want - have;
                if (left <= 0)
                {
                    break;
                }

                Thing? pile = RollDungeonGroundDrop(mission, lv);
                if (pile == null)
                {
                    continue;
                }

                // Gear/books: never stack, always 1-item, and reject if LV/price far above dangerLv.
                bool isGearOrBook = false;
                try
                {
                    isGearOrBook = pile.IsEquipment
                        || (pile.category != null && (pile.category.id ?? "") == "spellbook")
                        || (pile.id ?? "").StartsWith("book_");
                }
                catch { }

                if (isGearOrBook)
                {
                    int cardLv = 0;
                    try { cardLv = pile.LV; } catch { }
                    int priceGate = 0;
                    try { priceGate = pile.GetPrice(CurrencyType.Money, sell: false); } catch { }
                    // dangerLv * 1.5 soft cap on card LV; danger*200+500 oren soft cap on price.
                    if (cardLv > danger * 3 / 2 || priceGate > danger * 200 + 500)
                    {
                        if (pile.parent == null) pile.Destroy();
                        continue;
                    }
                }

                int n = 1;
                try { n = Math.Max(1, pile.Num); } catch { n = 1; }

                // Keep piles looking like ground scatter: mostly small stacks.
                int maxStack = left <= 3 ? left : Mathf.Clamp(2 + explore / 40 + danger / 25, 2, 8);
                if (isGearOrBook) maxStack = 1;
                if (n <= 1)
                {
                    try { n = 1 + (EClass.rnd(Math.Max(1, maxStack))); } catch { n = Math.Min(3, maxStack); }
                }

                n = Mathf.Clamp(n, 1, Math.Min(maxStack, left));
                 pile.SetNum(n);

                things.Add(pile);
            }

            if (things.Count == before)
            {
                Thing? fallback = RollDungeonGroundDrop(mission, lv)
                    ?? TryCreateFromFilter("dungeon", lv)
                    ?? TryCreateFromFilter("eq", lv)
                    ?? TryCreate("mushroom", 1);
                if (fallback != null)
                {
                    things.Add(fallback);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("collect explore failed: " + ex.Message);
        }
    }


    /// <summary>
    /// Small danger-scaled mineral packet (10-30 total, material tier rises with dangerLv).
    /// Never produces sulfur, gold ore, or scrap. Region dispatch is excluded.
    /// </summary>
    static void AddDungeonOrePacket(DungeonDispatchMission mission, List<Thing> things, float scale)
    {
        try
        {
            if (mission == null || things == null || mission.isRegion)
            {
                return;
            }

            float s = Mathf.Clamp(scale, 0f, 1f);
            if (s <= 0.0001f)
            {
                return;
            }

            int danger = Math.Max(1, mission.dangerLv);
            int total = Mathf.Clamp(1 + EClass.rnd(15), 1, 15);
            total = Mathf.RoundToInt(total * s);
            if (total <= 0)
            {
                return;
            }

            Thing? ore = CreateWeightedOreDangerScaled(danger);
            if (ore == null)
            {
                return;
            }
            ore.SetNum(total);
            things.Add(ore);
            NoteLoot(mission, ore, "Ore");
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("add dungeon ore packet failed: " + ex.Message);
        }
    }


    /// <summary>
    /// Dungeon gather-node loot only. Quantity from gather skill; not mixed into explore budget.
    /// </summary>
    static void CollectDungeonGatherThings(DungeonDispatchMission mission, float scale, List<Thing> things)
    {
        try
        {
            if (mission == null || things == null || mission.isRegion)
            {
                return;
            }

            float s = Mathf.Clamp(scale, 0f, 1f);
            if (s <= 0.0001f)
            {
                return;
            }

            int danger = Math.Max(1, mission.dangerLv);
            int lv = RewardLv(mission);
            int gather = Math.Max(0, mission.gatherSkill);
            int target = PlannedGatherHaulTotal(mission);
            if (target <= 0)
            {
                return;
            }

            int want = Mathf.Max(0, Mathf.RoundToInt(target * s));
            // Gather is a separate budget; cap to a few piles (not a second backpack).
            int gatherCap = Mathf.Clamp(2 + danger / 40, 2, 4);
            want = Mathf.Clamp(want, 0, gatherCap);
            if (want <= 0)
            {
                return;
            }

            int safety = 0;
            int maxRolls = Mathf.Clamp(want * 2 + 8, 8, 160);
            int before = things.Count;
            while ((CountThingNums(things) - CountThingNumsRange(things, before)) < want && safety < maxRolls)
            {
                safety++;
                int have = CountThingNums(things) - CountThingNumsRange(things, before);
                int left = want - have;
                if (left <= 0)
                {
                    break;
                }

                Thing? pile = RollDungeonGatherNodeDrop(mission, lv);
                if (pile == null)
                {
                    continue;
                }

                // Gear/books: never stack, always 1-item, reject if LV/price far above dangerLv.
                bool isGearOrBook = false;
                try
                {
                    isGearOrBook = pile.IsEquipment
                        || (pile.category != null && (pile.category.id ?? "") == "spellbook")
                        || (pile.id ?? "").StartsWith("book_");
                }
                catch { }

                if (isGearOrBook)
                {
                    int cardLv = 0;
                    try { cardLv = pile.LV; } catch { }
                    int priceGate = 0;
                    try { priceGate = pile.GetPrice(CurrencyType.Money, sell: false); } catch { }
                    if (cardLv > danger * 3 / 2 || priceGate > danger * 200 + 500)
                    {
                        if (pile.parent == null) pile.Destroy();
                        continue;
                    }
                }

                int n = 1;
                try { n = Math.Max(1, pile.Num); } catch { n = 1; }
                int maxStack = left <= 3 ? left : Mathf.Clamp(2 + gather / 35 + danger / 30, 2, 6);
                if (isGearOrBook) maxStack = 1;
                if (n <= 1)
                {
                    try { n = 1 + (EClass.rnd(Math.Max(1, maxStack))); } catch { n = Math.Min(2, maxStack); }
                }

                n = Mathf.Clamp(n, 1, Math.Min(maxStack, left));
                 pile.SetNum(n);
                 KeepFoodFresh(pile);
                things.Add(pile);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("collect dungeon gather failed: " + ex.Message);
        }
    }
    /// <summary>
    /// Simulated lockpick chest opens.
    /// Build a treasure chest, extract contents into the same parcel via FlattenInto,
    /// and never mail the chest body itself.
    /// </summary>
    static void CollectLockpickChestThings(DungeonDispatchMission mission, float scale, List<Thing> things)
    {
        if (mission == null || things == null)
        {
            return;
        }

        float s = Mathf.Clamp(scale, 0f, 1f);
        if (s <= 0.0001f)
        {
            return;
        }

        int chests = PlannedLockpickChestCount(mission);
        if (chests <= 0)
        {
            return;
        }

        int open = Mathf.Max(0, Mathf.RoundToInt(chests * s));
        if (open <= 0 && chests > 0 && s >= 0.5f)
        {
            open = 1;
        }

        open = Mathf.Clamp(open, 0, 16);
        for (int i = 0; i < open; i++)
        {
            int before = things.Count;
            Thing? chest = null;
            try
            {
                int lv = mission.isRegion
                    ? Math.Max(1, 5 + mission.lockpickSkill / 10)
                    : Math.Max(1, RewardLv(mission));

                try
                {
                    // Real chest card ids: chest3 / chest_treasure (bare "chest" does not exist).
                    chest = ThingGen.CreateTreasure("chest3", lv, TreasureType.RandomChest);
                }
                catch
                {
                    chest = null;
                }

                if (chest == null)
                {
                    try { chest = ThingGen.CreateTreasure("chest_treasure", lv, TreasureType.RandomChest); }
                    catch { chest = null; }
                }

                if (chest == null)
                {
                    try
                    {
                        chest = ThingGen.Create("chest3", -1, lv)
                            ?? ThingGen.Create("chest_treasure", -1, lv);
                        if (chest != null)
                        {
                            ThingGen.CreateTreasureContent(chest, lv, TreasureType.RandomChest, clearContent: true);
                        }
                    }
                    catch
                    {
                        chest = null;
                    }
                }

                if (chest != null)
                {
                    // Extract contents only; discard the empty chest body.
                    FlattenInto(things, chest, keepContainer: false);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("lockpick chest extract: " + ex.Message);
            }
            finally
            {
                try
                {
                    if (chest != null && chest.parent == null && !chest.isDestroyed)
                    {
                        chest.Destroy();
                    }
                }
                catch
                {
                }
            }

            if (things.Count > before)
            {
                for (int j = before; j < things.Count; j++)
                {
                    NoteLoot(mission, things[j], "Lockpick");
                }
            }
            else
            {
                // Fallback: re-roll one treasure and extract contents again.
                // Prefer vanilla CreateTreasureContent over a custom currency table.
                int beforeRetry = things.Count;
                Thing? retry = null;
                try
                {
                    int lv = mission.isRegion
                        ? Math.Max(1, 5 + mission.lockpickSkill / 10)
                        : Math.Max(1, RewardLv(mission));
                    try { retry = ThingGen.CreateTreasure("chest3", lv, TreasureType.RandomChest); }
                    catch { retry = null; }
                    if (retry == null)
                    {
                        try { retry = ThingGen.CreateTreasure("chest_treasure", lv, TreasureType.RandomChest); }
                        catch { retry = null; }
                    }
                    if (retry == null)
                    {
                        try
                        {
                            retry = ThingGen.Create("chest3", -1, lv)
                                ?? ThingGen.Create("chest_treasure", -1, lv);
                            if (retry != null)
                            {
                                ThingGen.CreateTreasureContent(retry, lv, TreasureType.RandomChest, clearContent: true);
                            }
                        }
                        catch { retry = null; }
                    }

                    if (retry != null)
                    {
                        FlattenInto(things, retry, keepContainer: false);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogDebug("lockpick chest retry: " + ex.Message);
                }
                finally
                {
                    try
                    {
                        if (retry != null && retry.parent == null && !retry.isDestroyed)
                        {
                            retry.Destroy();
                        }
                    }
                    catch
                    {
                    }
                }

                if (things.Count > beforeRetry)
                {
                    for (int j = beforeRetry; j < things.Count; j++)
                    {
                        NoteLoot(mission, things[j], "Lockpick");
                    }
                }
                else
                {
                    // Last resort: one vanilla currency stack, not a custom 85/12/3 table.
                    Thing? coin = null;
                    try { coin = ThingGen.CreateCurrency(Mathf.Clamp(8 + mission.lockpickSkill / 6, 6, 40)); }
                    catch { coin = null; }
                    if (coin == null)
                    {
                        coin = TryCreate("money", 1);
                        if (coin != null)
                        {
                            try { coin.SetNum(Mathf.Clamp(8 + mission.lockpickSkill / 6, 6, 40)); } catch { }
                        }
                    }
                    if (coin != null)
                    {
                        things.Add(coin);
                        NoteLoot(mission, coin, "Lockpick");
                    }
                }
            }
        }
    }


    static int CountThingNumsRange(List<Thing> things, int startIndex)
    {
        if (things == null || things.Count == 0 || startIndex >= things.Count)
        {
            return 0;
        }

        int n = 0;
        for (int i = Math.Max(0, startIndex); i < things.Count; i++)
        {
            Thing t = things[i];
            if (t == null)
            {
                continue;
            }

            try { n += Math.Max(1, t.Num); }
            catch { n += 1; }
        }

        return n;
    }


    static void CollectRegionThings(DungeonDispatchMission mission, List<Thing> things)
    {
        try
        {
            // v26+: fixed 4-biome manual pools only (no live scan / cooked leaks).
            CollectRegionThingsManual(mission, things);
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("collect region failed: " + ex.Message);
        }
    }


    static int CountThingNums(List<Thing> things)
    {
        if (things == null || things.Count == 0)
        {
            return 0;
        }

        int n = 0;
        for (int i = 0; i < things.Count; i++)
        {
            Thing t = things[i];
            if (t == null)
            {
                continue;
            }

            try { n += Math.Max(1, t.Num); }
            catch { n += 1; }
        }

        return n;
    }


    static void ScaleThingList(List<Thing> things, float scale)
    {
        if (things == null || things.Count == 0)
        {
            return;
        }

        float s = Mathf.Clamp(scale, 0.05f, 1f);
        for (int i = things.Count - 1; i >= 0; i--)
        {
            Thing t = things[i];
            if (t == null)
            {
                things.RemoveAt(i);
                continue;
            }

            try
            {
                int n = Math.Max(1, t.Num);
                int shown = Mathf.FloorToInt(n * s + 0.0001f);
                if (shown <= 0 && n * s >= 0.5f)
                {
                    shown = 1;
                }

                if (shown <= 0)
                {
                     if (t.parent == null) t.Destroy();
                    things.RemoveAt(i);
                }
                else
                {
                    t.SetNum(shown);
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchRewards.cs silent catch: " + __e.Message); }
}
    }


    static void CollectFixedDungeonThings(DungeonDispatchMission mission, List<Thing> things)
    {

            int danger = Math.Max(1, mission.dangerLv);
            int lv = RewardLv(mission);
            float haul = HaulUnits(mission);
            int members = Math.Max(1, mission.MemberCount);
            int explore = ExploreSkillScore(mission);
            // Success extra: a few deeper room finds from the same ground pool + specials.
            // Count uses raw danger; item identity stays zone-biome based.
            int n = Mathf.Clamp(
                Mathf.RoundToInt(2.0f + haul * 0.55f + members * 0.30f + explore / 40f + danger / 12f),
                2,
                14);
            for (int i = 0; i < n; i++)
            {
                Thing? t = RollDungeonGroundDrop(mission, lv + 3);
                if (t == null)
                {
                    // Rare non-staple dungeon find only as last resort (not bare ore/material).
                    t = TryCreateFromFilter(i == 0 ? "equipment" : "dungeon", lv)
                        ?? TryCreateFromFilter("eq", lv);
                }
                if (t != null)
                {
                    things.Add(t);
                }
            }

            Zone? zone = mission.GetZone();
            if (zone != null)
            {
                foreach (string id in SpecialRewardConfig.GetSpecialItemIds(zone))
                {
                    Thing? special = TryCreate(id, lv);
                    if (special != null)
                    {
                        things.Add(special);
        NoteLoot(mission, special, NpcLabor.LaborText.T("dis.loot.special"));
                    }
                }
            }

    }


    static Thing? BuildExploreParcel(DungeonDispatchMission mission, float scale)
    {
        var things = new List<Thing>();
        CollectExploreThings(mission, scale, things);
        if (things.Count == 0)
        {
            return null;
        }

        try
        {
        return ThingGen.CreateParcel(
            NpcLabor.LaborText.T("dis.parcel.package"),
            things.ToArray());
        }
        catch
        {
            return things[0];
        }
    }


    /// <summary>
    /// One simulated mid-run or bonus find (gather / material / dungeon junk).
    /// </summary>
    static Thing? RollSingleFind(DungeonDispatchMission mission, float scale)
    {
        try
        {
            // Dungeon uses doubled pool lv. Region haul never uses dangerLv.
            int lv = RewardLv(mission);
            int roll = EClass.rnd(100);
            float chance = Mathf.Clamp01(0.35f + scale);

            // Soft fail so partial ticks do not flood lootLog.
            if (EClass.rnd(100) > Mathf.RoundToInt(chance * 100f))
            {
                return null;
            }

            if (mission.isRegion)
            {
                return RollRegionDrop(mission.regionKind ?? "", mission);
            }

            if (roll < 40)
            {
                return RollDungeonGatherNodeDrop(mission, lv);
            }

            if (roll < 85)
            {
                return RollDungeonGroundDrop(mission, lv);
            }

            return RollDungeonGroundDrop(mission, lv)
                ?? TryCreateFromFilter("dungeon", lv)
                ?? TryCreateFromFilter("eq", lv)
                ?? TryCreate("mushroom", 1);
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("roll single find: " + ex.Message);
            return null;
        }
    }


    /// <summary>
    /// Simulated BossNefia chest for successful random nefia clears (weaker than real kill).
    /// </summary>
    static Thing? BuildBossSimChest(DungeonDispatchMission mission)
    {
        try
        {
            int lv = BossRewardLv(mission);
            // Prefer real treasure API when present.
            try
            {
                Thing chest = ThingGen.CreateTreasure("chest_boss", lv, TreasureType.BossNefia);
                if (chest != null)
                {
                    StripMoneyFromContainer(chest);
                    return chest;
                }
            }
            catch
            {

                    Thing chest = ThingGen.CreateTreasure("chest_boss", lv);
                    if (chest != null)
                    {
                        StripMoneyFromContainer(chest);
                        return chest;
                    }

}

            // Fallback: parcel of a few high-ish dungeon finds labeled as boss loot.
            var things = new List<Thing>();
            int danger = Math.Max(1, mission.dangerLv);
            int n = Mathf.Clamp(2 + danger / 15 + mission.MemberCount / 2, 2, 6);
            for (int i = 0; i < n; i++)
            {
                Thing? t = TryCreateFromFilter(i == 0 ? "equipment" : PickDungeonFilter(lv + 5), lv + 5);
                if (t != null)
                {
                    things.Add(t);
                }
            }

            if (things.Count == 0)
            {
                Thing? one = TryCreateFromFilter("treasure", lv) ?? TryCreateFromFilter("equipment", lv);
                if (one != null)
                {
                    things.Add(one);
                }
            }

            if (things.Count == 0)
            {
                return null;
            }

            try
            {
                return ThingGen.CreateParcel(
                    NpcLabor.LaborText.T("dis.parcel.loot"),
                    things.ToArray());
            }
            catch
            {
                return things[0];
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("boss sim chest: " + ex.Message);
            return null;
        }
    }



    static void FlattenInto(List<Thing> dest, Thing source, bool keepContainer)
    {
        if (dest == null || source == null)
        {
            return;
        }

        bool extractedAny = false;
        try
        {
            StripMoneyFromContainer(source, keepLockpickCurrency: true);
            var kids = new List<Thing>();
            if (source.things != null)
            {
                foreach (Thing t in source.things)
                {
                    if (t != null)
                    {
                        kids.Add(t);
                    }
                }
            }

            if (kids.Count > 0)
            {
                foreach (Thing t in kids)
                {
                    try
                    {
                        if (t.parent != null)
                        {
                            t.parent.RemoveCard(t);
                        }
                    }
                    catch
                    {
                    }

                    if (!IsMoneyThing(t) || IsLockpickCurrencyThing(t))
                    {
                        dest.Add(t);
                        extractedAny = true;
                    }
                }

                if (keepContainer)
                {
                    dest.Add(source);
                }
                else
                {
                    try
                    {
                        if (source.parent == null && !source.isDestroyed)
                        {
                            source.Destroy();
                        }
                    }
                    catch
                    {
                    }
                }

                return;
            }
        }
        catch (System.Exception __e)
        {
            Plugin.LogDebug("DungeonDispatchRewards.cs silent catch: " + __e.Message);
        }

        // Lockpick / boss extract path: never mail the empty chest body.
        // Only keep the source itself when the caller explicitly wants the container.
        if (keepContainer && !IsMoneyThing(source))
        {
            dest.Add(source);
            return;
        }

        // If we failed to extract anything and are discarding the container, leave dest unchanged.
        // Caller (CollectLockpickChestThings) will fall back to currency when nothing was added.
        if (!extractedAny && !keepContainer)
        {
            try
            {
                if (source.parent == null && !source.isDestroyed)
                {
                    source.Destroy();
                }
            }
            catch
            {
            }
        }
    }



    static Thing? BuildRewardParcel(string title, List<Thing> all)
    {
        if (all == null || all.Count == 0)
        {
            return null;
        }


            Thing? pack = ThingGen.CreateParcel(title, all.ToArray());
            if (pack != null)
            {
                return pack;
            }


        // Fallback: nest everything under the first item so mailbox gets one entry.
        Thing first = all[0];
        try
        {
            for (int i = 1; i < all.Count; i++)
            {
                try
                {
                    first.AddThing(all[i]);
                }
                catch
                {

                        first.AddCard(all[i]);

}
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchRewards.cs silent catch: " + __e.Message); }

            first.c_idRefName = title;

return first;
    }


    /// <summary>
    /// PutInMailBox only looks at EClass._map. When the mission finishes while PC is
    /// away from the base, drop into home zone map / owner map instead of current map.
    /// </summary>
    static bool TryPutInHomeMail(FactionBranch branch, DungeonDispatchMission mission, Thing pack)
    {
        if (branch == null || pack == null)
        {
            return false;
        }

        Zone? home = null;
        try
        {
            home = mission?.GetHomeZone() ?? branch.owner;
        }
        catch
        {
            home = branch.owner;
        }

        // Preferred path: vanilla API when PC is already at the home active map.

            bool atHome = false;
            try
            {
                atHome = home != null
                    && EClass._zone != null
                    && (EClass._zone.uid == home.uid
                        || (EClass._zone.GetTopZone()?.uid ?? -1) == home.uid
                        || (home.IsPCFaction && EClass._zone.IsPCFaction && EClass._zone == home));
            }
            catch
            {
                atHome = home != null && EClass._zone != null && EClass._zone.uid == home.uid;
            }

            if (atHome)
            {
                branch.PutInMailBox(pack);
                return true;
            }


        // Off-home settle: write into home zone's installed mailbox if map is loaded.
        try
        {
            Map? map = null;
            try
            {
                map = home?.map;
            }
            catch
            {
                map = null;
            }

            if (map?.props?.installed != null)
            {
                Thing? mail = null;
                try
                {
                    mail = map.props.installed.FindEmptyContainer<TraitMailPost>();
                }
                catch
                {

                        mail = map.props.installed.Find<TraitMailPost>();

}

                if (mail != null)
                {
                    mail.AddCard(pack);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("home map mailbox failed: " + ex.Message);
        }

        // Last resort: vanilla PutInMailBox (may land on current zone floor / mailbox).
        try
        {
            branch.PutInMailBox(pack);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("PutInMailBox fallback failed: " + ex.Message);
            return false;
        }
    }


    static void StripMoney(List<Thing> things, bool keepLockpickCurrency = false)
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

            if (IsMoneyThing(t))
            {
                if (keepLockpickCurrency && IsLockpickCurrencyThing(t))
                {
                    StripMoneyFromContainer(t, keepLockpickCurrency: true);
                    continue;
                }

                things.RemoveAt(i);
                continue;
            }

            StripMoneyFromContainer(t, keepLockpickCurrency);
        }
    }


    static void StripMoneyFromContainer(Thing container, bool keepLockpickCurrency = false)
    {
        if (container?.things == null)
        {
            return;
        }

        try
        {
            var remove = new List<Thing>();
            foreach (Thing t in container.things)
            {
                if (t == null)
                {
                    continue;
                }

                if (!IsMoneyThing(t))
                {
                    continue;
                }

                if (keepLockpickCurrency && IsLockpickCurrencyThing(t))
                {
                    continue;
                }

                remove.Add(t);
            }

            foreach (Thing t in remove)
            {
                try
                {
                    t.Destroy();
                }
                catch
                {

                        container.RemoveCard(t);

}
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchRewards.cs silent catch: " + __e.Message); }
}


    static bool IsMoneyThing(Thing t)
    {
        if (t == null)
        {
            return false;
        }


            string id = t.id ?? "";
            if (id == "money" || id == "money2" || id == "cash" || id == "goldbar" || id == "medal")
            {
                return true;
            }

return false;
    }


    /// <summary>Lockpick chest currencies that are allowed through deliver strip.</summary>
    static bool IsLockpickCurrencyThing(Thing t)
    {
        if (t == null)
        {
            return false;
        }

        try
        {
            string id = t.id ?? "";
            return id == "money" || id == "money2" || id == "medal";
        }
        catch
        {
            return false;
        }
    }


    static string PickMaterialFilter(int lv)
    {
        if (lv >= 30)
        {
            return EClass.rnd(2) == 0 ? "material" : "ore";
        }

        if (lv >= 12)
        {
            return EClass.rnd(3) == 0 ? "ore" : "material";
        }

        return EClass.rnd(4) == 0 ? "ore" : "material";
    }


    static string PickDungeonFilter(int lv)
    {
        int roll = EClass.rnd(100);
        if (roll < 30)
        {
            return "equipment";
        }

        if (roll < 50)
        {
            return lv >= 15 ? "treasure" : "dungeon";
        }

        if (roll < 70)
        {
            return "material";
        }

        if (roll < 85)
        {
            return "ore";
        }

        return "potion";
    }


    static Thing? TryCreateFromFilter(string filter, int lv)
    {
        if (string.IsNullOrEmpty(filter))
        {
            return null;
        }

        try
        {
            return ThingGen.CreateFromFilter(filter, Math.Max(1, lv));
        }
        catch
        {
            return TryCreateFromCategorySafe(filter, lv);
        }
    }


    static Thing? TryCreateFromCategorySafe(string cat, int lv)
    {
        try
        {
            return ThingGen.CreateFromCategory(cat, Math.Max(1, lv));
        }
        catch
        {
            try
            {
                return ThingGen.CreateFromTag(cat, Math.Max(1, lv));
            }
            catch
            {
                return null;
            }
        }
    }


    static Thing? TryCreate(string id, int lv)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        try
        {
            // ThingGen.Create(id, idMat, lv): second arg is MATERIAL id, not level.
            // Passing lv there made fruit "oak" (mat id 1) instead of fresh default.
            return ThingGen.Create(id, -1, Math.Max(1, lv));
        }
        catch
        {
            try
            {
                return ThingGen.Create(id);
            }
            catch
            {
                return null;
            }
        }
    }


    static void CompactStacks(List<Thing> things)
    {
        if (things == null || things.Count <= 1)
        {
            return;
        }

        try
        {
            for (int i = 0; i < things.Count; i++)
            {
                Thing a = things[i];
                if (a == null || a.isDestroyed)
                {
                    continue;
                }

                for (int j = things.Count - 1; j > i; j--)
                {
                    Thing b = things[j];
                    if (b == null || b.isDestroyed)
                    {
                        things.RemoveAt(j);
                        continue;
                    }

                    bool stacked = false;
                    try
                    {
                        stacked = b.TryStackTo(a);
                    }
                    catch
                    {
                        stacked = false;
                    }

                    if (stacked || b.isDestroyed)
                    {
                        things.RemoveAt(j);
                    }
                }
            }

            // Drop null / destroyed slots.
            for (int i = things.Count - 1; i >= 0; i--)
            {
                Thing t = things[i];
                if (t == null)
                {
                    things.RemoveAt(i);
                    continue;
                }


                    if (t.isDestroyed)
                    {
                        things.RemoveAt(i);
                    }

}
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("compact stacks: " + ex.Message);
        }
    }


    internal static void GrantExp(IList<Chara> members, DispatchSettleKind kind, DungeonDispatchMission? mission = null)
    {
        if (members == null)
        {
            return;
        }

        for (int i = 0; i < members.Count; i++)
        {
            GrantExp(members[i], kind, mission);
        }
    }


    /// <summary>
    /// Base exp × weeks (region weeks; dungeon treated as 1).
    /// Region specialty ×2 on top of base for the matching life skill.
    /// Returns a compact log line for console/debug.
    /// </summary>
    internal static string GrantExp(Chara c, DispatchSettleKind kind, DungeonDispatchMission? mission = null)
    {
        if (c == null)
        {
            return "";
        }

        try
        {
            int weeks = 1;
            if (mission != null && mission.isRegion)
            {
                weeks = Mathf.Clamp(mission.exploreWeeks <= 0 ? 1 : mission.exploreWeeks, 1, 4);
            }

            int exploreBase = LaborConfig.DispatchExpExplore(kind);
            int lockpickBase = LaborConfig.DispatchExpLockpick(kind);
            int gatherBase = LaborConfig.DispatchExpGather(kind);

            int explore = exploreBase * weeks;
            int lockpick = lockpickBase * weeks;
            int gather = gatherBase * weeks;

            // Region specialty: plain explore, mountain mining, forest lumber, beach digging.
            int specialtyMult = LaborConfig.RegionSpecialtyMult;
            int specialtyId = 0;
            int specialtyAmt = 0;
            if (mission != null && mission.isRegion && specialtyMult > 1)
            {
                string rk = DungeonDispatchTargets.NormalizeRegionKind(mission.regionKind);
                if (rk == "field")
                {
                    rk = "plain";
                }

                // Extra amount so total for specialty skill becomes base * specialtyMult.
                int extra = exploreBase * weeks * (specialtyMult - 1);
                switch (rk)
                {
                    case "plain":
                        specialtyId = DungeonDispatchMission.SkillExplore;
                        specialtyAmt = extra;
                        break;
                    case "mountain":
                        specialtyId = DungeonDispatchMission.SkillMining;
                        specialtyAmt = exploreBase * weeks * specialtyMult;
                        break;
                    case "forest":
                        specialtyId = DungeonDispatchMission.SkillLumber;
                        specialtyAmt = exploreBase * weeks * specialtyMult;
                        break;
                    case "beach":
                        specialtyId = DungeonDispatchMission.SkillDigging;
                        specialtyAmt = exploreBase * weeks * specialtyMult;
                        break;
                }
            }

            int exploreTotal = explore + (specialtyId == DungeonDispatchMission.SkillExplore ? specialtyAmt : 0);
            c.ModExp(DungeonDispatchMission.SkillExplore, exploreTotal);
            c.ModExp(DungeonDispatchMission.SkillLockpick, lockpick);
            c.ModExp(DungeonDispatchMission.SkillGather, gather);
            if (specialtyId > 0 && specialtyId != DungeonDispatchMission.SkillExplore && specialtyAmt > 0)
            {
                c.ModExp(specialtyId, specialtyAmt);
            }

            string name;
            try { name = c.NameSimple ?? c.Name ?? ("#" + c.uid); }
            catch { name = "#" + c.uid; }

            var parts = new List<string>();
            parts.Add(FormatExpGain(c, DungeonDispatchMission.SkillExplore, NpcLabor.LaborText.T("dis.exp.explore"), exploreTotal));
            parts.Add(FormatExpGain(c, DungeonDispatchMission.SkillLockpick, NpcLabor.LaborText.T("dis.exp.lockpick"), lockpick));
            parts.Add(FormatExpGain(c, DungeonDispatchMission.SkillGather, NpcLabor.LaborText.T("dis.exp.gather"), gather));
            if (specialtyId == DungeonDispatchMission.SkillMining)
            {
                parts.Add(FormatExpGain(c, specialtyId, NpcLabor.LaborText.T("dis.exp.dig"), specialtyAmt));
            }
            else if (specialtyId == DungeonDispatchMission.SkillLumber)
            {
                parts.Add(FormatExpGain(c, specialtyId, NpcLabor.LaborText.T("dis.exp.lumber"), specialtyAmt));
            }
            else if (specialtyId == DungeonDispatchMission.SkillDigging)
            {
                parts.Add(FormatExpGain(c, specialtyId, NpcLabor.LaborText.T("dis.exp.tunnel"), specialtyAmt));
            }

            string line = name + " exp×" + weeks + "w: " + string.Join(" / ", parts);
            Plugin.LogInfo("dispatch exp " + line);
            return line;
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("dispatch exp failed: " + ex.Message);
            return "";
        }
    }


    static string FormatExpGain(Chara c, int skillId, string label, int gained)
    {
        if (gained <= 0)
        {
            return NpcLabor.LaborText.T("dis.exp.zero", label);
        }

        int cur = 0;
        int toNext = 0;
        int lv = 0;
        try
        {
            Element? el = c.elements?.GetElement(skillId);
            if (el != null)
            {
                try { cur = el.vExp; } catch { cur = 0; }
                try { toNext = el.ExpToNext; } catch { toNext = 1000; }
                try { lv = el.Value; } catch { lv = c.Evalue(skillId); }
            }
            else
            {
                lv = c.Evalue(skillId);
                toNext = 1000;
            }
        }
        catch
        {
            try { lv = c.Evalue(skillId); } catch { lv = 0; }
            toNext = 1000;
        }

        int remain = Math.Max(0, toNext - cur);
        return NpcLabor.LaborText.T("dis.exp.gain", label, gained, lv, remain);
    }


    /// <summary>
    /// Vanilla nefia clear fame for successful dungeon dispatch (PC receives fame).
    /// Region missions skip fame (no danger clear).
    /// </summary>
    internal static void GrantFameOnDungeonSuccess(DungeonDispatchMission mission, DispatchSettleKind kind)
    {
        if (mission == null || kind != DispatchSettleKind.Success || mission.isRegion)
        {
            return;
        }


            int danger = Math.Max(1, mission.dangerLv);
            int fameRaw = LaborConfig.FameBase + danger * LaborConfig.FamePerDanger;
            int fame = EClass.rndHalf(fameRaw);
            if (fame <= 0)
            {
                fame = Math.Max(1, fameRaw / 2);
            }

            EClass.player.ModFame(fame);
            Plugin.LogInfo("dispatch fame +" + fame + " danger=" + danger + " mission=" + mission.missionId);

    }
}

