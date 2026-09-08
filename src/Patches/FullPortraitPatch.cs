using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ACS.Patches;

/// <summary>
/// 对话全身立绘兜底。
///
/// 原版其实支持 mod 提供 <c>{id}-full.png</c> 全身立绘（<see cref="Portrait.imageFull"/>，
/// 有全身图时会 <c>gameObject.SetActive(!spriteFull)</c> 自动隐藏小头像框），但要生效需
/// 同时满足两个条件，实测都不一定成立：
/// (a) mod 的 <c>Portrait/</c> 目录里必须有同名普通头像 <c>{id}.png</c>——否则
///     <see cref="Portrait.SetChara"/> 走 no-portrait 分支（拿角色 sprite 当头像），
///     根本不会调用 SetPortrait(id)，也就永远查不到 -full；
/// (b) UI prefab 的 <c>enableFull</c> 为 true 且 <c>imageFull</c> 引用存在（Inspector 值，
///     对话 prefab 多半没配）。
///
/// 本补丁在运行时兜底：Prefix 打开 enableFull，缺 imageFull 时自建一个 Image
/// （挂在 Canvas 的直接子物体上并置为首个兄弟，避免被“有全身图就隐藏小头像框”的
/// SetActive 一起隐藏，且能随对话面板一起销毁/隐藏）；Postfix 按 sprite 比例设尺寸。
/// </summary>
[HarmonyPatch]
internal static class FullPortraitPatch
{
    private const string GoName = "BCS_FullPortrait";

    /// <summary>
    /// 总开关（控制台 <c>ACS.FullPortrait on|off</c>）。关掉后不改任何原版行为，
    /// 用于验证“原版本身是否已能把 -full 立绘显示出来”。
    /// </summary>
    internal static bool Enabled = true;

    // 布局参数（运行时可用控制台 ACS.PortraitLayout 调整，见 TuningCommands）

    /// <summary>立绘高 = 屏幕高 × 该比例。</summary>
    internal static float HeightRatio = 0.85f;

    /// <summary>水平偏移（像素）：锚在左时 = 距左边距；锚在右时 = 距右边距。</summary>
    internal static float OffsetX = 8f;

    /// <summary>垂直偏移（像素）：距底边（负 = 沉到屏幕外，正 = 抬起）。</summary>
    internal static float OffsetY = 0f;

    /// <summary>true = 锚在父级右下角（右上角方向），false = 锚在左下角（默认）。</summary>
    internal static bool AnchorRight;

    internal static void ResetLayout()
    {
        HeightRatio = 0.85f;
        OffsetX = 8f;
        OffsetY = 0f;
        AnchorRight = false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Portrait), nameof(Portrait.SetPortrait),
        new[] { typeof(string), typeof(Color), typeof(bool) })]
    internal static void SetPortraitPrefix(Portrait __instance, string id)
    {
        try {
            if (!Enabled) {
                // 关掉：完全不改原版行为（已自建的立绘也隐藏）
                Image? off = __instance.imageFull;
                if (off is not null && off.gameObject.name == GoName && off.gameObject.activeSelf) {
                    off.gameObject.SetActive(false);
                }

                return;
            }

            // 只在对话面板（DialogDrama）里启用全身立绘：角色面板 / 队伍列表 /
            // 造人界面等也在用 Portrait，无差别开会把它们全变成大立绘。
            if (!InDialogContext(__instance)) {
                Image? old = __instance.imageFull;
                if (old is not null && old.gameObject.name == GoName && old.gameObject.activeSelf) {
                    old.gameObject.SetActive(false);
                }

                return;
            }

            __instance.enableFull = true;
            bool created = false;
            if (__instance.imageFull == null) {
                __instance.imageFull = CreateImage(__instance);
                created = true;
            }

            AcsMod.Log($"[FullPortrait] SetPortrait id={id}"
                + (created ? " (imageFull 自建)" : " (沿用 prefab imageFull)"));
        }
        catch (Exception ex) {
            AcsMod.Warn("[FullPortrait] prefix failed: " + ex.Message);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Portrait), nameof(Portrait.SetPortrait),
        new[] { typeof(string), typeof(Color), typeof(bool) })]
    internal static void SetPortraitPostfix(Portrait __instance, string id)
    {
        try {
            Image? img = __instance.imageFull;
            if (img is null) {
                return;
            }

            // 动态立绘：若是横条动画，切成帧并把当前 sprite 换成首帧（尺寸按单帧算）
            PortraitAnimator.Register(img, id);

            Sprite? sp = img.sprite;
            AcsMod.Log("[FullPortrait] " + id + "-full: " + (sp is null ? "未找到（仍用小头像）" : $"命中 {sp.rect.width}x{sp.rect.height}"));
            bool ours = img.gameObject.name == GoName;
            if (sp is null) {
                // 没有 -full 图：原版会让 imageFull 失活；自建的也一并关掉，避免残留空框
                if (ours && img.gameObject.activeSelf) {
                    img.gameObject.SetActive(false);
                }

                return;
            }

            // 布局：锚点取父级左下或右下角（父 = Canvas 直接子物体，即对话面板根，
            // 通常铺满屏幕 → 等价于屏幕左下/右下角），pivot 与锚点同角，
            // 偏移 (OffsetX, OffsetY) 按像素，高度 = 屏高 × HeightRatio，宽按 sprite 比例。
            RectTransform rt = img.rectTransform;
            float h = Screen.height * HeightRatio;
            float w = h * (sp.rect.width / Mathf.Max(1f, sp.rect.height));
            float ax = AnchorRight ? 1f : 0f;
            rt.anchorMin = new Vector2(ax, 0f);
            rt.anchorMax = new Vector2(ax, 0f);
            rt.pivot = new Vector2(ax, 0f);
            rt.anchoredPosition = new Vector2(AnchorRight ? -OffsetX : OffsetX, OffsetY);
            rt.sizeDelta = new Vector2(w, h);

            // 原版在显示全身立绘时会把小头像框整个隐藏（SetActive(!spriteFull)），
            // 对话框左上会空一块。这里重新激活小头像框，小头像 + 全身立绘共存。
            if (!__instance.gameObject.activeSelf) {
                __instance.gameObject.SetActive(true);
            }
        }
        catch (Exception ex) {
            AcsMod.Warn("[FullPortrait] postfix failed: " + ex.Message);
        }
    }

    /// <summary>
    /// 该头像是否属于对话面板：向上找 DialogDrama 组件，或祖先名含 drama/dialog。
    /// 组件判定为主（准确），名称判定兜底（DialogDrama 未必是 MonoBehaviour 祖先）。
    /// </summary>
    private static bool InDialogContext(Portrait portrait)
    {
        Transform? t = portrait.transform;
        while (t is not null) {
            if (t.GetComponent<DialogDrama>() is not null) {
                return true;
            }

            string n = t.name ?? "";
            if (n.IndexOf("drama", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("dialog", StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }

            t = t.parent;
        }

        return false;
    }

    /// <summary>
    /// 自建立绘 Image。宿主取“Canvas 的直接子物体”（一般就是对话面板根）：
    /// 立在 Portrait 之外才不会被隐藏小头像的 SetActive 波及，同时随面板一起隐藏。
    /// </summary>
    private static Image CreateImage(Portrait portrait)
    {
        Transform host = portrait.transform;
        Transform? t = portrait.transform;
        while (t is not null && t.parent is not null) {
            if (t.parent.GetComponent<Canvas>() is not null) {
                host = t;
                break;
            }

            t = t.parent;
        }

        var go = new GameObject(GoName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(host, false);
        go.transform.SetAsFirstSibling(); // 立绘在最底层，对话框文本盖其上

        var img = go.GetComponent<Image>();
        img.preserveAspect = true;
        img.raycastTarget = false;
        return img;
    }
}
