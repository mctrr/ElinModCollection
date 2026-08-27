using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ACS.Patches;

/// <summary>
/// LayerEditSkin：默认只列 Skin；底部开关再显示 Texture。
/// 按 mod 分组；PNG 头高度 &gt; 512 排除；坏图不 GetSprite，避免列表 NRE。
/// ACS 横条用切好的第一帧做图标/预览，避免整条超宽图被跳过后天空白块。
/// </summary>
[HarmonyPatch]
internal class SkinEditUiPatch
{
    internal static bool IncludeTexture;

    private const float IconMaxPx = 128f;
    private const float PreviewMaxPx = 256f;

    private static UIButton? _textureButton;
    private static bool _listedThisOpen;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(LayerEditSkin), nameof(LayerEditSkin.Activate))]
    internal static void OnActivatePrefix(Chara _chara)
    {
        IncludeTexture = false;
        _textureButton = null;
        _listedThisOpen = false;
        try {
            SpriteReplacer.ListSkins();
            _listedThisOpen = true;
        }
        catch (Exception ex) {
            AcsMod.Warn($"SkinEditUi.Activate prefix: {ex.Message}");
        }
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
                    string id = PcSkinPatch.NormalizeSkinId(Path.GetFileName(a.path));
                    TextureSkinCatalog.EnsureSkin(id);
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
            || id.IndexOf(PcSkinPatch.StaticCombatSuffix, StringComparison.Ordinal) >= 0) {
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
    /// 列表/预览用第一帧。ACS 横条先按文件名切开，避免整条超宽图被当成单帧。
    /// Unity Image 在 sprite=null 时会画白块，所以取不到图就不要留空 sprite。
    /// </summary>
    private static Sprite? GetUiSprite(SpriteData? data)
    {
        if (data is null || data.path.IsEmpty()) {
            return null;
        }

        Sprite? sprite = LoadFirstFrame(data);
        if (sprite is not null && !LooksLikeUncutStrip(sprite)) {
            return sprite;
        }

        string id = PcSkinPatch.NormalizeSkinId(Path.GetFileName(data.path));
        if (id.IsEmpty()
            || SpriteReplacer.dictSkins is null
            || !SpriteReplacer.dictSkins.TryGetValue(id, out var skin)
            || skin?.suffixes is null) {
            return sprite;
        }

        if (skin.suffixes.TryGetValue("_acs_idle", out var idle)
            && idle is not null
            && !ReferenceEquals(idle, data)) {
            Sprite? idleSprite = LoadFirstFrame(idle);
            if (idleSprite is not null) {
                return idleSprite;
            }
        }

        if (skin.data is not null && !ReferenceEquals(skin.data, data)) {
            Sprite? baseSprite = LoadFirstFrame(skin.data);
            if (baseSprite is not null && !LooksLikeUncutStrip(baseSprite)) {
                return baseSprite;
            }
        }

        return sprite;
    }

    private static Sprite? LoadFirstFrame(SpriteData data)
    {
        if (data is null || data.path.IsEmpty()) {
            return null;
        }

        try {
            if (data.frame < 1) {
                data.frame = 1;
            }

            SkinListPatch.EnsureAcsSprites(data);

            Sprite? sprite = data.sprites is { Length: > 0 } ? data.sprites[0] : null;
            if (sprite is not null && sprite.texture is not null) {
                return sprite;
            }

            data.Init();
            if (data.frame < 1) {
                data.frame = 1;
            }

            SkinListPatch.EnsureAcsSprites(data);
            sprite = data.sprites is { Length: > 0 } ? data.sprites[0] : data.GetSprite();
            if (sprite is not null && sprite.texture is not null) {
                return sprite;
            }
        }
        catch (Exception) {
        }

        return null;
    }

    private static bool LooksLikeUncutStrip(Sprite? sprite)
    {
        if (sprite is null || sprite.texture is null) {
            return false;
        }

        return sprite.rect.width > sprite.rect.height * 1.5f;
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
        string name = PcSkinPatch.NormalizeSkinId(Path.GetFileName(data.path));
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
