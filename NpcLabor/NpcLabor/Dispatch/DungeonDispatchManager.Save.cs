using NpcLabor.TownLabor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace NpcLabor.Dispatch;

/// <summary>
/// PARTIAL: World-gap detection, throttled hour save, json save/load.
/// Split from DungeonDispatchManager.cs (2026-10-02).
/// </summary>
internal static partial class DungeonDispatchManager
{
    static int CurrentWorldRaw()
    {
        try
        {
            return EClass.world?.date?.GetRaw() ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// True when world time moved past the last mod-side save — typically the player
    /// ran the save without this plugin. Normal same-mod load keeps lastSeen in sync.
    /// Legacy files with lastSeen 0 resume missions (no forced recall).
    /// </summary>
    static bool ShouldAutoRecallAfterModGap(int lastSeenWorldRaw)
    {
        int now = CurrentWorldRaw();
        if (lastSeenWorldRaw <= 0 || now <= 0)
        {
            return false;
        }

        return now > lastSeenWorldRaw;
    }

    static string? SavePath()
    {
        try
        {
            string root = GameIO.pathCurrentSave;
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            return Path.Combine(root, "npclabor_dispatch.json");
        }
        catch
        {
            return null;
        }
    }

    static bool _hourSavePending;

    internal static bool HasPendingHourSave => _hourSavePending;

    internal static void FlushPendingHourSave()
    {
        if (!_hourSavePending)
        {
            return;
        }

        _hourSavePending = false;
        if (Missions.Count > 0)
        {
            Save();
        }
    }

    internal static void Save()
    {
        _hourSavePending = false;
        try
        {
            string? path = SavePath();
            if (path == null)
            {
                return;
            }

            foreach (DungeonDispatchMission m in Missions)
            {
                m.EnsureMemberList();
                if (m.missionId <= 0)
                {
                    m.missionId = _nextMissionId++;
                }

                if (m.uidMembers.Count > 0)
                {
                    m.uidChara = m.uidMembers[0];
                }
            }

            var data = new DungeonDispatchSaveData
            {
                missions = Missions.ToList(),
                lastPresets = LastPresets.ToList(),
                lastSeenWorldRaw = CurrentWorldRaw(),
            };
            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(path, json);
            Plugin.LogDebug("dispatch saved " + Missions.Count + " -> " + path);
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("dispatch save failed: " + ex.Message);
        }
    }

    internal static void Load()
    {
        Missions.Clear();
        LastPresets.Clear();
        _nextMissionId = 1;
        try
        {
            string? path = SavePath();
            if (path == null || !File.Exists(path))
            {
                Plugin.LogDebug("dispatch load: no file");
                return;
            }

            string json = File.ReadAllText(path);
            DungeonDispatchSaveData? data = JsonConvert.DeserializeObject<DungeonDispatchSaveData>(json);
            if (data?.missions == null && (data?.lastPresets == null || data.lastPresets.Count == 0))
            {
                return;
            }

            if (data?.lastPresets != null)
            {
                for (int i = 0; i < data.lastPresets.Count; i++)
                {
                    DungeonDispatchLastPreset p = data.lastPresets[i];
                    if (p == null || string.IsNullOrEmpty(p.key) || p.uidMembers == null || p.uidMembers.Count == 0)
                    {
                        continue;
                    }

                    if (p.uidMembers == null)
                    {
                        p.uidMembers = new List<int>();
                    }

                    LastPresets.Add(p);
                }
            }

            if (data?.missions == null)
            {
                return;
            }

            foreach (DungeonDispatchMission m in data.missions)
            {
                if (m == null)
                {
                    continue;
                }

                m.EnsureMemberList();
                if (m.MemberCount <= 0)
                {
                    continue;
                }

                if (m.missionId <= 0)
                {
                    m.missionId = _nextMissionId++;
                }
                else if (m.missionId >= _nextMissionId)
                {
                    _nextMissionId = m.missionId + 1;
                }

                Missions.Add(m);
            }

            foreach (DungeonDispatchMission m in Missions.ToList())
            {
                m.EnsureMemberList();
                var alive = new List<int>();
                foreach (int uid in m.uidMembers)
                {
                    Chara? c = null;
                    try
                    {
                        c = RefChara.Get(uid);
                    }
                    catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
if (c == null || c.isDead)
                    {
                        continue;
                    }

                    alive.Add(uid);
                }

                m.uidMembers = alive;
                if (m.uidMembers.Count == 0)
                {
                    Missions.Remove(m);
                    continue;
                }

                m.uidChara = m.uidMembers[0];

                if (m.isRegion)
                {
                    Zone? field = m.GetZone();
                    if (field == null || field.destryoed)
                    {
                        field = DungeonDispatchTargets.EnsureRegionFieldZone(m.regionGx, m.regionGy);
                        if (field != null)
                        {
                            m.uidZone = field.uid;
                        }
                    }

                    foreach (Chara c in m.GetMembers())
                    {
                        try
                        {
                            PrepareDispatchedChara(c);
                            if (field != null
                                && (c.currentZone == null
                                    || (c.homeZone != null && c.currentZone.uid == c.homeZone.uid)
                                    || c.currentZone.uid != field.uid))
                            {
                                try
                                {
                                    c.MoveZone(field, ZoneTransition.EnterState.RandomVisit);
                                }
                                catch
                                {
                                    try { c.MoveZone(field); } catch { }
                                }
                            }

                            PrepareDispatchedChara(c);
                        }
                        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
}

                    m.currentFloorLv = 0;
                    continue;
                }

                Zone? z = m.GetZone();
                if (z == null || z.destryoed)
                {
                    foreach (Chara c in m.GetMembers())
                    {
                        try
                        {
                            ClearDispatchedFlags(c);
                            Zone? home = m.GetHomeZone();
                            if (home != null)
                            {
                                c.MoveZone(home, ZoneTransition.EnterState.Return);
                            }
                        }
                        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
}

                    Missions.Remove(m);
                    continue;
                }

                Zone? floor = DungeonDispatchTargets.ResolveFloorForProgress(z, m.ProgressPercent) ?? z;
                m.currentFloorLv = SafeLv(floor);
                foreach (Chara c in m.GetMembers())
                {
                    try
                    {
                        if (c.currentZone == null
                            || (floor != null && c.currentZone.uid != floor.uid
                                && (c.homeZone == null || c.currentZone.uid == c.homeZone.uid)))
                        {
                            if (floor != null)
                            {
                                c.MoveZone(floor, ZoneTransition.EnterState.RandomVisit);
                            }
                        }

                        PrepareDispatchedChara(c);
                    }
                    catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
}
            }

            Plugin.LogInfo("dispatch loaded missions=" + Missions.Count);

            int lastSeen = 0;
            try { lastSeen = data != null ? data.lastSeenWorldRaw : 0; } catch { lastSeen = 0; }
            if (ShouldAutoRecallAfterModGap(lastSeen) && Missions.Count > 0)
            {
                int n = Missions.Count;
                Plugin.LogWarn("dispatch reinstall heal: world advanced without mod save (lastSeen="
                    + lastSeen + " now=" + CurrentWorldRaw() + "); auto-recalling " + n + " mission(s)");
                foreach (DungeonDispatchMission m in Missions.ToList())
                {
                    try
                    {
                        if (m != null && Missions.Contains(m))
                        {
                            Settle(m, DispatchSettleKind.Recall, destroyRandom: false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.LogWarn("dispatch reinstall recall: " + ex.Message);
                    }
                }

                try { Save(); } catch { }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("dispatch load failed: " + ex.Message);
        }

        try
        {
            // One typed pin per mission via quests.list only. Widget paints itself.
            RefreshTrackerQuests();
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("dispatch load tracker: " + ex.Message);
        }

        try
        {
            SanitizeRegionMapLights();
            // Scrub orphan pins an older build baked into cell.obj before re-pinning.
            RepairStuckRegionPins();
            RefreshRegionMapMarkers();
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
}
}
