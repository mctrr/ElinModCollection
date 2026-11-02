using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace NpcLabor.PermanentDispatch;

/// <summary>
/// PARTIAL: json save/load + world-gap auto recall. Mirrors the slice D
/// pattern: if the world advanced past lastSeenWorldRaw without a mod save,
/// the save was played without NPC Labor — settle every posting out (whole
/// months already credited still pay) so a reinstall never strands workers.
/// </summary>
internal static partial class PermanentDispatchManager
{
    static bool _hourSavePending;

    internal sealed class PermanentDispatchSaveData
    {
        [JsonProperty]
        public List<PermanentDispatchMission> missions = new();

        [JsonProperty]
        public int nextMissionId = 1;

        [JsonProperty]
        public int lastSeenWorldRaw;
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

            return Path.Combine(root, "npclabor_pdispatch.json");
        }
        catch
        {
            return null;
        }
    }

    internal static void FlushPendingHourSave()
    {
        if (!_hourSavePending)
        {
            return;
        }

        _hourSavePending = false;
        Save();
    }

    internal static void Save()
    {
        try
        {
            string? path = SavePath();
            if (path == null)
            {
                return;
            }

            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            foreach (PermanentDispatchMission m in Missions)
            {
                if (m.missionId <= 0)
                {
                    m.missionId = _nextMissionId++;
                }
            }

            var data = new PermanentDispatchSaveData
            {
                missions = Missions.ToList(),
                nextMissionId = _nextMissionId,
                lastSeenWorldRaw = CurrentRawDate(),
            };
            File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
            Plugin.LogDebug("pdispatch saved " + Missions.Count + " -> " + path);
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("pdispatch save failed: " + ex.Message);
        }
    }

    static int CurrentRawDate()
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

    internal static void Load()
    {
        Missions.Clear();
        _nextMissionId = 1;
        try
        {
            string? path = SavePath();
            if (path == null || !File.Exists(path))
            {
                return;
            }

            PermanentDispatchSaveData? data =
                JsonConvert.DeserializeObject<PermanentDispatchSaveData>(File.ReadAllText(path));
            if (data == null)
            {
                return;
            }

            _nextMissionId = Math.Max(1, data.nextMissionId);
            if (data.missions != null)
            {
                foreach (PermanentDispatchMission m in data.missions)
                {
                    if (m != null && !string.IsNullOrEmpty(m.defId))
                    {
                        Missions.Add(m);
                    }
                }
            }

            // Played-without-mod heal: the world ran ahead of our last save.
            try
            {
                int lastSeen = data.lastSeenWorldRaw;
                if (lastSeen > 0 && CurrentRawDate() > lastSeen)
                {
                    Plugin.LogInfo("pdispatch world gap detected -> settling " + Missions.Count);
                    var snapshot = Missions.ToList();
                    foreach (PermanentDispatchMission m in snapshot)
                    {
                        try { SettleOut(m, recalled: true); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("pdispatch world gap check: " + ex.Message);
            }

            Plugin.LogDebug("pdispatch loaded " + Missions.Count);
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("pdispatch load failed: " + ex.Message);
        }
    }
}
