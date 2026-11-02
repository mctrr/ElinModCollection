using NpcLabor.CoCraft;
using NpcLabor.Craft;
using NpcLabor.Dispatch;
using NpcLabor.Process;

namespace NpcLabor.TownLabor;

/// <summary>
/// Shared busy check across A co-craft / B processor / D dispatch / E town labor / G production.
/// </summary>
internal static class LaborBusy
{
    /// <summary>One probe per slice: does this slice hold the character, and what to say about it.</summary>
    sealed class SliceProbe
    {
        internal string Name = "";
        internal System.Func<int, bool> Holds = _ => false;
        internal System.Func<string?> Reason = () => null;
    }

    /// <summary>
    /// Every slice that can hold a resident, in the order the reason is reported.
    /// Adding a slice means adding one entry — IsBusy, IsBusyElsewhere and BusyReason
    /// all read this table, so none of them can drift out of sync with the others.
    /// </summary>
    static readonly SliceProbe[] Probes =
    {
        new SliceProbe
        {
            Name = "townLabor",
            Holds = uid => TownLaborManager.IsBusy(uid),
            Reason = () => NpcLabor.LaborText.T("town.busy.town", NpcLabor.LaborTerms.TownWork),
        },
        new SliceProbe
        {
            Name = "dispatch",
            Holds = uid => DungeonDispatchManager.IsBusy(uid),
            Reason = () => NpcLabor.LaborText.T("town.busy.dungeon", NpcLabor.LaborTerms.DungeonExplore),
        },
        new SliceProbe
        {
            Name = "process",
            Holds = uid => ProcessorJobSession.IsWorkerBusy(uid),
            Reason = () => NpcLabor.LaborText.T("town.busy.process", NpcLabor.LaborTerms.Process),
        },
        new SliceProbe
        {
            Name = "coCraft",
            Holds = uid => CoCraftSession.Active && CoCraftSession.NpcUid == uid,
            Reason = () => NpcLabor.LaborText.T("town.busy.coCraft"),
        },
        new SliceProbe
        {
            Name = CraftSlice,
            Holds = uid => CraftManager.Has(uid),
            Reason = () => NpcLabor.LaborText.T("town.busy.craft"),
        },
    };

    /// <summary>
    /// Production is the one slice that asks "is this person free?" about its own
    /// candidates — see <see cref="IsBusyElsewhere"/>.
    /// </summary>
    const string CraftSlice = "craft";

    internal static bool IsBusy(int uidChara)
        => AnyBusy(uidChara, includeCraft: true);

    /// <summary>
    /// Busy check for the production slice itself. It must not count its own assignment as
    /// "busy", or a resident would be permanently blocked by the very job they were given.
    /// So this checks every other slice and skips the craft table.
    /// </summary>
    internal static bool IsBusyElsewhere(int uidChara)
        => AnyBusy(uidChara, includeCraft: false);

    static bool AnyBusy(int uidChara, bool includeCraft)
    {
        if (uidChara <= 0)
        {
            return false;
        }

        for (int i = 0; i < Probes.Length; i++)
        {
            SliceProbe p = Probes[i];
            if (!includeCraft && p.Name == CraftSlice)
            {
                continue;
            }

            try
            {
                if (p.Holds(uidChara))
                {
                    return true;
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("LaborBusy.cs silent catch: " + __e.Message); }
        }

        return false;
    }

    internal static bool IsBusy(Chara? c)
    {
        if (c == null)
        {
            return true;
        }

        try
        {
            return IsBusy(c.uid);
        }
        catch
        {
            return true;
        }
    }

    internal static string? BusyReason(Chara? c)
    {
        if (c == null)
        {
            return NpcLabor.LaborText.T("town.busy.invalid");
        }

        int uid = c.uid;
        for (int i = 0; i < Probes.Length; i++)
        {
            SliceProbe p = Probes[i];
            try
            {
                if (p.Holds(uid))
                {
                    return p.Reason();
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("LaborBusy.cs silent catch: " + __e.Message); }
        }

        return null;
    }
}
