using System.Collections.Generic;
using UnityEngine;

namespace NpcLabor.TownLabor;

/// <summary>
/// Cosmetic shop-work AI: idle around the shop, no tight leash on the client tile.
/// Avoids DoGoto thrash that causes jitter when PC stands near the worker.
/// </summary>
internal class AI_TownLabor : AIAct
{
    // Soft work radius: only walk if far away; never hard-teleport from AI.
    internal const int ComfortRadius = 8;

    // Arrival radius: the worker walks to ~2 tiles of the client before the
    // first work emote (walk-to-work, mirrors AI_TownLaborPcSelf.ArriveDist).
    internal const int ArriveDist = 2;

    // Safety counters: never let the cosmetic AI spin or chase forever.
    internal const int MaxChaseAttempts = 12;
    internal const int MaxFastIterations = 600;

    // DoGoto retries before a blocked approach gets one soft snap.
    internal const int MaxApproachTries = 3;

    internal int missionId;
    int _chaseAttempts;
    int _fastIterations;
    int _lastFrame = -1;
    bool _approached;
    int _approachTries;

    public override bool CanManualCancel() => false;

    public override bool CancelWhenDamaged => false;

    public override bool IsIdle => true;

    public override IEnumerable<Status> Run()
    {
        while (true)
        {
            // Detect zero-duration loop spins (e.g. a yielded action that never
            // actually waited): if iterations stop advancing frames, cancel.
            int frame = Time.frameCount;
            if (_lastFrame > 0 && frame - _lastFrame <= 1)
            {
                _fastIterations++;
                if (_fastIterations > MaxFastIterations)
                {
                    yield return Cancel();
                    yield break;
                }
            }
            else
            {
                _fastIterations = 0;
            }

            _lastFrame = frame;

            TownLaborMission? m = missionId > 0
                ? TownLaborManager.FindByMissionId(missionId)
                : (owner != null ? TownLaborManager.FindByWorker(owner.uid) : null);

            if (m == null || owner == null || owner.isDead)
            {
                yield return Success();
                yield break;
            }

            if (m.uidWorker != owner.uid)
            {
                yield return Success();
                yield break;
            }

            bool offMap = false;
            try
            {
                offMap = EClass._zone == null || EClass._zone.uid != m.uidZone || !owner.ExistsOnMap;
            }
            catch
            {
                offMap = true;
            }

            if (offMap)
            {
                yield return DoWait(60 + EClass.rnd(40));
                continue;
            }

            Chara? client = m.GetClient();
            TownLaborJobDef? def = m.Def;

            // Walk-to-work: approach the client once before any work emote.
            // No teleport on start - mirrors AI_TownLaborPcSelf. Only when the
            // path stays blocked after a few DoGoto tries do we soft-snap once,
            // then work in place either way (companion missions are never
            // cancelled just because the route is crowded).
            if (!_approached)
            {
                if (client != null && client.ExistsOnMap && owner.ExistsOnMap)
                {
                    int d = 99;
                    try { d = owner.Dist(client); } catch { d = 99; }
                    if (d > ArriveDist)
                    {
                        if (_approachTries < MaxApproachTries)
                        {
                            _approachTries++;
                            yield return DoGoto(client, ArriveDist);
                            if (owner == null || owner.isDead)
                            {
                                yield return Cancel();
                                yield break;
                            }

                            continue;
                        }

                        // Blocked repeatedly: one soft snap, then work in place.
                        try { TownLaborManager.PlaceWorkerNearClient(owner, client, force: true); } catch { }
                    }
                }

                _approached = true;
            }

            if (client != null && client.ExistsOnMap && owner.ExistsOnMap)
            {
                int dist = 99;
                try { dist = owner.Dist(client); } catch { }

                // Wide comfort zone: only gently walk closer when quite far.
                if (dist > ComfortRadius && _chaseAttempts < MaxChaseAttempts)
                {
                    yield return DoGoto(client, ComfortRadius - 2);
                    _chaseAttempts++;
                    if (owner == null || owner.isDead)
                    {
                        yield return Cancel();
                        yield break;
                    }
                }
                else if (dist <= ComfortRadius)
                {
                    // Close enough; reset chase budget.
                    _chaseAttempts = 0;
                }

                try { owner.LookAt(client); } catch { }
            }

            try
            {
                owner.ShowEmo(def?.WorkEmo ?? Emo.happy);
            }
            catch
            {
            }

            yield return DoWait(80 + EClass.rnd(60));
        }
    }
}
