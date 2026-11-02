using System;
using System.Collections.Generic;
using System.Linq;
using NpcLabor.TownLabor;

namespace NpcLabor.PermanentDispatch;

/// <summary>
/// Runtime manager for permanent town dispatch ("常驻派遣").
/// Modeled on slice D's DungeonDispatchManager: GameDate.AdvanceHour tick,
/// Zone.Activate repositioning, Game save/load hooks, world-gap auto recall.
/// Save: npclabor_pdispatch.json
///
/// Payout model (player spec 2026-10-28):
/// - the dispatched NPC physically appears in the target town for the duration;
/// - only WHOLE elapsed months are credited (potential / develop / influence
///   apply at the month boundary; gold + items accrue as counters);
/// - everything is delivered in ONE home-mailbox parcel when the posting ends,
///   whether it ends by recall, worker death, or mod removal.
/// </summary>
internal static partial class PermanentDispatchManager
{
    static readonly List<PermanentDispatchMission> Missions = new();
    static int _nextMissionId = 1;

    internal static IReadOnlyList<PermanentDispatchMission> All => Missions;

    internal static int Count => Missions.Count;

    internal static bool HasPendingHourSave => _hourSavePending;

    internal static PermanentDispatchMission? FindByChara(int uidChara)
    {
        for (int i = 0; i < Missions.Count; i++)
        {
            if (Missions[i].uidChara == uidChara)
            {
                return Missions[i];
            }
        }

        return null;
    }

    internal static PermanentDispatchMission? FindByDef(string defId)
    {
        for (int i = 0; i < Missions.Count; i++)
        {
            if (Missions[i].defId == defId)
            {
                return Missions[i];
            }
        }

        return null;
    }

    internal static PermanentDispatchMission? FindByMissionId(int id)
    {
        for (int i = 0; i < Missions.Count; i++)
        {
            if (Missions[i].missionId == id)
            {
                return Missions[i];
            }
        }

        return null;
    }

    internal static bool IsBusy(int uidChara) => FindByChara(uidChara) != null;

    internal static bool IsDefBusy(string defId) => FindByDef(defId) != null;

    /// <summary>Worker currently holding this posting, null when open or gone.</summary>
    internal static Chara? FindWorker(string defId)
    {
        PermanentDispatchMission? m = FindByDef(defId);
        if (m == null)
        {
            return null;
        }

        try
        {
            return RefChara.Get(m.uidChara);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Whether this zone's quest board hosts any permanent dispatch task.
    /// Guild tasks hang on the guild's own board, so this asks the board matcher
    /// rather than the physical post zone.</summary>
    internal static bool ZoneHasTasks(Zone? zone)
    {
        try
        {
            string? id = zone?.id;
            return !string.IsNullOrEmpty(id) && PermanentDispatchDef.ForBoard(id).Count > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Start a posting. Returns null on success, else a LaborText error line.
    /// Guild gate: the PC's rank in that guild must be >= 2 (same threshold
    /// vanilla uses to halve/double guild-skill training cost).
    /// </summary>
    internal static string? TryStart(Chara worker, PermanentDispatchDef def)
    {
        if (worker == null || def == null)
        {
            return LaborText.T("pdisp.err.invalid");
        }

        if (PermanentDispatchDef.ResolveZone(def.ZoneId) == null)
        {
            return LaborText.T("pdisp.err.noZone", def.Name);
        }

        if (LaborBusy.IsBusy(worker.uid) || IsBusy(worker.uid))
        {
            return LaborText.T("pdisp.err.busy", worker.NameSimple ?? worker.Name ?? ("#" + worker.uid));
        }

        if (IsDefBusy(def.Id))
        {
            return LaborText.T("pdisp.err.defBusy", def.Name);
        }

        if (!def.GateMet(worker))
        {
            return LaborText.T("pdisp.err.gate", worker.NameSimple ?? worker.Name ?? ("#" + worker.uid), def.Name);
        }

        if (def.IsGuildTask)
        {
            int rank = PermanentDispatchDef.PcGuildRank(def.GuildId);
            if (rank < PermanentDispatchDef.GuildRankGate)
            {
                return LaborText.T("pdisp.err.guildRank", def.Name);
            }
        }

        Zone? target = PermanentDispatchDef.ResolveZone(def.ZoneId);
        Zone? home = null;
        try
        {
            home = worker.homeZone
                ?? EClass.BranchOrHomeBranch?.owner
                ?? EClass.Branch?.owner
                ?? EClass.pc?.homeZone;
        }
        catch
        {
            home = null;
        }

        bool wasParty = LaborRoster.IsPartyMember(worker);
        if (wasParty)
        {
            try
            {
                if (!worker.IsGlobal)
                {
                    worker.SetGlobal();
                }
            }
            catch { }

            try { EClass.pc?.party?.RemoveMember(worker); } catch { }
            try { worker.c_wasInPcParty = true; } catch { }
        }

        var m = new PermanentDispatchMission
        {
            missionId = _nextMissionId++,
            defId = def.Id,
            uidChara = worker.uid,
            workerName = worker.NameSimple ?? worker.Name ?? ("#" + worker.uid),
            uidZone = target?.uid ?? 0,
            zoneId = def.ZoneId,
            zoneName = SafeZoneName(target),
            homeZoneUid = home?.uid ?? 0,
            wasPartyMember = wasParty,
            startMonth = CurrentMonthIndex(),
            settledThrough = CurrentMonthIndex(),
            monthsPaid = 0,
            guildRankAtStart = def.IsGuildTask ? Math.Max(0, PermanentDispatchDef.PcGuildRank(def.GuildId)) : 0,
        };

        Missions.Add(m);

        // Physically place the NPC in the target town.
        MoveToPost(m, worker);
        PrepareDispatchedChara(worker);

        try
        {
            Msg.Say(LaborText.T("pdisp.msg.start", m.workerName, def.Name, m.zoneName));
        }
        catch { }

        Plugin.LogInfo("pdispatch start id=" + m.missionId + " def=" + def.Id
            + " worker=" + m.uidChara + " zone=" + m.uidZone);
        try { Save(); } catch { }
        return null;
    }

    /// <summary>
    /// Recall a posting: pays every settled whole month in one parcel and sends
    /// the worker home. Mid-month recall still pays the whole months that
    /// passed; the partial month is lost.
    /// </summary>
    internal static bool TryRecall(int missionId)
    {
        PermanentDispatchMission? m = FindByMissionId(missionId);
        if (m == null)
        {
            return false;
        }

        SettleOut(m, recalled: true);
        return true;
    }

    /// <summary>Journal "放弃" on a posting: same thing as recalling it.</summary>
    internal static void Cancel(string defId)
    {
        PermanentDispatchMission? m = FindByDef(defId);
        if (m != null)
        {
            SettleOut(m, recalled: true);
        }
    }

    /// <summary>
    /// Raw date of the next whole-month settlement (vanilla month = 43200 min).
    /// Used as the quest deadline so the board/journal 剩余 column counts down to
    /// the next payout instead of a meaningless "permanent".
    /// </summary>
    internal static int NextMonthRaw()
    {
        try
        {
            int raw = EClass.world?.date?.GetRaw() ?? 0;
            return raw + (43200 - (raw % 43200));
        }
        catch
        {
            return 0;
        }
    }

    internal static int HoursToNextMonth()
    {
        try
        {
            int h = (NextMonthRaw() - (EClass.world?.date?.GetRaw() ?? 0)) / 60;
            return Math.Max(1, h);
        }
        catch
        {
            return 1;
        }
    }

    /// <summary>Hourly tick. Only fires month boundaries; cheap on ordinary hours.</summary>
    internal static void OnSimulateHour()
    {
        if (Missions.Count == 0)
        {
            return;
        }

        int now = CurrentMonthIndex();
        bool changed = false;
        var snapshot = Missions.ToList();
        foreach (PermanentDispatchMission m in snapshot)
        {
            try
            {
                if (!Missions.Contains(m))
                {
                    continue;
                }

                Chara? c = null;
                try { c = RefChara.Get(m.uidChara); } catch { }
                if (c == null || c.isDead)
                {
                    // Worker died mid-posting: whole months already settled still pay.
                    Plugin.LogInfo("pdispatch worker gone id=" + m.missionId);
                    SettleOut(m, recalled: false);
                    changed = true;
                    continue;
                }

                // Keep the NPC physically in its post town.
                MoveToPost(m, c);
                PrepareDispatchedChara(c);

                while (m.settledThrough < now)
                {
                    m.settledThrough++;
                    m.monthsPaid++;
                    CreditMonthly(m, c);
                    changed = true;
                }
            }
            catch (Exception ex)
            {
                Plugin.LogWarn("pdispatch hour tick: " + ex.Message);
            }
        }

        if (changed)
        {
            _hourSavePending = true;
        }
    }

    /// <summary>Player entered a zone: pull posted NPCs back onto their post map.</summary>
    internal static void OnZoneEntered(Zone zone)
    {
        if (zone == null || Missions.Count == 0)
        {
            return;
        }

        foreach (PermanentDispatchMission m in Missions)
        {
            try
            {
                if (m.uidZone != zone.uid)
                {
                    continue;
                }

                Chara? c = null;
                try { c = RefChara.Get(m.uidChara); } catch { }
                if (c == null || c.isDead)
                {
                    continue;
                }

                MoveToPost(m, c);
                PrepareDispatchedChara(c);
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("pdispatch zone enter: " + ex.Message);
            }
        }
    }

    internal static void OnWorkerDied(Chara c)
    {
        if (c == null)
        {
            return;
        }

        PermanentDispatchMission? m = FindByChara(c.uid);
        if (m == null)
        {
            return;
        }

        SettleOut(m, recalled: false);
    }

    static void MoveToPost(PermanentDispatchMission m, Chara c)
    {
        try
        {
            bool onPost = c.currentZone != null && c.currentZone.uid == m.uidZone;
            if (onPost)
            {
                return;
            }

            Zone? target = PermanentDispatchDef.ResolveZone(m.zoneId);
            if (target == null)
            {
                return;
            }

            m.uidZone = target.uid;
            try
            {
                c.MoveZone(target, ZoneTransition.EnterState.RandomVisit);
            }
            catch
            {
                c.MoveZone(target);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("pdispatch move: " + ex.Message);
        }
    }

    internal static int CurrentMonthIndex()
    {
        try
        {
            int month = 1;
            int year = 0;
            try { month = EClass.world?.date?.month ?? 1; } catch { month = 1; }
            try { year = EClass.world?.date?.year ?? 0; } catch { year = 0; }
            return year * 12 + Math.Max(0, month - 1);
        }
        catch
        {
            return 0;
        }
    }

    static string SafeZoneName(Zone? z)
    {
        try { return z?.Name ?? z?.id ?? ""; } catch { return ""; }
    }

    /// <summary>
    /// Same flag set the dungeon dispatch uses: friendly, parked, no resident
    /// talk tree (dialog is blocked by patch with a busy message instead).
    /// </summary>
    internal static void PrepareDispatchedChara(Chara c)
    {
        if (c == null)
        {
            return;
        }

        try { c.SetHostility(Hostility.Friend); } catch { }
        try { c.noMove = true; } catch { }
        try { c.enemy = null; } catch { }
        try { c.isRestrained = true; } catch { }
        try { c.SetAIIdle(); } catch { }
    }

    internal static void ClearDispatchedFlags(Chara? c)
    {
        if (c == null)
        {
            return;
        }

        try { c.noMove = false; } catch { }
        try { c.isRestrained = false; } catch { }
        try { c.enemy = null; } catch { }
        try { c.SetAIIdle(); } catch { }
    }
}
