using System;
using System.Collections.Generic;
using NpcLabor.Dispatch;
using UnityEngine;

namespace NpcLabor.TownLabor;

/// <summary>
/// Rewards for town shop labor. Does NOT call Quest.Complete.
/// Success: silent affinity, flat cash ((minWage + shop invest) × hours, no skill
/// factor), plat, furniture ticket chance,
/// shop invest only when skill > shop invest and the town is still ahead of the shop,
/// themed leftover, hours-based skill exp.
/// Fail/recall: no main prize; recall may grant tiny exp.
/// Delivery: same-map Drop near PC; off-map home mailbox.
/// Player settle log stays lean: money / plat / ticket / leftover + soft invest warn.
/// </summary>
internal static class TownLaborRewards
{
    // Back-compat aliases used by console/debug; live values come from LaborConfig.
    internal static int MinWagePerHour => LaborConfig.MinWagePerHour;
    internal static int ExpPerHour => LaborConfig.ExpPerHour;
    internal static int FurnitureTicketChancePercent => LaborConfig.FurnitureTicketChancePercent;

    internal static void Deliver(
        TownLaborMission mission,
        TownLaborSettleKind kind,
        Chara? worker,
        Chara? client)
    {
        if (mission == null)
        {
            return;
        }

        if (!mission.rewardPreviewLocked)
        {
            LockRewardPreview(mission);
        }

        if (mission.rewardLog == null)
        {
            mission.rewardLog = new List<string>();
        }
        else
        {
            mission.rewardLog.Clear();
        }

        bool success = kind == TownLaborSettleKind.Success;
        TownLaborJobDef? def = mission.Def;
        int hours = Math.Max(1, mission.hoursTotal > 0 ? mission.hoursTotal : 8);
        int investLv = Math.Max(0, mission.clientInvest);
        if (investLv <= 0 && client != null)
        {
            try { investLv = Math.Max(0, client.c_invest); } catch { investLv = 0; }
        }
        int workerSkill = mission.workerSkill;
        if (workerSkill <= 0 && worker != null && mission.skillId > 0)
        {
            workerSkill = TownLaborJobs.GetWorkerSkill(worker, mission.skillId);
        }

        int clientInvest = mission.clientInvest;
        if (clientInvest <= 0 && client != null)
        {
            try { clientInvest = Math.Max(0, client.c_invest); } catch { clientInvest = 0; }
        }

        var drops = new List<Thing>();

        // Affinity — silent.
        if (success && client != null && !client.isDead && EClass.pc != null)
        {
            int aff = LaborConfig.AffinityBase;
            try
            {
                client.ModAffinity(EClass.pc, aff, show: false);
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("townlabor affinity: " + ex.Message);
            }
        }

        // Shop invest: two gates, see TownLaborJobs.CanGrantShopInvest —
        // skill must beat the shop (c_invest) AND the town must still be ahead of it.
        // Soft reminder when the skill gate passes but the town gate blocks the raise.
        if (success && client != null && !client.isDead && workerSkill > investLv)
        {
            try
            {
                bool canInvestTrait = false;
                try
                {
                    if (client.trait is TraitCitizen cit)
                    {
                        canInvestTrait = cit.CanInvest;
                    }
                    else if (client.trait is TraitMerchant)
                    {
                        canInvestTrait = true;
                    }
                    else
                    {
                        // Some shop NPCs expose CanInvest without the exact type tests above.
                        try
                        {
                            var tr = client.trait;
                            if (tr != null)
                            {
                                var p = tr.GetType().GetProperty("CanInvest");
                                if (p != null && p.PropertyType == typeof(bool))
                                {
                                    canInvestTrait = (bool)(p.GetValue(tr) ?? false);
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch (System.Exception __e) { Plugin.LogDebug("TownLaborRewards.cs silent catch: " + __e.Message); }
                if (canInvestTrait)
                {
                    if (TownLaborManager.ShopInvestQuotaUsed(mission.uidZone, client.uid))
                    {
                        // Monthly per-shop quota: one free raise per calendar month,
                        // recorded only on actual grants so recall/fail spend nothing.
                        string quotaMsg = LaborText.T("reward.investQuota");
                        mission.rewardLog.Add(quotaMsg);
                        try { Msg.Say(quotaMsg); } catch { }
                    }
                    else if (TownLaborJobs.CanGrantShopInvest(client, workerSkill))
                    {
                        TownLaborManager.RecordShopInvest(mission.uidZone, client.uid);
                        client.c_invest = client.c_invest + 1;
                        // Keep plat curve aligned with post-invest value when we just raised it.
                        try { clientInvest = Math.Max(clientInvest, client.c_invest); } catch { }

                        string shop = "";
                        try
                        {
                            shop = mission.clientName
                                ?? client.NameSimple
                                ?? client.Name
                                ?? "";
                        }
                        catch
                        {
                            shop = mission.clientName ?? "";
                        }

                        if (string.IsNullOrWhiteSpace(shop))
                        {
                            shop = LaborText.T("town.msg.town");
                        }

                        string investMsg = LaborText.T("reward.investRaised", shop.Trim());
                        mission.rewardLog.Add(investMsg);
                        // Dedicated line so the raise is never buried under money/plat/ticket.
                        try { Msg.Say(investMsg); } catch { }
                    }
                    else
                    {
                        string capMsg = LaborText.T("reward.investTownCap");
                        mission.rewardLog.Add(capMsg);
                        try { Msg.Say(capMsg); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("townlabor invest: " + ex.Message);
            }
        }

        // Cash: locked at accept so the task description matches the payout.
        // Pay depends on the shop's invest level only — worker skill no longer scales
        // it (it still gates the free invest grant, see canGrant above).
        if (success)
        {
            int money = Math.Max(0, mission.rewardMoney);
            if (money > 0)
            {
                Thing? coin = TryCreateMoney(money);
                if (coin != null)
                {
                    drops.Add(coin);
                    mission.rewardLog.Add(LaborText.T("reward.money", money));
                }
            }
        }

        // Platinum (白金币) — locked at accept; scales with shop invest tier.
        if (success)
        {
            int platNum = Math.Max(0, mission.rewardPlat);
            Thing? plat = TryCreatePlat(platNum);
            if (plat != null)
            {
                drops.Add(plat);
                mission.rewardLog.Add(LaborText.T("reward.plat", platNum));
            }
        }

        // Local furniture ticket — roll locked at accept.
        if (success && mission.rewardTicketGranted)
        {
            Thing? ticket = TryCreateFurnitureTicket(mission, client);
            if (ticket != null)
            {
                drops.Add(ticket);
                mission.rewardLog.Add(LaborText.T("reward.furnitureTicket"));
            }
        }

        // Themed leftover / 打工奖励 (success only, optional flavor).
        if (success && def != null)
        {
            Thing? leftover = RollLeftover(def, client);
            if (leftover != null)
            {
                drops.Add(leftover);
                string name = SafeName(leftover);
                mission.rewardLog.Add(LaborText.T("reward.leftover", name));
            }
        }

        // Skill exp: companion full on settle; PC self already dripped half-rate hourly
        // so success settle skips bulk exp (avoid double). Recall still grants tiny consolation.
        if (worker != null && !worker.isDead && mission.skillId > 0)
        {
            int exp = 0;
            bool pcSelf = false;
            try { pcSelf = mission.isPcSelf; } catch { pcSelf = false; }

            if (success)
            {
                if (!pcSelf)
                {
                    exp = ExpPerHour * hours;
                }
                // PC self: already granted ExpPerHour/2 each worked hour.
            }
            else if (kind == TownLaborSettleKind.Recall)
            {
                exp = Math.Max(6, hours); // tiny consolation
            }

            if (exp > 0)
            {
                try
                {
                    worker.ModExp(mission.skillId, exp);
                }
                catch (Exception ex)
                {
                    Plugin.LogDebug("townlabor exp: " + ex.Message);
                }
            }
        }

        // Potential: one trainer-equivalent tick on the job's own skill. Hidden
        // from every text (player spec 2026-10-30: keep it as a small surprise);
        // the number is the vanilla trainer default that ElementContainer.Train
        // applies, so it never out-scales actually paying a trainer.
        if (success && worker != null && !worker.isDead && mission.skillId > 0)
        {
            try
            {
                worker.elements.Train(mission.skillId);
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("townlabor potential: " + ex.Message);
            }
        }

        if (drops.Count == 0)
        {
            return;
        }

        DeliverThings(mission, drops);
    }

    /// <summary>
    /// Plat curve: min platBase; +platPerTier roughly every investTierSize shop invest.
    /// </summary>
    internal static int CalcRewardPlat(int clientInvest)
    {
        int invest = Math.Max(0, clientInvest);
        int basePlat = LaborConfig.PlatBase;
        int perTier = LaborConfig.PlatPerInvestTier;
        int tier = LaborConfig.InvestTierSize;
        return Math.Max(basePlat, basePlat + perTier * (invest / tier));
    }

    /// <summary>
    /// Hourly wage: (minWagePerHour + shop invest level). The only input is the
    /// shop's <c>c_invest</c> — worker skill does not scale pay any more (it still
    /// gates the free invest grant). 0 invest still pays the base rate.
    /// </summary>
    internal static int CalcFullWagePerHour(int investLv)
        => MinWagePerHour + Math.Max(0, investLv);

    /// <summary>Cash for the whole shift: hourly wage × hours. No skill factor.</summary>
    internal static int CalcPreviewMoney(int investLv, int hours)
        => CalcFullWagePerHour(investLv) * Math.Max(1, hours);

    /// <summary>
    /// Lock deterministic reward pins (cash / plat / ticket) at accept so the task
    /// description can show the exact payout. Leftover stays a settle-time roll.
    /// </summary>
    internal static void LockRewardPreview(TownLaborMission mission)
    {
        if (mission == null)
        {
            return;
        }

        int clientInvest = Math.Max(0, mission.clientInvest);
        int hours = Math.Max(1, mission.hoursTotal > 0 ? mission.hoursTotal : 8);
        mission.rewardMoney = CalcPreviewMoney(clientInvest, hours);
        mission.rewardPlat = CalcRewardPlat(clientInvest);
        mission.rewardTicketGranted = RollPercent(FurnitureTicketChancePercent);
        mission.rewardPreviewLocked = true;
    }

    /// <summary>
    /// Task-description reward line from the locked pins (money / plat / ticket).
    /// No skill wording: skill no longer changes the payout.
    /// </summary>
    internal static string RewardPreviewLine(TownLaborMission mission)
    {
        if (mission == null || !mission.rewardPreviewLocked)
        {
            return "";
        }

        var bits = new List<string>();
        if (mission.rewardMoney > 0)
        {
            bits.Add(LaborText.T("reward.money", mission.rewardMoney));
        }

        if (mission.rewardPlat > 0)
        {
            bits.Add(LaborText.T("reward.plat", mission.rewardPlat));
        }

        if (mission.rewardTicketGranted)
        {
            bits.Add(LaborText.T("reward.furnitureTicket"));
        }

        if (bits.Count == 0)
        {
            return "";
        }

        return LaborText.T("town.reward.line", string.Join(" / ", bits));
    }

    /// <summary>
    /// Board/tracker money-only line. No platinum / ticket / skill wording.
    /// </summary>
    internal static string MoneyOnlyPreviewLine(TownLaborMission mission)
    {
        if (mission == null)
        {
            return "";
        }

        if (!mission.rewardPreviewLocked)
        {
            LockRewardPreview(mission);
        }

        int money = Math.Max(0, mission.rewardMoney);
        if (money <= 0)
        {
            int investLv = Math.Max(0, mission.clientInvest);
            int hours = Math.Max(1, mission.hoursTotal > 0 ? mission.hoursTotal : 8);
            money = CalcFullWagePerHour(investLv) * hours;
        }

        if (money <= 0)
        {
            return "";
        }

        return LaborText.T("town.reward.moneyOnly", money);
    }

    static bool RollPercent(int percent)
    {
        if (percent <= 0)
        {
            return false;
        }

        if (percent >= 100)
        {
            return true;
        }

        try
        {
            return EClass.rnd(100) < percent;
        }
        catch
        {
            return false;
        }
    }

    static void DeliverThings(TownLaborMission mission, List<Thing> things)
    {
        if (things == null || things.Count == 0)
        {
            return;
        }

        bool sameMap = false;
        try
        {
            sameMap = EClass._zone != null && EClass._zone.uid == mission.uidZone
                && EClass.pc != null && EClass.pc.ExistsOnMap;
        }
        catch
        {
            sameMap = false;
        }

        if (sameMap)
        {
            Point? at = null;
            try
            {
                at = EClass.pc?.pos?.Copy() ?? EClass.pc?.pos;
            }
            catch (System.Exception __e) { Plugin.LogDebug("TownLaborRewards.cs silent catch: " + __e.Message); }
foreach (Thing t in things)
            {
                if (t == null || t.isDestroyed)
                {
                    continue;
                }

                try
                {
                    if (at != null && EClass._zone != null)
                    {
                        EClass._zone.AddCard(t, at);
                    }
                    else if (EClass.pc != null)
                    {
                        EClass.pc.Pick(t);
                    }
                }
                catch
                {
                    try { EClass.pc?.Pick(t); } catch { }
                }
            }

            return;
        }

        // Off-map: home mailbox parcel.
        try
        {
            FactionBranch? branch = EClass.BranchOrHomeBranch ?? EClass.Branch;
            if (branch == null)
            {
                foreach (Thing t in things)
                {
                    try { EClass.pc?.Pick(t); } catch { }
                }

                return;
            }

            string title = LaborTerms.TownWork + " · " + (string.IsNullOrEmpty(mission.jobTitle) ? NpcLabor.LaborText.T("town.msg.town") : mission.jobTitle);
            Thing? pack = null;
            try
            {
                pack = ThingGen.CreateParcel(title, things.ToArray());
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("townlabor parcel: " + ex.Message);
            }

            if (pack == null)
            {
                foreach (Thing t in things)
                {
                    try { branch.PutInMailBox(t); } catch { }
                }

                return;
            }

            try
            {
                branch.PutInMailBox(pack);
            }
            catch (Exception ex)
            {
                Plugin.LogWarn("townlabor mailbox: " + ex.Message);
                try { EClass.pc?.Pick(pack); } catch { }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("townlabor deliver offmap: " + ex.Message);
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
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborRewards.cs silent catch: " + __e.Message); }
        try
        {
            Thing? t = ThingGen.Create("money");
            if (t != null)
            {
                try { t.SetNum(amount); } catch { }
            }

            return t;
        }
        catch
        {
            return null;
        }
    }

    static Thing? TryCreatePlat(int amount)
    {
        if (amount <= 0)
        {
            return null;
        }

        try
        {
            Thing? t = ThingGen.Create("plat");
            if (t != null)
            {
                try { t.SetNum(amount); } catch { }
                return t;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborRewards.cs silent catch: " + __e.Message); }
        try
        {
            return ThingGen.Create("plat").SetNum(amount);
        }
        catch
        {
            return null;
        }
    }

    static Thing? TryCreateFurnitureTicket(TownLaborMission mission, Chara? client)
    {
        try
        {
            Thing? t = ThingGen.Create("ticket_furniture");
            if (t == null || t.isDestroyed)
            {
                return null;
            }

            Zone? zone = null;
            try
            {
                zone = mission.GetZone()
                    ?? client?.currentZone
                    ?? client?.homeZone
                    ?? EClass._zone;
                if (zone != null)
                {
                    zone = zone.GetTopZone() ?? zone;
                }
            }
            catch
            {
                zone = EClass._zone;
            }

            if (zone != null)
            {
                try
                {
                    TraitTicketFurniture.SetZone(zone, t);
                }
                catch (Exception ex)
                {
                    Plugin.LogDebug("townlabor ticket zone: " + ex.Message);
                }
            }

            return t;
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("townlabor ticket: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Stocked like a vanilla shop. Vanilla merchants create their stock with
    /// <c>ThingGen.Create(id, -1, ShopLv)</c> (Trait.cs:2434/2776): the level drives
    /// the tierGroup material roll, so an ingot bought from a blacksmith is always
    /// a level-appropriate iron-family ingot. We mirror that with the client's
    /// c_invest instead of pinning materials by hand — higher shop investment
    /// naturally rolls better metals, exactly like the real shop counter.
    /// </summary>
    static Thing? RollLeftover(TownLaborJobDef def, Chara? client)
    {
        if (def?.LeftoverIds == null || def.LeftoverIds.Length == 0)
        {
            return null;
        }

        int stockLv = 1;
        try { stockLv = Math.Max(1, client?.c_invest ?? 1); } catch { stockLv = 1; }

        int tries = Math.Min(4, def.LeftoverIds.Length);
        for (int i = 0; i < tries; i++)
        {
            string id;
            try
            {
                id = def.LeftoverIds[EClass.rnd(def.LeftoverIds.Length)];
            }
            catch
            {
                id = def.LeftoverIds[0];
            }

            Thing? t = TryCreate(id, 1, stockLv);
            if (t != null)
            {
                return t;
            }
        }

        for (int i = 0; i < def.LeftoverIds.Length; i++)
        {
            Thing? t = TryCreate(def.LeftoverIds[i], 1, stockLv);
            if (t != null)
            {
                return t;
            }
        }

        return null;
    }

    static Thing? TryCreate(string id, int num, int stockLv)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        try
        {
            // Vanilla shop-stock pattern: level, not a hand-pinned material,
            // governs the tierGroup roll (Card.SetMainMaterial -> GetRandomMaterial).
            Thing? t = ThingGen.Create(id, -1, stockLv);
            if (t == null || t.isDestroyed)
            {
                return null;
            }

            if (num > 1)
            {
                try { t.SetNum(num); } catch { }
            }

            return t;
        }
        catch
        {
            return null;
        }
    }

    static string SafeName(Thing t)
    {
        try
        {
            return t.Name ?? t.id ?? "?";
        }
        catch
        {
            try { return t.id ?? "?"; }
            catch { return "?"; }
        }
    }
}
