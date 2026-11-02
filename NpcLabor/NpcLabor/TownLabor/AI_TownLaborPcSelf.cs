using System.Collections.Generic;

namespace NpcLabor.TownLabor;

/// <summary>
/// PC self-work: walk to the client, then stand beside them while the mission
/// consumes in-game hours (like resting) — SP drains hourly and the PC stays
/// visibly working next to the shopkeeper. Manual cancel / failed path aborts.
/// </summary>
internal class AI_TownLaborPcSelf : AIAct
{
    internal const int ArriveDist = 1;
    internal const int WorkRadius = 2;

    internal int missionId;

    bool _finishedOk;
    int _pathFailStreak;

    public override bool CanManualCancel() => true;

    public override bool CancelWhenDamaged => true;

    // Turbo time like rest/craft so self-work hours do not crawl.
    public override bool UseTurbo => true;

    public override bool InformCancel => true;

    public override void OnCancel()
    {
        if (_finishedOk)
        {
            return;
        }

        TownLaborManager.NotifyPcSelfApproachCancelled(missionId);
    }

    public override IEnumerable<Status> Run()
    {
        TownLaborMission? m = missionId > 0
            ? TownLaborManager.FindByMissionId(missionId)
            : null;

        if (m == null || !m.isPcSelf || owner == null || owner.isDead)
        {
            yield return Cancel();
            yield break;
        }

        // Already working (save/load or re-entry): stay in work phase.
        if (m.workStarted)
        {
            foreach (Status s in WorkPhase())
            {
                yield return s;
            }

            yield break;
        }

        Chara? client = m.GetClient();
        if (client == null || client.isDead)
        {
            TownLaborManager.NotifyPcSelfApproachCancelled(missionId);
            yield return Cancel();
            yield break;
        }

        bool sameMap = false;
        try
        {
            sameMap = owner.ExistsOnMap
                && client.ExistsOnMap
                && EClass._zone != null
                && EClass._zone.uid == m.uidZone;
        }
        catch
        {
            sameMap = false;
        }

        if (!sameMap)
        {
            // Cannot walk off-map; keep mission in approach phase for later re-entry.
            yield return Success();
            yield break;
        }

        int dist = 99;
        try { dist = owner.Dist(client); } catch { dist = 99; }

        if (dist > ArriveDist)
        {
            yield return DoGoto(client, ArriveDist);

            if (owner == null || owner.isDead)
            {
                TownLaborManager.NotifyPcSelfApproachCancelled(missionId);
                yield return Cancel();
                yield break;
            }

            // Mission may have been settled while walking.
            m = TownLaborManager.FindByMissionId(missionId);
            if (m == null || !m.isPcSelf)
            {
                _finishedOk = true;
                yield return Success();
                yield break;
            }

            client = m.GetClient();
            if (client == null || client.isDead || !client.ExistsOnMap || !owner.ExistsOnMap)
            {
                TownLaborManager.NotifyPcSelfApproachCancelled(missionId);
                yield return Cancel();
                yield break;
            }

            try { dist = owner.Dist(client); } catch { dist = 99; }
            if (dist > ArriveDist + 1)
            {
                // Path blocked / stuck: soft snap once, then fail if still too far.
                try { TownLaborManager.PlaceWorkerNearClient(owner, client, force: true); } catch { }
                try { dist = owner.Dist(client); } catch { dist = 99; }
                if (dist > ArriveDist + 2)
                {
                    TownLaborManager.NotifyPcSelfApproachCancelled(missionId);
                    yield return Cancel();
                    yield break;
                }
            }
        }

        try { owner.LookAt(client); } catch { }

        _finishedOk = true;
        TownLaborManager.NotifyPcSelfArrived(missionId);
        foreach (Status s in WorkPhase())
        {
            yield return s;
        }
    }

    /// <summary>
    /// Hold the PC beside the client while the mission ticks hours and drains SP.
    /// Ends when the mission settles (success/recall/fail) or the PC is gone.
    /// </summary>
    IEnumerable<Status> WorkPhase()
    {
        int fast = 0;
        int lastFrame = -1;
        _pathFailStreak = 0;
        while (true)
        {
            // Safety: never spin on zero-duration yields.
            int frame = UnityEngine.Time.frameCount;
            if (lastFrame > 0 && frame - lastFrame <= 1)
            {
                if (++fast > 600)
                {
                    yield return Cancel();
                    yield break;
                }
            }
            else
            {
                fast = 0;
            }

            lastFrame = frame;

            TownLaborMission? m = missionId > 0
                ? TownLaborManager.FindByMissionId(missionId)
                : null;
            if (m == null || !m.isPcSelf || !m.workStarted || owner == null || owner.isDead)
            {
                _finishedOk = true;
                yield return Success();
                yield break;
            }

            Chara? client = m.GetClient();
            bool sameMap = false;
            try
            {
                sameMap = owner.ExistsOnMap
                    && client != null
                    && client.ExistsOnMap
                    && EClass._zone != null
                    && EClass._zone.uid == m.uidZone;
            }
            catch
            {
                sameMap = false;
            }

            if (sameMap && client != null)
            {
                int dist = 99;
                try { dist = owner.Dist(client); } catch { dist = 99; }
                if (dist > WorkRadius)
                {
                    yield return DoGoto(client, WorkRadius - 1);
                    if (owner == null || owner.isDead)
                    {
                        TownLaborManager.NotifyPcSelfApproachCancelled(missionId);
                        yield return Cancel();
                        yield break;
                    }

                    try { dist = owner.Dist(client); } catch { dist = 99; }
                    if (dist > WorkRadius)
                    {
                        _pathFailStreak++;
                        if (_pathFailStreak >= 2)
                        {
                            try { TownLaborManager.PlaceWorkerNearClient(owner, client, force: true); } catch { }
                            _pathFailStreak = 0;
                        }
                    }
                    else
                    {
                        _pathFailStreak = 0;
                    }
                }
                else
                {
                    _pathFailStreak = 0;
                }

                try { owner.LookAt(client); } catch { }
            }

            try
            {
                owner.ShowEmo(m.Def?.WorkEmo ?? Emo.happy);
            }
            catch
            {
            }

            yield return DoWait(40 + EClass.rnd(30));
        }
    }
}
