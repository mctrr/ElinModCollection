using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NpcLabor.CoCraft;
using NpcLabor.TownLabor;

namespace NpcLabor.PermanentDispatch;

/// <summary>
/// Rows and actions for permanent postings.
///
/// Entry points are the vanilla quest board (rows appended under the town labor
/// rows) and the PC quest journal (active postings only). Both are display-only
/// row objects rebuilt on refresh — nothing is written into the save.
/// </summary>
internal static class PermanentDispatchUi
{
    /// <summary>One row for a task: offer when <paramref name="active"/> is false.</summary>
    internal static QuestNpcLaborPDispatch BuildRow(string defId, bool active)
    {
        PermanentDispatchDef? def = PermanentDispatchDef.GetById(defId);
        Zone? post = PermanentDispatchDef.ResolveZone(def?.ZoneId ?? "");
        var q = new QuestNpcLaborPDispatch
        {
            defId = defId,
            active = active,
            id = "npclabor_pdispatch_" + defId,
            // 剩余 column counts down to the next whole-month settlement.
            deadline = PermanentDispatchManager.NextMonthRaw(),
            uidClientZone = post?.uid ?? 0,
            track = false,
            isNew = false,
            isComplete = false,
            phase = 0,
            startDate = 0,
        };
        q.AssignUid();

        Chara? worker = active ? PermanentDispatchManager.FindWorker(defId) : null;

        // 报酬 column. Pay is base + rate x skill since the balance pass, so a
        // running posting must quote what the worker on it actually earns;
        // an offer only knows the floor (nobody is hired yet).
        try
        {
            q.rewardMoney = active && worker != null && def != null
                ? def.MonthlyMoney(worker)
                : def?.MoneyPerMonth ?? 0;
        }
        catch
        {
            q.rewardMoney = def?.MoneyPerMonth ?? 0;
        }

        if (worker != null)
        {
            try
            {
                q.person = new Person(worker);
            }
            catch
            {
                QuestTrackerSupport.EnsureSafePerson(q);
            }
        }
        else
        {
            // No client NPC: the 委托人 column reads the commissioning side instead
            // of falling back to the literal "null" name.
            QuestTrackerSupport.EnsureSafePerson(q);
            try
            {
                q.person.tempName = def != null && def.IsGuildTask
                    ? def.GuildName
                    : (post?.Name ?? def?.ZoneId ?? "");
            }
            catch { }
        }

        return q;
    }

    /// <summary>
    /// Board rows, below the town labor rows. Only THIS place's postings are
    /// listed (player spec 2026-11-01): another town's running posting no longer
    /// shows up here, it is read and cancelled from the PC quest journal.
    /// </summary>
    internal static void AppendBoardRows(LayerQuestBoard board)
    {
        if (board?.list == null || !LaborConfig.FeatureEnabled)
        {
            return;
        }

        // Unlock gate (player 2026-11-02: base Lv2). Running postings are unaffected —
        // they stay readable and cancellable from the journal.
        if (!LaborTerms.PDispatchUnlocked)
        {
            return;
        }

        string zoneId = EClass._zone?.id ?? "";
        var rows = new List<QuestNpcLaborPDispatch>();

        foreach (PermanentDispatchDef d in PermanentDispatchDef.ForBoard(zoneId))
        {
            rows.Add(BuildRow(d.Id, PermanentDispatchManager.IsDefBusy(d.Id)));
        }

        if (rows.Count == 0)
        {
            return;
        }

        foreach (QuestNpcLaborPDispatch q in rows)
        {
            board.list.Add(q);
        }

        // Postings belong at the very bottom, under the shop labor rows, whatever
        // order the two board postfixes ended up running in.
        foreach (QuestNpcLaborPDispatch q in rows)
        {
            board.list.items.Remove(q);
            board.list.items.Add(q);
        }

        try
        {
            board.list.Refresh();
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("pdispatch board refresh: " + ex.Message);
        }
    }

    /// <summary>
    /// Journal rows. <paramref name="content"/> is vanilla's ContentQuest; we only
    /// touch two members of it (the list field and List()), so a rename in a game
    /// update just means no journal rows instead of a crash.
    /// </summary>
    internal static void AppendJournalRows(object? content)
    {
        if (content == null || !LaborConfig.FeatureEnabled || PermanentDispatchManager.Count == 0)
        {
            return;
        }

        object? list = AccessTools.Field(content.GetType(), "list")?.GetValue(content);
        if (list == null)
        {
            return;
        }

        MethodInfo? add = FindSingleArg(list.GetType(), "Add");
        if (add == null)
        {
            return;
        }

        int added = 0;
        foreach (PermanentDispatchMission m in PermanentDispatchManager.All)
        {
            add.Invoke(list, new object[] { BuildRow(m.defId, active: true) });
            added++;
        }

        if (added <= 0)
        {
            return;
        }

        // Refresh (not List): List() clears items and re-runs vanilla's onList,
        // which would wipe the rows just added.
        MethodInfo? rebuild = AccessTools.Method(list.GetType(), "Refresh", new[] { typeof(bool) })
                              ?? AccessTools.Method(list.GetType(), "Refresh", Type.EmptyTypes);
        if (rebuild == null)
        {
            return;
        }

        rebuild.Invoke(list, rebuild.GetParameters().Length == 1 ? new object[] { false } : null);

        SuppressTrackButton(list);
    }

    /// <summary>
    /// Vanilla wires a 追踪 button onto every journal row. Posting rows are
    /// display-only and never enter <c>EClass.game.quests.list</c>, so pinning one
    /// gives <see cref="WidgetQuestTracker"/> no row to build: <c>items</c> stays
    /// empty, the freshly activated widget <c>Deactivate()</c>s itself from inside
    /// its own <c>OnActivate -> Refresh</c>, and ContentQuest's follow-up
    /// <c>WidgetQuestTracker.Instance.Refresh()</c> then runs <c>RebuildLayout</c>
    /// on the DestroyImmediate'd object (NRE at <c>Component.get_transform</c>).
    ///
    /// A row that cannot be tracked must not offer the button. Called on every
    /// journal rebuild, so vanilla's onInstantiate re-wiring never survives.
    /// </summary>
    static void SuppressTrackButton(object list)
    {
        if (list is not UIList ui)
        {
            return;
        }

        foreach (UIList.ButtonPair pair in ui.buttons)
        {
            if (pair.component is not ItemGeneral row)
            {
                continue;
            }

            UIButton? track = row.button3;
            if (track == null)
            {
                continue;
            }

            bool mine = pair.obj is QuestNpcLaborPDispatch;

            try
            {
                // Vanilla paints the pin by activating/deactivating button3.icon's
                // GameObject (ContentQuest.RefreshTrackButtons -> ClassExtension.SetActive),
                // so leave icon and interactable alone: touching them is what leaves a
                // row stuck in the "pressed" look. Hide the whole button on our rows,
                // and explicitly show it on every other row -- UIList recycles
                // ItemGeneral instances, so a hidden button would otherwise follow the
                // component onto a vanilla quest.
                if (mine)
                {
                    // SetOnClick also drops the vanilla listener (track toggle + tracker
                    // refresh), leaving a no-op so a stray click cannot rearm the crash.
                    track.SetOnClick(() => { });
                }

                track.gameObject.SetActive(!mine);
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("pdispatch journal track button: " + ex.Message);
            }
        }
    }

    static MethodInfo? FindSingleArg(Type t, string name)
    {
        foreach (MethodInfo mi in t.GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            if (mi.Name == name && mi.GetParameters().Length == 1)
            {
                return mi;
            }
        }

        return null;
    }

    /// <summary>Row click: recall an active posting, otherwise open the picker.</summary>
    internal static void OpenFromDef(string defId)
    {
        try
        {
            PermanentDispatchDef? def = PermanentDispatchDef.GetById(defId);
            if (def == null)
            {
                return;
            }

            PermanentDispatchMission? active = PermanentDispatchManager.FindByDef(defId);
            if (active != null)
            {
                OpenRecallMenu(def, active);
                return;
            }

            if (def.IsGuildTask && PermanentDispatchDef.PcGuildRank(def.GuildId) < PermanentDispatchDef.GuildRankGate)
            {
                Msg.Say(LaborText.T("pdisp.err.guildRank", def.Name));
                SE.Beep();
                return;
            }

            OpenPicker(def);
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("pdispatch open: " + ex.Message);
            try { SE.Beep(); } catch { }
        }
    }

    static void OpenRecallMenu(PermanentDispatchDef def, PermanentDispatchMission active)
    {
        var rows = new List<int> { 1, 0 };
        LayerList menu = EClass.ui.AddLayer<LayerList>();
        PersonPickerUi.SetOpenPicker(menu);
        menu.SetList2(
            rows,
            id => id == 1 ? LaborText.T("pdisp.ui.recall") : LaborText.T("pdisp.ui.close"),
            (id, item) =>
            {
                if (id != 1)
                {
                    SE.Click();
                    return;
                }

                if (!PermanentDispatchManager.TryRecall(active.missionId))
                {
                    Msg.Say(LaborText.T("pdisp.err.recallFail"));
                    SE.Beep();
                    return;
                }

                SE.Click();
            },
            (id, item) =>
            {
                try { item.DisableIcon(); } catch { }
                try
                {
                    item.button1?.mainText?.SetText(
                        id == 1 ? LaborText.T("pdisp.ui.recall") : LaborText.T("pdisp.ui.close"),
                        id == 1 ? FontColor.Bad : FontColor.Default);
                }
                catch { }

                try { item.Build(); } catch { }
                if (id == 1)
                {
                    try
                    {
                        item.button1?.subText?.SetText(
                            LaborText.T("pdisp.ui.activeSub", active.workerName, active.monthsPaid),
                            FontColor.Default);
                    }
                    catch { }
                }
            })
        .SetSize(520f);
    }

    static void OpenPicker(PermanentDispatchDef def)
    {
        var candidates = new List<Chara>();
        foreach (Chara c in TownLaborManager.ListCandidates(null))
        {
            try
            {
                if (!PermanentDispatchManager.IsBusy(c.uid) && def.GateMet(c))
                {
                    candidates.Add(c);
                }
            }
            catch { }
        }

        if (candidates.Count == 0)
        {
            // The gate lives here instead of in the posting text: when nobody
            // qualifies is the one moment the player actually needs to see it.
            Msg.Say(LaborText.T("pdisp.ui.noCandidate", def.Name) + " " + def.GateText());
            SE.Beep();
            return;
        }

        var uids = new List<int>();
        foreach (Chara c in candidates)
        {
            uids.Add(c.uid);
        }

        LayerList pick = EClass.ui.AddLayer<LayerList>();
        PersonPickerUi.SetOpenPicker(pick);
        pick.SetList2(
            uids,
            uid =>
            {
                Chara? c = candidates.Find(x => x.uid == uid);
                string name = c?.NameSimple ?? c?.Name ?? ("#" + uid);
                int skill = def.GateSkill > 0 ? SafeSkill(c, def.GateSkill) : 0;
                string label = skill > 0
                    ? name + "  " + PermanentDispatchDef.SkillName(def.GateSkill) + ":" + skill
                    : name;

                // Pay is base + rate x skill, so the board can only quote the
                // floor; here we know the candidate, so show what they'd earn.
                try
                {
                    int pay = c != null ? def.MonthlyMoney(c) : 0;
                    if (pay > 0)
                    {
                        label += "  " + LaborText.T("reward.money", pay);
                    }
                }
                catch { }

                return label;
            },
            (uid, item) =>
            {
                Chara? c = candidates.Find(x => x.uid == uid);
                if (c == null)
                {
                    return;
                }

                string? err = PermanentDispatchManager.TryStart(c, def);
                if (err != null)
                {
                    Msg.Say(err);
                    SE.Beep();
                    return;
                }

                SE.Click();
            },
            (uid, item) =>
            {
                try { item.DisableIcon(); } catch { }
            })
        .SetSize(520f);
    }

    static int SafeSkill(Chara? c, int id)
    {
        try { return Math.Max(0, c?.Evalue(id) ?? 0); } catch { return 0; }
    }
}
