using System.Collections.Generic;
using UnityEngine;

namespace NpcLabor.Process;

/// <summary>
/// One processor job = one machine running one batch job. Instance state carved out of the
/// old all-static ProcessorJobSession so several machines can each run their own job.
///
/// Runtime-only; not saved. Ingredients park on THIS job's machine tile. Every field that
/// used to be a ProcessorJobSession static now lives here, in particular the refs/lists that
/// must never be shared across jobs (CrafterRef / MachineRef / Ingredients / IngredientMarks).
/// </summary>
internal sealed class ProcessorJob
{
    internal int JobToken;
    /// <summary>Registry key: the machine this job owns. One job per machine, structurally.</summary>
    internal int MachineUid;
    internal int NpcUid;
    /// <summary>Zone where machine + parked ings live. Catch-up only runs when PC re-enters this zone.</summary>
    internal int WorkZoneUid;
    internal int SkillId;
    internal int NpcSkill;
    internal int Remaining;
    internal int Completed;
    internal int ExpGranted;
    internal string? NpcName;
    internal string? MachineName;
    internal string LastClearReason = "";
    internal bool Active;
    /// <summary>Resident job held while PC is away from the work map. No live AI / no hour sim.</summary>
    internal bool Suspended;
    /// <summary>True while Clear/refund is moving ingredients back onto the map.</summary>
    internal bool IsReturningIngredients;
    /// <summary>Party companions leave party for the job so AI is free, then rejoin on Clear.</summary>
    internal bool WasPartyMember;
    /// <summary>Last observed live craft duration (after NPC half-cut). Used for return catch-up.</summary>
    internal int LastDuration;
    /// <summary>Last observed SP cost per craft (after NPC reduction; SP not drained for NPC jobs).</summary>
    internal int LastCostSp;
    /// <summary>world.date.GetRaw() when the job was suspended (PC left).</summary>
    internal int SuspendedAtRaw;
    /// <summary>Ignore freestanding AI cancel for a few frames after resume SetAI.</summary>
    internal int ResumeIgnoreCancelUntilFrame;
    /// <summary>Bounded retries when the freestanding process AI is cancelled on the work zone. Resets on job start / clear / successful craft.</summary>
    internal int AiRestartBudget = 3;
    /// <summary>Frame the last "leftovers went elsewhere" note was shown on, so one batch says it once.</summary>
    internal int LeftoverNoteFrame = -1;

    // Strong refs: installed furniture is not always found via map uid lookup alone.
    internal TraitCrafter? CrafterRef;
    internal Card? MachineRef;

    // Live ingredient stacks for THIS job. After zone unload these refs go stale -
    // rebind from IngredientMarks before any catch-up / resume craft.
    internal readonly List<Thing> Ingredients = new List<Thing>();
    /// <summary>Stable identity for parked ings across map unload/reload. Per-job so machine A never rebinds onto machine B's tile.</summary>
    internal readonly List<IngredientMark> IngredientMarks = new List<IngredientMark>();

    internal bool IsHeld => Active && NpcUid != 0;
}

/// <summary>Stable identity for a parked ingredient stack across zone unload/reload.</summary>
internal struct IngredientMark
{
    internal int Uid;
    internal string Id;
    internal int MaterialId;

    internal static IngredientMark From(Thing t)
    {
        IngredientMark m = default;
        if (t == null)
        {
            return m;
        }

        try { m.Uid = t.uid; } catch { m.Uid = 0; }
        try { m.Id = t.id ?? ""; } catch { m.Id = ""; }
        try { m.MaterialId = t.material != null ? t.material.id : t.idMaterial; } catch { m.MaterialId = 0; }
        return m;
    }
}
