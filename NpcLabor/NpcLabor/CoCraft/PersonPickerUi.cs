using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NpcLabor.CoCraft;

/// <summary>
/// Shared person-row styling for LayerList pickers (co-craft + processor + dispatch).
/// Vanilla people lists use ItemGeneral.SetChara → Chara.SetImage on the row icon.
/// LayerList.SetList2 always DisableIcon first; we re-enable for real characters.
/// Full-body SetImage + SetNativeSize overflows LayerList a/b/c key letters — clamp and
/// park the icon to the right of the shortcut column.
/// </summary>
internal static class PersonPickerUi
{
    // Keep under the LayerList a/b/c keyText column so shortcuts stay readable.
    // Resident-board feel: compact portrait, never cover a/b/c key letters.
    const float IconSlot = 22f;
    const float IconOffsetX = 34f;
    const float TextAfterIconX = 64f;

    internal static void StyleRow(
        ItemGeneral item,
        int id,
        string label,
        string sub,
        int menuSelf = int.MinValue,
        int menuAuto = int.MinValue,
        int menuOff = int.MinValue)
    {
        if (item == null)
        {
            return;
        }

        var rowSw = System.Diagnostics.Stopwatch.StartNew();
        WireRowRightClick(item);

        Chara? portrait = ResolvePortrait(id, menuSelf);
        if (portrait == null)
        {
            try
            {
                item.DisableIcon();
            }
            catch (System.Exception __e) { Plugin.LogDebug("PersonPickerUi.cs silent catch: " + __e.Message); }

            SetRowText(item, id, label, sub, null, menuSelf, menuAuto, menuOff);
        }
        else if (DeferPortraits)
        {
            // The face is the expensive half of a row: SetChara pulls (and with skin
            // mods sometimes builds) a sprite per person. A 98-row picker used to spend
            // 150+ ms in SetList2 doing all of them on the opening frame. Draw the row
            // now — name and skill value are what you pick by — and fill faces over the
            // next few frames.
            try
            {
                item.DisableIcon();
            }
            catch (System.Exception __e) { Plugin.LogDebug("PersonPickerUi.cs silent catch: " + __e.Message); }

            SetRowText(item, id, label, sub, portrait, menuSelf, menuAuto, menuOff);

            ItemGeneral row = item;
            Chara face = portrait;
            EnqueuePortrait(() => ApplyFace(row, face, id, label, sub, menuSelf, menuAuto, menuOff));
        }
        else
        {
            ApplyFace(item, portrait, id, label, sub, menuSelf, menuAuto, menuOff);
        }

        NoteRowCost(rowSw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Give every picker row the framework's own right-click hook. UIButton checks
    /// onRightClick *before* it decides to press and, when it fires, onClick never
    /// runs — so a right-click on a row closes the picker instead of picking.
    ///
    /// (Elin's input module has been seen reporting a right press as Left, in which
    /// case this never fires and the press guards in Patches/PickerInputGuard.cs,
    /// which do not look at the button index at all, take over instead.)
    /// </summary>
    static void WireRowRightClick(ItemGeneral item)
    {
        try
        {
            UIButton? btn = item?.button1;
            if (btn == null || btn.onRightClick != null)
            {
                return;
            }

            btn.onRightClick = () =>
            {
                Plugin.LogInfo("[rightclick] row onRightClick — close without applying");
                ClosePicker();
            };
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("WireRowRightClick: " + ex.Message);
        }
    }

    /// <summary>SetChara + icon slot + our own text. SetChara overwrites the name, so text always goes last.</summary>
    static void ApplyFace(ItemGeneral item, Chara portrait, int id, string label, string sub, int menuSelf, int menuAuto, int menuOff)
    {
        if (item == null || portrait == null)
        {
            return;
        }

        try
        {
            item.SetChara(portrait);
        }
        catch
        {
            try
            {
                if (item.button1?.icon != null)
                {
                    portrait.SetImage(item.button1.icon);
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("PersonPickerUi.cs silent catch: " + __e.Message); }
        }

        EnableIcon(item);
        SetRowText(item, id, label, sub, portrait, menuSelf, menuAuto, menuOff);
        FitIconSlot(item);
    }

    static void SetRowText(ItemGeneral item, int id, string label, string sub, Chara? portrait, int menuSelf, int menuAuto, int menuOff)
    {
        // Always apply our own label after SetChara overwrites the name.
        try
        {
            if (item.button1?.mainText != null)
            {
                FontColor color = FontColor.ButtonGeneral;
                if (id == menuSelf)
                {
                    color = FontColor.Good;
                }
                else if (id == menuOff)
                {
                    color = FontColor.Bad;
                }
                else if (id == menuAuto)
                {
                    color = FontColor.Default;
                }
                else if (portrait != null && portrait.IsPCParty)
                {
                    color = FontColor.Good;
                }

                item.button1.mainText.SetText(label ?? "", color);
            }
        }
        catch
        {
            try
            {
                if (item.button1?.mainText != null)
                {
                    item.button1.mainText.text = label ?? "";
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("PersonPickerUi.cs silent catch: " + __e.Message); }
}

        // No item.Build() here on purpose. Every caller passes this as the
        // onInstantiate callback of LayerList.SetList2, and vanilla already does
        // `onInstantiate(a, item); item.Build();` right after we return — calling
        // Build twice per row per open was pure duplicated layout work.
        // Long picker labels (e.g. 爱好·队 + name) scroll right instead of wrapping to a second line.
        try
        {
            if (item.button1?.mainText != null)
            {
                item.button1.mainText.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("PersonPickerUi.cs silent catch: " + __e.Message); }
        // Build / SetChara may re-expand the sprite; keep the small slot.
        // Only the final pass at the end of this method does it — the framework's
        // own item.Build() runs after we return, so re-running this between the
        // icon setup and the subText block just tripled the GetComponent work for
        // no layout benefit.
        if (!string.IsNullOrEmpty(sub))
        {
            try
            {
                // Do NOT call ItemGeneral.SetSubText: it runs lang.lang() and drops free-form Chinese.
                if (item.button1?.subText != null)
                {
                    item.button1.subText.SetActive(enable: true);
                    try
                    {
                        item.button1.subText.SetText(sub, FontColor.Default);
                    }
                    catch
                    {
                        item.button1.subText.text = sub;
                    }

                    item.button1.subText.alignment = TextAnchor.MiddleRight;
                    try
                    {
                        item.button1.subText.rectTransform.anchoredPosition = new Vector2(210f, 0f);
                    }
                    catch (System.Exception __e) { Plugin.LogDebug("PersonPickerUi.cs silent catch: " + __e.Message); }
                    try
                    {
                        if (item.button1.mainText != null)
                        {
                            RectTransform mrt = item.button1.mainText.rectTransform;
                            mrt.sizeDelta = new Vector2(Mathf.Min(mrt.sizeDelta.x <= 0 ? 170f : mrt.sizeDelta.x, 170f), mrt.sizeDelta.y);
                        }
                    }
                    catch (System.Exception __e) { Plugin.LogDebug("PersonPickerUi.cs silent catch: " + __e.Message); }
}
            }
            catch (System.Exception __e) { Plugin.LogDebug("PersonPickerUi.cs silent catch: " + __e.Message); }
}

        // Final pass: SetSubText / Build can reshuffle transforms, so the icon slot
        // is re-fitted by ApplyFace after this, not here.
    }

    /// <summary>
    /// Per-open cost of one picker row. Call <see cref="BeginRowCost"/> before
    /// building a list and <see cref="LogRowCost"/> after, so the number covers
    /// exactly one open instead of a rolling window that may never line up with
    /// the row count.
    /// </summary>
    static long _rowMsTotal;
    static int _rowCalls;

    internal static void BeginRowCost()
    {
        _rowMsTotal = 0;
        _rowCalls = 0;
        ClearPortraitQueue();
    }

    // ---------- deferred row portraits ----------
    //
    // A picker row costs two very different things: the text (cheap) and the face
    // (SetChara → sprite per person). Doing both inline meant every open paid for
    // every face on one frame. Rows are drawn text-only and the faces are filled a
    // few per frame from Plugin.Update.

    /// <summary>Faces applied per frame. 8 keeps a 98-row list under ~0.2 s of trickle.</summary>
    const int FacesPerFrame = 8;

    static readonly List<Action> _faces = new List<Action>();
    static int _faceHead;
    static long _faceMs;

    internal static bool DeferPortraits => true;

    static void EnqueuePortrait(Action apply)
    {
        _faces.Add(apply);
    }

    /// <summary>Apply a few queued faces. Called from Plugin.Update.</summary>
    internal static void DrainFaces()
    {
        if (_faceHead >= _faces.Count)
        {
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int done = 0;

        int budget = FacesPerFrame;
        while (_faceHead < _faces.Count && budget-- > 0)
        {
            Action apply = _faces[_faceHead];
            _faceHead++;
            done++;

            try
            {
                apply();
            }
            catch (System.Exception ex)
            {
                // Rows can be destroyed before their turn (picker closed): skip silently.
                Plugin.LogDebug("portrait drain: " + ex.Message);
            }
        }

        _faceMs += sw.ElapsedMilliseconds;

        if (_faceHead >= _faces.Count)
        {
            // What the faces cost in total, deferred across frames — the number that
            // used to land inside SetList2 on the opening frame.
            Plugin.LogInfo($"[pickertime] faces x{_faces.Count} total={_faceMs}ms");
            _faceMs = 0;
            _faces.Clear();
            _faceHead = 0;
        }
    }

    internal static void ClearPortraitQueue()
    {
        _faces.Clear();
        _faceHead = 0;
    }

    internal static void LogRowCost(string tag)
    {
        if (_rowCalls <= 0)
        {
            return;
        }

        Plugin.LogInfo($"[pickertime] {tag} StyleRow avg={_rowMsTotal / (double)_rowCalls:0.00}ms x{_rowCalls} rows");
        _rowMsTotal = 0;
        _rowCalls = 0;
    }

    static void NoteRowCost(long ms)
    {
        _rowMsTotal += ms;
        _rowCalls++;
    }

    static Chara? ResolvePortrait(int id, int menuSelf)
    {
        if (id == menuSelf)
        {
            return EClass.pc;
        }

        // Special menu sentinels (auto/off/…) have no portrait.
        if (id <= 0)
        {
            return null;
        }

        Chara? c = RefChara.Get(id);
        if (c == null || c.IsPC || c == EClass.pc)
        {
            return null;
        }

        // Measured: one row face costs ~105 ms the first time that person's sprite is
        // read (BCS custom skins decode a big sheet per person). A 96-resident picker
        // therefore spent 10 s painting faces — ten seconds of stutter, or one ten
        // second freeze before the faces were deferred. Only companions get a face;
        // everyone else is a name + skill value row, which is what you pick by.
        if (!IsFaced(c))
        {
            return null;
        }

        return c;
    }

    /// <summary>Party companions (and the PC row) are the only rows worth a face.</summary>
    static bool IsFaced(Chara c)
    {
        try
        {
            return c.IsPCParty || AssistantResolver.IsPartyMember(c);
        }
        catch
        {
            return false;
        }
    }

    static void EnableIcon(ItemGeneral item)
    {
        try
        {
            if (item.button1?.icon == null)
            {
                return;
            }

            // DisableIcon turns off icon.transform.parent; reverse that.
            Transform parent = item.button1.icon.transform.parent;
            if (parent != null)
            {
                parent.gameObject.SetActive(true);
            }

            item.button1.icon.gameObject.SetActive(true);
            item.button1.icon.enabled = true;

            // SetList2 calls DisableIcon first (mainText x=20 when no keyText).
            // With keyText present, leave letter free and start name after portrait.
            if (item.button1.mainText != null)
            {
                RectTransform rt = item.button1.mainText.rectTransform;
                float x = item.button1.keyText != null ? TextAfterIconX : TextAfterIconX - 8f;
                rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("PersonPickerUi.cs silent catch: " + __e.Message); }
}

    /// <summary>
    /// Clamp full-body SetImage sprites into a compact slot and park them to the
    /// right of LayerList a/b/c key letters so shortcuts stay readable.
    /// </summary>
    static void FitIconSlot(ItemGeneral item)
    {
        try
        {
            Image? icon = item?.button1?.icon;
            if (icon == null)
            {
                return;
            }

            icon.preserveAspect = true;
            icon.raycastTarget = false;

            RectTransform rt = icon.rectTransform;
            // SetImage changes pivot for full-body sprites; that makes even a small
            // sizeDelta spill left over the a/b/c key letter. Center the slot.
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(IconSlot, IconSlot);
            rt.anchoredPosition = new Vector2(IconOffsetX + IconSlot * 0.5f, 0f);

            // Nudge past keyText (a/b/c) which sits on the far left of the row.
            try
            {
                Vector2 pos = rt.anchoredPosition;
                if (pos.x < IconOffsetX)
                {
                    rt.anchoredPosition = new Vector2(IconOffsetX, pos.y);
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("PersonPickerUi.cs silent catch: " + __e.Message); }
// Avoid layout drivers re-expanding to native texture size.
            try
            {
                var fitter = icon.GetComponent<ContentSizeFitter>();
                if (fitter != null)
                {
                    fitter.enabled = false;
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("PersonPickerUi.cs silent catch: " + __e.Message); }
            try
            {
                // Kill any leftover native scale from SetImage pivots.
                icon.transform.localScale = Vector3.one;
            }
            catch (System.Exception __e) { Plugin.LogDebug("PersonPickerUi.cs silent catch: " + __e.Message); }
// Parent mask/frame: shrink oversized frames only.
            try
            {
                Transform parent = icon.transform.parent;
                if (parent != null && parent != item!.button1!.transform)
                {
                    RectTransform? prt = parent as RectTransform;
                    if (prt != null)
                    {
                        Vector2 ps = prt.sizeDelta;
                        if (ps.x > IconSlot + 8f || ps.y > IconSlot + 8f || ps.x <= 0f || ps.y <= 0f)
                        {
                            prt.sizeDelta = new Vector2(IconSlot + 4f, IconSlot + 4f);
                        }

                        Vector2 ppos = prt.anchoredPosition;
                        if (ppos.x < IconOffsetX - 4f)
                        {
                            prt.anchoredPosition = new Vector2(IconOffsetX - 4f, ppos.y);
                        }
                    }
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("PersonPickerUi.cs silent catch: " + __e.Message); }
// Keep shortcut letter above the portrait sprite in draw order.
            try
            {
                UIText? key = item?.button1?.keyText;
                if (key != null)
                {
                    key.gameObject.SetActive(true);
                    key.enabled = true;
                    key.transform.SetAsLastSibling();
                    try
                    {
                        RectTransform krt = key.rectTransform;
                        // Park letter on the far left; portrait starts at IconOffsetX.
                        if (krt.anchoredPosition.x > 18f)
                        {
                            krt.anchoredPosition = new Vector2(8f, krt.anchoredPosition.y);
                        }
                    }
                    catch (System.Exception __e) { Plugin.LogDebug("PersonPickerUi.cs silent catch: " + __e.Message); }
                    try
                    {
                        CanvasGroup? cg = key.GetComponent<CanvasGroup>();
                        if (cg != null)
                        {
                            cg.alpha = 1f;
                            cg.blocksRaycasts = false;
                        }
                    }
                    catch (System.Exception __e) { Plugin.LogDebug("PersonPickerUi.cs silent catch: " + __e.Message); }
                    try
                    {
                        // Ensure letter draws on top of any oversized sprite bleed.
                        var c = key.color;
                        c.a = 1f;
                        key.color = c;
                    }
                    catch (System.Exception __e) { Plugin.LogDebug("PersonPickerUi.cs silent catch: " + __e.Message); }
}

                // Icon under keyText.
                icon.transform.SetAsFirstSibling();
            }
            catch (System.Exception __e) { Plugin.LogDebug("PersonPickerUi.cs silent catch: " + __e.Message); }
}
        catch (System.Exception __e) { Plugin.LogDebug("PersonPickerUi.cs silent catch: " + __e.Message); }
}

    // ---------- action button portrait (the 加工 button) ----------
    //
    // Uses the framework's own UIButton.icon slot — measured at 32x32, anchored to
    // the button centre (0.5,0.5) — instead of a hand-placed Image child. Earlier
    // rounds hand-rolled a child node and had to hardcode size/pad/rise offsets to
    // chase the right pixels; the prefab already has a slot for exactly this.
    //
    // Measured layout of the cloned mold (ButtonWindowMenu_Simple):
    //   self     60x60  aMin=aMax=(0,1)        aPos=(120,-78)
    //   mainText 100x40 aMin=aMax=(1,0)        aPos=(-10.2,28.7) align=LowerRight
    //   icon     32x32  aMin=aMax=(0.5,0.5)    aPos=(0,2)
    // mainText is NOT parented to the button — it is a separate box anchored to the
    // container — so this must never try to reflow it.

    /// <summary>One-shot marker so the metrics dump fires once per button instance.</summary>
    sealed class LoggedFlag
    {
    }

    static readonly ConditionalWeakTable<UIButton, LoggedFlag> LoggedButtons
        = new ConditionalWeakTable<UIButton, LoggedFlag>();

    /// <summary>
    /// Show <paramref name="portrait"/> in the button's own icon slot, or clear it
    /// when null. Touches nothing but the icon: no extra GameObjects, no offsets.
    /// </summary>
    internal static void StyleActionButton(UIButton btn, Chara? portrait)
    {
        if (btn == null)
        {
            return;
        }

        try
        {
            DumpMetricsOnce(btn);

            Image? icon = btn.icon;
            if (icon == null)
            {
                return;
            }

            if (portrait == null)
            {
                icon.SetActive(enable: false);
                return;
            }

            // The mold's icon may sit under a frame node; the clone kept it, and
            // disabling the icon must not take the frame's layout with it.
            Transform? frame = icon.transform.parent;
            if (frame != null && frame != btn.transform)
            {
                frame.gameObject.SetActive(true);
            }

            portrait.SetImage(icon);

            // SetImage can hand back a tinted sprite, and this button has a dark
            // panel behind it — anything less than full white reads as muddy.
            icon.color = Color.white;
            icon.enabled = true;
            icon.gameObject.SetActive(true);
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("StyleActionButton: " + ex.Message);
        }
    }

    /// <summary>True when this button currently shows a portrait.</summary>
    internal static bool HasButtonPortrait(UIButton btn)
    {
        if (btn == null)
        {
            return false;
        }

        try
        {
            return btn.icon != null && btn.icon.gameObject.activeSelf;
        }
        catch
        {
            return false;
        }
    }


    // ---------- right-click cancel: back to the list, never craft ----------
    //
    // Closing the assistant picker (X / ESC / right-click) used to make vanilla start
    // a craft on its own. The picker is a Layer floating on top of the craft window,
    // and the click that dismisses it does not stop there: UIButton dispatches its
    // press at the end of the frame (WaitForEndOfFrame), by which time the picker is
    // gone and the craft button underneath receives that same click. OnClickCraft
    // then runs and the PC starts crafting.
    //
    // The framework already has the right tool for this. EInput.Consume(n) raises
    // EInput.skipFrame, and every deferred UIButton press re-checks skipFrame before
    // firing (UIButton.<OnPointerClick>b__0: `if (EInput.skipFrame > 0) return;`).
    // So consuming the input while the picker is being closed drops the stray press
    // before it can reach the button underneath — that is the engine's own
    // "this input is already handled" contract, not a timing guess.
    //
    // Nothing here is frame-counted and nothing inspects which mouse button went
    // down: closing the picker owns the click, so the picker eats it.

    /// <summary>Raw right-button truth, read the way Unity reports it on the press frame.</summary>
    internal static bool RawRightDown()
    {
        try
        {
            if (Input.GetMouseButtonDown(1) || Input.GetMouseButton(1))
            {
                return true;
            }

            var rm = EInput.rightMouse;
            return rm != null && (rm.down || rm.pressing);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Call at the top of a picker onClick. Returns true (apply nothing) when the
    /// click was a right-click — used by the other pickers too.
    /// </summary>
    internal static bool SwallowRightClick(string who)
    {
        if (!RawRightDown())
        {
            return false;
        }

        Plugin.LogInfo($"[rightclick] {who} swallowed a right-click (open={PickerOpen})");
        return true;
    }

    static LayerList? _openPicker;

    /// <summary>The picker layer currently on screen.</summary>
    internal static bool PickerOpen => _openPicker != null;

    // The picker floats over the craft window, and the click that dismisses it does not
    // die with the layer: UIButton dispatches presses at the end of the frame, so once
    // the picker is gone that same click can still press whatever sits where the X (or
    // the cancel row) was - in the craft window, the craft button. Vanilla's
    // LayerCraft.OnClickCraft cannot tell that press from a real one, so the picker marks
    // the gesture instead: from the moment it closes until the player starts the next
    // click, a craft press is a lie (see CraftPressIsStray).
    //
    // Gesture-scoped, not frame-scoped: it ends when the player *starts a new click*
    // (GetMouseButtonDown), which is the only way a genuine craft click can begin. So the
    // guard spans however many frames the dismissing press takes to be delivered - it
    // cannot expire while a leaked press is still pending - and it never survives into the
    // click that follows it. No frame counting, and no guessing at which mouse button the
    // EventSystem reported.
    static bool _strayCraft;

    /// <summary>
    /// True while a craft press cannot be genuine: the picker is still on screen, or the
    /// click that dismissed it is still the click in progress.
    /// </summary>
    internal static bool CraftPressIsStray => _openPicker != null || _strayCraft;

    /// <summary>
    /// Arm the guard for the click that is dismissing the picker. Called from
    /// <see cref="ConsumeClosingClick"/>, so every close path that eats the click also
    /// flags the gesture.
    /// </summary>
    static void ArmStrayCraftGuard()
    {
        try
        {
            if (Input.GetMouseButton(0) || Input.GetMouseButton(1)
                || Input.GetMouseButtonUp(0) || Input.GetMouseButtonUp(1))
            {
                _strayCraft = true;
            }
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("ArmStrayCraftGuard: " + ex.Message);
        }
    }

    /// <summary>
    /// Drop the guard when the player starts a new click. Driven from Plugin.Update, and
    /// that is early enough: a press is always dispatched at the end of the frame
    /// (UIButton queues it for WaitForEndOfFrame), so the clear lands before anything the
    /// new click can invoke.
    /// </summary>
    internal static void TickStrayCraftGuard()
    {
        if (!_strayCraft)
        {
            return;
        }

        try
        {
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
            {
                _strayCraft = false;
            }
        }
        catch
        {
            _strayCraft = false;
        }
    }

    /// <summary>The picker itself, for hooks that need to know it was the one closing.</summary>
    internal static LayerList? OpenPicker => _openPicker;

    /// <summary>
    /// Track the picker. On close (X / ESC / right-click / picking the "off" row) the
    /// click that dismissed it is consumed, so the button underneath never sees it and
    /// the player stays on the recipe list instead of starting a craft.
    ///
    /// No row-pick bookkeeping is needed: a deliberate pick also closes the picker, and
    /// dropping that click is correct too — <c>ApplyMenuChoice</c> has already run and
    /// set the pin by then.
    /// </summary>
    internal static void SetOpenPicker(LayerList? menu)
    {
        if (menu != null)
        {
            _openPicker = menu;
            return;
        }

        if (_openPicker == null)
        {
            return;
        }

        _openPicker = null;
        ConsumeClosingClick();
    }

    /// <summary>
    /// Announce that a picker-layer dismissal happened, without touching
    /// <paramref name="menu"/>'s own bookkeeping.
    ///
    /// This is the reliable half of the fix. <c>SetOpenPicker(null)</c> only runs on
    /// the paths we own; the X button, a right-click outside the list and ESC are
    /// driven by the layer system and never call us. This hook sees all of them, and
    /// it runs while the dismissing click is still the current one — which matters,
    /// because <c>Layer.Close</c> may finish asynchronously (the close-out tween) long
    /// after the press it should have eaten was dispatched.
    /// </summary>
    internal static void NotePickerDismissed(Layer dismissed)
    {
        if (dismissed == null || !ReferenceEquals(dismissed, _openPicker))
        {
            return;
        }

        _openPicker = null;
        ConsumeClosingClick();
    }

    /// <summary>
    /// Eat the click that closed the picker.
    ///
    /// <c>UIButton</c> queues a press in <c>OnPointerDown</c> and dispatches it from a
    /// <c>WaitForEndOfFrame</c> callback. The picker is gone by the time that callback
    /// runs, so the press lands on whatever sits underneath — here the vanilla craft
    /// button, which starts a craft nobody asked for. That callback re-checks
    /// <c>EInput.skipFrame</c> before firing
    /// (<c>UIButton.&lt;OnPointerDown&gt;b__68_1</c>: <c>if (EInput.skipFrame &gt; 0) return;</c>),
    /// so raising the flag is enough to drop the press. This is the engine's own "this
    /// input is already handled" contract, not a timing guess.
    ///
    /// There is deliberately no frame counting and no "which button went down" test
    /// here. An earlier attempt at both was the wrong shape: it could not tell a
    /// deliberate pick from a dismissal, and the pick is exactly the case where the
    /// click must still be dropped (<c>ApplyMenuChoice</c> has already run by then).
    ///
    /// Callers: <see cref="SetOpenPicker"/> for the paths we own, and
    /// <c>Patches/PickerInputGuard.cs</c> for the ones the layer system drives.
    /// </summary> </summary>
    internal static void ConsumeClosingClick()
    {
        try
        {
            // 1 is enough: the stray press is the one already queued for this frame.
            EInput.Consume(1);
            // EInput.Consume only helps if the leaked press is dispatched *after* this
            // call; the craft window's press can equally be dispatched before it. The
            // gesture flag does not depend on that ordering - it makes the whole gesture
            // unable to press craft, which is what "this click closed the picker" means.
            ArmStrayCraftGuard();
            Plugin.LogInfo("[picker] closed — click consumed, craft press blocked until release");
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("ConsumeClosingClick: " + ex.Message);
        }
    }

    /// <summary>Close our picker. Right-click on a row lands here and must not apply a choice.</summary>
    internal static void ClosePicker()
    {
        LayerList? menu = _openPicker;
        SetOpenPicker(null);
        if (menu == null)
        {
            return;
        }

        ClearPortraitQueue();
        try { menu.Close(); } catch (System.Exception ex) { Plugin.LogDebug("ClosePicker: " + ex.Message); }
    }

    /// <summary>Everything the framework could be telling us about this click, for the log.</summary>
    internal static string InputSnapshot()
    {
        try
        {
            var rm = EInput.rightMouse;
            var lm = EInput.leftMouse;
            return "rmb=" + Input.GetMouseButton(1)
                + " rmbDown=" + Input.GetMouseButtonDown(1)
                + " lmb=" + Input.GetMouseButton(0)
                + " eRight(down=" + (rm == null ? "?" : rm.down.ToString())
                + " pressing=" + (rm == null ? "?" : rm.pressing.ToString())
                + " consumed=" + (rm == null ? "?" : rm.consumed.ToString())
                + " wasConsumed=" + (rm == null ? "?" : rm.wasConsumed.ToString())
                + " ignoreClick=" + (rm == null ? "?" : rm.ignoreClick.ToString()) + ")"
                + " eLeftDown=" + (lm == null ? "?" : lm.down.ToString())
                + " skipFrame=" + EInput.skipFrame
                + " layerSkipInput=" + Layer.skipInput
                + " layerRightClicked=" + Layer.rightClicked
                + " closeOnRightClick=" + Layer.closeOnRightClick
                + " pickerOpen=" + PickerOpen;
        }
        catch (System.Exception ex)
        {
            return "snapshot failed: " + ex.Message;
        }
    }

    /// <summary>Print who actually called into us. Rate-limited because production can start an AI_UseCrafter per crafted unit.</summary>
    internal static void LogTrace(string tag)
    {
        if (Time.realtimeSinceStartup - _lastTraceAt < 1.5f)
        {
            return;
        }

        _lastTraceAt = Time.realtimeSinceStartup;

        try
        {
            var st = new System.Diagnostics.StackTrace(1, false);
            var sb = new StringBuilder();
            sb.Append(tag);

            int frames = Math.Min(st.FrameCount, 24);
            for (int i = 0; i < frames; i++)
            {
                System.Reflection.MethodBase? m = st.GetFrame(i)?.GetMethod();
                if (m == null)
                {
                    continue;
                }

                sb.Append("\n    ").Append(m.DeclaringType?.Name ?? "?").Append("::").Append(m.Name);
            }

            Plugin.LogInfo(sb.ToString());
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("trace: " + ex.Message);
        }
    }

    static float _lastTraceAt = -999f;


    static void DumpMetricsOnce(UIButton btn)
    {
        if (LoggedButtons.TryGetValue(btn, out _))
        {
            return;
        }

        LoggedButtons.GetValue(btn, _ => new LoggedFlag());

        try
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("[btnmetrics] ").Append(btn.gameObject.name);
            sb.Append(" parent=").Append(btn.transform.parent != null ? btn.transform.parent.name : "-");
            sb.Append(" | self ").Append(Describe(btn.transform as RectTransform));
            sb.Append(" | mainText ").Append(Describe(btn.mainText != null ? btn.mainText.rectTransform : null));
            if (btn.mainText != null)
            {
                sb.Append(" align=").Append(btn.mainText.alignment)
                  .Append(" font=").Append(btn.mainText.fontSize);
            }

            sb.Append(" | icon ").Append(Describe(btn.icon != null ? btn.icon.rectTransform : null));
            if (btn.icon != null)
            {
                Transform? ip = btn.icon.transform.parent;
                sb.Append(" iconParent=").Append(ip != null ? ip.name : "-");
            }

            Transform? mold = btn.transform.parent != null && btn.transform.parent.childCount > 1
                ? btn.transform.parent.GetChild(0) as Transform
                : null;
            sb.Append(" | sibling0 ").Append(mold != null ? mold.name : "-")
              .Append(Describe(mold as RectTransform));

            Plugin.LogInfo(sb.ToString());
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("DumpMetricsOnce: " + ex.Message);
        }
    }

    static string Describe(RectTransform? rt)
    {
        if (rt == null)
        {
            return "=null";
        }

        return " {rect=" + Fmt(rt.rect.size) + "/" + Fmt(rt.rect.position) + " pivot=" + Fmt(rt.pivot)
             + " aMin=" + Fmt(rt.anchorMin) + " aMax=" + Fmt(rt.anchorMax)
             + " aPos=" + Fmt(rt.anchoredPosition) + " size=" + Fmt(rt.sizeDelta)
             + " offMin=" + Fmt(rt.offsetMin) + " offMax=" + Fmt(rt.offsetMax) + "}";
    }

    static string Fmt(Vector2 v) => "(" + v.x.ToString("0.##") + "," + v.y.ToString("0.##") + ")";

    /// <summary>True if this chara must never appear as a worker/assistant option.</summary>
    internal static bool IsPcLike(Chara? c)
    {
        if (c == null)
        {
            return true;
        }

        if (c.IsPC || c == EClass.pc)
        {
            return true;
        }

        try
        {
            if (EClass.pc != null && c.uid == EClass.pc.uid)
            {
                return true;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("PersonPickerUi.cs silent catch: " + __e.Message); }
        return false;
    }

    /// <summary>
    /// Unique residents who cannot join the party (Demitas etc.) stay off
    /// co-craft, processor, town labor, and dispatch pickers.
    /// </summary>
    internal static bool IsStayHomeUnique(Chara? c)
    {
        if (c == null)
        {
            return false;
        }

        try
        {
            if (c.trait is TraitChara tc && tc.IsUnique && !tc.CanJoinParty)
            {
                return true;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("PersonPickerUi.cs silent catch: " + __e.Message); }

        return false;
    }

    /// <summary>
    /// Co-craft/processor-style hobby/work designator for picker rows:
    /// 爱好·队伍 艾琳 when a hobby matches, 工作·队伍 艾琳 when only an assigned
    /// work matches, else plain 队伍 艾琳. Truncates the name harder when tagged
    /// so the prefix fits on the row. partyTag is an already-localized label
    /// (队伍 / Party / 隊列) appended only for party members.
    /// </summary>
    internal static string HobbyTaggedLabel(
        Chara c,
        IReadOnlyCollection<int> skillIds,
        string partyTag,
        string name)
    {
        HobbyWorkKind kind = HobbyTag.GetRelevantKind(c, skillIds);

        int maxName = kind == HobbyWorkKind.None ? 8 : 5;
        if (name.Length > maxName)
        {
            name = name.Substring(0, maxName);
        }

        string tag = "";
        if (!string.IsNullOrEmpty(partyTag))
        {
            try
            {
                if (c.IsPCParty || (EClass.pc?.party?.members?.Contains(c) ?? false))
                {
                    tag = partyTag + " ";
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("PersonPickerUi.cs silent catch: " + __e.Message); }
        }

        if (kind == HobbyWorkKind.None)
        {
            return tag + name;
        }

        string prefix = kind == HobbyWorkKind.Hobby
            ? NpcLabor.LaborText.T("co.prefix.hobby")
            : NpcLabor.LaborText.T("co.prefix.work");
        return prefix + "·" + tag + name;
    }
}
