using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace NpcLabor.Dispatch;

internal sealed class SpecialRewardEntry
{
    [JsonProperty("zoneId")]
    public string ZoneId = "";

    [JsonProperty("zoneAlias")]
    public string ZoneAlias = "";

    /// <summary>
    /// Region kind for field missions: beach / forest / mountain / plain.
    /// When set, entry is matched by regionKind instead of dungeon zone id.
    /// </summary>
    [JsonProperty("regionKind")]
    public string RegionKind = "";

    [JsonProperty("items")]
    public List<string> Items = new List<string>();
}

internal sealed class SpecialRewardFile
{
    [JsonProperty("rewards")]
    public List<SpecialRewardEntry> Rewards = new List<SpecialRewardEntry>();
}

internal static class SpecialRewardConfig
{
    static SpecialRewardFile? _cache;
    static bool _loaded;

    internal static void Reload()
    {
        _loaded = false;
        _cache = null;
        EnsureLoaded();
    }

    internal static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _cache = new SpecialRewardFile();

        try
        {
            string? path = FindConfigPath();
            if (path == null || !File.Exists(path))
            {
                Plugin.LogDebug("dispatch special_rewards.json not found (optional).");
                return;
            }

            string json = File.ReadAllText(path);
            SpecialRewardFile? file = JsonConvert.DeserializeObject<SpecialRewardFile>(json);
            if (file?.Rewards != null)
            {
                _cache = file;
                Plugin.LogInfo($"dispatch special rewards loaded: {_cache.Rewards.Count} entries from {path}");
            }
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("dispatch special_rewards load failed: " + ex.Message);
        }
    }

    static string? FindConfigPath()
    {
        try
        {
            string? asm = typeof(SpecialRewardConfig).Assembly.Location;
            if (!string.IsNullOrEmpty(asm))
            {
                string dir = Path.GetDirectoryName(asm) ?? "";
                string p = Path.Combine(dir, "special_rewards.json");
                if (File.Exists(p))
                {
                    return p;
                }
            }
        }
        catch
        {
        }


         // No hardcoded fallback path — rely on Assembly.Location relative lookup only.
         return null;
    }

    internal static IEnumerable<string> GetSpecialItemIds(Zone zone)
    {
        EnsureLoaded();
        if (_cache?.Rewards == null || zone == null)
        {
            yield break;
        }

        string id = zone.id ?? "";
        string name = "";
        try
        {
            name = zone.Name ?? "";
        }
        catch
        {
        }

        foreach (SpecialRewardEntry entry in _cache.Rewards)
        {
            if (entry == null || entry.Items == null || entry.Items.Count == 0)
            {
                continue;
            }

            bool match = false;
            if (!string.IsNullOrEmpty(entry.ZoneId) &&
                string.Equals(entry.ZoneId, id, StringComparison.OrdinalIgnoreCase))
            {
                match = true;
            }
            else if (!string.IsNullOrEmpty(entry.ZoneAlias) &&
                     (string.Equals(entry.ZoneAlias, id, StringComparison.OrdinalIgnoreCase)
                      || (!string.IsNullOrEmpty(name) && name.IndexOf(entry.ZoneAlias, StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                match = true;
            }

            if (!match)
            {
                continue;
            }

            foreach (string item in entry.Items)
            {
                if (!string.IsNullOrEmpty(item))
                {
                    yield return item;
                }
            }
        }
    }

    /// <summary>
    /// Region specials from the same JSON as fixed-dungeon specials.
    /// Match entry.regionKind to mission.regionKind (beach/forest/mountain/plain).
    /// </summary>
    internal static IEnumerable<string> GetSpecialItemIdsForRegion(string regionKind)
    {
        EnsureLoaded();
        if (_cache?.Rewards == null || string.IsNullOrEmpty(regionKind))
        {
            yield break;
        }

        string kind = regionKind.Trim().ToLowerInvariant();
        foreach (SpecialRewardEntry entry in _cache.Rewards)
        {
            if (entry == null || entry.Items == null || entry.Items.Count == 0)
            {
                continue;
            }

            string rk = (entry.RegionKind ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(rk))
            {
                continue;
            }

            if (!string.Equals(rk, kind, StringComparison.OrdinalIgnoreCase)
                && !(kind == "field" && rk == "plain")
                && !(kind == "plain" && rk == "field"))
            {
                continue;
            }

            foreach (string item in entry.Items)
            {
                if (!string.IsNullOrEmpty(item))
                {
                    yield return item;
                }
            }
        }
    }

}
