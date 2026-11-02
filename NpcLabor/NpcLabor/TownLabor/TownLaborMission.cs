using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace NpcLabor.TownLabor;

internal enum TownLaborSettleKind
{
    Success = 0,
    Recall = 1,
    Failed = 2,
}

/// <summary>One active town shop labor assignment (1 worker, 1 client).</summary>
internal sealed class TownLaborMission
{
    [JsonProperty]
    public int missionId;

    [JsonProperty]
    public string jobId = "";

    [JsonProperty]
    public int jobKind;

    [JsonProperty]
    public int uidWorker;

    [JsonProperty]
    public int uidClient;

    [JsonProperty]
    public int uidZone;

    [JsonProperty]
    public string zoneId = "";

    [JsonProperty]
    public string zoneName = "";

    [JsonProperty]
    public string clientName = "";

    [JsonProperty]
    public string workerName = "";

    [JsonProperty]
    public string jobTitle = "";

    [JsonProperty]
    public int skillId;

    [JsonProperty]
    public int hoursTotal = 18;

    [JsonProperty]
    public int hoursLeft = 18;

    [JsonProperty]
    public bool wasPartyMember;

    /// <summary>PC self-work (plan A). No party hop; drains SP hourly after arrive.</summary>
    [JsonProperty]
    public bool isPcSelf;

    /// <summary>
    /// PC self only: false while walking to client (hours do not tick);
    /// true after arrive so hour/SP work begins. Companion labor always true.
    /// </summary>
    [JsonProperty]
    public bool workStarted = true;

    [JsonProperty]
    public int homeZoneUid;

    /// <summary>Shop level snapshot at accept (pay/invest gate).</summary>
    [JsonProperty]
    public int shopLv = 1;

    /// <summary>Worker job-skill snapshot at accept.</summary>
    [JsonProperty]
    public int workerSkill;

    /// <summary>Client c_invest snapshot at accept (plat curve).</summary>
    [JsonProperty]
    public int clientInvest;

    /// <summary>Locked expected cash at accept (task description == payout).</summary>
    [JsonProperty]
    public int rewardMoney;

    /// <summary>Locked platinum amount at accept.</summary>
    [JsonProperty]
    public int rewardPlat;

    /// <summary>Locked furniture-ticket roll at accept.</summary>
    [JsonProperty]
    public bool rewardTicketGranted;

    /// <summary>Reward pins are set (old saves are migrated on load).</summary>
    [JsonProperty]
    public bool rewardPreviewLocked;

    [JsonProperty]
    public List<string> rewardLog = new List<string>();

    [JsonIgnore]
    public TownLaborJobKind Kind
    {
        get => (TownLaborJobKind)jobKind;
        set => jobKind = (int)value;
    }

    [JsonIgnore]
    public int ProgressPercent
    {
        get
        {
            if (hoursTotal <= 0)
            {
                return 100;
            }

            int done = hoursTotal - Math.Max(0, hoursLeft);
            if (done < 0)
            {
                done = 0;
            }

            return Math.Max(0, Math.Min(100, done * 100 / hoursTotal));
        }
    }

    internal TownLaborJobDef? Def => TownLaborJobs.GetById(jobId) ?? TownLaborJobs.Get(Kind);

    internal Chara? GetWorker()
    {
        if (uidWorker <= 0)
        {
            return null;
        }

        return TownLaborManager.ResolveChara(uidWorker);
    }

    internal Chara? GetClient()
    {
        if (uidClient <= 0)
        {
            return null;
        }

        return TownLaborManager.ResolveChara(uidClient);
    }

    internal Zone? GetZone()
    {
        if (uidZone <= 0)
        {
            return null;
        }

        try
        {
            return EClass.game?.spatials?.Find(uidZone);
        }
        catch
        {
            return null;
        }
    }

    internal Zone? GetHomeZone()
    {
        if (homeZoneUid > 0)
        {
            try
            {
                Zone? z = EClass.game?.spatials?.Find(homeZoneUid);
                if (z != null)
                {
                    return z;
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("TownLaborMission.cs silent catch: " + __e.Message); }
}

        try
        {
            return EClass.pc?.homeZone
                ?? EClass.BranchOrHomeBranch?.owner
                ?? EClass.Branch?.owner;
        }
        catch
        {
            return null;
        }
    }

    internal string ProgressLine()
    {
        if (isPcSelf && !workStarted)
        {
            return NpcLabor.LaborText.T("town.progress.arriving");
        }

        int h = Math.Max(0, hoursLeft);
        return NpcLabor.LaborText.T("town.progress.line", ProgressPercent, h);
    }
}

internal sealed class TownLaborSaveData
{
    [JsonProperty]
    public List<TownLaborMission> missions = new List<TownLaborMission>();

    /// <summary>
    /// World date.GetRaw() at last mod save. Gap vs current raw => reinstall auto-recall.
    /// </summary>
    [JsonProperty]
    public int lastSeenWorldRaw;

    /// <summary>
    /// Per-shop free-invest quota: "zoneUid:clientUid" -> week index of the last
    /// free shop invest granted (one per shop per calendar week).
    /// </summary>
    [JsonProperty]
    public Dictionary<string, int> shopInvestWeeks = new Dictionary<string, int>();
}
