using System;
using System.Collections.Generic;
using System.Linq;
using NpcLabor.CoCraft;
using UnityEngine;

namespace NpcLabor.Dispatch;

/// <summary>
/// Quest board UI for dungeon dispatch (slice D).
/// Multi-select up to 4 members per dungeon; mission tracker list.
/// </summary>
internal static class DungeonDispatchUi
{
    internal const int TabDispatch = 3;

    // Multi-select state for person picker.
    static readonly HashSet<int> PendingSelection = new HashSet<int>();
    static DungeonDispatchTarget? PendingTarget;
    static int PendingExploreWeeks = 1;
    static LayerQuestBoard? PendingBoard;

    internal static void OpenBoard(LayerQuestBoard? board)
    {
        try
        {
            // Master switch. This is the single choke point for the whole board, so the
            // craft slice hanging off it is parked along with dispatch.
            if (!LaborConfig.FeatureEnabled)
            {
                Msg.Say(NpcLabor.LaborText.T("cfg.disabled"));
                SE.Beep();
                return;
            }

            if (!IsAtPcFactionHome())
            {
                Msg.Say(NpcLabor.LaborText.T(
                    "dis.msg.baseOnly",
                    NpcLabor.LaborTerms.Dispatch));
                SE.Beep();
                return;
            }

            ShowMainList(board);
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("dispatch UI open failed: " + ex);
        }
    }

    internal static bool IsAtPcFactionHome()
    {
        try
        {
            return EClass._zone != null && EClass._zone.IsPCFaction;
        }
        catch
        {
            return false;
        }
    }

    static void ShowMainList(LayerQuestBoard? board)
    {
        FactionBranch? branch = EClass.BranchOrHomeBranch ?? EClass.Branch;
        var rows = new List<DispatchRow>();

        // Base production is not listed here: it is set up at the workbench itself,
        // through the 协助 chip on LayerCraft (see LayerCraftPatches.OpenAssistantMenu).

        // Active missions first (task tracker view).
        if (DungeonDispatchManager.Count > 0)
        {
            rows.Add(DispatchRow.Header(NpcLabor.LaborText.T("dis.ui.activeHeader")));
            foreach (DungeonDispatchMission m in DungeonDispatchManager.All)
            {
                rows.Add(DispatchRow.Active(m));
            }
        }

        // The dispatch switch folds into the same unlock gate the player already sees,
        // so a closed slice reads as closed instead of silently missing. Declared up
        // front because a short-circuited out argument leaves the local unassigned.
        bool dispatchOn = NpcLabor.LaborConfig.FeatureDispatch;
        string? regionDeny = null;
        string? dungeonDeny = null;
        bool canRegion = dispatchOn && NpcLabor.LaborTerms.CanRegionDispatch(out regionDeny);
        bool canDungeon = dispatchOn && NpcLabor.LaborTerms.CanDungeonExplore(out dungeonDeny);
        if (!dispatchOn)
        {
            rows.Add(DispatchRow.Header("— " + NpcLabor.LaborText.T("cfg.disabled") + " —"));
        }

        List<DungeonDispatchTarget> regions = canRegion
            ? DungeonDispatchTargets.ListRegionTargets(branch)
            : new List<DungeonDispatchTarget>();
        if (canRegion && regions.Count > 0)
        {
            rows.Add(DispatchRow.Header("— " + NpcLabor.LaborTerms.RegionDispatch + " —"));
            foreach (DungeonDispatchTarget t in regions)
            {
                rows.Add(DispatchRow.FromTarget(t));
            }
        }
        else if (!canRegion && dispatchOn)
        {
            rows.Add(DispatchRow.Header(regionDeny ?? NpcLabor.LaborText.T("dis.error.notUnlocked", NpcLabor.LaborTerms.RegionDispatch)));
        }

        List<DungeonDispatchTarget> targets = canDungeon
            ? DungeonDispatchTargets.ListNearby(branch)
            : new List<DungeonDispatchTarget>();
        var randoms = targets.Where(t => t.IsRandomSite).ToList();
        var fixeds = targets.Where(t => !t.IsRandomSite).ToList();

        if (canDungeon && randoms.Count > 0)
        {
            rows.Add(DispatchRow.Header(NpcLabor.LaborText.T("dis.ui.randomHeader", NpcLabor.LaborTerms.DungeonExplore)));
            foreach (DungeonDispatchTarget t in randoms)
            {
                rows.Add(DispatchRow.FromTarget(t));
            }
        }

        if (canDungeon && fixeds.Count > 0)
        {
            rows.Add(DispatchRow.Header(NpcLabor.LaborText.T("dis.ui.fixedHeader", NpcLabor.LaborTerms.DungeonExplore)));
            foreach (DungeonDispatchTarget t in fixeds)
            {
                rows.Add(DispatchRow.FromTarget(t));
            }
        }
        else if (!canDungeon && dispatchOn)
        {
            rows.Add(DispatchRow.Header(dungeonDeny ?? NpcLabor.LaborText.T("dis.error.notUnlocked", NpcLabor.LaborTerms.DungeonExplore)));
        }

        if (DungeonDispatchManager.Count == 0 && targets.Count == 0 && regions.Count == 0 && canRegion && canDungeon)
        {
            rows.Add(DispatchRow.Header(NpcLabor.LaborText.T("dis.ui.noTargets")));
        }

        LayerList menu = EClass.ui.AddLayer<LayerList>();
            PersonPickerUi.SetOpenPicker(menu);
        menu.SetList2(
                rows,
                r => r.Label,
                (r, item) =>
                {
                    if (r.Kind == DispatchRowKind.Header)
                    {
                        SE.Beep();
                        return;
                    }

                    if (r.Kind == DispatchRowKind.Active && r.Mission != null)
                    {
                        OpenActiveMenu(board, r.Mission);
                        return;
                    }

                    if (r.Kind == DispatchRowKind.Target && r.Target != null)
                    {
                        if (DungeonDispatchManager.IsTargetBusy(r.Target))
                        {
                    Msg.Say(r.Target.IsRegion
                        ? NpcLabor.LaborText.T("dis.error.regionBusy", NpcLabor.LaborTerms.RegionDispatch)
                        : NpcLabor.LaborText.T("dis.error.dungeonBusy", NpcLabor.LaborTerms.DungeonExplore));
                            SE.Beep();
                            return;
                        }

                        // Show target info at selection time (not on final confirm).
                        try
                        {
                            string info = DungeonDispatchTargets.TargetInfoLine(r.Target);
                            Msg.Say(info);
                        }
                        catch
                        {
                        }

                        if (r.Target.IsRegion)
                        {
                            OpenRegionWeekPicker(board, r.Target);
                        }
                        else
                        {
                            PendingExploreWeeks = 1;
                            OpenMemberPicker(board, r.Target);
                        }
                    }
                },
                (r, item) =>
                {
                    StyleMainRow(item, r);
                })
            .SetSize(680f);

        try
        {
            menu.SetHeader(NpcLabor.LaborTerms.Dispatch);
        }
        catch
        {
            try
            {
                if (menu.windows != null && menu.windows.Count > 0)
                {
                    menu.windows[0].SetCaption(NpcLabor.LaborTerms.Dispatch);
                }
            }
            catch
            {
            }
        }
    }

    static void StyleMainRow(ItemGeneral item, DispatchRow r)
    {
        try
        {
            if (r.Kind == DispatchRowKind.Active)
            {
                Chara? c = r.Mission?.GetMembers().FirstOrDefault();
                if (c != null)
                {
                    PersonPickerUi.StyleRow(item, c.uid, r.Label, r.Sub);
                    return;
                }
            }

            try
            {
                item.DisableIcon();
            }
            catch
            {
            }

            try
            {
                if (item.button1?.mainText != null)
                {
                    FontColor color = r.Kind == DispatchRowKind.Header
                        ? FontColor.Default
                        : FontColor.ButtonGeneral;
                    // Avoid ItemGeneral.SetSubText(...).lang() wiping custom Chinese.
                    item.button1.mainText.SetText(r.Label ?? "", color);
                }
            }
            catch
            {
            }

            try
            {
                item.Build();
            }
            catch
            {
            }

            ApplyRawSubText(item, r.Sub, r.Kind == DispatchRowKind.Target ? 320 : 400);
        }
        catch
        {
        }
    }

    /// <summary>
    /// ItemGeneral.SetSubText runs lang.lang() which drops free-form Chinese.
    /// Write subText directly.
    /// </summary>
    static void ApplyRawSubText(ItemGeneral item, string? sub, int mainWidth = 320)
    {
        if (item?.button1 == null || string.IsNullOrEmpty(sub))
        {
            return;
        }

        try
        {
            if (item.button1.subText == null)
            {
                return;
            }

            item.button1.subText.SetActive(enable: true);
            try
            {
                item.button1.subText.SetText(sub, FontColor.Default);
            }
            catch
            {
                item.button1.subText.text = sub;
            }

            item.button1.subText.alignment = TextAnchor.MiddleRight;
            try
            {
                item.button1.subText.rectTransform.anchoredPosition = new Vector2(mainWidth, 0f);
            }
            catch
            {
            }

            try
            {
                if (item.button1.mainText != null)
                {
                    RectTransform mrt = item.button1.mainText.rectTransform;
                    mrt.sizeDelta = new Vector2(Mathf.Min(mrt.sizeDelta.x <= 0 ? mainWidth : mrt.sizeDelta.x, mainWidth - 40f), mrt.sizeDelta.y);
                }
            }
            catch
            {
            }
        }
        catch
        {
        }
    }

    static void OpenActiveMenu(LayerQuestBoard? board, DungeonDispatchMission mission)
    {
        string detail = DungeonDispatchManager.MissionTrackerText(mission);
        try
        {
            Msg.Say(detail.Replace("\n", " | "));
        }
        catch
        {
        }

        // Only action left: recall. Detail/loot already printed to log.
        var options = new List<int> { 1 };
        LayerList menu = EClass.ui.AddLayer<LayerList>();
            PersonPickerUi.SetOpenPicker(menu);
        menu.SetList2(
                options,
                id => NpcLabor.LaborText.T("dis.ui.recallPartial"),
                (id, item) =>
                {
                    bool ok = DungeonDispatchManager.TryRecall(mission.missionId);
                    if (!ok)
                    {
                        SE.Beep();
                        Msg.Say(NpcLabor.LaborText.T("dis.ui.recallFail"));
                    }
                    else
                    {
                        SE.Click();
                    }

                    try
                    {
                        ShowMainList(board);
                    }
                    catch
                    {
                    }
                },
                (id, item) =>
                {
                    try
                    {
                        item.DisableIcon();
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (item.button1?.mainText != null)
                        {
                            item.button1.mainText.SetText(NpcLabor.LaborText.T("dis.ui.recallPartial"), FontColor.Bad);
                        }
                    }
                    catch
                    {
                    }

                    try
                    {
                        item.Build();
                    }
                    catch
                    {
                    }

                    try
                    {
                        ApplyRawSubText(
                            item,
                            NpcLabor.LaborText.T("dis.ui.progress", mission.ProgressPercent, mission.DaysLeft.ToString("0.0")),
                            280);
                    }
                    catch
                    {
                    }
                })
            .SetSize(420f);

        try
        {
            menu.SetHeader(NpcLabor.LaborText.T("dis.ui.taskHeader", mission.zoneName));
        }
        catch
        {
        }
    }


    static void OpenRegionWeekPicker(LayerQuestBoard? board, DungeonDispatchTarget target)
    {
        if (target == null || !target.IsRegion)
        {
            PendingExploreWeeks = 1;
            OpenMemberPicker(board, target);
            return;
        }

        var weeks = new List<int> { 1, 2, 3, 4 };
        LayerList menu = EClass.ui.AddLayer<LayerList>();
            PersonPickerUi.SetOpenPicker(menu);
        menu.SetList2(
                weeks,
                w => NpcLabor.LaborText.T("dis.ui.weeks", w),
                (w, item) =>
                {
                    PendingExploreWeeks = Mathf.Clamp(w, 1, 4);
                    try
                    {
                        string info = DungeonDispatchTargets.RegionInfoLine(target, PendingExploreWeeks);
                        Msg.Say(info);
                    }
                    catch
                    {
                    }

                    SE.Click();
                    OpenMemberPicker(board, target);
                },
                (w, item) =>
                {
                    try { item.DisableIcon(); } catch { }
                    try
                    {
                        if (item.button1?.mainText != null)
                        {
                            string label = NpcLabor.LaborText.T("dis.ui.weeks", w);
                            item.button1.mainText.SetText(label, FontColor.ButtonGeneral);
                        }
                    }
                    catch
                    {
                    }

                    try { item.Build(); } catch { }
                })
            .SetSize(640f);

        string header = NpcLabor.LaborText.T("dis.ui.chooseWeeks", target.Name);
        try { menu.SetHeader(header); }
        catch
        {
            try
            {
                if (menu.windows != null && menu.windows.Count > 0)
                {
                    menu.windows[0].SetCaption(header);
                }
            }
            catch
            {
            }
        }
    }


    const int QuickSelectLast = -2;
    const int QuickSelectMaxHarvest = -3;

    static void ApplyQuickSelection(IList<Chara> members, DungeonDispatchTarget target, LayerQuestBoard? board, List<Chara> candidates, string emptyMsg)
    {
        PendingSelection.Clear();
        if (members == null || members.Count == 0)
        {
            Msg.Say(emptyMsg);
            SE.Beep();
            ShowMemberPickerList(board, target, candidates);
            return;
        }

        int n = 0;
        foreach (Chara c in members)
        {
            if (c == null || c.uid <= 0)
            {
                continue;
            }

            if (n >= DungeonDispatchMission.MaxMembersPerDungeon)
            {
                break;
            }

            PendingSelection.Add(c.uid);
            n++;
        }

        if (target != null && target.IsRegion)
        {
            // Restore last explore weeks only when reusing the last-dispatch preset.
            // Max-harvest keeps the week already chosen on the region week screen.
        }

        if (PendingSelection.Count == 0)
        {
            Msg.Say(emptyMsg);
            SE.Beep();
        }
        else
        {
            SE.Click();
        }

        ShowMemberPickerList(board, target, candidates);
    }

    static void ApplyLastDispatchSelection(DungeonDispatchTarget target, LayerQuestBoard? board, List<Chara> candidates)
    {
        List<Chara> last = DungeonDispatchManager.GetLastDispatchMembers(target);
        if (last.Count == 0)
        {
            Msg.Say(NpcLabor.LaborText.T("dis.ui.noLastTeam"));
            SE.Beep();
            ShowMemberPickerList(board, target, candidates);
            return;
        }

        if (target != null && target.IsRegion)
        {
            PendingExploreWeeks = DungeonDispatchManager.GetLastExploreWeeks(target);
        }

        ApplyQuickSelection(last, target, board, candidates, NpcLabor.LaborText.T("dis.ui.noLastTeam"));
    }

    static void ApplyMaxHarvestSelection(DungeonDispatchTarget target, LayerQuestBoard? board, List<Chara> candidates)
    {
        List<Chara> best = DungeonDispatchManager.PickMaxHarvestMembers(target);
        ApplyQuickSelection(best, target, board, candidates, NpcLabor.LaborText.T("dis.ui.noAutoPick"));
    }

    static void OpenMemberPicker(LayerQuestBoard? board, DungeonDispatchTarget target)
    {
        List<Chara> candidates = DungeonDispatchManager.ListCandidates();
        if (candidates.Count == 0)
        {
            Msg.Say(NpcLabor.LaborText.T("dis.ui.noCandidates"));
            SE.Beep();
            return;
        }

        PendingSelection.Clear();
        PendingTarget = target;
        PendingBoard = board;

        ShowMemberPickerList(board, target, candidates);
    }

    static void ShowMemberPickerList(LayerQuestBoard? board, DungeonDispatchTarget target, List<Chara> candidates)
    {
        // Rows: confirm, quick selects, then each candidate (toggle).
        bool hasLast = DungeonDispatchManager.GetLastDispatchMembers(target).Count > 0;
        var rows = new List<int> { -1 }; // confirm
        if (hasLast)
        {
            rows.Add(QuickSelectLast);
        }

        rows.Add(QuickSelectMaxHarvest);
        foreach (Chara c in candidates)
        {
            rows.Add(c.uid);
        }

        List<Chara> selected = ResolvePendingMembers();
        string teamLine = selected.Count > 0
            ? DungeonDispatchTargets.TeamSummaryLine(target, selected)
            : NpcLabor.LaborText.T("dis.stat.none");

        LayerList menu = EClass.ui.AddLayer<LayerList>();
            PersonPickerUi.SetOpenPicker(menu);
        menu.SetList2(
                rows,
                id =>
                {
                    if (id == -1)
                    {
                        return NpcLabor.LaborText.T("dis.ui.confirmSel", PendingSelection.Count, DungeonDispatchMission.MaxMembersPerDungeon);
                    }

                    if (id == QuickSelectLast)
                    {
                        return NpcLabor.LaborText.T("dis.ui.lastTeam");
                    }

                    if (id == QuickSelectMaxHarvest)
                    {
                        return target != null && target.IsRegion
                            ? NpcLabor.LaborText.T("dis.ui.maxHarvest")
                            : NpcLabor.LaborText.T("dis.ui.maxCombat");
                    }

                    Chara? c = RefChara.Get(id);
                    if (c == null)
                    {
                        return "#" + id;
                    }

                    string n = c.NameSimple ?? c.Name ?? ("#" + id);
                    string mark = PendingSelection.Contains(id) ? "✓ " : "";
                    return mark + PersonPickerUi.HobbyTaggedLabel(
                        c,
                        DungeonDispatchTargets.RelevantSkillIds(),
                        NpcLabor.LaborText.T("dis.ui.partyTag"),
                        n);
                },
                (id, item) =>
                {
                    if (id == -1)
                    {
                        ConfirmSelection(board, target);
                        return;
                    }

                    if (id == QuickSelectLast)
                    {
                        ApplyLastDispatchSelection(target, board, candidates);
                        return;
                    }

                    if (id == QuickSelectMaxHarvest)
                    {
                        ApplyMaxHarvestSelection(target, board, candidates);
                        return;
                    }

                    ToggleMember(id, board, target, candidates);
                },
                (id, item) =>
                {
                    if (id == -1)
                    {
                        try
                        {
                            item.DisableIcon();
                        }
                        catch
                        {
                        }

                        try
                        {
                            if (item.button1?.mainText != null)
                            {
                                item.button1.mainText.SetText(
                                    NpcLabor.LaborText.T("dis.ui.confirmSel", PendingSelection.Count, DungeonDispatchMission.MaxMembersPerDungeon),
                                    PendingSelection.Count > 0 ? FontColor.Good : FontColor.Default);
                            }
                        }
                        catch
                        {
                        }

                        try
                        {
                            item.Build();
                        }
                        catch
                        {
                        }

                        // Team totals: power / skills / success — no reward-tier text.
                        // Short main label leaves room; put totals further right.
                        ApplyRawSubText(item, teamLine, 160);
                        return;
                    }

                    if (id == QuickSelectLast || id == QuickSelectMaxHarvest)
                    {
                        string qLabel = id == QuickSelectLast
                            ? NpcLabor.LaborText.T("dis.ui.lastTeam")
                            : (target != null && target.IsRegion
                                ? NpcLabor.LaborText.T("dis.ui.maxHarvest")
                                : NpcLabor.LaborText.T("dis.ui.maxCombat"));
                        string qSub = id == QuickSelectLast
                            ? NpcLabor.LaborText.T("dis.ui.restoreLast")
                            : (target != null && target.IsRegion
                                ? NpcLabor.LaborText.T("dis.ui.autoPickGather")
                                : NpcLabor.LaborText.T("dis.ui.autoPickPower"));
                        try
                        {
                            item.DisableIcon();
                        }
                        catch
                        {
                        }

                        try
                        {
                            if (item.button1?.mainText != null)
                            {
                                item.button1.mainText.SetText(qLabel, FontColor.Good);
                            }
                        }
                        catch
                        {
                        }

                        try
                        {
                            item.Build();
                        }
                        catch
                        {
                        }

                        ApplyRawSubText(item, qSub, 160);
                        return;
                    }

                    Chara? c = RefChara.Get(id);
                    string label;
                    string sub = "";
                    if (c == null)
                    {
                        label = "#" + id;
                    }
                    else
                    {
                        string n = c.NameSimple ?? c.Name ?? ("#" + id);
                        string mark = PendingSelection.Contains(id) ? "✓ " : "";
                        label = mark + PersonPickerUi.HobbyTaggedLabel(
                            c,
                            DungeonDispatchTargets.RelevantSkillIds(),
                            NpcLabor.LaborText.T("dis.ui.partyTag"),
                            n);
                        sub = DungeonDispatchTargets.MemberSkillLine(c, target);
                    }

                    PersonPickerUi.StyleRow(item, id, label, sub);
                })
            .SetSize(640f);

        string header = NpcLabor.LaborText.T(
            "dis.ui.pickHeader",
            target.Name,
            DungeonDispatchMission.MaxMembersPerDungeon);
        if (target.IsRegion)
        {
            header = NpcLabor.LaborText.T(
                "dis.ui.pickHeaderWeeks",
                target.Name,
                PendingExploreWeeks,
                DungeonDispatchMission.MaxMembersPerDungeon);
        }
        try
        {
            menu.SetHeader(header);
        }
        catch
        {
            try
            {
                if (menu.windows != null && menu.windows.Count > 0)
                {
                    menu.windows[0].SetCaption(header);
                }
            }
            catch
            {
            }
        }

        // Also dump team line to log when selecting so totals are obvious.
        if (selected.Count > 0)
        {
            try
            {
                Msg.Say(teamLine);
            }
            catch
            {
            }
        }
    }

    static List<Chara> ResolvePendingMembers()
    {
        var members = new List<Chara>();
        foreach (int uid in PendingSelection)
        {
            Chara? c = RefChara.Get(uid);
            if (c != null)
            {
                members.Add(c);
            }
        }

        return members;
    }

    static void ToggleMember(int id, LayerQuestBoard? board, DungeonDispatchTarget target, List<Chara> candidates)
    {
        if (PersonPickerUi.SwallowRightClick("dispatch.toggleMember"))
        {
            return;
        }

        if (PendingSelection.Contains(id))
        {
            PendingSelection.Remove(id);
            SE.Click();
        }
        else
        {
            if (PendingSelection.Count >= DungeonDispatchMission.MaxMembersPerDungeon)
            {
                Msg.Say(NpcLabor.LaborText.T("dis.ui.tooMany", DungeonDispatchMission.MaxMembersPerDungeon));
                SE.Beep();
                return;
            }

            PendingSelection.Add(id);
            SE.Click();
        }

        // Refresh picker to show checkmarks.
        try
        {
            ShowMemberPickerList(board, target, candidates);
        }
        catch
        {
        }
    }

    static void ConfirmSelection(LayerQuestBoard? board, DungeonDispatchTarget target)
    {
        if (PendingSelection.Count == 0)
        {
            Msg.Say(NpcLabor.LaborText.T("dis.ui.pickFirst"));
            SE.Beep();
            return;
        }

        var members = new List<Chara>();
        foreach (int uid in PendingSelection)
        {
            Chara? c = RefChara.Get(uid);
            if (c != null)
            {
                members.Add(c);
            }
        }

        if (members.Count == 0)
        {
            Msg.Say(NpcLabor.LaborText.T("dis.ui.invalidChoice"));
            SE.Beep();
            return;
        }

        // Final confirm: no extra preview/subtext; dungeon details were shown on target pick.
        var opts = new List<int> { 1, 0 };
        LayerList menu = EClass.ui.AddLayer<LayerList>();
            PersonPickerUi.SetOpenPicker(menu);
        menu.SetList2(
                opts,
                id => id == 1
                    ? NpcLabor.LaborText.T("dis.ui.confirmSend")
                    : NpcLabor.LaborText.T("common.cancel"),
                (id, item) =>
                {
                    if (id != 1)
                    {
                        SE.Click();
                        return;
                    }

                    string? err = DungeonDispatchManager.TryStart(members, target, PendingExploreWeeks);
                    if (err != null)
                    {
                        Msg.Say(err);
                        SE.Beep();
                        return;
                    }

                    PendingSelection.Clear();
                    PendingTarget = null;
                    SE.Click();
                    try
                    {
                        board?.Close();
                    }
                    catch
                    {
                    }
                },
                (id, item) =>
                {
                    try
                    {
                        item.DisableIcon();
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (item.button1?.mainText != null)
                        {
                            item.button1.mainText.SetText(
                                id == 1
                                    ? NpcLabor.LaborText.T("dis.ui.confirmSend")
                                    : NpcLabor.LaborText.T("common.cancel"),
                                id == 1 ? FontColor.Good : FontColor.Bad);
                        }
                    }
                    catch
                    {
                    }

                    try
                    {
                        item.Build();
                    }
                    catch
                    {
                    }
                })
            .SetSize(420f);

        try
        {
            menu.SetHeader(NpcLabor.LaborText.T("dis.ui.confirmSend"));
        }
        catch
        {
        }
    }

    enum DispatchRowKind
    {
        Header,
        Active,
        Target,
    }

    sealed class DispatchRow
    {
        public DispatchRowKind Kind;
        public string Label = "";
        public string Sub = "";
        public DungeonDispatchMission? Mission;
        public DungeonDispatchTarget? Target;

        public static DispatchRow Header(string text)
            => new DispatchRow { Kind = DispatchRowKind.Header, Label = text };

        public static DispatchRow Active(DungeonDispatchMission m)
        {
            string names = m.MemberNames();
            if (names.Length > 14)
            {
                names = names.Substring(0, 14) + "…";
            }

            return new DispatchRow
            {
                Kind = DispatchRowKind.Active,
                Mission = m,
                Label = NpcLabor.LaborText.T("dis.ui.taskHeader", DungeonDispatchTargets.TruncateName(m.zoneName, 10)),
                Sub = names + "  " + NpcLabor.LaborText.T("dis.ui.progress", m.ProgressPercent, m.DaysLeft.ToString("0.0")),
            };
        }

        public static DispatchRow FromTarget(DungeonDispatchTarget t)
        {
            bool busy = false;
            try
            {
                busy = DungeonDispatchManager.IsTargetBusy(t);
            }
            catch
            {
            }

            // Region names are short (平原/森林...); dungeons may be longer.
            int nameMax = t.IsRegion ? 6 : (busy ? 8 : 10);
            string name = t.IsRegion
                ? (t.Name ?? NpcLabor.LaborText.T("dis.q.area"))
                : DungeonDispatchTargets.TruncateName(t.Name ?? "", nameMax);
            if (busy)
            {
                name += NpcLabor.LaborText.T("dis.ui.busySuffix");
            }

            // Put compass/dist/danger on the main label so it never depends on subText.lang().
            string meta = DungeonDispatchTargets.TargetListSub(t);
            string label = string.IsNullOrEmpty(meta) ? name : (name + "  " + meta);

            return new DispatchRow
            {
                Kind = DispatchRowKind.Target,
                Target = t,
                Label = label,
                Sub = busy ? NpcLabor.LaborText.T("dis.ui.inProgress") : meta,
            };
        }

    }
}
