using System;
using System.Text;

namespace NpcLabor.PermanentDispatch;

/// <summary>
/// Display row for a permanent posting, used in two places:
/// - the quest board next to the town labor rows (offer / active);
/// - the PC quest journal (active only), where the vanilla layout shows the
///   remaining time in the 剩余 column and offers its own 放弃 button.
///
/// Rows are rebuilt on every refresh and are never added to
/// <c>EClass.game.quests.list</c>, so nothing of ours is serialized into the
/// save (a custom Quest type in the save would not resolve if the mod were
/// removed).
/// </summary>
internal sealed class QuestNpcLaborPDispatch : Quest
{
    public string defId = "";

    /// <summary>Posting is already running: row offers recall instead of start.</summary>
    public bool active;

    // Rows are appended by hand, so vanilla must not collect them again.
    public override bool IsVisibleOnQuestBoard() => false;

    public override bool TrackOnStart => false;

    public override bool CanAbandon => active;

    public override bool IsRandomQuest => false;

    public override bool CanAutoAdvance => false;

    public override bool CanStartQuest() => false;

    public override bool CanUpdateOnTalk(Chara c) => false;

    public override int KarmaOnFail => 0;

    public override int FameOnComplete => 0;

    public override string idSource => "dummy";

    public override SourceQuest.Row source => QuestTrackerSupport.DisplaySource(base.source);

    public override string TitlePrefix => active
        ? NpcLabor.LaborText.T("pdisp.ui.inProgress").TagColor(FontColor.Good)
        : string.Empty;

    internal PermanentDispatchDef? Def => PermanentDispatchDef.GetById(defId);

    internal PermanentDispatchMission? Mission => PermanentDispatchManager.FindByDef(defId);

    /// <summary>
    /// Single row object per task, no <c>quests.uid</c> bump (that counter is for
    /// quests the game actually owns). Vanilla Quest.Equals is uid-only, so a
    /// distinct uid per def keeps 招募/进行中 rows from collapsing into each other.
    /// </summary>
    internal void AssignUid()
    {
        unchecked
        {
            int h = defId.GetHashCode() * 397;
            h ^= active ? 0x5f3a : 0x1b7c;
            uid = h == 0 ? 1 : h;
        }
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj is QuestNpcLaborPDispatch other)
        {
            return uid == other.uid;
        }

        return false;
    }

    public override int GetHashCode() => uid;

    public override string GetTitle()
    {
        QuestTrackerSupport.EnsureSafePerson(this);
        PermanentDispatchDef? def = Def;
        string name = def?.Name ?? defId;
        if (!active)
        {
            return name;
        }

        string? worker = Mission?.workerName;
        return string.IsNullOrEmpty(worker) ? name : name + " · " + worker;
    }

    public override string GetDetail(bool onJournal = false)
    {
        QuestTrackerSupport.EnsureSafePerson(this);
        PermanentDispatchDef? def = Def;
        if (def == null)
        {
            return "";
        }

        var sb = new StringBuilder();

        // Commission text first, in the voice of whoever hands the post out;
        // then the hard numbers (gate + what you get back).
        string desc = def.Description;
        if (desc.Length > 0)
        {
            sb.Append(desc).Append("\n\n");
        }

        sb.Append(def.GateText());
        string reward = def.RewardText();
        if (reward.Length > 0)
        {
            sb.Append("\n").Append(reward);
        }

        if (active)
        {
            PermanentDispatchMission? m = Mission;
            if (m != null)
            {
                sb.Append("\n").Append(NpcLabor.LaborText.T("pdisp.ui.served", m.monthsPaid));
                sb.Append("\n").Append(NpcLabor.LaborText.T("pdisp.ui.nextSettle",
                    Date.GetText(PermanentDispatchManager.HoursToNextMonth())));
            }
        }

        return sb.ToString();
    }

    public override void OnClickQuest()
    {
        try
        {
            PermanentDispatchUi.OpenFromDef(defId);
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("pdispatch row click: " + ex.Message);
            try { SE.Beep(); } catch { }
        }
    }

    // Cancelling from the journal recalls and settles; Quest.Fail is intercepted
    // by a prefix so vanilla never applies its fame penalty for this type.
    public override void OnFail()
    {
        PermanentDispatchManager.Cancel(defId);
    }
}
