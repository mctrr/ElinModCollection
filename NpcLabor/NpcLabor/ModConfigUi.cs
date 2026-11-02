using System;
using NpcLabor.Process;
using UnityEngine;

namespace NpcLabor;

/// <summary>
/// Builds the config tab that <c>LayerModConfig</c> (base game, integrated in 0.23.350)
/// renders for <c>ModPackage.onBuildConfig</c>. The base game itself only reads that
/// field; the shipped ModdingKit package is what would otherwise populate it, and it
/// does so from <c>Core.StartCase</c> - see <see cref="Plugin.OnBuildConfig"/> for why
/// we wire the delegate ourselves instead.
///
/// Every row writes straight into <see cref="LaborConfig.Data"/> and persists through
/// <see cref="LaborConfig.Save"/>, so a change lands in labor_config.json and takes
/// effect on the next tick without a restart.
/// </summary>
internal static class ModConfigUi
{
    static UINote? _builtNote;
    static int _builtFrame = -1;

    internal static void Build(UINote note)
    {
        // LayerModConfig.Show does note.Clear() -> onBuildConfig.Invoke(note) -> note.Build(),
        // so one open means one invoke. If the delegate ever carries a second registrant
        // (the shipped ModdingKit appends its own for IModConfig implementors) both run
        // inside that single Invoke: same note, same frame. Drawing twice would duplicate
        // every row, so only the first call per (note, frame) does any work.
        if (ReferenceEquals(_builtNote, note) && _builtFrame == Time.frameCount)
        {
            return;
        }

        _builtNote = note;
        _builtFrame = Time.frameCount;

        LaborConfigFile c = LaborConfig.Data;

        note.AddHeader(LaborText.T("cfg.title"), null);
        note.AddTopic(LaborText.T("cfg.hint"), "");

        BuildFeatures(note, c);
        BuildTown(note, c);
        BuildDispatch(note, c);
        BuildProcess(note, c);
        BuildUnlock(note, c);
        BuildDebug(note, c);
    }

    internal static void Reset()
        => LaborConfig.ResetToDefaults();

    static void BuildFeatures(UINote note, LaborConfigFile c)
    {
        note.AddHeader(LaborText.T("cfg.sec.features"), null);
        Toggle(note, "cfg.feat.enabled", () => c.Features.Enabled, v => c.Features.Enabled = v);
        Toggle(note, "cfg.feat.dispatch", () => c.Features.Dispatch, v => c.Features.Dispatch = v);
        Toggle(note, "cfg.feat.townLabor", () => c.Features.TownLabor, v => c.Features.TownLabor = v);
        Toggle(note, "cfg.feat.craft", () => c.Features.Craft, v => c.Features.Craft = v);
    }

    static void BuildTown(UINote note, LaborConfigFile c)
    {
        TownLaborConfigSection t = c.TownLabor;
        note.AddHeader(LaborText.T("cfg.sec.town"), null);

        Int(note, "cfg.town.minWage", () => t.MinWagePerHour, v => t.MinWagePerHour = v, 0, 500);
        Int(note, "cfg.town.expPerHour", () => t.ExpPerHour, v => t.ExpPerHour = v, 0, 200);
        Int(note, "cfg.town.ticket", () => t.FurnitureTicketChancePercent, v => t.FurnitureTicketChancePercent = v, 0, 100);
        Int(note, "cfg.town.hoursMin", () => t.HoursMin, v => t.HoursMin = v, 1, 24);
        Int(note, "cfg.town.hoursMax", () => t.HoursMax, v => t.HoursMax = v, 1, 24);
        Int(note, "cfg.town.platBase", () => t.PlatBase, v => t.PlatBase = v, 0, 100);
        Int(note, "cfg.town.platPerTier", () => t.PlatPerInvestTier, v => t.PlatPerInvestTier = v, 0, 100);
        Int(note, "cfg.town.tierSize", () => t.InvestTierSize, v => t.InvestTierSize = v, 1, 500);
        Int(note, "cfg.town.affinity", () => t.AffinityBase, v => t.AffinityBase = v, 0, 100);
    }

    static void BuildDispatch(UINote note, LaborConfigFile c)
    {
        DispatchConfigSection d = c.Dispatch;
        note.AddHeader(LaborText.T("cfg.sec.dispatch"), null);

        Int(note, "cfg.dis.lockpickPerChest", () => d.LockpickPerChest, v => d.LockpickPerChest = v, 1, 200);
        Int(note, "cfg.dis.lockpickMaxChests", () => d.LockpickMaxChests, v => d.LockpickMaxChests = v, 0, 64);
        Float(note, "cfg.dis.bossMult", () => d.BossRewardMult, v => d.BossRewardMult = v, 0.5f, 5f, "0.00");
        Int(note, "cfg.dis.specialtyMult", () => d.RegionSpecialtyMult, v => d.RegionSpecialtyMult = v, 1, 10);
        Int(note, "cfg.dis.expExploreSuccess", () => d.ExpExploreSuccess, v => d.ExpExploreSuccess = v, 0, 500);
        Int(note, "cfg.dis.expExploreFailure", () => d.ExpExploreFailure, v => d.ExpExploreFailure = v, 0, 500);
        Int(note, "cfg.dis.expExploreRecall", () => d.ExpExploreRecall, v => d.ExpExploreRecall = v, 0, 500);
        Int(note, "cfg.dis.expLockpickSuccess", () => d.ExpLockpickSuccess, v => d.ExpLockpickSuccess = v, 0, 500);
        Int(note, "cfg.dis.expLockpickFailure", () => d.ExpLockpickFailure, v => d.ExpLockpickFailure = v, 0, 500);
        Int(note, "cfg.dis.expLockpickRecall", () => d.ExpLockpickRecall, v => d.ExpLockpickRecall = v, 0, 500);
        Int(note, "cfg.dis.expGatherSuccess", () => d.ExpGatherSuccess, v => d.ExpGatherSuccess = v, 0, 500);
        Int(note, "cfg.dis.expGatherFailure", () => d.ExpGatherFailure, v => d.ExpGatherFailure = v, 0, 500);
        Int(note, "cfg.dis.expGatherRecall", () => d.ExpGatherRecall, v => d.ExpGatherRecall = v, 0, 500);
        Int(note, "cfg.dis.fameBase", () => d.FameBase, v => d.FameBase = v, 0, 500);
        Int(note, "cfg.dis.famePerDanger", () => d.FamePerDanger, v => d.FamePerDanger = v, 0, 100);
    }

    /// <summary>
    /// Slice B NPC process speed multiplier. One slider only; the row's value is a
    /// <b>speed</b> factor (see <see cref="ProcessConfigSection.NpcSpeedMultiplier"/>).
    ///
    /// The set lambda is also the multiplier's only writer, so it is where the cached
    /// per-job duration has to be dropped: without that the job list would keep showing
    /// an ETA computed at the old value until the running craft finishes.
    /// </summary>
    static void BuildProcess(UINote note, LaborConfigFile c)
    {
        ProcessConfigSection p = c.Process;
        note.AddHeader(LaborText.T("cfg.sec.process"), null);

        Float(note, "cfg.proc.speed", () => p.NpcSpeedMultiplier, v =>
        {
            p.NpcSpeedMultiplier = v;
            ProcessorJobSession.InvalidateDurationCache();
        }, 0.1f, 4f, "0.00");
    }

    static void BuildUnlock(UINote note, LaborConfigFile c)
    {
        UnlockConfigSection u = c.Unlock;
        note.AddHeader(LaborText.T("cfg.sec.unlock"), null);

        Int(note, "cfg.unlock.region", () => u.RegionBranchLv, v => u.RegionBranchLv = v, 0, 20);
        Int(note, "cfg.unlock.dungeon", () => u.DungeonBranchLv, v => u.DungeonBranchLv = v, 0, 20);
    }

    /// <summary>
    /// Debug helpers (player request 2026-10-30): permanent postings settle on
    /// whole months, which makes them painful to test. These two buttons ride
    /// the exact same monthly accounting the hour tick uses — no parallel payout
    /// path — so what a test run pays is what a real month pays.
    /// </summary>
    static void BuildDebug(UINote note, LaborConfigFile c)
    {
        note.AddHeader(LaborText.T("cfg.sec.debug"), null);
        note.AddTopic(LaborText.T("cfg.dbg.pdispHint"), "");

        note.AddButton(LaborText.T("cfg.dbg.pdispMonth"), () =>
        {
            try
            {
                int n = NpcLabor.PermanentDispatch.PermanentDispatchManager.DebugAdvanceMonths(1);
                Msg.Say(LaborText.T("cfg.dbg.pdispMonthDone", n));
                SE.Click();
            }
            catch (Exception ex)
            {
                Plugin.LogWarn("config debug advance: " + ex.Message);
            }
        });

        note.AddButton(LaborText.T("cfg.dbg.pdispFinish"), () =>
        {
            try
            {
                int n = NpcLabor.PermanentDispatch.PermanentDispatchManager.DebugFinishAll();
                Msg.Say(LaborText.T("cfg.dbg.pdispFinishDone", n));
                SE.Click();
            }
            catch (Exception ex)
            {
                Plugin.LogWarn("config debug finish: " + ex.Message);
            }
        });
    }

    static void Toggle(UINote note, string key, Func<bool> get, Action<bool> set)
    {
        note.AddToggle(LaborText.T(key), get(), v =>
        {
            try
            {
                set(v);
                LaborConfig.Save();
            }
            catch (Exception ex)
            {
                Plugin.LogWarn("config toggle " + key + ": " + ex.Message);
            }
        });
    }

    static void Int(UINote note, string key, Func<int> get, Action<int> set, int min, int max)
        => Slider(note, key, () => get(), v => set((int)v), min, max, true, "0");

    static void Float(UINote note, string key, Func<float> get, Action<float> set, float min, float max, string format)
        => Slider(note, key, get, set, min, max, false, format);

    /// <summary>
    /// One labelled slider. UINote.AddSlider draws no caption of its own, so the name
    /// goes on the row above it.
    ///
    /// The Func&lt;float,string&gt; argument is the only per-change hook the base game
    /// offers: AddSlider wires it to Slider.onValueChanged and calls it with the new
    /// value purely to refresh the readout. Writing the field from inside it is therefore
    /// both how the value is stored and how the label stays in sync - but it also means
    /// AddSlider invokes us once at build time with the untouched value, hence the
    /// equality guard that keeps merely opening the screen from rewriting the file.
    /// </summary>
    static void Slider(UINote note, string key, Func<float> get, Action<float> set,
                       float min, float max, bool isInt, string format)
    {
        note.AddTopic(LaborText.T(key), "");
        note.AddSlider(get(), v =>
        {
            try
            {
                if (Mathf.Approximately(v, get()))
                {
                    return v.ToString(format);
                }

                set(v);
                LaborConfig.Save();
            }
            catch (Exception ex)
            {
                Plugin.LogWarn("config slider " + key + ": " + ex.Message);
            }

            return v.ToString(format);
        }, min, max, isInt);
    }
}
