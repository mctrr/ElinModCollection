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
/// PARTIAL: Console debug completion and full runtime clear.
/// Split from TownLaborManager.cs (2026-10-02).
/// </summary>
internal static partial class TownLaborManager
{
    internal static void ClearAllRuntime()
    {
        Missions.Clear();
        _nextMissionId = 1;
    }

    /// <summary>Debug: finish all active town labor as success.</summary>
    internal static int DebugCompleteAll()
    {
        int n = 0;
        foreach (TownLaborMission m in Missions.ToList())
        {
            try
            {
                Settle(m, TownLaborSettleKind.Success);
                n++;
            }
            catch (Exception ex)
            {
                Plugin.LogWarn("townlabor debug complete: " + ex.Message);
            }
        }

        return n;
    }

    /// <summary>Debug: finish one town labor by missionId or worker uid.</summary>
    internal static bool DebugCompleteOne(int missionIdOrWorkerUid)
    {
        TownLaborMission? m = FindByMissionId(missionIdOrWorkerUid) ?? FindByWorker(missionIdOrWorkerUid);
        if (m == null)
        {
            return false;
        }

        try
        {
            Settle(m, TownLaborSettleKind.Success);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("townlabor debug complete one: " + ex.Message);
            return false;
        }
    }
}
