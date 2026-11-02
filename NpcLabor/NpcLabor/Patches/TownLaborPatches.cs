using System;
using System.Collections.Generic;
using HarmonyLib;
using NpcLabor.Dispatch;
using NpcLabor.TownLabor;

namespace NpcLabor.Patches;

/// <summary>
/// Slice E: append town shop labor rows under vanilla quests on tab 0.
/// Keep D dispatch button/tab independent.
/// </summary>
[HarmonyPatch(typeof(LayerQuestBoard), nameof(LayerQuestBoard.RefreshQuest))]
internal static class TownLaborQuestBoardPatch
{
    [HarmonyPostfix]
    static void Postfix(LayerQuestBoard __instance)
    {
        try
        {
            AppendTownLaborRows(__instance);
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("townlabor RefreshQuest: " + ex.Message);
        }
    }

    static void AppendTownLaborRows(LayerQuestBoard board)
    {
        if (board?.list == null)
        {
            return;
        }

        if (!TownLaborManager.CanHostTownLabor(EClass._zone))
        {
            return;
        }

        // Slice switch: with town labor off no short-term job is offered on the board.
        // Any shift already underway keeps running and still pays out.
        if (!LaborConfig.FeatureTownLabor)
        {
            return;
        }

        var offers = TownLaborManager.ListOffers(EClass._zone);
        if (offers == null || offers.Count == 0)
        {
            return;
        }

        // Vanilla RefreshQuest already set callbacks and refreshed. Append more rows
        // and refresh again so ItemQuest rows appear under vanilla quests.
        int added = 0;
        foreach (TownLaborOffer offer in offers)
        {
            if (offer?.Client == null || offer.Def == null)
            {
                continue;
            }

            QuestNpcLaborTownLaborOffer q = BuildOfferQuest(offer);
            try
            {
                board.list.Add(q);
                added++;
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("townlabor list.Add: " + ex.Message);
            }
        }

        if (added <= 0)
        {
            return;
        }

        try
        {
            board.list.Refresh();
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("townlabor list.Refresh: " + ex.Message);
        }

        // Re-bind click handlers: vanilla onInstantiate may have already run for existing
        // items; after Refresh, ensure labor rows call OnClickQuest (our override) only.
        // ItemQuest already sets button.SetOnClick -> a.OnClickQuest via callbacks.
        Plugin.LogDebug("townlabor board rows +" + added);
    }

    static QuestNpcLaborTownLaborOffer BuildOfferQuest(TownLaborOffer offer)
    {
        // Pay shown in the board's own 报酬 column (vanilla Quest.GetRewardText packs
        // rewardMoney through the "qReward" line), not repeated in the body text.
        int rewardMoney = 0;
        try
        {
            int investLv = 0;
            try { investLv = Math.Max(0, offer.Client?.c_invest ?? 0); } catch { investLv = 0; }

            if (offer.IsActive)
            {
                TownLaborMission? active = TownLaborManager.FindByClient(offer.Client!.uid);
                if (active != null && active.rewardPreviewLocked)
                {
                    rewardMoney = Math.Max(0, active.rewardMoney);
                }
                else
                {
                    int hours = Math.Max(1, offer.Hours);
                    rewardMoney = TownLaborRewards.CalcPreviewMoney(investLv, hours);
                }
            }
            else
            {
                rewardMoney = TownLaborRewards.CalcPreviewMoney(investLv, Math.Max(1, offer.Hours));
            }
        }
        catch
        {
            rewardMoney = 0;
        }

        var q = new QuestNpcLaborTownLaborOffer
        {
            clientUid = offer.Client!.uid,
            jobId = offer.Def.Id,
            isActiveLabor = offer.IsActive,
            laborHours = offer.Hours,
            // Unique non-zero uid so Quest.Equals / UIList.Contains do not collapse rows.
            // Do not bump game.quests.uid (that counter is for real started quests).
            uid = NextOfferUid(offer.Client!.uid, offer.Def!.Id),
            id = "npclabor_townlabor_offer_" + offer.Client!.uid,
            rewardMoney = rewardMoney,
            isNew = !offer.IsActive,
            isComplete = false,
            phase = 0,
            deadline = 0,
            track = false,
            startDate = 0,
        };

        try
        {
            // Bind portrait/client name without assigning chara.quest (assignQuest: false).
            q.SetClient(offer.Client, assignQuest: false);
        }
        catch
        {
            try
            {
                q.person = new Person(offer.Client);
            }
            catch
            {
                q.EnsureSafePerson();
            }
        }

        // Force person temp cache so Client resolve works for map-local merchants.
        try
        {
            if (q.person == null)
            {
                q.person = new Person(offer.Client);
            }
            else
            {
                q.person.SetChara(offer.Client);
            }
        }
        catch
        {
            q.EnsureSafePerson();
        }

        q.EnsureSafePerson();
        q.isComplete = false;
        q.phase = 0;

        // Fake remaining hours for Date.GetText(q.Hours) display: Hours uses deadline.
        // deadline==0 => Hours=-1 => "dateDayVoid". Prefer a temporary raw deadline.
        try
        {
            int hours = Math.Max(1, offer.Hours);
            if (offer.IsActive)
            {
                TownLaborMission? m = TownLaborManager.FindByClient(offer.Client!.uid);
                hours = m != null ? Math.Max(1, m.hoursLeft) : hours;
            }

            q.deadline = EClass.world.date.GetRaw() + hours * 60;
            // Guard against accidental expiry chrome if date math goes weird.
            if (q.Hours < 0)
            {
                q.deadline = EClass.world.date.GetRaw() + Math.Max(1, offer.Hours) * 60;
            }
        }
        catch
        {
            q.deadline = 0;
        }

        q.isComplete = false;
        return q;
    }

    /// <summary>
    /// Stable-ish unique synthetic uid for board rows. Stays outside typical quest uid range
    /// collisions by mixing client + job hash with a large base offset.
    /// </summary>
    static int NextOfferUid(int clientUid, string jobId)
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + clientUid;
            h = h * 31 + (jobId ?? string.Empty).GetHashCode();
             // Use negative uid to avoid colliding with real quest uids (which are always positive).
             int uid = -(Math.Abs(h) + 1);
             if (uid >= 0) { uid = -1; } // safety: ensure always negative
            if (uid == 0)
            {
                 uid = -1;
            }

            return uid;
        }
    }
}

/// <summary>
/// Freeze PC hunger while they stand a self-work shift (6-12h). Vanilla grows
/// hunger in Chara.Tick (turn % 50 == 1, rnd(3) == 0 -> hunger.Mod(1)); past
/// VeryHungry it locks HP regen and at Starving the PC bleeds until the shift
/// AI gets damage-cancelled. A self-work shift is treated like resting, so
/// food should not drain. Stats.Hunger is one shared static instance for every
/// Chara, so a prefix on Stats.Mod can cheaply filter exactly the hunger
/// growth of the PC while their top-level AI is a self-work shift.
/// </summary>
[HarmonyPatch(typeof(Stats), nameof(Stats.Mod))]
internal static class StatsHungerFreezePatch
{
    static bool Prefix(Stats __instance, int a)
    {
        try
        {
            if (a > 0 && ReferenceEquals(__instance, Stats.Hunger))
            {
                Chara? pc = EClass.pc;
                if (pc != null && pc.ai is AI_TownLaborPcSelf)
                {
                    return false;
                }
            }
        }
        catch
        {
        }

        return true;
    }
}

/// <summary>
/// Middle-click ("all action") menu on the shopkeeper: offer 继续帮工 when the
/// player has a still-running self-work shift on that shop but the work AI is no
/// longer attached (they walked away / the act was cancelled). ActPlan builds its
/// entry list centrally in <c>_Update</c>, so the extra line is appended after
/// the vanilla plan is built — nothing vanilla is removed or reordered.
/// </summary>
[HarmonyPatch(typeof(ActPlan), nameof(ActPlan.Update))]
internal static class TownLaborResumeActPatch
{
    [HarmonyPostfix]
    static void Postfix(ActPlan __instance)
    {
        try
        {
            if (__instance == null || __instance.input != ActInput.AllAction)
            {
                return;
            }

            if (!LaborConfig.FeatureEnabled || !LaborConfig.FeatureTownLabor)
            {
                return;
            }

            Chara? client = FindResumableClient(__instance);
            if (client == null)
            {
                return;
            }

            Chara target = client;
            // DynamicAct renders through Lang.Get(id); unknown keys echo back, so
            // handing it the already-localized line is enough.
            var act = new DynamicAct(NpcLabor.LaborText.T("town.ui.resume"),
                () => NpcLabor.TownLabor.TownLaborManager.TryResumePcSelf(target));

            __instance.list.Add(new ActPlan.Item
            {
                act = act,
                tc = target,
                pos = __instance.pos.Copy(),
            });
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("townlabor resume act: " + ex.Message);
        }
    }

    static Chara? FindResumableClient(ActPlan plan)
    {
        try
        {
            List<Card>? cards = plan.pos?.ListCards();
            if (cards == null)
            {
                return null;
            }

            foreach (Card c in cards)
            {
                if (c is Chara ch && NpcLabor.TownLabor.TownLaborManager.CanResumePcSelf(ch))
                {
                    return ch;
                }
            }
        }
        catch { }

        return null;
    }
}
