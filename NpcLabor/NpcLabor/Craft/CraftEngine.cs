using System;
using System.Collections.Generic;

namespace NpcLabor.Craft;

/// <summary>
/// The recipe side of the cottage industry slice: which recipes a resident can work on,
/// what each one consumes, and how a satisfied ingredient list turns into a finished
/// Thing.
///
/// Vanilla's <c>Recipe.Craft</c> is deliberately not reused. It grades quality from
/// <c>EClass.pc</c>, hands the product straight to the PC and announces it to the PC —
/// all three are wrong for a resident cooking alone in the colony. This class performs
/// the same steps against the assigned worker instead, and leans on the vanilla helpers
/// that are worker agnostic (<c>ThingGen.Create</c>, <c>CraftUtil.MakeDish</c>), so the
/// products are ordinary game items with ordinary materials, decay and cooking data.
///
/// Everything is defensive: a recipe that cannot be resolved, a card row that is missing
/// or an exception mid-craft returns null rather than taking the hour tick down with it.
/// </summary>
internal static class CraftEngine
{
    static List<string>? _recipeIds;
    static readonly Dictionary<string, string> _productIds = new Dictionary<string, string>();

    /// <summary>Forget the cached recipe list — call after a load or a sheet reload.</summary>
    internal static void Invalidate()
    {
        _recipeIds = null;
        _productIds.Clear();
    }

    /// <summary>
    /// Every recipe a resident could reasonably be told to make: craftable, produces a
    /// real item, and not one of the terrain recipes that only make sense while standing
    /// in build mode.
    /// </summary>
    internal static List<string> RecipeIds()
    {
        if (_recipeIds != null)
        {
            return _recipeIds;
        }

        var ids = new List<string>();
        try
        {
            RecipeManager.BuildList();
            List<RecipeSource>? all = RecipeManager.list;
            if (all != null)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    RecipeSource? src = all[i];
                    if (src == null || string.IsNullOrEmpty(src.id))
                    {
                        continue;
                    }

                    if (!src.IsCraftable())
                    {
                        continue;
                    }

                    // Terrain recipes build floors and walls; a resident should not be
                    // asked to pave the colony from the quest board.
                    string type = src.type ?? "";
                    if (string.Equals(type, "Block", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(type, "Obj", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(type, "Deco", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(type, "Floor", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(ProductId(src.id)))
                    {
                        continue;
                    }

                    ids.Add(src.id);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("craft recipe list: " + ex.Message);
        }

        ids.Sort(StringComparer.OrdinalIgnoreCase);
        _recipeIds = ids;
        return ids;
    }

    internal static RecipeSource? Source(string recipeId)
    {
        if (string.IsNullOrEmpty(recipeId))
        {
            return null;
        }

        try
        {
            return RecipeManager.Get(recipeId);
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft source: " + ex.Message);
            return null;
        }
    }

    internal static Recipe? RecipeOf(string recipeId)
    {
        try
        {
            return string.IsNullOrEmpty(recipeId) ? null : Recipe.GetOrCreate(recipeId);
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft recipe: " + ex.Message);
            return null;
        }
    }

    /// <summary>Card id of what a recipe produces, or null when it makes no item.</summary>
    internal static string? ProductId(string recipeId)
    {
        if (string.IsNullOrEmpty(recipeId))
        {
            return null;
        }

        if (_productIds.TryGetValue(recipeId, out string cached))
        {
            return cached.Length == 0 ? null : cached;
        }

        string id = "";
        try
        {
            Recipe? r = Recipe.GetOrCreate(recipeId);
            id = r?.GetIdThing() ?? "";

            // Only real cards count. Terrain recipes report a tile id here, which would
            // otherwise slip a "build a wall" row into the production list.
            if (id.Length > 0 && EClass.sources?.cards?.map != null
                && !EClass.sources.cards.map.ContainsKey(id))
            {
                id = "";
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft product id: " + ex.Message);
            id = "";
        }

        _productIds[recipeId] = id;
        return id.Length == 0 ? null : id;
    }

    /// <summary>Player-facing name of a recipe, for panels and logs.</summary>
    internal static string Name(string recipeId)
    {
        try
        {
            RecipeSource? src = Source(recipeId);
            string name = src?.Name ?? "";
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft name: " + ex.Message);
        }

        return string.IsNullOrEmpty(recipeId) ? "?" : recipeId;
    }

    /// <summary>The workbench a recipe needs, or an empty string when it needs none.</summary>
    internal static string Factory(string recipeId)
    {
        try
        {
            return Source(recipeId)?.idFactory ?? "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Player-facing name of the bench a recipe needs — the cooking pot, the anvil — so the
    /// board can say what to place rather than naming an internal id. Empty for a recipe
    /// that needs no bench; falls back to the raw factory id if the sheet has no card for it.
    /// </summary>
    internal static string FactoryName(string recipeId)
    {
        try
        {
            RecipeSource? src = Source(recipeId);
            if (src == null || !src.NeedFactory)
            {
                return "";
            }

            string? name = src.NameFactory;
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }

            return src.idFactory ?? "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Can this bench make this recipe? Vanilla's own rule: the bench's trait decides
    /// (<c>Trait.Contains</c>), and it is overridden per bench — a cauldron takes both
    /// <c>cauldron</c> and <c>camppot</c> recipes, a hearth takes its own kind, and so on.
    ///
    /// This deliberately replaces an earlier comparison against <c>RecipeSource.idFactory</c>
    /// and <c>TraitCrafter.IdSource</c>. Those only line up for the processor benches
    /// (Mill / Kiln / Smelter / Spinner, the only traits that override <c>IdSource</c>), so a
    /// cooking pot never matched its own recipes and a pot full of food still reported
    /// "no campfire at the base".
    ///
    /// A null bench means work done by hand: vanilla lists exactly the <c>idFactory == "self"</c>
    /// recipes for that case.
    /// </summary>
    internal static bool BenchMakes(Thing? bench, string recipeId)
    {
        RecipeSource? src = Source(recipeId);
        if (src == null)
        {
            return false;
        }

        if (bench == null)
        {
            return string.Equals(src.idFactory ?? "", "self", StringComparison.OrdinalIgnoreCase);
        }

        try
        {
            return bench.trait != null && bench.trait.Contains(src);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Player-facing names of every bench that could make this recipe. A recipe may name
    /// several factories, so "no cooking pot at the base" can name them all instead of only
    /// the first one - which is how a pot user got told to place a campfire.
    /// </summary>
    internal static List<string> FactoryNames(string recipeId)
    {
        var names = new List<string>();

        try
        {
            string[]? factory = Source(recipeId)?.row?.factory;
            if (factory != null)
            {
                for (int i = 0; i < factory.Length; i++)
                {
                    string id = factory[i] ?? "";
                    if (id.Length == 0
                        || string.Equals(id, "x", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(id, "none", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(id, "self", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string name = id;
                    try
                    {
                        string? cardName = EClass.sources?.cards?.map?[id]?.GetName();
                        if (!string.IsNullOrEmpty(cardName))
                        {
                            name = cardName!;
                        }
                    }
                    catch
                    {
                    }

                    if (!string.IsNullOrEmpty(name) && !names.Contains(name))
                    {
                        names.Add(name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft factory names: " + ex.Message);
        }

        if (names.Count == 0)
        {
            string single = FactoryName(recipeId);
            if (!string.IsNullOrEmpty(single))
            {
                names.Add(single);
            }
        }

        return names;
    }

    internal static int RecipeLv(string recipeId)
    {
        try
        {
            return RecipeOf(recipeId)?.RecipeLv ?? 1;
        }
        catch
        {
            return 1;
        }
    }

    // ────────────────────────────────────────────────────────────── ingredients

    /// <summary>
    /// The ingredient slots of a recipe, optional ones dropped — a resident asked to
    /// cook something should not be blocked on a garnish nobody will miss.
    /// </summary>
    internal static List<Recipe.Ingredient> Ingredients(string recipeId)
    {
        var list = new List<Recipe.Ingredient>();

        try
        {
            Recipe? r = RecipeOf(recipeId);
            if (r == null)
            {
                return list;
            }

            r.BuildIngredientList();

            List<Recipe.Ingredient>? ings = r.ingredients;
            if (ings == null)
            {
                return list;
            }

            for (int i = 0; i < ings.Count; i++)
            {
                Recipe.Ingredient? ing = ings[i];
                if (ing == null || ing.optional)
                {
                    continue;
                }

                list.Add(ing);
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft ingredients: " + ex.Message);
        }

        return list;
    }

    internal static int Need(Recipe.Ingredient ing)
    {
        try
        {
            return Math.Max(1, ing.req);
        }
        catch
        {
            return 1;
        }
    }

    internal static string IngredientName(Recipe.Ingredient ing)
    {
        try
        {
            string n = ing.GetName();
            return string.IsNullOrEmpty(n) ? (ing.id ?? "?") : n;
        }
        catch
        {
            try
            {
                return ing.id ?? "?";
            }
            catch
            {
                return "?";
            }
        }
    }

    /// <summary>
    /// Does this item fill this ingredient slot?
    ///
    /// Deliberately not <c>Ingredient.CanSetThing</c>: that one compares the raw slot token
    /// against the item id, so a category slot such as <c>#meat</c> would only ever accept an
    /// item literally called "meat" — the colony would look permanently short of meat, and
    /// every species' meat is the same thing to the recipe. The rules here are the ones the
    /// craft panel itself uses (<c>Props.ListThingStack</c>): a <c>#cat</c> slot matches by
    /// category descent plus the "|" alternatives, and a plain slot matches the item id or the
    /// card it was reflavoured from.
    ///
    /// Quantities are not checked here — the caller sums matches across the pantry, so a slot
    /// needing 3 can be filled by three separate stacks.
    /// </summary>
    internal static bool Matches(Recipe.Ingredient ing, Thing t)
    {
        if (ing == null || t == null || t.isDestroyed)
        {
            return false;
        }

        try
        {
            // The same two guards the craft panel applies before offering an item.
            if (t.isEquipped || t.IsExcludeFromCraft(ing))
            {
                return false;
            }

            if (ing.ingType == Recipe.IngType.CreativeFood)
            {
                if (t.HasElement(10)
                    && !IsInCategory(t, "seasoning")
                    && !IsInCategory(t, "meal")
                    && !IsInCategory(t, "drink")
                    && !(t.trait is TraitFoodFishSlice))
                {
                    return true;
                }
            }

            if (ing.useCat)
            {
                // Vanilla skips the material-tag test on a category slot (Props.FindCat),
                // so a "#meat@raw" slot must not be narrowed to tagged meat only.
                if (IsInCategory(t, ing.id))
                {
                    return true;
                }

                return IsInAnyCategory(t, ing.idOther);
            }

            if (!string.IsNullOrEmpty(ing.tag) && !HasMaterialTag(t, ing.tag))
            {
                return false;
            }

            if (MatchesId(t, ing.id))
            {
                return true;
            }

            return MatchesAnyId(t, ing.idOther);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Item id, or the id of the card it was reflavoured from (vanilla's isOrigin branch).</summary>
    static bool MatchesId(Thing t, string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        if (string.Equals(t.id, id, StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            return string.Equals(t.source?._origin, id, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    static bool MatchesAnyId(Thing t, List<string>? ids)
    {
        if (ids == null)
        {
            return false;
        }

        // Vanilla's own alternative loop is the correct one here: the copy inside
        // Ingredient.IsValidIngredient re-tests the primary id and so misses the rest.
        for (int i = 0; i < ids.Count; i++)
        {
            if (MatchesId(t, ids[i]))
            {
                return true;
            }
        }

        return false;
    }

    static bool IsInCategory(Thing t, string? cat)
    {
        if (string.IsNullOrEmpty(cat))
        {
            return false;
        }

        try
        {
            return t.category != null && t.category.IsChildOf(cat);
        }
        catch
        {
            return false;
        }
    }

    static bool IsInAnyCategory(Thing t, List<string>? cats)
    {
        if (cats == null)
        {
            return false;
        }

        for (int i = 0; i < cats.Count; i++)
        {
            if (IsInCategory(t, cats[i]))
            {
                return true;
            }
        }

        return false;
    }

    static bool HasMaterialTag(Thing t, string tag)
    {
        try
        {
            return t.material != null && t.material.tag != null && t.material.tag.Contains(tag);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>How many of a slot's items sit across the given containers.</summary>
    internal static int CountIn(IList<Thing> containers, Recipe.Ingredient ing)
    {
        int have = 0;

        for (int i = 0; i < containers.Count; i++)
        {
            List<Thing> items = Contents(containers[i]);
            for (int k = 0; k < items.Count; k++)
            {
                if (Matches(ing, items[k]))
                {
                    have += Math.Max(1, items[k].Num);
                }
            }
        }

        return have;
    }

    /// <summary>
    /// Whether every required slot can be filled from these containers. <paramref name="shortOf"/>
    /// carries the first missing slot so the panel can say what the crew is waiting for.
    /// </summary>
    internal static bool CanSupply(IList<Thing> containers, string recipeId, out string shortOf)
    {
        shortOf = "";

        List<Recipe.Ingredient> ings = Ingredients(recipeId);
        if (ings.Count == 0)
        {
            // Recipes that take nothing are fine: a resident can work on them forever.
            return true;
        }

        for (int i = 0; i < ings.Count; i++)
        {
            Recipe.Ingredient ing = ings[i];
            if (CountIn(containers, ing) >= Need(ing))
            {
                continue;
            }

            shortOf = IngredientName(ing);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Pull one recipe's worth of ingredients out of the containers. All or nothing: if a
    /// slot runs dry halfway the pieces already taken are handed back, so a failed craft
    /// never quietly eats the colony's food.
    /// </summary>
    internal static bool TakeMaterials(IList<Thing> containers, string recipeId, List<Thing> taken)
    {
        List<Recipe.Ingredient> ings = Ingredients(recipeId);
        if (ings.Count == 0)
        {
            return true;
        }

        if (!CanSupply(containers, recipeId, out _))
        {
            return false;
        }

        for (int i = 0; i < ings.Count; i++)
        {
            Recipe.Ingredient ing = ings[i];
            int need = Need(ing);

            for (int c = 0; c < containers.Count && need > 0; c++)
            {
                Thing? container = containers[c];
                if (container == null)
                {
                    continue;
                }

                List<Thing> items = Contents(container);
                for (int k = items.Count - 1; k >= 0 && need > 0; k--)
                {
                    Thing t = items[k];
                    if (!Matches(ing, t))
                    {
                        continue;
                    }

                    int take = Math.Min(need, Math.Max(1, t.Num));
                    Thing? piece = SafeSplit(t, take);
                    if (piece == null)
                    {
                        continue;
                    }

                    taken.Add(piece);
                    need -= Math.Max(1, piece.Num);
                }
            }

            if (need > 0)
            {
                // Should not happen after CanSupply, but hand everything back rather
                // than craft from half a recipe.
                GiveBack(containers, taken);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Put partially taken materials back where they came from — the first container is
    /// good enough, and re-adding something that never left is harmless because the card
    /// is detached from its parent first.
    /// </summary>
    static void GiveBack(IList<Thing> containers, List<Thing> taken)
    {
        Thing? home = null;
        for (int i = 0; i < containers.Count; i++)
        {
            if (containers[i] != null)
            {
                home = containers[i];
                break;
            }
        }

        for (int i = 0; i < taken.Count; i++)
        {
            Thing? t = taken[i];
            if (t == null)
            {
                continue;
            }

            try
            {
                if (home != null)
                {
                    t.parent?.RemoveCard(t);
                    home.AddThing(t);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("craft give back: " + ex.Message);
            }
        }

        taken.Clear();
    }

    /// <summary>Splitting gives the taken piece; the caller decides its fate.</summary>
    static Thing? SafeSplit(Thing t, int num)
    {
        try
        {
            if (num >= t.Num)
            {
                return t;
            }

            return t.Split(num);
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft split: " + ex.Message);
            return null;
        }
    }

    internal static List<Thing> Contents(Thing container)
    {
        var list = new List<Thing>();

        try
        {
            if (container == null || container.things == null)
            {
                return list;
            }

            int n = container.things.Count;
            for (int i = 0; i < n; i++)
            {
                Thing? t = container.things[i];
                if (t != null && !t.isDestroyed)
                {
                    list.Add(t);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft contents: " + ex.Message);
        }

        return list;
    }

    // ────────────────────────────────────────────────────────────── producing

    /// <summary>
    /// Quality bonus, shaped exactly like vanilla's <c>Recipe.GetQualityBonus</c> but read
    /// off the resident doing the work instead of the player. Being under-levelled hurts a
    /// lot (a recipe five levels out of reach already costs -10), being over-levelled pays
    /// a little, and the recipe's own level adds a square-root term.
    /// </summary>
    internal static int QualityBonus(string recipeId, Chara maker)
    {
        try
        {
            Recipe? r = RecipeOf(recipeId);
            if (r == null || r.IsStaticLV())
            {
                return 0;
            }

            Element req = r.source.GetReqSkill();
            int skill = maker?.Evalue(req.id) ?? 0;
            int diff = req.Value - skill;

            if (diff > 0)
            {
                return diff < 5 ? 0 : -(diff - 4) * 10;
            }

            int bonus = EClass.curve(-diff, 10, 20, 80) / 10 * 10 + 10;
            if (r.RecipeLv > 0)
            {
                bonus += (int)Math.Sqrt(r.RecipeLv - 1) * 10;
            }

            return bonus;
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft quality: " + ex.Message);
            return 0;
        }
    }

    /// <summary>Skill the recipe asks for, so a panel can warn about a mismatch.</summary>
    internal static int RequiredSkillValue(string recipeId)
    {
        try
        {
            return RecipeOf(recipeId)?.source.GetReqSkill().Value ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    internal static int MakerSkill(string recipeId, Chara maker)
    {
        try
        {
            Recipe? r = RecipeOf(recipeId);
            if (r == null)
            {
                return 0;
            }

            Element req = r.source.GetReqSkill();
            return maker?.Evalue(req.id) ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Element id a recipe trains, for the "does this NPC have the skill" check.</summary>
    internal static int RequiredSkillId(string recipeId)
    {
        try
        {
            return RecipeOf(recipeId)?.source.GetReqSkill().id ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Turn one recipe's worth of ingredients into the finished item. Materials must
    /// already have been taken by <see cref="TakeMaterials"/>; they are consumed here by
    /// being folded into the product (vanilla's dish path reads their quality and
    /// elements) or simply dropped.
    /// </summary>
    internal static Thing? Produce(string recipeId, Chara maker, List<Thing> ings)
    {
        try
        {
            if (RecipeOf(recipeId) is not RecipeCard rc)
            {
                return null;
            }

            string productId = rc.idCard;
            if (string.IsNullOrEmpty(productId))
            {
                return null;
            }

            int quality = QualityBonus(recipeId, maker);
            int mat = -1;
            try
            {
                mat = rc.GetMainMaterial()?.id ?? -1;
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("craft material: " + ex.Message);
                mat = -1;
            }

            int lv = Math.Max(1, (rc.renderRow?.LV ?? 1) + quality);

            Thing? t = ThingGen.Create(productId, mat, lv);
            if (t == null)
            {
                return null;
            }

            try
            {
                t.isCrafted = true;
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("craft isCrafted: " + ex.Message);
            }

            try
            {
                if (t.category == null || t.category.ignoreBless == 0)
                {
                    t.SetBlessedState(BlessedState.Normal);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("craft bless: " + ex.Message);
            }

            if (rc.isDish && ings != null && ings.Count > 0)
            {
                // The vanilla cooking entry point, but with the resident credited for
                // the work: ingredients are folded in, quality and cooking data applied.
                try
                {
                    CraftUtil.MakeDish(t, ings, quality, maker);
                }
                catch (Exception ex)
                {
                    Plugin.LogDebug("craft dish: " + ex.Message);
                }
            }
            else
            {
                try
                {
                    rc.MixIngredients(t);
                }
                catch (Exception ex)
                {
                    Plugin.LogDebug("craft mix: " + ex.Message);
                }
            }

            return t;
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("craft produce: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Minutes of work one item costs. The recipe's own level scales it, so a plain dish
    /// is quick work and a masterwork is an afternoon; the floor keeps anything from
    /// finishing instantly.
    /// </summary>
    internal static int MinutesPerItem(string recipeId)
    {
        int lv = RecipeLv(recipeId);
        int minutes = 20 + lv * 4;
        return Math.Max(10, Math.Min(240, minutes));
    }

    /// <summary>
    /// How many minutes of work a resident gets done in an hour. Above the recipe's skill
    /// requirement the work speeds up, below it slows down, clamped so a mismatch is never
    /// a hard stop.
    /// </summary>
    internal static double WorkRate(string recipeId, Chara maker)
    {
        try
        {
            Recipe? r = RecipeOf(recipeId);
            if (r == null)
            {
                return 1.0;
            }

            Element req = r.source.GetReqSkill();
            int skill = maker?.Evalue(req.id) ?? 0;
            int diff = skill - req.Value;

            double rate = 1.0 + diff * 0.05;
            if (rate < 0.4)
            {
                return 0.4;
            }

            return rate > 4.0 ? 4.0 : rate;
        }
        catch
        {
            return 1.0;
        }
    }
}
