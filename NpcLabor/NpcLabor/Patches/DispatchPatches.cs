using System;
using HarmonyLib;
using UnityEngine;
using NpcLabor.Dispatch;
using NpcLabor.TownLabor;
using NpcLabor.PermanentDispatch;
using NpcLabor.Process;
using NpcLabor.Craft;

namespace NpcLabor.Patches;

// Tick once per real game hour everywhere (home, dungeon, region travel).
// Do NOT hook FactionBranch.OnSimulateHour: that only runs on the active PC-faction
// zone, and offline home re-entry Simulate would fast-forward all missed hours again.
[HarmonyPatch(typeof(GameDate), nameof(GameDate.AdvanceHour))]
internal static class DispatchGameDateHourPatch
{
    [HarmonyPostfix]
    static void Postfix()
    {
        // Master switch off parks every slice's hourly progress in one place. The
        // per-slice switches deliberately do NOT come through here: they gate entry
        // points only, so a job already running keeps settling to completion.
        if (!LaborConfig.FeatureEnabled)
        {
            return;
        }

        try
        {
            if (DungeonDispatchManager.Count > 0)
            {
                DungeonDispatchManager.OnSimulateHour();
            }
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("dispatch gamedate hour: " + ex.Message);
        }

        try
        {
            if (TownLaborManager.Count > 0)
            {
                TownLaborManager.OnSimulateHour();
            }
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("townlabor gamedate hour: " + ex.Message);
        }

        try
        {
            if (CraftManager.IsRunning)
            {
                CraftManager.OnSimulateHour();
            }
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("craft gamedate hour: " + ex.Message);
        }

        try
        {
            if (PermanentDispatchManager.Count > 0)
            {
                PermanentDispatchManager.OnSimulateHour();
            }
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("pdispatch gamedate hour: " + ex.Message);
        }

        // Sleep/wait loops AdvanceHour inside AdvanceMin. Write once after that loop
        // so lastSeenWorldRaw matches the final hour, not each hop.
        if (!_deferHourSave)
        {
            FlushHourSaves();
        }
    }

    static bool _deferHourSave;

    internal static void BeginHourBurst()
    {
        _deferHourSave = true;
    }

    internal static void EndHourBurst()
    {
        _deferHourSave = false;
        FlushHourSaves();
    }

    static void FlushHourSaves()
    {
        if (!DungeonDispatchManager.HasPendingHourSave && !TownLaborManager.HasPendingHourSave
            && !PermanentDispatchManager.HasPendingHourSave)
        {
            return;
        }

        try { DungeonDispatchManager.FlushPendingHourSave(); }
        catch (System.Exception __e) { Plugin.LogDebug("DispatchPatches.cs dispatch hour-save: " + __e.Message); }
        try { TownLaborManager.FlushPendingHourSave(); }
        catch (System.Exception __e) { Plugin.LogDebug("DispatchPatches.cs town hour-save: " + __e.Message); }
        try { PermanentDispatchManager.FlushPendingHourSave(); }
        catch (System.Exception __e) { Plugin.LogDebug("DispatchPatches.cs pdispatch hour-save: " + __e.Message); }
    }
}

[HarmonyPatch(typeof(GameDate), nameof(GameDate.AdvanceMin))]
internal static class DispatchGameDateMinFlushPatch
{
    [HarmonyPrefix]
    static void Prefix()
    {
        DispatchGameDateHourPatch.BeginHourBurst();
    }

    [HarmonyFinalizer]
    static void Finalizer()
    {
        DispatchGameDateHourPatch.EndHourBurst();
    }
}
// Final safety net: vanilla EloMapActor.OnChangeHour does light.sr.color with no null check.
// Older NPC Labor builds could insert null-sr lights via AddLight("iconFlag").
[HarmonyPatch(typeof(EloMapActor), nameof(EloMapActor.OnChangeHour))]
internal static class DispatchEloMapActorHourPatch
{
    [HarmonyPrefix]
    static void Prefix()
    {
        try
        {
            DungeonDispatchManager.SanitizeRegionMapLights();
        }
        catch (System.Exception __e) { Plugin.LogDebug("DispatchPatches.cs silent catch: " + __e.Message); }
}
}

// QuestManager.Start always calls Quest.UpdateJournal -> Msg "journalUpdate2".
// Our trackers are display-only; mission start already announced. Suppress the spam
// so F9/load re-pin and hour recovery never yell "地区派遣更新了".
[HarmonyPatch(typeof(Quest), nameof(Quest.UpdateJournal))]
internal static class DispatchQuestUpdateJournalQuietPatch
{
    [HarmonyPrefix]
    static bool Prefix(Quest __instance)
    {
        try
        {
            if (__instance is QuestNpcLaborDispatch || __instance is QuestNpcLaborTownLabor)
            {
                return false;
            }

            // Legacy Dummy rows with our id prefix (mod reinstall mid-save).
            string id = __instance?.id ?? string.Empty;
            if (id.StartsWith("npclabor_dispatch_", StringComparison.Ordinal)
                || id.StartsWith("npclabor_townlabor_", StringComparison.Ordinal))
            {
                return false;
            }
        }
        catch
        {
        }

        return true;
    }
}

[HarmonyPatch(typeof(Game), nameof(Game.OnBeforeSave))]
internal static class DispatchSavePatch
{
    [HarmonyPrefix]
    static void Prefix()
    {
        try
        {
            // Keep live typed pins in game.txt so F9 does not destroy/recreate them.
            // Destroy+Start calls Quest.UpdateJournal ("journalUpdate2") — that is the
            // "地区派遣更新了" spam on save. Only drop Dummy/orphan/dup rows here.
            // Mission truth still lives in npclabor_*.json.
            DungeonDispatchManager.SanitizeBrokenTrackerQuestsForSave();
            TownLaborManager.SanitizeBrokenTrackerQuestsForSave();
            DungeonDispatchManager.Save();
            TownLaborManager.Save();
            CraftManager.Save();
            PermanentDispatchManager.Save();
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("dispatch save patch: " + ex.Message);
        }
    }
}

// After save: list-only dedupe. Do not touch WidgetQuestTracker.items here —
// mutating/DestroyImmediate mid-widget lifetime caused SetActive NREs on Refresh.
[HarmonyPatch(typeof(Game), nameof(Game.Save))]
internal static class DispatchSaveRepinPatch
{
    [HarmonyPostfix]
    static void Postfix()
    {
        try
        {
            DungeonDispatchManager.DedupeNpcLaborTrackerQuests();
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("dispatch save list dedupe: " + ex.Message);
        }
    }
}

[HarmonyPatch(typeof(Game), nameof(Game.OnLoad))]
internal static class DispatchLoadPatch
{
    [HarmonyPostfix]
    static void Postfix()
    {
        try
        {
            SpecialRewardConfig.Reload();
            // Do NOT hard-strip + re-Start pins on load.
            // WidgetQuestTracker matches rows by Quest object reference and only appends.
            // Strip/Start replaces the Quest instance -> old rows stick around -> visual
            // double pins, then ItemQuestTracker.Refresh hits null buttonGoto.SetActive NRE.
            // Load() already heals list (drop Dummy/orphan/dup, Start only if missing).
            DungeonDispatchManager.Load();
            TownLaborManager.Load();
            CraftManager.Load();
            PermanentDispatchManager.Load();
            // List heal only inside Load (Start missing pins). Do not thrash WidgetQuestTracker here.
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("dispatch load patch: " + ex.Message);
        }
    }
}

[HarmonyPatch(typeof(LayerQuestBoard))]
internal static class DispatchQuestBoardPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(LayerQuestBoard.OnInit))]
    static void OnInitPostfix(LayerQuestBoard __instance)
    {
        try
        {
            EnsureDispatchButton(__instance);
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("dispatch OnInit button: " + ex.Message);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(LayerQuestBoard.OnSwitchContent))]
    static void OnSwitchContentPostfix(LayerQuestBoard __instance, Window window)
    {
        try
        {
            EnsureDispatchButton(__instance);

            if (window != null && window.idTab == DungeonDispatchUi.TabDispatch)
            {
                if (!DungeonDispatchUi.IsAtPcFactionHome())
                {
                Msg.Say(NpcLabor.LaborText.T(
                    "dis.msg.baseOnly",
                    NpcLabor.LaborTerms.Dispatch));
                    return;
                }

                DungeonDispatchUi.OpenBoard(__instance);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("dispatch OnSwitchContent: " + ex.Message);
        }
    }

    static void EnsureDispatchButton(LayerQuestBoard board)
    {
        if (board == null)
        {
            return;
        }

        bool atHome = DungeonDispatchUi.IsAtPcFactionHome();

        // Parked mod: the dispatch button never appears on the quest board.
        if (!LaborConfig.FeatureEnabled)
        {
            return;
        }

        Window? host = null;
        try
        {
            if (board.windows != null && board.windows.Count > 0)
            {
                host = board.windows[0];
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("DispatchPatches.cs silent catch: " + __e.Message); }
if (host == null)
        {
            return;
        }

        WindowMenu? menu = null;
        try
        {
            menu = host.menuRight;
        }
        catch (System.Exception __e) { Plugin.LogDebug("DispatchPatches.cs silent catch: " + __e.Message); }
if (menu == null)
        {
            try
            {
                menu = host.menuBottom;
            }
            catch (System.Exception __e) { Plugin.LogDebug("DispatchPatches.cs silent catch: " + __e.Message); }
}

        if (menu == null)
        {
            Plugin.LogDebug("dispatch: no window menu for button");
            return;
        }

        try
        {
            if (TryRefreshDispatchButton(menu))
            {
                return;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("DispatchPatches.cs silent catch: " + __e.Message); }
        if (!atHome)
        {
            return;
        }

        try
        {
            // Show active mission count only; concurrency is per-dungeon (max 4 members).
            int n = DungeonDispatchManager.Count;
            string label = n > 0 ? (NpcLabor.LaborTerms.Dispatch + " (" + n + ")") : NpcLabor.LaborTerms.Dispatch;
            UIButton? btn = menu.AddButton(
                "continue",
                _ =>
                {
                    DungeonDispatchUi.OpenBoard(board);
                },
                sprite: null,
                idButton: "Default");

            if (btn != null)
            {
                ForceDispatchButtonWhite(btn, label);
                Plugin.LogDebug("dispatch button ready: " + label);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("dispatch AddButton failed: " + ex.Message);
        }
    }

    static bool TryRefreshDispatchButton(WindowMenu menu)
    {
        try
        {
            UIButton[] buttons = Array.Empty<UIButton>();
            try
            {
                if (menu.layout != null)
                {
                    buttons = menu.layout.GetComponentsInChildren<UIButton>(true);
                }
            }
            catch
            {
                return false;
            }

            int n = DungeonDispatchManager.Count;
            string label = n > 0 ? (NpcLabor.LaborTerms.Dispatch + " (" + n + ")") : NpcLabor.LaborTerms.Dispatch;
            foreach (UIButton b in buttons)
            {
                try
                {
                    string t = b?.mainText?.text ?? "";
                    // Skip the 常驻派遣 button — it contains "派遣" too.
                    if (t.IndexOf(NpcLabor.LaborTerms.PDispatch, StringComparison.Ordinal) >= 0)
                    {
                        continue;
                    }

                    if (t.IndexOf(NpcLabor.LaborTerms.Dispatch, StringComparison.Ordinal) >= 0
                        || t.IndexOf(NpcLabor.LaborTerms.DungeonExplore, StringComparison.Ordinal) >= 0
                        || t.IndexOf("派遣", StringComparison.Ordinal) >= 0)
                    {
                        ForceDispatchButtonWhite(b, label);
                        return true;
                    }
                }
                catch (System.Exception __e) { Plugin.LogDebug("DispatchPatches.cs silent catch: " + __e.Message); }
}
        }
        catch (System.Exception __e) { Plugin.LogDebug("DispatchPatches.cs silent catch: " + __e.Message); }
return false;
    }

    /// <summary>
    /// Quest board side buttons use a dark skin; ButtonGeneral can read as black.
    /// Force plain white so dispatch label stays readable like other tab labels the player expects.
    /// </summary>
    static void ForceDispatchButtonWhite(UIButton? btn, string label)
    {
        if (btn?.mainText == null)
        {
            return;
        }

        try
        {
            // Skin colors first (Button is typically light on quest board chrome).
            btn.mainText.SetText(label, FontColor.Button);
        }
        catch
        {
            try
            {
                btn.mainText.SetText(label);
            }
            catch
            {
                try { btn.mainText.text = label; } catch { }
            }
        }

        try
        {
            // Absolute override — player asked for white text specifically.
            btn.mainText.color = UnityEngine.Color.white;
        }
        catch (System.Exception __e) { Plugin.LogDebug("DispatchPatches.cs silent catch: " + __e.Message); }
        try
        {
            btn.mainText.SetColor(FontColor.Button);
        }
        catch (System.Exception __e) { Plugin.LogDebug("DispatchPatches.cs silent catch: " + __e.Message); }
        try
        {
            btn.mainText.color = UnityEngine.Color.white;
        }
        catch (System.Exception __e) { Plugin.LogDebug("DispatchPatches.cs silent catch: " + __e.Message); }
}
}

// Zone enter: keep dispatched NPCs on the progress floor as friendly stakeouts.
// Hook Zone.Activate (runs on every real zone entry: EnterLocalZone from the world
// map, LayerTravel, recall, load). Player.MoveZone(Zone) is NOT hit by those paths -
// the normal enter flow goes through Chara.MoveZone(Zone, ZoneTransition) - so region
// dispatch members were never pulled to the player field on arrival.
[HarmonyPatch(typeof(Zone), nameof(Zone.Activate))]
internal static class DispatchZoneEnterPatch
{
    [HarmonyPostfix]
    static void Postfix(Zone __instance)
    {
        try
        {
            if (__instance == null)
            {
                return;
            }

            if (EClass.player != null && EClass.player.simulatingZone)
            {
                return;
            }

            DungeonDispatchManager.OnZoneEntered(__instance);
            TownLaborManager.OnZoneEntered(__instance);
            PermanentDispatchManager.OnZoneEntered(__instance);
            ProcessorJobSession.OnZoneEntered(__instance);
            NpcLabor.Craft.CraftManager.OnZoneEntered();
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("dispatch zone enter: " + ex.Message);
        }
    }
}

// Block talking / menus with dispatched explorers (isRestrained still opens "strain" dialog).
[HarmonyPatch(typeof(Chara), nameof(Chara.ShowDialog))]
[HarmonyPatch(new Type[] { })]
internal static class DispatchShowDialogPatch
{
    [HarmonyPrefix]
    static bool Prefix(Chara __instance)
    {
        try
        {
            if (__instance == null)
            {
                return true;
            }

            string who = __instance.NameSimple ?? __instance.Name ?? NpcLabor.LaborText.T("town.msg.companion");
            if (DungeonDispatchManager.IsBusy(__instance.uid))
            {
                Msg.Say(NpcLabor.LaborText.T("dis.msg.busyTalk", who));
                SE.Beep();
                return false;
            }

            if (TownLaborManager.IsBusy(__instance.uid))
            {
                Msg.Say(NpcLabor.LaborText.T("town.talk.busy", who, NpcLabor.LaborTerms.TownWork));
                SE.Beep();
                return false;
            }

            if (PermanentDispatchManager.IsBusy(__instance.uid))
            {
                Msg.Say(NpcLabor.LaborText.T("pdisp.err.busy", who));
                SE.Beep();
                return false;
            }

            return true;
        }
        catch
        {
            return true;
        }
    }
}

[HarmonyPatch(typeof(LayerInteraction), nameof(LayerInteraction.Show), typeof(IInspect))]
internal static class DispatchLayerInteractionPatch
{
    [HarmonyPrefix]
    static bool Prefix(IInspect newTarget)
    {
        try
        {
            Chara? c = newTarget as Chara;
            if (c == null)
            {
                return true;
            }

            string who = c.NameSimple ?? c.Name ?? NpcLabor.LaborText.T("town.msg.companion");
            if (DungeonDispatchManager.IsBusy(c.uid))
            {
                Msg.Say(NpcLabor.LaborText.T("dis.msg.busyInteract", who));
                SE.Beep();
                return false;
            }

            if (TownLaborManager.IsBusy(c.uid))
            {
                Msg.Say(NpcLabor.LaborText.T("town.talk.noInteract", who, NpcLabor.LaborTerms.TownWork));
                SE.Beep();
                return false;
            }

            if (PermanentDispatchManager.IsBusy(c.uid))
            {
                Msg.Say(NpcLabor.LaborText.T("pdisp.err.busy", who));
                SE.Beep();
                return false;
            }

            return true;
        }
        catch
        {
            return true;
        }
    }
}

[HarmonyPatch(typeof(QuestManager), nameof(QuestManager.OnShowDialog))]
internal static class DispatchQuestOnShowDialogPatch
{
    [HarmonyPrefix]
    static void Prefix(QuestManager __instance)
    {
        try
        {
            if (__instance?.list == null)
            {
                return;
            }

            foreach (Quest q in __instance.list)
            {
                if (q == null)
                {
                    continue;
                }

                try
                {
                    if (q is QuestNpcLaborDispatch dq)
                    {
                        dq.EnsureSafePerson();
                        dq.deadline = 0;
                    }
                    else if (q is QuestNpcLaborTownLabor tq)
                    {
                        tq.EnsureSafePerson();
                        tq.deadline = 0;
                    }
                    else if (q.person == null)
                    {
                        q.person = new Person();
                    }

                    // Touch once so a bad person state fails here, not in vanilla.
                    _ = q.person;
                    _ = q.person?.chara;
                }
                catch
                {
                    try
                    {
                        q.person = new Person();
                    }
                    catch (System.Exception __e) { Plugin.LogDebug("DispatchPatches.cs silent catch: " + __e.Message); }
}
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("DispatchPatches.cs silent catch: " + __e.Message); }
}
}
