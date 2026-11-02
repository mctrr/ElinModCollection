using System.Collections.Generic;
using HarmonyLib;
using NpcLabor.CoCraft;
using NpcLabor.Craft;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace NpcLabor.Patches;

/// <summary>
/// The float craft window starts a craft by *replay*, not by a button: while the left
/// button is held over the same recipe cell it remembers, <c>LayerCraftFloat.Update</c>
/// calls the list's onClick again. So the click that dismisses the assistant picker can
/// start a craft there even with every UIButton guard in place - the exact hole that made
/// the earlier button-level fixes useless. Same condition, same meaning: not while the
/// picker is up and not from the click that closed it.
/// </summary>
[HarmonyPatch(typeof(LayerCraftFloat), nameof(LayerCraftFloat.Update))]
internal static class LayerCraftFloatReplayGuard
{
    static bool _notified;

    [HarmonyPrefix]
    static void Prefix(LayerCraftFloat __instance)
    {
        try
        {
            if (!PersonPickerUi.CraftPressIsStray)
            {
                _notified = false;
                return;
            }

            // Only the replay branch is a problem, and it only runs while the left button
            // is held. Forget which recipe was last clicked instead of skipping Update, so
            // the middle-click menu and the field's own reset keep working.
            if (!Input.GetMouseButton(0))
            {
                return;
            }

            __instance.idLastRecipe = null;
            if (!_notified)
            {
                _notified = true;
                Plugin.LogInfo("[cocraft] float craft replay refused: the assistant picker owns this click");
            }
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("float craft replay guard: " + ex.Message);
        }
    }
}

/// <summary>
/// Co-craft assistant picker on LayerCraft.
/// Craft action button lives under RecipeInfo1/Options (prefab-wired OnClickCraft).
/// Keep the craft button in its original stretch slot (label stays centered). Overlay a
/// compact fixed-width assist chip on its left edge instead of reparenting into a row.
/// </summary>
[HarmonyPatch(typeof(LayerCraft))]
internal static class LayerCraftPatches
{
    const string ButtonName = "NpcLabor_CoCraftBtn";
    const float ChipWidth = 64f;
    const float ChipMinHeight = 28f;
    const float ChipGap = 6f;

    const int MenuAuto = -100;
    const int MenuOff = -101;

    /// <summary>Base-production row: set up a standing order for this very workbench.</summary>
    const int MenuProduce = -102;

    /// <summary>Cancel rows encode <c>MenuCancelBase - index</c> into <see cref="_menuOrders"/>.</summary>
    const int MenuCancelBase = -300;

    /// <summary>Standing orders shown as cancel rows in the assist menu being built.</summary>
    static List<CraftJob> _menuOrders = new List<CraftJob>();

    [HarmonyPostfix]
    [HarmonyPatch(nameof(LayerCraft.OnAfterInit))]
    static void OnAfterInitPostfix(LayerCraft __instance)
    {
        try
        {
            EnsureButton(__instance);
            RefreshButtonLabel(__instance);
            // Restore the per-workbench pin even when SetFactory was not hit.
            try { AssistantResolver.PinnedUid = WorkbenchMemory.ReadCoCraft(__instance.factory); } catch { }
        }
        catch (System.Exception ex)
        {
            Plugin.LogWarn("co-craft button init failed: " + ex);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(LayerCraft.SetFactory))]
    static void SetFactoryPostfix(LayerCraft __instance)
    {
        try
        {
            EnsureButton(__instance);
            RefreshButtonLabel(__instance);
            try { AssistantResolver.PinnedUid = WorkbenchMemory.ReadCoCraft(__instance.factory); } catch { }
        }
        catch (System.Exception __e) { Plugin.LogDebug("LayerCraftPatches.cs silent catch: " + __e.Message); }
}

    [HarmonyPostfix]
    [HarmonyPatch(nameof(LayerCraft.RefreshRecipe))]
    static void RefreshRecipePostfix(LayerCraft __instance)
    {
        try
        {
            EnsureButton(__instance);
            RefreshButtonLabel(__instance);
        }
        catch (System.Exception __e) { Plugin.LogDebug("LayerCraftPatches.cs silent catch: " + __e.Message); }
}

    [HarmonyPostfix]
    [HarmonyPatch(nameof(LayerCraft.OnKill))]
    static void OnKillPostfix()
    {
        if (CoCraftSession.Active && (EClass.pc == null || EClass.pc.ai is not AI_UseCrafter))
        {
            CoCraftSession.Clear("layer-kill");
        }
    }

    /// <summary>
    /// The one place that actually starts a workbench craft, so the one place that can
    /// refuse one. `OnClickCraft` has no in-game caller at all: it is invoked by the
    /// prefab-wired UnityEvent on the craft button, which is why every attempt to guard
    /// the input route (which button, which mouse button, how many frames) missed. A
    /// press that arrives here while the assistant picker is up, or from the click that
    /// just dismissed it, was not aimed at the craft button - it is the dismissal click
    /// falling through now that the picker is gone. Refuse it and let the player click
    /// craft for real (<see cref="PersonPickerUi.CraftPressIsStray"/>).
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(nameof(LayerCraft.OnClickCraft))]
    static bool OnClickCraftPrefix()
    {
        try
        {
            if (PersonPickerUi.CraftPressIsStray)
            {
                Plugin.LogInfo("[cocraft] craft press refused: it is the click that dismissed the assistant picker");
                return false;
            }
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("co-craft craft guard: " + ex.Message);
        }

        return true;
    }

    static void EnsureButton(LayerCraft layer)
    {
        if (layer == null)
        {
            return;
        }

        UIButton? existing = FindOurButton(layer);
        if (existing != null)
        {
            WireClick(existing, layer);
            UIButton? craft = FindCraftActionButton(layer);
            if (craft != null)
            {
                PlaceAssistChip(existing, craft);
            }

            return;
        }

        UIButton? created =
            TryCreateBesideCraftButton(layer)
            ?? TryCreateBesideKnownControls(layer)
            ?? TryCreateViaWindowMenu(layer)
            ?? TryCreateViaBottomButton(layer);

        if (created == null)
        {
            Plugin.LogWarn("co-craft button: no vanilla host");
            DumpLayout(layer);
            return;
        }

        created.gameObject.name = ButtonName;
        created.gameObject.SetActive(true);
        StripIconChrome(created);
        WireClick(created, layer);
        RefreshButtonLabel(layer);
        Plugin.LogInfo("co-craft button ready under " + GetPath(created.transform));
    }

    static UIButton? TryCreateBesideCraftButton(LayerCraft layer)
    {
        UIButton? craftBtn = FindCraftActionButton(layer);
        if (craftBtn == null)
        {
            Plugin.LogDebug("co-craft: OnClickCraft UIButton not found");
            return null;
        }

        return CloneAssistChip(craftBtn);
    }

    static UIButton? TryCreateBesideKnownControls(LayerCraft layer)
    {
        UIButton? mold =
            layer.toggleCraftTo
            ?? layer.toggleRepeat
            ?? layer.buttonTrack
            ?? layer.buttonAutoRefuel
            ?? layer.buttonRefuel;

        if (mold == null)
        {
            return null;
        }

        return CloneCompactSibling(mold);
    }

    static UIButton? TryCreateViaWindowMenu(LayerCraft layer)
    {
        Window? host = ResolveHostWindow(layer);
        if (host == null)
        {
            return null;
        }

        WindowMenu? menu = host.menuBottom ?? host.menuLeftBottom ?? host.menuRight ?? host.menuLeft;
        if (menu == null)
        {
            return null;
        }

        try
        {
            UIButton btn = menu.AddButton(
                "continue",
                _ => OpenAssistantMenu(layer),
                sprite: null,
                idButton: "Default");
            if (btn == null)
            {
                return null;
            }

            SetButtonText(btn, NpcLabor.LaborTerms.Assist);
            StripIconChrome(btn);
            return btn;
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("WindowMenu.AddButton failed: " + ex.Message);
            return null;
        }
    }

    static UIButton? TryCreateViaBottomButton(LayerCraft layer)
    {
        Window? host = ResolveHostWindow(layer);
        if (host?.rectBottom == null)
        {
            return null;
        }

        try
        {
            UIButton btn = host.AddBottomButton("continue", () => OpenAssistantMenu(layer));
            if (btn != null)
            {
                SetButtonText(btn, NpcLabor.LaborTerms.Assist);
                StripIconChrome(btn);
                btn.transform.SetAsLastSibling();
                host.rectBottom.RebuildLayout(recursive: true);
            }

            return btn;
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("AddBottomButton failed: " + ex.Message);
            return null;
        }
    }

    static UIButton? CloneAssistChip(UIButton craft)
    {
        if (craft == null || craft.transform.parent == null)
        {
            return null;
        }

        // Keep craft button untouched in its original stretch slot so its label stays centered.
        GameObject go = Object.Instantiate(craft.gameObject, craft.transform.parent);
        go.name = ButtonName;
        go.SetActive(true);

        UIButton? btn = go.GetComponent<UIButton>();
        if (btn == null)
        {
            Object.Destroy(go);
            return null;
        }

        try
        {
            btn.onClick.RemoveAllListeners();
        }
        catch (System.Exception __e) { Plugin.LogDebug("LayerCraftPatches.cs silent catch: " + __e.Message); }
        try
        {
            btn.isChecked = false;
            if (btn.imageCheck != null)
            {
                btn.imageCheck.gameObject.SetActive(false);
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("LayerCraftPatches.cs silent catch: " + __e.Message); }
StripIconChrome(btn);
        PlaceAssistChip(btn, craft);
        return btn;
    }

    static UIButton? CloneCompactSibling(UIButton mold)
    {
        if (mold == null || mold.transform.parent == null)
        {
            return null;
        }

        Transform parent = mold.transform.parent;
        GameObject go = Object.Instantiate(mold.gameObject, parent);
        go.name = ButtonName;
        go.SetActive(true);

        UIButton? btn = go.GetComponent<UIButton>();
        if (btn == null)
        {
            Object.Destroy(go);
            return null;
        }

        try
        {
            btn.onClick.RemoveAllListeners();
        }
        catch (System.Exception __e) { Plugin.LogDebug("LayerCraftPatches.cs silent catch: " + __e.Message); }
StripIconChrome(btn);
        PlaceAssistChip(btn, mold);
        return btn;
    }

    /// <summary>
    /// Do NOT reparent the craft button. Overlay a compact chip on its left edge so
    /// the vanilla craft label remains centered in the full bar.
    /// </summary>
    static void PlaceAssistChip(UIButton assist, UIButton craft)
    {
        if (assist == null || craft == null)
        {
            return;
        }

        Transform? parent = craft.transform.parent;
        if (parent == null)
        {
            return;
        }

        if (assist.transform.parent != parent)
        {
            assist.transform.SetParent(parent, worldPositionStays: false);
        }

        // Sit just above craft in draw order without stealing its layout slot.
        assist.transform.SetSiblingIndex(craft.transform.GetSiblingIndex() + 1);

        RectTransform? craftRt = craft.GetComponent<RectTransform>();
        RectTransform? rt = assist.GetComponent<RectTransform>();
        if (craftRt == null || rt == null)
        {
            return;
        }

        float h = ApproximateVisualHeight(craftRt);
        float w = ChipWidth;

        rt.localScale = Vector3.one;
        rt.localRotation = craftRt.localRotation;

        Vector2 amin = craftRt.anchorMin;
        Vector2 amax = craftRt.anchorMax;
        bool stretchX = Mathf.Abs(amax.x - amin.x) > 0.001f;
        bool stretchY = Mathf.Abs(amax.y - amin.y) > 0.001f;

        if (stretchX || stretchY)
        {
            // Sit just outside the craft bar's left edge so craft text stays fully centered.
            rt.anchorMin = new Vector2(amin.x, amin.y);
            rt.anchorMax = new Vector2(amin.x, amax.y);
            rt.pivot = new Vector2(1f, 0.5f);
            float left = craftRt.offsetMin.x;
            float bottom = craftRt.offsetMin.y;
            float top = craftRt.offsetMax.y;
            float rightEdge = left - ChipGap;
            rt.offsetMin = new Vector2(rightEdge - w, bottom);
            rt.offsetMax = new Vector2(rightEdge, top);
            if (!stretchY)
            {
                rt.sizeDelta = new Vector2(w, h);
                rt.anchoredPosition = new Vector2(rightEdge - w * 0.5f, craftRt.anchoredPosition.y);
            }
        }
        else
        {
            rt.anchorMin = craftRt.anchorMin;
            rt.anchorMax = craftRt.anchorMax;
            rt.pivot = new Vector2(1f, craftRt.pivot.y);
            rt.sizeDelta = new Vector2(w, Mathf.Max(h, Mathf.Abs(craftRt.sizeDelta.y)));
            float craftW = craftRt.rect.width;
            if (craftW < 1f)
            {
                craftW = Mathf.Abs(craftRt.sizeDelta.x);
            }

            float leftLocal = craftRt.anchoredPosition.x - craftRt.pivot.x * craftW;
            rt.anchoredPosition = new Vector2(leftLocal - ChipGap, craftRt.anchoredPosition.y);
        }

        LayoutElement le = GetOrAddLayoutElement(assist.gameObject);
        le.ignoreLayout = true;
        le.minWidth = w;
        le.preferredWidth = w;
        le.flexibleWidth = 0f;
        le.minHeight = h;
        le.preferredHeight = h;
        le.flexibleHeight = 0f;

        DisableFitters(assist.gameObject);
        StripIconChrome(assist);

        try
        {
            if (assist.mainText != null)
            {
                RectTransform tr = assist.mainText.rectTransform;
                tr.anchorMin = new Vector2(0f, 0f);
                tr.anchorMax = new Vector2(1f, 1f);
                tr.offsetMin = new Vector2(2f, 0f);
                tr.offsetMax = new Vector2(-2f, 0f);
                assist.mainText.alignment = TextAnchor.MiddleCenter;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("LayerCraftPatches.cs silent catch: " + __e.Message); }
Plugin.LogDebug(
            "co-craft chip overlay craft=" + craft.name
            + " chipW=" + w
            + " h=" + h
            + " path=" + GetPath(assist.transform));
    }

    static LayoutElement GetOrAddLayoutElement(GameObject go)
    {
        LayoutElement? le = go.GetComponent<LayoutElement>();
        if (le == null)
        {
            le = go.AddComponent<LayoutElement>();
        }

        return le;
    }

    static void DisableFitters(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        ContentSizeFitter[] fitters = root.GetComponentsInChildren<ContentSizeFitter>(true);
        for (int i = 0; i < fitters.Length; i++)
        {
            if (fitters[i] != null)
            {
                fitters[i].enabled = false;
            }
        }
    }

    static float ApproximateVisualHeight(RectTransform rt)
    {
        if (rt == null)
        {
            return ChipMinHeight;
        }

        float h = rt.rect.height;
        if (h < 1f)
        {
            h = Mathf.Abs(rt.sizeDelta.y);
        }

        if (h > 64f)
        {
            h = ChipMinHeight;
        }

        if (h < 1f)
        {
            h = ChipMinHeight;
        }

        return h;
    }

    static UIButton? FindCraftActionButton(LayerCraft layer)
    {
        UIButton[] buttons = layer.GetComponentsInChildren<UIButton>(includeInactive: true);
        for (int i = 0; i < buttons.Length; i++)
        {
            UIButton b = buttons[i];
            if (b == null || b.gameObject.name == ButtonName)
            {
                continue;
            }

            if (ButtonInvokes(b, "OnClickCraft"))
            {
                return b;
            }
        }

        Button[] plain = layer.GetComponentsInChildren<Button>(includeInactive: true);
        for (int i = 0; i < plain.Length; i++)
        {
            Button b = plain[i];
            if (b == null || b.gameObject.name == ButtonName)
            {
                continue;
            }

            if (ButtonInvokes(b, "OnClickCraft"))
            {
                return b as UIButton ?? b.GetComponent<UIButton>();
            }
        }

        return null;
    }

    static bool ButtonInvokes(Button button, string methodName)
    {
        if (button == null || button.onClick == null)
        {
            return false;
        }

        int n = button.onClick.GetPersistentEventCount();
        for (int i = 0; i < n; i++)
        {
            string m = button.onClick.GetPersistentMethodName(i);
            if (m == methodName)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(m) && m.EndsWith(methodName))
            {
                Object target = button.onClick.GetPersistentTarget(i);
                if (target is LayerCraft || (target != null && target.GetType().Name == "LayerCraft"))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Unity's Instantiate clones a button's *persistent* (prefab-wired) onClick listeners
    /// together with the GameObject, and RemoveAllListeners only clears the runtime list:
    /// the persistent calls are rebuilt from the serialized group on every invoke. So a
    /// chip cloned from the vanilla craft button keeps calling LayerCraft.OnClickCraft no
    /// matter how often we clear it, and pressing the chip starts a craft - the whole
    /// reason the assistant picker's cancel/X looked like it crafted. Switch every
    /// inherited persistent listener off; the chip's own click is a runtime listener and
    /// is unaffected.
    /// </summary>
    static void StripInheritedOnClick(UIButton btn)
    {
        if (btn == null || btn.onClick == null)
        {
            return;
        }

        try
        {
            int count = btn.onClick.GetPersistentEventCount();
            string detail = "";
            int disabled = 0;
            for (int i = 0; i < count; i++)
            {
                if (btn.onClick.GetPersistentListenerState(i) == UnityEventCallState.Off)
                {
                    continue;
                }

                try
                {
                    Object target = btn.onClick.GetPersistentTarget(i);
                    detail += " [" + i + "] " + btn.onClick.GetPersistentMethodName(i)
                        + "@" + (target != null ? target.name : "null");
                }
                catch (System.Exception inner)
                {
                    Plugin.LogDebug("StripInheritedOnClick dump: " + inner.Message);
                }

                btn.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
                disabled++;
            }

            if (disabled > 0)
            {
                Plugin.LogInfo(
                    "[cocraft] turned off " + disabled + " inherited persistent onClick listener(s) on "
                    + btn.gameObject.name + ":" + detail);
            }
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("StripInheritedOnClick: " + ex.Message);
        }
    }

    static void StripIconChrome(UIButton btn)
    {
        if (btn == null)
        {
            return;
        }

        StripInheritedOnClick(btn);

        try
        {
            if (btn.icon != null)
            {
                if (btn.icon.transform.parent != null && btn.icon.transform.parent != btn.transform)
                {
                    btn.icon.transform.parent.SetActive(enable: false);
                }
                else
                {
                    btn.icon.SetActive(enable: false);
                }
            }
        }
        catch
        {
            try
            {
                if (btn.icon != null)
                {
                    btn.icon.enabled = false;
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("LayerCraftPatches.cs silent catch: " + __e.Message); }
}

        try
        {
            if (btn.imageCheck != null)
            {
                btn.imageCheck.gameObject.SetActive(false);
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("LayerCraftPatches.cs silent catch: " + __e.Message); }
}

    static Window? ResolveHostWindow(LayerCraft layer)
    {
        UIButton? probe =
            layer.toggleCraftTo
            ?? layer.toggleRepeat
            ?? layer.buttonAutoRefuel
            ?? layer.buttonTrack
            ?? layer.buttonRefuel;

        if (probe != null)
        {
            Window? fromProbe = probe.GetComponentInParent<Window>();
            if (fromProbe != null)
            {
                return fromProbe;
            }
        }

        if (layer.inputNum != null)
        {
            Window? fromInput = layer.inputNum.GetComponentInParent<Window>();
            if (fromInput != null)
            {
                return fromInput;
            }
        }

        if (layer.info1 != null)
        {
            Window? fromInfo = layer.info1.GetComponentInParent<Window>();
            if (fromInfo != null)
            {
                return fromInfo;
            }
        }

        UIButton? craft = FindCraftActionButton(layer);
        if (craft != null)
        {
            Window? fromCraft = craft.GetComponentInParent<Window>();
            if (fromCraft != null)
            {
                return fromCraft;
            }
        }

        if (layer.windows != null)
        {
            for (int i = 0; i < layer.windows.Count; i++)
            {
                Window w = layer.windows[i];
                if (w == null)
                {
                    continue;
                }

                if (layer.windowList != null && w == layer.windowList)
                {
                    continue;
                }

                return w;
            }

            if (layer.windows.Count > 0)
            {
                return layer.windows[0];
            }
        }

        return layer.windowList;
    }

    static UIButton? FindOurButton(LayerCraft layer)
    {
        Transform? t = FindChildRecursive(layer.transform, ButtonName);
        if (t == null && layer.windows != null)
        {
            for (int i = 0; i < layer.windows.Count; i++)
            {
                Window? w = layer.windows[i];
                if (w == null)
                {
                    continue;
                }

                t = FindChildRecursive(w.transform, ButtonName);
                if (t != null)
                {
                    break;
                }
            }
        }

        return t != null ? t.GetComponent<UIButton>() : null;
    }

    static void WireClick(UIButton? btn, LayerCraft layer)
    {
        if (btn == null)
        {
            return;
        }

        btn.onClick.RemoveAllListeners();
        btn.SetOnClick(() => OpenAssistantMenu(layer));
    }

    static void OpenAssistantMenu(LayerCraft layer)
    {
        try
        {
            // A press on the assist chip that is really the picker's own dismissal click
            // would reopen the list the player just closed.
            if (PersonPickerUi.CraftPressIsStray)
            {
                Plugin.LogInfo("[cocraft] assist press refused: it is the click that dismissed the assistant picker");
                return;
            }

            // Refresh the per-workbench pin so the picker reflects the last choice.
            try { AssistantResolver.PinnedUid = WorkbenchMemory.ReadCoCraft(layer?.factory); } catch { }

            int skillId = GetRecipeSkillId(layer);
            string skillName = GetSkillName(skillId);
            var options = new List<int> { MenuAuto };

            foreach (Chara c in AssistantResolver.ListEligible())
            {
                if (PersonPickerUi.IsPcLike(c))
                {
                    continue;
                }

                if (skillId > 0 && c.Evalue(skillId) <= 0)
                {
                    continue;
                }

                options.Add(c.uid);
            }

            options.Add(MenuOff);

            // Base production lives here, at the workbench itself, rather than on the quest
            // board: the player is already standing at the station and has already picked a
            // recipe in this panel, so neither has to be chosen again.
            _menuOrders = CraftManager.OrdersFor(layer?.factory);
            if (NpcLabor.LaborConfig.FeatureCraft)
            {
                options.Add(MenuProduce);
                for (int i = 0; i < _menuOrders.Count; i++)
                {
                    options.Add(MenuCancelBase - i);
                }
            }

            LayerList menu = EClass.ui.AddLayer<LayerList>();
            PersonPickerUi.SetOpenPicker(menu);
            menu.SetList2(
                    options,
                    id => MenuLabel(id, skillId),
                    (id, item) =>
                    {
                        if (id == MenuProduce)
                        {
                            // Close the assist menu first: the wizard opens its own list, and
                            // leaving this one registered underneath would strand it open.
                            PersonPickerUi.ClosePicker();
                            CraftUi.OpenForWorkbench(layer?.factory, layer?.recipe?.id);
                            return;
                        }

                        if (id <= MenuCancelBase)
                        {
                            int idx = MenuCancelBase - id;
                            if (idx >= 0 && idx < _menuOrders.Count && _menuOrders[idx] != null)
                            {
                                CraftJob job = _menuOrders[idx];
                                CraftManager.Cancel(job.uid);
                                Msg.SayRaw(NpcLabor.LaborText.T("craft.msg.cancelled", job.name));
                                RefreshButtonLabel(layer);
                            }

                            return;
                        }

                        ApplyMenuChoice(id, layer);
                        RefreshButtonLabel(layer);
                        try
                        {
                            layer.info1?.RefreshList();
                            layer.info1?.RefreshBalance();
                        }
                        catch (System.Exception __e) { Plugin.LogDebug("LayerCraftPatches.cs silent catch: " + __e.Message); }
SE.Click();
                    },
                    (id, item) =>
                    {
                        // Portraits like the resident board: ItemGeneral.SetChara.
                        PersonPickerUi.StyleRow(
                            item,
                            id,
                            MenuLabel(id, skillId),
                            MenuSkillSub(id, skillId, skillName),
                            menuAuto: MenuAuto,
                            menuOff: MenuOff);
                    })
                .SetSize(460f);

            SelectCurrentPin(menu, options);

            string title = skillName.IsEmpty() ? NpcLabor.LaborTerms.CoCraft : (NpcLabor.LaborTerms.CoCraft + " · " + skillName);
            try
            {
                menu.SetHeader(title);
            }
            catch
            {
                try
                {
                    if (menu.windows != null && menu.windows.Count > 0)
                    {
                        menu.windows[0].SetCaption(title);
                    }
                }
                catch (System.Exception __e) { Plugin.LogDebug("LayerCraftPatches.cs silent catch: " + __e.Message); }
}
        }
        catch (System.Exception ex)
        {
            Plugin.LogWarn("co-craft menu failed: " + ex);
        }
    }

    static string MenuLabel(int id, int skillId)
    {
        if (id == MenuAuto)
        {
            return NpcLabor.LaborText.T("co.mode.auto");
        }

        if (id == MenuOff)
        {
            return NpcLabor.LaborText.T("co.mode.off");
        }

        // Base production: one row to set up a standing order here, one row per order so it
        // can be stopped where it was set up (the board no longer lists production).
        if (id == MenuProduce)
        {
            return NpcLabor.LaborText.T("craft.ui.assign");
        }

        if (id <= MenuCancelBase)
        {
            int idx = MenuCancelBase - id;
            CraftJob? job = idx >= 0 && idx < _menuOrders.Count ? _menuOrders[idx] : null;
            return job == null
                ? NpcLabor.LaborText.T("craft.ui.cancel")
                : NpcLabor.LaborText.T("craft.ui.cancelOrder", job.ShortLabel);
        }

        Chara? c = RefChara.Get(id);
        if (c == null || PersonPickerUi.IsPcLike(c))
        {
            return c == null ? ("#" + id) : NpcLabor.LaborText.T("co.mode.you");
        }

        // A matching hobby/work prefix marks the "right" person at a glance:
        // 爱好·队 艾琳 when a hobby matches, 工作·队 艾琳 when only an assigned
        // work matches (the concrete hobby name is not shown).
        HobbyWorkKind kind = HobbyTag.GetRelevantKind(c, skillId);

        // Portrait carries identity; keep name readable but compact for subtext room.
        // The prefix eats into the same row width, so trim the name harder.
        string name = AssistantResolver.NameOf(c);
        int maxName = kind == HobbyWorkKind.None ? 8 : 5;
        if (name.Length > maxName)
        {
            name = name.Substring(0, maxName);
        }

        string tag = AssistantResolver.IsPartyMember(c)
            ? NpcLabor.LaborText.T("co.tag.party")
            : NpcLabor.LaborText.T("co.tag.home");
        if (kind == HobbyWorkKind.None)
        {
            return tag + " " + name;
        }

        string prefix = kind == HobbyWorkKind.Hobby
            ? NpcLabor.LaborText.T("co.prefix.hobby")
            : NpcLabor.LaborText.T("co.prefix.work");
        return prefix + "·" + tag + " " + name;
    }

    static string MenuSkillSub(int id, int skillId, string skillName)
    {
        if (id == MenuOff)
        {
            return NpcLabor.LaborText.T("co.menu.noAssist");
        }

        if (skillId <= 0)
        {
            return "";
        }

        string sn = skillName.IsEmpty() ? NpcLabor.LaborText.T("co.menu.skillShort") : skillName;
        if (sn.Length > 4)
        {
            sn = sn.Substring(0, 4);
        }

        if (id == MenuAuto)
        {
            Chara? best = AssistantResolver.FindBestForSkill(skillId);
            if (best == null)
            {
                return NpcLabor.LaborText.T("co.menu.noOne");
            }

            int v = best.Evalue(skillId);
            return v > 0 ? sn + " " + v : NpcLabor.LaborText.T("co.menu.noOne");
        }

        Chara? c = RefChara.Get(id);
        if (c == null)
        {
            return "";
        }

        int skill = c.Evalue(skillId);
        return skill > 0 ? sn + " " + skill : "";
    }

    static string GetSkillName(int skillId)
    {
        if (skillId <= 0)
        {
            return "";
        }

        try
        {
            Element el = Element.Create(skillId, 0);
            if (el != null && !el.Name.IsEmpty())
            {
                return el.Name;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("LayerCraftPatches.cs silent catch: " + __e.Message); }
return "";
    }

    static void ApplyMenuChoice(int id, LayerCraft? layer = null)
    {
        if (PersonPickerUi.SwallowRightClick("cocraft.applyChoice"))
        {
            return;
        }

        Plugin.LogInfo($"[cocraft] ApplyMenuChoice id={id} pinBefore={AssistantResolver.PinnedUid?.ToString() ?? "null"}");
        if (id == MenuAuto)
        {
            AssistantResolver.SetPin(AssistantResolver.AutoSentinel);
            Msg.SayRaw(NpcLabor.LaborText.T("co.msg.autoSet"));
            try { WorkbenchMemory.WriteCoCraft(layer?.factory, AssistantResolver.PinnedUid); } catch { }
            return;
        }

        if (id == MenuOff)
        {
            AssistantResolver.SetPin(AssistantResolver.OffSentinel);
            Msg.SayRaw(NpcLabor.LaborText.T("co.msg.off"));
            try { WorkbenchMemory.WriteCoCraft(layer?.factory, AssistantResolver.PinnedUid); } catch { }
            return;
        }

        AssistantResolver.SetPin(id);
        Chara? c = AssistantResolver.GetPinned();
        if (c != null)
        {
            Msg.SayRaw(NpcLabor.LaborText.T("co.msg.pinned", AssistantResolver.NameOf(c)));
        }
        else
        {
            Msg.SayRaw(NpcLabor.LaborText.T("co.msg.pinFailed"));
        }
        try { WorkbenchMemory.WriteCoCraft(layer?.factory, AssistantResolver.PinnedUid); } catch { }
    }

    /// <summary>Default-select the row matching the current pin (last chosen assistant).</summary>
    static void SelectCurrentPin(LayerList menu, List<int> options)
    {
        if (menu == null || menu.list == null || options == null)
        {
            return;
        }

        int? target = null;
        int? pin = AssistantResolver.PinnedUid;
        if (pin == AssistantResolver.AutoSentinel)
        {
            target = MenuAuto;
        }
        else if (pin == AssistantResolver.OffSentinel || !pin.HasValue)
        {
            target = MenuOff;
        }
        else
        {
            target = pin;
        }

        if (!target.HasValue)
        {
            return;
        }

        int idx = options.IndexOf(target.Value);
        if (idx >= 0)
        {
            try { menu.list.Select(idx); } catch (System.Exception __e) { Plugin.LogDebug("LayerCraftPatches.cs silent catch: " + __e.Message); }
}
    }

    static void RefreshButtonLabel(LayerCraft layer)
    {
        UIButton? btn = FindOurButton(layer);
        if (btn == null)
        {
            return;
        }

        // Button itself never shows NPC name — detail lives in the picker menu.
        SetButtonText(btn, NpcLabor.LaborTerms.Assist);

        UIButton? craft = FindCraftActionButton(layer);
        if (craft != null)
        {
            PlaceAssistChip(btn, craft);
        }
    }

    static void SetButtonText(UIButton btn, string label)
    {
        if (btn == null)
        {
            return;
        }

        if (btn.mainText != null)
        {
            btn.mainText.text = label;
            try
            {
                btn.mainText.SetText(label);
            }
            catch (System.Exception __e) { Plugin.LogDebug("LayerCraftPatches.cs silent catch: " + __e.Message); }
            try
            {
                ContentSizeFitter? f = btn.mainText.GetComponent<ContentSizeFitter>();
                if (f != null)
                {
                    f.enabled = false;
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("LayerCraftPatches.cs silent catch: " + __e.Message); }
}

        if (btn.subText != null)
        {
            try
            {
                btn.subText.SetActive(enable: false);
            }
            catch (System.Exception __e) { Plugin.LogDebug("LayerCraftPatches.cs silent catch: " + __e.Message); }
}
    }

    static int GetRecipeSkillId(LayerCraft layer)
    {
        try
        {
            if (layer?.recipe?.source != null)
            {
                return layer.recipe.source.GetReqSkill().id;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("LayerCraftPatches.cs silent catch: " + __e.Message); }
return 0;
    }

    static void DumpLayout(LayerCraft layer)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("co-craft dump windows=");
            sb.Append(layer.windows?.Count ?? 0);
            UIButton? craft = FindCraftActionButton(layer);
            sb.Append(" craftBtn=").Append(craft != null);
            if (craft != null)
            {
                sb.Append(" craftPath=").Append(GetPath(craft.transform));
                LayoutGroup? lg = craft.transform.parent != null
                    ? craft.transform.parent.GetComponent<LayoutGroup>()
                    : null;
                sb.Append(" parentLayout=").Append(lg != null ? lg.GetType().Name : "none");
            }

            Plugin.LogWarn(sb.ToString());
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("layout dump failed: " + ex.Message);
        }
    }

    static string GetPath(Transform t)
    {
        var parts = new List<string>();
        Transform? cur = t;
        int guard = 0;
        while (cur != null && guard++ < 14)
        {
            parts.Add(cur.name);
            cur = cur.parent;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }

    static Transform? FindChildRecursive(Transform parent, string name)
    {
        if (parent.name == name)
        {
            return parent;
        }

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform? found = FindChildRecursive(parent.GetChild(i), name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}

// A probe used to sit here on Layer.OnRightClick. It never logged a single line:
// Layer.UpdateInput routes a right press to option.screenClickCloseRight → OnBack(),
// and never through Layer.OnRightClick, so nothing there ever needed to be known.
//
// The stray-craft-on-close bug is not handled here. Closing the assistant
// picker consumes the dismissing click (Patches/PickerInputGuard.cs hooks
// Layer.Close → PersonPickerUi.NotePickerDismissed → EInput.Consume), so the
// craft button underneath never receives it.
