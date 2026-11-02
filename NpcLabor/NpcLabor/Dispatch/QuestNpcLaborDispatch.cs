using System;
using System.Text;
using Newtonsoft.Json;

namespace NpcLabor.Dispatch;

/// <summary>
/// Display-only tracked quest for an active dispatch mission (who / where / progress / loot).
/// No deadline, no fame/karma, not abandonable from journal (recall via dispatch UI).
/// Named generically — dispatch may grow beyond dungeons later.
/// </summary>
/// <remarks>
/// Must stay public: vanilla GameIO uses TypeNameHandling.Auto. Internal types fail
/// BindToType on load and become QuestDummy, which then collides with our re-created tracker.
/// </remarks>
public sealed class QuestNpcLaborDispatch : Quest
{
    [JsonProperty]
    public int missionId;

    public override bool TrackOnStart => true;

    // Journal abandon calls Quest.Fail (fame hit). Use dispatch UI recall instead.
    public override bool CanAbandon => false;

    public override bool IsVisibleOnQuestBoard() => false;

    public override int KarmaOnFail => 0;

    public override int FameOnComplete => 0;

    // Never hijack NPC talk / QuestManager.OnShowDialog.
    public override bool CanUpdateOnTalk(Chara c) => false;

    public override bool CanAutoAdvance => false;

    public override string idSource => "dummy";

    public override SourceQuest.Row source => QuestTrackerSupport.DisplaySource(base.source);

    /// <summary>
    /// Quest.chara / QuestManager.OnShowDialog assume person is non-null.
    /// Our tracker is not tied to a client NPC — always keep an empty Person.
    /// </summary>
    internal void EnsureSafePerson() => QuestTrackerSupport.EnsureSafePerson(this);

    public override void OnStart()
    {
        EnsureSafePerson();
        deadline = 0;
        try
        {
            base.OnStart();
        }
        catch (System.Exception __e) { Plugin.LogDebug("QuestNpcLaborDispatch.cs silent catch: " + __e.Message); }
    }

    public override string GetTitle()
    {
        EnsureSafePerson();
        DungeonDispatchMission? m = Mission();
        if (m == null)
        {
            return NpcLabor.LaborTerms.DungeonExplore;
        }

        string zone = ShortZone(m.zoneName);
        bool region = false;
        try { region = m.isRegion; } catch { region = false; }
        // fallback: region missions often have regionKind
        if (!region)
        {
            try { region = !string.IsNullOrEmpty(m.regionKind); } catch { }
        }

        string head = region ? NpcLabor.LaborTerms.RegionDispatch : NpcLabor.LaborTerms.DungeonExplore;
        return head + " · " + zone;
    }

    public override string GetDetail(bool hint)
    {
        EnsureSafePerson();
        return GetTrackerText();
    }

    public override string GetTextProgress()
    {
        EnsureSafePerson();
        DungeonDispatchMission? m = Mission();
        if (m == null)
        {
            return NpcLabor.LaborText.T("dis.q.ended");
        }

        string line = m.MemberNames()
            + " @ "
            + FloorLabel(m)
            + "  "
            + NpcLabor.LaborText.T("dis.q.progress", m.ProgressPercent)
            + "  "
            + NpcLabor.LaborText.T("dis.q.left", m.DaysLeft.ToString("0.0"));
        // Region outing: no live haul dump — player intuition is enough.
        if (!m.isRegion)
        {
            line += "\n" + NpcLabor.LaborText.T("dis.q.harvest", m.LootSummary(4));
        }

        return line;
    }

    public override string GetTrackerText()
    {
        EnsureSafePerson();
        DungeonDispatchMission? m = Mission();
        if (m == null)
        {
            // Title is drawn by ItemQuestTracker.textTitle; body must not re-emit it
            // or the pin looks like two stacked trackers.
            return NpcLabor.LaborText.T("dis.q.ended");
        }

        var sb = new StringBuilder();
        sb.Append(NpcLabor.LaborText.T("dis.q.members", m.MemberNames()));
        sb.Append('\n');
        sb.Append(NpcLabor.LaborText.T("dis.q.position", FloorLabel(m)));
        sb.Append("  ").Append(NpcLabor.LaborText.T("dis.q.progress", m.ProgressPercent));
        sb.Append("  ").Append(NpcLabor.LaborText.T("dis.q.left", m.DaysLeft.ToString("0.0")));
        // Region: hide harvest / plan lines so tracker cannot inflate fake loot.
        if (!m.isRegion)
        {
            sb.Append('\n');
            sb.Append(NpcLabor.LaborText.T("dis.q.harvest", m.LootSummary(6)));
        }

        return sb.ToString();
    }

    public override void OnFail()
    {
        // Should not run (CanAbandon false + deadline 0). Never recall here.
    }

    DungeonDispatchMission? Mission()
    {
        if (missionId <= 0)
        {
            return null;
        }

        return DungeonDispatchManager.FindByMissionId(missionId);
    }

    static string FloorLabel(DungeonDispatchMission m)
    {
        if (m.isRegion)
        {
            string name = !string.IsNullOrEmpty(m.regionKind)
                ? DungeonDispatchTargets.RegionDisplayName(m.regionKind)
                : (string.IsNullOrEmpty(m.zoneName) ? NpcLabor.LaborText.T("dis.q.area") : m.zoneName);
            int weeks = m.exploreWeeks > 0 ? m.exploreWeeks : 1;
            return NpcLabor.LaborText.T("dis.q.regionPos", name, weeks);
        }

        return DungeonDispatchManager.FormatFloorLabel(m.currentFloorLv);
    }

    static string ShortZone(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return NpcLabor.LaborText.T("dis.q.target");
        }

        // Strip trailing danger annotations if any slipped in.
        string s = name!;
        int idx = s.IndexOf("Lv", StringComparison.OrdinalIgnoreCase);
        if (idx > 0)
        {
            s = s.Substring(0, idx).Trim();
        }

        idx = s.IndexOf('(');
        if (idx > 0)
        {
            s = s.Substring(0, idx).Trim();
        }

        if (s.Length > 14)
        {
            s = s.Substring(0, 14) + "…";
        }

        return s;
    }
}
