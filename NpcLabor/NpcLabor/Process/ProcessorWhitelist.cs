using System;

namespace NpcLabor.Process;

/// <summary>
/// Slice B: non-factory drag-grid processors (one conversion via TraitCrafter.Craft).
/// Factories (LayerCraft recipe list) and special UI machines stay out.
/// </summary>
internal static class ProcessorWhitelist
{
    static readonly System.Collections.Generic.HashSet<string> WarnedFallbackTypes
        = new System.Collections.Generic.HashSet<string>();

    internal static bool IsSupported(TraitCrafter? crafter)
    {
        if (crafter == null || crafter.IsFactory)
        {
            return false;
        }

        // Special / non-conversion drag machines.
        if (crafter is TraitGrindstone
            or TraitBarrelMaker
            or TraitRuneMold
            or TraitScratchMachine
            or TraitIncubator
            or TraitRollingFortune
            or TraitToolTalisman)
        {
            return false;
        }

        // Explicit conversion processors + any other plain TraitCrafter with an IdSource.
        if (crafter is TraitSawMill
            or TraitMill
            or TraitWoodMill
            or TraitStoneCutter
            or TraitSpinner
            or TraitGemCutter
            or TraitKiln
            or TraitSmelter
            or TraitButcher
            or TraitDyeMaker
            or TraitSculpture
            or TraitRationMaker)
        {
            return true;
        }

        // Fallback: non-factory crafter that participates in recipe-null Craft path.
        try
        {
            if (!crafter.IdSource.IsEmpty())
            {
                // Unlisted machine reached via fallback: log once per type so future
                // balance/support reviews can decide whether it belongs on the list.
                string typeName = crafter.GetType().Name;
                if (WarnedFallbackTypes.Add(typeName))
                {
                    Plugin.LogWarn(
                        "processor whitelist fallback matched unlisted crafter " + typeName
                        + " (IdSource=" + crafter.IdSource + "); treating it as a processor");
                }

                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    internal static string DisplayName(TraitCrafter crafter)
    {
        try
        {
            string title = crafter.CrafterTitle;
            if (!title.IsEmpty())
            {
                string named = title.lang();
                if (!named.IsEmpty())
                {
                    return named;
                }
            }
        }
        catch
        {
        }

        try
        {
            if (crafter.owner != null && !crafter.owner.Name.IsEmpty())
            {
                return crafter.owner.Name;
            }
        }
        catch
        {
        }

        return crafter.GetType().Name.Replace("Trait", "");
    }

    internal static int ResolveSkillId(TraitCrafter crafter)
    {
        try
        {
            string alias = crafter.IDReqEle(null);
            if (!alias.IsEmpty())
            {
                return EClass.sources.elements.alias[alias].id;
            }
        }
        catch
        {
        }

        return 0;
    }
}
