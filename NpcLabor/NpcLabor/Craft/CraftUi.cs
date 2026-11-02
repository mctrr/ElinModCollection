using System;
using System.Collections.Generic;
using NpcLabor.CoCraft;
using NpcLabor.Dispatch;

namespace NpcLabor.Craft;

/// <summary>
/// Base production wizard (slice G). It is opened from the workbench itself — the 协助 chip
/// on <c>LayerCraft</c> — so the station is already known (the panel's own factory) and the
/// recipe the player had selected there becomes the default choice, instead of the player
/// having to find the same bench again in a list.
///
/// Pages: who makes it, which of this workbench's recipes, how far to take it, how many.
/// The assignment is still a plain <c>recipeId</c> in <see cref="CraftJob"/>, so the tick,
/// the act and the save file are unaffected by where the wizard was opened from.
/// </summary>
internal static class CraftUi
{
    enum RowKind
    {
        Header,
        Info,
        Worker,
        Recipe,
        Page,
        Mode,
        Target,
    }

    sealed class Row
    {
        public RowKind Kind;
        public string Label = "";
        public string Sub = "";
        public Chara? Worker;
        public string RecipeId = "";
        public CraftMode Mode;
        public int Target;

        /// <summary>Page step for the recipe picker's prev/next rows.</summary>
        public int Page;

        public static Row Header(string text) => new Row { Kind = RowKind.Header, Label = text };

        public static Row Info(string text) => new Row { Kind = RowKind.Info, Label = text };
    }

    /// <summary>Half-finished assignment; lives only while the wizard is open.</summary>
    sealed class Draft
    {
        public int Uid;

        /// <summary>Recipes this workbench can make, the preselected one moved to the front.</summary>
        public List<string> Recipes = new List<string>();

        public string Recipe = "";
        public CraftMode Mode = CraftMode.Endless;
        public int Target = 10;
        public int Page;
    }

    static Draft? _draft;

    /// <summary>Recipes offered per page of the picker.</summary>
    const int RecipesPerPage = 12;

    // ────────────────────────────────────────────────────────────── entry

    /// <summary>
    /// Open the wizard for the workbench the player is standing at. <paramref name="bench"/>
    /// is that bench object, so the recipe list is exactly what the craft panel itself offers
    /// (the bench trait decides — see <see cref="CraftEngine.BenchMakes"/>), and
    /// <paramref name="currentRecipeId"/> is the recipe selected in the panel, put first and
    /// preselected so "make this one, keep it stocked" stays a short path.
    /// </summary>
    internal static void OpenForWorkbench(Thing? bench, string? currentRecipeId)
    {
        try
        {
            List<string> recipes = CraftManager.RecipesForBench(bench);
            if (recipes.Count == 0)
            {
                Msg.Say(LaborText.T("craft.ui.noStationAvailable"));
                SE.Beep();
                return;
            }

            if (!string.IsNullOrEmpty(currentRecipeId))
            {
                for (int i = recipes.Count - 1; i > 0; i--)
                {
                    if (string.Equals(recipes[i], currentRecipeId, StringComparison.OrdinalIgnoreCase))
                    {
                        string pick = recipes[i];
                        recipes.RemoveAt(i);
                        recipes.Insert(0, pick);
                        break;
                    }
                }
            }

            _draft = new Draft
            {
                Recipes = recipes,
                Recipe = currentRecipeId ?? "",
            };

            ShowWorker();
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("craft UI open failed: " + ex);
        }
    }

    /// <summary>
    /// Step 1: who does it. The workbench decides the skill, so the list leads with
    /// residents who actually have it; when nobody does, the whole pool is still offered
    /// rather than leaving the step with nothing to click.
    /// </summary>
    static void ShowWorker()
    {
        _draft ??= new Draft();

        var rows = new List<Row>();
        rows.Add(Row.Header(LaborText.T("craft.ui.pickWorker")));

        List<Chara> pool = CraftManager.Candidates();
        var scored = new List<(Chara C, int Have, int Need)>();

        for (int i = 0; i < pool.Count; i++)
        {
            Chara c = pool[i];
            int have = StationSkill(_draft.Recipes, c, out int need);
            scored.Add((c, have, need));
        }

        bool anySkilled = false;
        for (int i = 0; i < scored.Count; i++)
        {
            if (scored[i].Have > 0)
            {
                anySkilled = true;
                break;
            }
        }

        scored.Sort((a, b) =>
        {
            int cmp = b.Have.CompareTo(a.Have);
            return cmp != 0 ? cmp : a.C.uid.CompareTo(b.C.uid);
        });

        for (int i = 0; i < scored.Count; i++)
        {
            (Chara c, int have, int need) = scored[i];
            if (anySkilled && have <= 0)
            {
                continue;
            }

            bool busy = CraftManager.Has(c.uid);
            rows.Add(new Row
            {
                Kind = RowKind.Worker,
                Worker = c,
                Label = (busy ? "[·] " : "[  ] ") + SafeName(c),
                Sub = LaborText.T("craft.ui.skill", have.ToString(), need.ToString()),
            });
        }

        if (rows.Count <= 1)
        {
            rows.Add(Row.Info(LaborText.T("craft.ui.noWorker")));
        }

        Show(LaborText.T("craft.ui.pickWorker"), rows);
    }

    /// <summary>Step 2: one of the recipes this workbench can make.</summary>
    static void ShowRecipe()
    {
        _draft ??= new Draft();

        List<string> ids = _draft.Recipes;
        int pages = Math.Max(1, (ids.Count + RecipesPerPage - 1) / RecipesPerPage);
        _draft.Page = Math.Max(0, Math.Min(_draft.Page, pages - 1));

        var rows = new List<Row>();
        rows.Add(Row.Info(LaborText.T("craft.ui.page",
            (_draft.Page + 1).ToString(), pages.ToString())));

        int start = _draft.Page * RecipesPerPage;
        int end = Math.Min(ids.Count, start + RecipesPerPage);

        for (int i = start; i < end; i++)
        {
            string id = ids[i];

            // The recipe the craft panel already had selected is marked and sorted first,
            // so confirming "this one" is the top row.
            bool picked = !string.IsNullOrEmpty(_draft.Recipe)
                && string.Equals(_draft.Recipe, id, StringComparison.OrdinalIgnoreCase);

            rows.Add(new Row
            {
                Kind = RowKind.Recipe,
                RecipeId = id,
                Label = (picked ? "[·] " : "") + CraftEngine.Name(id),
                Sub = Materials(id),
            });
        }

        if (ids.Count == 0)
        {
            rows.Add(Row.Info("-"));
        }

        if (_draft.Page > 0)
        {
            rows.Add(new Row { Kind = RowKind.Page, Page = -1, Label = LaborText.T("craft.ui.prev") });
        }

        if (_draft.Page < pages - 1)
        {
            rows.Add(new Row { Kind = RowKind.Page, Page = 1, Label = LaborText.T("craft.ui.next") });
        }

        Show(LaborText.T("craft.ui.pickRecipe"), rows);
    }

    /// <summary>
    /// The best skill a candidate brings to this workbench, plus the requirement of the
    /// recipe that earned it, so a row can read "skill 12 (wants 10)" rather than a bare
    /// number with no reference point.
    /// </summary>
    static int StationSkill(List<string> recipes, Chara c, out int need)
    {
        need = 0;
        int best = 0;

        for (int i = 0; i < recipes.Count; i++)
        {
            string id = recipes[i];
            try
            {
                int have = CraftEngine.MakerSkill(id, c);
                if (have > best)
                {
                    best = have;
                    need = CraftEngine.RequiredSkillValue(id);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogDebug("craft UI station skill: " + ex.Message);
            }
        }

        return best;
    }

    static void ShowMode()
    {
        _draft ??= new Draft();

        var rows = new List<Row>
        {
            new Row { Kind = RowKind.Mode, Mode = CraftMode.Count, Label = LaborText.T("craft.ui.mode.count") },
            new Row { Kind = RowKind.Mode, Mode = CraftMode.Keep, Label = LaborText.T("craft.ui.mode.keep") },
            new Row { Kind = RowKind.Mode, Mode = CraftMode.Endless, Label = LaborText.T("craft.ui.mode.endless") },
        };

        Show(LaborText.T("craft.ui.pickMode"), rows);
    }

    static void ShowTarget()
    {
        _draft ??= new Draft();

        int[] choices = { 1, 5, 10, 20, 50, 100, 200 };
        var rows = new List<Row>();

        for (int i = 0; i < choices.Length; i++)
        {
            rows.Add(new Row
            {
                Kind = RowKind.Target,
                Target = choices[i],
                Label = choices[i].ToString(),
            });
        }

        Show(LaborText.T("craft.ui.pickTarget"), rows);
    }

    // ────────────────────────────────────────────────────────────── clicks

    static void OnClick(Row r)
    {
        try
        {
            switch (r.Kind)
            {
                case RowKind.Header:
                case RowKind.Info:
                    return;

                case RowKind.Worker:
                    if (r.Worker != null)
                    {
                        _draft ??= new Draft();
                        _draft.Uid = r.Worker.uid;
                        _draft.Page = 0;
                        ShowRecipe();
                    }

                    return;

                case RowKind.Recipe:
                    OnRecipeClick(r);
                    return;

                case RowKind.Page:
                    if (_draft != null)
                    {
                        _draft.Page = Math.Max(0, _draft.Page + r.Page);
                        ShowRecipe();
                    }

                    return;

                case RowKind.Mode:
                    if (_draft != null)
                    {
                        _draft.Mode = r.Mode;
                        if (r.Mode == CraftMode.Endless)
                        {
                            Finish();
                        }
                        else
                        {
                            ShowTarget();
                        }
                    }

                    return;

                case RowKind.Target:
                    if (_draft != null)
                    {
                        _draft.Target = r.Target;
                        Finish();
                    }

                    return;
            }
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("craft UI click: " + ex.Message);
        }
    }

    static void OnRecipeClick(Row r)
    {
        if (_draft == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(r.RecipeId))
        {
            return;
        }

        _draft.Recipe = r.RecipeId;
        ShowMode();
    }

    static void Finish()
    {
        if (_draft == null)
        {
            CloseWizard();
            return;
        }

        Chara? who = null;
        try
        {
            who = RefChara.Get(_draft.Uid);
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft finish: " + ex.Message);
        }

        if (who == null)
        {
            Msg.Say(LaborText.T("craft.ui.noWorker"));
            CloseWizard();
            return;
        }

        CraftJob? job = CraftManager.Assign(who, _draft.Recipe, _draft.Mode, _draft.Target);
        if (job == null)
        {
            SE.Beep();
            CloseWizard();
            return;
        }

        Msg.Say(LaborText.T("craft.ui.assign") + "：" + job.Describe());
        CloseWizard();
    }

    /// <summary>Dismiss the wizard and drop back to the craft panel it was opened from.</summary>
    static void CloseWizard()
    {
        _draft = null;
        PersonPickerUi.ClosePicker();
    }

    // ────────────────────────────────────────────────────────────── list plumbing

    static void Show(string caption, List<Row> rows)
    {
        LayerList menu = EClass.ui.AddLayer<LayerList>();
        PersonPickerUi.SetOpenPicker(menu);
        menu.SetList2(
                rows,
                r => r.Label,
                (r, item) => OnClick(r),
                (r, item) => StyleRow(item, r))
            .SetSize(620f);

        SetHeader(menu, caption);
    }

    static void StyleRow(ItemGeneral? item, Row r)
    {
        if (item == null)
        {
            return;
        }

        try
        {
            item.DisableIcon();
        }
        catch
        {
        }

        try
        {
            if (item.button1?.mainText != null)
            {
                FontColor color = r.Kind == RowKind.Header ? FontColor.Default : FontColor.ButtonGeneral;
                item.button1.mainText.SetText(r.Label ?? "", color);
            }
        }
        catch
        {
        }

        try
        {
            if (!string.IsNullOrEmpty(r.Sub) && item.button1?.subText != null)
            {
                item.button1.subText.SetText(r.Sub);
            }
        }
        catch
        {
        }

        try
        {
            item.Build();
        }
        catch
        {
        }
    }

    static void SetHeader(LayerList menu, string caption)
    {
        try
        {
            menu.SetHeader(caption);
            return;
        }
        catch
        {
        }

        try
        {
            if (menu.windows != null && menu.windows.Count > 0)
            {
                menu.windows[0].SetCaption(caption);
            }
        }
        catch
        {
        }
    }

    // ────────────────────────────────────────────────────────────── text helpers

    /// <summary>One line of ingredient slots, or a note that nothing is needed.</summary>
    static string Materials(string recipeId)
    {
        try
        {
            List<Recipe.Ingredient> ings = CraftEngine.Ingredients(recipeId);
            if (ings.Count == 0)
            {
                return LaborText.T("craft.ui.materialOk");
            }

            var parts = new List<string>();
            for (int i = 0; i < ings.Count; i++)
            {
                parts.Add(CraftEngine.IngredientName(ings[i]) + "×" + CraftEngine.Need(ings[i]).ToString());
            }

            return LaborText.T("craft.ui.materials", string.Join("  ", parts.ToArray()));
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("craft UI materials: " + ex.Message);
            return "";
        }
    }

    static string SafeName(Chara? c)
    {
        try
        {
            return c?.NameSimple ?? c?.Name ?? "?";
        }
        catch
        {
            return "?";
        }
    }
}
