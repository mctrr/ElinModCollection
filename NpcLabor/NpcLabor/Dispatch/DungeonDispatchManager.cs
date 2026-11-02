using NpcLabor.TownLabor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace NpcLabor.Dispatch;

internal sealed class DungeonDispatchLastPreset
{
    [JsonProperty]
    public string key = "";

    [JsonProperty]
    public bool isRegion;

    [JsonProperty]
    public string regionKind = "";

    [JsonProperty]
    public int uidZone;

    [JsonProperty]
    public string zoneId = "";

    [JsonProperty]
    public int exploreWeeks = 1;

    [JsonProperty]
    public List<int> uidMembers = new List<int>();
}

internal sealed class DungeonDispatchSaveData
{
    [JsonProperty]
    public List<DungeonDispatchMission> missions = new List<DungeonDispatchMission>();

    /// <summary>Last started team per target key (region kind or zone id).</summary>
    [JsonProperty]
    public List<DungeonDispatchLastPreset> lastPresets = new List<DungeonDispatchLastPreset>();

    /// <summary>
    /// World date.GetRaw() written on every mod save.
    /// If the world advances past this without a mod save, Load treats it as
    /// "played without NPC Labor" and auto-recalls open missions (reinstall heal).
    /// </summary>
    [JsonProperty]
    public int lastSeenWorldRaw;
}

/// <summary>
/// Runtime + save manager for slice D dungeon dispatch.
/// One mission per dungeon, up to 4 members. Save: npclabor_dispatch.json
/// </summary>
internal static partial class DungeonDispatchManager
{
    static readonly List<DungeonDispatchMission> Missions = new List<DungeonDispatchMission>();
    static readonly List<DungeonDispatchLastPreset> LastPresets = new List<DungeonDispatchLastPreset>();
    static int _nextMissionId = 1;

    internal static IReadOnlyList<DungeonDispatchMission> All => Missions;

    internal static int Count => Missions.Count;

    internal static int BusyMemberCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < Missions.Count; i++)
            {
                Missions[i].EnsureMemberList();
                n += Missions[i].MemberCount;
            }

            return n;
        }
    }

    internal static bool IsBusy(int uidChara)
    {
        for (int i = 0; i < Missions.Count; i++)
        {
            if (Missions[i].HasMember(uidChara))
            {
                return true;
            }
        }

        return false;
    }

    internal static DungeonDispatchMission? FindByChara(int uidChara)
    {
        for (int i = 0; i < Missions.Count; i++)
        {
            if (Missions[i].HasMember(uidChara))
            {
                return Missions[i];
            }
        }

        return null;
    }

    internal static DungeonDispatchMission? FindByZone(int uidZone)
    {
        for (int i = 0; i < Missions.Count; i++)
        {
            DungeonDispatchMission m = Missions[i];
            if (m.uidZone == uidZone)
            {
                return m;
            }

            // Also match child floors under the same top dungeon / region field.
            try
            {
                Zone? mz = m.GetZone();
                Zone? z = EClass.game?.spatials?.Find(uidZone);
                if (mz != null && z != null)
                {
                    Zone topM = mz.GetTopZone() ?? mz;
                    Zone topZ = z.GetTopZone() ?? z;
                    if (topM.uid == topZ.uid)
                    {
                        return m;
                    }
                }

                // Region field: match by overworld tile coords.
                if (m.isRegion && z != null)
                {
                    try
                    {
                        if (z.x == m.regionGx && z.y == m.regionGy)
                        {
                            return m;
                        }
                    }
                    catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
}
            }
            catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
}

        return null;
    }

    internal static DungeonDispatchMission? FindByMissionId(int id)
    {
        for (int i = 0; i < Missions.Count; i++)
        {
            if (Missions[i].missionId == id)
            {
                return Missions[i];
            }
        }

        return null;
    }

    internal static bool IsZoneBusy(int uidZone)
    {
        return FindByZone(uidZone) != null;
    }

    internal static DungeonDispatchMission? FindByRegion(string regionKind)
    {
        string want = DungeonDispatchTargets.NormalizeRegionKind(regionKind);
        if (string.IsNullOrEmpty(want))
        {
            return null;
        }

        for (int i = 0; i < Missions.Count; i++)
        {
            DungeonDispatchMission m = Missions[i];
            if (m != null && m.isRegion
                && DungeonDispatchTargets.NormalizeRegionKind(m.regionKind) == want)
            {
                return m;
            }
        }

        return null;
    }

    internal static bool IsRegionBusy(string regionKind)
    {
        return FindByRegion(regionKind) != null;
    }

    internal static bool IsTargetBusy(DungeonDispatchTarget target)
    {
        if (target == null)
        {
            return false;
        }

        if (target.IsRegion)
        {
            return IsRegionBusy(target.RegionKind);
        }

        try
        {
            return target.Zone != null && IsZoneBusy(target.Zone.uid);
        }
        catch
        {
            return false;
        }
    }
}
