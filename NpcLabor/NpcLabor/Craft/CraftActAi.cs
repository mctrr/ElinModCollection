using System;
using System.Collections.Generic;
using NpcLabor.TownLabor;

namespace NpcLabor.Craft;

/// <summary>
/// The visible half of base production: a resident walks to the bench and works through
/// the same progress bar vanilla uses at a crafting station (<c>AI_UseCrafter</c>) — the
/// bench plays its own animation and sound, the ingredients lie out on it, and the goods
/// only exist once that progress ends. Nothing is conjured out of thin air.
///
/// Vanilla's own craft is not called, for the reasons in <see cref="CraftEngine"/>: it
/// grades quality and hands the product to <c>EClass.pc</c>. Only the presentation is
/// borrowed, and it is borrowed from the real traits, so a cooking pot bubbles and sounds
/// exactly like it does for the player.
///
/// The act owns its ingredients for its whole life. It takes them out of the pantry when
/// it starts, and hands them back if it is interrupted (a player order, the resident
/// being teleported out, the bench being destroyed). The shift's minutes for whatever it
/// did not finish are refunded through <see cref="CraftManager.NoteMade"/>.
/// </summary>
internal sealed class CraftActAi : AIAct
{
    /// <summary>Which job this act is working for, so the tick leaves it alone.</summary>
    internal int JobUid;

    internal string RecipeId = "";

    /// <summary>Items this one act will turn out — one ingredient batch each.</summary>
    internal int Count = 1;

    /// <summary>Bench to work at, or null for a recipe with no factory ("self").</summary>
    internal Thing? Bench;

    readonly List<List<Thing>> _batches = new List<List<Thing>>();
    bool _done;
    bool _settled;
    int _made;
    int _beat;

    public override bool CanManualCancel() => false;

    /// <summary>Standing still at a bench is the point; a nudge is not a reason to drop the work.</summary>
    public override bool CancelWhenMoved => false;

    public override void OnStart()
    {
        try
        {
            TraitCrafter? crafter = Crafter();
            if (crafter != null && crafter.Icon != Emo.none)
            {
                owner.ShowEmo(crafter.Icon);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft act emo: " + ex.Message);
        }
    }

    public override void OnCancel()
    {
        Settle(LaborText.T("craft.note.interrupted"));
    }

    /// <summary>
    /// Zone teardown bypass for the settle path.
    ///
    /// <para>
    /// <c>Map.OnDeactivate</c> drops every AI with a plain field assignment
    /// (<c>chara.ai = new NoGoal()</c>), which skips <c>Chara.SetAI</c> and therefore
    /// never calls <see cref="OnCancel"/>. The ingredients this act took out of the
    /// pantry are already removed from their container and laid out on the bench, so
    /// without this hook leaving the base mid-shift stranded them on the bench as
    /// loose world items — invisible to the pantry scan — and the order then sat on
    /// "missing materials" forever while the wood was visibly right there.
    /// </para>
    ///
    /// <para>
    /// Must run <b>before</b> the map is torn down: returning the stacks needs the
    /// containers, and the job bookkeeping still needs to be writable.
    /// </para>
    /// </summary>
    internal void AbortForZoneLeave()
    {
        Settle(LaborText.T("craft.note.interrupted"));
    }

    public override IEnumerable<Status> Run()
    {
        Chara? maker = owner;
        if (maker == null || maker.isDead || string.IsNullOrEmpty(RecipeId) || Count <= 0)
        {
            Settle(null);
            yield break;
        }

        TraitCrafter? crafter = Crafter();

        // 1. draw the ingredients for everything this act will finish, up front, so the
        //    bench is laid out with them and nothing appears later from nowhere.
        if (!TakeAll())
        {
            Settle(LaborText.T("craft.note.missing", "?"), problem: true);
            yield break;
        }

        // 2. light the bench and put the ingredients where the player can see them.
        WarmUp(crafter);
        LayOut(crafter, maker);

        // 3. the work: same beats as vanilla's crafting station, minus its UI layer.
        Progress_Custom progress = new Progress_Custom
        {
            cancelWhenMoved = false,
            canProgress = () => CanProgress(maker),
            onProgress = _ => Beat(maker, crafter),
            onProgressComplete = () => Finish(maker, crafter),
        }.SetDuration(Duration(maker), 5);

        yield return Do(progress);

        if (!_done)
        {
            Settle(LaborText.T("craft.note.interrupted"));
            yield break;
        }

        Cool(crafter);
        Settle(null);
        yield break;
    }

    // ────────────────────────────────────────────────────────────── materials

    /// <summary>
    /// One batch of ingredients per item. A shortfall (the pantry ran dry since the tick
    /// looked) simply means fewer items, never a half-made one.
    /// </summary>
    bool TakeAll()
    {
        // A reset (zone change, reload) re-runs this act from the top; the ingredients it
        // already took are still its own, so it must not draw a second set.
        if (_batches.Count > 0)
        {
            return true;
        }

        List<Thing> pantry = CraftManager.Pantry();
        for (int i = 0; i < Count; i++)
        {
            var batch = new List<Thing>();
            if (!CraftEngine.TakeMaterials(pantry, RecipeId, batch))
            {
                break;
            }

            _batches.Add(batch);
        }

        return _batches.Count > 0;
    }

    /// <summary>
    /// A consuming bench (the pot, the microwave) gets the ingredients laid out on it, so
    /// the food is visibly in the pot. A bench that does not consume them keeps them in
    /// the worker's hands instead.
    /// </summary>
    void LayOut(TraitCrafter? crafter, Chara maker)
    {
        Point? at = null;
        Thing? bench = Bench;
        if (bench != null && !bench.isDestroyed && bench.ExistsOnMap)
        {
            at = bench.pos;
        }

        bool onBench = at != null && (crafter == null || IsConsumeIng(crafter));
        bool hide = crafter != null && crafter.animeType == TraitCrafter.AnimeType.Microwave;

        for (int i = 0; i < _batches.Count; i++)
        {
            List<Thing> batch = _batches[i];
            for (int k = 0; k < batch.Count; k++)
            {
                Thing t = batch[k];
                if (t == null || t.isDestroyed)
                {
                    continue;
                }

                try
                {
                    t.parent?.RemoveCard(t);

                    if (onBench && at != null)
                    {
                        Card? card = EClass._zone?.AddCard(t, at);
                        if (card != null)
                        {
                            card.altitude = 0;
                            if (hide)
                            {
                                card.isHidden = true;
                            }
                        }
                    }
                    else
                    {
                        maker.AddThing(t);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogDebug("craft lay out: " + ex.Message);
                }
            }
        }
    }

    /// <summary>Hand anything that never made it into an item back to the pantry.</summary>
    void ReturnIngredients()
    {
        if (_batches.Count == 0)
        {
            return;
        }

        var leftovers = new List<Thing>();
        for (int i = 0; i < _batches.Count; i++)
        {
            List<Thing> batch = _batches[i];
            for (int k = 0; k < batch.Count; k++)
            {
                if (batch[k] != null)
                {
                    leftovers.Add(batch[k]);
                }
            }
        }

        _batches.Clear();
        CraftManager.ReturnToPantry(leftovers, owner);
    }

    // ────────────────────────────────────────────────────────────── the work

    /// <summary>Frames the progress runs. The same shift-time model as the tick, plus a beat per extra item.</summary>
    int Duration(Chara maker)
    {
        int baseFrames = Math.Max(20, CraftEngine.MinutesPerItem(RecipeId) * 2);

        double rate = CraftEngine.WorkRate(RecipeId, maker);
        if (rate < 0.25)
        {
            rate = 0.25;
        }

        int frames = (int)(baseFrames / rate);
        frames += 20 * Math.Max(0, _batches.Count - 1);

        if (frames < 30)
        {
            frames = 30;
        }

        return frames > 360 ? 360 : frames;
    }

    /// <summary>Keep going while the resident is alive, upright, and the bench is still there.</summary>
    bool CanProgress(Chara maker)
    {
        try
        {
            if (maker == null || maker.isDead || !maker.ExistsOnMap)
            {
                return false;
            }

            Thing? bench = Bench;
            if (bench == null)
            {
                return true;
            }

            if (bench.isDestroyed || !bench.ExistsOnMap)
            {
                return false;
            }

            TraitCrafter? crafter = Crafter();
            if (crafter != null && RequiresOn(crafter) && !bench.isOn)
            {
                return false;
            }

            return maker.Dist(bench) <= 3;
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft can progress: " + ex.Message);
            return true;
        }
    }

    /// <summary>One frame-beat of the work: the bench's animation and sound, vanilla's cadence.</summary>
    void Beat(Chara maker, TraitCrafter? crafter)
    {
        _beat++;

        try
        {
            Thing? bench = Bench;

            if (crafter != null && bench != null && !bench.isDestroyed && bench.ExistsOnMap)
            {
                maker.LookAt(bench);

                if (!string.IsNullOrEmpty(crafter.idSoundProgress))
                {
                    maker.PlaySound(crafter.idSoundProgress);
                }

                // Vanilla animates the bench itself for the pot and the microwave only.
                if (crafter.animeType == TraitCrafter.AnimeType.Pot
                    || crafter.animeType == TraitCrafter.AnimeType.Microwave)
                {
                    bench.renderer.PlayAnime(crafter.IdAnimeProgress);
                }

                AnimateIngredients(crafter.IdAnimeProgress);
                return;
            }

            // Working by hand: no bench to rattle, so the worker carries the beat.
            if (_beat % 4 == 0)
            {
                maker.PlaySound("craft");
            }

            if (crafter != null)
            {
                AnimateIngredients(crafter.IdAnimeProgress);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft beat: " + ex.Message);
        }
    }

    void AnimateIngredients(AnimeID anime)
    {
        for (int i = 0; i < _batches.Count; i++)
        {
            List<Thing> batch = _batches[i];
            for (int k = 0; k < batch.Count; k++)
            {
                Thing t = batch[k];
                try
                {
                    if (t != null && !t.isDestroyed && t.ExistsOnMap)
                    {
                        t.renderer.PlayAnime(anime);
                    }
                }
                catch
                {
                }
            }
        }
    }

    /// <summary>The finish: the puff, the knock, and only now the goods.</summary>
    void Finish(Chara maker, TraitCrafter? crafter)
    {
        if (_done)
        {
            return;
        }

        _done = true;

        try
        {
            if (crafter != null && !string.IsNullOrEmpty(crafter.idSoundComplete))
            {
                maker.PlaySound(crafter.idSoundComplete);
            }

            EClass.Sound.Play("craft");

            Point? at = null;
            Thing? bench = Bench;
            if (bench != null && !bench.isDestroyed && bench.ExistsOnMap)
            {
                at = bench.pos;
            }
            else if (maker.pos != null)
            {
                at = maker.pos;
            }

            if (at != null)
            {
                Effect.Get("smoke").Play(at);
                Effect.Get("mine").Play(at).Emit(6 + EClass.rnd(6));
            }

            maker.renderer.PlayAnime(AnimeID.JumpSmall);
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft finish fx: " + ex.Message);
        }

        for (int i = 0; i < _batches.Count; i++)
        {
            List<Thing> batch = _batches[i];
            Thing? product = CraftEngine.Produce(RecipeId, maker, batch);
            if (product == null)
            {
                break;
            }

            CraftManager.DepositProduct(product, maker);

            for (int k = 0; k < batch.Count; k++)
            {
                Consume(batch[k]);
            }

            _made++;
        }

        // Consumed batches are done with; anything left is for the settle path to return.
        if (_made > 0)
        {
            _batches.RemoveRange(0, _made);
        }
    }

    static void Consume(Thing? t)
    {
        try
        {
            if (t != null && !t.isDestroyed)
            {
                t.Destroy();
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft act consume: " + ex.Message);
        }
    }

    // ────────────────────────────────────────────────────────────── bench / report

    void WarmUp(TraitCrafter? crafter)
    {
        if (crafter == null)
        {
            return;
        }

        try
        {
            Thing? bench = Bench;
            if (bench == null || bench.isDestroyed)
            {
                return;
            }

            if (RequiresOn(crafter) && !bench.isOn)
            {
                crafter.Toggle(true);
            }

            if (!string.IsNullOrEmpty(crafter.idSoundBG))
            {
                SE.Play(crafter.idSoundBG);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft warm up: " + ex.Message);
        }
    }

    void Cool(TraitCrafter? crafter)
    {
        if (crafter == null)
        {
            return;
        }

        try
        {
            Thing? bench = Bench;
            if (crafter.AutoTurnOff && bench != null && !bench.isDestroyed && bench.isOn)
            {
                crafter.Toggle(false, true);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft cool: " + ex.Message);
        }

        StopSounds(crafter);
    }

    void StopSounds(TraitCrafter? crafter)
    {
        try
        {
            if (crafter != null && !string.IsNullOrEmpty(crafter.idSoundBG))
            {
                EClass.Sound.Stop(crafter.idSoundBG);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft stop sound: " + ex.Message);
        }
    }

    /// <summary>
    /// Close the act out exactly once: give back what was not turned into an item, stop
    /// the looped sound, and tell the job what happened (including a refund of the shift
    /// minutes it did not get to spend).
    ///
    /// <paramref name="problem"/> marks a note the player may have to act on. A plain
    /// interruption (a recall, a teleport, the bench being taken away) is not one: it
    /// self-clears on the next tick, so it must not be reported as a stalled order.
    /// </summary>
    void Settle(string? note, bool problem = false)
    {
        if (_settled)
        {
            return;
        }

        _settled = true;

        ReturnIngredients();
        StopSounds(Crafter());

        CraftManager.NoteMade(JobUid, Math.Max(0, _made), Math.Max(0, Count - _made), note, problem);
    }

    TraitCrafter? Crafter()
    {
        try
        {
            Thing? bench = Bench;
            if (bench == null || bench.isDestroyed)
            {
                return null;
            }

            return bench.trait as TraitCrafter;
        }
        catch
        {
            return null;
        }
    }

    static bool IsConsumeIng(TraitCrafter crafter)
    {
        try
        {
            return crafter.IsConsumeIng;
        }
        catch
        {
            return true;
        }
    }

    static bool RequiresOn(TraitCrafter crafter)
    {
        try
        {
            return crafter.IsRequireFuel || crafter.ToggleType != ToggleType.None;
        }
        catch
        {
            return false;
        }
    }
}
