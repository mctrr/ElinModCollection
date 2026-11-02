using NpcLabor.TownLabor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace NpcLabor.Dispatch;

/// <summary>
/// PARTIAL: Quest tracker lifecycle (create/sanitize/refresh/dedupe) plus the
/// mission display text.
/// Split from DungeonDispatchManager.cs (2026-10-02).
/// </summary>
internal static partial class DungeonDispatchManager
{
    internal static bool IsOurDispatchTrackerQuest(Quest? q)
    {
        if (q == null)
        {
            return false;
        }

        if (q is QuestNpcLaborDispatch)
        {
            return true;
        }

        try
        {
            string id = q.id ?? string.Empty;
            if (id.StartsWith("npclabor_dispatch_", StringComparison.Ordinal))
            {
                return true;
            }
        }
        catch { }

        // Legacy Dummy rows may lose idSource / type but keep title crumbs — still strip by type name.
        try
        {
            string tn = q.GetType().Name ?? string.Empty;
            if (tn.IndexOf("NpcLaborDispatch", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// F9/auto-save: keep live typed pins so tracker order stays stable. Only drop
    /// Dummy / missionId=0 / orphan / duplicate rows that would double on next load.
    /// </summary>
    internal static void SanitizeBrokenTrackerQuestsForSave()
    {
        try
        {
            QuestManager? qm = EClass.game?.quests;
            if (qm?.list == null)
            {
                return;
            }

            var seen = new HashSet<int>();
            foreach (Quest q in qm.list.ToList())
            {
                if (!IsOurDispatchTrackerQuest(q))
                {
                    continue;
                }

                int mid = 0;
                bool typed = false;
                try
                {
                    if (q is QuestNpcLaborDispatch dq)
                    {
                        typed = true;
                        mid = dq.missionId;
                    }
                    else
                    {
                        string id = q.id ?? string.Empty;
                        const string prefix = "npclabor_dispatch_";
                        if (id.StartsWith(prefix, StringComparison.Ordinal))
                        {
                            int.TryParse(id.Substring(prefix.Length), out mid);
                        }
                    }
                }
                catch { mid = 0; }

                bool orphan = mid <= 0 || FindByMissionId(mid) == null;
                bool dup = mid > 0 && !seen.Add(mid);
                // Always drop non-typed (QuestDummy / legacy) — typed live rows stay.
                bool drop = !typed || orphan || dup;
                if (!drop)
                {
                    try
                    {
                        if (q is QuestNpcLaborDispatch live)
                        {
                            live.track = true;
                            live.deadline = 0;
                            live.EnsureSafePerson();
                        }
                    }
                    catch { }
                    continue;
                }

                try { q.track = false; } catch { }
                try { qm.Remove(q); } catch { }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("dispatch sanitize broken trackers: " + ex.Message);
        }
    }

    /// <summary>
    /// Ask vanilla WidgetQuestTracker to repaint from quests.list.
    /// Never DestroyImmediate rows here — that caused SetActive NREs.
    /// </summary>
    internal static void RequestQuestTrackerRefresh()
    {
        try
        {
            if (WidgetQuestTracker.Instance != null)
            {
                WidgetQuestTracker.Instance.Refresh();
                return;
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("dispatch tracker refresh instance: " + ex.Message);
        }

        try
        {
            var w = EClass.ui?.widgets?.GetWidget("QuestTracker") as WidgetQuestTracker;
            w?.Refresh();
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("dispatch tracker refresh widget: " + ex.Message);
        }
    }


    static void TryStartTrackerQuest(DungeonDispatchMission mission)
    {
        if (mission == null || mission.missionId <= 0)
        {
            return;
        }

        try
        {
            // Guard: never Start if a live pin already exists for this mission.
            // Do not Remove+Start (that races the widget and can leave two rows).
            try
            {
                QuestManager? qm = EClass.game?.quests;
                if (qm?.list != null)
                {
                    foreach (Quest existing in qm.list)
                    {
                        if (existing is QuestNpcLaborDispatch dq && dq.missionId == mission.missionId)
                        {
                            try
                            {
                                dq.track = true;
                                dq.deadline = 0;
                                dq.isNew = false;
                                dq.EnsureSafePerson();
                            }
                            catch { }
                            // Same Quest object already in list — widget row already bound by ref.
                            // Do not Show/Refresh here; that only races concurrent Refresh loops.
                            return;
                        }
                    }

                    // Drop Dummy/orphan rows for this id only (list-level, no widget destroy).
                    string wantId = "npclabor_dispatch_" + mission.missionId;
                    foreach (Quest orphan in qm.list.ToList())
                    {
                        if (orphan is QuestNpcLaborDispatch)
                        {
                            continue;
                        }

                        if (orphan == null)
                        {
                            continue;
                        }

                        bool match = false;
                        try
                        {
                            match = string.Equals(orphan.id, wantId, StringComparison.Ordinal);
                        }
                        catch { match = false; }
                        if (!match)
                        {
                            continue;
                        }

                        try { orphan.track = false; } catch { }
                        try { qm.Remove(orphan); } catch { }
                    }
                }
            }
            catch { }

            var q = new QuestNpcLaborDispatch
            {
                missionId = mission.missionId,
                id = "npclabor_dispatch_" + mission.missionId,
            };

            // Assign uid + defaults like vanilla Quest.Create path.
            try
            {
                q.Init();
            }
            catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
            // deadline 0 = never expires (GameDate raw timestamp otherwise).
            // Setting hoursLeft here made multi-day dispatches "expire" in a few hours,
            // calling Quest.Fail → fame loss and recalling members out of the dungeon.
            q.deadline = 0;
            q.track = true;
            // isNew false: we are a synthetic pin, not a freshly accepted journal quest.
            q.isNew = false;

            // Quest.chara / QuestManager.OnShowDialog dereference person — must be non-null
            // and must NOT bind a client NPC (would steal dialogs / NRE on null person).
            try
            {
                q.SetClient(null, assignQuest: false);
            }
            catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
            q.EnsureSafePerson();

            QuestManager? startQm = EClass.game?.quests;
            if (startQm == null)
            {
                return;
            }

            startQm.Start(q);
            q.EnsureSafePerson();
            q.deadline = 0;
            q.track = true;
            q.isNew = false;
            // List-only safety net in case Start raced another pin for the same id.
            // Do not call WidgetQuestTracker.Refresh here — vanilla Start already Show()s.
            DedupeNpcLaborTrackerQuests();
            Plugin.LogDebug("dispatch tracker quest start id=" + mission.missionId);
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("dispatch tracker quest failed: " + ex.Message);
        }
    }

    static void RemoveTrackerQuest(DungeonDispatchMission? mission)
    {
        if (mission == null)
        {
            return;
        }

        try
        {
            QuestManager? qm = EClass.game?.quests;
            if (qm?.list == null)
            {
                return;
            }

            string wantId = "npclabor_dispatch_" + mission.missionId;
            foreach (Quest q in qm.list.ToList())
            {
                bool match = false;
                try
                {
                    if (q is QuestNpcLaborDispatch dq && dq.missionId == mission.missionId)
                    {
                        match = true;
                    }
                    else if (q != null && string.Equals(q.id, wantId, StringComparison.Ordinal))
                    {
                        // Legacy / QuestDummy rows that lost the concrete type or missionId.
                        match = true;
                    }
                }
                catch { }

                if (!match)
                {
                    continue;
                }

                try
                {
                    // Force pin off before remove so WidgetQuestTracker.ItemQuestTracker.Kill runs.
                    if (q != null)
                    {
                        q.track = false;
                    }
                }
                catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
                try
                {
                    if (q != null)
                    {
                        qm.Remove(q);
                    }
                }
                catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
            }

            // Vanilla ItemQuestTracker.Refresh kills rows whose quest left the list.
            RequestQuestTrackerRefresh();
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("dispatch tracker remove: " + ex.Message);
        }
    }

    /// <summary>
    /// QuestManager.Remove does not refresh WidgetQuestTracker. Force pin rows to drop
    /// when a dispatch quest ends (otherwise player must click the X manually).
    /// </summary>
    static void RefreshQuestTrackerWidget() => RequestQuestTrackerRefresh();

    /// <summary>
    /// Drop extra live quests.list entries that share the same NpcLabor missionId
    /// (typed or Dummy with our id prefix). Keeps one row per mission.
    /// List-only — never DestroyImmediate widget rows.
    /// </summary>
    internal static void DedupeNpcLaborTrackerQuests()
    {
        try
        {
            QuestManager? qm = EClass.game?.quests;
            if (qm?.list == null)
            {
                return;
            }

            var seenDispatch = new HashSet<int>();
            var seenTown = new HashSet<int>();

            foreach (Quest q in qm.list.ToList())
            {
                if (q == null)
                {
                    continue;
                }

                bool isDispatch = IsOurDispatchTrackerQuest(q);
                bool isTown = false;
                try { isTown = TownLaborManager.IsOurTownTrackerQuestPublic(q); } catch { isTown = false; }
                if (!isDispatch && !isTown)
                {
                    continue;
                }

                int mid = 0;
                bool typed = false;
                try
                {
                    if (q is QuestNpcLaborDispatch dq)
                    {
                        typed = true;
                        mid = dq.missionId;
                    }
                    else if (q is QuestNpcLaborTownLabor tq)
                    {
                        typed = true;
                        mid = tq.missionId;
                    }
                    else
                    {
                        string id = q.id ?? string.Empty;
                        const string dPrefix = "npclabor_dispatch_";
                        const string tPrefix = "npclabor_townlabor_";
                        if (id.StartsWith(dPrefix, StringComparison.Ordinal))
                        {
                            int.TryParse(id.Substring(dPrefix.Length), out mid);
                        }
                        else if (id.StartsWith(tPrefix, StringComparison.Ordinal))
                        {
                            int.TryParse(id.Substring(tPrefix.Length), out mid);
                        }
                    }
                }
                catch { mid = 0; }

                bool orphan;
                bool dup;
                if (isDispatch)
                {
                    orphan = mid <= 0 || FindByMissionId(mid) == null;
                    dup = mid > 0 && !seenDispatch.Add(mid);
                }
                else
                {
                    orphan = mid <= 0 || TownLaborManager.FindByMissionId(mid) == null;
                    dup = mid > 0 && !seenTown.Add(mid);
                }

                // Prefer typed live rows. Non-typed / orphan / dup always drop.
                bool drop = !typed || orphan || dup;
                if (!drop)
                {
                    try
                    {
                        if (q is QuestNpcLaborDispatch liveD)
                        {
                            liveD.track = true;
                            liveD.deadline = 0;
                            liveD.EnsureSafePerson();
                        }
                        else if (q is QuestNpcLaborTownLabor liveT)
                        {
                            liveT.track = true;
                            liveT.deadline = 0;
                            liveT.EnsureSafePerson();
                        }
                    }
                    catch { }
                    continue;
                }

                try { q.track = false; } catch { }
                try { qm.Remove(q); } catch { }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("dispatch dedupe trackers: " + ex.Message);
        }
    }


internal static void RefreshTrackerQuests()
    {
        try
        {
            // First pass: drop orphan / legacy / duplicate pins so we never keep two rows.
            try
            {
                QuestManager? qm = EClass.game?.quests;
                if (qm?.list != null)
                {
                    var seen = new HashSet<int>();
                    bool removed = false;
                    foreach (Quest q in qm.list.ToList())
                    {
                        if (!IsOurDispatchTrackerQuest(q))
                        {
                            continue;
                        }

                        int mid = 0;
                        try
                        {
                            if (q is QuestNpcLaborDispatch dq)
                            {
                                mid = dq.missionId;
                            }
                            else
                            {
                                string id = q.id ?? string.Empty;
                                const string prefix = "npclabor_dispatch_";
                                if (id.StartsWith(prefix, StringComparison.Ordinal))
                                {
                                    int.TryParse(id.Substring(prefix.Length), out mid);
                                }
                            }
                        }
                        catch { mid = 0; }

                        bool typed = q is QuestNpcLaborDispatch;
                        bool orphan = mid <= 0 || FindByMissionId(mid) == null;
                        bool dup = mid > 0 && !seen.Add(mid);
                        // Keep only one live typed pin; Dummy/legacy always drop and re-Start.
                        if (typed && !orphan && !dup)
                        {
                            continue;
                        }

                        try { q.track = false; } catch { }
                        try { qm.Remove(q); } catch { }
                        removed = true;
                    }

                    if (removed)
                    {
                        // Defer paint; Start/Show below will refresh once.
                    }
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }

            foreach (DungeonDispatchMission m in Missions)
            {
                bool has = false;
                try
                {
                    QuestManager? qm = EClass.game?.quests;
                    if (qm?.list != null)
                    {
                        foreach (Quest q in qm.list)
                        {
                            if (q is QuestNpcLaborDispatch dq && dq.missionId == m.missionId)
                            {
                                has = true;
                                try
                                {
                                    dq.track = true;
                                    dq.deadline = 0;
                                    dq.EnsureSafePerson();
                                }
                                catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
                                break;
                            }
                        }
                    }
                }
                catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
                if (!has)
                {
                    TryStartTrackerQuest(m);
                }
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
    }

    // --- moved down from the old monolith: mission tracker display text ---

    internal static string MissionTrackerText(DungeonDispatchMission m)
    {
        m.EnsureMemberList();
        string where;
        if (m.isRegion)
        {
            where = string.IsNullOrEmpty(m.regionKind)
                ? (m.zoneName ?? NpcLabor.LaborText.T("dis.q.area"))
                : DungeonDispatchTargets.RegionDisplayName(m.regionKind);
        }
        else
        {
            where = FormatFloorLabel(m.currentFloorLv);
        }

        string text = m.zoneName
            + "\n" + m.MemberNames()
            + "\n" + NpcLabor.LaborText.T("dis.q.progress", m.ProgressPercent)
            + "  " + NpcLabor.LaborText.T("dis.q.left", m.DaysLeft.ToString("0.0")) + " · " + where;
        // Region outing intentionally omits live haul preview.
        if (!m.isRegion)
        {
            text += "\n" + NpcLabor.LaborText.T("dis.q.harvest", m.LootSummary(8));
        }

        return text;
    }

    internal static string FormatFloorLabel(int lv)
    {
        if (lv == 0)
        {
            return NpcLabor.LaborText.T("dis.q.entrance");
        }

        if (lv < 0)
        {
            return "B" + (-lv);
        }

        return "F" + lv;
    }
}
