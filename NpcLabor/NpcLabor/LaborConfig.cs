using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace NpcLabor;

/// <summary>
/// Tunable balance numbers loaded from package/labor_config.json.
/// Missing/invalid fields fall back to coded defaults (current balance pass).
/// </summary>
internal sealed class LaborConfigFile
{
    [JsonProperty("features")]
    public FeatureConfigSection Features = new FeatureConfigSection();

    [JsonProperty("townLabor")]
    public TownLaborConfigSection TownLabor = new TownLaborConfigSection();

    [JsonProperty("dispatch")]
    public DispatchConfigSection Dispatch = new DispatchConfigSection();

    [JsonProperty("process")]
    public ProcessConfigSection Process = new ProcessConfigSection();

    [JsonProperty("unlock")]
    public UnlockConfigSection Unlock = new UnlockConfigSection();
}

/// <summary>
/// Per-slice on/off switches. These gate the entry points: with a slice off the
/// board row disappears and no new job can be started, while anything already
/// running keeps settling so disabling a slice never strands cargo or wages.
/// The master <see cref="Enabled"/> switch is the hard off - it also stops the
/// hourly tick, which is what you want when the whole mod is being parked.
/// </summary>
internal sealed class FeatureConfigSection
{
    [JsonProperty("enabled")]
    public bool Enabled = true;

    [JsonProperty("dispatch")]
    public bool Dispatch = true;

    [JsonProperty("townLabor")]
    public bool TownLabor = true;

    [JsonProperty("craft")]
    public bool Craft = true;
}

internal sealed class TownLaborConfigSection
{
    [JsonProperty("minWagePerHour")]
    public int MinWagePerHour = 50;

    [JsonProperty("expPerHour")]
    public int ExpPerHour = 12;

    [JsonProperty("furnitureTicketChancePercent")]
    public int FurnitureTicketChancePercent = 20;

    /// <summary>
    /// Shift length window. 2026-10-30: 6-12h felt too long in play — a town shop
    /// errand is now 2-6h, which also keeps the board's 剩余 column meaningful.
    /// </summary>
    [JsonProperty("hoursMin")]
    public int HoursMin = 2;

    [JsonProperty("hoursMax")]
    public int HoursMax = 6;

    /// <summary>
    /// Retired (2026-10-28): the invest cap now compares 城镇投资 against the shop's
    /// own level directly (see <see cref="TownLaborJobs.CanGrantShopInvest"/>), so
    /// there is no headroom to tune. Kept in the json so existing config files still
    /// deserialize, but nothing reads it — the config page no longer shows it.
    /// </summary>
    [JsonProperty("investTownHeadroom")]
    public int InvestTownHeadroom = 5;

    [JsonProperty("platBase")]
    public int PlatBase = 2;

    [JsonProperty("platPerInvestTier")]
    public int PlatPerInvestTier = 2;

    [JsonProperty("investTierSize")]
    public int InvestTierSize = 20;

    [JsonProperty("affinityBase")]
    public int AffinityBase = 5;
}

internal sealed class DispatchConfigSection
{
    [JsonProperty("lockpickPerChest")]
    public int LockpickPerChest = 20;

    [JsonProperty("lockpickMaxChests")]
    public int LockpickMaxChests = 12;

    [JsonProperty("bossRewardMult")]
    public float BossRewardMult = 1.2f;

    [JsonProperty("regionSpecialtyMult")]
    public int RegionSpecialtyMult = 2;

    [JsonProperty("expExploreSuccess")]
    public int ExpExploreSuccess = 80;

    [JsonProperty("expExploreFailure")]
    public int ExpExploreFailure = 40;

    [JsonProperty("expExploreRecall")]
    public int ExpExploreRecall = 20;

    [JsonProperty("expLockpickSuccess")]
    public int ExpLockpickSuccess = 50;

    [JsonProperty("expLockpickFailure")]
    public int ExpLockpickFailure = 25;

    [JsonProperty("expLockpickRecall")]
    public int ExpLockpickRecall = 10;

    [JsonProperty("expGatherSuccess")]
    public int ExpGatherSuccess = 60;

    [JsonProperty("expGatherFailure")]
    public int ExpGatherFailure = 30;

    [JsonProperty("expGatherRecall")]
    public int ExpGatherRecall = 15;

    [JsonProperty("fameBase")]
    public int FameBase = 30;

    [JsonProperty("famePerDanger")]
    public int FamePerDanger = 2;
}

internal sealed class ProcessConfigSection
{
    /// <summary>
    /// Slice B NPC process <b>speed</b> multiplier, not a duration multiplier:
    /// duration = skill-scaled base / this value. 1.0 is vanilla speed, above 1.0 is
    /// faster (shorter duration). This replaced the old hard-coded 2x speed-up; the
    /// shipped default was 1.0 (vanilla) until 2026-10-14, when it was lowered to
    /// 0.25 (a quarter of vanilla speed) on request.
    ///
    /// Bounds live in <see cref="LaborConfig.NpcSpeedMultiplier"/>; a hand-edited json
    /// can put anything here and only the folded property is trusted.
    /// </summary>
    [JsonProperty("npcSpeedMultiplier")]
    public float NpcSpeedMultiplier = 0.25f;
}

internal sealed class UnlockConfigSection
{
    [JsonProperty("regionBranchLv")]
    public int RegionBranchLv = 3;

    [JsonProperty("dungeonBranchLv")]
    public int DungeonBranchLv = 5;
}

internal static class LaborConfig
{
    static LaborConfigFile _data = CreateDefaults();
    static bool _loaded;
    static string? _configPath;
    static float _lastWrite = -99f;
    static bool _writePending;

    internal static LaborConfigFile Data
    {
        get
        {
            EnsureLoaded();
            return _data;
        }
    }

    internal static TownLaborConfigSection Town
    {
        get
        {
            EnsureLoaded();
            return _data.TownLabor ?? (_data.TownLabor = new TownLaborConfigSection());
        }
    }

    internal static DispatchConfigSection Dispatch
    {
        get
        {
            EnsureLoaded();
            return _data.Dispatch ?? (_data.Dispatch = new DispatchConfigSection());
        }
    }

    internal static ProcessConfigSection Process
    {
        get
        {
            EnsureLoaded();
            return _data.Process ?? (_data.Process = new ProcessConfigSection());
        }
    }

    internal static UnlockConfigSection Unlock
    {
        get
        {
            EnsureLoaded();
            return _data.Unlock ?? (_data.Unlock = new UnlockConfigSection());
        }
    }

    internal static FeatureConfigSection Features
    {
        get
        {
            EnsureLoaded();
            return _data.Features ?? (_data.Features = new FeatureConfigSection());
        }
    }

    /// <summary>Master switch. Off parks every slice and stops the hourly tick.</summary>
    internal static bool FeatureEnabled => Features.Enabled;

    /// <summary>
    /// Slice switches, already folded with the master switch. These gate entry
    /// points only (can I start a new job?) so a slice turned off mid-flight
    /// still lets the running job settle instead of stranding it.
    /// </summary>
    internal static bool FeatureDispatch => FeatureEnabled && Features.Dispatch;
    internal static bool FeatureTownLabor => FeatureEnabled && Features.TownLabor;
    internal static bool FeatureCraft => FeatureEnabled && Features.Craft;


    internal static int MinWagePerHour => Math.Max(0, Town.MinWagePerHour);
    internal static int ExpPerHour => Math.Max(0, Town.ExpPerHour);
    internal static int FurnitureTicketChancePercent => Mathf.Clamp(Town.FurnitureTicketChancePercent, 0, 100);
    internal static int HoursMin => Math.Max(1, Town.HoursMin);
    internal static int HoursMax => Math.Max(HoursMin, Town.HoursMax);
    internal static int InvestTownHeadroom => Math.Max(0, Town.InvestTownHeadroom);
    internal static int PlatBase => Math.Max(0, Town.PlatBase);
    internal static int PlatPerInvestTier => Math.Max(0, Town.PlatPerInvestTier);
    internal static int InvestTierSize => Math.Max(1, Town.InvestTierSize);
    internal static int AffinityBase => Math.Max(0, Town.AffinityBase);

    internal static int LockpickPerChest => Math.Max(1, Dispatch.LockpickPerChest);
    internal static int LockpickMaxChests => Math.Max(0, Dispatch.LockpickMaxChests);
    internal static float BossRewardMult => Mathf.Max(0.1f, Dispatch.BossRewardMult);
    internal static int RegionSpecialtyMult => Math.Max(1, Dispatch.RegionSpecialtyMult);
    internal static int FameBase => Math.Max(0, Dispatch.FameBase);
    internal static int FamePerDanger => Math.Max(0, Dispatch.FamePerDanger);

    /// <summary>
    /// Folded slice B NPC process <b>speed</b> multiplier (see
    /// <see cref="ProcessConfigSection.NpcSpeedMultiplier"/>). Clamped so a bad value in
    /// labor_config.json can never divide by ~0 (which would give a gigantic duration) or
    /// invert the meaning of the slider.
    /// </summary>
    internal static float NpcSpeedMultiplier => Mathf.Clamp(Process.NpcSpeedMultiplier, 0.1f, 4f);

    internal static int RegionUnlockBranchLv => Math.Max(0, Unlock.RegionBranchLv);
    internal static int DungeonUnlockBranchLv => Math.Max(0, Unlock.DungeonBranchLv);

    /// <summary>Absolute path of labor_config.json, or "" when the mod dir is unknown.</summary>
    internal static string ConfigPath
    {
        get
        {
            if (_configPath == null)
            {
                _configPath = ResolveConfigPath();
            }

            return _configPath;
        }
    }

    /// <summary>
    /// Write the live values back to labor_config.json. Called straight from the
    /// config screen, so a slider drag is throttled down to one write every few
    /// frames and <see cref="TickAutoSave"/> flushes the tail once the drag stops.
    /// </summary>
    internal static void Save(bool immediate = false)
    {
        string path = ConfigPath;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (!immediate && Time.realtimeSinceStartup - _lastWrite < SaveThrottleSeconds)
        {
            _writePending = true;
            return;
        }

        try
        {
            System.IO.File.WriteAllText(path, JsonConvert.SerializeObject(_data, Formatting.Indented));
            _lastWrite = Time.realtimeSinceStartup;
            _writePending = false;
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("labor_config save failed: " + ex.Message);
        }
    }

    /// <summary>Cheap per-frame flush for a throttled save. No-op when nothing is pending.</summary>
    internal static void TickAutoSave()
    {
        if (!_writePending)
        {
            return;
        }

        if (Time.realtimeSinceStartup - _lastWrite >= SaveThrottleSeconds)
        {
            Save(immediate: true);
        }
    }

    /// <summary>Throw the whole table back to the coded defaults and persist that.</summary>
    internal static void ResetToDefaults()
    {
        _data = CreateDefaults();
        _loaded = true;
        Save(immediate: true);
        Plugin.LogInfo("labor config reset to defaults.");
    }

    internal static void Reload()
    {
        _loaded = false;
        _data = CreateDefaults();
        EnsureLoaded();
    }

    internal static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _data = CreateDefaults();

        try
        {
            string path = ConfigPath;
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            {
                Plugin.LogDebug("labor_config.json not found; using defaults.");
                return;
            }

            string json = System.IO.File.ReadAllText(path);
            LaborConfigFile? file = JsonConvert.DeserializeObject<LaborConfigFile>(json);
            if (file == null)
            {
                Plugin.LogWarn("labor_config.json empty; using defaults.");
                return;
            }

            _data = MergeWithDefaults(file);
            Plugin.LogInfo("labor config loaded from " + path);
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("labor_config load failed: " + ex.Message);
            _data = CreateDefaults();
        }
    }

    static LaborConfigFile CreateDefaults()
        => new LaborConfigFile
        {
            TownLabor = new TownLaborConfigSection(),
            Dispatch = new DispatchConfigSection(),
            Process = new ProcessConfigSection(),
            Unlock = new UnlockConfigSection(),
        };

    static LaborConfigFile MergeWithDefaults(LaborConfigFile file)
    {
        LaborConfigFile d = CreateDefaults();
        if (file.Features != null)
        {
            d.Features = file.Features;
        }

        if (file.TownLabor != null)
        {
            d.TownLabor = file.TownLabor;
        }

        if (file.Dispatch != null)
        {
            d.Dispatch = file.Dispatch;
        }

        if (file.Process != null)
        {
            d.Process = file.Process;
        }

        if (file.Unlock != null)
        {
            d.Unlock = file.Unlock;
        }

        return d;
    }

    /// <summary>How long a slider must sit still before the throttled write lands.</summary>
    const float SaveThrottleSeconds = 0.4f;

    static string ResolveConfigPath()
    {
        try
        {
            string? asm = typeof(LaborConfig).Assembly.Location;
            if (!string.IsNullOrEmpty(asm))
            {
                string dir = Path.GetDirectoryName(asm) ?? "";
                if (!string.IsNullOrEmpty(dir))
                {
                    return Path.Combine(dir, "labor_config.json");
                }
            }
        }
        catch
        {
        }


         // No hardcoded fallback path — rely on Assembly.Location relative lookup only.
         return "";
    }

    internal static int DispatchExpExplore(global::NpcLabor.Dispatch.DispatchSettleKind kind)
    {
        EnsureLoaded();
        return kind switch
        {
            global::NpcLabor.Dispatch.DispatchSettleKind.Success => Math.Max(0, Dispatch.ExpExploreSuccess),
            global::NpcLabor.Dispatch.DispatchSettleKind.Failure => Math.Max(0, Dispatch.ExpExploreFailure),
            global::NpcLabor.Dispatch.DispatchSettleKind.Recall => Math.Max(0, Dispatch.ExpExploreRecall),
            _ => 10,
        };
    }

    internal static int DispatchExpLockpick(global::NpcLabor.Dispatch.DispatchSettleKind kind)
    {
        EnsureLoaded();
        return kind switch
        {
            global::NpcLabor.Dispatch.DispatchSettleKind.Success => Math.Max(0, Dispatch.ExpLockpickSuccess),
            global::NpcLabor.Dispatch.DispatchSettleKind.Failure => Math.Max(0, Dispatch.ExpLockpickFailure),
            global::NpcLabor.Dispatch.DispatchSettleKind.Recall => Math.Max(0, Dispatch.ExpLockpickRecall),
            _ => 5,
        };
    }

    internal static int DispatchExpGather(global::NpcLabor.Dispatch.DispatchSettleKind kind)
    {
        EnsureLoaded();
        return kind switch
        {
            global::NpcLabor.Dispatch.DispatchSettleKind.Success => Math.Max(0, Dispatch.ExpGatherSuccess),
            global::NpcLabor.Dispatch.DispatchSettleKind.Failure => Math.Max(0, Dispatch.ExpGatherFailure),
            global::NpcLabor.Dispatch.DispatchSettleKind.Recall => Math.Max(0, Dispatch.ExpGatherRecall),
            _ => 8,
        };
    }
}
