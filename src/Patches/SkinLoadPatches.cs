using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ACS.API;
using HarmonyLib;
using UnityEngine;

namespace ACS.Patches;

/// <summary>
/// Skin 扫描与后缀加载：ListSkins 合并 ACS 文件、Reload 解析 _acs_ 后缀。
/// </summary>
[HarmonyPatch]
internal class SkinListPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(SpriteReplacer), nameof(SpriteReplacer.ListSkins))]
    internal static void OnListSkins(Dictionary<string, SpriteReplacer> __result)
    {
        var staticCombatMerges = new List<StaticCombatMerge>();

        var staticCombatSkinIds = __result.Keys
            .Where(id => id.EndsWith(PcSkinPatch.StaticCombatSuffix, StringComparison.Ordinal))
            .ToArray();
        foreach (var staticCombatSkinId in staticCombatSkinIds) {
            if (TryMergeStaticCombatSkinIntoBase(staticCombatSkinId, __result) is { } merged) {
                staticCombatMerges.Add(merged);
            }
        }

        var acsSkinIds = __result.Keys
            .Where(id => id.Contains("_acs_"))
            .ToArray();
        foreach (var acsSkinId in acsSkinIds) {
            MergeAcsSkinIntoBase(acsSkinId, __result);
        }

        foreach (var entry in __result) {
            NormalizeAcsSuffixes(entry.Value, entry.Key);
        }

        // After native *_bcs_combat: optional Skin/bcs_static_map.ini [combat] fill-ins.
        foreach (var mapped in StaticCombatMapApply.ApplyToDictSkins(__result)) {
            staticCombatMerges.Add(new StaticCombatMerge(mapped.BaseId, mapped.Data));
        }

        // _bcs_combat 仅写入 dictSkins[baseId]；NPC 的 sourceCard.replacer 由 Elin 单独 Reload
        // 创建，并非同一实例，所以合并不会自动覆盖。
        // 把结果广播给当前已加载且匹配的 NPC 运行时实例。
        if (staticCombatMerges.Count > 0) {
            BroadcastStaticCombatToCharaInstances(staticCombatMerges);
        }

        // ListSkins 完成后立刻给已在场的 PC 重绑
        try {
            PcSkinBinder.Ensure("ListSkins", forceListSkins: false);
        }
        catch (Exception) {
            // 主菜单等尚未有 pc
        }
    }

    /// <summary>
    /// 单次 _bcs_combat 合并结果，供 ListSkins 末尾广播给已加载 NPC 实例。
    /// </summary>
    private readonly struct StaticCombatMerge
    {
        public string BaseId { get; }
        public SpriteData Data { get; }
        public StaticCombatMerge(string baseId, SpriteData data)
        {
            BaseId = baseId;
            Data = data;
        }
    }

    /// <summary>
    /// 把静态战斗 skin（如 pcr_jinghua_bcs_combat）合并到底 base skin（pcr_jinghua）的 suffixes。
    /// 合并成功时返回 (基名, 战斗数据)，用于 ListSkins 末尾广播到已加载 NPC 运行时实例。
    /// </summary>
    private static StaticCombatMerge? TryMergeStaticCombatSkinIntoBase(
        string combatSkinId,
        Dictionary<string, SpriteReplacer> dictSkins)
    {
        int suffixIdx = combatSkinId.IndexOf(PcSkinPatch.StaticCombatSuffix, StringComparison.Ordinal);
        if (suffixIdx <= 0) {
            return null;
        }

        string baseId = combatSkinId.Substring(0, suffixIdx);
        if (!dictSkins.TryGetValue(baseId, out var baseSkin)
            || !dictSkins.TryGetValue(combatSkinId, out var combatSkin)) {
            return null;
        }

        var combatData = combatSkin.suffixes.TryGetValue("");
        if (combatData is null) {
            return null;
        }

        baseSkin.suffixes[PcSkinPatch.StaticCombatSuffix] = combatData;
        dictSkins.Remove(combatSkinId);

        AcsMod.Log($"skin '{baseId}' static combat image (from {combatSkinId})");
        return new StaticCombatMerge(baseId, combatData);
    }

    /// <summary>
    /// 把 ListSkins 合并得到的 _bcs_combat 复制到当前地图上已加载 NPC 的运行时 replacer。
    /// 这些 replacer 与 dictSkins[baseId] 通常不是同一实例（Elin 内部 Reload 各自重建），
    /// 所以合并不会自动覆盖。匹配走 c_idSpriteReplacer 经 NormalizeSkinId 后的基名。
    /// PC 跳过：PC 的 spriteReplacer 已被 TryApplyPcSkin 直接绑定到 dictSkins[baseId]。
    /// </summary>
    private static void BroadcastStaticCombatToCharaInstances(List<StaticCombatMerge> merges)
    {
        try {
            var map = EClass._map;
            if (map?.charas is null) {
                return;
            }

            int updated = 0;
            foreach (var chara in map.charas) {
                if (chara is null || chara.IsPC) {
                    continue;
                }

                if (chara.c_idSpriteReplacer.IsEmpty()) {
                    continue;
                }

                string normalized = PcSkinPatch.NormalizeSkinId(chara.c_idSpriteReplacer);
                if (normalized.IsEmpty()) {
                    continue;
                }

                foreach (var merge in merges) {
                    if (normalized != merge.BaseId) {
                        continue;
                    }

                    var replacer = chara.spriteReplacer ?? chara.sourceCard?.replacer;
                    if (replacer?.suffixes is null) {
                        break;
                    }

                    // 仅当缺位时注入：后续 ListSkins 不覆盖已写入的战斗数据
                    if (!replacer.suffixes.TryGetValue(PcSkinPatch.StaticCombatSuffix, out var existing)
                        || existing is null) {
                        replacer.suffixes[PcSkinPatch.StaticCombatSuffix] = merge.Data;
                        updated++;
                    }
                    break;
                }
            }

            if (updated > 0) {
                AcsMod.Log($"broadcast static combat to {updated} runtime chara instance(s)");
            }
        }
        catch (Exception ex) {
            AcsMod.Warn($"BroadcastStaticCombatToCharaInstances: {ex.Message}");
        }
    }

    private static void MergeAcsSkinIntoBase(string acsSkinId, Dictionary<string, SpriteReplacer> dictSkins)
    {
        int acsIdx = acsSkinId.IndexOf("_acs_");
        if (acsIdx < 0) {
            return;
        }

        string baseId = acsSkinId.Substring(0, acsIdx);
        string acsPart = acsSkinId.Substring(acsIdx);

        var clip = AcsClip.CreateFromFormat(acsPart);
        if (clip is not { Length: > 0 }) {
            return;
        }

        if (!dictSkins.TryGetValue(acsSkinId, out var acsSkin)) {
            return;
        }

        var acsData = acsSkin.suffixes.TryGetValue("");
        if (acsData is null) {
            return;
        }

        // 文件名优先；覆盖错误/过期 ini，避免整条图当 1 帧导致巨大
        ApplyClipMeta(acsData, clip);

        if (!dictSkins.TryGetValue(baseId, out var baseSkin)) {
            // 无底图时用 ACS skin 充当 base，保证 LayerEditSkin 列表可显示
            baseSkin = acsSkin;
            baseSkin.data = acsData;
            dictSkins[baseId] = baseSkin;
            dictSkins.Remove(acsSkinId);
        }

        string suffixKey = $"_acs_{clip.Name}";
        baseSkin.suffixes[suffixKey] = acsData;
        baseSkin.suffixes[AcsController.ReservedSuffix] = null;

        dictSkins.Remove(acsSkinId);

        AcsMod.Log($"skin '{baseId}' clip '{clip.Name}' with {clip.Length} frames @ {clip.Interval}ms (from {acsSkinId})");
    }

    /// <summary>
    /// 长格式 _acs_idle#41_0-31 → 短 key _acs_idle + ApplyClipMeta。
    /// 短 key 解析失败绝不能 Remove，否则只剩 ReservedSuffix、画面仍是 PCC。
    /// 短 key 仍可从 data.path 文件名恢复 #interval_begin-end（Reload 后常见）。
    /// </summary>
    internal static void NormalizeAcsSuffixes(SpriteReplacer replacer, string logId)
    {
        if (replacer?.suffixes is null || replacer.suffixes.Count == 0) {
            return;
        }

        var acsSuffixes = replacer.suffixes
            .Where(kv => kv.Key != null && kv.Key.StartsWith("_acs_"))
            .ToArray();
        if (acsSuffixes.Length == 0) {
            return;
        }

        foreach (var entry in acsSuffixes) {
            var suffix = entry.Key;
            var data = entry.Value;
            var clip = ResolveClip(suffix, data);
            if (clip is not { Length: > 0 }) {
                // 短 key 且 path 也解析不出：保留条目，勿 Remove
                continue;
            }

            if (data is not null) {
                ApplyClipMeta(data, clip);
            }

            string shortKey = $"_acs_{clip.Name}";
            replacer.suffixes[shortKey] = data;
            replacer.suffixes[AcsController.ReservedSuffix] = null;

            if (suffix != shortKey) {
                replacer.suffixes.Remove(suffix);
            }

            AcsMod.Log($"skin '{logId}' clip '{clip.Name}' with {clip.Length} frames @ {clip.Interval}ms");
        }
    }

    /// <summary>
    /// 优先后缀键；短键（_acs_move）则从 path 文件名取 _acs_move#41_0-14。
    /// </summary>
    internal static AcsClip? ResolveClip(string suffix, SpriteData? data)
    {
        var clip = AcsClip.CreateFromFormat(suffix);
        if (clip is { Length: > 0 }) {
            return clip;
        }

        return ResolveClipFromPath(data);
    }

    /// <summary>
    /// 仅从 path 文件名解析 ACS clip（显示热路径 / LoadAnimationIni 后校正用）。
    /// </summary>
    internal static AcsClip? ResolveClipFromPath(SpriteData? data)
    {
        if (data is null || data.path.IsEmpty()) {
            return null;
        }

        try {
            string file = Path.GetFileNameWithoutExtension(data.path);
            int acsIdx = file.IndexOf("_acs_", StringComparison.Ordinal);
            if (acsIdx < 0) {
                return null;
            }

            return AcsClip.CreateFromFormat(file.Substring(acsIdx));
        }
        catch (Exception) {
            return null;
        }
    }

    /// <summary>
    /// 文件名解析的帧数优先于 .ini。
    /// 错误 frame（常见 1）会把整条横图当单帧 → move 1920 宽整条显示，远大于 idle 128 格。
    ///
    /// 关键：禁止 data.GetSprites() 做强制重切。
    /// GetSprites → Load → LoadAnimationIni 会先读 ini 再 LoadSprites；
    /// 若 ini 缺失/解析失败会把 frame 打成 1，随后 sprites 按整条 strip 创建，
    /// 即使之后再改 data.frame，已有 sprites 也不会自动重切。
    /// 正确做法：写好 frame 后直接 LoadSprites()。
    /// </summary>
    internal static void ApplyClipMeta(SpriteData data, AcsClip clip)
    {
        if (data is null || clip is null || clip.Length <= 0) {
            return;
        }

        data.frame = clip.Length;
        data.time = clip.Interval / 1000f;
        data.scale = 100;

        try {
            if (!data.path.IsEmpty()) {
                File.WriteAllLines($"{data.path}.ini", [
                    $"frame={data.frame}",
                    $"time={data.time}",
                    $"scale={data.scale}",
                ]);
            }
        }
        catch (Exception) {
            // 只读路径等：内存 frame 仍正确
        }

        ForceReloadSprites(data);
    }

    /// <summary>
    /// 按当前 data.frame 直接重切，不经过 LoadAnimationIni。
    /// </summary>
    internal static void ForceReloadSprites(SpriteData data)
    {
        if (data is null) {
            return;
        }

        try {
            data.sprites = null;
            data.LoadSprites();
            // 功能3：ACS 纹理用 Point，避免 Bilinear 软边（不改 scale/位置）
            ApplyPointFilter(data);
        }
        catch (Exception) {
            // path 未就绪等：留给显示路径再试
        }
    }

    /// <summary>
    /// 像素风：ACS/静态 skin 纹理强制 Point。
    /// 与 PCC 部件 SpriteVariation 的 Point 路径对齐；不改尺寸与 pivot。
    /// </summary>
    internal static void ApplyPointFilter(SpriteData data)
    {
        if (data is null) {
            return;
        }

        try {
            if (data.tex is not null && data.tex.filterMode != FilterMode.Point) {
                data.tex.filterMode = FilterMode.Point;
            }

            var sprites = data.sprites;
            if (sprites is null) {
                return;
            }

            for (int i = 0; i < sprites.Length; i++) {
                var s = sprites[i];
                if (s is null || s.texture is null) {
                    continue;
                }

                if (s.texture.filterMode != FilterMode.Point) {
                    s.texture.filterMode = FilterMode.Point;
                }
            }
        }
        catch (Exception) {
        }
    }

    /// <summary>
    /// ?????????? frame/scale????????????????
    /// ???frame ??? sprites ???? strip???????
    /// ???????? meta ???????????? path / ????
    /// </summary>
    internal static void EnsureAcsSprites(SpriteData data)
    {
        if (data is null || data.path.IsEmpty()) {
            return;
        }

        // ?????????? meta ????????? Point ???????????
        if (data.frame > 0
            && data.scale == 100
            && data.time > 0f
            && data.sprites is { Length: > 0 } sprites
            && sprites.Length == data.frame
            && sprites[0] is not null) {
            // ??????????????? strip?????
            bool looksLikeUncutStrip = data.frame == 1
                && sprites[0].texture is not null
                && sprites[0].rect.width > sprites[0].texture.height * 1.5f;
            if (!looksLikeUncutStrip) {
                if (data.tex is not null && data.tex.filterMode != FilterMode.Point) {
                    ApplyPointFilter(data);
                }
                return;
            }
        }

        var clip = ResolveClipFromPath(data);
        if (clip is not { Length: > 0 }) {
            // ??????????? Point??? Bilinear ??
            ApplyPointFilter(data);
            return;
        }

        bool metaDirty = data.frame != clip.Length
            || data.scale != 100
            || data.time <= 0f;

        if (metaDirty) {
            data.frame = clip.Length;
            data.time = clip.Interval / 1000f;
            data.scale = 100;
        }

        bool needSlice = data.sprites is null
            || data.sprites.Length != data.frame
            || data.sprites.Length == 0
            || data.sprites[0] is null;

        // ?????????????????frame ?? 1 ???????
        if (!needSlice && data.sprites is { Length: 1 } && data.sprites[0] is not null) {
            try {
                var s0 = data.sprites[0];
                if (s0.texture is not null
                    && s0.rect.width > s0.texture.height * 1.5f
                    && clip.Length > 1) {
                    needSlice = true;
                }
            }
            catch (Exception) {
            }
        }

        if (metaDirty || needSlice) {
            ForceReloadSprites(data);
        }
        else {
            ApplyPointFilter(data);
        }
    }


}

/// <summary>
/// 原版 LoadAnimationIni 无文件时 frame=1；有文件也可能被错误 ini 覆盖。
/// ACS 路径在读完 ini 后立刻用文件名帧数盖回去，避免后续 LoadSprites 切成整条。
/// </summary>
[HarmonyPatch(typeof(SpriteData), nameof(SpriteData.LoadAnimationIni))]
internal static class SpriteDataLoadAnimationIniPatch
{
    [HarmonyPostfix]
    internal static void Postfix(SpriteData __instance)
    {
        if (__instance is null || __instance.path.IsEmpty()) {
            return;
        }

        // 快速过滤：path 不含 _acs_ 则不是 ACS 横条
        if (__instance.path.IndexOf("_acs_", StringComparison.Ordinal) < 0) {
            return;
        }

        var clip = SkinListPatch.ResolveClipFromPath(__instance);
        if (clip is not { Length: > 0 }) {
            return;
        }

        if (__instance.frame != clip.Length) {
            __instance.frame = clip.Length;
        }

        __instance.time = clip.Interval / 1000f;
        if (__instance.scale != 100) {
            __instance.scale = 100;
        }
    }
}

/// <summary>
/// Beta：Reload(string id, RenderData) 后补齐 ACS 后缀；无底图种族也可从 dictModItems 建后缀。
/// </summary>
[HarmonyPatch]
internal class ReloadSuffixPatch
{
    private static readonly FieldInfo SortedModIdsField =
        AccessTools.Field(typeof(SpriteReplacer), "sortedModIds");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SpriteReplacer), nameof(SpriteReplacer.Reload))]
    internal static void OnReloadSuffixes(SpriteReplacer __instance, string id, RenderData renderData)
    {
        // 无底图 PCC 种族仍可能在 dictModItems 有 _acs_ 贴图
        if (__instance.suffixes.Count == 0) {
            var dictModItems = SpriteReplacer.dictModItems;
            if (dictModItems is null || id.IsEmpty()) {
                // 即使无底图也尝试合并 _bcs_combat
                TryMergeBcsCombatOnReload(__instance, id);
                return;
            }

            bool hasAcsTexture = dictModItems.Keys.Any(k =>
                k != null && k.StartsWith(id) && k.Contains("_acs_"));
            if (hasAcsTexture) {
                try {
                    __instance.SortModItemIds();
                    var sorted = SortedModIdsField?.GetValue(null) as List<string>;
                    if (sorted is null) {
                        sorted = dictModItems.Keys.OrderBy(k => k).ToList();
                    }
                    __instance.BuildSuffixData(id, dictModItems, sorted);
                }
                catch (Exception ex) {
                    AcsMod.Warn($"BuildSuffixData fallback failed for '{id}': {ex.Message}");
                }
            }
        }

        if (__instance.suffixes.Count == 0) {
            TryMergeBcsCombatOnReload(__instance, id);
            return;
        }

        SkinListPatch.NormalizeAcsSuffixes(__instance, id);
        TryInjectStaticCombatFromDictSkins(__instance, id);
        TryMergeBcsCombatOnReload(__instance, id);
    }

    private static void TryMergeBcsCombatOnReload(SpriteReplacer replacer, string id)
    {
        try {
            if (replacer?.suffixes is null || id.IsEmpty()) {
                return;
            }

            // ??????
            if (replacer.suffixes.TryGetValue(PcSkinPatch.StaticCombatSuffix, out var existing)
                && existing is not null) {
                return;
            }

            // ListSkins ?? *_bcs_combat ??? base ? Remove ?? key?
            // ????? dictSkins[id] ???? key ????????
            SpriteData? combatData = null;
            string? source = null;

            if (SpriteReplacer.dictSkins.TryGetValue(id, out var baseSkin)
                && !ReferenceEquals(baseSkin, replacer)
                && baseSkin.suffixes is not null
                && baseSkin.suffixes.TryGetValue(PcSkinPatch.StaticCombatSuffix, out combatData)
                && combatData is not null) {
                source = id;
            }

            if (combatData is null) {
                string combatSkinId = id + PcSkinPatch.StaticCombatSuffix;
                if (SpriteReplacer.dictSkins.TryGetValue(combatSkinId, out var combatSkin)) {
                    combatData = combatSkin.data;
                    if (combatData is null
                        && combatSkin.suffixes is not null
                        && combatSkin.suffixes.TryGetValue("", out var raw)
                        && raw is not null) {
                        combatData = raw;
                    }

                    if (combatData is not null) {
                        source = combatSkinId;
                    }
                }
            }

            if (combatData is null || source is null) {
                return;
            }

            replacer.suffixes[PcSkinPatch.StaticCombatSuffix] = combatData;
            AcsMod.Log($"skin '{id}' static combat merged on Reload (from {source})");
        }
        catch (Exception ex) {
            AcsMod.Warn($"TryMergeBcsCombatOnReload: {ex.Message}");
        }
    }


    /// <summary>
    /// 从 <c>SpriteReplacer.dictSkins[id]</c> 取 _bcs_combat 注入当前 Reload 出的实例。
    /// 跳过：当前实例已是 dictSkins[id]（PC 等）；已存在 _bcs_combat；dictSkins 中没合并。
    /// </summary>
    private static void TryInjectStaticCombatFromDictSkins(SpriteReplacer replacer, string id)
    {
        try {
            if (replacer?.suffixes is null || id.IsEmpty()) {
                return;
            }

            if (replacer.suffixes.TryGetValue(PcSkinPatch.StaticCombatSuffix, out var existing)
                && existing is not null) {
                return;
            }

            if (SpriteReplacer.dictSkins.TryGetValue(id) is not { } canonical) {
                return;
            }

            if (ReferenceEquals(canonical, replacer)) {
                return;
            }

            if (canonical.suffixes is null
                || !canonical.suffixes.TryGetValue(PcSkinPatch.StaticCombatSuffix, out var combatData)
                || combatData is null) {
                return;
            }

            replacer.suffixes[PcSkinPatch.StaticCombatSuffix] = combatData;
        }
        catch (Exception ex) {
            AcsMod.Warn($"TryInjectStaticCombatFromDictSkins: {ex.Message}");
        }
    }
}
