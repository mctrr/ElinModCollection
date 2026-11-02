namespace NpcLabor;

/// <summary>
/// The pool rules shared by the town labor board and the dispatch board.
///
/// Both boards used to carry their own byte-identical copy of these two checks
/// (<c>TownLaborManager.Roster.cs</c> and <c>DungeonDispatchManager.Roster.cs</c>).
/// They ask the same question, so the answer lives here once.
///
/// What deliberately stays in each slice: which pool is enumerated, the sort key
/// (town skill / dispatch combat power / assistant skill) and which busy table is
/// consulted. Those genuinely differ and are not shared.
///
/// Not to be confused with <c>AssistantResolver.IsPartyMember</c>, which is a
/// different test: it returns false when <c>EClass.pc.party</c> is null even for a
/// character with <c>IsPCParty</c>. Do not merge the two — co-craft eligibility
/// and board eligibility are separate rules.
/// </summary>
internal static class LaborRoster
{
    /// <summary>Party member test. Vanilla lookups are wrapped: failure means "not party".</summary>
    internal static bool IsPartyMember(Chara c)
    {
        try
        {
            return c != null && (c.IsPCParty || (EClass.pc?.party?.members?.Contains(c) ?? false));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Who a labor board may offer a job to: party members, members of the
    /// current/home branch, and faction members that still have a home branch.
    /// </summary>
    internal static bool IsInPool(Chara c)
    {
        if (IsPartyMember(c))
        {
            return true;
        }

        FactionBranch? branch = EClass.BranchOrHomeBranch ?? EClass.Branch;
        if (branch?.members != null && branch.members.Contains(c))
        {
            return true;
        }

        return c.IsPCFaction && c.homeBranch != null;
    }
}
