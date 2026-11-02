using System.Collections.Generic;
using UnityEngine;

namespace NpcLabor.Process;

/// <summary>
/// NPC freestanding processor AI (no LayerDragGrid).
/// Goto machine → convert ings via TraitCrafter.Craft → drop product on ground → repeat.
/// </summary>
internal class AI_NpcProcess : AIAct
{
    /// <summary>Handle back to this AI's ProcessorJob in the registry. Resolve fresh - never cache across yields.</summary>
    internal int jobToken;
    /// <summary>Set when WalkToMachine gives up because the path to the machine is permanently blocked (distinct from machine-gone).</summary>
    internal string? walkFailReason;

    /// <summary>
    /// The job this AI owns right now, or null if it was cleared / superseded.
    /// Must be re-read after every yield: a suspended/cleared job removes it from the registry,
    /// and an iterator cannot assume a cached object is still live.
    /// </summary>
    ProcessorJob? Job => ProcessorJobSession.Resolve(jobToken);

    /// <summary>Stuck-detection: if the worker makes no progress toward the machine for this many AI ticks, snap it next to the machine (vanilla AI_Goto stalls forever on chara-occupied tiles).</summary>
    const int StuckSnapThreshold = 90;

    public override bool CanManualCancel() => true;

    public override bool CancelWhenDamaged => true;

    public override void OnCancel()
    {
        ProcessorJob? job = Job;
        if (job != null && owner != null && owner.uid == job.NpcUid)
        {
            // Leave/unload cancels freestanding AI — hold resident job instead of wiping.
            ProcessorJobSession.HandleAiInterrupted(job, "ai-cancel");
        }
    }

    public override void OnSuccess()
    {
        ProcessorJob? job = Job;
        if (job == null || owner == null || owner.uid != job.NpcUid)
        {
            return;
        }

        if (job.Suspended)
        {
            return;
        }

        if (job.Remaining <= 0)
        {
            ProcessorJobSession.Clear(job, "ai-end");
            return;
        }

        ProcessorJobSession.RestartWorkerAi(job, "onsuccess-remaining");
    }

    public override IEnumerable<Status> Run()
    {
        ProcessorJob? job = Job;
        if (job == null || !job.Active || job.Suspended
            || owner == null || owner.uid != job.NpcUid)
        {
            yield return Success();
            yield break;
        }

        walkFailReason = null;

        TraitCrafter? crafter = ProcessorJobSession.GetCrafter(job);
        Card? machine = crafter?.owner;
        if (crafter == null || machine == null || machine.isDestroyed)
        {
            ProcessorJobSession.Clear(job, "machine-gone");
            yield return Cancel();
            yield break;
        }

        // Walk next to the machine. Vanilla AI_Goto can stall forever on a
        // chara-occupied tile, so WalkToMachine snaps the worker next to the
        // machine after StuckSnapThreshold ticks without progress.
        if (machine.ExistsOnMap)
        {
            foreach (Status s in WalkToMachine(machine))
            {
                yield return s;
            }

            job = Job;
            if (job == null || owner == null || owner.isDead)
            {
                if (job != null) { ProcessorJobSession.Clear(job, "ai-fail"); }
                yield return Cancel();
                yield break;
            }

            if (machine.isDestroyed || owner.Dist(machine) > 1)
            {
                if (!HandleWalkEnd(
                        new ProcessorJobSession.WalkEndContext(job, workerLost: false, machineGone: machine.isDestroyed, dist: owner.Dist(machine)),
                        out Status walkStop))
                {
                    yield return walkStop;
                    yield break;
                }
            }

            owner.LookAt(machine);
        }

        if (crafter.Icon != 0)
        {
            try
            {
                owner.ShowEmo(crafter.Icon);
            }
            catch (System.Exception __e) { Plugin.LogDebug("AI_NpcProcess.cs silent catch: " + __e.Message); }
}

        while (true)
        {
            // Re-resolve after every yield: a suspend/clear between ticks removes the job.
            job = Job;
            if (job == null || !job.Active || job.Suspended || job.Remaining <= 0)
            {
                break;
            }

            crafter = ProcessorJobSession.GetCrafter(job);
            machine = crafter?.owner;
            if (crafter == null || machine == null || machine.isDestroyed)
            {
                ProcessorJobSession.Clear(job, "machine-gone");
                yield return Cancel();
                yield break;
            }

            if (owner.stamina != null && owner.stamina.value <= 0)
            {
                ProcessorJobSession.Clear(job, "stamina");
                yield return Cancel();
                yield break;
            }

            // Keep adjacency if we drifted.
            if (machine.ExistsOnMap && owner.Dist(machine) > 1)
            {
                foreach (Status s in WalkToMachine(machine))
                {
                    yield return s;
                }

                job = Job;
                if (job == null || owner == null || owner.isDead)
                {
                    if (job != null) { ProcessorJobSession.Clear(job, "ai-fail"); }
                    yield return Cancel();
                    yield break;
                }

                if (machine.isDestroyed || owner.Dist(machine) > 1)
                {
                    if (!HandleWalkEnd(
                            new ProcessorJobSession.WalkEndContext(job, workerLost: false, machineGone: machine.isDestroyed, dist: owner.Dist(machine)),
                            out Status walkStop))
                    {
                        yield return walkStop;
                        yield break;
                    }
                }

                owner.LookAt(machine);
            }

            List<Thing> sources = job.Ingredients;
            if (sources == null || sources.Count == 0)
            {
                ProcessorJobSession.Clear(job, "no-ings");
                yield return Cancel();
                yield break;
            }

            // Validate source stacks still usable.
            if (!ProcessorJobSession.CanUseIngredientStacks(job))
            {
                ProcessorJobSession.Clear(job,
                    ProcessorJobSession.IsAnyIngredientHeldByPc(job) ? "pc-pick" : "no-ings");
                yield return Cancel();
                yield break;
            }

            List<Thing> targets = new List<Thing>(sources);
            if (!EnsureFuel(crafter, machine, targets))
            {
                ProcessorJobSession.Clear(job, "no-fuel");
                yield return Cancel();
                yield break;
            }

            // Split one set of ingredients for this craft.
            if (!PrepareOneCraft(job, owner, crafter, machine, sources,
                    out List<Thing> ings, out BlessedState blessed, out bool requireOn,
                    out AI_UseCrafter shell, out int costSp, out int duration))
            {
                ProcessorJobSession.Clear(job, "no-ings");
                yield return Cancel();
                yield break;
            }

            if (!crafter.idSoundBG.IsEmpty())
            {
                try
                {
                    SE.Play(crafter.idSoundBG);
                }
                catch (System.Exception __e) { Plugin.LogDebug("AI_NpcProcess.cs silent catch: " + __e.Message); }
}

            Progress_Custom progress = new Progress_Custom
            {
                cancelWhenMoved = false,
                canProgress = () =>
                {
                    if (requireOn && (machine == null || !machine.isOn))
                    {
                        return false;
                    }

                    if (machine == null || machine.isDestroyed)
                    {
                        return false;
                    }

                    // Check if player has picked up any ingredients from the machine.
                    if (!ProcessorJobSession.CanUseIngredientStacks(job))
                    {
                        return false;
                    }

                    foreach (Thing ing in ings)
                    {
                        if (ing == null)
                        {
                            return false;
                        }

                        if (ing.isDestroyed)
                        {
                            return false;
                        }

                        if (crafter.IsConsumeIng && !ing.ExistsOnMap)
                        {
                            return false;
                        }
                    }

                    return owner != null && !owner.isDead;
                },
                onProgress = _ =>
                {
                    if (owner == null)
                    {
                        return;
                    }

                    if (machine != null && machine.ExistsOnMap && !owner.pos.Equals(machine.pos))
                    {
                        owner.LookAt(machine);
                    }

                    try
                    {
                        owner.PlaySound(crafter.idSoundProgress);
                    }
                    catch (System.Exception __e) { Plugin.LogDebug("AI_NpcProcess.cs silent catch: " + __e.Message); }
                    try
                    {
                        if (machine != null && machine.ExistsOnMap)
                        {
                            TraitCrafter.AnimeType animeType = crafter.animeType;
                            if ((uint)(animeType - 1) <= 1u)
                            {
                                machine.renderer.PlayAnime(crafter.IdAnimeProgress);
                            }
                        }
                    }
                    catch (System.Exception __e) { Plugin.LogDebug("AI_NpcProcess.cs silent catch: " + __e.Message); }
foreach (Thing ing2 in ings)
                    {
                        try
                        {
                            if (ing2 != null && ing2.renderer != null)
                            {
                                ing2.renderer.PlayAnime(crafter.IdAnimeProgress);
                            }
                        }
                        catch (System.Exception __e) { Plugin.LogDebug("AI_NpcProcess.cs silent catch: " + __e.Message); }
}
                },
                onProgressComplete = () =>
                {
                    CompleteOne(job, owner, crafter, machine, shell, ings, blessed, costSp, duration);
                }
            }.SetDuration(duration, 2);

            try
            {
                owner.SetTempHand(-1, -1);
            }
            catch (System.Exception __e) { Plugin.LogDebug("AI_NpcProcess.cs silent catch: " + __e.Message); }
yield return Do(progress);

            if (progress.status == Status.Fail)
            {
                Plugin.LogInfo(
                    $"processor progress fail left={job.Remaining} done={job.Completed}");
                CleanupPartialIngs(job, ings, sources);
                ProcessorJobSession.HandleAiInterrupted(job, "ai-fail");
                yield return Cancel();
                yield break;
            }

            job = Job;
            if (job == null || !job.Active)
            {
                yield return Success();
                yield break;
            }

            if (job.Remaining <= 0)
            {
                break;
            }
        }

        // Clean end of loop (suspend exits while via !Active/Suspended — do not Clear held jobs).
        job = Job;
        if (job != null && job.Active && !job.Suspended && job.Remaining <= 0)
        {
            TraitCrafter? c2 = ProcessorJobSession.GetCrafter(job);
            if (c2 != null && c2.AutoTurnOff && c2.owner != null && c2.owner.isOn)
            {
                try
                {
                    c2.Toggle(on: false);
                }
                catch (System.Exception __e) { Plugin.LogDebug("AI_NpcProcess.cs silent catch: " + __e.Message); }
}

            if (c2 != null && !c2.idSoundBG.IsEmpty())
            {
                try
                {
                    EClass.Sound.Stop(c2.idSoundBG);
                }
                catch (System.Exception __e) { Plugin.LogDebug("AI_NpcProcess.cs silent catch: " + __e.Message); }
}

            ProcessorJobSession.Clear(job, "ai-end");
        }

        yield return Success();
    }

    /// <summary>
    /// Decide what a finished walk means and act on it. Delegates to <see cref="ProcessorWalk.Classify"/>
    /// so a bump / false arrival retries instead of being mislabelled "machine-gone".
    /// Returns true when the caller may continue (Arrived); false when this iterator must stop.
    /// </summary>
    bool HandleWalkEnd(ProcessorJobSession.WalkEndContext ctx, out Status stop)
    {
        stop = Status.Running;
        ProcessorWalk.Outcome outcome = ProcessorWalk.Classify(
            workerLost: ctx.WorkerLost,
            machineGone: ctx.MachineGone,
            dist: ctx.Dist,
            walkFailReason: walkFailReason);

        switch (outcome)
        {
            case ProcessorWalk.Outcome.Arrived:
                return true;

            case ProcessorWalk.Outcome.Retry:
                // Machine is still here and we are not permanently stuck: the walk was
                // interrupted (player bump / AI_Goto false success). Re-assert the process
                // AI so Run() restarts from the top and walks again. Bounded by the job's
                // AiRestartBudget so a permanently unreachable machine cannot spin forever;
                // when the budget is spent we fall through to a terminal "stuck".
                if (ProcessorJobSession.TryBudgetedWalkRetry(ctx.Job))
                {
                    stop = Cancel();
                    return false;
                }

                ProcessorJobSession.Clear(ctx.Job, "stuck");
                stop = Cancel();
                return false;

            case ProcessorWalk.Outcome.Stuck:
                ProcessorJobSession.Clear(ctx.Job, "stuck");
                stop = Cancel();
                return false;

            case ProcessorWalk.Outcome.MachineGone:
                ProcessorJobSession.Clear(ctx.Job, "machine-gone");
                stop = Cancel();
                return false;

            default:
                // WorkerLost — the worker died or the job vanished mid-walk.
                ProcessorJobSession.Clear(ctx.Job, "ai-fail");
                stop = Cancel();
                return false;
        }
    }

    /// <summary>
    /// Walk next to the machine with stuck detection. Vanilla AI_Goto waits forever
    /// on chara-occupied tiles (Chara.CanMoveTo blocks NPC entry unless CanReplace),
    /// so if the worker makes no progress for StuckSnapThreshold ticks we snap them
    /// to a free tile next to the machine and re-path. Gives up after 3 failed snaps.
    /// </summary>
    IEnumerable<Status> WalkToMachine(Card machine)
    {
        if (owner == null || owner.isDead || machine == null || machine.isDestroyed || !machine.ExistsOnMap)
        {
            yield break;
        }

        int stuckTicks = 0;
        int snapCount = 0;
        int lastDist = owner.Dist(machine);
        SetChild(new AI_Goto(machine, 1), KeepRunning);

        // One movement step: advance the goto child, then check arrival / stuck.
        // Returns true when the walk should stop (arrived, dead, machine gone,
        // or stuck-gave-up).
        bool StepOnce()
        {
            TickChild();
            if (owner == null || owner.isDead || machine.isDestroyed)
            {
                return true;
            }

            int dist = owner.Dist(machine);
            if (dist <= 1)
            {
                return true;
            }

            if (dist < lastDist)
            {
                lastDist = dist;
                stuckTicks = 0;
            }
            else
            {
                stuckTicks++;
                if (stuckTicks >= StuckSnapThreshold)
                {
                    if (++snapCount > 3)
                    {
                        // No free tile near the machine or the path is permanently blocked.
                        walkFailReason = "stuck";
                        return true;
                    }

                    if (TrySnapNearMachine(machine))
                    {
                        lastDist = owner.Dist(machine);
                        stuckTicks = 0;
                        // Stale child path — re-path from the new spot.
                        SetChild(new AI_Goto(machine, 1), KeepRunning);
                    }
                    else
                    {
                        // No free tile found; wait and retry the snap later.
                        stuckTicks = 0;
                    }
                }
            }

            return false;
        }

        while (child != null && child.status == Status.Running)
        {
            // Two steps per tick — the worker walks to the machine twice as fast.
            if (StepOnce())
            {
                yield break;
            }

            if (child != null && child.status == Status.Running)
            {
                if (StepOnce())
                {
                    yield break;
                }
            }

            yield return Status.Running;
        }
    }

    /// <summary>
    /// Snap the worker onto a free tile next to the machine without cancelling the
    /// running AI. Mirrors town-labor PlaceWorkerNearClient: MoveImmediate avoids
    /// Card.Teleport, which cancels the chara's AI (would kill this iterator).
    /// </summary>
    bool TrySnapNearMachine(Card machine)
    {
        if (owner == null || owner.isDead || machine == null || machine.isDestroyed
            || !machine.ExistsOnMap || machine.pos == null || !owner.ExistsOnMap)
        {
            return false;
        }

        try
        {
            Point? dest = FindFreePointNear(machine.pos);
            if (dest == null || !dest.IsValid || !dest.IsInBounds || dest.Equals(machine.pos))
            {
                return false;
            }

            if (EClass.pc != null && EClass.pc.pos != null && dest.Equals(EClass.pc.pos))
            {
                return false;
            }

            owner.MoveImmediate(dest, focus: false, cancelAI: false);
            // True as long as the worker actually moved — the caller re-paths and
            // walks the remaining tile (snap may land 2 away when all adjacent
            // tiles are occupied).
            return owner.ExistsOnMap;
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("processor snap near machine: " + ex.Message);
            return false;
        }
    }

    /// <summary>Free point near origin (mirrors town-labor FindNearPoint): not blocked, not the PC, not another chara. Prefers distance-1 adjacency.</summary>
    Point? FindFreePointNear(Point origin)
    {
        if (origin == null)
        {
            return null;
        }

        int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1, 2, -2, 0, 0, 2, 2, -2, -2, 3, -3, 0, 0 };
        int[] dy = { 0, 0, 1, -1, 1, -1, 1, -1, 0, 0, 2, -2, 2, -2, 2, -2, 0, 0, 3, -3 };
        Point? pcPos = null;
        try { pcPos = EClass.pc?.pos; } catch { pcPos = null; }

        for (int i = 0; i < dx.Length; i++)
        {
            try
            {
                Point p = new Point(origin.x + dx[i], origin.z + dy[i]);
                if (!p.IsValid || !p.IsInBounds)
                {
                    continue;
                }

                if (p.IsBlocked)
                {
                    continue;
                }

                if (pcPos != null && p.Equals(pcPos))
                {
                    continue;
                }

                if (p.Equals(origin))
                {
                    continue;
                }

                if (p.HasChara)
                {
                    bool self = false;
                    try
                    {
                        if (owner != null && owner.pos != null && p.Equals(owner.pos))
                        {
                            self = true;
                        }
                    }
                    catch { }

                    if (!self)
                    {
                        continue;
                    }
                }

                return p;
            }
            catch { }
        }

        return null;
    }

    /// <summary>
    /// Keep the machine fed instead of stopping halfway, on vanilla's own fuel: the worker tops
    /// the machine's charge up through <c>Trait.TryRefuel</c> (the machine's fuel spot and the
    /// player's pack, never the ingredients on the bench), so a long smelting run only stops when
    /// there is genuinely no fuel left anywhere. Fuel the player already put into the machine by
    /// hand is in <c>c_charges</c>, which is checked first.
    /// </summary>
    static bool EnsureFuel(TraitCrafter crafter, Card machine, List<Thing> targets)
    {
        try
        {
            if (!crafter.IsRequireFuel)
            {
                return true;
            }

            int need = Mathf.Max(1, crafter.FuelCost);
            if (machine.c_charges >= need)
            {
                return true;
            }

            crafter.TryRefuel(need - machine.c_charges, targets);
            return machine.c_charges >= need;
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("processor refuel: " + ex.Message);
            try
            {
                return !crafter.IsRequireFuel || machine.c_charges >= Mathf.Max(1, crafter.FuelCost);
            }
            catch
            {
                return false;
            }
        }
    }

    static int ComputeDuration(ProcessorJob job, TraitCrafter crafter, AI_UseCrafter shell, int costSp)
    {
        // Mirror TraitCrafter.GetDuration but use NPC skill instead of EClass.pc,
        // then cut NPC process duration in half vs the skill-scaled base.
        try
        {
            SourceRecipe.Row? src = crafter.GetSource(shell);
            if (src == null)
            {
                return ProcessorJobSession.ApplyNpcDurationCut(10);
            }

            int skill = job.NpcSkill;
            return ProcessorJobSession.ApplyNpcDurationCut(src.time * 100 / (80 + skill * 5));
        }
        catch
        {
            try
            {
                return ProcessorJobSession.ApplyNpcDurationCut(crafter.GetDuration(shell, costSp));
            }
            catch
            {
                return ProcessorJobSession.ApplyNpcDurationCut(10);
            }
        }
    }

    static void CompleteOne(
        ProcessorJob job,
        Chara owner,
        TraitCrafter crafter,
        Card machine,
        AI_UseCrafter shell,
        List<Thing> ings,
        BlessedState blessed,
        int costSp,
        int duration)
    {
        try
        {
            if (crafter.StopSoundProgress)
            {
                EClass.Sound.Stop(crafter.idSoundProgress);
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("AI_NpcProcess.cs silent catch: " + __e.Message); }
        try
        {
            owner.PlaySound(crafter.idSoundComplete);
        }
        catch (System.Exception __e) { Plugin.LogDebug("AI_NpcProcess.cs silent catch: " + __e.Message); }
int exp = 0;
        bool craftedOk = false;
        Thing? spawnedProduct = null;
        try
        {
            // recipe is always null for processors - conversion path.
            Thing? product = crafter.Craft(shell);
            if (product != null)
            {
                try
                {
                    if (product.category != null && product.category.ignoreBless == 0)
                    {
                        product.SetBlessedState(blessed);
                    }
                }
                catch (System.Exception __e) { Plugin.LogDebug("AI_NpcProcess.cs silent catch: " + __e.Message); }
                try
                {
                    product.PlaySoundDrop(spatial: false);
                }
                catch (System.Exception __e) { Plugin.LogDebug("AI_NpcProcess.cs silent catch: " + __e.Message); }
                Point dropAt = (machine != null && machine.ExistsOnMap) ? machine.pos : owner.pos;
                EClass._zone.AddCard(product, dropAt);
                try
                {
                    product.Identify(show: false);
                }
                catch (System.Exception __e) { Plugin.LogDebug("AI_NpcProcess.cs silent catch: " + __e.Message); }
                // Slice B rule: product on ground - do not auto-pick to NPC or PC.
                spawnedProduct = product;
                craftedOk = true;
                // A successful craft means the job is healthy again — refresh the restart budget.
                ProcessorJobSession.ResetAiRestartBudget(job);
            }
        }
        catch (System.Exception ex)
        {
            Plugin.LogWarn("processor Craft failed: " + ex.Message);
            craftedOk = false;
        }

        // Consume craft pieces. MVP processors are IsConsumeIng; always Destroy pieces
        // so a null GetSource cannot skip consume and leave free mats / free products.
        bool consumedOk = true;
        try
        {
            bool isConsume;
            try { isConsume = crafter.IsConsumeIng; } catch { isConsume = true; }

            if (isConsume)
            {
                consumedOk = ProcessorJobSession.ConsumeCraftCycle(job, ings);
            }
            else
            {
                // Non-consume machines: return leftover pieces to PC.
                foreach (Thing ing3 in ings)
                {
                    if (ing3 != null && !ing3.isDestroyed && ing3.ExistsOnMap)
                    {
                        try { ProcessorJobSession.ReturnOneIngredient(ing3, job); }
                        catch (System.Exception __e) { Plugin.LogDebug("AI_NpcProcess.cs silent catch: " + __e.Message); }
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("consume ings: " + ex.Message);
            consumedOk = false;
        }

        // Integrity: never credit a craft that did not both produce and consume when required.
        if (craftedOk && !consumedOk)
        {
            Plugin.LogWarn("processor product without consume - destroying product, no NoteCompleted");
            try
            {
                if (spawnedProduct != null && !spawnedProduct.isDestroyed)
                {
                    spawnedProduct.Destroy();
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("AI_NpcProcess.cs silent catch: " + __e.Message); }
            ProcessorJobSession.SyncIngredientMarksAfterCraft(job);
            return;
        }

        if (!craftedOk)
        {
            // No product: do not advance Remaining (mats already Split from bulk; pieces
            // destroyed above if isConsume - rare GetSource miss). Avoid free progress.
            ProcessorJobSession.SyncIngredientMarksAfterCraft(job);
            return;
        }

        if (crafter.IsRequireFuel && machine != null)
        {
            try
            {
                machine.ModCharge(-crafter.FuelCost * 1);
                if (machine.c_charges <= 0)
                {
                    machine.c_charges = 0;
                    crafter.Toggle(on: false);
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("AI_NpcProcess.cs silent catch: " + __e.Message); }
        }

        try
        {
            int skillId = job.SkillId;
            if (skillId == 0)
            {
                string alias = crafter.IDReqEle(null);
                if (!alias.IsEmpty())
                {
                    skillId = EClass.sources.elements.alias[alias].id;
                }
            }

            if (skillId != 0 && owner.elements != null)
            {
                // costSp is kept only for exp scaling; process jobs no longer spend SP.
                exp = costSp * 12 * (100 + duration * 2) / 100;
                owner.elements.ModExp(skillId, exp);
            }
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("exp failed: " + ex.Message);
            exp = 0;
        }

        try
        {
            Point from = (machine != null && machine.ExistsOnMap) ? machine.pos : owner.pos;
            Effect.Get("smoke").Play(from);
            owner.renderer.PlayAnime(AnimeID.JumpSmall);
        }
        catch (System.Exception __e) { Plugin.LogDebug("AI_NpcProcess.cs silent catch: " + __e.Message); }
        ProcessorJobSession.NoteCompleted(job, exp);
        ProcessorJobSession.SyncIngredientMarksAfterCraft(job);
        Plugin.LogDebug(
            $"processor tick done={job.Completed} left={job.Remaining} exp+{exp}");
    }

    /// <summary>
    /// One conversion cycle for return catch-up (no progress anime). Uses parked bulk on machine.
    /// Returns false if ingredients/fuel/machine cannot complete another craft.
    /// </summary>
    internal static bool TryCatchUpOne(ProcessorJob job, Chara worker, TraitCrafter crafter, Card machine)
    {
        if (worker == null || worker.isDead || crafter == null || machine == null || machine.isDestroyed)
        {
            return false;
        }

        if (job == null || !job.Active || job.Remaining <= 0)
        {
            return false;
        }

        List<Thing> sources = job.Ingredients;
        if (sources == null || sources.Count == 0)
        {
            return false;
        }

        if (!ProcessorJobSession.CanUseIngredientStacks(job))
        {
            ProcessorJobSession.Clear(job,
                ProcessorJobSession.IsAnyIngredientHeldByPc(job) ? "pc-pick" : "no-ings");
            return false;
        }

        // Catch-up must use live map stacks only. Ghost refs after unload would Split
        // in memory and leave the real parked bulk untouched (= free product / dupe).
        int[] numsBefore = new int[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            Thing? s = sources[i];
            if (s == null || s.isDestroyed || s.Num <= 0)
            {
                return false;
            }

            try
            {
                if (!s.ExistsOnMap && crafter.IsConsumeIng)
                {
                    Plugin.LogDebug("catch-up abort: ingredient not on map uid=" + s.uid);
                    return false;
                }
            }
            catch
            {
                return false;
            }

            numsBefore[i] = s.Num;
        }

        List<Thing> targets = new List<Thing>(sources);
        try
        {
            if (!EnsureFuel(crafter, machine, targets))
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        int remainingBefore = job.Remaining;
        int completedBefore = job.Completed;
        List<Thing> ings = new List<Thing>();
        try
        {
            if (!PrepareOneCraft(job, worker, crafter, machine, sources,
                    out ings, out BlessedState blessed, out _,
                    out AI_UseCrafter shell, out int costSp, out int duration))
            {
                return false;
            }

            CompleteOne(job, worker, crafter, machine, shell, ings, blessed, costSp, duration);

            if (!job.Active)
            {
                return false;
            }

            // CompleteOne must have advanced the job; otherwise treat as failed cycle.
            if (job.Completed <= completedBefore
                && job.Remaining >= remainingBefore)
            {
                Plugin.LogDebug("catch-up one made no progress");
                return false;
            }

            // Bulk must have decreased (or last unit destroyed) for consume machines.
            if (crafter.IsConsumeIng)
            {
                bool anyDecreased = false;
                for (int i = 0; i < sources.Count; i++)
                {
                    Thing? s = sources[i];
                    if (s == null || s.isDestroyed)
                    {
                        anyDecreased = true;
                        break;
                    }

                    if (s.Num < numsBefore[i])
                    {
                        anyDecreased = true;
                        break;
                    }
                }

                if (!anyDecreased)
                {
                    Plugin.LogWarn("catch-up crafted without bulk decrease - abort further catch-up");
                    return false;
                }
            }

            ProcessorJobSession.SyncIngredientMarksAfterCraft(job);
            return true;
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("processor catch-up one failed: " + ex.Message);
            CleanupPartialIngs(job, ings, sources);
            return false;
        }
    }

    /// <summary>
    /// Split one set of ingredients and work out the shell, SP and duration for this craft.
    /// Shared by the live cycle (Run) and the return catch-up (TryCatchUpOne) so the two
    /// cannot drift apart. Returns false when a piece cannot be taken — the caller owns
    /// whatever else has to happen (clear the session, cancel the AI, ...).
    /// </summary>
    static bool PrepareOneCraft(
        ProcessorJob job, Chara worker, TraitCrafter crafter, Card machine, IList<Thing> sources,
        out List<Thing> ings, out BlessedState blessed, out bool requireOn,
        out AI_UseCrafter shell, out int costSp, out int duration)
    {
        ings = new List<Thing>();
        blessed = BlessedState.Normal;
        shell = null!;
        requireOn = false;
        costSp = 0;
        duration = 0;

        for (int i = 0; i < sources.Count; i++)
        {
            Thing src = sources[i];
            if (src == null || src.isDestroyed || src.Num <= 0)
            {
                CleanupPartialIngs(job, ings, sources);
                return false;
            }

            if (!ProcessorJobSession.TryTakeCraftPiece(src, crafter, machine, worker, out Thing? piece)
                || piece == null)
            {
                CleanupPartialIngs(job, ings, sources);
                return false;
            }

            ings.Add(piece);
            if (piece.blessedState <= BlessedState.Cursed && blessed > piece.blessedState)
            {
                blessed = piece.blessedState;
            }

            if (piece.blessedState > BlessedState.Normal && blessed == BlessedState.Normal)
            {
                blessed = piece.blessedState;
            }
        }

        requireOn = crafter.IsRequireFuel || crafter.ToggleType != ToggleType.None;
        if (requireOn && !machine.isOn)
        {
            try
            {
                crafter.Toggle(on: true);
            }
            catch (System.Exception __e) { Plugin.LogDebug("AI_NpcProcess.cs silent catch: " + __e.Message); }
        }

        // Build a throwaway AI_UseCrafter shell so GetSource / Craft / GetCostSp keep working.
        shell = new AI_UseCrafter
        {
            crafter = crafter,
            recipe = null,
            num = 1,
            ings = ings,
            owner = worker
        };

        int baseSp;
        try
        {
            baseSp = crafter.GetCostSp(shell);
        }
        catch
        {
            baseSp = crafter.CostSP;
        }

        costSp = ProcessorJobSession.AdjustCostSp(job, baseSp);
        duration = ComputeDuration(job, crafter, shell, costSp);
        ProcessorJobSession.RememberCraftTiming(job, duration, costSp);
        return true;
    }

    static void CleanupPartialIngs(ProcessorJob? job, List<Thing> ings, IList<Thing>? splitFrom = null)
    {
        if (ings == null)
        {
            return;
        }

        List<Thing>? sources = job?.Ingredients;
        for (int idx = 0; idx < ings.Count; idx++)
        {
            Thing t = ings[idx];
            if (t == null || t.isDestroyed)
            {
                continue;
            }

            try
            {
                Card? root = t.GetRootCard();
                if (root != null && root.IsPC)
                {
                    continue;
                }
            }
            catch
            {
            }

            bool parked = false;
            if (sources != null)
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    if (ReferenceEquals(t, sources[i]))
                    {
                        parked = true;
                        break;
                    }
                }
            }

            if (parked)
            {
                continue;
            }

            // Give the amount back to the stack it was split from. Split(1) only hands
            // back the parked stack itself when it held the last unit; otherwise it is a
            // fresh stack whose Num was already taken off the bulk, so destroying it
            // outright would make those materials vanish.
            Thing? src = (splitFrom != null && idx < splitFrom.Count) ? splitFrom[idx] : null;
            if (src != null && !src.isDestroyed && !ReferenceEquals(t, src))
            {
                try
                {
                    src.SetNum(src.Num + t.Num);
                }
                catch (System.Exception __e) { Plugin.LogDebug("AI_NpcProcess.cs silent catch: " + __e.Message); }
            }

            try
            {
                // Split-off work pieces only. Last-unit Split returns the parked
                // stack itself; leave it so a cancelled Progress can resume.
                t.Destroy();
            }
            catch (System.Exception __e) { Plugin.LogDebug("AI_NpcProcess.cs silent catch: " + __e.Message); }
        }
    }

}


