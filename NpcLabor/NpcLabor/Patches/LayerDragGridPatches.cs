using System.Collections.Generic;
using HarmonyLib;
using NpcLabor.CoCraft;
using NpcLabor.Process;
using UnityEngine;
using UnityEngine.UI;

namespace NpcLabor.Patches;

/// <summary>
/// Slice B: processor operator mode switch on LayerDragGrid (加工).
/// Mode off  = PC crafts as vanilla when grid fills.
/// Mode on   = filling the grid starts an NPC job and closes the UI.
/// </summary>
[HarmonyPatch(typeof(LayerDragGrid))]
internal static class LayerDragGridPatches
{
    const string ButtonName = "NpcLabor_OutsourceBtn";
    const string JobsButtonName = "NpcLabor_JobsBtn";
    const int MenuSelf = -200;
    const int MenuAuto = -100;

    /// <summary>
    /// After we intercept TryStartCraft, OnProcess still calls RedrawButton.
    /// Closing/clearing the layer first makes RefreshCost NRE; suppress during the
    /// deferred start/close window only. Frame-stamped so the flag can never leak.
    /// </summary>
    internal static bool SuppressUiRefresh;
    static int _ensureButtonFrame = -1;
    static int _suppressFrame = -1;
    static bool _startPending;

    [HarmonyPostfix]
    [HarmonyPatch(nameof(LayerDragGrid.SetInv))]
    static void SetInvPostfix(LayerDragGrid __instance, InvOwnerDraglet owner)
    {
        try
        {
            EnsureModeButton(__instance, owner);
            // Some machines finish layout after SetInv; re-try next frame.
            ScheduleEnsureButton(__instance);
        }
        catch (System.Exception ex)
        {
            Plugin.LogWarn("processor mode button failed: " + ex);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(LayerDragGrid.RefreshCost))]
    static bool RefreshCostPrefix(LayerDragGrid __instance)
    {
        ExpireStaleSuppress();
        if (SuppressUiRefresh)
        {
            return false;
        }

        // Layer may already be tearing down after process close.
        if (__instance == null || !__instance || __instance.owner == null || __instance.owner.owner == null)
        {
            return false;
        }

        return true;
    }

    /// <summary>Clear the one-shot start state (suppress flag + pending guard).</summary>
    static void ClearStartState()
    {
        SuppressUiRefresh = false;
        _suppressFrame = -1;
        _startPending = false;
    }

    /// <summary>
    /// Bounded-lifetime safety: if the deferred reset was lost (action queue cleared,
    /// layer destroyed before FinishStart ran), the global flag auto-expires so it can
    /// never stick across frames or suppress other layers.
    /// </summary>
    internal static void ExpireStaleSuppress()
    {
        if (!SuppressUiRefresh || _suppressFrame < 0)
        {
            return;
        }

        if (Time.frameCount - _suppressFrame > 2)
        {
            ClearStartState();
        }
    }

    static void ScheduleEnsureButton(LayerDragGrid layer)
    {
        if (layer == null)
        {
            return;
        }

        try
        {
            // Avoid spamming many identical next-frame jobs in one open.
            int frame = Time.frameCount;
            if (_ensureButtonFrame == frame)
            {
                return;
            }

            _ensureButtonFrame = frame;
            EClass.core.actionsNextFrame.Add(delegate
            {
                try
                {
                    if (layer == null || !layer)
                    {
                        return;
                    }

                    EnsureModeButton(layer, layer.owner);
                }
                catch (System.Exception ex)
                {
                    Plugin.LogDebug("deferred EnsureModeButton: " + ex.Message);
                }
            });
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("ScheduleEnsureButton: " + ex.Message);
        }
    }

    static void EnsureModeButton(LayerDragGrid layer, InvOwnerDraglet? owner)
    {
        if (layer == null)
        {
            return;
        }

        TraitCrafter? crafter = (owner as InvOwnerCraft)?.crafter
            ?? owner?.owner?.trait as TraitCrafter;

        bool supported = ProcessorWhitelist.IsSupported(crafter);
        UIButton? existing = FindOurButton(layer);

        if (!supported)
        {
            if (existing != null)
            {
                existing.gameObject.SetActive(false);
            }

            UIButton? staleJobs = FindJobsButton(layer);
            if (staleJobs != null)
            {
                staleJobs.gameObject.SetActive(false);
            }

            return;
        }

        try { ProcessorOutsourceMode.ModeUid = WorkbenchMemory.ReadProcessor(crafter?.owner); } catch { }
        if (existing != null)
        {
            existing.gameObject.SetActive(true);
            Wire(existing, layer, crafter!);
            RefreshLabel(existing, crafter!);
            EnsureJobsButton(layer, crafter!);
            return;
        }

        UIButton? created = TryCreateButton(layer, ButtonName, "continue");
        if (created == null)
        {
            Plugin.LogWarn("processor mode: no host button slot on " + (crafter?.GetType().Name ?? "?"));
            return;
        }

        created.gameObject.name = ButtonName;
        created.gameObject.SetActive(true);
        Wire(created, layer, crafter!);
        RefreshLabel(created, crafter!);
        EnsureJobsButton(layer, crafter!);
        Plugin.LogInfo(
            "processor mode button ready for " + crafter!.GetType().Name
            + " under " + GetPath(created.transform));
    }

    /// <summary>
    /// Second button next to the operator-mode chip: opens the all-jobs list. Shown on
    /// every supported machine; with zero jobs the list shows the empty placeholder.
    /// </summary>
    static void EnsureJobsButton(LayerDragGrid layer, TraitCrafter crafter)
    {
        UIButton? existing = FindJobsButton(layer);
        if (existing != null)
        {
            existing.gameObject.SetActive(true);
            existing.onClick.RemoveAllListeners();
            existing.SetOnClick(() => ProcessJobUi.OpenJobsMenu());
            SetLabel(existing, NpcLabor.LaborText.T("proc.ui.jobsTitle"));
            return;
        }

        UIButton? created = TryCreateButton(layer, JobsButtonName, "info");
        if (created == null)
        {
            return;
        }

        created.gameObject.name = JobsButtonName;
        created.gameObject.SetActive(true);
        created.onClick.RemoveAllListeners();
        created.SetOnClick(() => ProcessJobUi.OpenJobsMenu());
        // No face on this chip; label is the fixed section title.
        try { if (created.icon != null) { created.icon.SetActive(false); } } catch (System.Exception __e) { Plugin.LogDebug("LayerDragGridPatches.cs silent catch: " + __e.Message); }
        SetLabel(created, NpcLabor.LaborText.T("proc.ui.jobsTitle"));
    }

    static UIButton? TryCreateButton(LayerDragGrid layer, string name, string fallbackBottomId)
    {
        // Prefer any visible utility control; stock/refuel often hidden on simple mills.
        UIButton? mold =
            FirstActive(layer.buttonStock, layer.buttonAlly, layer.buttonRefuel, layer.buttonAutoRefuel, layer.buttonDeliver)
            ?? layer.buttonStock
            ?? layer.buttonAlly
            ?? layer.buttonRefuel
            ?? layer.buttonAutoRefuel
            ?? layer.buttonDeliver
            ?? layer.buttonOwner as UIButton
            ?? FindAnyUiButton(layer);

        if (mold != null && mold.transform.parent != null)
        {
            // Force mold parent active so our clone is visible even if mold itself was hidden.
            Transform parent = mold.transform.parent;
            try
            {
                parent.gameObject.SetActive(true);
            }
            catch (System.Exception __e) { Plugin.LogDebug("LayerDragGridPatches.cs silent catch: " + __e.Message); }
GameObject go = Object.Instantiate(mold.gameObject, parent);
            go.name = name;
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
            catch (System.Exception __e) { Plugin.LogDebug("LayerDragGridPatches.cs silent catch: " + __e.Message); }
            try
            {
                if (btn.icon != null)
                {
                    btn.icon.SetActive(enable: false);
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("LayerDragGridPatches.cs silent catch: " + __e.Message); }
// Keep a compact fixed size so hidden molds don't leave a zero-size clone.
            try
            {
                RectTransform rt = btn.GetComponent<RectTransform>();
                if (rt != null)
                {
                    if (rt.rect.width < 8f || rt.rect.height < 8f)
                    {
                        rt.sizeDelta = new Vector2(88f, 28f);
                    }
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("LayerDragGridPatches.cs silent catch: " + __e.Message); }
LayoutElement? le = go.GetComponent<LayoutElement>();
            if (le == null)
            {
                le = go.AddComponent<LayoutElement>();
            }

            le.minWidth = 64f;
            le.preferredWidth = 88f;
            le.minHeight = 24f;
            le.preferredHeight = 28f;
            le.flexibleWidth = 0f;
            le.ignoreLayout = false;

            go.transform.SetAsLastSibling();
            try
            {
                parent.RebuildLayout(recursive: true);
            }
            catch (System.Exception __e) { Plugin.LogDebug("LayerDragGridPatches.cs silent catch: " + __e.Message); }
return btn;
        }

        // Fallback: window bottom button.
        try
        {
            Window? host = null;
            if (layer.windows != null && layer.windows.Count > 0)
            {
                host = layer.windows[0];
            }

            if (host != null)
            {
                UIButton btn = host.AddBottomButton(fallbackBottomId, () => { });
                if (btn != null)
                {
                    btn.gameObject.name = name;
                    return btn;
                }
            }
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("AddBottomButton mode failed: " + ex.Message);
        }

        return null;
    }

    static UIButton? FirstActive(params UIButton?[] buttons)
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            UIButton? b = buttons[i];
            if (b != null && b.gameObject != null && b.gameObject.activeInHierarchy)
            {
                return b;
            }
        }

        return null;
    }

    static UIButton? FindAnyUiButton(LayerDragGrid layer)
    {
        try
        {
            UIButton[] all = layer.GetComponentsInChildren<UIButton>(includeInactive: true);
            for (int i = 0; i < all.Length; i++)
            {
                UIButton b = all[i];
                if (b == null || b.gameObject.name == ButtonName || b.gameObject.name == JobsButtonName)
                {
                    continue;
                }

                // Skip grid item slots.
                if (b.GetComponent<ButtonGrid>() != null)
                {
                    continue;
                }

                return b;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("LayerDragGridPatches.cs silent catch: " + __e.Message); }
return null;
    }

    static void Wire(UIButton btn, LayerDragGrid layer, TraitCrafter crafter)
    {
        btn.onClick.RemoveAllListeners();
        btn.SetOnClick(() => OpenModeMenu(layer, crafter));
    }

    static void OpenModeMenu(LayerDragGrid layer, TraitCrafter crafter)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Refresh the per-machine mode so the picker reflects the last choice.
            try { ProcessorOutsourceMode.ModeUid = WorkbenchMemory.ReadProcessor(crafter?.owner); } catch { }

            int skillId = ProcessorWhitelist.ResolveSkillId(crafter);
            string skillName = GetSkillName(skillId);
            var options = new List<int> { MenuSelf, MenuAuto };

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

                if (c.ai is AI_NpcProcess)
                {
                    continue;
                }

                options.Add(c.uid);
            }

            long tList = sw.ElapsedMilliseconds;
            sw.Restart();

            LayerList menu = EClass.ui.AddLayer<LayerList>();
            PersonPickerUi.SetOpenPicker(menu);
            long tLayer = sw.ElapsedMilliseconds;
            sw.Restart();
            PersonPickerUi.BeginRowCost();            menu.SetList2(
                    options,
                    id => MenuLabel(id, skillId),
                    (id, item) =>
                    {
                        ApplyMode(id, crafter);
                        UIButton? btn = FindOurButton(layer);
                        if (btn != null)
                        {
                            RefreshLabel(btn, crafter);
                        }

                        SE.Click();
                    },
                    (id, item) =>
                    {
                        // SetList2 disables icon first; re-enable portrait via vanilla SetChara.
                        PersonPickerUi.StyleRow(
                            item,
                            id,
                            MenuLabel(id, skillId),
                            MenuSub(id, skillId, skillName),
                            menuSelf: MenuSelf,
                            menuAuto: MenuAuto);
                    })
                .SetSize(460f);

            long tRows = sw.ElapsedMilliseconds;
            sw.Restart();
            PersonPickerUi.LogRowCost("processor");
            SelectCurrentMode(menu, options);

            string title = NpcLabor.LaborTerms.Process;
            try
            {
                string machine = ProcessorWhitelist.DisplayName(crafter);
                if (!machine.IsEmpty())
                {
                    title = NpcLabor.LaborTerms.Process + "·" + machine;
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("LayerDragGridPatches.cs silent catch: " + __e.Message); }
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
                catch (System.Exception __e) { Plugin.LogDebug("LayerDragGridPatches.cs silent catch: " + __e.Message); }
}

            // No caching anywhere in this path: every open rebuilds the layer and
            // every row from scratch. Log the split so "it only stutters once" can be
            // told apart from "something specific is slow".
            Plugin.LogInfo(
                $"[pickertime] rows={options.Count} skill={skillId} list={tList}ms addLayer={tLayer}ms setList2={tRows}ms rest={sw.ElapsedMilliseconds}ms");
        }
        catch (System.Exception ex)
        {
            Plugin.LogWarn("processor mode menu failed: " + ex);
        }
    }

    /// <summary>Default-select the row matching the current mode (last chosen operator).</summary>
    static void SelectCurrentMode(LayerList menu, List<int> options)
    {
        if (menu == null || menu.list == null || options == null)
        {
            return;
        }

        int? target = null;
        int? mode = ProcessorOutsourceMode.ModeUid;
        if (mode == ProcessorOutsourceMode.AutoSentinel)
        {
            target = MenuAuto;
        }
        else if (!mode.HasValue)
        {
            target = MenuSelf;
        }
        else
        {
            target = mode;
        }

        if (!target.HasValue)
        {
            return;
        }

        int idx = options.IndexOf(target.Value);
        if (idx >= 0)
        {
            try { menu.list.Select(idx); } catch (System.Exception __e) { Plugin.LogDebug("LayerDragGridPatches.cs silent catch: " + __e.Message); }
}
    }

    static string MenuLabel(int id, int skillId)
    {
        if (id == MenuSelf)
        {
            return NpcLabor.LaborText.T("proc.mode.selfYou");
        }

        if (id == MenuAuto)
        {
            return NpcLabor.LaborText.T("proc.mode.auto");
        }

        Chara? c = RefChara.Get(id);
        if (c == null || PersonPickerUi.IsPcLike(c))
        {
            return c == null ? ("#" + id) : NpcLabor.LaborText.T("proc.mode.selfYou");
        }

        // A matching hobby/work prefix marks the "right" person at a glance:
        // 爱好·队 艾琳 when a hobby matches, 工作·队 艾琳 when only an assigned
        // work matches (the concrete hobby name is not shown).
        HobbyWorkKind kind = HobbyTag.GetRelevantKind(c, skillId);

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

    static string MenuSub(int id, int skillId, string skillName)
    {
        if (id == MenuSelf)
        {
            return NpcLabor.LaborText.T("proc.menu.selfProcess");
        }

        if (skillId <= 0)
        {
            return id == MenuAuto ? NpcLabor.LaborText.T("proc.menu.pickBest") : "";
        }

        string sn = skillName.IsEmpty() ? NpcLabor.LaborText.T("proc.menu.skillShort") : skillName;
        if (sn.Length > 4)
        {
            sn = sn.Substring(0, 4);
        }

        if (id == MenuAuto)
        {
            Chara? best = ProcessorOutsourceMode.FindBest(skillId);
            if (best == null)
            {
                return NpcLabor.LaborText.T("proc.menu.noOne");
            }

            return sn + " " + best.Evalue(skillId);
        }

        Chara? c = RefChara.Get(id);
        if (c == null)
        {
            return "";
        }

        int v = c.Evalue(skillId);
        return v > 0 ? sn + " " + v : "";
    }

    static void ApplyMode(int id, TraitCrafter crafter)
    {
        if (PersonPickerUi.SwallowRightClick("processor.applyMode"))
        {
            return;
        }

        if (id == MenuSelf)
        {
            ProcessorOutsourceMode.SetOff();
            Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.selfMode"));
            try { WorkbenchMemory.WriteProcessor(crafter?.owner, ProcessorOutsourceMode.ModeUid); } catch { }
            return;
        }

        if (id == MenuAuto)
        {
            ProcessorOutsourceMode.SetAuto();
            int skillId = ProcessorWhitelist.ResolveSkillId(crafter);
            Chara? best = ProcessorOutsourceMode.FindBest(skillId);
            if (best != null)
            {
                Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.autoSet", AssistantResolver.NameOf(best)));
            }
            else
            {
                Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.autoNone"));
            }

            try { WorkbenchMemory.WriteProcessor(crafter?.owner, ProcessorOutsourceMode.ModeUid); } catch { }
            return;
        }

        ProcessorOutsourceMode.SetPin(id);
        Chara? c = ProcessorOutsourceMode.GetPinned();
        if (c != null)
        {
            Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.pinned", AssistantResolver.NameOf(c)));
        }
        else
        {
            Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.pinFailed"));
        }
        try { WorkbenchMemory.WriteProcessor(crafter?.owner, ProcessorOutsourceMode.ModeUid); } catch { }
    }

    static void RefreshLabel(UIButton btn, TraitCrafter crafter)
    {
        int skillId = ProcessorWhitelist.ResolveSkillId(crafter);
        RefreshPortrait(btn, skillId);

        // With a face on the chip the 加工· prefix is redundant, and the shorter
        // label is what keeps the text clear of the portrait now that we no longer
        // reflow mainText. No portrait -> keep the original 加工·xxx label.
        string mode = ProcessorOutsourceMode.Describe(skillId);
        SetLabel(btn, PersonPickerUi.HasButtonPortrait(btn)
            ? mode
            : NpcLabor.LaborTerms.Process + "·" + mode);
    }

    /// <summary>
    /// Put the operator's face on the left of the chip: the pinned worker, the PC
    /// for 自己, nothing for 自动 (the auto pick changes per job, so a fixed face
    /// there would lie - the label already says 自动).
    /// </summary>
    static void RefreshPortrait(UIButton btn, int skillId)
    {
        Chara? portrait = null;

        try
        {
            if (ProcessorOutsourceMode.IsAuto)
            {
                portrait = null;
            }
            else if (ProcessorOutsourceMode.IsOff)
            {
                portrait = EClass.pc;
            }
            else
            {
                portrait = ProcessorOutsourceMode.GetPinned();
            }
        }
        catch (System.Exception ex)
        {
            Plugin.LogDebug("processor button portrait: " + ex.Message);
        }

        PersonPickerUi.StyleActionButton(btn, portrait);
    }

    /// <summary>
    /// Called when the drag-grid would normally start PC craft.
    /// Returns true if we consumed the start (NPC job or blocked).
    /// </summary>
    internal static bool TryInterceptStart(InvOwnerCraft inv)
    {
        if (inv == null || inv.crafter == null)
        {
            return false;
        }

        if (!ProcessorWhitelist.IsSupported(inv.crafter))
        {
            return false;
        }

        if (!ProcessorOutsourceMode.IsEnabled)
        {
            return false;
        }

        LayerDragGrid? layer = inv.dragGrid ?? LayerDragGrid.Instance;
        if (layer == null)
        {
            return false;
        }

        // Per-machine gate: only THIS machine being busy blocks its own start. Other
        // machines running their own jobs must not block this one (multi-machine).
        // Same single key as the core TryStart gate (:310) — one fact, one string.
        int thisMachineUid = 0;
        try { thisMachineUid = inv.crafter.owner != null ? inv.crafter.owner.uid : 0; } catch { thisMachineUid = 0; }
        if (thisMachineUid != 0 && ProcessorJobSession.IsMachineBusy(thisMachineUid))
        {
            Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.busy"));
            SE.BeepSmall();
            return true;
        }

        // Grids must be full — vanilla already checked, re-check safety.
        for (int i = 0; i < inv.numDragGrid; i++)
        {
            if (layer.buttons[i].Card == null)
            {
                return true;
            }
        }

        List<Thing> targets = layer.GetTargets();
        if (targets == null || targets.Count == 0)
        {
            SE.BeepSmall();
            return true;
        }

        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] == null || targets[i].isDestroyed || targets[i].Num <= 0)
            {
                Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.materialIncomplete"));
                SE.BeepSmall();
                return true;
            }
        }

        if (!inv.crafter.IsFuelEnough(1, targets))
        {
            Msg.Say("notEnoughFuel");
            SE.BeepSmall();
            return true;
        }

        int skillId = ProcessorWhitelist.ResolveSkillId(inv.crafter);
        Chara? worker = ProcessorOutsourceMode.ResolveWorker(skillId);
        if (worker == null)
        {
            Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.noWorker"));
            SE.BeepSmall();
            // Do not fall through to PC craft silently if user enabled NPC process.
            return true;
        }

        int max = targets[0].Num;
        for (int i = 1; i < targets.Count; i++)
        {
            if (targets[i].Num < max)
            {
                max = targets[i].Num;
            }
        }

        if (targets.Count >= 2 && targets[0] == targets[1])
        {
            max = targets[0].Num / 2;
        }

        if (max <= 0)
        {
            Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.notEnoughMaterial"));
            SE.BeepSmall();
            return true;
        }

        // Use full available stack count for the job.
        // Capture whether this worker came from auto-pick NOW: by the time the deferred
        // FinishStart runs, ModeUid may have changed, but the reason string must not.
        StartJob(layer, inv.crafter, targets, worker, max, ProcessorOutsourceMode.IsAuto);
        return true;
    }

    static void StartJob(
        LayerDragGrid layer,
        TraitCrafter crafter,
        List<Thing> targets,
        Chara worker,
        int count,
        bool autoPicked)
    {
        try
        {
            // Per-machine double-check (TryInterceptStart already gated, but a deferred
            // FinishStart can run a frame later): only THIS machine blocks its own start.
            int thisMachineUid = 0;
            try { thisMachineUid = crafter != null && crafter.owner != null ? crafter.owner.uid : 0; } catch { thisMachineUid = 0; }
            if (count <= 0
                || (thisMachineUid != 0 && ProcessorJobSession.IsMachineBusy(thisMachineUid))
                || _startPending)
            {
                return;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                Thing? t = targets[i];
                if (t == null || t.isDestroyed || t.Num < count)
                {
                    if (targets.Count >= 2 && i == 1 && targets[0] == targets[1] && t != null && t.Num >= count)
                    {
                        continue;
                    }

                    Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.changed"));
                    SE.BeepSmall();
                    return;
                }
            }

            if (targets.Count >= 2 && targets[0] == targets[1] && targets[0].Num < count * 2)
            {
                Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.changed"));
                SE.BeepSmall();
                return;
            }

            var claimed = new List<Thing>();
            for (int i = 0; i < targets.Count; i++)
            {
                Thing src = targets[i];
                Thing? piece = null;
                try
                {
                    piece = src.Split(count);
                }
                catch (System.Exception ex)
                {
                    Plugin.LogWarn("split ingredient failed: " + ex.Message);
                    Refund(claimed);
                    Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.cannotTake"));
                    return;
                }

                if (piece == null)
                {
                    Refund(claimed);
                    Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.cannotTake"));
                    return;
                }

                claimed.Add(piece);
            }

            // Park claimed pieces on the machine tile so bulk jobs do not overweight PC.
            // Do not Pick into PC inventory here.
            for (int i = 0; i < claimed.Count; i++)
            {
                Thing? piece = claimed[i];
                if (piece == null || piece.isDestroyed)
                {
                    continue;
                }

                if (!ProcessorJobSession.ParkIngredientOnMachine(piece, crafter))
                {
                    Refund(claimed);
                    Msg.SayRaw(NpcLabor.LaborText.T("proc.msg.cannotTake"));
                    SE.BeepSmall();
                    return;
                }
            }

            // Suppress RefreshCost/Redraw while OnProcess finishes and while we tear down UI.
            // Frame-stamp so ExpireStaleSuppress can bound the window.
            SuppressUiRefresh = true;
            _suppressFrame = Time.frameCount;
            _startPending = true;

            // Stop any PC craft AI that may already be spinning up.
            try
            {
                if (EClass.pc?.ai is AI_UseCrafter && EClass.pc.ai.IsRunning)
                {
                    EClass.pc.ai.Success();
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("LayerDragGridPatches.cs silent catch: " + __e.Message); }
// Clear grid slots without forcing put-back of claimed stacks (already split off).
            try
            {
                if (layer != null && (bool)layer && layer.buttons != null)
                {
                    for (int i = 0; i < layer.buttons.Count; i++)
                    {
                        ButtonGrid? b = layer.buttons[i];
                        if (b == null || !b.gameObject.activeSelf)
                        {
                            continue;
                        }

                        try
                        {
                            b.SetCardGrid(null);
                        }
                        catch (System.Exception __e) { Plugin.LogDebug("LayerDragGridPatches.cs silent catch: " + __e.Message); }
}
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("LayerDragGridPatches.cs silent catch: " + __e.Message); }
// Capture refs for deferred start/close so OnProcess RedrawButton can return safely first.
            TraitCrafter crafterRef = crafter!;
            Chara workerRef = worker;
            List<Thing> claimedRef = claimed;
            int countRef = count;
            LayerDragGrid? layerRef = layer;

            void FinishStart()
            {
                try
                {
                    // Re-park if anything got unparented while the UI closed.
                    for (int i = 0; i < claimedRef.Count; i++)
                    {
                        Thing? piece = claimedRef[i];
                        if (piece == null || piece.isDestroyed)
                        {
                            continue;
                        }

                        if (!piece.ExistsOnMap)
                        {
                            if (!ProcessorJobSession.ParkIngredientOnMachine(piece, crafterRef))
                            {
                                Refund(claimedRef);
                                SE.BeepSmall();
                                return;
                            }
                        }
                        else
                        {
                            ProcessorJobSession.ApplyParkFlags(piece);
                        }
                    }

                    bool ok = ProcessorJobSession.TryStart(workerRef, crafterRef, claimedRef, countRef, autoPicked);
                    if (!ok)
                    {
                        Refund(claimedRef);
                        SE.BeepSmall();
                    }
                    else
                    {
                        SE.Click();
                    }
                }
                catch (System.Exception ex)
                {
                    Plugin.LogWarn("deferred StartJob failed: " + ex);
                    Refund(claimedRef);
                }
                finally
                {
                    try
                    {
                        if (layerRef != null && (bool)layerRef)
                        {
                            layerRef.Close();
                        }
                    }
                    catch (System.Exception __e) { Plugin.LogDebug("LayerDragGridPatches.cs silent catch: " + __e.Message); }
                    // Single-delay window only: clear right after Close so the global
                    // suppress flag never leaks into a second frame / other layers.
                    // Late RefreshCost from the OnKill path is still guarded by the
                    // null checks in RefreshCostPrefix above.
                    ClearStartState();
                }
            }

            // Defer job start + UI close to next frame so current OnProcess/RedrawButton stack finishes.
            try
            {
                EClass.core.actionsNextFrame.Add(FinishStart);
            }
            catch
            {
                // Fallback if core queue unavailable.
                FinishStart();
            }
        }
        catch (System.Exception ex)
        {
            ClearStartState();
            Plugin.LogWarn("StartJob failed: " + ex);
        }
    }

    static void Refund(List<Thing> claimed)
    {
        if (claimed == null || EClass.pc == null)
        {
            return;
        }

        foreach (Thing t in claimed)
        {
            if (t == null || t.isDestroyed)
            {
                continue;
            }

            try
            {
                ProcessorJobSession.ReturnOneIngredient(t);
            }
            catch (System.Exception __e) { Plugin.LogDebug("LayerDragGridPatches.cs silent catch: " + __e.Message); }
        }
    }

    static UIButton? FindOurButton(LayerDragGrid layer)
        => FindButtonByName(layer, ButtonName);

    static UIButton? FindJobsButton(LayerDragGrid layer)
        => FindButtonByName(layer, JobsButtonName);

    static UIButton? FindButtonByName(LayerDragGrid layer, string name)
    {
        Transform? t = FindChildRecursive(layer.transform, name);
        if (t == null && layer.windows != null)
        {
            for (int i = 0; i < layer.windows.Count; i++)
            {
                Window? w = layer.windows[i];
                if (w == null)
                {
                    continue;
                }

                t = FindChildRecursive(w.transform, name);
                if (t != null)
                {
                    break;
                }
            }
        }

        return t != null ? t.GetComponent<UIButton>() : null;
    }

    static void SetLabel(UIButton btn, string label)
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
            catch (System.Exception __e) { Plugin.LogDebug("LayerDragGridPatches.cs silent catch: " + __e.Message); }
}
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
        catch (System.Exception __e) { Plugin.LogDebug("LayerDragGridPatches.cs silent catch: " + __e.Message); }
return "";
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

/// <summary>
/// When process mode is on (not self), divert full-grid auto-start from PC to NPC job.
/// </summary>
[HarmonyPatch(typeof(InvOwnerCraft), nameof(InvOwnerCraft.TryStartCraft))]
internal static class InvOwnerCraftOutsourcePatch
{
    [HarmonyPrefix]
    static bool Prefix(InvOwnerCraft __instance)
    {
        try
        {
            if (LayerDragGridPatches.TryInterceptStart(__instance))
            {
                return false;
            }
        }
        catch (System.Exception ex)
        {
            Plugin.LogWarn("TryStartCraft intercept failed: " + ex);
        }

        return true;
    }
}

/// <summary>
/// Guard RedrawButton during process intercept so RefreshCost is not hit on a half-closed layer.
/// </summary>
[HarmonyPatch(typeof(InvOwnerDraglet), nameof(InvOwnerDraglet.RedrawButton))]
internal static class InvOwnerDragletRedrawButtonPatch
{
    [HarmonyPrefix]
    static bool Prefix()
    {
        LayerDragGridPatches.ExpireStaleSuppress();
        return !LayerDragGridPatches.SuppressUiRefresh;
    }
}

