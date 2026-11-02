using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace NpcLabor.Dispatch;

/// <summary>Save-safe planned region stack (id + qty + optional mat alias).</summary>
internal sealed class PlannedLootEntry
{
    [JsonProperty]
    public string id = "";

    [JsonProperty]
    public int num = 1;

    [JsonProperty]
    public string matAlias = "";

    /// <summary>Stable material id when alias localization drifts (e.g. sea sand=97).</summary>
    [JsonProperty]
    public int matId = -1;

    [JsonProperty]
    public string name = "";

    /// <summary>
    /// Optional card ref for named meats/corpses (c_idRefCard). Required so
    /// recreate keeps chicken/cow/sheep/putty instead of anonymous meat.
    /// </summary>
    [JsonProperty]
    public string refCard = "";
}

/// <summary>One dungeon dispatch mission: up to 4 members explore the same zone together.</summary>
internal sealed class DungeonDispatchMission
{
    public const int SkillExplore = 210;
    public const int SkillLockpick = 280;
    public const int SkillGather = 250; // gathering
    public const int SkillMining = 220;
    public const int SkillLumber = 225;
    public const int SkillDigging = 230;
    public const int SkillFishing = 245;

    // Combat skills (SKILL.* ids from Elin).
    public const int SkillMartial = 100;
    public const int SkillWeaponSword = 101;
    public const int SkillWeaponAxe = 102;
    public const int SkillWeaponStaff = 103;
    public const int SkillWeaponBow = 104;
    public const int SkillWeaponGun = 105;
    public const int SkillWeaponPolearm = 106;
    public const int SkillWeaponDagger = 107;
    public const int SkillThrowing = 108;
    public const int SkillWeaponCrossbow = 109;
    public const int SkillWeaponScythe = 110;
    public const int SkillWeaponBlunt = 111;
    public const int SkillTactics = 132;
    public const int SkillMarksman = 133;
    public const int SkillCasting = 304;

    public const int MaxMembersPerDungeon = 4;

    [JsonProperty]
    public int missionId;

    [JsonProperty]
    public int uidZone;

    [JsonProperty]
    public string zoneId = "";

    [JsonProperty]
    public string zoneName = "";

    [JsonProperty]
    public int dangerLv = 1;

    [JsonProperty]
    public bool isRandomSite;

    /// <summary>Virtual region outing (plain/forest/beach/mountain), not a real zone.</summary>
    [JsonProperty]
    public bool isRegion;

    [JsonProperty]
    public string regionKind = "";

    [JsonProperty]
    public int regionGx;

    [JsonProperty]
    public int regionGy;

    /// <summary>Region explore duration in weeks (1-4). Dungeons ignore this.</summary>
    [JsonProperty]
    public int exploreWeeks = 1;

    [JsonProperty]
    public int hoursTotal = 24;

    [JsonProperty]
    public int hoursLeft = 24;

    [JsonProperty]
    public int combatPower = 1;

    [JsonProperty]
    public int exploreSkill;

    [JsonProperty]
    public int lockpickSkill;

    [JsonProperty]
    public int gatherSkill;

    [JsonProperty]
    public int successChance = 50;

    [JsonProperty]
    public int distDays = 1;

    /// <summary>
    /// Outbound travel hours locked at start. No harvest until spent hours exceed this.
    /// </summary>
    [JsonProperty]
    public int travelHours;

    [JsonProperty]
    public int dangerDays = 1;

    [JsonProperty]
    public int homeZoneUid;

    [JsonProperty]
    public List<int> uidMembers = new List<int>();

    /// <summary>Legacy single-member field; migrated into uidMembers on load.</summary>
    [JsonProperty]
    public int uidChara;

    [JsonProperty]
    public int currentFloorLv;

    [JsonProperty]
    public int deepestFloorLv;

    [JsonProperty]
    public List<string> lootLog = new List<string>();

    [JsonProperty]
    public int partialLootTicks;

    /// <summary>
    /// Save-safe planned region haul. Built once so tracker == final mail across reloads.
    /// </summary>
    [JsonProperty]
    public List<PlannedLootEntry> plannedLootEntries = new List<PlannedLootEntry>();

    /// <summary>
    /// Bumped when region loot composition rules change so old locked plans rebuild.
    /// v10: sea sand alias sand_sea, mandatory sulfur, even forest/plain staples.
    /// </summary>
    [JsonProperty]
    public int planGenVersion;

    /// <summary>
    /// Locked at mission start: multiplies base haul by 0.8..1.2.
    /// 0 means not rolled yet (old saves / first EnsureRegionPlan).
    /// </summary>
    [JsonProperty]
    public float haulVariance;

    /// <summary>
    /// Locked total item count for the full completion plan.
    /// explore materials + gather food/nodes + lockpick currency stacks.
    /// </summary>
    [JsonProperty]
    public int plannedHaulTotal;

    [JsonIgnore]
    public bool plannedLootReady;

    [JsonIgnore]
    public int HoursSpent
    {
        get
        {
            if (hoursTotal <= 0)
            {
                return 0;
            }

            int spent = hoursTotal - hoursLeft;
            return spent < 0 ? 0 : spent;
        }
    }

    /// <summary>Overall mission clock including travel (UI time bar).</summary>
    [JsonIgnore]
    public int ProgressPercent
    {
        get
        {
            if (hoursTotal <= 0)
            {
                return 100;
            }

            int pct = HoursSpent * 100 / hoursTotal;
            if (pct < 0)
            {
                return 0;
            }

            return pct > 100 ? 100 : pct;
        }
    }

    /// <summary>
    /// Effective travel hours used by the harvest gate (save field + legacy fallback).
    /// </summary>
    [JsonIgnore]
    public int EffectiveTravelHours
    {
        get
        {
            int travel = travelHours;
            if (travel < 0)
            {
                travel = 0;
            }

            // Legacy saves / missing field: at least half of DistDays (UI ??).
            if (travel <= 0 && distDays > 0)
            {
                travel = System.Math.Max(6, (int)System.Math.Ceiling(System.Math.Max(1, distDays) * 0.5 * 24.0));
            }

            // Never shorter than displayed ?? days.
            if (distDays > 0)
            {
                int minTravel = System.Math.Max(6, (int)System.Math.Ceiling(System.Math.Max(1, distDays) * 0.5 * 24.0));
                if (travel < minTravel)
                {
                    travel = minTravel;
                }
            }

            if (hoursTotal > 0 && travel >= hoursTotal)
            {
                // Keep a non-zero explore window so natural complete can still pay.
                travel = System.Math.Max(0, hoursTotal - System.Math.Max(1, hoursTotal / 5));
            }

            return travel < 0 ? 0 : travel;
        }
    }

    /// <summary>
    /// Harvest clock: 0 until travel finishes, then only explore portion counts.
    /// Task details / mail must both use this (not raw ProgressPercent).
    /// </summary>
    [JsonIgnore]
    public int HarvestProgressPercent
    {
        get
        {
            if (hoursTotal <= 0)
            {
                return 100;
            }

            int travel = EffectiveTravelHours;
            int spent = HoursSpent;
            // Must be strictly greater than travel hours before any loot.
            if (spent <= travel)
            {
                return 0;
            }

            int exploreTotal = hoursTotal - travel;
            if (exploreTotal <= 0)
            {
                return 100;
            }

            int exploreSpent = spent - travel;
            int pct = exploreSpent * 100 / exploreTotal;
            if (pct < 0)
            {
                return 0;
            }

            return pct > 100 ? 100 : pct;
        }
    }

    [JsonIgnore]
    public float DaysLeft
    {
        get
        {
            if (hoursLeft <= 0)
            {
                return 0f;
            }

            return hoursLeft / 24f;
        }
    }

    [JsonIgnore]
    public int MemberCount => uidMembers?.Count ?? 0;

    public void EnsureMemberList()
    {
        if (uidMembers == null)
        {
            uidMembers = new List<int>();
        }

        if (uidMembers.Count == 0 && uidChara > 0)
        {
            uidMembers.Add(uidChara);
        }

        if (lootLog == null)
        {
            lootLog = new List<string>();
        }

        if (plannedLootEntries == null)
        {
            plannedLootEntries = new List<PlannedLootEntry>();
        }
    }

    public bool HasMember(int uid)
    {
        EnsureMemberList();
        for (int i = 0; i < uidMembers.Count; i++)
        {
            if (uidMembers[i] == uid)
            {
                return true;
            }
        }

        return false;
    }

    public List<Chara> GetMembers()
    {
        EnsureMemberList();
        var list = new List<Chara>();
        for (int i = 0; i < uidMembers.Count; i++)
        {
            try
            {
                Chara? c = RefChara.Get(uidMembers[i]);
                if (c != null)
                {
                    list.Add(c);
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchMission.cs silent catch: " + __e.Message); }
}

        return list;
    }

    public string MemberNames()
    {
        var members = GetMembers();
        if (members.Count == 0)
        {
            return "#" + (uidMembers.Count > 0 ? uidMembers[0].ToString() : "?");
        }

        var names = new List<string>();
        foreach (Chara c in members)
        {
            try
            {
                names.Add(c.NameSimple ?? c.Name ?? ("#" + c.uid));
            }
            catch
            {
                names.Add("#" + c.uid);
            }
        }

        return string.Join("、", names);
    }

    public Zone? GetZone()
    {
        try
        {
            if (uidZone > 0)
            {
                Zone? z = EClass.game?.spatials?.Find(uidZone);
                if (z != null)
                {
                    return z;
                }
            }

            if (!string.IsNullOrEmpty(zoneId))
            {
                return EClass.game?.spatials?.Find(zoneId);
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchMission.cs silent catch: " + __e.Message); }
return null;
    }

    public Zone? GetHomeZone()
    {
        try
        {
            if (homeZoneUid > 0)
            {
                return EClass.game?.spatials?.Find(homeZoneUid);
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchMission.cs silent catch: " + __e.Message); }
        try
        {
            foreach (Chara c in GetMembers())
            {
                if (c?.homeZone != null)
                {
                    return c.homeZone;
                }
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchMission.cs silent catch: " + __e.Message); }
        try
        {
            return EClass.pc?.homeZone ?? EClass.BranchOrHomeBranch?.owner ?? EClass.Branch?.owner;
        }
        catch
        {
            return null;
        }
    }

    public string LootSummary(int max = 6)
    {
        EnsureMemberList();
        if (lootLog == null || lootLog.Count == 0)
        {
            return NpcLabor.LaborText.T("dis.loot.none");
        }

        // Aggregate by base name and SUM quantities:
        // "Namex10" + "Namex9" => "Namex19" (not entry-count x2).
        var order = new List<string>();
        var counts = new Dictionary<string, int>();
        for (int i = 0; i < lootLog.Count; i++)
        {
            string raw = lootLog[i] ?? "";
            string key = raw.Trim();
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            // Drop tag prefixes like "[mail] ".
            int bracket = key.LastIndexOf(']');
            if (key.StartsWith("[") && bracket > 0 && bracket + 1 < key.Length)
            {
                key = key.Substring(bracket + 1).Trim();
            }

            int qty = 1;
            int xAt = key.LastIndexOf('x');
            int multAt = key.LastIndexOf('\u00d7');
            int cut = System.Math.Max(xAt, multAt);
            if (cut > 0 && cut < key.Length - 1)
            {
                string tail = key.Substring(cut + 1).Trim();
                int n;
                if (int.TryParse(tail, out n) && n > 0)
                {
                    qty = n;
                    key = key.Substring(0, cut).Trim();
                }
            }
            else
            {
                // "10\u4e2aName"
                int ge = key.IndexOf('\u4e2a');
                if (ge > 0)
                {
                    string head = key.Substring(0, ge).Trim();
                    int n;
                    if (int.TryParse(head, out n) && n > 0 && ge + 1 < key.Length)
                    {
                        qty = n;
                        key = key.Substring(ge + 1).Trim();
                    }
                }
            }

            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            if (!counts.ContainsKey(key))
            {
                counts[key] = 0;
                order.Add(key);
            }

            counts[key] += System.Math.Max(1, qty);
        }

        if (order.Count == 0)
        {
            return NpcLabor.LaborText.T("dis.loot.none");
        }

        int show = order.Count < max ? order.Count : max;
        int startIdx = order.Count - show;
        var parts = new List<string>();
        for (int i = startIdx; i < order.Count; i++)
        {
            string k = order[i];
            int c = counts[k];
            parts.Add(c > 1 ? (k + "x" + c) : k);
        }

        string s = string.Join("\u3001", parts);
        if (order.Count > max)
        {
            int totalUnits = 0;
            foreach (var kv in counts)
            {
                totalUnits += kv.Value;
            }

            s = NpcLabor.LaborText.T("dis.loot.etcItems", s, totalUnits);
        }

        return s;
    }

}
