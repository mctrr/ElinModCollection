using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using NpcLabor.TownLabor;

namespace NpcLabor.Craft;

/// <summary>
/// The cottage industry slice: standing instructions that keep a resident of the colony
/// making something — cooking for the settlement being the case it was written for.
///
/// A job is deliberately coarse. It owns a worker, a recipe and a count, and it advances
/// on the same hourly tick the rest of the mod uses. Each hour the worker puts in a shift's
/// worth of minutes (faster if their skill is above the recipe's requirement), the pantry
/// is drawn down, and finished goods go back to the shared containers of the base.
///
/// The board picks a station first, then a resident who has the skill that station needs,
/// then one of the recipes that station can make — so the player chooses the workbench and
/// the game narrows the rest (see <see cref="Stations"/>).
///
/// Two rules keep this from becoming a second, invisible economy:
///   * Work only happens in a PC faction zone. A resident who is out on a dispatch, on a
///     caravan or simply standing in town does nothing, which is what "not while we are
///     away exploring" means in practice.
///   * Ingredients are taken all-or-nothing, and a craft that cannot be finished hands
///     everything back. The pantry is never drained by a half-run.
/// </summary>
internal static class CraftManager
{
    internal static List<CraftJob> Jobs = new List<CraftJob>();

    static bool _dirty;

    /// <summary>Something changed that the save file should hear about.</summary>
    internal static bool Dirty => _dirty;

    /// <summary>Most items one visible craft act turns out, however fast the cook is.</summary>
    const int MaxBatch = 10;

    // ────────────────────────────────────────────────────────────── the act reports back

    internal static CraftJob? JobOf(int uid)
    {
        for (int i = 0; i < Jobs.Count; i++)
        {
            if (Jobs[i] != null && Jobs[i].uid == uid)
            {
                return Jobs[i];
            }
        }

        return null;
    }

    /// <summary>
    /// An act reporting back: how many items it turned out, how many shifts' worth it had
    /// to hand back, and why, if it was cut short. Runs on the game thread, like the tick.
    /// <paramref name="problem"/> is false for a plain interruption, which clears itself on
    /// the next tick and must not read as a stalled order.
    /// </summary>
    internal static void NoteMade(int uid, int made, int refundItems, string? note, bool problem = false)
    {
        CraftJob? job = JobOf(uid);
        if (job == null)
        {
            return;
        }

        if (made > 0)
        {
            job.produced += made;
            _dirty = true;
        }

        if (refundItems > 0)
        {
            int cost = CraftEngine.MinutesPerItem(job.recipeId);
            if (cost <= 0)
            {
                cost = 30;
            }

            job.minutes += (double)refundItems * cost;
        }

        if (!string.IsNullOrEmpty(note))
        {
            job.Wait(note, problem);
        }
        else
        {
            job.Running("");
        }
    }

    /// <summary>Put unfinished ingredients back in the pantry, or at the worker's feet.</summary>
    internal static void ReturnToPantry(IList<Thing> things, Chara? worker)
    {
        if (things == null || things.Count == 0)
        {
            return;
        }

        List<Thing> pantry = Pantry();
        for (int i = 0; i < things.Count; i++)
        {
            Thing t = things[i];
            if (t == null || t.isDestroyed)
            {
                continue;
            }

            bool stored = false;
            for (int k = 0; k < pantry.Count && !stored; k++)
            {
                stored = Put(pantry[k], t);
            }

            if (!stored)
            {
                DropAt(worker?.pos, t);
            }
        }
    }

    /// <summary>Put a finished item away — the craft act owns the product, not the tick.</summary>
    internal static void DepositProduct(Thing product, Chara worker) => Deposit(product, worker);

    /// <summary>
    /// Close out every base-production act still running on the map that is about to be
    /// deactivated, so the ingredients it drew from the pantry go back into a container
    /// instead of being stranded on the bench.
    ///
    /// <para>
    /// Vanilla discards AI on teardown by assigning the field directly
    /// (<c>Map.OnDeactivate</c>), which bypasses <c>Chara.SetAI</c> and therefore never
    /// reaches <see cref="AIAct.OnCancel"/>. The act's own interrupted path is the only
    /// thing that returns the stacks, so it has to be invoked from here. Hooked from
    /// <c>Map.OnDeactivate</c> with the map still intact — that is the last frame where
    /// the containers are still scannable.
    /// </para>
    /// </summary>
    internal static void AbandonActsOnLeave(Map? leaving)
    {
        try
        {
            List<Chara>? charas = leaving?.charas;
            if (charas == null || charas.Count == 0)
            {
                return;
            }

            // Snapshot: settling mutates job state and can move items, not this list.
            Chara[] snapshot = charas.ToArray();
            int settled = 0;

            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i]?.ai is CraftActAi act)
                {
                    act.AbortForZoneLeave();
                    settled++;
                }
            }

            if (settled > 0)
            {
                Plugin.LogInfo($"craft acts settled on zone leave: {settled}");
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft acts on leave: " + ex.Message);
        }
    }

    /// <summary>Items the instruction still wants, or 0 when it never stops.</summary>
    static int Remaining(CraftJob job)
    {
        switch (job.mode)
        {
            case CraftMode.Count:
                return Math.Max(0, Math.Max(1, job.target) - job.produced);

            case CraftMode.Keep:
                return Math.Max(0, Math.Max(1, job.target) - CountProduct(job.recipeId));

            default:
                return 0;
        }
    }

    /// <summary>
    /// Recipes with no bench work on the spot. Vanilla marks those "self" / "none" / "x"
    /// in the sheet, and <see cref="CraftEngine.NeedFactory"/> says the same thing.
    /// </summary>
    static bool IsRealFactory(string factory)
    {
        if (string.IsNullOrEmpty(factory))
        {
            return false;
        }

        return !string.Equals(factory, "self", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(factory, "none", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(factory, "x", StringComparison.OrdinalIgnoreCase);
    }

    static bool AtBench(Chara worker, Thing bench)
    {
        try
        {
            return worker.Dist(bench) <= 2;
        }
        catch
        {
            return false;
        }
    }

    // ────────────────────────────────────────────────────────────── queries

    internal static bool Has(int uid)
    {
        for (int i = 0; i < Jobs.Count; i++)
        {
            if (Jobs[i] != null && Jobs[i].uid == uid)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsRunning => Jobs.Count > 0;

    /// <summary>Residents who could be given an instruction: home, alive, not the PC.</summary>
    internal static List<Chara> Candidates()
    {
        var found = new List<Chara>();

        try
        {
            List<Chara>? charas = EClass._map?.charas;
            if (charas == null)
            {
                return found;
            }

            for (int i = 0; i < charas.Count; i++)
            {
                Chara c = charas[i];
                if (c == null || c.isDead || c.IsPC)
                {
                    continue;
                }

                if (!c.IsPCFactionOrMinion)
                {
                    continue;
                }

                found.Add(c);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("craft candidates: " + ex.Message);
        }

        return found;
    }

    /// <summary>Recipes the given bench can make, in the sheet's own order.</summary>
    internal static List<string> RecipesForBench(Thing? bench)
    {
        var list = new List<string>();

        List<string> ids = CraftEngine.RecipeIds();
        for (int i = 0; i < ids.Count; i++)
        {
            string id = ids[i];

            RecipeSource? src = CraftEngine.Source(id);
            if (src == null || src.isChara || src.noListing || src.isBridgePillar)
            {
                continue;
            }

            if (CraftEngine.BenchMakes(bench, id))
            {
                list.Add(id);
            }
        }

        return list;
    }

    /// <summary>Standing orders that the given bench is able to carry out.</summary>
    internal static List<CraftJob> OrdersFor(Thing? bench)
    {
        var found = new List<CraftJob>();

        for (int i = 0; i < Jobs.Count; i++)
        {
            CraftJob job = Jobs[i];
            if (job != null && CraftEngine.BenchMakes(bench, job.recipeId))
            {
                found.Add(job);
            }
        }

        return found;
    }

    /// <summary>Bench names for a recipe, joined for a hint line ("大锅、篝火").</summary>
    internal static string BenchNamesOf(string recipeId)
    {
        List<string> names = CraftEngine.FactoryNames(recipeId);
        return names.Count == 0 ? "" : string.Join("、", names.ToArray());
    }

    /// <summary>Assign a recipe to a resident. Replaces whatever they were making before.</summary>
    internal static CraftJob? Assign(Chara who, string recipeId, CraftMode mode, int target)
    {
        if (who == null || string.IsNullOrEmpty(recipeId))
        {
            return null;
        }

        Cancel(who.uid);

        var job = new CraftJob
        {
            uid = who.uid,
            name = SafeName(who),
            recipeId = recipeId,
            mode = mode,
            target = Math.Max(1, target),
        };

        Jobs.Add(job);
        _dirty = true;
        return job;
    }

    /// <summary>Drop every instruction (the "careful, this is not the same as stop" case).</summary>
    internal static void Clear()
    {
        Jobs.Clear();
        _dirty = true;
    }

    internal static void Cancel(int uid)
    {
        for (int i = Jobs.Count - 1; i >= 0; i--)
        {
            if (Jobs[i] != null && Jobs[i].uid == uid)
            {
                Jobs.RemoveAt(i);
                _dirty = true;
            }
        }
    }

    // ────────────────────────────────────────────────────────────── hour tick

    internal static void OnSimulateHour()
    {
        if (Jobs.Count == 0)
        {
            return;
        }

        for (int i = 0; i < Jobs.Count; i++)
        {
            CraftJob? job = Jobs[i];
            if (job == null)
            {
                continue;
            }

            try
            {
                Advance(job);
            }
            catch (Exception ex)
            {
                job.Wait(LaborText.T("craft.note.failed"), problem: true);
                Plugin.LogWarn("craft tick: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// The player just entered a zone. Standing production is only reported here and when a
    /// craft actually stops for want of materials — there is no board page for it any more,
    /// so this is how an order becomes visible without opening anything.
    ///
    /// Only real problems are named (missing materials, no bench, a failed act). Ordinary
    /// in-between states — production only runs while the player stands at their own base, a
    /// resident busy on another labor, a worker still walking — are reported as running: the
    /// player does not need a line explaining why an order is quietly waiting.
    /// </summary>
    internal static void OnZoneEntered()
    {
        if (Jobs.Count == 0)
        {
            return;
        }

        try
        {
            Zone? zone = EClass._zone;
            if (zone == null || !zone.IsPCFaction)
            {
                return;
            }

            int active = 0;
            int problems = 0;
            string detail = "";
            var where = new List<string>();

            for (int i = 0; i < Jobs.Count; i++)
            {
                CraftJob job = Jobs[i];
                if (job == null || job.done)
                {
                    continue;
                }

                active++;

                // Name the bench, so the line doubles as "where do I go to cancel this".
                string bench = BenchNamesOf(job.recipeId);
                if (bench.Length > 0 && !where.Contains(bench))
                {
                    where.Add(bench);
                }

                // Only a note the player can act on counts as a problem. "Only works at your
                // own base" is what a note reads after a trip away, so surfacing it at the
                // base door is noise — those orders are simply running.
                if (!job.stalled || !job.noteProblem)
                {
                    continue;
                }

                problems++;
                if (detail.Length == 0)
                {
                    detail = (bench.Length > 0 ? bench + " · " : "") + job.name + " · " + job.note;
                }
            }

            string benches = where.Count == 0
                ? LaborText.T("craft.ui.noStation")
                : string.Join("、", where.ToArray());

            if (problems > 0)
            {
                Msg.SayRaw(LaborText.T("craft.msg.waiting", problems.ToString(), detail));
            }
            else if (active > 0)
            {
                Msg.SayRaw(LaborText.T("craft.msg.running", active.ToString(), benches));
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft zone enter: " + ex.Message);
        }
    }

    static void Advance(CraftJob job)
    {
        if (job.done)
        {
            return;
        }

        Chara? worker = null;
        try
        {
            worker = RefChara.Get(job.uid);
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft worker: " + ex.Message);
        }

        if (worker == null || worker.isDead)
        {
            job.Wait(LaborText.T("craft.note.workerGone"), problem: true);
            return;
        }

        Zone? zone = EClass._zone;
        if (zone == null || !zone.IsPCFaction)
        {
            // Base production only advances while the player stands at their own base: a
            // normal pause, not something the player needs a line about.
            job.Wait(LaborText.T("craft.note.notHome"), problem: false);
            return;
        }

        Zone? here = worker.currentZone;
        if (here == null || here.uid != zone.uid)
        {
            job.Wait(LaborText.T("craft.note.awayWorker"), problem: false);
            return;
        }

        try
        {
            if (LaborBusy.IsBusyElsewhere(job.uid))
            {
                job.Wait(LaborText.T("craft.note.busy"), problem: false);
                return;
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft busy: " + ex.Message);
        }

        if (Satisfied(job, out int stock))
        {
            if (job.mode == CraftMode.Count)
            {
                job.done = true;
                job.Running("");
            }
            else
            {
                job.Running(LaborText.T("craft.note.stocked", stock.ToString()));
            }

            return;
        }

        // A recipe that needs a bench cannot start until one that can actually make it is
        // found, and the worker should be standing at it — that is the "walk over and do it
        // themselves" part. Whether a bench qualifies is the bench trait's own answer
        // (CraftEngine.BenchMakes), the same rule the craft panel uses.
        Thing? bench = null;
        if (IsRealFactory(CraftEngine.Factory(job.recipeId) ?? ""))
        {
            bench = FindStationFor(job.recipeId, worker);
            if (bench == null)
            {
                job.Wait(LaborText.T("craft.note.noBench", BenchNamesOf(job.recipeId)), problem: true);
                return;
            }
        }

        // Already at it: the act reports back when it has finished, so leave it alone.
        if (worker.ai is CraftActAi working && working.JobUid == job.uid)
        {
            job.Running(LaborText.T("craft.note.working"));
            return;
        }

        // Materials are checked before the walk, not after it. Read the other way round a
        // shortfall only surfaced once the worker had already arrived, and until then the
        // board said "walking" — so a resident standing at a bench it could not use looked
        // like a pathing problem.
        List<Thing> pantry = Pantry();
        if (!CraftEngine.CanSupply(pantry, job.recipeId, out string missing))
        {
            string note = LaborText.T("craft.note.missing", missing);
            bool firstTime = !job.stalled || !string.Equals(job.note, note, StringComparison.Ordinal);
            job.Wait(note, problem: true);

            // Tell the player once per stall, not once per in-game hour: the tick runs hourly
            // and the base is where the player is standing when this happens.
            if (firstTime)
            {
                Msg.SayRaw(LaborText.T("craft.msg.missing", job.name, missing));
            }

            return;
        }

        if (bench != null && !AtBench(worker, bench))
        {
            job.Running(LaborText.T("craft.note.walking"));
            if (worker.ai is not AI_Goto)
            {
                try
                {
                    worker.SetAI(new AI_Goto(bench, 1));
                }
                catch (Exception ex)
                {
                    Plugin.LogDebug("craft goto: " + ex.Message);
                }
            }

            return;
        }

        // At the bench, still banking shift minutes towards the next item. The note must not
        // be cleared here: on a slow recipe the resident stands still for several in-game
        // hours, and an empty note made that look like nothing was happening.
        job.Running(LaborText.T("craft.note.working"));

        double rate = CraftEngine.WorkRate(job.recipeId, worker) * 60.0;
        job.minutes += rate;

        int cost = CraftEngine.MinutesPerItem(job.recipeId);
        if (cost <= 0)
        {
            cost = 30;
        }

        if (job.minutes < cost)
        {
            return;
        }

        // A shift at the bench is one visible act. A resident whose skill is above the
        // recipe gets through more than one item in that shift, and the batch carries
        // exactly that many — which is what makes a good cook worth assigning.
        int batch = (int)(job.minutes / cost);
        if (batch < 1)
        {
            batch = 1;
        }

        if (batch > MaxBatch)
        {
            batch = MaxBatch;
        }

        int want = Remaining(job);
        if (want > 0 && batch > want)
        {
            batch = want;
        }

        // The shift's minutes are spent now; the act hands them back for anything it
        // could not finish, so an interruption never eats a resident's time.
        job.minutes -= (double)batch * cost;
        job.Running(LaborText.T("craft.note.working"));
        _dirty = true;

        try
        {
            worker.SetAI(new CraftActAi
            {
                JobUid = job.uid,
                RecipeId = job.recipeId,
                Count = batch,
                Bench = bench,
            });
        }
        catch (Exception ex)
        {
            job.minutes += (double)batch * cost;
            job.Wait(LaborText.T("craft.note.failed"), problem: true);
            Plugin.LogWarn("craft start act: " + ex.Message);
        }
    }

    /// <summary>Has the instruction been met? "Keep" also needs the pantry counted.</summary>
    static bool Satisfied(CraftJob job, out int stock)
    {
        stock = 0;

        switch (job.mode)
        {
            case CraftMode.Count:
                return job.produced >= Math.Max(1, job.target);

            case CraftMode.Keep:
                stock = CountProduct(job.recipeId);
                return stock >= Math.Max(1, job.target);

            default:
                return false;
        }
    }

    // ────────────────────────────────────────────────────────────── pantry

    /// <summary>Shared containers in the current zone — the colony's pantry.</summary>
    internal static List<Thing> Pantry()
    {
        var list = new List<Thing>();

        try
        {
            List<Thing>? things = EClass._map?.things;
            if (things == null)
            {
                return list;
            }

            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t == null || t.isDestroyed)
                {
                    continue;
                }

                bool shared = false;
                try
                {
                    shared = t.IsSharedContainer;
                }
                catch (Exception ex)
                {
                    Plugin.LogDebug("craft shared: " + ex.Message);
                }

                if (shared)
                {
                    list.Add(t);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("craft pantry: " + ex.Message);
        }

        return list;
    }

    /// <summary>How many finished items of a recipe currently sit in the pantry.</summary>
    internal static int CountProduct(string recipeId)
    {
        string? pid = CraftEngine.ProductId(recipeId);
        if (string.IsNullOrEmpty(pid))
        {
            return 0;
        }

        int n = 0;
        List<Thing> pantry = Pantry();

        for (int i = 0; i < pantry.Count; i++)
        {
            List<Thing> items = CraftEngine.Contents(pantry[i]);
            for (int k = 0; k < items.Count; k++)
            {
                try
                {
                    if (string.Equals(items[k].id, pid, StringComparison.OrdinalIgnoreCase))
                    {
                        n += Math.Max(1, items[k].Num);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.LogDebug("craft count: " + ex.Message);
                }
            }
        }

        return n;
    }

    // ────────────────────────────────────────────────────────────── output

    /// <summary>
    /// Put a finished item away: into a shared container of the base (the public box the
    /// player already uses as the pantry), at the worker's feet when none will take it, and
    /// in the worker's hands as the very last resort so a product is never lost.
    /// </summary>
    static void Deposit(Thing product, Chara worker)
    {
        if (product == null)
        {
            return;
        }

        List<Thing> pantry = Pantry();
        for (int i = 0; i < pantry.Count; i++)
        {
            if (Put(pantry[i], product))
            {
                return;
            }
        }

        if (DropAt(worker?.pos, product))
        {
            return;
        }

        // Nowhere to put it: keep it in the worker's hands rather than losing it.
        try
        {
            worker?.AddThing(product);
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("craft deposit: " + ex.Message);
        }
    }

    static bool Put(Thing container, Thing product)
    {
        try
        {
            if (container == null || product == null || container.things == null)
            {
                return false;
            }

            container.AddThing(product);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft put: " + ex.Message);
            return false;
        }
    }

    static bool DropAt(Point? pos, Thing product)
    {
        if (pos == null || product == null)
        {
            return false;
        }

        try
        {
            EClass._zone.AddCard(product, pos);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft drop: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// The bench that can actually make this recipe — the bench trait decides
    /// (<see cref="CraftEngine.BenchMakes"/>), which is the same rule its craft panel uses.
    /// When the base has several benches that qualify, the nearest to the worker wins, so two
    /// cooks do not queue for one pot.
    /// </summary>
    static Thing? FindStationFor(string recipeId, Chara? near)
    {
        try
        {
            List<Thing>? things = EClass._map?.things;
            if (things == null)
            {
                return null;
            }

            Thing? best = null;
            int bestDist = int.MaxValue;

            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t == null || t.isDestroyed || t.trait is not TraitCrafter)
                {
                    continue;
                }

                if (!CraftEngine.BenchMakes(t, recipeId))
                {
                    continue;
                }

                if (near == null)
                {
                    return t;
                }

                int dist;
                try
                {
                    dist = near.Dist(t);
                }
                catch
                {
                    dist = int.MaxValue;
                }

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = t;
                }
            }

            return best;
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft station: " + ex.Message);
        }

        return null;
    }

    static string SafeName(Chara c)
    {
        try
        {
            return c.Name ?? "";
        }
        catch
        {
            return "";
        }
    }

    // ────────────────────────────────────────────────────────────── save / load

    static string? SavePath()
    {
        try
        {
            string root = GameIO.pathCurrentSave;
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            return Path.Combine(root, "npclabor_craft.json");
        }
        catch
        {
            return null;
        }
    }

    internal static void Save()
    {
        _dirty = false;

        try
        {
            string? path = SavePath();
            if (path == null)
            {
                return;
            }

            File.WriteAllText(path, JsonConvert.SerializeObject(Jobs, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("craft save failed: " + ex.Message);
        }
    }

    internal static void Load()
    {
        Jobs = new List<CraftJob>();
        CraftEngine.Invalidate();

        try
        {
            string? path = SavePath();
            if (path == null || !File.Exists(path))
            {
                return;
            }

            List<CraftJob>? jobs = JsonConvert.DeserializeObject<List<CraftJob>>(File.ReadAllText(path));
            if (jobs != null)
            {
                Jobs = jobs;
            }

            Plugin.LogDebug("craft loaded " + Jobs.Count + " job(s)");
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("craft load failed: " + ex.Message);
        }
    }

    internal static string Describe()
    {
        if (Jobs.Count == 0)
        {
            return LaborText.T("craft.state.none");
        }

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Jobs.Count; i++)
        {
            sb.AppendLine("  " + Jobs[i].Describe());
        }

        return sb.ToString().TrimEnd();
    }
}
