using System;
using System.Collections.Generic;
using UnityEngine;

namespace NpcLabor.Dispatch;

internal sealed class DungeonDispatchTarget
{
    public Zone? Zone;
    public int DistDays;
    public int DangerLv;
    public bool IsRandomSite;
    public string Name = "";
    public string Compass = "";

    /// <summary>False for virtual region dispatches (plain/forest/beach/mountain).</summary>
    public bool IsRegion;
    public string RegionKind = ""; // plain / forest / beach / mountain
    public int RegionGx;
    public int RegionGy;

    public bool IsDungeon => !IsRegion && Zone != null;
}

internal static class DungeonDispatchTargets
{
    internal static int GetRadius(FactionBranch? branch)
    {
        int lv = 1;
        try
        {
            if (branch != null)
            {
                lv = branch.lv;
            }
        }
        catch
        {
        }

        return Mathf.Clamp(5 + lv, 5, 10);
    }

    internal static int ZoneDistDays(Zone home, Zone target)
    {
        try
        {
            int d = home.Dist(target);
            if (d <= 0)
            {
                int dx = Math.Abs(home.x - target.x);
                int dy = Math.Abs(home.y - target.y);
                d = Math.Max(dx, dy);
            }

            return Math.Max(1, d);
        }
        catch
        {
            try
            {
                int dx = Math.Abs(home.x - target.x);
                int dy = Math.Abs(home.y - target.y);
                return Math.Max(1, Math.Max(dx, dy));
            }
            catch
            {
                return 1;
            }
        }
    }

    internal static string CompassFrom(Zone home, Zone target)
    {
        try
        {
            int dx = target.x - home.x;
            int dy = target.y - home.y;
            if (Math.Abs(dx) >= Math.Abs(dy))
            {
                if (dx > 0)
                {
                    return NpcLabor.LaborText.T("dis.compass.e");
                }

                if (dx < 0)
                {
                    return NpcLabor.LaborText.T("dis.compass.w");
                }
            }

            if (dy > 0)
            {
                return NpcLabor.LaborText.T("dis.compass.s");
            }

            if (dy < 0)
            {
                return NpcLabor.LaborText.T("dis.compass.n");
            }
        }
        catch
        {
        }

        return NpcLabor.LaborText.T("dis.compass.near");
    }

    /// <summary>
    /// Combat power for duration/success.
    /// survival = (DV + PV) / 10
    /// offense  = max(melee, ranged, casting)
    ///   melee  = main-hand weapon skill + tactics (no weapon => martial)
    ///   ranged = usable ranged weapon skill + marksman
    ///   casting = casting
    /// power = survival + offense
    /// </summary>
    internal static int GetCombatPower(Chara c)
    {
        if (c == null)
        {
            return 1;
        }

        try
        {
            int dv = 0;
            int pv = 0;
            try { dv = Math.Max(0, c.DV); } catch { dv = 0; }
            try { pv = Math.Max(0, c.PV); } catch { pv = 0; }
            int survival = (dv + pv) / 10;

            int tactics = SafeSkill(c, DungeonDispatchMission.SkillTactics);
            int casting = SafeSkill(c, DungeonDispatchMission.SkillCasting);
            int marksman = SafeSkill(c, DungeonDispatchMission.SkillMarksman);

            int meleeSkill = ResolveMainHandWeaponSkillId(c);
            int melee = SafeSkill(c, meleeSkill) + tactics;

            int ranged = 0;
            int rangedSkillId = ResolveUsableRangedWeaponSkillId(c);
            if (rangedSkillId > 0)
            {
                ranged = SafeSkill(c, rangedSkillId) + marksman;
            }

            int offense = melee;
            if (ranged > offense) offense = ranged;
            if (casting > offense) offense = casting;

            return Math.Max(1, survival + offense);
        }
        catch
        {
            return 1;
        }
    }

    static int ResolveMainHandWeaponSkillId(Chara c)
    {
        try
        {
            Thing? w = null;
            try { w = c?.body?.slotMainHand?.thing; } catch { w = null; }
            if (w == null)
            {
                return DungeonDispatchMission.SkillMartial;
            }

            // Ranged held in main hand still counts as its weapon skill.
            int fromTrait = TryWeaponSkillIdFromTrait(w);
            if (fromTrait > 0)
            {
                return fromTrait;
            }

            int fromCat = GuessWeaponSkillIdFromThing(w);
            if (fromCat > 0)
            {
                return fromCat;
            }

            try
            {
                if (w.IsRangedWeapon)
                {
                    return DungeonDispatchMission.SkillWeaponBow;
                }
            }
            catch
            {
            }

            try
            {
                if (w.IsMeleeWeapon || w.IsWeapon)
                {
                    return DungeonDispatchMission.SkillMartial;
                }
            }
            catch
            {
            }
        }
        catch
        {
        }

        return DungeonDispatchMission.SkillMartial;
    }

    /// <summary>
    /// Returns ranged weapon skill id if the chara has a usable ranged weapon, else 0.
    /// </summary>
    static int ResolveUsableRangedWeaponSkillId(Chara c)
    {
        if (c == null)
        {
            return 0;
        }

        try
        {
            Thing? ranged = null;
            try { ranged = c.ranged; } catch { ranged = null; }
            if (ranged == null)
            {
                try { ranged = c.GetBestRangedWeapon(); } catch { ranged = null; }
            }

            if (ranged == null)
            {
                return 0;
            }

            try
            {
                if (!c.CanEquipRanged(ranged) && c.ranged != ranged)
                {
                    // Best-in-bag but cannot equip: still count lightly via skill id only if already the equipped ranged.
                }
            }
            catch
            {
            }

            // Prefer currently equipped/assigned ranged; otherwise accept best ranged if equippable.
            bool usable = false;
            try
            {
                if (c.ranged != null && ReferenceEquals(c.ranged, ranged))
                {
                    usable = true;
                }
                else
                {
                    usable = c.CanEquipRanged(ranged);
                }
            }
            catch
            {
                usable = true;
            }

            if (!usable)
            {
                return 0;
            }

            int id = TryWeaponSkillIdFromTrait(ranged);
            if (id > 0)
            {
                return id;
            }

            id = GuessWeaponSkillIdFromThing(ranged);
            if (id > 0)
            {
                return id;
            }

            return DungeonDispatchMission.SkillWeaponBow;
        }
        catch
        {
            return 0;
        }
    }

    static int TryWeaponSkillIdFromTrait(Thing w)
    {
        try
        {
            if (w?.trait is TraitToolRange tr)
            {
                Element? ws = tr.WeaponSkill;
                if (ws != null && ws.id > 0)
                {
                    return ws.id;
                }
            }
        }
        catch
        {
        }

        return 0;
    }

    static int GuessWeaponSkillIdFromThing(Thing w)
    {
        if (w == null)
        {
            return 0;
        }

        string key = "";
        try
        {
            key = (w.category?.id ?? w.category?.name ?? w.sourceCard?.category ?? w.id ?? "").ToLowerInvariant();
        }
        catch
        {
            try { key = (w.id ?? "").ToLowerInvariant(); } catch { key = ""; }
        }

        string tags = "";
        try
        {
            if (w.sourceCard?.tag != null)
            {
                tags = string.Join(",", w.sourceCard.tag).ToLowerInvariant();
            }
        }
        catch
        {
        }

        string blob = key + "," + tags + "," + (w.id ?? "").ToLowerInvariant();

        if (blob.Contains("gun") || blob.Contains("firearm") || blob.Contains("pistol") || blob.Contains("rifle"))
            return DungeonDispatchMission.SkillWeaponGun;
        if (blob.Contains("crossbow"))
            return DungeonDispatchMission.SkillWeaponCrossbow;
        if (blob.Contains("bow") || blob.Contains("sling"))
            return DungeonDispatchMission.SkillWeaponBow;
        if (blob.Contains("throw"))
            return DungeonDispatchMission.SkillThrowing;
        if (blob.Contains("dagger") || blob.Contains("knife"))
            return DungeonDispatchMission.SkillWeaponDagger;
        if (blob.Contains("axe"))
            return DungeonDispatchMission.SkillWeaponAxe;
        if (blob.Contains("pole") || blob.Contains("spear") || blob.Contains("lance"))
            return DungeonDispatchMission.SkillWeaponPolearm;
        if (blob.Contains("staff") || blob.Contains("cane") || blob.Contains("rod"))
            return DungeonDispatchMission.SkillWeaponStaff;
        if (blob.Contains("scythe"))
            return DungeonDispatchMission.SkillWeaponScythe;
        if (blob.Contains("blunt") || blob.Contains("mace") || blob.Contains("hammer") || blob.Contains("club"))
            return DungeonDispatchMission.SkillWeaponBlunt;
        if (blob.Contains("sword") || blob.Contains("blade") || blob.Contains("katana"))
            return DungeonDispatchMission.SkillWeaponSword;
        if (blob.Contains("martial") || blob.Contains("fist") || blob.Contains("claw"))
            return DungeonDispatchMission.SkillMartial;

        try
        {
            if (w.IsRangedWeapon)
            {
                return DungeonDispatchMission.SkillWeaponBow;
            }
        }
        catch
        {
        }

        return 0;
    }

    internal static int AggregateCombatPower(IList<Chara> members)
    {
        if (members == null || members.Count == 0)
        {
            return 1;
        }

        int sum = 0;
        for (int i = 0; i < members.Count; i++)
        {
            sum += GetCombatPower(members[i]);
        }

        return Math.Max(1, sum);
    }

    internal static int AggregateSkill(IList<Chara> members, int skillId)
    {
        if (members == null || members.Count == 0)
        {
            return 0;
        }

        int sum = 0;
        for (int i = 0; i < members.Count; i++)
        {
            sum += SafeSkill(members[i], skillId);
        }

        return sum;
    }

    internal static int AggregateGatherSkill(IList<Chara> members, Zone? zone)
    {
        if (members == null || members.Count == 0)
        {
            return 0;
        }

        int sum = 0;
        for (int i = 0; i < members.Count; i++)
        {
            sum += GetRelevantGatherSkill(members[i], zone);
        }

        return sum;
    }

    /// <summary>
    /// The skill a region actually pays for: lumber in forest, mining or digging in the
    /// mountains, digging or fishing on a beach. Anywhere else there is no specialty, so
    /// the caller's gathering value comes straight back and can be folded in without the
    /// caller special-casing "plain".
    /// </summary>
    internal static int RegionSpecialtySkill(Chara c, string? regionKind, int noSpecialty)
    {
        string rk = NormalizeRegionKind(regionKind);
        if (rk == "forest")
        {
            return SafeSkill(c, DungeonDispatchMission.SkillLumber);
        }

        if (rk == "mountain")
        {
            return Math.Max(
                SafeSkill(c, DungeonDispatchMission.SkillMining),
                SafeSkill(c, DungeonDispatchMission.SkillDigging));
        }

        if (rk == "beach")
        {
            return Math.Max(
                SafeSkill(c, DungeonDispatchMission.SkillDigging),
                SafeSkill(c, DungeonDispatchMission.SkillFishing));
        }

        return noSpecialty;
    }

    internal static int AggregateRegionGather(IList<Chara> members, string? regionKind)
    {
        if (members == null || members.Count == 0)
        {
            return 0;
        }

        string rk = NormalizeRegionKind(regionKind);
        int sum = 0;
        for (int i = 0; i < members.Count; i++)
        {
            Chara c = members[i];
            int g = SafeSkill(c, DungeonDispatchMission.SkillGather);
            // plain: no specialty, so g survives the Math.Max untouched.
            sum += Math.Max(g, RegionSpecialtySkill(c, rk, g));
        }

        return sum;
    }

    internal static int GetDangerDays(int dangerLv, int combatPower)
    {
        // New power scale is roughly tens~hundreds per member; keep multi-day clears.
        int power = Math.Max(1, combatPower);
        int danger = Math.Max(1, dangerLv);
        int days = Mathf.CeilToInt((danger * 8f) / power);
        return Math.Max(1, days);
    }

    internal static int GetSuccessChance(int combatPower, int dangerLv, int exploreSkill, int lockpickSkill, int gatherSkill = 0)
    {
        int power = Math.Max(1, combatPower);
        int danger = Math.Max(1, dangerLv);

        // Anchor (player): danger 150 / power 1000 ? 60-70% with modest skills.
        // "Fair" power is about danger*6.5; each extra power point moves chance slowly.
        // Skills help a little, but no longer dominate the roll.
        float need = danger * 6.5f;
        float edge = power - need;
        float skillBonus = exploreSkill / 15f
            + lockpickSkill / 25f
            + gatherSkill / 30f;
        float raw = 65f + edge * 0.05f + skillBonus;

        // Domination floors are much stricter than the old *3/*5 bands.
        if (power >= danger * 20)
        {
            raw = Math.Max(raw, 100f);
        }
        else if (power >= danger * 15)
        {
            raw = Math.Max(raw, 92f);
        }
        else if (power >= danger * 12)
        {
            raw = Math.Max(raw, 85f);
        }
        else if (power >= danger * 10)
        {
            raw = Math.Max(raw, 78f);
        }
        else if (power >= danger * 8)
        {
            raw = Math.Max(raw, 72f);
        }

        return Mathf.Clamp(Mathf.RoundToInt(raw), 15, 100);
    }

    internal static int GetBaseHours(int distDays, int dangerDays, bool halfTravel = false)
    {
        // Dungeon duration only. Region uses GetRegionTravelHours / GetRegionTotalHours.
        float distPart = Math.Max(1, distDays);
        if (halfTravel)
        {
            distPart *= 0.5f;
        }

        float dangerPart = Math.Max(1, dangerDays);
        int hours = Mathf.CeilToInt((distPart + dangerPart) * 24f);
        return Math.Max(halfTravel ? 12 : 24, hours);
    }

    /// <summary>Region travel hours only (half distance days). Explore weeks added by caller.</summary>
    internal static int GetRegionTravelHours(int distDays)
    {
        float distPart = Math.Max(1, distDays) * 0.5f;
        return Math.Max(6, Mathf.CeilToInt(distPart * 24f));
    }

    internal static int GetRegionTotalHours(int distDays, int exploreWeeks)
    {
        int weeks = Mathf.Clamp(exploreWeeks <= 0 ? 1 : exploreWeeks, 1, 4);
        return GetRegionTravelHours(distDays) + weeks * 7 * 24;
    }

    internal static int GetBaseHours(DungeonDispatchTarget target, int dangerDays)
    {
        if (target == null)
        {
            return GetBaseHours(1, dangerDays, halfTravel: false);
        }

        // Region duration is travel + explore weeks (see GetRegionTotalHours).
        if (target.IsRegion)
        {
            return GetRegionTotalHours(target.DistDays, 1);
        }

        return GetBaseHours(target.DistDays, dangerDays, halfTravel: false);
    }

    internal static int RollMissionHours(int baseHours)
    {
        int min = Mathf.Max(1, Mathf.FloorToInt(baseHours * 0.8f));
        int max = Mathf.Max(min + 1, Mathf.FloorToInt(baseHours * 1.2f));
        try
        {
            return min + EClass.rnd(Math.Max(1, max - min + 1));
        }
        catch
        {
            return baseHours;
        }
    }

    /// <summary>
    /// Selected party totals for the member picker confirm row.
    /// No reward-tier wording — keep loot as a surprise.
    /// </summary>
    internal static string TeamSummaryLine(DungeonDispatchTarget target, IList<Chara> members)
    {
        if (target == null || members == null || members.Count == 0)
        {
            return NpcLabor.LaborText.T("dis.stat.none");
        }

        int power = AggregateCombatPower(members);
        int explore = AggregateSkill(members, DungeonDispatchMission.SkillExplore);
        int lockpick = AggregateSkill(members, DungeonDispatchMission.SkillLockpick);
        int gather = target.IsRegion
            ? AggregateRegionGather(members, target.RegionKind)
            : AggregateGatherSkill(members, target.Zone);
        if (target.IsRegion)
        {
            // Region has no combat success roll / danger.
            return NpcLabor.LaborText.T("dis.stat.line", power, explore, lockpick, gather);
        }

        int chance = GetSuccessChance(power, target.DangerLv, explore, lockpick, gather);
        // Keep compact so LayerList sub column still shows success %.
        return NpcLabor.LaborText.T("dis.stat.lineChance", power, explore, lockpick, gather, chance);
    }

    internal static string MemberSkillLine(Chara worker, Zone? zone = null)
    {
        int power = GetCombatPower(worker);
        int explore = SafeSkill(worker, DungeonDispatchMission.SkillExplore);
        int lockpick = SafeSkill(worker, DungeonDispatchMission.SkillLockpick);
        int gather = GetRelevantGatherSkill(worker, zone);
        return NpcLabor.LaborText.T("dis.stat.line", power, explore, lockpick, gather);
    }

    internal static string MemberSkillLine(Chara worker, DungeonDispatchTarget target)
    {
        if (target != null && target.IsRegion)
        {
            int power = GetCombatPower(worker);
            int explore = SafeSkill(worker, DungeonDispatchMission.SkillExplore);
            int lockpick = SafeSkill(worker, DungeonDispatchMission.SkillLockpick);
            int gather = AggregateRegionGather(new List<Chara> { worker }, target.RegionKind);
            return NpcLabor.LaborText.T("dis.stat.line", power, explore, lockpick, gather);
        }

        return MemberSkillLine(worker, target?.Zone);
    }

    /// <summary>
    /// Skill ids the dispatch system actually values, for the hobby/work picker
    /// tag (爱好/工作): combat weapons + tactics/marksman/casting, explore,
    /// lockpick, and the gather family. A hobby/work matching any of these marks
    /// the person as a natural fit at a glance.
    /// </summary>
    internal static IReadOnlyCollection<int> RelevantSkillIds()
    {
        return new List<int>
        {
            DungeonDispatchMission.SkillExplore,
            DungeonDispatchMission.SkillLockpick,
            DungeonDispatchMission.SkillGather,
            DungeonDispatchMission.SkillMining,
            DungeonDispatchMission.SkillLumber,
            DungeonDispatchMission.SkillDigging,
            DungeonDispatchMission.SkillFishing,
            DungeonDispatchMission.SkillMartial,
            DungeonDispatchMission.SkillTactics,
            DungeonDispatchMission.SkillMarksman,
            DungeonDispatchMission.SkillCasting,
            DungeonDispatchMission.SkillWeaponSword,
            DungeonDispatchMission.SkillWeaponAxe,
            DungeonDispatchMission.SkillWeaponStaff,
            DungeonDispatchMission.SkillWeaponBow,
            DungeonDispatchMission.SkillWeaponGun,
            DungeonDispatchMission.SkillWeaponPolearm,
            DungeonDispatchMission.SkillWeaponDagger,
            DungeonDispatchMission.SkillThrowing,
            DungeonDispatchMission.SkillWeaponCrossbow,
            DungeonDispatchMission.SkillWeaponScythe,
            DungeonDispatchMission.SkillWeaponBlunt,
        };
    }

    internal static int GetRelevantGatherSkill(Chara worker, Zone? zone)
    {
        int gather = SafeSkill(worker, DungeonDispatchMission.SkillGather);
        int mining = SafeSkill(worker, DungeonDispatchMission.SkillMining);
        int lumber = SafeSkill(worker, DungeonDispatchMission.SkillLumber);
        int digging = SafeSkill(worker, DungeonDispatchMission.SkillDigging);

        // Prefer the strongest of gather-family skills; dungeon harvest sites use gathering/mining/digging.
        int best = gather;
        if (mining > best)
        {
            best = mining;
        }

        if (digging > best)
        {
            best = digging;
        }

        // Forest-like random nature dungeons also care about lumber.
        try
        {
            if (zone is Zone_RandomDungeonNature || zone is Zone_RandomDungeonForest)
            {
                if (lumber > best)
                {
                    best = lumber;
                }
            }
        }
        catch
        {
            if (lumber > best)
            {
                best = lumber;
            }
        }

        return best;
    }

    internal static int SafeSkill(Chara c, int id)
    {
        try
        {
            return c != null ? Math.Max(0, c.Evalue(id)) : 0;
        }
        catch
        {
            return 0;
        }
    }

    internal static List<DungeonDispatchTarget> ListNearby(FactionBranch? branch)
    {
        var result = new List<DungeonDispatchTarget>();
        try
        {
            Zone? home = branch?.owner;
            Region? region = EClass.world?.region;
            if (home == null || region == null)
            {
                return result;
            }

            int radius = GetRadius(branch);
            var seen = new HashSet<int>();
            List<Zone> zones = new List<Zone>();

            try
            {
                List<Zone>? inRadius = region.ListZonesInRadius(home, radius);
                if (inRadius != null)
                {
                    zones.AddRange(inRadius);
                }
            }
            catch
            {
            }

            // Also scan known random sites; ListZonesInRadius can miss some map entries.
            try
            {
                List<Zone>? randoms = region.ListRandomSites();
                if (randoms != null)
                {
                    foreach (Zone z in randoms)
                    {
                        if (z == null)
                        {
                            continue;
                        }

                        try
                        {
                            if (z.Dist(home) <= radius)
                            {
                                zones.Add(z);
                            }
                        }
                        catch
                        {
                            zones.Add(z);
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                List<Zone>? all = EClass.game?.spatials?.Zones;
                if (all != null)
                {
                    foreach (Zone z in all)
                    {
                        if (z == null)
                        {
                            continue;
                        }

                        try
                        {
                            if (z.isRandomSite && z.isKnown && z.Dist(home) <= radius)
                            {
                                zones.Add(z);
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }

            foreach (Zone z in zones)
            {
                if (z == null || !seen.Add(z.uid))
                {
                    continue;
                }

                Zone top = SafeTopZone(z) ?? z;
                if (!seen.Add(top.uid) && top.uid != z.uid)
                {
                    // still evaluate top once
                }

                Zone candidate = PreferDispatchRoot(top, z);
                if (!IsDispatchable(candidate, home))
                {
                    continue;
                }

                if (!seen.Contains(candidate.uid))
                {
                    seen.Add(candidate.uid);
                }

                // Dedup by candidate uid already handled via seen on z; re-check list.
                bool exists = false;
                for (int i = 0; i < result.Count; i++)
                {
                    if (result[i].Zone != null && result[i].Zone!.uid == candidate.uid)
                    {
                        exists = true;
                        break;
                    }
                }

                if (exists)
                {
                    continue;
                }

                result.Add(new DungeonDispatchTarget
                {
                    Zone = candidate,
                    DistDays = ZoneDistDays(home, candidate),
                    DangerLv = Math.Max(1, candidate.DangerLv),
                    IsRandomSite = candidate.isRandomSite,
                    Name = SafeZoneName(candidate),
                    Compass = CompassFrom(home, candidate),
                });
            }

            result.Sort((a, b) =>
            {
                int cmp = a.DistDays.CompareTo(b.DistDays);
                if (cmp != 0)
                {
                    return cmp;
                }

                cmp = a.DangerLv.CompareTo(b.DangerLv);
                if (cmp != 0)
                {
                    return cmp;
                }

                return string.CompareOrdinal(a.Name, b.Name);
            });
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("dispatch list targets failed: " + ex.Message);
        }

        return result;
    }

    static Zone PreferDispatchRoot(Zone top, Zone original)
    {
        try
        {
            // Keep the random map pin itself; its top zone may lose isRandomSite.
            if (original.isRandomSite)
            {
                return original;
            }
        }
        catch
        {
        }

        return top ?? original;
    }

    static Zone? SafeTopZone(Zone z)
    {
        try
        {
            return z.GetTopZone() ?? z;
        }
        catch
        {
            return z;
        }
    }

    internal static bool IsDispatchable(Zone z, Zone home)
    {
        if (z == null || home == null)
        {
            return false;
        }

        try
        {
            if (z == home || z.IsPCFaction || z.IsRegion)
            {
                return false;
            }

            // Skip pure instance floors; allow top dungeon / random site map entries.
            try
            {
                if (z.IsInstance && z.GetTopZone() != null && z.GetTopZone().uid != z.uid && !z.isRandomSite)
                {
                    return false;
                }
            }
            catch
            {
                if (z.IsInstance && !z.isRandomSite)
                {
                    return false;
                }
            }

            // Random map sites may not flip isKnown until visited; still list if on region map.
            bool known = false;
            try
            {
                known = z.isKnown;
            }
            catch
            {
            }

            if (!known)
            {
                bool allowUnknownRandom = false;
                try
                {
                    allowUnknownRandom = z.isRandomSite;
                }
                catch
                {
                }

                if (!allowUnknownRandom)
                {
                    return false;
                }
            }

            if (z.destryoed)
            {
                return false;
            }

            if (z.isRandomSite)
            {
                return true;
            }

            if (z.IsNefia)
            {
                return true;
            }

            if (z is Zone_Dungeon || z is Zone_RandomDungeon)
            {
                return true;
            }

            string id = z.id ?? "";
            if (id.IndexOf("dungeon", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("cave", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("ruin", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("nefia", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            try
            {
                if (z.ShowDangerLv && z.DangerLv > 1)
                {
                    return true;
                }
            }
            catch
            {
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    internal static string SafeZoneName(Zone z)
    {
        // Do NOT use NameWithDangerLevel — list already shows 危N on the right.
        try
        {
            string n = z.Name;
            if (!string.IsNullOrEmpty(n))
            {
                return TruncateName(n, 12);
            }
        }
        catch
        {
        }


        try
        {
            return TruncateName(z.id ?? ("#" + z.uid), 12);
        }
        catch
        {
            return "???";
        }
    }

    internal static string TargetListSub(DungeonDispatchTarget t)
    {
        if (t == null)
        {
            return "";
        }

        string compass = string.IsNullOrEmpty(t.Compass) ? "?" : t.Compass;
        if (t.IsRegion)
        {
            // Region outing has no danger level.
            return NpcLabor.LaborText.T("dis.dist", compass, t.DistDays);
        }

        // Compact list meta for dungeons.
        return NpcLabor.LaborText.T("dis.distDanger", compass, t.DistDays, t.DangerLv);
    }

    internal static string TargetInfoLine(DungeonDispatchTarget t)
    {
        if (t == null)
        {
            return "";
        }

        if (t.IsRegion)
        {
            return RegionInfoLine(t);
        }

        int dangerDays = GetDangerDays(t.DangerLv, 1);
        int baseHours = GetBaseHours(t, dangerDays);
        string kind = t.IsRandomSite
            ? NpcLabor.LaborText.T("dis.kind.random")
            : NpcLabor.LaborText.T("dis.kind.fixed");
        return NpcLabor.LaborText.T(
            "dis.info.dungeon",
            t.Name,
            kind,
            t.Compass,
            t.DangerLv,
            t.DistDays,
            Mathf.CeilToInt(baseHours / 24f));
    }

    internal static string TruncateName(string n, int max)
    {
        if (string.IsNullOrEmpty(n))
        {
            return "???";
        }

        // Drop common danger suffixes if a caller passed NameWithDangerLevel by mistake.
        int cut = n.IndexOf(" Lv", StringComparison.OrdinalIgnoreCase);
        if (cut < 0)
        {
            cut = n.IndexOf("LV", StringComparison.Ordinal);
        }

        if (cut > 0)
        {
            n = n.Substring(0, cut).Trim();
        }

        int paren = n.IndexOf('(');
        if (paren > 0)
        {
            n = n.Substring(0, paren).Trim();
        }

        if (n.Length > max)
        {
            return n.Substring(0, max) + "…";
        }

        return n;
    }


    internal static List<DungeonDispatchTarget> ListRegionTargets(FactionBranch? branch)
    {
        var result = new List<DungeonDispatchTarget>();
        try
        {
            Zone? home = branch?.owner ?? EClass.pc?.homeZone ?? EClass.BranchOrHomeBranch?.owner;
            if (home == null)
            {
                return result;
            }

            // Always offer four virtual region types; pick nearest matching tile if possible.
            string[] kinds = { "plain", "forest", "beach", "mountain" };
            foreach (string kind in kinds)
            {
                DungeonDispatchTarget t = BuildRegionTarget(home, kind);
                if (t.RegionGx == int.MinValue || t.RegionGy == int.MinValue)
                {
                    continue;
                }

                result.Add(t);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("dispatch list regions failed: " + ex.Message);
        }

        return result;
    }

    internal static DungeonDispatchTarget BuildRegionTarget(Zone home, string kind)
    {
        string name = RegionDisplayName(kind);
        int dist = 1;
        string compass = NpcLabor.LaborText.T("dis.compass.near");
        int gx = int.MinValue;
        int gy = int.MinValue;
        bool found = false;

        try
        {
            if (TryFindNearestRegionTile(home, kind, out int fx, out int fy, out int fdist))
            {
                found = true;
                gx = fx;
                gy = fy;
                dist = Math.Max(1, fdist);
                compass = CompassFromCoords(home.x, home.y, fx, fy);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("region nearest " + kind + ": " + ex.Message);
        }

        if (!found)
        {
            dist = 1;
            compass = NpcLabor.LaborText.T("dis.compass.near");
        }

        return new DungeonDispatchTarget
        {
            Zone = null,
            DistDays = dist,
            DangerLv = 1, // unused for region; kept for save/schema compat
            IsRandomSite = false,
            Name = name,
            Compass = compass,
            IsRegion = true,
            RegionKind = NormalizeRegionKind(kind),
            RegionGx = gx,
            RegionGy = gy,
        };
    }


    /// <summary>
    /// Canonical lowercase region kind (plain / forest / beach / mountain).
    /// All region matching, zone ids and saved missions must use this form so a
    /// mixed-case input can never create duplicate keys or miss a match.
    /// </summary>
    internal static string NormalizeRegionKind(string? kind)
    {
        return (kind ?? "").Trim().ToLowerInvariant();
    }

    internal static string RegionDisplayName(string kind)
    {
        switch (NormalizeRegionKind(kind))
        {
            case "plain":
                return NpcLabor.LaborText.T("dis.region.name.plain");
            case "forest":
                return NpcLabor.LaborText.T("dis.region.name.forest");
            case "beach":
                return NpcLabor.LaborText.T("dis.region.name.beach");
            case "mountain":
                return NpcLabor.LaborText.T("dis.region.name.mountain");
            default:
                return NpcLabor.LaborText.T("dis.region.name.area");
        }
    }

    internal static string CompassFromCoords(int hx, int hy, int tx, int ty)
    {
        int dx = tx - hx;
        int dy = ty - hy;
        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            if (dx > 0) return NpcLabor.LaborText.T("dis.compass.e");
            if (dx < 0) return NpcLabor.LaborText.T("dis.compass.w");
        }
        if (dy > 0) return NpcLabor.LaborText.T("dis.compass.s");
        if (dy < 0) return NpcLabor.LaborText.T("dis.compass.n");
        return NpcLabor.LaborText.T("dis.compass.near");
    }

    internal static bool TryFindNearestRegionTile(Zone home, string kind, out int gx, out int gy, out int dist)
    {
        gx = home.x;
        gy = home.y;
        dist = int.MaxValue;
        EloMap? map = null;
        try
        {
            map = EClass.world?.region?.elomap;
        }
        catch
        {
            map = null;
        }

        if (map == null)
        {
            try
            {
                map = EClass.scene?.elomap;
            }
            catch
            {
                map = null;
            }
        }

        if (map == null)
        {
            return false;
        }

        int minX = 0;
        int minY = 0;
        int w = 0;
        int h = 0;
        try
        {
            minX = map.minX;
            minY = map.minY;
            w = map.w;
            h = map.h;
        }
        catch
        {
            return false;
        }

        if (w <= 0 || h <= 0)
        {
            return false;
        }

        int best = int.MaxValue;
        int bestX = home.x;
        int bestY = home.y;
        bool any = false;
        int maxR = Math.Max(w, h);
        for (int r = 0; r <= maxR; r++)
        {
            int x0 = home.x - r;
            int x1 = home.x + r;
            int y0 = home.y - r;
            int y1 = home.y + r;
            for (int x = x0; x <= x1; x++)
            {
                CheckTile(map, kind, x, y0, home, ref any, ref best, ref bestX, ref bestY);
                if (r > 0)
                {
                    CheckTile(map, kind, x, y1, home, ref any, ref best, ref bestX, ref bestY);
                }
            }
            for (int y = y0 + 1; y <= y1 - 1; y++)
            {
                CheckTile(map, kind, x0, y, home, ref any, ref best, ref bestX, ref bestY);
                if (r > 0)
                {
                    CheckTile(map, kind, x1, y, home, ref any, ref best, ref bestX, ref bestY);
                }
            }

            if (any && best <= r)
            {
                break;
            }
        }

        if (!any)
        {
            return false;
        }

        gx = bestX;
        gy = bestY;
        dist = Math.Max(1, best);
        return true;
    }

    static bool CheckTile(
        EloMap map,
        string kind,
        int gx,
        int gy,
        Zone home,
        ref bool any,
        ref int best,
        ref int bestX,
        ref int bestY)
    {
        try
        {
            EloMap.TileInfo? info = map.GetTileInfo(gx, gy);
            if (info == null || !MatchRegionTile(info, kind) || !IsFreeRegionTile(map, home, gx, gy))
            {
                return false;
            }

            int d = Math.Max(Math.Abs(gx - home.x), Math.Abs(gy - home.y));
            if (d <= 0)
            {
                return false;
            }
            if (d < best)
            {
                best = d;
                bestX = gx;
                bestY = gy;
                any = true;
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    internal static bool MatchRegionTile(EloMap.TileInfo info, string kind)
    {
        if (info == null)
        {
            return false;
        }

        string biome = "";
        try
        {
            biome = info.source?.idBiome ?? "";
        }
        catch
        {
            biome = "";
        }

        string alias = "";
        try { alias = info.source?.alias ?? ""; } catch { alias = ""; }
        string tileName = "";
        try { tileName = info.source?.name ?? ""; } catch { tileName = ""; }
        int tileId = 0;
        try { tileId = info.source?.id ?? 0; } catch { tileId = 0; }
        string profile = "";
        try { profile = info.idZoneProfile ?? ""; } catch { profile = ""; }

        string b = biome;
        bool rock = false;
        bool shore = false;
        bool sea = false;
        bool snow = false;
        bool blocked = false;
        bool canEmbark = false;
        try { rock = info.rock; } catch { }
        try { shore = info.shore; } catch { }
        try { sea = info.sea; } catch { }
        try { snow = info.IsSnow; } catch { }
        try { blocked = info.blocked; } catch { }
        try { canEmbark = info.CanEmbark && !blocked; } catch { canEmbark = !string.IsNullOrEmpty(profile) && !blocked; }

        if (!canEmbark || blocked)
        {
            return false;
        }

        switch (NormalizeRegionKind(kind))
        {
            case "plain":
                return !rock && !sea && !shore && !snow
                    && (b.Equals("Plain", StringComparison.OrdinalIgnoreCase)
                        || b.Equals("Default", StringComparison.OrdinalIgnoreCase)
                        || b.Equals("Mud", StringComparison.OrdinalIgnoreCase)
                        || alias.Equals("plain", StringComparison.OrdinalIgnoreCase)
                        || tileId == 1);
            case "forest":
                return b.Equals("Forest", StringComparison.OrdinalIgnoreCase)
                    || alias.Equals("forest", StringComparison.OrdinalIgnoreCase)
                    || tileId == 3 || tileId == 13;
            case "beach":
                return shore
                    || b.Equals("Sand", StringComparison.OrdinalIgnoreCase)
                    || alias.Equals("beach", StringComparison.OrdinalIgnoreCase)
                    || tileId == 14
                    || (b.Equals("Water", StringComparison.OrdinalIgnoreCase) && !sea);
            case "mountain":
                // Yellow enterable mountain only. Grey "rock"/"wall" mountains are blocked.
                if (rock || sea || shore || tileId == 6)
                {
                    return false;
                }

                if (tileId == 5
                    || alias.Equals("mountain", StringComparison.OrdinalIgnoreCase)
                    || tileName.Equals("Mountain", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(profile)
                    && (profile.IndexOf("Mountain", StringComparison.OrdinalIgnoreCase) >= 0
                        || profile.IndexOf("Hill", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return true;
                }

                return b.Equals("Barren", StringComparison.OrdinalIgnoreCase) && !snow;
            default:
                return false;
        }
    }

    internal static bool IsFreeRegionTile(EloMap map, Zone? home, int gx, int gy)
    {
        if (home != null)
        {
            try
            {
                if (gx == home.x && gy == home.y)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        Zone? existing = null;
        try
        {
            existing = map?.GetZone(gx, gy);
        }
        catch
        {
            existing = null;
        }

        if (existing == null)
        {
            try
            {
                if (EClass.game?.spatials?.Zones != null)
                {
                    foreach (Zone z in EClass.game.spatials.Zones)
                    {
                        if (z == null || z.destryoed)
                        {
                            continue;
                        }

                        try
                        {
                            if (z.x == gx && z.y == gy)
                            {
                                existing = z;
                                break;
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }

        if (existing == null)
        {
            return true;
        }

        return IsReusableFieldZone(existing);
    }

    internal static bool IsReusableFieldZone(Zone? zone)
    {
        if (zone == null)
        {
            return false;
        }

        try
        {
            if (zone.destryoed || zone is Region || zone is Zone_Tent || zone.IsPCFaction)
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        string id = "";
        try { id = zone.id ?? ""; } catch { id = ""; }
        return string.Equals(id, "field", StringComparison.OrdinalIgnoreCase)
            || id.StartsWith("region:", StringComparison.OrdinalIgnoreCase);
    }

    internal static string RegionInfoLine(DungeonDispatchTarget t, int exploreWeeks = 1)
    {
        if (t == null)
        {
            return "";
        }

        int weeks = Mathf.Clamp(exploreWeeks <= 0 ? 1 : exploreWeeks, 1, 4);
        return NpcLabor.LaborText.T(
            "dis.info.region",
            t.Name,
            t.Compass,
            weeks);
    }

    /// <summary>
    /// Progress maps to deeper floors: after k/totalFloors progress, use floor index k.
    /// Only returns already-existing zones (FindZone / children). Never creates floors —
    /// creating rewired Spatial parents and broke fixed dungeon exits.
    /// </summary>
    internal static Zone? ResolveFloorForProgress(Zone root, int progressPercent)
    {
        if (root == null)
        {
            return null;
        }

        Zone top;
        try
        {
            top = root.GetTopZone() ?? root;
        }
        catch
        {
            top = root;
        }

        int wantLv = EstimateFloorLvForProgress(top, progressPercent);
        Zone? exact = FindExistingFloor(top, wantLv);
        if (exact != null)
        {
            return exact;
        }

        // Fallback: deepest existing floor the party could have reached (not deeper than want).
        List<Zone> floors = ListExistingFloors(top);
        if (floors.Count == 0)
        {
            return top;
        }

        Zone best = floors[0];
        int bestDepth = 0;
        int wantDepth = FloorDepth(top.lv, wantLv);
        for (int i = 0; i < floors.Count; i++)
        {
            Zone f = floors[i];
            int d = FloorDepth(top.lv, SafeZoneLv(f));
            if (d <= wantDepth && d >= bestDepth)
            {
                best = f;
                bestDepth = d;
            }
        }

        return best;
    }

    internal static int EstimateFloorLvForProgress(Zone root, int progressPercent)
    {
        if (root == null)
        {
            return 0;
        }

        Zone top;
        try
        {
            top = root.GetTopZone() ?? root;
        }
        catch
        {
            top = root;
        }

        int entrance = SafeZoneLv(top);
        int deepest = entrance;
        try
        {
            deepest = top.GetDeepestLv();
        }
        catch
        {
            deepest = entrance;
        }

        // Prefer real child extremes when GetDeepestLv is flat.
        List<Zone> floors = ListExistingFloors(top);
        for (int i = 0; i < floors.Count; i++)
        {
            int lv = SafeZoneLv(floors[i]);
            if (FloorDepth(entrance, lv) > FloorDepth(entrance, deepest))
            {
                deepest = lv;
            }
        }

        int totalFloors = Math.Max(1, FloorDepth(entrance, deepest) + 1);
        int pct = Mathf.Clamp(progressPercent, 0, 100);
        int idx = pct * totalFloors / 100;
        if (idx >= totalFloors)
        {
            idx = totalFloors - 1;
        }

        if (deepest == entrance || idx == 0)
        {
            return entrance;
        }

        int dir = deepest < entrance ? -1 : 1;
        return entrance + dir * idx;
    }

    static int FloorDepth(int entranceLv, int floorLv)
    {
        return Math.Abs(floorLv - entranceLv);
    }

    static int SafeZoneLv(Zone z)
    {
        try
        {
            return z.lv;
        }
        catch
        {
            return 0;
        }
    }

    static Zone? FindExistingFloor(Zone top, int lv)
    {
        if (top == null)
        {
            return null;
        }

        try
        {
            if (SafeZoneLv(top) == lv)
            {
                return top;
            }
        }
        catch
        {
        }

        try
        {
            Zone? z = top.FindZone(lv);
            if (z != null && !z.destryoed)
            {
                return z;
            }
        }
        catch
        {
        }

        try
        {
            if (top.children != null)
            {
                for (int i = 0; i < top.children.Count; i++)
                {
                    if (top.children[i] is Zone z && !z.destryoed && SafeZoneLv(z) == lv)
                    {
                        return z;
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    internal static List<Zone> ListExistingFloors(Zone top)
    {
        var list = new List<Zone>();
        if (top == null)
        {
            return list;
        }

        void Add(Zone? z)
        {
            if (z == null || z.destryoed)
            {
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].uid == z.uid)
                {
                    return;
                }
            }

            list.Add(z);
        }

        Add(top);

        int entrance = SafeZoneLv(top);
        int deepest = entrance;
        try
        {
            deepest = top.GetDeepestLv();
        }
        catch
        {
        }

        int lo = Math.Min(entrance, deepest);
        int hi = Math.Max(entrance, deepest);
        // Probe known level ids without creating.
        for (int lv = lo; lv <= hi; lv++)
        {
            if (lv == entrance)
            {
                continue;
            }

            try
            {
                Add(top.FindZone(lv));
            }
            catch
            {
            }
        }

        try
        {
            if (top.children != null)
            {
                for (int i = 0; i < top.children.Count; i++)
                {
                    if (top.children[i] is Zone z)
                    {
                        Add(z);
                    }
                }
            }
        }
        catch
        {
        }

        list.Sort((a, b) => FloorDepth(entrance, SafeZoneLv(a)).CompareTo(FloorDepth(entrance, SafeZoneLv(b))));
        return list;
    }

    /// <summary>
    /// Place region dispatch on a real field zone at (gx,gy) so PC can meet the squad.
    /// Uses EloMap.GetZone when present; otherwise Region.CreateZone("field").
    /// </summary>
    internal static Zone? EnsureRegionFieldZone(int gx, int gy)
    {
        Region? region = null;
        try
        {
            region = EClass.world?.region;
        }
        catch
        {
            region = null;
        }

        if (region == null)
        {
            return null;
        }

        // Existing zone bound on the overworld tile.
        try
        {
            EloMap? map = region.elomap ?? EClass.scene?.elomap;
            if (map != null)
            {
                Zone? existing = null;
                try
                {
                    existing = map.GetZone(gx, gy);
                }
                catch
                {
                    existing = null;
                }

                if (IsReusableFieldZone(existing))
                {
                    return existing;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("region GetZone: " + ex.Message);
        }

        // Fallback scan of registered zones at this tile.
        try
        {
            if (EClass.game?.spatials?.Zones != null)
            {
                foreach (Zone z in EClass.game.spatials.Zones)
                {
                    if (z == null || z.destryoed || z is Region)
                    {
                        continue;
                    }

                    try
                    {
                        if (z.x == gx && z.y == gy && IsReusableFieldZone(z))
                        {
                            return z;
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch
        {
        }

        // Create a transient field zone at the tile (vanilla Region.CreateZone path).
        try
        {
            Zone? created = region.CreateZone(new Point(gx, gy));
            if (created != null)
            {
                try
                {
                    region.elomap?.SetZone(gx, gy, created);
                }
                catch
                {
                }

                // dateExpire==0 => Zone.CanDestroy() returns false, so Region.OnActivate
                // never reaps this stakeout field mid-mission (default field expiry is 7 days).
                try
                {
                    created.dateExpire = 0;
                }
                catch
                {
                }

                Plugin.LogInfo("dispatch region field created uid=" + created.uid
                    + " @" + gx + "," + gy);
                return created;
            }
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("dispatch region CreateZone failed: " + ex.Message);
        }

        // Last resort: SpatialGen directly.
        try
        {
            Spatial? sp = SpatialGen.Create("field", region, true, gx, gy);
            Zone? z = sp as Zone;
            if (z != null)
            {
                try
                {
                    region.elomap?.SetZone(gx, gy, z);
                }
                catch
                {
                }

                try
                {
                    z.dateExpire = 0;
                }
                catch
                {
                }

                return z;
            }
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("dispatch region SpatialGen field failed: " + ex.Message);
        }

        return null;
    }

}
