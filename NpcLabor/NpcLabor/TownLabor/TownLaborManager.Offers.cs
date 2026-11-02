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
/// PARTIAL: Board generation: zone eligibility, offer listing, sampling and ordering.
/// Split from TownLaborManager.cs (2026-10-02).
/// </summary>
internal static partial class TownLaborManager
{
    /// <summary>Whether the current zone can host town labor rows.</summary>
    internal static bool CanHostTownLabor(Zone? zone)
    {
        if (zone == null)
        {
            return false;
        }

        try
        {
            // User lock: base/PC-faction has no town labor rows.
            if (zone.IsPCFaction)
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        try
        {
            // Prefer towns; still allow non-base settlements that host merchants.
            if (zone.IsTown)
            {
                return true;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
// Fallback: any non-PCFaction zone that currently has a matching merchant.
        return true;
    }

    internal static List<TownLaborOffer> ListOffers(Zone? zone = null)
    {
        var offers = new List<TownLaborOffer>();
        zone ??= EClass._zone;
        if (!CanHostTownLabor(zone))
        {
            return offers;
        }

        if (EClass._map?.charas == null)
        {
            return offers;
        }

        var seen = new HashSet<int>();
        foreach (Chara c in EClass._map.charas)
        {
            if (c == null || c.isDead || PersonPickerUi.IsPcLike(c))
            {
                continue;
            }

            if (!seen.Add(c.uid))
            {
                continue;
            }

            // Skip own faction residents/party as clients.
            try
            {
                if (c.IsPCFaction || c.IsPCParty)
                {
                    continue;
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("TownLaborManager.cs silent catch: " + __e.Message); }
TownLaborJobDef? def = TownLaborJobs.MatchJob(c);
            if (def == null)
            {
                continue;
            }

            bool active = IsClientBusy(c.uid);
            offers.Add(new TownLaborOffer
            {
                Client = c,
                Def = def,
                Hours = TownLaborJobs.RollWorkHours(),
                IsActive = active,
            });
        }

        // Always keep already-active labor rows visible.
        var activeOffers = new List<TownLaborOffer>();
        var free = new List<TownLaborOffer>();
        for (int i = 0; i < offers.Count; i++)
        {
            if (offers[i].IsActive)
            {
                activeOffers.Add(offers[i]);
            }
            else
            {
                free.Add(offers[i]);
            }
        }

        // Board only needs a few free posts, not every shopkeeper.
        int take = SampleBoardCount(zone, free.Count);
        if (free.Count > take)
        {
            free = SampleOffers(free, take, zone);
        }

        free.Sort(CompareOffers);
        activeOffers.Sort(CompareOffers);

        var result = new List<TownLaborOffer>(activeOffers.Count + free.Count);
        result.AddRange(activeOffers);
        result.AddRange(free);
        return result;
    }

    static int CompareOffers(TownLaborOffer a, TownLaborOffer b)
    {
        int ka = (int)a.Def.Kind;
        int kb = (int)b.Def.Kind;
        int cmp = ka.CompareTo(kb);
        if (cmp != 0)
        {
            return cmp;
        }

        return a.Client.uid.CompareTo(b.Client.uid);
    }

    static int SampleBoardCount(Zone? zone, int freeCount)
    {
        if (freeCount <= 0)
        {
            return 0;
        }

        if (freeCount <= TownLaborJobs.MinBoardOffers)
        {
            return freeCount;
        }

        // Stable 2..3 per zone+week so reopening board does not thrash.
        int seed = BoardSeed(zone);
        int span = TownLaborJobs.MaxBoardOffers - TownLaborJobs.MinBoardOffers + 1;
        int n = TownLaborJobs.MinBoardOffers + (seed % span);
        if (n > freeCount)
        {
            n = freeCount;
        }

        if (n > TownLaborJobs.MaxBoardOffers)
        {
            n = TownLaborJobs.MaxBoardOffers;
        }

        return n;
    }

    static int BoardSeed(Zone? zone)
    {
        int z = 0;
        try { z = zone?.uid ?? 0; } catch { z = 0; }

        // One board sample per calendar week (30-day months -> week buckets of 7).
        int week = 0;
        try
        {
            int day = EClass.world?.date?.day ?? 1;
            int month = 1;
            int year = 0;
            try { month = EClass.world?.date?.month ?? 1; } catch { month = 1; }
            try { year = EClass.world?.date?.year ?? 0; } catch { year = 0; }
            // Absolute day index, then /7 so offers refresh weekly rather than daily.
            int dayIndex = year * 360 + Math.Max(0, month - 1) * 30 + Math.Max(1, day);
            week = dayIndex / 7;
        }
        catch
        {
            week = 0;
        }

        unchecked
        {
            return z * 397 ^ week * 31 ^ 0x51a7;
        }
    }

    static List<TownLaborOffer> SampleOffers(List<TownLaborOffer> source, int take, Zone? zone)
    {
        if (source == null || source.Count == 0 || take <= 0)
        {
            return new List<TownLaborOffer>();
        }

        if (take >= source.Count)
        {
            return new List<TownLaborOffer>(source);
        }

        var copy = new List<TownLaborOffer>(source);
        int state = BoardSeed(zone);
        for (int i = copy.Count - 1; i > 0; i--)
        {
            state = unchecked(state * 1103515245 + 12345);
            int j = (state & 0x7fffffff) % (i + 1);
            TownLaborOffer tmp = copy[i];
            copy[i] = copy[j];
            copy[j] = tmp;
        }

        return copy.GetRange(0, take);
    }
}
