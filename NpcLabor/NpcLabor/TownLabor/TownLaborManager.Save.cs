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
/// PARTIAL: World-gap detection, throttled hour save, json save/load.
/// Split from TownLaborManager.cs (2026-10-02).
/// </summary>
internal static partial class TownLaborManager
{
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

    /// <summary>
    /// Calendar month index (30-day months), used by the per-shop invest quota.
    /// The board seed keeps its own week-based formula in Offers.cs — board
    /// sampling is unaffected by the quota switching to monthly buckets.
    /// </summary>
    internal static int CurrentMonthIndex()
    {
        try
        {
            int month = 1;
            int year = 0;
            try { month = EClass.world?.date?.month ?? 1; } catch { month = 1; }
            try { year = EClass.world?.date?.year ?? 0; } catch { year = 0; }
            return year * 12 + Math.Max(0, month - 1);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Per-shop free-invest quota: shopKey "zoneUid:clientUid" -> month index of
    /// the last free invest granted. One grant per shop per calendar month
    /// (2026-10-28: was weekly). Persisted so save-scumming a reload cannot reset
    /// the bucket, and old entries are pruned on save so the map cannot grow
    /// unboundedly. The json member keeps the legacy "shopInvestWeeks" name for
    /// save compatibility; values written by older weekly builds are smaller than
    /// any current month index, so they read as "stale" and are pruned naturally.
    /// </summary>
    static readonly Dictionary<string, int> ShopInvestMonth = new();

    internal static string ShopInvestKey(int uidZone, int uidClient) => uidZone + ":" + uidClient;

    internal static bool ShopInvestQuotaUsed(int uidZone, int uidClient)
        => ShopInvestMonth.TryGetValue(ShopInvestKey(uidZone, uidClient), out int month)
            && month >= CurrentMonthIndex();

    internal static void RecordShopInvest(int uidZone, int uidClient)
        => ShopInvestMonth[ShopInvestKey(uidZone, uidClient)] = CurrentMonthIndex();

    static void PruneShopInvestMonths()
    {
        int now = CurrentMonthIndex();
        var stale = new List<string>();
        foreach (var kv in ShopInvestMonth)
        {
            if (now - kv.Value > 4)
            {
                stale.Add(kv.Key);
            }
        }

        for (int i = 0; i < stale.Count; i++)
        {
            ShopInvestMonth.Remove(stale[i]);
        }
    }

    /// <summary>
    /// World advanced past last mod save => played without plugin; auto-recall on Load.
    /// Legacy lastSeen 0 resumes (no forced recall).
    /// </summary>
    static bool ShouldAutoRecallAfterModGap(int lastSeenWorldRaw)
    {
        int now = CurrentRawDate();
        if (lastSeenWorldRaw <= 0 || now <= 0)
        {
            return false;
        }

        return now > lastSeenWorldRaw;
    }




    static string SafeZoneName(Zone zone)
    {
        try
        {
            return zone.Name ?? zone.NameWithLevel ?? zone.id ?? ("#" + zone.uid);
        }
        catch
        {
            try { return zone.id ?? ("#" + zone.uid); }
            catch { return NpcLabor.LaborText.T("town.msg.townName"); }
        }
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

            return Path.Combine(root, "npclabor_townlabor.json");
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

            foreach (TownLaborMission m in Missions)
            {
                if (m.missionId <= 0)
                {
                    m.missionId = _nextMissionId++;
                }
            }

            PruneShopInvestMonths();
            var data = new TownLaborSaveData
            {
                missions = Missions.ToList(),
                lastSeenWorldRaw = CurrentRawDate(),
                shopInvestWeeks = new Dictionary<string, int>(ShopInvestMonth),
            };
            string json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(path, json);
            Plugin.LogDebug("townlabor saved " + Missions.Count + " -> " + path);
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("townlabor save failed: " + ex.Message);
        }
    }

    internal static void Load()
    {
        Missions.Clear();
        ShopInvestMonth.Clear();
        _nextMissionId = 1;
        try
        {
            string? path = SavePath();
            if (path == null || !File.Exists(path))
            {
                Plugin.LogDebug("townlabor load: no file");
                return;
            }

            string json = File.ReadAllText(path);
            TownLaborSaveData? data = JsonConvert.DeserializeObject<TownLaborSaveData>(json);
            if (data?.missions == null)
            {
                return;
            }

            try
            {
                if (data.shopInvestWeeks != null)
                {
                    foreach (var kv in data.shopInvestWeeks)
                    {
                        if (!string.IsNullOrEmpty(kv.Key))
                        {
                            ShopInvestMonth[kv.Key] = kv.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("townlabor load quota: " + ex.Message);
            }

            foreach (TownLaborMission m in data.missions)
            {
                if (m == null || m.uidWorker <= 0)
                {
                    continue;
                }

                if (m.missionId <= 0)
                {
                    m.missionId = _nextMissionId++;
                }

                if (m.missionId >= _nextMissionId)
                {
                    _nextMissionId = m.missionId + 1;
                }

                if (m.rewardLog == null)
                {
                    m.rewardLog = new List<string>();
                }

                Missions.Add(m);
            }


            Plugin.LogInfo("townlabor loaded " + Missions.Count);

            int lastSeen = 0;
            try { lastSeen = data.lastSeenWorldRaw; } catch { lastSeen = 0; }
            if (ShouldAutoRecallAfterModGap(lastSeen) && Missions.Count > 0)
            {
                int n = Missions.Count;
                Plugin.LogWarn("townlabor reinstall heal: world advanced without mod save (lastSeen="
                    + lastSeen + " now=" + CurrentRawDate() + "); auto-recalling " + n + " mission(s)");
                foreach (TownLaborMission m in Missions.ToList())
                {
                    try
                    {
                        if (m != null && Missions.Contains(m))
                        {
                            Settle(m, TownLaborSettleKind.Recall);
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.LogWarn("townlabor reinstall recall: " + ex.Message);
                    }
                }

                try { Save(); } catch { }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("townlabor load failed: " + ex.Message);
            Missions.Clear();
        }

        // Old saves lack workStarted: companions must tick; PC self defaults to already-working
        // only if missing was false for isPcSelf false (C# default true handles missing JSON).
        try
        {
            foreach (TownLaborMission m in Missions)
            {
                if (m == null)
                {
                    continue;
                }

                if (!m.isPcSelf)
                {
                    m.workStarted = true;
                }
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
        try
        {
            // Migrate old saves: lock reward pins so descriptions match payouts.
            foreach (TownLaborMission m in Missions)
            {
                if (m != null && !m.rewardPreviewLocked)
                {
                    TownLaborRewards.LockRewardPreview(m);
                }
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
        try
        {
            RefreshTrackerQuests();
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("townlabor load tracker: " + ex.Message);
        }
    }
}
