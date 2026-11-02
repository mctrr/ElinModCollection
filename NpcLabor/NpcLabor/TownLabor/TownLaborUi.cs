using System;
using System.Collections.Generic;
using NpcLabor.CoCraft;
using UnityEngine;

namespace NpcLabor.TownLabor;

/// <summary>
/// Town shop labor UI (plan A):
/// offer click → 1 自己做 / 2 交给同伴 / 取消
/// active → recall / close
/// </summary>
internal static class TownLaborUi
{
    const int ChoiceSelf = 1;
    const int ChoiceCompanion = 2;
    const int ChoiceCancel = 0;

    internal static void OpenFromOffer(QuestNpcLaborTownLaborOffer offer)
    {
        if (offer == null)
        {
            return;
        }

        try
        {
            if (offer.isActiveLabor || TownLaborManager.IsClientBusy(offer.clientUid))
            {
                OpenActiveMenu(offer);
                return;
            }

            OpenAcceptMenu(offer);
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("townlabor UI: " + ex.Message);
            try { SE.Beep(); } catch { }
        }
    }

    static void OpenActiveMenu(QuestNpcLaborTownLaborOffer offer)
    {
        TownLaborMission? m = offer.ActiveMission() ?? TownLaborManager.FindByClient(offer.clientUid);
        if (m == null)
        {
            Msg.Say(NpcLabor.LaborText.T("town.msg.endedShort", NpcLabor.LaborTerms.TownWork));
            SE.Beep();
            return;
        }

        bool pcSelf = false;
        try { pcSelf = m.isPcSelf; } catch { pcSelf = false; }

        var rows = new List<int> { 1, 0 }; // 1=recall/stop, 0=close
        LayerList menu = EClass.ui.AddLayer<LayerList>();
            PersonPickerUi.SetOpenPicker(menu);
        menu.SetList2(
                rows,
                id =>
                {
                    if (id != 1)
                    {
                        return LaborText.T("town.ui.close");
                    }

                    return pcSelf ? LaborText.T("town.ui.stop") : LaborText.T("town.ui.recall");
                },
                (id, item) =>
                {
                    if (id != 1)
                    {
                        SE.Click();
                        return;
                    }

                    bool ok = TownLaborManager.TryRecall(m.missionId);
                    if (!ok)
                    {
                        Msg.Say(pcSelf
                            ? NpcLabor.LaborText.T("town.msg.stopFail")
                            : NpcLabor.LaborText.T("town.msg.recallFail"));
                        SE.Beep();
                        return;
                    }

                    SE.Click();
                },
                (id, item) =>
                {
                    try { item.DisableIcon(); } catch { }
                    try
                    {
                        if (item.button1?.mainText != null)
                        {
                            string label = id == 1
                                ? (pcSelf ? LaborText.T("town.ui.stop") : LaborText.T("town.ui.recall"))
                                : LaborText.T("town.ui.close");
                            item.button1.mainText.SetText(
                                label,
                                id == 1 ? FontColor.Bad : FontColor.Default);
                        }
                    }
                    catch
                    {
                    }

                    try { item.Build(); } catch { }

                    if (id == 1)
                    {
                        string who = pcSelf
                            ? LaborText.T("town.you")
                            : (string.IsNullOrEmpty(m.workerName) ? NpcLabor.LaborText.T("town.msg.worker") : m.workerName);
                        ApplyRawSubText(item, who + "  " + m.ProgressLine(), 220);
                    }
                })
            .SetSize(480f);

        string header = NpcLabor.LaborText.T(
            "town.ui.header",
            NpcLabor.LaborText.T("town.ui.busyTag", NpcLabor.LaborTerms.TownWork),
            string.IsNullOrEmpty(m.jobTitle) ? NpcLabor.LaborText.T("town.msg.town") : m.jobTitle,
            string.IsNullOrEmpty(m.clientName) ? NpcLabor.LaborText.T("town.msg.client") : m.clientName);
        try { menu.SetHeader(header); }
        catch
        {
            try
            {
                if (menu.windows != null && menu.windows.Count > 0)
                {
                    menu.windows[0].SetCaption(header);
                }
            }
            catch
            {
            }
        }
    }

    /// <summary>Plan A accept: 1 self / 2 companion / cancel.</summary>
    static void OpenAcceptMenu(QuestNpcLaborTownLaborOffer offer)
    {
        TownLaborJobDef? def = offer.Def ?? TownLaborJobs.GetById(offer.jobId);
        if (def == null)
        {
            Msg.Say(NpcLabor.LaborText.T("town.msg.jobInvalid"));
            SE.Beep();
            return;
        }

        Chara? client = offer.Client
            ?? TownLaborManager.ResolveChara(offer.clientUid, offer.person);
        if (client == null || client.isDead)
        {
            Msg.Say(NpcLabor.LaborText.T("town.msg.clientGone"));
            SE.Beep();
            return;
        }

        // Keep person cache warm for later board refreshes / portrait.
        try
        {
            if (offer.person == null)
            {
                offer.person = new Person(client);
            }
            else
            {
                offer.person.SetChara(client);
            }
        }
        catch
        {
            try { offer.SetClient(client, assignQuest: false); } catch { }
        }

        int hours = Math.Max(1, offer.laborHours);
        var rows = new List<int> { ChoiceSelf, ChoiceCompanion, ChoiceCancel };
        LayerList menu = EClass.ui.AddLayer<LayerList>();
            PersonPickerUi.SetOpenPicker(menu);
        menu.SetList2(
                rows,
                id => id switch
                {
                    ChoiceSelf => LaborText.T("town.ui.self"),
                    ChoiceCompanion => LaborText.T("town.ui.companion"),
                    _ => LaborText.T("town.ui.cancel"),
                },
                (id, item) =>
                {
                    if (id == ChoiceCancel)
                    {
                        SE.Click();
                        return;
                    }

                    if (id == ChoiceSelf)
                    {
                        string? err = TownLaborManager.TryStartSelf(client, def, offer.laborHours);
                        if (err != null)
                        {
                            Msg.Say(err);
                            SE.Beep();
                            return;
                        }

                        SE.Click();
                        return;
                    }

                    // Companion path.
                    OpenPersonPicker(offer, def, client);
                },
                (id, item) =>
                {
                    try { item.DisableIcon(); } catch { }
                    try
                    {
                        if (item.button1?.mainText != null)
                        {
                            string label = id switch
                            {
                                ChoiceSelf => LaborText.T("town.ui.self"),
                                ChoiceCompanion => LaborText.T("town.ui.companion"),
                                _ => LaborText.T("town.ui.cancel"),
                            };
                            FontColor col = id == ChoiceCancel ? FontColor.Bad
                                : id == ChoiceSelf ? FontColor.Good
                                : FontColor.ButtonGeneral;
                            item.button1.mainText.SetText(label, col);
                        }
                    }
                    catch
                    {
                    }

                    try { item.Build(); } catch { }

                    if (id == ChoiceSelf)
                    {
                        // Pay no longer depends on skill, so no skill warning here:
                        // skill only gates the free shop-invest grant after success.
                        string sub = LaborText.T("town.ui.selfSub", hours);
                        ApplyRawSubText(item, sub, 240);
                    }
                    else if (id == ChoiceCompanion)
                    {
                        ApplyRawSubText(item, LaborText.T("town.ui.companionSub", hours), 240);
                    }
                })
            .SetSize(520f);

        string boardTitle = TownLaborJobs.ResolveBoardTitle(client, def);
        string header = boardTitle + " · " + (client.NameSimple ?? client.Name ?? NpcLabor.LaborText.T("town.msg.client"));
        try { menu.SetHeader(header); }
        catch
        {
            try
            {
                if (menu.windows != null && menu.windows.Count > 0)
                {
                    menu.windows[0].SetCaption(header);
                }
            }
            catch
            {
            }
        }
    }

    static void OpenPersonPicker(QuestNpcLaborTownLaborOffer offer, TownLaborJobDef def, Chara client)
    {
        List<Chara> candidates = TownLaborManager.ListCandidates(def);
        if (candidates.Count == 0)
        {
            Msg.Say(NpcLabor.LaborText.T("town.msg.noCandidates"));
            SE.Beep();
            return;
        }

        var rows = new List<int>();
        foreach (Chara c in candidates)
        {
            rows.Add(c.uid);
        }

        LayerList menu = EClass.ui.AddLayer<LayerList>();
            PersonPickerUi.SetOpenPicker(menu);
        menu.SetList2(
                rows,
                id =>
                {
                    Chara? c = TownLaborManager.ResolveChara(id);
                    if (c == null)
                    {
                        return "#" + id;
                    }

                    string n = c.NameSimple ?? c.Name ?? ("#" + id);
                    return PersonPickerUi.HobbyTaggedLabel(
                        c,
                        JobSkillIds(def),
                        NpcLabor.LaborText.T("town.ui.partyTag"),
                        n);
                },
                (id, item) =>
                {
                    Chara? worker = TownLaborManager.ResolveChara(id);
                    if (worker == null)
                    {
                        Msg.Say(NpcLabor.LaborText.T("town.msg.invalidChoice"));
                        SE.Beep();
                        return;
                    }

                    if (PersonPickerUi.SwallowRightClick("town.pickWorker"))
                    {
                        return;
                    }

                    ConfirmStart(offer, def, client, worker);
                },
                (id, item) =>
                {
                    Chara? c = TownLaborManager.ResolveChara(id);
                    string label;
                    string sub = "";
                    if (c == null)
                    {
                        label = "#" + id;
                    }
                    else
                    {
                        string n = c.NameSimple ?? c.Name ?? ("#" + id);
                        label = PersonPickerUi.HobbyTaggedLabel(
                            c,
                            JobSkillIds(def),
                            NpcLabor.LaborText.T("town.ui.partyTag"),
                            n);
                        int sk = 0;
                        try
                        {
                            if (def.SkillId > 0)
                            {
                                sk = c.Evalue(def.SkillId);
                            }
                        }
                        catch
                        {
                        }

                        sub = (string.IsNullOrEmpty(def.SkillLabel) ? NpcLabor.LaborText.T("town.ui.skillFallback") : def.SkillLabel) + " " + sk;
                        // Skill no longer scales pay; it only decides whether this
                        // worker can earn the shop a free invest level afterwards.
                        int shopInvestPick = Math.Max(0, client != null ? client.c_invest : 0);
                        if (sk <= shopInvestPick)
                        {
                            sub += " · " + LaborText.T("town.ui.noInvestWarn");
                        }
                    }

                    PersonPickerUi.StyleRow(item, id, label, sub);
                })
            .SetSize(520f);

        string boardTitle = TownLaborJobs.ResolveBoardTitle(client, def);
        string header = NpcLabor.LaborText.T(
            "town.ui.choosePick",
            boardTitle + " · " + (client.NameSimple ?? client.Name ?? NpcLabor.LaborText.T("town.msg.client")),
            Math.Max(1, offer.laborHours));
        try { menu.SetHeader(header); }
        catch
        {
            try
            {
                if (menu.windows != null && menu.windows.Count > 0)
                {
                    menu.windows[0].SetCaption(header);
                }
            }
            catch
            {
            }
        }
    }

    static void ConfirmStart(QuestNpcLaborTownLaborOffer offer, TownLaborJobDef def, Chara client, Chara worker)
    {
        string who = worker.NameSimple ?? worker.Name ?? ("#" + worker.uid);
        string job = TownLaborJobs.ResolveBoardTitle(client, def);
        var opts = new List<int> { 1, 0 };
        LayerList menu = EClass.ui.AddLayer<LayerList>();
            PersonPickerUi.SetOpenPicker(menu);
        menu.SetList2(
                opts,
                id => id == 1
                    ? NpcLabor.LaborText.T("town.ui.sendConfirm", who, job)
                    : NpcLabor.LaborText.T("common.cancel"),
                (id, item) =>
                {
                    if (id != 1)
                    {
                        SE.Click();
                        return;
                    }

                    string? err = TownLaborManager.TryStart(worker, client, def, offer.laborHours);
                    if (err != null)
                    {
                        Msg.Say(err);
                        SE.Beep();
                        return;
                    }

                    SE.Click();
                },
                (id, item) =>
                {
                    try { item.DisableIcon(); } catch { }
                    try
                    {
                        if (item.button1?.mainText != null)
                        {
                            item.button1.mainText.SetText(
                                id == 1
                                    ? NpcLabor.LaborText.T("town.ui.sendConfirm", who, job)
                                    : NpcLabor.LaborText.T("common.cancel"),
                                id == 1 ? FontColor.Good : FontColor.Bad);
                        }
                    }
                    catch
                    {
                    }

                    try { item.Build(); } catch { }

                    if (id == 1)
                    {
                        string sub = NpcLabor.LaborText.T("town.ui.hours", Math.Max(1, offer.laborHours));
                        ApplyRawSubText(item, sub, 260);
                    }
                })
            .SetSize(520f);

        try { menu.SetHeader(NpcLabor.LaborText.T("town.ui.confirmStart")); }
        catch
        {
        }
    }

    const int LeaveAbort = 1;
    const int LeaveSwap = 2;
    const int LeaveCancel = 0;

    /// <summary>
    /// PC self-work leave menu: 1 abort(fail) / 2 hand off to NPC / cancel.
    /// Does not auto-leave the map; player re-attempts leave after resolving self-work.
    /// </summary>
    internal static void OpenPcSelfLeaveMenu(Action? onAbort = null, Action? onSwapComplete = null, Action? onCancel = null)
    {
        var rows = new List<int> { LeaveAbort, LeaveSwap, LeaveCancel };
        LayerList menu = EClass.ui.AddLayer<LayerList>();
            PersonPickerUi.SetOpenPicker(menu);
        menu.SetList2(
                rows,
                id => id switch
                {
                    LeaveAbort => LaborText.T("town.leave.abort"),
                    LeaveSwap => LaborText.T("town.leave.swap"),
                    _ => LaborText.T("town.leave.cancel"),
                },
                (id, item) =>
                {
                    if (id == LeaveCancel)
                    {
                        SE.Click();
                        try { onCancel?.Invoke(); } catch { }
                        return;
                    }

                    if (id == LeaveAbort)
                    {
                        bool ok = TownLaborManager.AbortAllPcSelfLabor();
                        if (ok)
                        {
                            try
                            {
                                string job = LaborTerms.TownWork;
                                Msg.Say(LaborText.T("town.leave.abortOk", job));
                            }
                            catch { }
                            SE.Click();
                            try { onAbort?.Invoke(); } catch { }
                        }
                        else
                        {
                            Msg.Say(LaborText.T("town.msg.stopFail"));
                            SE.Beep();
                        }
                        return;
                    }

                    // Swap: pick companion for each active self mission (usually one).
                    OpenLeaveSwapPicker(onSwapComplete, onCancel);
                },
                (id, item) =>
                {
                    try { item.DisableIcon(); } catch { }
                    try
                    {
                        if (item.button1?.mainText != null)
                        {
                            string label = id switch
                            {
                                LeaveAbort => LaborText.T("town.leave.abort"),
                                LeaveSwap => LaborText.T("town.leave.swap"),
                                _ => LaborText.T("town.leave.cancel"),
                            };
                            FontColor col = id == LeaveAbort ? FontColor.Bad
                                : id == LeaveSwap ? FontColor.Good
                                : FontColor.Default;
                            item.button1.mainText.SetText(label, col);
                        }
                    }
                    catch { }
                    try { item.Build(); } catch { }
                })
            .SetSize(520f);

        try { menu.SetHeader(LaborText.T("town.leave.header")); }
        catch
        {
            try
            {
                if (menu.windows != null && menu.windows.Count > 0)
                {
                    menu.windows[0].SetCaption(LaborText.T("town.leave.header"));
                }
            }
            catch { }
        }
    }

    static void OpenLeaveSwapPicker(Action? onSwapComplete, Action? onCancel)
    {
        TownLaborMission? self = TownLaborManager.FindActivePcSelf();
        if (self == null)
        {
            Msg.Say(LaborText.T("town.msg.endedShort", LaborTerms.TownWork));
            SE.Beep();
            try { onCancel?.Invoke(); } catch { }
            return;
        }

        TownLaborJobDef? def = self.Def ?? TownLaborJobs.GetById(self.jobId);
        List<Chara> candidates = TownLaborManager.ListCandidates(def);
        // PC is busy as self-worker; ListCandidates already excludes busy, but double-skip PC.
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            try
            {
                if (candidates[i] != null && candidates[i].IsPC)
                {
                    candidates.RemoveAt(i);
                }
            }
            catch { }
        }

        if (candidates.Count == 0)
        {
            Msg.Say(LaborText.T("town.leave.noCandidates"));
            SE.Beep();
            return;
        }

        var rows = new List<int>();
        for (int i = 0; i < candidates.Count; i++)
        {
            rows.Add(candidates[i].uid);
        }

        LayerList menu = EClass.ui.AddLayer<LayerList>();
            PersonPickerUi.SetOpenPicker(menu);
        menu.SetList2(
                rows,
                id =>
                {
                    Chara? c = TownLaborManager.ResolveChara(id);
                    if (c == null) return "#" + id;
                    string n = c.NameSimple ?? c.Name ?? ("#" + id);
                    return PersonPickerUi.HobbyTaggedLabel(
                        c,
                        JobSkillIds(def),
                        LaborText.T("town.ui.partyTag"),
                        n);
                },
                (id, item) =>
                {
                    if (PersonPickerUi.SwallowRightClick("town.handOffPcSelf"))
                    {
                        return;
                    }

                    Chara? worker = TownLaborManager.ResolveChara(id);
                    if (worker == null)
                    {
                        Msg.Say(LaborText.T("town.msg.invalidChoice"));
                        SE.Beep();
                        return;
                    }

                    string? err = TownLaborManager.TryHandOffPcSelfTo(self.missionId, worker);
                    if (err != null)
                    {
                        Msg.Say(err);
                        SE.Beep();
                        return;
                    }

                    string who = worker.NameSimple ?? worker.Name ?? ("#" + worker.uid);
                    string job = string.IsNullOrEmpty(self.jobTitle) ? LaborTerms.TownWork : self.jobTitle;
                    Msg.Say(LaborText.T("town.leave.swapOk", who, job));
                    SE.Click();
                    try { onSwapComplete?.Invoke(); } catch { }
                },
                (id, item) =>
                {
                    Chara? c = TownLaborManager.ResolveChara(id);
                    string label;
                    string sub = "";
                    if (c == null)
                    {
                        label = "#" + id;
                    }
                    else
                    {
                        string n = c.NameSimple ?? c.Name ?? ("#" + id);
                        label = PersonPickerUi.HobbyTaggedLabel(
                            c,
                            JobSkillIds(def),
                            LaborText.T("town.ui.partyTag"),
                            n);
                        int sk = 0;
                        try
                        {
                            if (def != null && def.SkillId > 0)
                            {
                                sk = c.Evalue(def.SkillId);
                            }
                        }
                        catch { }
                        sub = (def == null || string.IsNullOrEmpty(def.SkillLabel)
                            ? LaborText.T("town.ui.skillFallback")
                            : def.SkillLabel) + " " + sk;
                    }

                    PersonPickerUi.StyleRow(item, id, label, sub);
                })
            .SetSize(520f);

        string header = LaborText.T("town.leave.swap");
        try { menu.SetHeader(header); }
        catch
        {
            try
            {
                if (menu.windows != null && menu.windows.Count > 0)
                {
                    menu.windows[0].SetCaption(header);
                }
            }
            catch { }
        }
    }

    /// <summary>Job skill ids for the hobby/work picker tag (one skill per job).</summary>
    static IReadOnlyCollection<int> JobSkillIds(TownLaborJobDef? def)
    {
        if (def != null && def.SkillId > 0)
        {
            return new[] { def.SkillId };
        }

        return Array.Empty<int>();
    }

    /// <summary>
    /// ItemGeneral.SetSubText runs lang.lang() and drops free-form Chinese.
    /// </summary>
    static void ApplyRawSubText(ItemGeneral item, string? sub, int mainWidth = 260)
    {
        if (item?.button1 == null || string.IsNullOrEmpty(sub))
        {
            return;
        }

        try
        {
            if (item.button1.subText == null)
            {
                return;
            }

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
            catch
            {
            }

            try
            {
                if (item.button1.mainText != null)
                {
                    RectTransform mrt = item.button1.mainText.rectTransform;
                    mrt.sizeDelta = new Vector2(
                        Mathf.Min(mrt.sizeDelta.x <= 0 ? mainWidth : mrt.sizeDelta.x, mainWidth),
                        mrt.sizeDelta.y);
                }
            }
            catch
            {
            }
        }
        catch
        {
        }
    }
}
