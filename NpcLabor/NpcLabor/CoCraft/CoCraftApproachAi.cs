using System.Collections.Generic;
using UnityEngine;

namespace NpcLabor.CoCraft;

/// <summary>
/// Co-craft approach AI: the assistant walks to the PC (no teleport), then stands
/// in place beside them for the whole craft. If the path is blocked or they cannot
/// arrive in time, SnapAssistantToPc is used as a last-resort fallback. Replaced
/// with NoGoal on Clear; a player command replaces it too and OnCancel frees movement.
///
/// Walk is ticked as a local AI_Goto, not SetChild. Vanilla AIAct.Tick only advances
/// a running child, which would freeze this enumerator and skip the stuck/timeout snap.
/// </summary>
internal class CoCraftApproachAi : AIAct
{
    /// <summary>Frames without closing distance (~4s at 60fps) before snapping beside the PC.</summary>
    const int StuckFrames = 240;

    AI_Goto? _walk;

    public override bool CancelWhenDamaged => false;

    public override bool CanManualCancel() => false;

    public override void OnCancel()
    {
        StopWalk();
        // Player command or Clear replaced us — stop pinning the NPC in place.
        try
        {
            if (owner != null)
            {
                owner.noMove = false;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("CoCraftApproachAi.cs silent catch: " + __e.Message); }
    }

    public override void OnReset()
    {
        StopWalk();
    }

    void StopWalk()
    {
        try { _walk?.Reset(); } catch { }
        _walk = null;
    }

    public override IEnumerable<Status> Run()
    {
        int lastDist = 99;
        int lastProgressFrame = Time.frameCount;
        while (CoCraftSession.Active && !CoCraftSession.ApproachExpired())
        {
            Chara? pc = EClass.pc;
            if (owner == null || owner.isDead || pc == null || !pc.ExistsOnMap)
            {
                yield break;
            }

            if (owner.ExistsOnMap && pc.pos != null && owner.pos.Equals(pc.pos))
            {
                break; // already standing in the PC's cell
            }

            int dist = owner.ExistsOnMap ? owner.Dist(pc) : 99;
            if (dist < lastDist)
            {
                lastDist = dist;
                lastProgressFrame = Time.frameCount;
            }
            else if (Time.frameCount - lastProgressFrame > StuckFrames)
            {
                break; // path blocked — snap fallback below
            }

            // Walk into the PC's own cell (shared tile) so the PC's surrounding
            // furniture/characters can never block the assistant. Two steps per
            // tick — the assistant walks over twice as fast.
            if (_walk == null || !_walk.IsRunning)
            {
                StopWalk();
                _walk = new AI_Goto(pc, 0);
                _walk.SetOwner(owner);
            }

            _walk.Tick();
            if (_walk != null && _walk.IsRunning)
            {
                _walk.Tick();
            }

            yield return Status.Running;
        }

        StopWalk();

        if (CoCraftSession.Active && owner != null && !owner.isDead)
        {
            if (!(owner.ExistsOnMap && EClass.pc != null && EClass.pc.pos != null
                && owner.pos.Equals(EClass.pc.pos)))
            {
                // Blocked / too slow: snap into the PC's cell as a last resort.
                CoCraftSession.SnapAssistantToPc(owner);
            }

            // Stand in place until the craft ends.
            try { owner.noMove = true; } catch { }
            while (CoCraftSession.Active)
            {
                yield return Status.Running;
            }

            try { if (owner != null) owner.noMove = false; } catch { }
        }

        yield break;
    }
}
