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

    /// <summary>立绘高 = 屏幕高 × 该比例（左下贴底竖立，对话框压在其上）。</summary>
    private const float HeightRatio = 0.85f;

    /// <summary>距屏幕左边距（像素）。</summary>
    private const float LeftMargin = 8f;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Portrait), nameof(Portrait.SetPortrait),
        new[] { typeof(string), typeof(Color), typeof(bool) })]
    internal static void SetPortraitPrefix(Portrait __instance)
    {
        try {
            __instance.enableFull = true;
            if (__instance.imageFull == null) {
                __instance.imageFull = CreateImage(__instance);
            }
        }
        catch (Exception ex) {
            AcsMod.Warn("[FullPortrait] prefix failed: " + ex.Message);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Portrait), nameof(Portrait.SetPortrait),
        new[] { typeof(string), typeof(Color), typeof(bool) })]
    internal static void SetPortraitPostfix(Portrait __instance)
    {
        try {
            Image? img = __instance.imageFull;
            if (img is null) {
                return;
            }

            Sprite? sp = img.sprite;
            bool ours = img.gameObject.name == GoName;
            if (sp is null) {
                // 没有 -full 图：原版会让 imageFull 失活；自建的也一并关掉，避免残留空框
                if (ours && img.gameObject.activeSelf) {
                    img.gameObject.SetActive(false);
                }

                return;
            }

            RectTransform rt = img.rectTransform;
            float h = Screen.height * HeightRatio;
            float w = h * (sp.rect.width / Mathf.Max(1f, sp.rect.height));
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(LeftMargin, 0f);
            rt.sizeDelta = new Vector2(w, h);
        }
        catch (Exception ex) {
            AcsMod.Warn("[FullPortrait] postfix failed: " + ex.Message);
        }
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
