using System;

namespace NpcLabor;

/// <summary>
/// Player-facing product names. Keep one vocabulary across UI / msg / quest titles.
/// Strings resolve through LaborText (CN default / EN or JP when the game language matches).
/// Unlock levels come from LaborConfig.
/// </summary>
internal static class LaborTerms
{
    internal static string DungeonExplore => LaborText.T("term.dungeonExplore");
    internal static string RegionDispatch => LaborText.T("term.regionDispatch");
    /// <summary>Short board button label covering dungeon + region dispatch.</summary>
    internal static string Dispatch => LaborText.T("term.dispatch");
    /// <summary>Town shop labor (slice E). Chosen over 城镇帮工.</summary>
    internal static string TownWork => LaborText.T("term.townWork");
    internal static string CoCraft => LaborText.T("term.coCraft");
    internal static string Assist => LaborText.T("term.assist");
    /// <summary>Slice G: residents craft at a workbench on the player's orders.</summary>
    internal static string BaseCraft => LaborText.T("term.baseCraft");
    /// <summary>Slice B: PC processor flow (self / auto / pin operator). Not called 外包.</summary>
    internal static string Process => LaborText.T("term.process");
    /// <summary>Deprecated alias; prefer Process. Kept so old call sites compile.</summary>
    internal static string ProcessOutsource => Process;
    /// <summary>Permanent town dispatch (常驻派遣). 2026-10-28 design rounds 7-12.</summary>
    internal static string PDispatch => LaborText.T("term.pdispatch");

    /// <summary>Home branch level required to start region dispatch.</summary>
    internal static int RegionUnlockBranchLv => LaborConfig.RegionUnlockBranchLv;
    /// <summary>Home branch level required to start dungeon explore.</summary>
    internal static int DungeonUnlockBranchLv => LaborConfig.DungeonUnlockBranchLv;
    /// <summary>Home branch level required for the town shop labor board (2026-11-02).</summary>
    internal static int TownWorkUnlockBranchLv => LaborConfig.TownLaborUnlockBranchLv;
    /// <summary>Home branch level required for base production at a workbench (2026-11-02).</summary>
    internal static int BaseCraftUnlockBranchLv => LaborConfig.CraftUnlockBranchLv;
    /// <summary>Home branch level required for permanent postings (2026-11-02).</summary>
    internal static int PDispatchUnlockBranchLv => LaborConfig.PDispatchUnlockBranchLv;

    internal static int HomeBranchLv()
    {
        try
        {
            FactionBranch? b = EClass.BranchOrHomeBranch ?? EClass.Branch;
            if (b != null)
            {
                return Math.Max(0, b.lv);
            }
        }
        catch
        {
        }

        return 0;
    }

    /// <summary>
    /// Level gate shared by every locked slice. <paramref name="term"/> is the
    /// player-facing slice name; the reason string is the same one the dispatch
    /// buttons already show. <paramref name="need"/> &lt;= 0 means "no gate".
    /// </summary>
    static bool BranchLvMet(int need, string term, out string? denyReason)
    {
        int lv = HomeBranchLv();
        if (lv >= need)
        {
            denyReason = null;
            return true;
        }

        denyReason = LaborText.T("unlock.base", term, need, lv);
        return false;
    }

    /// <summary>Gate only, no reason string — for entry points that just hide a row.</summary>
    internal static bool TownWorkUnlocked => HomeBranchLv() >= TownWorkUnlockBranchLv;
    internal static bool BaseCraftUnlocked => HomeBranchLv() >= BaseCraftUnlockBranchLv;
    internal static bool PDispatchUnlocked => HomeBranchLv() >= PDispatchUnlockBranchLv;

    internal static bool CanTownWork(out string? denyReason)
        => BranchLvMet(TownWorkUnlockBranchLv, TownWork, out denyReason);

    internal static bool CanBaseCraft(out string? denyReason)
        => BranchLvMet(BaseCraftUnlockBranchLv, BaseCraft, out denyReason);

    internal static bool CanPDispatch(out string? denyReason)
        => BranchLvMet(PDispatchUnlockBranchLv, PDispatch, out denyReason);

    internal static bool CanRegionDispatch(out string? denyReason)
        => BranchLvMet(RegionUnlockBranchLv, RegionDispatch, out denyReason);

    internal static bool CanDungeonExplore(out string? denyReason)
        => BranchLvMet(DungeonUnlockBranchLv, DungeonExplore, out denyReason);
}
