using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ACS.Patches;

/// <summary>
/// 诊断：把对话面板（<see cref="DialogDrama"/>）的真实层级与关键 Image 的颜色 dump 出来。
///
/// <b>为什么需要</b>：对话框 body 的底纹 / 底板在 prefab 里，代码里拿不到名字 ——
/// <c>DialogDrama</c> 只暴露 <c>textName</c> / <c>textMain</c> / <c>transChoices</c> 等字段，
/// 而「底板」是靠 <c>GetComponentInParent</c> 上溯才能碰到的普通 Image。
/// 不 dump 就只能靠猜名字改 <c>color.a</c>，猜错就是白改一轮。
///
/// 只在 <see cref="AcsMod.DiagnosticLog"/> 打开时工作，且同一个 DialogDrama 只 dump 一次。
/// </summary>
internal static class DialogProbe
{
    private static readonly HashSet<int> Dumped = new();

    /// <summary>dump 一个对话面板的层级树 + 每个节点上 Image/Text 的关键信息。</summary>
    internal static void Dump(DialogDrama dialog, string reason)
    {
        if (!AcsMod.DiagnosticLog || dialog is null) {
            return;
        }

        int key = dialog.GetInstanceID();
        if (!Dumped.Add(key)) {
            return;
        }

        try {
            var sb = new StringBuilder();
            sb.Append("[DialogProbe] ").Append(reason)
              .Append(" name=").Append(dialog.name)
              .Append(" inter=").Append(Screen.width).Append('x').Append(Screen.height)
              .AppendLine();

            // 从 DialogDrama 自己往上找到 LayerDrama 根，再从根往下整棵树 dump：
            // 底板很可能挂在 DialogDrama 的父级或兄弟节点上，只 dump 自己会漏掉。
            Transform root = dialog.transform;
            while (root.parent is not null && root.parent.GetComponent<Canvas>() is null) {
                root = root.parent;
            }

            sb.AppendLine("[DialogProbe] --- tree from " + root.name + " ---");
            Walk(root, 0, sb);
            DumpFields(dialog, sb);

            AcsMod.Log(sb.ToString());
        }
        catch (Exception ex) {
            AcsMod.Warn("[DialogProbe] dump failed: " + ex + "\n" + ex.StackTrace);
        }
    }

    /// <summary>递归打印层级。深度限制 6 层，避免把整个 LayerDrama 刷屏。</summary>
    private static void Walk(Transform t, int depth, StringBuilder sb)
    {
        if (t is null || depth > 6) {
            return;
        }

        string pad = new string(' ', depth * 2);
        sb.Append(pad).Append(t.name);

        // 是否是关键节点（DialogDrama / 有 Image / 有 Text）
        var dialog = t.GetComponent<DialogDrama>();
        if (dialog is not null) {
            sb.Append("  <DialogDrama>");
        }

        var img = t.GetComponent<Image>();
        if (img is not null) {
            Color c = img.color;
            sb.Append("  [Image a=").Append(c.a.ToString("0.###"))
              .Append(" rgb=").Append(c.r.ToString("0.##")).Append(',')
              .Append(c.g.ToString("0.##")).Append(',')
              .Append(c.b.ToString("0.##"))
              .Append(" sprite=").Append(img.sprite is null ? "null" : img.sprite.name)
              .Append(" enabled=").Append(img.enabled)
              .Append(']');
        }

        var txt = t.GetComponent<Text>();
        if (txt is not null) {
            sb.Append("  [Text \"").Append(Shorten(txt.text, 24)).Append("\"]");
        }

        var uitext = t.GetComponent<UIText>();
        if (uitext is not null) {
            sb.Append("  [UIText]");
        }

        var rt = t as RectTransform;
        if (rt is not null) {
            sb.Append("  rt=").Append(rt.rect.width.ToString("0")).Append('x').Append(rt.rect.height.ToString("0"))
              .Append(" anchor=").Append(rt.anchorMin.ToString("0.##")).Append('-').Append(rt.anchorMax.ToString("0.##"))
              .Append(" pos=").Append(rt.anchoredPosition.ToString("0.#"))
              .Append(" active=").Append(t.gameObject.activeSelf);
        }

        sb.AppendLine();

        for (int i = 0; i < t.childCount; i++) {
            Walk(t.GetChild(i), depth + 1, sb);
        }
    }

    /// <summary>
    /// 把 <see cref="DialogDrama"/> 的公开字段各自**指向哪个节点**打出来。
    /// 5 个 prefab 变体（DialogDefault / DialogAdv / DialogNerun / DialogMono / DialogMono2）
    /// 的节点命名完全不同，只有这行能确认「我搬的是不是玩家屏幕上看见的那个东西」。
    /// </summary>
    private static void DumpFields(DialogDrama dialog, StringBuilder sb)
    {
        sb.AppendLine("[DialogProbe] --- field binding ---");
        At(sb, "textName", dialog.textName != null ? dialog.textName.transform : null, dialog);
        At(sb, "textMain", dialog.textMain != null ? dialog.textMain.transform : null, dialog);
        At(sb, "transChoices", dialog.transChoices, dialog);
        At(sb, "goAffinity", dialog.goAffinity != null ? dialog.goAffinity.transform : null, dialog);
        At(sb, "iconNext", dialog.iconNext != null ? dialog.iconNext.transform : null, dialog);
        At(sb, "portrait", dialog.portrait != null ? dialog.portrait.transform : null, dialog);
        At(sb, "imageBgAdv", dialog.imageBgAdv != null ? dialog.imageBgAdv.transform : null, dialog);
        At(sb, "portrait.imageFull",
            dialog.portrait != null && dialog.portrait.imageFull != null
                ? dialog.portrait.imageFull.transform
                : null,
            dialog);
    }

    private static void At(StringBuilder sb, string label, Transform? t, DialogDrama dialog)
    {
        sb.Append("[DialogProbe]   ").Append(label).Append(" = ");

        if (t == null) {
            sb.AppendLine("(null)");
            return;
        }

        sb.Append(PathOf(t, dialog.transform));

        var sr = t.GetComponentInParent<ScrollRect>();
        if (sr != null) {
            sb.Append("  scrollRect=").Append(PathOf(sr.transform, dialog.transform));
        }

        if (t.GetComponentInParent<ContentSizeFitter>() != null) {
            sb.Append("  +fitter");
        }

        sb.Append("  children=").Append(t.childCount);
        sb.AppendLine();
    }

    private static string PathOf(Transform t, Transform root)
    {
        var sb = new StringBuilder(t.name);
        Transform? p = t.parent;
        int guard = 0;
        while (p != null && p != root && guard++ < 12) {
            sb.Insert(0, p.name + "/");
            p = p.parent;
        }

        return sb.ToString();
    }

    private static string Shorten(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) {
            return "";
        }

        s = s.Replace('\n', ' ');
        return s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
