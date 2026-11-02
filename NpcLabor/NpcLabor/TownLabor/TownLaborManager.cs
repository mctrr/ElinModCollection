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
/// Runtime + save manager for slice E town shop labor.
/// Save: npclabor_townlabor.json. Max 2 concurrent per town; 1 worker per client.
/// </summary>
internal static partial class TownLaborManager
{
    static readonly List<TownLaborMission> Missions = new List<TownLaborMission>();
    static int _nextMissionId = 1;
    static int _poseTick;


    internal static IReadOnlyList<TownLaborMission> All => Missions;
    internal static int Count => Missions.Count;

    internal static bool IsBusy(int uidChara)
    {
        for (int i = 0; i < Missions.Count; i++)
        {
            if (Missions[i].uidWorker == uidChara)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsClientBusy(int uidClient)
    {
        for (int i = 0; i < Missions.Count; i++)
        {
            if (Missions[i].uidClient == uidClient)
            {
                return true;
            }
        }

        return false;
    }

    internal static int CountInZone(int uidZone)
    {
        int n = 0;
        for (int i = 0; i < Missions.Count; i++)
        {
            if (Missions[i].uidZone == uidZone)
            {
                n++;
            }
        }

        return n;
    }

    internal static TownLaborMission? FindByMissionId(int id)
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

    internal static TownLaborMission? FindByWorker(int uid)
    {
        for (int i = 0; i < Missions.Count; i++)
        {
            if (Missions[i].uidWorker == uid)
            {
                return Missions[i];
            }
        }

        return null;
    }

    internal static TownLaborMission? FindByClient(int uid)
    {
        for (int i = 0; i < Missions.Count; i++)
        {
            if (Missions[i].uidClient == uid)
            {
                return Missions[i];
            }
        }

        return null;
    }
    /// <summary>
    /// Resolve a chara by uid for town labor.
    /// Local merchants are often map-only; RefChara.Get only hits globalCharas.
    /// Order: person temp cache → current map → global → branch members.
    /// </summary>
    internal static Chara? ResolveChara(int uid, Person? person = null)
    {
        if (uid <= 0)
        {
            return null;
        }

        // PC self-work missions key uidWorker to EClass.pc.
        try
        {
            if (EClass.pc != null && EClass.pc.uid == uid && !EClass.pc.isDead)
            {
                return EClass.pc;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
        try
        {
            if (person != null)
            {
                Chara? fromPerson = person.chara;
                if (fromPerson != null && fromPerson.uid == uid && !fromPerson.isDead)
                {
                    return fromPerson;
                }
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
        try
        {
            if (EClass._map?.charas != null)
            {
                foreach (Chara c in EClass._map.charas)
                {
                    if (c != null && c.uid == uid && !c.isDead)
                    {
                        return c;
                    }
                }
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
        try
        {
            Chara? global = RefChara.Get(uid);
            if (global != null && !global.isDead)
            {
                return global;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
        try
        {
            FactionBranch? branch = EClass.BranchOrHomeBranch ?? EClass.Branch;
            if (branch?.members != null)
            {
                foreach (Chara m in branch.members)
                {
                    if (m != null && m.uid == uid && !m.isDead)
                    {
                        return m;
                    }
                }
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
        try
        {
            if (EClass.pc?.party?.members != null)
            {
                foreach (Chara m in EClass.pc.party.members)
                {
                    if (m != null && m.uid == uid && !m.isDead)
                    {
                        return m;
                    }
                }
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
        return null;
    }
}

/// <summary>Board row model for an available (or active) town labor offer.</summary>
internal sealed class TownLaborOffer
{
    internal Chara Client = null!;
    internal TownLaborJobDef Def = null!;
    internal int Hours = 18;
    internal bool IsActive;
}
