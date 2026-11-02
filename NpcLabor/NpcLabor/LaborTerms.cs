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

    internal static bool CanRegionDispatch(out string? denyReason)
    {
        int lv = HomeBranchLv();
        int need = RegionUnlockBranchLv;
        if (lv >= need)
        {
            denyReason = null;
            return true;
        }

        denyReason = LaborText.T("unlock.region", RegionDispatch, need, lv);
        return false;
    }

    internal static bool CanDungeonExplore(out string? denyReason)
    {
        int lv = HomeBranchLv();
        int need = DungeonUnlockBranchLv;
        if (lv >= need)
        {
            denyReason = null;
            return true;
        }

        denyReason = LaborText.T("unlock.dungeon", DungeonExplore, need, lv);
        return false;
    }
}
