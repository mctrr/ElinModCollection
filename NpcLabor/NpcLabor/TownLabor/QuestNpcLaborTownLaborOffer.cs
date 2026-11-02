using System;
using System.Text;

namespace NpcLabor.TownLabor;

/// <summary>
/// Board-only synthetic quest row for a town shop labor offer.
/// Never started into QuestManager; click opens TownLaborUi (not vanilla drama).
/// </summary>
internal sealed class QuestNpcLaborTownLaborOffer : Quest
{
    public int clientUid;
    public string jobId = "";
    public bool isActiveLabor;
    public int laborHours;

    public override bool TrackOnStart => false;

    public override bool CanAbandon => false;

    public override bool IsVisibleOnQuestBoard() => false;

    public override bool IsRandomQuest => true;

    public override bool CanUpdateOnTalk(Chara c) => false;

    public override bool CanAutoAdvance => false;

    public override bool CanStartQuest() => false;

    public override int KarmaOnFail => 0;

    public override int FameOnComplete => 0;

    public override string idSource => "dummy";

    // Free rows show shop-identity titles only ("鱼店需要人手"); no "店铺帮工·" prefix.
    // Active rows keep a short busy tag so the board still marks in-progress work.
    public override string TitlePrefix => isActiveLabor
        ? NpcLabor.LaborText.T("town.ui.inProgress").TagColor(FontColor.Good)
        : string.Empty;

    public override SourceQuest.Row source => QuestTrackerSupport.DisplaySource(base.source);

    internal void EnsureSafePerson() => QuestTrackerSupport.EnsureSafePerson(this);

    internal TownLaborJobDef? Def => TownLaborJobs.GetById(jobId);

    /// <summary>
    /// Local shopkeepers are often map-only; RefChara.Get only sees globals.
    /// Prefer person cache, then map scan, then global.
    /// </summary>
    internal Chara? Client
    {
        get
        {
            if (clientUid <= 0)
            {
                return null;
            }

            return TownLaborManager.ResolveChara(clientUid, person);
        }
    }

    internal TownLaborMission? ActiveMission()
    {
        if (clientUid <= 0)
        {
            return null;
        }

        return TownLaborManager.FindByClient(clientUid);
    }

    /// <summary>
    /// Vanilla Quest.Equals is uid-only. Board offers never go through Init(),
    /// so without unique uids every row collapses to uid 0 and UIList/Contains
    /// treat them as the same quest (wrong title/state reuse).
    /// </summary>
    public override bool Equals(object obj)
    {
        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj is QuestNpcLaborTownLaborOffer other)
        {
            if (uid != 0 && other.uid != 0)
            {
                return uid == other.uid;
            }

            return clientUid == other.clientUid
                && string.Equals(jobId, other.jobId, StringComparison.Ordinal);
        }

        return base.Equals(obj);
    }

    public override int GetHashCode()
    {
        if (uid != 0)
        {
            return uid;
        }

        unchecked
        {
            int h = clientUid * 397;
            h ^= (jobId ?? string.Empty).GetHashCode();
            return h;
        }
    }

    public override string GetTitle()
    {
        EnsureSafePerson();
        TownLaborJobDef? def = Def;
        string job;
        TownLaborMission? m = isActiveLabor ? ActiveMission() : null;
        if (m != null && !string.IsNullOrEmpty(m.jobTitle))
        {
            job = m.jobTitle;
        }
        else
        {
            job = TownLaborJobs.ResolveBoardTitle(Client, def);
            if (string.IsNullOrEmpty(job))
            {
                job = def?.Title ?? (string.IsNullOrEmpty(jobId) ? NpcLabor.LaborTerms.TownWork : jobId);
            }
        }

        if (isActiveLabor && m != null)
        {
            if (m.isPcSelf)
            {
                return job + " " + NpcLabor.LaborText.T("town.ui.youTag");
            }

            if (!string.IsNullOrEmpty(m.workerName))
            {
                return job + " · " + m.workerName;
            }
        }

        return job;
    }

    public override string GetDetail(bool onJournal = false)
    {
        EnsureSafePerson();
        TownLaborJobDef? def = Def;
        var sb = new StringBuilder();

        // Short board reason only; multi-variant per job, stable per offer.
        int seed = unchecked(clientUid * 397 ^ (jobId ?? string.Empty).GetHashCode() ^ laborHours * 17);
        string reason = def != null
            ? def.PickDetail(seed)
            : NpcLabor.LaborText.T("town.ui.needHelp");
        sb.Append(reason);

        if (isActiveLabor)
        {
            TownLaborMission? m = ActiveMission();
            if (m != null)
            {
                sb.Append("\n").Append(m.workerName)
                    .Append("  ").Append(m.ProgressLine());
            }
            else
            {
                sb.Append("\n").Append(NpcLabor.LaborText.T("town.ui.inProgress"));
            }
        }

        // Hours and pay are not repeated here: the entry already shows the
        // remaining hours top-right and the exact pay in the vanilla 报酬 column
        // (see BuildOfferQuest -> rewardMoney). One body sentence is enough.
        return sb.ToString();
    }

    public override string GetTextProgress()
    {
        EnsureSafePerson();
        if (!isActiveLabor)
        {
            return "";
        }

        TownLaborMission? m = ActiveMission();
        return m != null ? m.ProgressLine() : NpcLabor.LaborText.T("town.ui.inProgress");
    }

    public override void OnClickQuest()
    {
        EnsureSafePerson();
        try
        {
            TownLaborUi.OpenFromOffer(this);
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("townlabor offer click: " + ex.Message);
            try { SE.Beep(); } catch { }
        }
    }

    public override void OnFail()
    {
        // Board-only row; never fail as a real quest.
    }
}
