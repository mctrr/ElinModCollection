using System;
using System.Collections.Generic;
using System.IO;
using ACS.API;
using ACS.Skin;
using ACS.Tiling;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ACS.Patches;

/// <summary>
/// LayerEditSkin：默认只列 Skin；底部开关再显示 Texture。
/// 按 mod 分组；PNG 头高度 &gt; 512 排除；坏图不建精灵，避免列表 NRE。
///
/// 图标/预览一律走**首帧图**（<see cref="PngTiler.TryCreateThumb"/>）：
/// 一个条目 = 一张 sprite sheet，而 UI 只要最左侧一帧（原尺寸，缩放交给 FitImage）。
/// 走 <c>data.GetSprite()</c> 会把整条 sheet（40960×512 那种）解码、建贴图、传 GPU，
/// 而列表是滚动驱动的 —— 逐行累积既是卡顿来源，也是显存膨胀来源。
/// 这条路径**完全不碰 data.sprites / data.tex**，与真实皮肤载入彻底解耦。
/// </summary>
[HarmonyPatch]
internal class SkinEditUiPatch
{
    internal static bool IncludeTexture;

    private const float IconMaxPx = 128f;
    private const float PreviewMaxPx = 256f;

    /// <summary>首帧图标缓存：PNG 全路径 → (精灵, 文件 mtime)。滚回去不复解码。</summary>
    private static readonly Dictionary<string, ThumbEntry> Thumbs = new(StringComparer.OrdinalIgnoreCase);

    private sealed class ThumbEntry
    {
        public Sprite? Sprite;
        public DateTime StampUtc;
    }

    /// <summary>素材换过（<c>ACS.ReloadAcs</c>）后整体失效；平时靠 mtime 自动失效。</summary>
    internal static void ClearThumbs()
    {
        Thumbs.Clear();
    }

    private static UIButton? _textureButton;
    private static bool _listedThisOpen;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(LayerEditSkin), nameof(LayerEditSkin.Activate))]
    internal static void OnActivatePrefix(Chara _chara)
    {
        IncludeTexture = false;
        _textureButton = null;

        // 故意**不调** SpriteReplacer.ListSkins()。
        // 原版 LayerEditSkin.RefreshList 的 onList 里本来就有一次
        // `SpriteReplacer.ListSkins().Values.ToList()`，而 RefreshList 被 Activate 直接调用 ——
        // 以前这里再抢跑一次，等于每次开换肤界面把「读一遍磁盘、重建整张 dictSkins」
        // 跑两遍（实测本机 Skin 7 个根 81 张 + Texture 161 个根 1557 张），
        // 而 Texture 开关此刻还是关的，第二遍纯属白扫。
        // 真正的触发时机由 onList → FillList 里的 _listedThisOpen 兜底。
        _listedThisOpen = false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(LayerEditSkin), nameof(LayerEditSkin.Activate))]
    internal static void OnActivatePostfix(LayerEditSkin __instance)
    {
        try {
            if (__instance.windows is not { Count: > 0 }) {
                return;
            }

            _textureButton = __instance.windows[0].AddBottomButton("bcs_textureSkins", () =>
            {
                IncludeTexture = !IncludeTexture;
                RefreshTextureButton();
                __instance.RefreshList();
            });
            RefreshTextureButton();
        }
        catch (Exception ex) {
            AcsMod.Warn($"SkinEditUi.Activate postfix: {ex.Message}");
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(LayerEditSkin), nameof(LayerEditSkin.RefreshList))]
    internal static bool RefreshListPrefix(LayerEditSkin __instance)
    {
        try {
            var list = __instance.list;
            var chara = __instance.chara;
            if (list is null || chara is null) {
                return true;
            }

            list.Clear();
            list.callbacks = new UIList.Callback<SpriteData, UIButton>
            {
                onClick = (a, b) =>
                {
                    list.Select(a);
                    SE.Click();
                    string id = SkinIndex.NormalizeId(Path.GetFileName(a.path));
                    SkinIndex.EnsureSkin(id);
                    chara.c_idSpriteReplacer = id;
                    chara._CreateRenderer();
                    __instance.RefreshImage(a);
                    SkinUi.RebindAfterSkinEdit(chara);
                },
                onRedraw = (a, b, i) =>
                {
                    b.mainText.SetText(FormatSkinLabel(a));
                    b.tooltip.lang = FormatSkinTooltip(a);
                    TrySetIcon(a, b);
                },
                onList = _ => FillList(list)
            };
            list.List();
            return false;
        }
        catch (Exception ex) {
            AcsMod.Warn($"SkinEditUi.RefreshList: {ex.Message}");
            return true;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(LayerEditSkin), nameof(LayerEditSkin.RefreshImage))]
    internal static void OnRefreshImage(LayerEditSkin __instance, SpriteData data)
    {
        try {
            var image = __instance.imageSkin;
            if (image is null) {
                return;
            }

            Sprite? sprite = GetUiSprite(data ?? __instance.currentData);
            if (sprite is null) {
                return;
            }

            image.sprite = sprite;
            FitImage(image, PreviewMaxPx);
        }
        catch (Exception) {
        }
    }

    private static void FillList(UIDynamicList list)
    {
        if (!_listedThisOpen) {
            SpriteReplacer.ListSkins();
            _listedThisOpen = true;
        }

        var groups = new SortedDictionary<string, List<SpriteData>>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var skins = SpriteReplacer.dictSkins;
        if (skins is not null) {
            foreach (var kv in skins) {
                var data = kv.Value?.data;
                if (data is null || data.path.IsEmpty()) {
                    continue;
                }

                if (TextureSkinCatalog.IsTexture(data)) {
                    continue;
                }

                if (!IsListable(data.path, kv.Key)) {
                    continue;
                }

                seen.Add(kv.Key);
                AddToGroup(groups, TextureSkinCatalog.LabelFromSkinPath(data.path), data);
            }
        }

        if (IncludeTexture) {
            foreach (var entry in TextureSkinCatalog.ListBaseItems()) {
                if (!seen.Add(entry.Id)) {
                    continue;
                }

                if (!IsListable(entry.Path, entry.Id)) {
                    continue;
                }

                var skin = TextureSkinCatalog.EnsureSkin(entry.Id);
                SpriteData? data = skin?.data;
                if (data is null || data.path.IsEmpty()) {
                    data = new SpriteData { path = entry.Path };
                }

                AddToGroup(groups, entry.Label, data);
            }
        }

        foreach (var kv in groups) {
            kv.Value.Sort((a, b) => string.Compare(
                Path.GetFileName(a.path),
                Path.GetFileName(b.path),
                StringComparison.OrdinalIgnoreCase));
            foreach (var data in kv.Value) {
                list.Add(data);
            }
        }
    }

    private static void AddToGroup(
        SortedDictionary<string, List<SpriteData>> groups,
        string label,
        SpriteData data)
    {
        if (label.IsEmpty()) {
            label = "Skin";
        }

        if (!groups.TryGetValue(label, out var list)) {
            list = new List<SpriteData>();
            groups[label] = list;
        }

        list.Add(data);
    }

    private static bool IsListable(string path, string id)
    {
        if (path.IsEmpty() || id.IsEmpty()) {
            return false;
        }

        if (id.IndexOf("_acs_", StringComparison.Ordinal) >= 0
            || id.IndexOf(AcsSuffixes.StaticCombat, StringComparison.Ordinal) >= 0) {
            return false;
        }

        string png = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? path
            : path + ".png";
        if (!File.Exists(png)) {
            return false;
        }

        if (!TextureSkinCatalog.TryGetPngHeight(png, out int height)
            || height > TextureSkinCatalog.MaxSpriteHeight) {
            return false;
        }

        return true;
    }

    private static void TrySetIcon(SpriteData data, UIButton button)
    {
        try {
            if (button.icon is null) {
                return;
            }

            Sprite? sprite = GetUiSprite(data);
            button.icon.sprite = sprite;
            button.icon.enabled = sprite is not null;
            if (sprite is null) {
                return;
            }

            FitImage(button.icon, IconMaxPx);
        }
        catch (Exception) {
            if (button.icon is not null) {
                button.icon.sprite = null;
                button.icon.enabled = false;
            }
        }
    }

    /// <summary>
    /// 列表/预览图标的唯一来源：该条目所在 sprite sheet 的**最左侧一帧**，原尺寸。
    ///
    /// 只读 PNG 本身 + 作者声明的帧数（`_acs_*` 看文件名尾段，底图看同目录 .ini 的 frame），
    /// **不碰 data.sprites / data.tex**：图标与真实皮肤载入互不影响，
    /// 滚一遍列表不会再顺手把 109 张 sheet 全载进显存。
    ///
    /// 取不到就返回 null（例如缺 .ini 的未切开长条、非 8 位/隔行 PNG）。
    /// Unity Image 在 sprite=null 时会画白块，所以交给调用方关掉 icon。
    /// </summary>
    private static Sprite? GetUiSprite(SpriteData? data)
    {
        if (data is null || data.path.IsEmpty()) {
            return null;
        }

        string png = data.path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? data.path
            : data.path + ".png";

        DateTime stamp = SafeStamp(png);
        if (Thumbs.TryGetValue(png, out var hit) && hit.StampUtc == stamp) {
            return hit.Sprite;
        }

        Sprite? sprite = PngTiler.TryCreateThumb(png, ResolveFrames(data), out string note);
        if (sprite is null && !note.IsEmpty()) {
            AcsMod.Log("thumb skip '" + png + "': " + note);
        }

        Thumbs[png] = new ThumbEntry { Sprite = sprite, StampUtc = stamp };
        return sprite;
    }

    private static DateTime SafeStamp(string png)
    {
        try {
            return File.GetLastWriteTimeUtc(png);
        }
        catch (Exception) {
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// 帧数只取**作者声明的元数据**，不看像素：
    /// `_acs_*` 片解析文件名尾段（`#417_0-38` → 39 帧），底图读同目录 `.ini` 的 `frame` 键。
    /// 都拿不到就按 1 帧 —— 此时若确实是长条，<see cref="PngTiler.TryCreateThumb"/> 的
    /// 「未切开横条」判据会挡掉它（缺 ini 该由作者补，不由 UI 糊一条色带充数）。
    /// </summary>
    private static int ResolveFrames(SpriteData data)
    {
        try {
            var clip = SkinListPatch.ResolveClipFromPath(data);
            if (clip is { Length: > 0 }) {
                return clip.Length;
            }
        }
        catch (Exception) {
        }

        try {
            string stem = data.path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? data.path.Substring(0, data.path.Length - 4)
                : data.path;
            int v = SkinListPatch.ReadIniInt(stem + ".ini", "frame");
            if (v > 0) {
                return v;
            }
        }
        catch (Exception) {
        }

        return 1;
    }

    private static void FitImage(Image image, float maxPx)
    {
        if (image is null || image.sprite is null) {
            return;
        }

        image.SetNativeSize();
        var rt = image.rectTransform;
        if (rt is null) {
            return;
        }

        Vector2 size = rt.sizeDelta;
        float w = size.x;
        float h = size.y;
        if (w <= maxPx && h <= maxPx) {
            return;
        }

        float scale = maxPx / Math.Max(w, h);
        rt.sizeDelta = new Vector2(w * scale, h * scale);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(LayerEditSkin), nameof(LayerEditSkin.OnClickClear))]
    internal static void OnClickClearPostfix(LayerEditSkin __instance)
    {
        SkinUi.RebindAfterSkinEdit(__instance.chara);
    }

    private static void RefreshTextureButton()
    {
        if (_textureButton is null) {
            return;
        }

        _textureButton.mainText.SetText(TextureToggleLabel());
        _textureButton.RebuildLayout();
    }

    private static string FormatSkinLabel(SpriteData data)
    {
        string name = SkinIndex.NormalizeId(Path.GetFileName(data.path));
        if (name.IsEmpty()) {
            name = Path.GetFileName(data.path);
        }

        string? source = TextureSkinCatalog.GetSourceLabel(data);
        if (source.IsEmpty()) {
            return name;
        }

        return "[" + source + "] " + name;
    }

    private static string FormatSkinTooltip(SpriteData data)
    {
        string? source = TextureSkinCatalog.GetSourceLabel(data);
        if (source.IsEmpty()) {
            return data.path;
        }

        return source + "\n" + data.path;
    }

    internal static string TextureToggleLabel()
    {
        string onOff = IncludeTexture ? "On" : "Off";
        return TextureToggleTitle() + " (" + onOff + ")";
    }

    internal static string TextureToggleTitle()
    {
        string code = Lang.langCode ?? "";
        if (code.IndexOf("CN", StringComparison.OrdinalIgnoreCase) >= 0
            || code.IndexOf("ZH", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "Texture 贴图";
        }

        if (code.IndexOf("JP", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "Texture";
        }

        return "Texture sprites";
    }
}
