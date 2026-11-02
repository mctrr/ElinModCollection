using System;
using System.Collections.Generic;
using System.Text;
using ReflexCLI.Attributes;

namespace NpcLabor.Craft;

/// <summary>
/// Reflex console commands for slice G (base production).
///
///   NpcLaborCraftRecipes [fragment]     — recipes a resident could be told to make
///   NpcLaborCraftNeed    cook_meat      — ingredient slots and whether the pantry can fill them
///   NpcLaborCraftBench   cook_meat      — the bench a recipe asks for, and the zone's benches
///   NpcLaborCraftStations               — the stations the board would offer (recipe counts)
///   NpcLaborCraftPantry                 — the shared containers and what is in them
///   NpcLaborCraftAssign  1234 cook_meat keep 20 — assign by character uid
///   NpcLaborCraftCancel  1234           — drop one assignment
///   NpcLaborCraftState                  — what everyone at the base is making
///   NpcLaborCraftTick                   — run one production hour right now (debug)
///
/// Useful when a recipe refuses to start: NpcLaborCraftNeed prints the exact slots the
/// game wants, which is the fastest way to tell "the pantry really is missing it" from
/// "the ingredient matcher does not like the item".
/// </summary>
[ConsoleCommandClassCustomizer("")]
internal static class CraftConsole
{
    const int MaxLines = 60;

    [ConsoleCommand("")]
    public static string NpcLaborCraftRecipes(string fragment = "")
    {
        try
        {
            var sb = new StringBuilder();
            List<string> ids = CraftEngine.RecipeIds();
            sb.AppendLine("craftable recipes: " + ids.Count);

            int shown = 0;
            for (int i = 0; i < ids.Count && shown < MaxLines; i++)
            {
                string id = ids[i];
                string name = CraftEngine.Name(id);
                if (!string.IsNullOrEmpty(fragment)
                    && id.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) < 0
                    && name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                sb.AppendLine("  " + id.PadRight(28) + " " + name
                    + "  →" + (CraftEngine.ProductId(id) ?? "?")
                    + "  bench=" + Describe(CraftEngine.Factory(id)));
                shown++;
            }

            if (shown == 0)
            {
                sb.AppendLine("  (no match)");
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("NpcLaborCraftRecipes: " + ex.Message);
            return "NpcLaborCraftRecipes failed: " + ex.Message;
        }
    }

    [ConsoleCommand("")]
    public static string NpcLaborCraftNeed(string recipeId)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("recipe " + recipeId + "  (" + CraftEngine.Name(recipeId) + ")");
            sb.AppendLine("  product " + (CraftEngine.ProductId(recipeId) ?? "?")
                + "  bench=" + Describe(CraftEngine.Factory(recipeId))
                + "  minutes/item=" + CraftEngine.MinutesPerItem(recipeId));
            sb.AppendLine("  skill required=" + CraftEngine.RequiredSkillValue(recipeId)
                + " (" + CraftEngine.RequiredSkillId(recipeId) + ")");

            List<Thing> pantry = CraftManager.Pantry();
            sb.AppendLine("  pantry containers=" + pantry.Count);

            List<Recipe.Ingredient> ings = CraftEngine.Ingredients(recipeId);
            for (int i = 0; i < ings.Count; i++)
            {
                Recipe.Ingredient ing = ings[i];
                sb.AppendLine("  need " + CraftEngine.IngredientName(ing)
                    + " ×" + CraftEngine.Need(ing)
                    + "  have " + CraftEngine.CountIn(pantry, ing));
            }

            if (ings.Count == 0)
            {
                sb.AppendLine("  (recipe takes no ingredients)");
            }

            sb.AppendLine("  supply: " + (CraftEngine.CanSupply(pantry, recipeId, out string missing)
                ? "ready"
                : "missing " + missing));

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("NpcLaborCraftNeed: " + ex.Message);
            return "NpcLaborCraftNeed failed: " + ex.Message;
        }
    }

    /// <summary>
    /// Where the work will happen: the bench a recipe asks for, and every workstation in
    /// the zone with its factory id. A job stuck on "no 料理锅 at the base" is answered here.
    /// </summary>
    [ConsoleCommand("")]
    public static string NpcLaborCraftBench(string recipeId = "")
    {
        try
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(recipeId))
            {
                List<string> names = CraftEngine.FactoryNames(recipeId);
                sb.AppendLine("recipe " + recipeId + " needs bench: "
                    + (names.Count == 0 ? "(none - works by hand)" : string.Join("、", names.ToArray())));
            }

            sb.AppendLine("workstations in this zone:");

            int shown = 0;
            List<Thing>? things = EClass._map?.things;
            if (things != null)
            {
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    if (t == null || t.isDestroyed || t.trait is not TraitCrafter)
                    {
                        continue;
                    }

                    string name = "";
                    try
                    {
                        name = t.Name ?? t.id;
                    }
                    catch
                    {
                        name = "?";
                    }

                    // makes= is the authoritative answer: the bench trait decides, exactly as
                    // its own craft panel does. (factory= used to print TraitCrafter.IdSource,
                    // which is overridden only by the processor benches.)
                    sb.AppendLine("  " + (t.id ?? "?").PadRight(22) + " " + name
                        + (string.IsNullOrEmpty(recipeId)
                            ? ""
                            : "  makes=" + (CraftEngine.BenchMakes(t, recipeId) ? "yes" : "no"))
                        + "  at " + t.pos.x + "," + t.pos.z
                        + "  on=" + (t.isOn ? "yes" : "no"));
                    shown++;
                }
            }

            if (shown == 0)
            {
                sb.AppendLine("  (none - place the bench the recipe asks for)");
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("NpcLaborCraftBench: " + ex.Message);
            return "NpcLaborCraftBench failed: " + ex.Message;
        }
    }

    [ConsoleCommand("")]
    public static string NpcLaborCraftPantry()
    {
        try
        {
            var sb = new StringBuilder();
            List<Thing> pantry = CraftManager.Pantry();
            sb.AppendLine("shared containers in this zone: " + pantry.Count);

            for (int i = 0; i < pantry.Count; i++)
            {
                Thing c = pantry[i];
                string name = "";
                try
                {
                    name = c.Name ?? "?";
                }
                catch
                {
                    name = "?";
                }

                List<Thing> items = CraftEngine.Contents(c);
                sb.AppendLine("  [" + i + "] " + name + "  at " + c.pos.x + "," + c.pos.z
                    + "  items=" + items.Count);

                for (int k = 0; k < items.Count && k < 8; k++)
                {
                    Thing t = items[k];
                    string tn = "";
                    try
                    {
                        tn = t.Name ?? t.id;
                    }
                    catch
                    {
                        tn = "?";
                    }

                    sb.AppendLine("        " + (t.id ?? "?").PadRight(20) + " ×" + t.Num + "  " + tn);
                }
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("NpcLaborCraftPantry: " + ex.Message);
            return "NpcLaborCraftPantry failed: " + ex.Message;
        }
    }

    [ConsoleCommand("")]
    public static string NpcLaborCraftAssign(int uid, string recipeId, string mode = "endless", int target = 10)
    {
        try
        {
            Chara? who = RefChara.Get(uid);
            if (who == null)
            {
                return "no character with uid " + uid;
            }

            CraftMode m;
            switch ((mode ?? "").ToLowerInvariant())
            {
                case "count":
                    m = CraftMode.Count;
                    break;
                case "keep":
                    m = CraftMode.Keep;
                    break;
                default:
                    m = CraftMode.Endless;
                    break;
            }

            CraftJob? job = CraftManager.Assign(who, recipeId, m, target);
            if (job == null)
            {
                return "assign failed (bad uid or recipe id)";
            }

            CraftManager.Save();
            return "assigned: " + job.Describe();
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("NpcLaborCraftAssign: " + ex.Message);
            return "NpcLaborCraftAssign failed: " + ex.Message;
        }
    }

    [ConsoleCommand("")]
    public static string NpcLaborCraftCancel(int uid = 0)
    {
        try
        {
            if (uid <= 0)
            {
                int n = CraftManager.Jobs.Count;
                CraftManager.Clear();
                CraftManager.Save();
                return "cleared " + n + " assignment(s)";
            }

            CraftManager.Cancel(uid);
            CraftManager.Save();
            return "cancelled uid " + uid;
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("NpcLaborCraftCancel: " + ex.Message);
            return "NpcLaborCraftCancel failed: " + ex.Message;
        }
    }

    [ConsoleCommand("")]
    public static string NpcLaborCraftState()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("production assignments: " + CraftManager.Jobs.Count);

            for (int i = 0; i < CraftManager.Jobs.Count; i++)
            {
                CraftJob job = CraftManager.Jobs[i];
                sb.AppendLine("  uid=" + job.uid + "  " + job.Describe()
                    + "\n     recipe=" + job.recipeId
                    + " mode=" + job.mode
                    + " target=" + job.target
                    + " produced=" + job.produced
                    + " minutes=" + job.minutes.ToString("0.0")
                    + " stalled=" + job.stalled
                    + " done=" + job.done
                    + (string.IsNullOrEmpty(job.note) ? "" : " note=" + job.note));
            }

            List<Chara> pool = CraftManager.Candidates();
            sb.AppendLine("residents in this zone: " + pool.Count);
            for (int i = 0; i < pool.Count && i < 20; i++)
            {
                Chara c = pool[i];
                string n = "";
                try
                {
                    n = c.NameSimple ?? c.Name ?? "?";
                }
                catch
                {
                    n = "?";
                }

                sb.AppendLine("  uid=" + c.uid + "  " + n
                    + (CraftManager.Has(c.uid) ? "  [assigned]" : ""));
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("NpcLaborCraftState: " + ex.Message);
            return "NpcLaborCraftState failed: " + ex.Message;
        }
    }

    /// <summary>Run one production hour out of band, so a setup can be checked without waiting.</summary>
    [ConsoleCommand("")]
    public static string NpcLaborCraftTick()
    {
        try
        {
            CraftManager.OnSimulateHour();
            return "tick done\n" + CraftManager.Describe();
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("NpcLaborCraftTick: " + ex.Message);
            return "NpcLaborCraftTick failed: " + ex.Message;
        }
    }

    /// <summary>
    /// List the crafting benches standing in this zone and how many recipes each can make.
    /// Membership is the bench trait's own answer, so this is exactly what its craft panel
    /// offers — the way to tell "the pot really cannot make this" from "the pot is not here".
    /// </summary>
    [ConsoleCommand("")]
    public static string NpcLaborCraftStations()
    {
        try
        {
            var sb = new StringBuilder();
            int count = 0;

            List<Thing>? things = EClass._map?.things;
            if (things != null)
            {
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    if (t == null || t.isDestroyed || t.trait is not TraitCrafter)
                    {
                        continue;
                    }

                    string name = "";
                    try { name = t.Name ?? ""; } catch { name = ""; }

                    sb.AppendLine("  " + (t.id ?? "?").PadRight(24)
                        + " ×" + CraftManager.RecipesForBench(t).Count + "  " + name);
                    count++;
                }
            }

            int hand = CraftManager.RecipesForBench(null).Count;
            if (hand > 0)
            {
                sb.AppendLine("  " + "(hand)".PadRight(24) + " ×" + hand + "  " + LaborText.T("craft.ui.noStation"));
                count++;
            }

            return count == 0 ? "no crafting bench in this zone" : sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("NpcLaborCraftStations: " + ex.Message);
            return "NpcLaborCraftStations failed: " + ex.Message;
        }
    }

    static string Describe(string factory)
        => string.IsNullOrEmpty(factory) ? "-" : factory;
}
