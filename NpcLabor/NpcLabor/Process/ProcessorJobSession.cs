using System.Collections.Generic;
using UnityEngine;

namespace NpcLabor.Process;

/// <summary>
/// Processor outsourcing jobs (slice B). Runtime-only; not saved.
///
/// IMPORTANT (multi-job migration, S1): this class is being turned into a registry/facade over
/// <see cref="ProcessorJob"/>. In S1 it still holds exactly ONE job so behavior is byte-identical
/// to the old all-static version. S2..S7 move the registry to many jobs, one per machine.
///
/// Ingredients park on the machine tile. PC leave: party worker Clears; resident
/// job is held (Suspended) with SuspendedAtRaw recorded — no hour-tick craft while
/// away. On return to WorkZoneUid: compute finished crafts from elapsed game minutes,
/// spawn products for that count at half away speed, then resume freestanding AI if Remaining > 0.
/// </summary>
internal static class ProcessorJobSession
{
    internal const float NpcSpPerSkill = 0.25f;
    /// <summary>
    /// Catch-up model: one AI progress step ~= one game minute (GameDate raw unit).
    /// Live craft uses Progress_Custom duration with interval 2; away catch-up is a
    /// coarse estimate (duration minutes per craft), not a full anime sim.
    /// </summary>
    internal const int MinutesPerProgress = 1;
    /// <summary>Away catch-up only: half on-map rate (double minutes per craft).</summary>
    internal const int AwaySpeedDivisor = 2;

    // ---- Registry --------------------------------------------------------------
    // One job per machine, structurally. Multiple jobs may be live at once (S3 lifted
    // the old one-job cap); machinery always takes an explicit ProcessorJob.
    internal static readonly List<ProcessorJob> Jobs = new List<ProcessorJob>();

    internal static ProcessorJob? ForMachine(int machineUid)
    {
        if (machineUid == 0)
        {
            return null;
        }

        for (int i = 0; i < Jobs.Count; i++)
        {
            if (Jobs[i].MachineUid == machineUid)
            {
                return Jobs[i];
            }
        }
        return null;
    }

    internal static ProcessorJob? ForWorker(int npcUid, int exceptJobToken = 0)
    {
        if (npcUid == 0)
        {
            return null;
        }

        for (int i = 0; i < Jobs.Count; i++)
        {
            ProcessorJob j = Jobs[i];
            if (j.NpcUid == npcUid && j.IsHeld && j.JobToken != exceptJobToken)
            {
                return j;
            }
        }
        return null;
    }

    /// <summary>True when this machine already has a job (any job, held or not).</summary>
    internal static bool IsMachineBusy(int machineUid)
    {
        return ForMachine(machineUid) != null;
    }

    /// <summary>True when this worker is already held by a job.</summary>
    internal static bool IsWorkerBusy(int npcUid, int exceptJobToken = 0)
    {
        return ForWorker(npcUid, exceptJobToken) != null;
    }

    /// <summary>Emit a new job into the registry. Caller must have checked busy gates.</summary>
    internal static ProcessorJob Register()
    {
        ProcessorJob job = new ProcessorJob();
        // Monotonic unique token: the AI's only handle back to its job. Never derived
        // from uid^uid^count, which can collide across two concurrent machines.
        job.JobToken = ++_nextJobToken;
        if (_nextJobToken == int.MaxValue)
        {
            _nextJobToken = 0; // wrap; collisions are astronomically unlikely in one session
        }
        Jobs.Add(job);
        return job;
    }

    static int _nextJobToken;

    /// <summary>Drop a job from the registry.</summary>
    internal static void Unregister(ProcessorJob job)
    {
        if (job != null)
        {
            Jobs.Remove(job);
        }
    }

    internal static int Count => Jobs.Count;

    /// <summary>True when every tracked job is suspended (nothing is live to hold on leave).</summary>
    internal static bool AllSuspended
    {
        get
        {
            for (int i = 0; i < Jobs.Count; i++)
            {
                if (!Jobs[i].Suspended)
                {
                    return false;
                }
            }
            return Jobs.Count > 0;
        }
    }

    /// <summary>Resolve the job an AI instance owns, by its jobToken. Null if it was cleared.</summary>
    internal static ProcessorJob? Resolve(int jobToken)
    {
        if (jobToken == 0)
        {
            return null;
        }

        for (int i = 0; i < Jobs.Count; i++)
        {
            if (Jobs[i].JobToken == jobToken)
            {
                return Jobs[i];
            }
        }
        return null;
    }

    internal static Chara? GetWorker(ProcessorJob job)
    {
        if (job == null || !job.Active || job.NpcUid == 0)
        {
            return null;
        }

        Chara? c = RefChara.Get(job.NpcUid);
        if (c == null || c.isDead)
        {
            return null;
        }

        // Suspended jobs may keep the worker on an unloaded home map; only require
        // live-in-current-zone when freestanding AI should actually run.
        if (!job.Suspended)
        {
            try
            {
                if (!c.IsAliveInCurrentZone)
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        return c;
    }

    internal static TraitCrafter? GetCrafter(ProcessorJob job)
    {
        if (job == null || !job.Active)
        {
            return null;
        }

        if (job.CrafterRef != null)
        {
            Card? owner = job.CrafterRef.owner;
            if (owner != null && !owner.isDestroyed)
            {
                job.MachineRef = owner;
                return job.CrafterRef;
            }
        }

        Card? card = GetMachineCard(job);
        TraitCrafter? trait = card?.trait as TraitCrafter;
        if (trait != null)
        {
            job.CrafterRef = trait;
        }

        return trait;
    }

    internal static Card? GetMachineCard(ProcessorJob job)
    {
        if (job == null)
        {
            return null;
        }

        if (job.MachineRef != null && !job.MachineRef.isDestroyed)
        {
            return job.MachineRef;
        }

        if (job.CrafterRef != null)
        {
            Card? owner = job.CrafterRef.owner;
            if (owner != null && !owner.isDestroyed)
            {
                job.MachineRef = owner;
                return owner;
            }
        }

        if (job.MachineUid == 0)
        {
            return null;
        }

        Card? found = FindCardByUid(job.MachineUid);
        if (found != null)
        {
            job.MachineRef = found;
            if (found.trait is TraitCrafter trait)
            {
                job.CrafterRef = trait;
            }
        }

        return found;
    }

    static Card? FindCardByUid(int uid)
    {
        if (uid == 0 || EClass._map == null)
        {
            return null;
        }

        try
        {
            Thing? t = EClass._map.FindThing(uid);
            if (t != null)
            {
                return t;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("ProcessorJobSession.cs silent catch: " + __e.Message); }

        try
        {
            if (EClass._map.things != null)
            {
                foreach (Thing t in EClass._map.things)
                {
                    if (t != null && t.uid == uid)
                    {
                        return t;
                    }
                }
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("ProcessorJobSession.cs silent catch: " + __e.Message); }

        // Installed furniture is held via CrafterRef/MachineRef at job start.
        // Do not walk Installed by uid: PropsInstalled.Find is id-based, not uid-based.
        return null;
    }

    internal static bool TryStart(
        Chara worker,
        TraitCrafter crafter,
        List<Thing> ings,
        int count,
        bool autoPicked = false)
    {
        if (worker == null || crafter == null || ings == null || ings.Count == 0 || count <= 0)
        {
            return false;
        }

        if (!ProcessorWhitelist.IsSupported(crafter))
        {
            Plugin.LogWarn("processor start rejected: unsupported crafter " + crafter.GetType().Name);
            return false;
        }

        if (crafter.owner == null || crafter.owner.isDestroyed)
        {
            return false;
        }

        // Gate 1 (landmine #1): one job per machine, structurally.
        int machineUid = crafter.owner.uid;
        if (IsMachineBusy(machineUid))
        {
            Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.busy"));
            return false;
        }

        // Gate 2 (landmine #2): a worker cannot be held by two jobs at once.
        // The two entry paths have different reasons: auto-pick found nobody free
        // ("no worker"), while a manual assignment hit a worker already on another
        // machine ("X is working another machine"). Say the reason that is true.
        if (IsWorkerBusy(worker.uid))
        {
            Msg.SayRaw(autoPicked
                ? NpcLabor.LaborText.T("proc.msg.noWorker")
                : NpcLabor.LaborText.T("proc.ui.jobsWorkerBusy", CoCraft.AssistantResolver.NameOf(worker)));
            return false;
        }

        int skillId = ProcessorWhitelist.ResolveSkillId(crafter);
        int skill = skillId > 0 ? worker.Evalue(skillId) : 0;
        if (skillId > 0 && skill <= 0)
        {
            Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.cannotOperate", CoCraft.AssistantResolver.NameOf(worker)));
            return false;
        }

        ProcessorJob job = Register();

        // Claim ingredient stacks (split so PC inventory keeps remainder).
        job.Ingredients.Clear();
        job.IngredientMarks.Clear();
        for (int i = 0; i < ings.Count; i++)
        {
            Thing? src = ings[i];
            if (src == null || src.isDestroyed || src.Num <= 0)
            {
                ReturnIngredients(job);
                job.Ingredients.Clear();
                job.IngredientMarks.Clear();
                Unregister(job);
                return false;
            }

            // Park on the machine tile for the whole multi-batch job.
            // Do not hold bulk materials in PC inventory (overweight).
            if (!ParkIngredientOnMachine(src, crafter))
            {
                ReturnIngredients(job);
                job.Ingredients.Clear();
                job.IngredientMarks.Clear();
                Unregister(job);
                return false;
            }

            job.Ingredients.Add(src);
            job.IngredientMarks.Add(IngredientMark.From(src));
        }

        job.Active = true;
        job.NpcUid = worker.uid;
        job.MachineUid = crafter.owner.uid;
        job.CrafterRef = crafter;
        job.MachineRef = crafter.owner;
        job.SkillId = skillId;
        job.NpcSkill = skill;
        job.Remaining = count;
        job.Completed = 0;
        job.ExpGranted = 0;
        job.NpcName = CoCraft.AssistantResolver.NameOf(worker);
        job.MachineName = ProcessorWhitelist.DisplayName(crafter);
        job.LastClearReason = "";
        job.WasPartyMember = false;
        job.LastDuration = 0;
        job.LastCostSp = 0;
        job.Suspended = false;
        job.SuspendedAtRaw = 0;
        job.ResumeIgnoreCancelUntilFrame = 0;
        job.AiRestartBudget = 3;
        try
        {
            job.WorkZoneUid = EClass._zone != null ? EClass._zone.uid : 0;
        }
        catch
        {
            job.WorkZoneUid = 0;
        }

        // Party AI can keep companions glued to the PC; temporarily detach so they can work.
        // Order matters: vanilla Party.RemoveMember always clears c_wasInPcParty=false.
        // Set the flag AFTER RemoveMember so FactionBranch.OnAfterSimulate can auto-rejoin
        // if this runtime-only job is lost (crash / force quit) before Clear().
        // Same pattern as town labor; still no process-job save.
        try
        {
            bool inParty = worker.IsPCParty
                || (EClass.pc?.party?.members != null && EClass.pc.party.members.Contains(worker));
            if (inParty && EClass.pc?.party != null)
            {
                job.WasPartyMember = true;
                EClass.pc.party.RemoveMember(worker);
                try { worker.c_wasInPcParty = true; } catch { }
            }
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("processor leave party: " + ex.Message);
        }

        Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.start", job.NpcName, job.MachineName, job.Remaining));
        Plugin.LogInfo(
            $"processor start: npc={job.NpcName}#{job.NpcUid} machine={job.MachineName}#{job.MachineUid} " +
            $"skill={job.SkillId}/{job.NpcSkill} count={job.Remaining} wasParty={job.WasPartyMember}");

        try
        {
            // Immediate kick so the NPC leaves whatever idle/work goal they had.
            worker.SetAIImmediate(new AI_NpcProcess
            {
                jobToken = job.JobToken
            });
        }
        catch (System.Exception ex)
        {
            Plugin.LogWarn("SetAI AI_NpcProcess failed: " + ex.Message);
            Clear(job, "ai-set-fail", announce: false);
            return false;
        }

        // Belt-and-suspenders: SetAI already removes ConWait, but a wait order can
        // re-apply it between the kick and the next tick.
        try { worker.RemoveCondition<ConWait>(); } catch { }

        return true;
    }

    internal static int AdjustCostSp(ProcessorJob job, int baseSp)
    {
        if (job == null || !job.Active)
        {
            return baseSp;
        }

        int reduced = baseSp - Mathf.FloorToInt(job.NpcSkill * NpcSpPerSkill);
        return Mathf.Max(1, reduced);
    }

    /// <summary>
    /// NPC process duration from the skill-scaled base, scaled by the player's
    /// configured <b>speed</b> multiplier (slice B).
    ///
    /// The configured value is a speed multiplier, so the duration is the base
    /// <b>divided</b> by it: multiplier 2.0 -> half the duration (twice as fast),
    /// multiplier 0.5 -> double the duration. Writing this the other way round
    /// (<c>base * multiplier</c>) inverts the whole slider, and the inversion is
    /// invisible at any value of exactly 1.0 because <c>base * 1 == base / 1</c>
    /// (the shipped default is 0.25 since 2026-10-14, but a hand-edited json can
    /// still sit on 1.0).
    ///
    /// This is the single place the multiplier is applied: all four duration call
    /// sites in <c>AI_NpcProcess.ComputeDuration</c> and both
    /// <see cref="EstimateDuration"/> consumers (the job-list ETA and the off-map
    /// catch-up) route through here, so they can never disagree about the rate.
    /// </summary>
    internal static int ApplyNpcDurationCut(int baseDuration)
    {
        float multiplier = LaborConfig.NpcSpeedMultiplier;
        int duration = Mathf.Max(1, Mathf.FloorToInt(Mathf.Max(1, baseDuration) / multiplier));

        // Direction guard. At a value of exactly 1.0 this function is arithmetic-identical
        // to the inverted form, so a regression to `baseDuration * multiplier` would hide
        // itself on any config sitting at 1.0 (the shipped default is 0.25, but hand-edited
        // jsons land on 1.0 easily). The check only costs anything while debugging.
        UnityEngine.Debug.Assert(
            multiplier >= 1f ? duration <= baseDuration : duration >= baseDuration,
            "ApplyNpcDurationCut inverted: speed multiplier " + multiplier
            + " turned base duration " + baseDuration + " into " + duration
            + " (a speed multiplier must shorten the duration when raised)");

        return duration;
    }

    /// <summary>Cache live craft timing for estimates / debug.</summary>
    internal static void RememberCraftTiming(ProcessorJob job, int duration, int costSp)
    {
        if (job == null || !job.Active)
        {
            return;
        }

        if (duration > 0)
        {
            job.LastDuration = duration;
        }

        if (costSp > 0)
        {
            job.LastCostSp = costSp;
        }
    }

    internal static int EstimateDuration(ProcessorJob job)
    {
        if (job != null && job.LastDuration > 0)
        {
            return job.LastDuration;
        }

        int skill = Mathf.Max(0, job?.NpcSkill ?? 0);
        int raw = 10 * 100 / Mathf.Max(1, 80 + skill * 5);
        return ApplyNpcDurationCut(raw);
    }

    /// <summary>
    /// Drop the cached per-craft duration on every live job so the next
    /// <see cref="EstimateDuration"/> recomputes it at the new speed multiplier.
    ///
    /// <para>
    /// <c>LastDuration</c> caches the duration the <i>running</i> craft was started with.
    /// It is correct for the job it belongs to, but after the player drags the speed
    /// slider nothing recomputes it until that craft finishes - so the job-list ETA
    /// (and the off-map catch-up projection) would keep quoting the previous
    /// multiplier's timing for a whole craft. That is the one visible defect of letting
    /// the slider take effect mid-flight, so the config write path calls this.
    /// </para>
    ///
    /// <para>
    /// Deliberately <b>not</b> wired into the <c>LaborConfig.Process</c> getter/setter:
    /// the config screen reads those accessors every frame while it is open, and a
    /// read must not have side effects. Callers are the only writers of the multiplier
    /// (<see cref="ModConfigUi"/>), which keeps "the slider changed" and "the cache is
    /// stale" in one place.
    /// </para>
    ///
    /// <para>
    /// Only the estimate is dropped; the craft already in progress keeps its own duration
    /// (the change lands on the next item), which is what the slider is specified to do.
    /// </para>
    /// </summary>
    internal static void InvalidateDurationCache()
    {
        for (int i = 0; i < Jobs.Count; i++)
        {
            ProcessorJob job = Jobs[i];
            if (job != null)
            {
                job.LastDuration = 0;
            }
        }
    }

    static int CurrentRawDate()
    {
        try
        {
            return EClass.world?.date?.GetRaw() ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    internal static void NoteCompleted(ProcessorJob job, int exp)
    {
        if (job == null || !job.Active)
        {
            return;
        }

        job.Completed++;
        if (job.Remaining > 0)
        {
            job.Remaining--;
        }

        if (exp > 0)
        {
            job.ExpGranted += exp;
        }
    }

    /// <summary>True when the claimed ingredient stacks are still usable and sitting on a map cell, not inside the PC.</summary>
    internal static bool CanUseIngredientStacks(ProcessorJob job)
    {
        if (job == null)
        {
            return false;
        }

        List<Thing> ingredients = job.Ingredients;
        if (ingredients.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < ingredients.Count; i++)
        {
            Thing? t = ingredients[i];
            if (t == null || t.isDestroyed || t.Num <= 0)
            {
                return false;
            }

            try
            {
                if (!t.ExistsOnMap)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when any claimed ingredient stack has been picked up by the PC or otherwise moved off-map.</summary>
    internal static bool IsAnyIngredientHeldByPc(ProcessorJob job)
    {
        if (job == null)
        {
            return false;
        }

        List<Thing> ingredients = job.Ingredients;
        for (int i = 0; i < ingredients.Count; i++)
        {
            Thing? t = ingredients[i];
            if (t == null || t.isDestroyed || t.Num <= 0)
            {
                continue;
            }

            try
            {
                Card? root = t.GetRootCard();
                if (root != null && root.IsPC)
                {
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                if (!t.ExistsOnMap)
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Leftover parked stacks after Remaining hits 0 are refunded on Clear.
    /// Never refill Remaining from leftover Num — that duplicated last-unit crafts.
    /// </summary>
    internal static bool TryContinueLeftover(ProcessorJob job)
    {
        return job != null && job.Active && !job.Suspended && job.Remaining > 0;
    }

    /// <summary>
    /// Take one craft piece the same way vanilla AI_UseCrafter does:
    /// Split(1). Last unit returns the parked stack itself; do not Duplicate.
    /// </summary>
    internal static bool TryTakeCraftPiece(
        Thing src,
        TraitCrafter crafter,
        Card? machine,
        Chara worker,
        out Thing? piece)
    {
        piece = null;
        if (src == null || src.isDestroyed || src.Num <= 0 || worker == null)
        {
            return false;
        }

        bool consumeIng = true;
        try { consumeIng = crafter == null || crafter.IsConsumeIng; } catch { consumeIng = true; }
        try
        {
            piece = src.Split(1);
        }
        catch
        {
            piece = null;
        }

        if (piece == null || piece.isDestroyed)
        {
            piece = null;
            return false;
        }

        if (consumeIng && EClass._zone != null)
        {
            try
            {
                Point place = (machine != null && machine.ExistsOnMap)
                    ? machine.pos
                    : worker.pos;
                if (!piece.ExistsOnMap)
                {
                    Card card = EClass._zone.AddCard(piece, place);
                    try { card.altitude = (machine != null && machine.ExistsOnMap) ? 0 : 1; } catch { }
                    piece = card as Thing ?? piece;
                }

                PrepareCraftPiece(piece, crafter);
            }
            catch (System.Exception ex)
            {
                Plugin.LogDebug("take craft piece place: " + ex.Message);
                if (piece != null && !piece.isDestroyed && !ReferenceEquals(piece, src))
                {
                    try { piece.Destroy(); } catch { }
                }
                return false;
            }
        }
        else
        {
            PrepareCraftPiece(piece, crafter);
        }

        return piece != null && !piece.isDestroyed;
    }

    /// <summary>
    /// Destroy the Split work pieces, including last-unit parked stacks.
    /// Vanilla AI_UseCrafter does the same Destroy after Craft.
    /// </summary>
    internal static bool ConsumeCraftCycle(ProcessorJob job, List<Thing> ings)
    {
        if (ings == null || ings.Count == 0)
        {
            return false;
        }

        bool ok = true;
        for (int i = 0; i < ings.Count; i++)
        {
            Thing? piece = ings[i];
            if (piece == null || piece.isDestroyed)
            {
                ok = false;
                continue;
            }

            int before = 0;
            try { before = piece.Num; } catch { before = 0; }
            try { piece.Destroy(); }
            catch
            {
                ok = false;
                continue;
            }

            if (!piece.isDestroyed && piece.Num >= before && before > 0)
            {
                ok = false;
            }
        }

        SyncIngredientMarksAfterCraft(job);
        return ok;
    }

    internal static void RestartWorkerAi(ProcessorJob job, string reason)
    {
        if (job == null || !job.Active || job.Suspended)
        {
            return;
        }

        Chara? worker = GetWorker(job);
        if (worker == null || worker.isDead)
        {
            return;
        }

        try { job.ResumeIgnoreCancelUntilFrame = Time.frameCount + 3; } catch { job.ResumeIgnoreCancelUntilFrame = 0; }
        Plugin.LogInfo($"processor restart ai ({reason}) npc={job.NpcUid} left={job.Remaining}");
        try
        {
            worker.SetAIImmediate(new AI_NpcProcess
            {
                jobToken = job.JobToken
            });
        }
        catch (System.Exception ex)
        {
            Plugin.LogWarn("processor leftover SetAI failed: " + ex.Message);
        }

        try { worker.RemoveCondition<ConWait>(); } catch { }
    }

    internal static void ResetAiRestartBudget(ProcessorJob job) { if (job != null) job.AiRestartBudget = 3; }

    /// <summary>Clear every job (plugin teardown / PC death).</summary>
    internal static void ClearAll(string reason, bool announce = true)
    {
        // Snapshot: Clear unregisters, so iterate a copy.
        ProcessorJob[] snapshot = Jobs.ToArray();
        for (int i = 0; i < snapshot.Length; i++)
        {
            Clear(snapshot[i], reason, announce);
        }
    }

    internal static void Clear(ProcessorJob? job, string reason, bool announce = true)
    {
        if (job == null)
        {
            return;
        }

        if (!job.Active && job.NpcUid == 0)
        {
            if (job.Ingredients.Count == 0 && job.IngredientMarks.Count > 0)
            {
                try { TryRebindIngredients(job, GetMachineCard(job)); } catch { }
            }
            ReturnIngredients(job);
            job.Ingredients.Clear();
            job.IngredientMarks.Clear();
            Unregister(job);
            return;
        }

        job.LastClearReason = reason ?? "";
        if (announce && job.Active)
        {
            AnnounceFinish(job, reason);
        }

        Plugin.LogDebug(
            $"processor clear ({reason}) npc={job.NpcUid} done={job.Completed} left={job.Remaining} exp={job.ExpGranted}");

        // Snapshot before zeroing so party restore still finds the worker.
        int restoreUid = job.NpcUid;
        bool restoreParty = job.WasPartyMember;

        // After suspend, Ingredients were cleared; rebind from marks before refund.
        if (job.Ingredients.Count == 0 && job.IngredientMarks.Count > 0)
        {
            try { TryRebindIngredients(job, GetMachineCard(job)); } catch { }
        }

        // Return any leftover claimed ingredients still on map / held by NPC.
        ReturnIngredients(job);
        job.Ingredients.Clear();
        job.IngredientMarks.Clear();

        job.Active = false;
        job.Suspended = false;
        job.NpcUid = 0;
        job.MachineUid = 0;
        job.WorkZoneUid = 0;
        job.CrafterRef = null;
        job.MachineRef = null;
        job.SkillId = 0;
        job.NpcSkill = 0;
        job.Remaining = 0;
        job.Completed = 0;
        job.ExpGranted = 0;
        job.NpcName = null;
        job.MachineName = null;
        job.WasPartyMember = false;
        job.LastDuration = 0;
        job.LastCostSp = 0;
        job.SuspendedAtRaw = 0;
        job.ResumeIgnoreCancelUntilFrame = 0;
        job.AiRestartBudget = 3;

        Unregister(job);

        if (restoreParty && restoreUid > 0)
        {
            TryRestoreParty(restoreUid);
        }
    }

    static void TryRestoreParty(int uid)
    {
        try
        {
            Chara? worker = RefChara.Get(uid);
            if (worker == null || worker.isDead)
            {
                return;
            }

            Party? party = EClass.pc?.party;
            if (party == null)
            {
                // Keep flag so vanilla OnAfterSimulate can still heal later.
                try { worker.c_wasInPcParty = true; } catch { }
                return;
            }

            try
            {
                if (!worker.IsGlobal)
                {
                    worker.SetGlobal();
                }
            }
            catch (System.Exception ex)
            {
                Plugin.LogDebug("processor SetGlobal: " + ex.Message);
            }

            bool already = false;
            try
            {
                already = worker.IsPCParty
                    || worker.party == party
                    || (party.uidMembers != null && party.uidMembers.Contains(worker.uid));
            }
            catch
            {
                already = false;
            }

            bool rejoined = already;
            if (!already)
            {
                try
                {
                    try { worker.party = null; } catch { }
                    party.AddMemeber(worker, showMsg: false);
                }
                catch (System.Exception ex)
                {
                    Plugin.LogDebug("processor party rejoin: " + ex.Message);
                    try
                    {
                        if (party.uidMembers != null && !party.uidMembers.Contains(worker.uid))
                        {
                            party.uidMembers.Add(worker.uid);
                        }
                        try { party._members = null; } catch { }
                        try { worker.party = party; } catch { }
                    }
                    catch
                    {
                    }
                }

                try
                {
                    rejoined = worker.IsPCParty
                        || worker.party == party
                        || (party.uidMembers != null && party.uidMembers.Contains(worker.uid));
                }
                catch
                {
                    rejoined = false;
                }
            }

            // Only clear vanilla auto-rejoin flag once membership looks good.
            if (rejoined)
            {
                try { worker.c_wasInPcParty = false; } catch { }
            }
            else
            {
                try { worker.c_wasInPcParty = true; } catch { }
                Plugin.LogDebug("processor party restore incomplete uid=" + uid);
            }
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("processor party restore: " + ex.Message);
        }
    }

    /// <summary>
    /// Save/load reset. Slice B is runtime-only — jobs are never written into the
    /// save — but a load happens inside the same process, so this static registry
    /// survives it and keeps describing cards from the world that was just replaced
    /// (an older save then reads as: machine occupied, worker list still holding the
    /// previous worker). The loaded save is the authority, so drop everything here.
    ///
    /// <para>
    /// Deliberately NOT <see cref="ClearAll"/>: that path refunds ingredients, restores
    /// the party and announces, all of which act on the world. Parked stacks, party
    /// state and machine grid are whatever the save says now — moving anything risks
    /// dupes or lost items. Unregister only, touch nothing.
    /// </para>
    /// </summary>
    internal static void DropAllOnLoad()
    {
        if (Jobs.Count == 0)
        {
            return;
        }

        // Snapshot: Unregister mutates the list.
        ProcessorJob[] snapshot = Jobs.ToArray();
        for (int i = 0; i < snapshot.Length; i++)
        {
            Unregister(snapshot[i]);
        }

        Plugin.LogInfo($"processor registry dropped on world load: {snapshot.Length} job(s)");
    }

    /// <summary>
    /// Hand every leftover stack back. Stacks are placed, not dumped: see
    /// <see cref="ReturnOneIngredient"/>.
    /// </summary>
    internal static void ReturnIngredients(ProcessorJob? job)
    {
        // EClass.pc is a property whose getter dereferences EClass.core. Once the
        // game starts tearing itself down (ApplicationQuit -> OnDestroy) core is
        // already gone and the getter itself throws NullReferenceException, so a
        // plain `if (EClass.pc == null)` guard does not protect anything. Read it
        // inside a try and bail out when the world is already gone.
        Chara? pc;
        try
        {
            pc = EClass.pc;
        }
        catch
        {
            return;
        }

        if (pc == null)
        {
            return;
        }

        if (job == null || job.IsReturningIngredients)
        {
            return;
        }

        job.IsReturningIngredients = true;
        try
        {
            for (int i = 0; i < job.Ingredients.Count; i++)
            {
                Thing? t = job.Ingredients[i];
                if (t == null || t.isDestroyed || t.Num <= 0)
                {
                    continue;
                }

                try
                {
                    ReturnOneIngredient(t);
                }
                catch (System.Exception ex)
                {
                    Plugin.LogDebug("return ingredient failed: " + ex.Message);
                }
            }
        }
        finally
        {
            job.IsReturningIngredients = false;
        }
    }

    /// <summary>
    /// Park a claimed ingredient stack on the machine tile for the job lifetime.
    /// Visible on the machine; ignoreAutoPick so bulk is not auto-looted mid-job.
    /// </summary>
    internal static bool ParkIngredientOnMachine(Thing piece, TraitCrafter crafter)
    {
        if (piece == null || piece.isDestroyed || piece.Num <= 0)
        {
            return false;
        }
        Card? machine = crafter?.owner;
        if (machine == null || machine.isDestroyed || EClass._zone == null)
        {
            return false;
        }
        try
        {
            // Already sitting on the machine cell — just re-mark parking flags.
            if (piece.ExistsOnMap
                && machine.ExistsOnMap
                && piece.pos != null
                && machine.pos != null
                && piece.pos.Equals(machine.pos))
            {
                ApplyParkFlags(piece);
                return true;
            }
            Point? place = machine.ExistsOnMap
                ? machine.pos
                : (EClass.pc != null ? EClass.pc.pos : null);
            if (place == null)
            {
                return false;
            }
            Card card = EClass._zone.AddCard(piece, place);
            if (card == null || card.isDestroyed)
            {
                return false;
            }
            try
            {
                card.altitude = machine.ExistsOnMap ? 0 : 1;
            }
            catch
            {
            }
            ApplyParkFlags(card as Thing ?? piece);
            return true;
        }
        catch (System.Exception ex)
        {
            Plugin.LogWarn("park ingredient on machine failed: " + ex.Message);
            return false;
        }
    }
    internal static void ApplyParkFlags(Thing t)
    {
        if (t == null || t.isDestroyed)
        {
            return;
        }
        // Never hide parked bulk. isHidden makes stacks look deleted and confuses
        // map/inventory scans; ignoreAutoPick alone is enough to stop auto-loot.
        try { t.isHidden = false; } catch { } // heal older builds that hid bulk
        try { t.ignoreAutoPick = true; } catch { }
    }
    internal static void ClearParkFlags(Thing t)
    {
        if (t == null || t.isDestroyed)
        {
            return;
        }
        try { t.isHidden = false; } catch { } // heal older builds that hid bulk
        try { t.ignoreAutoPick = false; } catch { }
    }
    /// <summary>
    /// After Split for one craft cycle: keep microwave pieces hidden; show others for anime.
    /// </summary>
    internal static void PrepareCraftPiece(Thing piece, TraitCrafter crafter)
    {
        if (piece == null || piece.isDestroyed)
        {
            return;
        }
        try
        {
            // Duplicate() copies bit flags from the parked bulk stack.
            bool hide = crafter != null && crafter.animeType == TraitCrafter.AnimeType.Microwave;
            piece.isHidden = hide;
        }
        catch
        {
        }
        try { piece.ignoreAutoPick = true; } catch { }
    }
    /// <summary>
    /// Hand one leftover stack back — never by burying the player under it.
    /// Whatever is left goes back onto the machine tile as one stack, with auto-pick
    /// left off so an idle player cannot vacuum the pile straight back into the fatal
    /// overload this used to cause.
    /// </summary>
    internal static void ReturnOneIngredient(Thing t, ProcessorJob? job = null)
    {
        if (t == null || t.isDestroyed || t.Num <= 0)
        {
            return;
        }

        ClearParkFlags(t);

        try
        {
            Card? root = t.GetRootCard();
            if (root != null && root.IsPC)
            {
                return;
            }
        }
        catch
        {
        }

        if (DropBeside(t, WorkPoint(job)))
        {
            NoteLeftover(job);
            return;
        }

        // No floor to put it on at all: the pack is the only place it will not vanish.
        try
        {
            EClass.pc?.Pick(t);
            Plugin.LogWarn("processor leftover went to the pack: nowhere else to put it");
        }
        catch (System.Exception ex)
        {
            Plugin.LogWarn("processor return lost: " + ex.Message);
        }
    }

    /// <summary>Where the job actually happens: the machine, else the worker, else the player.</summary>
    static Point? WorkPoint(ProcessorJob? job)
    {
        try
        {
            Card? machine = GetMachineCard(job);
            if (machine != null && machine.ExistsOnMap && machine.pos != null)
            {
                return machine.pos;
            }
        }
        catch
        {
        }

        try
        {
            Chara? worker = GetWorker(job);
            if (worker != null && worker.ExistsOnMap && worker.pos != null)
            {
                return worker.pos;
            }
        }
        catch
        {
        }

        try
        {
            if (EClass.pc != null && EClass.pc.ExistsOnMap)
            {
                return EClass.pc.pos;
            }
        }
        catch
        {
        }

        return null;
    }

    /// <summary>
    /// Leave the stack on the floor at the work spot (the machine tile, or the worker's
    /// feet if the machine is gone). Auto-pick stays off: letting a pile this size be
    /// auto-looted would only rebuild the overload that killed the player.
    /// </summary>
    static bool DropBeside(Thing t, Point? at)
    {
        Point? p = at ?? EClass.pc?.pos;
        if (p == null || EClass._zone == null)
        {
            return false;
        }

        try
        {
            t.parent?.RemoveCard(t);
            Card card = EClass._zone.AddCard(t, p);
            if (card == null || card.isDestroyed)
            {
                return false;
            }

            Thing placed = card as Thing ?? t;
            try { placed.altitude = 0; } catch { }
            try { placed.isHidden = false; } catch { }
            try { placed.ignoreAutoPick = true; } catch { }
            return true;
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("processor drop leftover: " + ex.Message);
            return false;
        }
    }

    /// <summary>Tell the player where the leftovers went, once per frame at most.</summary>
    static void NoteLeftover(ProcessorJob? job)
    {
        if (job == null)
        {
            return;
        }

        int frame;
        try { frame = Time.frameCount; } catch { frame = -1; }
        if (job.LeftoverNoteFrame == frame)
        {
            return;
        }

        job.LeftoverNoteFrame = frame;

        string what = job.MachineName ?? NpcLabor.LaborText.T("proc.msg.machineFallback");
        string msg = NpcLabor.LaborText.T("proc.msg.leftoverGround", what);

        try { Msg.SayRaw(msg); } catch { }
        Plugin.LogInfo("processor leftover: " + msg);
    }

    /// <summary>
    /// PC is leaving the current map. Party workers abort; residents hold the job
    /// (ings stay on machine) until PC returns to WorkZoneUid.
    /// Applies to every tracked job.
    /// </summary>
    internal static void OnPcLeavingZone()
    {
        ProcessorJob[] snapshot = Jobs.ToArray();
        for (int i = 0; i < snapshot.Length; i++)
        {
            ProcessorJob job = snapshot[i];
            if (!job.Active || job.Suspended)
            {
                continue;
            }

            if (job.WasPartyMember)
            {
                Clear(job, "zone-change");
                continue;
            }

            SuspendForZoneLeave(job);
        }
    }

    /// <summary>
    /// Freestanding AI cancelled. If PC is leaving / job already held, do not wipe resident work.
    /// </summary>
    internal static void HandleAiInterrupted(ProcessorJob? job, string reason)
    {
        if (job == null || !job.Active)
        {
            return;
        }

        if (job.Suspended)
        {
            return;
        }

        if (job.WasPartyMember)
        {
            Clear(job, string.IsNullOrEmpty(reason) ? "ai-cancel" : reason);
            return;
        }

        if ((reason == "ai-cancel" || reason == "ai-fail")
            && job.ResumeIgnoreCancelUntilFrame > 0
            && Time.frameCount <= job.ResumeIgnoreCancelUntilFrame)
        {
            return;
        }

        bool leaving = false;
        try
        {
            if (EClass.pc != null && EClass.player != null)
            {
                leaving = EClass.player.nextZone != null
                    && (EClass._zone == null || EClass.player.nextZone.uid != EClass._zone.uid);
            }
        }
        catch { leaving = false; }

        bool offWork = false;
        try
        {
            if (job.WorkZoneUid != 0 && EClass._zone != null && EClass._zone.uid != job.WorkZoneUid)
            {
                offWork = true;
            }
        }
        catch { offWork = false; }

        if (leaving || offWork)
        {
            SuspendForZoneLeave(job);
            return;
        }

        // Transient AI override (e.g. a lingering wait order) cancelled the process AI
        // while the job is still valid here — re-assert instead of wiping the job.
        if (reason == "ai-cancel" && TryRestartAfterInterrupt(job))
        {
            return;
        }

        Clear(job, string.IsNullOrEmpty(reason) ? "ai-cancel" : reason);
    }

    /// <summary>
    /// Re-assert the process AI after an ai-cancel on the work zone, bounded by
    /// AiRestartBudget so we never fight a persistent goal forever.
    /// </summary>
    static bool TryRestartAfterInterrupt(ProcessorJob job)
    {
        if (job == null || job.AiRestartBudget <= 0)
        {
            return false;
        }

        Chara? worker = GetWorker(job);
        if (worker == null || worker.isDead)
        {
            return false;
        }

        job.AiRestartBudget--;
        try { worker.RemoveCondition<ConWait>(); } catch { }
        RestartWorkerAi(job, "ai-cancel-retry");
        return true;
    }

    /// <summary>
    /// Inputs for the walk-end decision, gathered at the call site so ProcessorWalk stays
    /// a pure function (see <see cref="ProcessorWalk.Classify"/>).
    /// </summary>
    internal readonly struct WalkEndContext
    {
        internal readonly ProcessorJob? Job;
        internal readonly bool WorkerLost;
        internal readonly bool MachineGone;
        internal readonly int Dist;

        internal WalkEndContext(ProcessorJob? job, bool workerLost, bool machineGone, int dist)
        {
            Job = job;
            WorkerLost = workerLost;
            MachineGone = machineGone;
            Dist = dist;
        }
    }

    /// <summary>
    /// Bounded retry for an interrupted walk (player bump / AI_Goto false success) while the
    /// machine is still present. Reuses the job's AiRestartBudget, which resets on job start,
    /// clear and every successful craft — so a healthy job never runs out, while a machine the
    /// worker genuinely cannot reach eventually exhausts the budget and reports "stuck".
    /// </summary>
    internal static bool TryBudgetedWalkRetry(ProcessorJob? job)
    {
        if (job == null || !job.Active || job.Suspended || job.AiRestartBudget <= 0)
        {
            return false;
        }

        job.AiRestartBudget--;
        RestartWorkerAi(job, "walk-retry");
        return true;
    }

    static void SuspendForZoneLeave(ProcessorJob job)
    {
        if (job == null || !job.Active || job.Suspended)
        {
            return;
        }

        job.Suspended = true;
        job.LastClearReason = "zone-suspend";
        job.SuspendedAtRaw = CurrentRawDate();

        // Drop live Thing refs while the work map is unloading. Marks keep identity
        // so return catch-up rebinds the real parked stacks (avoids ghost Split/dupe).
        RefreshIngredientMarksFromLive(job);
        job.Ingredients.Clear();
        job.CrafterRef = null;
        job.MachineRef = null;

        // Drop freestanding AI; OnCancel will no-op while Suspended.
        try
        {
            Chara? worker = RefChara.Get(job.NpcUid);
            if (worker != null && !worker.isDead)
            {
                try
                {
                    // Stop freestanding process AI; Suspended makes OnCancel a no-op.
                    worker.SetAI(new NoGoal());
                }
                catch
                {
                    try { worker.SetNoGoal(); } catch { }
                }
            }
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("processor suspend stop ai: " + ex.Message);
        }

        Plugin.LogInfo(
            $"processor suspend leave npc={job.NpcUid} left={job.Remaining} workZone={job.WorkZoneUid} raw={job.SuspendedAtRaw}");

        try
        {
            string name = job.NpcName ?? NpcLabor.LaborText.T("proc.msg.residentFallback");
            Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.zoneContinue", name, NpcLabor.LaborTerms.Process));
        }
        catch
        {
        }
    }

    /// <summary>PC entered a zone — resume every suspended job whose work zone is this one.</summary>
    internal static void OnZoneEntered(Zone? zone)
    {
        if (zone == null)
        {
            return;
        }

        ProcessorJob[] snapshot = Jobs.ToArray();
        for (int i = 0; i < snapshot.Length; i++)
        {
            ProcessorJob job = snapshot[i];
            if (!job.Active || !job.Suspended)
            {
                continue;
            }

            try
            {
                if (job.WorkZoneUid != 0 && zone.uid != job.WorkZoneUid)
                {
                    continue;
                }
            }
            catch
            {
                continue;
            }

            ApplyReturnCatchUpAndResume(job);
        }
    }

    static void ApplyReturnCatchUpAndResume(ProcessorJob job)
    {
        if (job == null || !job.Active || !job.Suspended)
        {
            return;
        }

        // Rebind machine on the now-loaded map.
        job.CrafterRef = null;
        job.MachineRef = null;
        TraitCrafter? crafter = GetCrafter(job);
        Card? machine = crafter?.owner;
        Chara? worker = GetWorker(job);

        if (worker == null || worker.isDead)
        {
            Clear(job, "ai-fail");
            return;
        }

        if (crafter == null || machine == null || machine.isDestroyed)
        {
            Clear(job, "machine-gone");
            return;
        }

        // Map reload replaces Card instances - rebind parked bulk before any Split/Craft.
        if (!TryRebindIngredients(job, machine))
        {
            Plugin.LogWarn(
                $"processor return rebind failed npc={job.NpcUid} marks={job.IngredientMarks.Count} - clear without free product");
            Clear(job, "no-ings");
            return;
        }

        // GetRaw() is game minutes. Live duration is AI progress steps; treat each
        // step as one minute so leave/return settle without simulating ticks off-map.
        // Away rate is half on-map speed (user lock).
        //
        // The 1 step = 1 minute mapping is a deliberate approximation, not a measured
        // conversion: an on-map craft also burns frames on walking and the progress
        // animation that off-map catch-up never runs, so the two are not comparable
        // one-to-one. Re-tune MinutesPerProgress only against real play timings —
        // it scales catch-up output linearly.
        int now = CurrentRawDate();
        int elapsedMins = job.SuspendedAtRaw > 0 && now > job.SuspendedAtRaw ? now - job.SuspendedAtRaw : 0;
        int minsPerCraft = Mathf.Max(1, EstimateDuration(job) * MinutesPerProgress * AwaySpeedDivisor);
        int canDo = minsPerCraft > 0 ? elapsedMins / minsPerCraft : 0;
        if (canDo > job.Remaining)
        {
            canDo = job.Remaining;
        }

        int did = 0;
        for (int i = 0; i < canDo; i++)
        {
            if (!job.Active || job.Remaining <= 0)
            {
                break;
            }

            if (!AI_NpcProcess.TryCatchUpOne(job, worker, crafter, machine))
            {
                break;
            }

            did++;
        }

        Plugin.LogInfo(
            $"processor return catch-up npc={job.NpcUid} elapsedMins={elapsedMins} minsPerCraft={minsPerCraft} (away x1/{AwaySpeedDivisor}) did={did} left={job.Remaining}");

        if (!job.Active)
        {
            return;
        }

        if (job.Remaining <= 0)
        {
            if (!TryContinueLeftover(job))
            {
                Clear(job, "ai-end");
                return;
            }
        }

        // Still work left — resume freestanding AI on this map.
        job.Suspended = false;
        job.SuspendedAtRaw = 0;
        try { job.ResumeIgnoreCancelUntilFrame = Time.frameCount + 3; } catch { job.ResumeIgnoreCancelUntilFrame = 0; }

        string name = job.NpcName ?? NpcLabor.LaborText.T("proc.msg.residentFallback");
        try
        {
            Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.zoneResume", name, job.Remaining));
        }
        catch { }

        try
        {
            worker.SetAIImmediate(new AI_NpcProcess
            {
                jobToken = job.JobToken
            });
        }
        catch (System.Exception ex)
        {
            Plugin.LogWarn("processor resume SetAI failed: " + ex.Message);
            Clear(job, "ai-set-fail", announce: false);
        }

        try { worker.RemoveCondition<ConWait>(); } catch { }
        job.AiRestartBudget = 3;
    }


    static void RefreshIngredientMarksFromLive(ProcessorJob? job)
    {
        if (job == null || job.Ingredients.Count == 0)
        {
            return;
        }

        if (job.IngredientMarks.Count != job.Ingredients.Count)
        {
            job.IngredientMarks.Clear();
            for (int i = 0; i < job.Ingredients.Count; i++)
            {
                job.IngredientMarks.Add(IngredientMark.From(job.Ingredients[i]));
            }
            return;
        }

        for (int i = 0; i < job.Ingredients.Count; i++)
        {
            Thing? t = job.Ingredients[i];
            if (t == null || t.isDestroyed)
            {
                continue;
            }

            job.IngredientMarks[i] = IngredientMark.From(t);
        }
    }

    /// <summary>
    /// Rebind Ingredients to live map Things after zone reload.
    /// Prefer uid; fallback to machine-cell stacks matching id/material.
    /// Returns false if any mark cannot be resolved to a live usable stack.
    /// </summary>
    internal static bool TryRebindIngredients(ProcessorJob? job, Card? machine)
    {
        if (job == null)
        {
            return false;
        }
        job.Ingredients.Clear();

        if (job.IngredientMarks.Count == 0)
        {
            return false;
        }

        HashSet<int> used = new HashSet<int>();
        for (int i = 0; i < job.IngredientMarks.Count; i++)
        {
            IngredientMark mark = job.IngredientMarks[i];
            Thing? live = ResolveIngredientMark(mark, machine, used);
            if (live == null || live.isDestroyed || live.Num <= 0)
            {
                job.Ingredients.Clear();
                return false;
            }

            try { used.Add(live.uid); } catch { }
            ApplyParkFlags(live);
            job.Ingredients.Add(live);
            job.IngredientMarks[i] = IngredientMark.From(live);
        }

        return job.Ingredients.Count == job.IngredientMarks.Count && job.Ingredients.Count > 0;
    }

    static Thing? ResolveIngredientMark(IngredientMark mark, Card? machine, HashSet<int> used)
    {
        // 1) Exact uid on current map.
        if (mark.Uid != 0)
        {
            Thing? byUid = null;
            try
            {
                Card? c = FindCardByUid(mark.Uid);
                byUid = c as Thing;
            }
            catch
            {
                byUid = null;
            }

            if (byUid != null && !byUid.isDestroyed && byUid.Num > 0)
            {
                int uid = 0;
                try { uid = byUid.uid; } catch { }
                if (uid == 0 || !used.Contains(uid))
                {
                    return byUid;
                }
            }
        }

        // 2) Machine cell stacks matching id + material (parked bulk).
        if (machine != null && !machine.isDestroyed && machine.ExistsOnMap && machine.pos != null)
        {
            Thing? onCell = FindMatchingThingOnPoint(machine.pos, mark, used);
            if (onCell != null)
            {
                return onCell;
            }
        }

        // 3) Any map thing matching id + material, prefer ignoreAutoPick parked.
        try
        {
            if (EClass._map?.things != null)
            {
                Thing? best = null;
                foreach (Thing t in EClass._map.things)
                {
                    if (!MatchesMark(t, mark, used))
                    {
                        continue;
                    }

                    bool parked = false;
                    try { parked = t.ignoreAutoPick; } catch { parked = false; }
                    if (parked)
                    {
                        return t;
                    }

                    if (best == null)
                    {
                        best = t;
                    }
                }

                if (best != null)
                {
                    return best;
                }
            }
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("resolve ingredient map scan: " + ex.Message);
        }

        return null;
    }

    static Thing? FindMatchingThingOnPoint(Point pos, IngredientMark mark, HashSet<int> used)
    {
        if (pos == null)
        {
            return null;
        }

        try
        {
            List<Thing>? list = pos.Things;
            if (list == null)
            {
                return null;
            }

            for (int i = 0; i < list.Count; i++)
            {
                Thing? t = list[i];
                if (MatchesMark(t, mark, used))
                {
                    return t;
                }
            }
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("resolve ingredient cell: " + ex.Message);
        }

        return null;
    }

    static bool MatchesMark(Thing? t, IngredientMark mark, HashSet<int> used)
    {
        if (t == null || t.isDestroyed || t.Num <= 0)
        {
            return false;
        }

        try
        {
            if (used != null && used.Contains(t.uid))
            {
                return false;
            }
        }
        catch
        {
        }

        try
        {
            if (!string.IsNullOrEmpty(mark.Id) && t.id != mark.Id)
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        if (mark.MaterialId != 0)
        {
            int mat = 0;
            try { mat = t.material != null ? t.material.id : t.idMaterial; } catch { mat = 0; }
            if (mat != 0 && mat != mark.MaterialId)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>After live Split/consume, keep marks in sync with current bulk uids.</summary>
    internal static void SyncIngredientMarksAfterCraft(ProcessorJob? job)
    {
        if (job == null || job.Ingredients.Count == 0)
        {
            return;
        }

        if (job.IngredientMarks.Count != job.Ingredients.Count)
        {
            job.IngredientMarks.Clear();
            for (int i = 0; i < job.Ingredients.Count; i++)
            {
                job.IngredientMarks.Add(IngredientMark.From(job.Ingredients[i]));
            }
            return;
        }

        for (int i = 0; i < job.Ingredients.Count; i++)
        {
            Thing? t = job.Ingredients[i];
            if (t == null || t.isDestroyed || t.Num <= 0)
            {
                continue;
            }

            job.IngredientMarks[i] = IngredientMark.From(t);
        }
    }

    static void AnnounceFinish(ProcessorJob job, string reason)
    {
        string name = job.NpcName ?? NpcLabor.LaborText.T("proc.msg.residentFallback");

        if (reason == "reopen" || reason == "plugin-destroy" || reason == "ai-set-fail" || reason == "zone-suspend")
        {
            return;
        }

        if (job.Completed > 0)
        {
            // Mid-job abort after some crafts is not "finished".
            if (reason == "ai-fail" || reason == "ai-cancel" || job.Remaining > 0)
            {
                Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.interrupted", name, NpcLabor.LaborTerms.Process));
                return;
            }

            if (job.ExpGranted > 0)
            {
                Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.doneSkilled", name, NpcLabor.LaborTerms.Process));
            }
            else
            {
                Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.done", name, NpcLabor.LaborTerms.Process));
            }
            return;
        }

        switch (reason)
        {
            case "ai-cancel":
            case "ai-fail":
                Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.interrupted", name, NpcLabor.LaborTerms.Process));
                break;
            case "pc-death":
            case "zone-change":
                Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.stopped", name, NpcLabor.LaborTerms.Process));
                break;
            case "no-fuel":
                Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.noFuel", name));
                break;
            case "no-ings":
                Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.noMaterial", name));
                break;
            case "stuck":
                Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.stuck", name));
                break;
            case "machine-gone":
                Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.noMachine", name));
                break;
            case "stamina":
                Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.exhausted", name));
                break;
            case "pc-pick":
                Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.stopped", name, NpcLabor.LaborTerms.Process));
                break;
            default:
                Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.ended", name, NpcLabor.LaborTerms.Process));
                break;
        }
    }
}
