using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using NpcLabor.CoCraft;
using NpcLabor.Dispatch;
using NpcLabor.Process;
using UnityEngine;

namespace NpcLabor.TownLabor;

/// <summary>
/// PARTIAL: Quest tracker lifecycle (start/remove/refresh/sanitize) and fail path.
/// Split from TownLaborManager.cs (2026-10-02).
/// </summary>
internal static partial class TownLaborManager
{
    /// <summary>Vanilla Quest.Fail for hard fail (sound + fame + karma). Safe if already removed.</summary>
    static void FailTrackerQuest(TownLaborMission? mission)
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

            foreach (Quest q in qm.list.ToList())
            {
                if (q is QuestNpcLaborTownLabor tq && tq.missionId == mission.missionId)
                {
                    try { tq.track = false; } catch { }
                    try
                    {
                        tq.Fail();
                    }
                    catch (Exception ex)
                    {
                        Plugin.LogDebug("townlabor tracker Fail(): " + ex.Message);
                        try { qm.Remove(q); } catch { }
                    }
                }
            }

                        RefreshQuestTrackerWidget();
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("townlabor tracker fail remove: " + ex.Message);
            try { RemoveTrackerQuest(mission); } catch { }
        }
    }

    

    internal static bool IsOurTownTrackerQuestPublic(Quest? q) => IsOurTownTrackerQuest(q);

    internal static bool IsOurTownTrackerQuest(Quest? q)
    {
        if (q == null)
        {
            return false;
        }

        if (q is QuestNpcLaborTownLabor)
        {
            return true;
        }

        try
        {
            string id = q.id ?? string.Empty;
            if (id.StartsWith("npclabor_townlabor_", StringComparison.Ordinal))
            {
                return true;
            }
        }
        catch { }

        try
        {
            string tn = q.GetType().Name ?? string.Empty;
            if (tn.IndexOf("NpcLaborTownLabor", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// F9/auto-save: keep live typed town pins. Drop Dummy / orphan / dups only.
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

            var live = new HashSet<int>();
            foreach (TownLaborMission m in Missions)
            {
                if (m != null && m.missionId > 0)
                {
                    live.Add(m.missionId);
                }
            }

            var seen = new HashSet<int>();
            foreach (Quest q in qm.list.ToList())
            {
                if (!IsOurTownTrackerQuest(q))
                {
                    continue;
                }

                int mid = 0;
                bool typed = false;
                try
                {
                    if (q is QuestNpcLaborTownLabor tq)
                    {
                        typed = true;
                        mid = tq.missionId;
                    }
                    else
                    {
                        string id = q.id ?? string.Empty;
                        const string prefix = "npclabor_townlabor_";
                        if (id.StartsWith(prefix, StringComparison.Ordinal))
                        {
                            int.TryParse(id.Substring(prefix.Length), out mid);
                        }
                    }
                }
                catch { mid = 0; }

                bool orphan = mid <= 0 || !live.Contains(mid);
                bool dup = mid > 0 && !seen.Add(mid);
                bool drop = !typed || orphan || dup;
                if (!drop)
                {
                    try
                    {
                        if (q is QuestNpcLaborTownLabor liveQ)
                        {
                            liveQ.track = true;
                            liveQ.deadline = 0;
                            liveQ.EnsureSafePerson();
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
            Plugin.LogDebug("townlabor sanitize broken trackers: " + ex.Message);
        }
    }

    static void TryStartTrackerQuest(TownLaborMission mission)
    {
        if (mission == null || mission.missionId <= 0)
        {
            return;
        }

        try
        {
            // Guard: never Start if a live pin already exists.
            // Do not Remove+Start (widget race / double pin).
            try
            {
                QuestManager? qm = EClass.game?.quests;
                if (qm?.list != null)
                {
                    foreach (Quest existing in qm.list)
                    {
                        if (existing is QuestNpcLaborTownLabor tq && tq.missionId == mission.missionId)
                        {
                            try
                            {
                                tq.track = true;
                                tq.deadline = 0;
                                tq.isNew = false;
                                tq.EnsureSafePerson();
                            }
                            catch { }
                            // Same Quest object already in list — widget row already bound by ref.
                            return;
                        }
                    }

                    string wantId = "npclabor_townlabor_" + mission.missionId;
                    foreach (Quest orphan in qm.list.ToList())
                    {
                        if (orphan is QuestNpcLaborTownLabor)
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

            var q = new QuestNpcLaborTownLabor
            {
                missionId = mission.missionId,
                id = "npclabor_townlabor_" + mission.missionId,
            };

            try { q.Init(); } catch { }
            q.deadline = 0;
            q.track = true;
            q.isNew = false;
            try { q.SetClient(null, assignQuest: false); } catch { }
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
            DungeonDispatchManager.DedupeNpcLaborTrackerQuests();
            // Vanilla Start already Show()s the pin; do not thrash WidgetQuestTracker.
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("townlabor tracker start: " + ex.Message);
        }
    }

    static void RemoveTrackerQuest(TownLaborMission? mission)
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

            string wantId = "npclabor_townlabor_" + mission.missionId;
            foreach (Quest q in qm.list.ToList())
            {
                bool match = false;
                try
                {
                    if (q is QuestNpcLaborTownLabor tq && tq.missionId == mission.missionId)
                    {
                        match = true;
                    }
                    else if (q != null && string.Equals(q.id, wantId, StringComparison.Ordinal))
                    {
                        match = true;
                    }
                }
                catch { }

                if (!match || q == null)
                {
                    continue;
                }

                try { q.track = false; } catch { }
                try { qm.Remove(q); } catch { }
            }

            // Vanilla kills widget rows whose quest left the list.
            RefreshQuestTrackerWidget();
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("townlabor tracker remove: " + ex.Message);
        }
    }

    static void RefreshTrackerQuests()
    {
        // Ensure every mission has exactly one tracker; drop orphans/dups/legacy.
        try
        {
            QuestManager? qm = EClass.game?.quests;
            var live = new HashSet<int>();
            foreach (TownLaborMission m in Missions)
            {
                live.Add(m.missionId);
            }

            if (qm?.list != null)
            {
                var seen = new HashSet<int>();
                foreach (Quest q in qm.list.ToList())
                {
                    if (!IsOurTownTrackerQuest(q))
                    {
                        continue;
                    }

                    int mid = 0;
                    try
                    {
                        if (q is QuestNpcLaborTownLabor tq)
                        {
                            mid = tq.missionId;
                        }
                        else
                        {
                            string id = q.id ?? string.Empty;
                            const string prefix = "npclabor_townlabor_";
                            if (id.StartsWith(prefix, StringComparison.Ordinal))
                            {
                                int.TryParse(id.Substring(prefix.Length), out mid);
                            }
                        }
                    }
                    catch { mid = 0; }

                    bool typed = q is QuestNpcLaborTownLabor;
                    bool orphan = mid <= 0 || !live.Contains(mid);
                    bool dup = mid > 0 && !seen.Add(mid);
                    if (typed && !orphan && !dup)
                    {
                        continue;
                    }

                    try { q.track = false; } catch { }
                    try { qm.Remove(q); } catch { }
                }
            }

            foreach (TownLaborMission m in Missions)
            {
                bool has = false;
                if (qm?.list != null)
                {
                    foreach (Quest q in qm.list)
                    {
                        if (q is QuestNpcLaborTownLabor tq && tq.missionId == m.missionId)
                        {
                            has = true;
                            try
                            {
                                tq.track = true;
                                tq.deadline = 0;
                                tq.EnsureSafePerson();
                            }
                            catch { }
                            break;
                        }
                    }
                }

                if (!has)
                {
                    TryStartTrackerQuest(m);
                }
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
    }

    static void RefreshQuestTrackerWidget()
    {
        DungeonDispatchManager.RequestQuestTrackerRefresh();
    }
}
