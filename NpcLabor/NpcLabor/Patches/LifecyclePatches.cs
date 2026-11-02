using HarmonyLib;
using System.Collections.Generic;
using NpcLabor.CoCraft;
using NpcLabor.Craft;
using NpcLabor.Dispatch;
using NpcLabor.TownLabor;
using NpcLabor.Process;

namespace NpcLabor.Patches;

[HarmonyPatch(typeof(AI_UseCrafter))]
internal static class AiUseCrafterPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(AI_UseCrafter.OnStart))]
    static void OnStartPrefix(AI_UseCrafter __instance)
    {
        try
        {
            OpenFromAi(__instance);
        }
        catch (System.Exception ex)
        {
            Plugin.LogWarn($"co-craft OnStart failed: {ex}");
        }
    }

    /// <summary>
    /// Hold this one AI_UseCrafter enumerator until the assistant arrives.
    /// Do not patch AIAct.Tick — that is every character every turn.
    /// AIAct.Start sets Enumerator = Run() then calls OnStart, so wrapping here
    /// is equivalent to the old Tick skip without a global detour.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(nameof(AI_UseCrafter.OnStart))]
    static void OnStartPostfix(AI_UseCrafter __instance)
    {
        try
        {
            if (!CoCraftSession.Active || __instance == null || EClass.pc == null
                || __instance.owner != EClass.pc)
            {
                return;
            }

            if (CoCraftSession.IsAssistantReady())
            {
                return;
            }

            if (CoCraftSession.ApproachExpired())
            {
                CoCraftSession.SnapCurrentAssistant();
                return;
            }

            IEnumerator<AIAct.Status>? inner = __instance.Enumerator;
            __instance.Enumerator = WaitForAssistantThen(inner).GetEnumerator();
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("co-craft wait wrap: " + ex.Message);
        }
    }

    static IEnumerable<AIAct.Status> WaitForAssistantThen(IEnumerator<AIAct.Status>? inner)
    {
        try
        {
            while (CoCraftSession.Active
                && !CoCraftSession.IsAssistantReady()
                && !CoCraftSession.ApproachExpired())
            {
                yield return AIAct.Status.Running;
            }

            // Timeout/blocked path: snap here. The approach AI may still be inside
            // AI_Goto and cannot run its own fallback until that child ends.
            if (CoCraftSession.Active && !CoCraftSession.IsAssistantReady())
            {
                CoCraftSession.SnapCurrentAssistant();
            }

            if (inner == null)
            {
                yield break;
            }

            while (inner.MoveNext())
            {
                yield return inner.Current;
            }
        }
        finally
        {
            try { inner?.Dispose(); } catch { }
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(AI_UseCrafter.OnEnd))]
    static void OnEndPrefix()
    {
        CoCraftSession.Clear("ai-end");
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(AI_UseCrafter.OnCancel))]
    static void OnCancelPrefix()
    {
        // OnCancel calls OnEnd; clear early so any cancel-side skill reads are vanilla.
        CoCraftSession.Clear("ai-cancel");
    }

    /// <summary>
    /// While the craft is held waiting for the assistant (AI tick hold), allow the
    /// player to right-click cancel. Vanilla LayerCraft.CanCancelAI is false, so
    /// without this the PC is stuck waiting with no way out.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(nameof(AI_UseCrafter.CanManualCancel))]
    static bool CanManualCancelPrefix(ref bool __result)
    {
        try
        {
            if (CoCraftSession.Active && !CoCraftSession.IsAssistantReady())
            {
                __result = true;
                return false;
            }
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("co-craft manual cancel: " + ex.Message);
        }

        return true;
    }

    internal static void OpenFromAi(AI_UseCrafter ai)
    {
        if (ai == null || EClass.pc == null)
        {
            return;
        }

        // Only assist when the PC is the craft owner.
        if (ai.owner != null && ai.owner != EClass.pc)
        {
            return;
        }

        // Slice B self-processing reuses the vanilla AI_UseCrafter conversion path
        // (recipe == null on whitelisted drag-grid machines). Co-craft assist must
        // never attach to that flow: there is no 协助 button on a drag-grid
        // processor, and an open assist pin/auto would otherwise resolve an
        // assistant here and hold the PC's conversion until that NPC walks over —
        // even when the machine operator is set to 自己 (self).
        if (ai.recipe == null && ai.crafter != null
            && ProcessorWhitelist.IsSupported(ai.crafter))
        {
            return;
        }

        int skillId = 0;
        Recipe? recipe = ai.recipe;
        TraitCrafter? crafter = ai.crafter;

        if (recipe?.source != null)
        {
            skillId = recipe.source.GetReqSkill().id;
        }
        else if (crafter != null)
        {
            string alias = crafter.IDReqEle(recipe?.source);
            if (!alias.IsEmpty())
            {
                try
                {
                    skillId = EClass.sources.elements.alias[alias].id;
                }
                catch
                {
                    skillId = 0;
                }
            }
        }

        if (skillId == 0)
        {
            Plugin.LogInfo("[cocraft] OpenFromAi: no resolvable element alias, no assistant (ai.recipe=" + (ai.recipe == null ? "null" : ai.recipe.id.ToString()) + ")");
            return;
        }

        Chara? assistant = AssistantResolver.ResolveForCraft(skillId);
        if (assistant == null)
        {
            Plugin.LogInfo($"[cocraft] OpenFromAi: skill={skillId} but no assistant resolved (pin={AssistantResolver.PinnedUid?.ToString() ?? "null"})");
            CoCraftSession.Clear("no-assistant");
            return;
        }

        Plugin.LogInfo($"[cocraft] session open skill={skillId} assistant={AssistantResolver.NameOf(assistant)} uid={assistant.uid}");

        CoCraftSession.Open(assistant, recipe, crafter);
        if (ai.num > 0)
        {
            CoCraftSession.BatchNum = ai.num;
        }
    }
}

[HarmonyPatch(typeof(Chara), nameof(Chara.Die))]
internal static class PcDeathClearPatch
{
    [HarmonyPrefix]
    static void Prefix(Chara __instance)
    {
        if (__instance == null)
        {
            return;
        }

        if (__instance.IsPC)
        {
            if (CoCraftSession.Active)
            {
                CoCraftSession.Clear("pc-death");
            }

            if (ProcessorJobSession.Count > 0)
            {
                ProcessorJobSession.ClearAll("pc-death");
            }

            return;
        }

        // Worker death aborts their processor job(s).
        for (int j = ProcessorJobSession.Jobs.Count - 1; j >= 0; j--)
        {
            ProcessorJob job = ProcessorJobSession.Jobs[j];
            if (job != null && job.Active && __instance.uid == job.NpcUid)
            {
                ProcessorJobSession.Clear(job, "ai-fail");
            }
        }

        // Dispatch worker death drops the mission (no death penalty rewards).
        try
        {
            DungeonDispatchManager.OnWorkerDied(__instance);
        }
        catch (System.Exception __e) { Plugin.LogDebug("LifecyclePatches.cs silent catch: " + __e.Message); }
// Town labor worker death aborts mission (no main prize).
        try
        {
            TownLaborManager.OnWorkerDied(__instance);
        }
        catch (System.Exception __e) { Plugin.LogDebug("LifecyclePatches.cs silent catch: " + __e.Message); }
}
}

[HarmonyPatch(typeof(Player), nameof(Player.MoveZone), typeof(Zone))]
internal static class ZoneChangeClearPatch
{
    [HarmonyPrefix]
    static void Prefix()
    {
        if (CoCraftSession.Active)
        {
            CoCraftSession.Clear("zone-change");
        }

        // Safety net after scene move. Real hold should already have run on Chara.MoveZone.
        if (ProcessorJobSession.Count > 0)
        {
            ProcessorJobSession.OnPcLeavingZone();
        }
    }
}


/// <summary>
/// One answer to "is this a real zone leave?" for the two <c>Chara.MoveZone</c>
/// handlers below (processor hold, town labor recall). A null PC, a null target
/// and the same-zone no-op are all not leaves. Kept here so the two handlers
/// cannot disagree about what counts as leaving.
/// </summary>
internal static class ZoneLeaveGuard
{
    internal static bool IsRealLeave(Chara? c, Zone? z)
    {
        if (c == null || !c.IsPC)
        {
            return false;
        }

        if (z == null)
        {
            return false;
        }

        try
        {
            return c.currentZone == null || c.currentZone.uid != z.uid;
        }
        catch
        {
            return true;
        }
    }
}

/// <summary>
/// Early PC leave: hold resident process BEFORE map unload cancels freestanding AI.
/// </summary>
[HarmonyPatch(typeof(Chara), nameof(Chara.MoveZone), typeof(Zone), typeof(ZoneTransition))]
internal static class ProcessorLeaveZonePatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.Low)]
    static void Prefix(Chara __instance, Zone z)
    {
        try
        {
            if (!ZoneLeaveGuard.IsRealLeave(__instance, z))
            {
                return;
            }

            if (ProcessorJobSession.Count == 0 || ProcessorJobSession.AllSuspended)
            {
                return;
            }

            // PC self-work leave menu may abort the move; hold the processor
            // suspend until the real leave happens. Companion labor is recalled
            // synchronously by the leave patch and the move goes through, so it
            // must NOT skip the suspend here.
            try
            {
                if (TownLaborManager.HasActivePcSelfLabor())
                {
                    return;
                }
            }
            catch { }

            ProcessorJobSession.OnPcLeavingZone();
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("processor leave zone: " + ex.Message);
        }
    }
}

/// <summary>
/// Leaving a work zone while companion labor is active recalls the workers
/// on the spot (Slice X: no off-map ticking). PC self-work keeps an explicit
/// leave menu. Real leave starts at Chara.MoveZone; Player.MoveZone is scene
/// init after the move is already committed.
/// </summary>
[HarmonyPatch(typeof(Chara), nameof(Chara.MoveZone), typeof(Zone), typeof(ZoneTransition))]
internal static class TownLaborLeaveConfirmPatch
{
    [HarmonyPrefix]
    static bool Prefix(Chara __instance, Zone z)
    {
        try
        {
            if (!ZoneLeaveGuard.IsRealLeave(__instance, z))
            {
                return true;
            }

            // PC self-work: multi-choice leave menu (abort / hand off / cancel).
            // The menu resolves on-map; the next leave attempt is free once done.
            if (TownLaborManager.HasActivePcSelfLabor())
            {
                TownLaborUi.OpenPcSelfLeaveMenu();
                return false;
            }

            // Companion labor: leaving the work zone recalls the workers right
            // here and lets vanilla finish the move. No dialog, no deferred
            // MoveZone re-entry with a stale transition (the recall-scroll
            // save-corruption source), and the auto-save inside MoveZone now
            // writes a state that carries no live mission.
            try
            {
                TownLaborManager.RecallCompanionLaborInCurrentZone();
            }
            catch (System.Exception ex)
            {
                Plugin.LogWarn("townlabor leave recall: " + ex.Message);
            }

            return true;
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("townlabor leave confirm: " + ex.Message);
            return true;
        }
    }
}

/// <summary>
/// Load reset for slice B. Processor jobs are runtime-only (never written into the
/// save), but a load runs inside the same process: the static Jobs registry survives
/// it and keeps describing cards from the world that was just replaced. Loading an
/// older save then shows the processor as occupied (drag grid refuses to start) and
/// the worker list still holds the previous worker as busy. Drop the registry here.
///
/// <para>
/// Ordering is safe: <c>Game.Load</c> calls <c>OnLoad</c> before <c>scene.Init</c>
/// activates the zone (verified in the Game decompile), so this runs before
/// <c>Zone.Activate</c> can resume a stale job — no catch-up, no ingredient rebind,
/// no dupe window. The drop itself touches no world state (see
/// <see cref="ProcessorJobSession.DropAllOnLoad"/>).
/// </para>
/// </summary>
[HarmonyPatch(typeof(Game), nameof(Game.OnLoad))]
internal static class ProcessorLoadResetPatch
{
    [HarmonyPostfix]
    static void Postfix()
    {
        ProcessorJobSession.DropAllOnLoad();
    }
}

/// <summary>
/// Same reset for the other world-replacement door: starting a new game
/// (<c>Game.Create</c>, used by LayerEmbark). A stale registry would otherwise
/// describe a world that never existed, and card uids in a fresh world can collide
/// with the stale entries' uids — phantom "occupied" on a brand-new map.
///
/// <para>
/// Kept as its own patch class on purpose: if one of the two targets ever fails to
/// bind (signature drift in a game update), Harmony fails the class, not the method
/// — and a dead Create hook must not take the load hook down with it.
/// </para>
/// </summary>
[HarmonyPatch(typeof(Game), nameof(Game.Create))]
internal static class ProcessorNewGameResetPatch
{
    [HarmonyPostfix]
    static void Postfix()
    {
        ProcessorJobSession.DropAllOnLoad();
    }
}

/// <summary>
/// Last frame before a zone is torn down: settle any base-production act that is still
/// holding pantry ingredients.
///
/// <para>
/// <c>Map.OnDeactivate</c> throws every AI away with a direct field write
/// (<c>chara.ai = new NoGoal()</c>), which never reaches <c>Chara.SetAI</c> and so never
/// runs <see cref="AIAct.OnCancel"/>. For base production that is the only path that
/// returns the ingredients the act took out of the pantry — without this prefix they
/// stayed on the bench as loose items, the pantry scan stopped seeing them, and the
/// order sat on "missing materials" with the wood visibly on the bench.
/// </para>
///
/// <para>
/// Prefix, not postfix: the return needs the containers to still be there. Leaving a
/// base mid-shift is the common case, so this also covers dungeon entry, dispatch and
/// quit-to-title.
/// </para>
/// </summary>
[HarmonyPatch(typeof(Map), nameof(Map.OnDeactivate))]
internal static class CraftActZoneTeardownPatch
{
    [HarmonyPrefix]
    static void Prefix(Map __instance)
    {
        NpcLabor.Craft.CraftManager.AbandonActsOnLeave(__instance);
    }
}
