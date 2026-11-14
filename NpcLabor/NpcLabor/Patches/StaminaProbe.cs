using System;
using System.Reflection;
using HarmonyLib;
using NpcLabor.CoCraft;

namespace NpcLabor.Patches;

/// <summary>
/// Read-only deep probe for "assist SP cut is invisible" reports.
///
/// StatsStamina.Mod is the virtual override that applies EVERY stamina change.
/// Before calling Stats.Mod it re-rolls and/or scales the incoming delta with the
/// PC's deity blessings (elements 1330 / 1403), so a GetCostSp-level log only
/// proves our postfix changed an argument - it says nothing about how much the
/// stamina bar actually lost. This probe measures both, without touching the
/// parameter, the result, or any game state.
///
/// Unlike every other patch here it is NOT mounted at boot: it carries no
/// [HarmonyPatch] attribute, so PatchAudit ignores it and the always-on audit
/// count stays a count of gameplay patches only. Install(bool) is driven by the
/// in-game "Verbose diagnostics" switch. With the switch OFF (the default) the
/// Harmony patch does not exist at all - no hook, no snapshot, no log line.
/// Turning the switch on mounts it in the same frame; turning it off unpatches
/// only our own prefix/postfix on that method.
/// </summary>
internal static class StaminaProbe
{
    static bool _installed;

    internal sealed class Snap
    {
        public int Before;
        public int In;
        public bool Assist;
    }

    /// <summary>Reported in the always-on ON/OFF boundary lines so a log can prove the probe state.</summary>
    internal static bool Installed => _installed;

    /// <summary>
    /// Mount or unmount the probe. Safe to call repeatedly (boot, toggle, config
    /// reset): the state check makes every call after the first a single bool test.
    /// </summary>
    internal static void Install(bool enabled)
    {
        Harmony? harmony = Plugin.Harmony;
        if (harmony == null || _installed == enabled)
        {
            return;
        }

        MethodBase? target = AccessTools.Method(typeof(StatsStamina), nameof(StatsStamina.Mod));
        if (target == null)
        {
            Plugin.LogWarn("[probe] StatsStamina.Mod not found - stamina probe unavailable");
            return;
        }

        try
        {
            if (enabled)
            {
                harmony.Patch(target,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(StaminaProbe), nameof(Prefix))),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(StaminaProbe), nameof(Postfix))));
                _installed = true;
                Plugin.LogInfo("[probe] StatsStamina.Mod mounted (verbose ON)");
            }
            else
            {
                // harmony.Id scopes this to our own patches; other mods on Mod() stay intact.
                harmony.Unpatch(target, HarmonyPatchType.All, harmony.Id);
                _installed = false;
            }
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("[probe] stamina probe mount/unmount failed: " + ex.Message);
        }
    }

    private static void Prefix(StatsStamina __instance, int a, out Snap? __state)
    {
        __state = null;
        // Belt and braces: Install() already unmounts on OFF, but a config write
        // landing mid-frame must never produce an ungated line.
        if (!Plugin.VerboseLog)
        {
            return;
        }

        __state = new Snap
        {
            Before = __instance.value,
            In = a,
            Assist = CoCraftSession.Active,
        };
    }

    private static void Postfix(StatsStamina __instance, Snap? __state)
    {
        if (__state == null)
        {
            return;
        }

        int after = __instance.value;
        int actual = __state.Before - after;   // positive = SP actually spent
        int requested = -__state.In;           // positive = SP the crafter asked for
        bool rerolled = __state.In < 0 && actual != requested;

        // One line per stamina change. A/B reads group by assist= and by repeated
        // a=-N batches; the [co] open/clear lines bracket each craft session.
        Plugin.LogDebug(
            $"[sp] assist={(__state.Assist ? "ON" : "OFF")} a={__state.In} " +
            $"value={__state.Before}->{after} actual={actual} " +
            $"requested={requested} rerolled={rerolled}");
    }
}
