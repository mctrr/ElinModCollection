using System.Collections.Generic;
using Newtonsoft.Json;

namespace NpcLabor.PermanentDispatch;

/// <summary>
/// One active posting. Only counters persist — monthly rewards are ROLLED at
/// settlement time (final recall / death), so no Thing objects ever live in
/// json. See <see cref="PermanentDispatchDef"/>.
/// </summary>
internal sealed class PermanentDispatchMission
{
    [JsonProperty]
    public int missionId;

    [JsonProperty]
    public string defId = "";

    [JsonProperty]
    public int uidChara;

    [JsonProperty]
    public string workerName = "";

    [JsonProperty]
    public int uidZone;

    [JsonProperty]
    public string zoneId = "";

    [JsonProperty]
    public string zoneName = "";

    [JsonProperty]
    public int homeZoneUid;

    [JsonProperty]
    public bool wasPartyMember;

    /// <summary>Month index the posting started in (CurrentMonthIndex).</summary>
    [JsonProperty]
    public int startMonth;

    /// <summary>
    /// Last month index whose whole-month reward has been credited
    /// (potential applied, develop/influence applied). Items/gold for these
    /// months are paid in the end parcel.
    /// </summary>
    [JsonProperty]
    public int settledThrough;

    /// <summary>How many whole months have been settled (for the payout math).</summary>
    [JsonProperty]
    public int monthsPaid;

    [JsonProperty]
    public int guildRankAtStart;

    [JsonIgnore]
    public PermanentDispatchDef? Def => PermanentDispatchDef.GetById(defId);
}
