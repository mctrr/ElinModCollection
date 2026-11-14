using System;
using System.Reflection;
using HarmonyLib;

namespace NpcLabor.Patches;

/// <summary>
/// Mounts the co-craft SP postfixes on every override of
/// <c>TraitCrafter.GetCostSp</c> / <c>GetDuration</c>.
///
/// Both methods are virtual. A [HarmonyPatch] on the base declaration only
/// intercepts calls that land on the base implementation; a virtual call on a
/// subclass that overrides it dispatches straight to the override body, so the
/// base patch never runs. That was the "assist reduces SP in the recipe preview
/// but not in game" bug - a full co-craft session reported spHits=0 spSkips=0
/// even though the UI preview showed the reduced value.
///
/// Checked against the nightly IL: TraitFactory.GetCostSp is a standalone
/// implementation (RecipeSource.GetSPCost, no call to base), so patching both
/// the base and its overrides cannot double-adjust the cost.
///
/// Enumerating overrides instead of hard-coding a class list keeps this correct
/// when the game adds new crafter subtypes.
/// </summary>
internal static class CoCraftSpHookInstaller
{
    internal static void Install(Harmony harmony)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        HarmonyMethod spPostfix = new HarmonyMethod(
            AccessTools.Method(typeof(SpPatches), nameof(SpPatches.Postfix)));
        HarmonyMethod durPostfix = new HarmonyMethod(
            AccessTools.Method(typeof(DurationRememberPatch), nameof(DurationRememberPatch.Postfix)));

        int sp = 0;
        int dur = 0;
        Type baseType = typeof(TraitCrafter);

        foreach (Type t in AccessTools.GetTypesFromAssembly(baseType.Assembly))
        {
            if (t == baseType || !baseType.IsAssignableFrom(t))
            {
                continue;
            }

            try
            {
                MethodInfo cost = t.GetMethod("GetCostSp", flags);
                if (cost != null && cost.GetBaseDefinition().DeclaringType == baseType)
                {
                    harmony.Patch(cost, postfix: spPostfix);
                    sp++;
                }

                MethodInfo duration = t.GetMethod("GetDuration", flags);
                if (duration != null && duration.GetBaseDefinition().DeclaringType == baseType)
                {
                    harmony.Patch(duration, postfix: durPostfix);
                    dur++;
                }
            }
            catch (Exception ex)
            {
                Plugin.LogWarn($"[patch] co-craft SP hook on {t.Name} failed: {ex.Message}");
            }
        }

        // Always-on: if a game update changes the override set, the next bug
        // report must show whether the hooks were mounted at all.
        Plugin.LogAlways($"[patch] co-craft SP hooks: GetCostSp overrides={sp}, GetDuration overrides={dur}");
    }
}
