namespace NpcLabor;

/// <summary>
/// Shared <see cref="Quest"/> plumbing for every synthetic NpcLabor quest
/// (dispatch tracker, town labor tracker, town labor board offer).
///
/// All three used to declare the same two members by hand:
///   * a <c>source</c> override that falls back to the "dummy" row, because these
///     rows are display-only and carry no real SourceQuest;
///   * <c>EnsureSafePerson()</c>, because vanilla <c>Quest.chara</c> and
///     <c>QuestManager.OnShowDialog</c> dereference <c>person</c>, while a tracker
///     has no client NPC.
///
/// Keep one implementation of each here so the three quest types cannot drift.
/// </summary>
internal static class QuestTrackerSupport
{
    /// <summary>
    /// The shared display row. <paramref name="fallback"/> is the caller's
    /// <c>base.source</c>: used both when the row is absent and when the lookup
    /// throws, so a missing/damaged source table can never break quest rendering.
    /// </summary>
    internal static SourceQuest.Row DisplaySource(SourceQuest.Row fallback)
    {
        try
        {
            return EClass.sources.quests.GetRow("dummy") ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>
    /// Vanilla quest code assumes <c>person</c> is non-null. There is no retry on
    /// failure: reading the field cannot throw, so the only throwing statement was
    /// the assignment, and repeating it would fail the same way.
    /// </summary>
    internal static void EnsureSafePerson(Quest q)
    {
        try
        {
            if (q.person == null)
            {
                q.person = new Person();
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("QuestTrackerSupport.EnsureSafePerson: " + __e.Message); }
    }
}
