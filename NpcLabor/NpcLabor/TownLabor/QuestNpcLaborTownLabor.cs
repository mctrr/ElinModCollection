using System;
using System.Text;
using Newtonsoft.Json;

namespace NpcLabor.TownLabor;

/// <summary>
/// Tracker for an active town labor mission.
/// Not visible on quest board; not abandonable from journal (recall via labor UI).
/// Hard fail goes through Quest.Fail() (vanilla fame/sound); success uses custom rewards.
/// </summary>
/// <remarks>
/// Must stay public: vanilla GameIO uses TypeNameHandling.Auto. Internal types fail
/// BindToType on load and become QuestDummy, which then collides with our re-created tracker.
/// </remarks>
public sealed class QuestNpcLaborTownLabor : Quest
{
    [JsonProperty]
    public int missionId;

    public override bool TrackOnStart => true;

    public override bool CanAbandon => false;

    public override bool IsVisibleOnQuestBoard() => false;

    public override int KarmaOnFail => 0;

    public override int FameOnComplete => 0;

    public override bool CanUpdateOnTalk(Chara c) => false;

    public override bool CanAutoAdvance => false;

    public override string idSource => "dummy";

    public override SourceQuest.Row source => QuestTrackerSupport.DisplaySource(base.source);

    internal void EnsureSafePerson() => QuestTrackerSupport.EnsureSafePerson(this);

    public override void OnStart()
    {
        EnsureSafePerson();
        deadline = 0;
        try { base.OnStart(); } catch { }
    }

    public override string GetTitle()
    {
        EnsureSafePerson();
        TownLaborMission? m = Mission();
        if (m == null)
        {
            return NpcLabor.LaborTerms.TownWork;
        }

        string job = string.IsNullOrEmpty(m.jobTitle) ? NpcLabor.LaborTerms.TownWork : m.jobTitle;
        string client = string.IsNullOrEmpty(m.clientName) ? NpcLabor.LaborText.T("town.msg.client") : m.clientName;
        return NpcLabor.LaborText.T("town.ui.header", NpcLabor.LaborTerms.TownWork, job, client);
    }

    public override string GetDetail(bool hint)
    {
        EnsureSafePerson();
        return GetTrackerText();
    }

    public override string GetTextProgress()
    {
        EnsureSafePerson();
        TownLaborMission? m = Mission();
        if (m == null)
        {
            return NpcLabor.LaborText.T("dis.q.ended");
        }

        string worker = m.isPcSelf
            ? NpcLabor.LaborText.T("town.you")
            : (string.IsNullOrEmpty(m.workerName) ? ("#" + m.uidWorker) : m.workerName);
        return worker + " @ " + m.zoneName + "  " + m.ProgressLine();
    }

    public override string GetTrackerText()
    {
        EnsureSafePerson();
        TownLaborMission? m = Mission();
        if (m == null)
        {
            // Title is drawn by ItemQuestTracker.textTitle; body must not re-emit it
            // or the pin looks like two stacked trackers.
            return NpcLabor.LaborText.T("dis.q.ended");
        }

        var sb = new StringBuilder();
        sb.Append(m.isPcSelf
            ? NpcLabor.LaborText.T("town.you")
            : (string.IsNullOrEmpty(m.workerName) ? ("#" + m.uidWorker) : m.workerName));
        sb.Append(" · ").Append(m.zoneName);
        sb.Append('\n');
        sb.Append(m.ProgressLine());
        string reward = TownLaborRewards.MoneyOnlyPreviewLine(m);
        if (!string.IsNullOrEmpty(reward))
        {
            sb.Append('\n').Append(reward);
        }

        return sb.ToString();
    }

    public override void OnFail()
    {
        // Fame/sound already applied in Quest.Fail(); mission settle owns cleanup.
    }

    TownLaborMission? Mission()
    {
        if (missionId <= 0)
        {
            return null;
        }

        return TownLaborManager.FindByMissionId(missionId);
    }
}
