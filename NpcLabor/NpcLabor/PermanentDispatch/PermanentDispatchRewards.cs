using System;
using System.Collections.Generic;

namespace NpcLabor.PermanentDispatch;

/// <summary>
/// PARTIAL: monthly credit + end-of-posting delivery.
/// Potential / development / influence apply the moment a whole month
/// completes (they live on the NPC or the zone). Gold + items are counters
/// only — the physical payout is ONE parcel into the home mailbox when the
/// posting ends.
/// </summary>
internal static partial class PermanentDispatchManager
{
    /// <summary>Vanilla weapon categories a god's offering list may name.</summary>
    static readonly string[] WeaponCategories =
    {
        "gun", "bow", "crossbow", "sword", "axe", "dagger", "staff", "polearm", "rod"
    };

    /// <summary>Apply one settled month's non-parcel rewards.</summary>
    static void CreditMonthly(PermanentDispatchMission m, Chara worker)
    {
        PermanentDispatchDef? def = m.Def;
        if (def == null)
        {
            return;
        }

        // 1. Potential: vanilla trainer semantics (ElementContainer.Train).
        int potSkill = def.PotentialSkill;
        if (potSkill == -1)
        {
            potSkill = BestTrainable(worker, def);
        }

        if (potSkill > 0)
        {
            try
            {
                worker.elements.Train(potSkill);
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("pdispatch train: " + ex.Message);
            }
        }

        // 2. Zone specials.
        Zone? zone = null;
        try { zone = PermanentDispatchDef.ResolveZone(def.ZoneId); } catch { zone = null; }
        if (zone == null)
        {
            return;
        }

        try
        {
            switch (def.Special)
            {
                case "develop":
                    zone.ModDevelopment(def.MonthlyZonePoints(worker));
                    break;
                case "townInfluence":
                    zone.ModInfluence(def.MonthlyZonePoints(worker));
                    break;
                case "influence":
                    zone.ModInfluence(1);
                    break;
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("pdispatch zone special: " + ex.Message);
        }
    }

    /// <summary>Guild task: the NPC's highest skill among that guild's trainables.</summary>
    static int BestTrainable(Chara worker, PermanentDispatchDef def)
    {
        if (!PermanentDispatchDef.GuildTrainables.TryGetValue(def.GuildId, out int[]? ids))
        {
            return 0;
        }

        int bestId = 0;
        int bestVal = -1;
        foreach (int id in ids)
        {
            try
            {
                int v = worker.Evalue(id);
                if (v > bestVal)
                {
                    bestVal = v;
                    bestId = id;
                }
            }
            catch { }
        }

        return bestId;
    }

    /// <summary>
    /// End a posting: roll every settled month's gold + items into one parcel,
    /// mail it home, clear flags, send the worker home, drop the mission.
    /// </summary>
    static void SettleOut(PermanentDispatchMission m, bool recalled)
    {
        PermanentDispatchDef? def = m.Def;
        Missions.Remove(m);

        if (def == null)
        {
            Plugin.LogWarn("pdispatch settle: def gone " + m.defId);
            return;
        }

        Chara? worker = null;
        try { worker = RefChara.Get(m.uidChara); } catch { }

        int months = Math.Max(0, m.monthsPaid);

        // ---- build the parcel ------------------------------------------------
        var things = new List<Thing>();

        int gold = 0;
        for (int i = 0; i < months; i++)
        {
            gold += def.MonthlyMoney(worker);
        }

        Thing? money = TryCreateMoney(gold);
        if (money != null)
        {
            things.Add(money);
        }

        // 祭坛：整段委托只给一件神之武器，不是每月一把——奇跡品质按月发太肥。
        if (months > 0)
        {
            Thing? godWeapon = RollGodWeapon(def);
            if (godWeapon != null)
            {
                things.Add(godWeapon);
            }
        }

        for (int i = 0; i < months; i++)
        {
            // Rare drop replaces the whole month's common pool (winery: one
            // bottle worth ten instead of ten ordinary ones).
            List<Thing> rare = RollRareItem(def);
            if (rare.Count > 0)
            {
                things.AddRange(rare);
            }
            else
            {
                int n = def.MonthlyItemCount(worker);
                for (int k = 0; k < n; k++)
                {
                    Thing? t = RollMonthlyItem(def);
                    if (t != null)
                    {
                        things.Add(t);
                    }
                }
            }

            things.AddRange(RollBlessedItem(def, worker));

            if (def.Special == "indulgence")
            {                try
                {
                    if (EClass.rnd(100) < 5)
                    {
                        Thing? ind = SafeCreate("1085"); // indulgence / 免罪符
                        if (ind != null)
                        {
                            things.Add(ind);
                        }
                    }
                }
                catch { }
            }
        }

        // ---- deliver ---------------------------------------------------------
        bool mailed = false;
        if (things.Count > 0)
        {
            try
            {
                FactionBranch? branch = EClass.BranchOrHomeBranch ?? EClass.Branch;
                string title = LaborTerms.PDispatch + " · " + def.Name;
                if (branch != null)
                {
                    Thing? pack = null;
                    try { pack = ThingGen.CreateParcel(title, things.ToArray()); } catch { }
                    if (pack != null)
                    {
                        branch.PutInMailBox(pack);
                        mailed = true;
                    }
                    else
                    {
                        foreach (Thing t in things)
                        {
                            try { branch.PutInMailBox(t); } catch { }
                        }

                        mailed = true;
                    }
                }
                else if (EClass.pc != null)
                {
                    foreach (Thing t in things)
                    {
                        try { EClass.pc.Pick(t); } catch { }
                    }

                    mailed = true;
                }
            }
            catch (Exception ex)
            {
                Plugin.LogWarn("pdispatch deliver: " + ex.Message);
            }
        }

        // ---- worker homecoming -------------------------------------------------
        if (worker != null && !worker.isDead)
        {
            try { ClearDispatchedFlags(worker); } catch { }
            try
            {
                Zone? home = null;
                try { home = worker.homeZone; } catch { }
                if (home == null && m.homeZoneUid > 0)
                {
                    try { home = EClass.game?.spatials?.Find(m.homeZoneUid); } catch { }
                }

                if (home != null)
                {
                    worker.MoveZone(home, ZoneTransition.EnterState.Return);
                }
                else if (m.wasPartyMember)
                {
                    // No home recorded: back to the party instead of stranding.
                    // (Vanilla method name has the AddMemeber typo.)
                    try { EClass.pc?.party?.AddMemeber(worker); } catch { }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogWarn("pdispatch homecoming: " + ex.Message);
            }

            if (m.wasPartyMember)
            {
                try { EClass.pc?.party?.AddMemeber(worker); } catch { }
            }
        }

        // ---- message ------------------------------------------------------------
        try
        {
            string body = months > 0
                ? LaborText.T("pdisp.msg.settle", m.workerName, def.Name, months,
                    mailed ? LaborText.T("pdisp.msg.mailed") : "")
                : LaborText.T("pdisp.msg.settleNone", m.workerName, def.Name,
                    recalled ? LaborText.T("pdisp.msg.recalled") : "");
            Msg.Say(body);
        }
        catch { }

        Plugin.LogInfo("pdispatch settle id=" + m.missionId + " def=" + def.Id
            + " months=" + months + " mailed=" + mailed + " recalled=" + recalled);
        try { Save(); } catch { }
    }

    /// <summary>
    /// One month's item. A missing source row (RQX not installed) just yields
    /// nothing — no substitute item, no substituted category.
    /// </summary>
    static Thing? RollMonthlyItem(PermanentDispatchDef def)
    {
        if (def.ItemIds.Length == 0)
        {
            return null;
        }

        return SafeCreate(def.ItemIds[EClass.rnd(def.ItemIds.Length)]);
    }

    static Thing? SafeCreate(string id, int lv = -1)
    {
        try
        {
            // RQX guild currency (and any other mod item) is only creatable when
            // its source row exists — this is the "no error without the mod" rule.
            if (EClass.sources?.things?.map != null && !EClass.sources.things.map.ContainsKey(id))
            {
                return null;
            }

            Thing? t = lv > 0 ? ThingGen.Create(id, -1, lv) : ThingGen.Create(id);
            return t;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Rare monthly drop (<see cref="PermanentDispatchDef.RareItemId"/>). A bill
    /// carries its amount in <c>c_bill</c>, not in the item level, so it is
    /// created plain and priced at 1 oren.
    /// </summary>
    static List<Thing> RollRareItem(PermanentDispatchDef def)
    {
        var list = new List<Thing>();
        if (string.IsNullOrEmpty(def.RareItemId) || def.RareChancePercent <= 0)
        {
            return list;
        }

        try
        {
            if (EClass.rnd(100) >= def.RareChancePercent)
            {
                return list;
            }
        }
        catch
        {
            return list;
        }

        for (int i = 0; i < Math.Max(1, def.RareItemCount); i++)
        {
            Thing? t = SafeCreate(def.RareItemId);
            if (t == null)
            {
                continue;
            }

            if (def.RareItemId == "bill")
            {
                try { t.c_bill = 1; } catch { }
            }

            list.Add(t);
        }

        return list;
    }

    /// <summary>
    /// Blessed-state monthly item (<see cref="PermanentDispatchDef.BlessedItemId"/>),
    /// e.g. the church's holy water: plain "water" id, blessed.
    /// </summary>
    static List<Thing> RollBlessedItem(PermanentDispatchDef def, Chara? worker)
    {
        var list = new List<Thing>();
        if (string.IsNullOrEmpty(def.BlessedItemId))
        {
            return list;
        }

        int count = worker != null ? def.BlessedCount(worker) : Math.Max(1, def.BlessedItemCount);
        for (int i = 0; i < count; i++)
        {
            Thing? t = SafeCreate(def.BlessedItemId);
            if (t == null)
            {
                continue;
            }

            try { t.SetBlessedState(BlessedState.Blessed); } catch { }
            list.Add(t);
        }

        return list;
    }

    /// <summary>
    /// 祭坛：该神偏好的武器一件，品质拉到「奇跡」（Rarity.Legendary）。
    /// 偏好来自 SourceReligion.cat_offer 里属于武器的类目；献祭表里没有武器
    /// 类目的神（收获、治愈之类）改给祂自己的赠物 rewards。
    /// GodId 为空的祭坛（城里那座通用祭坛）按 PC 当前信仰的神算。
    /// </summary>
    static Thing? RollGodWeapon(PermanentDispatchDef def)
    {
        if (def.Special != "faith")
        {
            return null;
        }

        try
        {
            string godId = def.GodId;
            if (string.IsNullOrEmpty(godId))
            {
                try { godId = EClass.pc?.faith?.id ?? ""; } catch { godId = ""; }
            }

            if (string.IsNullOrEmpty(godId))
            {
                return null;
            }

            SourceReligion.Row? row = null;
            try
            {
                if (EClass.sources?.religions?.map != null)
                {
                    EClass.sources.religions.map.TryGetValue(godId, out row);
                }
            }
            catch
            {
                row = null;
            }

            if (row == null)
            {
                return null;
            }

            var cats = new List<string>();
            if (row.cat_offer != null)
            {
                foreach (string c in row.cat_offer)
                {
                    if (Array.IndexOf(WeaponCategories, c) >= 0)
                    {
                        cats.Add(c);
                    }
                }
            }

            var ids = new List<string>();
            if (cats.Count > 0)
            {
                try
                {
                    var allThings = EClass.sources?.things?.map;
                    if (allThings != null)
                    {
                        foreach (SourceThing.Row r in allThings.Values)
                        {
                            if (r?.id != null && cats.Contains(r.category))
                            {
                                ids.Add(r.id);
                            }
                        }
                    }
                }
                catch { }
            }

            // 献祭表里没有武器：给神自己的赠物（收获之神 → 镰刀那类）。
            if (ids.Count == 0 && row.rewards != null)
            {
                foreach (string rew in row.rewards)
                {
                    if (string.IsNullOrEmpty(rew))
                    {
                        continue;
                    }

                    string[] parts = rew.Split('|');
                    string pick = parts[EClass.rnd(parts.Length)];
                    if (!string.IsNullOrEmpty(pick))
                    {
                        ids.Add(pick);
                    }
                }
            }

            if (ids.Count == 0)
            {
                return null;
            }

            Thing? t = SafeCreate(ids[EClass.rnd(ids.Count)]);
            if (t == null)
            {
                return null;
            }

            try { t.ChangeRarity(Rarity.Legendary); } catch { }
            return t;
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("pdispatch godweapon: " + ex.Message);
            return null;
        }
    }

    static Thing? TryCreateMoney(int amount)
    {
        if (amount <= 0)
        {
            return null;
        }

        try
        {
            return ThingGen.CreateCurrency(amount, "money");
        }
        catch
        {
            try
            {
                Thing? t = ThingGen.Create("money");
                try { t?.SetNum(amount); } catch { }
                return t;
            }
            catch
            {
                return null;
            }
        }
    }
}
