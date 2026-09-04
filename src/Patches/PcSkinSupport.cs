using System;
using System.IO;
using ACS.API;
using HarmonyLib;
using UnityEngine;

namespace ACS.Patches;

/// <summary>
/// 主角 skin：绑定、冷启动重试、控制台命令、居民菜单入口。
/// </summary>
[HarmonyPatch]
internal class PcSkinPatch
{
    internal const string StaticCombatSuffix = "_bcs_combat";

    private static int _lastListSkinsFrame = -999;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(CharaRenderer), nameof(CharaRenderer.SetOwner))]
    internal static void OnSetOwnerPrefix(CharaRenderer __instance, Card c, out string? __state)
    {
        __state = null;
        if (c is not Chara chara || chara.c_idSpriteReplacer.IsEmpty()) {
            return;
        }

        string id = NormalizeSkinId(chara.c_idSpriteReplacer);
        if (!id.IsEmpty() && id != chara.c_idSpriteReplacer) {
            chara.c_idSpriteReplacer = id;
        }

        var skin = TextureSkinCatalog.EnsureSkin(chara.c_idSpriteReplacer) ?? ResolveSkin(chara.c_idSpriteReplacer);
        PrepareSkinSprite(skin);

        // ACS override keeps race RenderData and only swaps the sprite.
        // Vanilla LayerEditSkin would switch to chara_custom_128/256 here and sink.
        if (__instance.pccData is null) {
            __state = chara.c_idSpriteReplacer;
            chara.c_idSpriteReplacer = null;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CharaRenderer), nameof(CharaRenderer.SetOwner))]
    internal static void OnSetOwner(CharaRenderer __instance, Card c, string? __state)
    {
        RestoreSpriteReplacerId(c, __state);
        TryApplyPcSkin(__instance);
        BindNpcCustomSkin(__instance);
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(CharaRenderer), nameof(CharaRenderer.SetOwner))]
    internal static Exception? OnSetOwnerFinalizer(CharaRenderer __instance, Card c, string? __state, Exception? __exception)
    {
        RestoreSpriteReplacerId(c, __state);
        TryApplyPcSkin(__instance);
        BindNpcCustomSkin(__instance);
        return __exception;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CharaRenderer), nameof(CharaRenderer.OnEnterScreen))]
    internal static void OnEnterScreen(CharaRenderer __instance)
    {
        TryApplyPcSkin(__instance);
        BindNpcCustomSkin(__instance);
        if (__instance is not null && __instance.pccData is null && __instance.isSynced) {
            EnsureNpcActor(__instance);
        }
    }

    /// <summary>
    /// Vanilla LayerEditSkin / ClearSkin only swap the renderer object.
    /// The old CardActor stays in scene.syncList, which leaves a ghost sprite
    /// on the map. Kill it before the new renderer is created.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Chara), nameof(Chara._CreateRenderer))]
    internal static void OnCreateRendererPrefix(Chara __instance)
    {
        var old = __instance.renderer;
        if (old is null) {
            return;
        }

        try {
            if (old.hasActor) {
                old.KillActor();
            }

            old.OnLeaveScreen();
            EClass.scene?.syncList?.Remove(old);
        }
        catch (Exception ex) {
            AcsMod.Warn($"OnCreateRendererPrefix: {ex.Message}");
        }
    }

    /// <summary>
    /// LayerEditSkin / ACS.SetSkin 调用的是 Chara 重写，不会走到 Card 基类实现。
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara._CreateRenderer))]
    internal static void OnCharaCreateRenderer(Chara __instance, CardRenderer __result)
    {
        if (__result is CharaRenderer cr) {
            TryApplyPcSkin(cr);
            BindNpcCustomSkin(cr);
        }
        else if (__instance.renderer is CharaRenderer cr2) {
            TryApplyPcSkin(cr2);
            BindNpcCustomSkin(cr2);
        }
    }

    /// <summary>
    /// Vanilla atlas transmute/boost（变身/boost 魔法）：原版条件设置 RendererReplacer
    /// 而不建 PCC → 此时叠 ACS 会生成 CharaActorPCC with null pccData → NullReference。
    /// 检测到这种渲染态就不碰皮肤绑定。
    /// </summary>
    internal static bool IsVanillaAtlasRender(CharaRenderer? renderer)
        => renderer is not null && renderer.replacer is not null && renderer.pccData is null;

    internal static bool IsVanillaAtlasRender(Chara? chara)
        => chara?.renderer is CharaRenderer cr && IsVanillaAtlasRender(cr);

    internal static void TryApplyPcSkin(CharaRenderer renderer, bool forceListSkins = false)
    {
        if (renderer is null || renderer.pccData is null || IsVanillaAtlasRender(renderer)) {
            return;
        }

        var owner = renderer.owner;
        if (owner is null || owner.mimicry != null) {
            return;
        }

        if (owner.c_idSpriteReplacer.IsEmpty()) {
            return;
        }

        string rawId = owner.c_idSpriteReplacer;
        string baseId = NormalizeSkinId(rawId);
        if (baseId.IsEmpty()) {
            return;
        }
        if (baseId != rawId) {
            owner.c_idSpriteReplacer = baseId;
        }

        var skin = ResolveSkin(baseId);
        if (skin is null || !HasUsableSkin(skin)) {
            bool dictEmpty = SpriteReplacer.dictSkins is null || SpriteReplacer.dictSkins.Count == 0;
            int fc = Time.frameCount;
            if (forceListSkins || (dictEmpty && fc - _lastListSkinsFrame >= 15)) {
                _lastListSkinsFrame = fc;
                try {
                    SpriteReplacer.ListSkins();
                }
                catch (Exception) {
                    // dict 未就绪时稍后再试
                }
            }
            skin = ResolveSkin(baseId);
        }
        if (skin is null || !HasUsableSkin(skin)) {
            return;
        }

        if (ReferenceEquals(owner.spriteReplacer, skin) && HasUsableSkin(skin)) {
            return;
        }

        // 只绑 spriteReplacer，保留 pccData / PCC actor；ACS 或静态图由 PccRenderPatch 叠
        owner.spriteReplacer = skin;
    }

    /// <summary>
    /// Non-PCC custom skins keep the race RenderData offset, same as ACS override.
    /// Only bind spriteReplacer; do not switch to chara_custom_128/256.
    /// </summary>
    internal static void BindNpcCustomSkin(CharaRenderer renderer)
    {
        if (renderer is null || renderer.pccData is not null) {
            return;
        }

        // 注意：不能在这里用 IsVanillaAtlasRender 拦截——非 PCC 的 NPC 一律 pccData==null
        // 且 replacer 常非 null，那样会把所有 NPC 换肤都当成“atlas 变身”跳过（曾踩过、
        // 已回滚）。atlas 变身防护只在 TryApplyPcSkin（PC 侧）保留。

        var owner = renderer.owner;
        if (owner is null || owner.mimicry != null || owner.c_idSpriteReplacer.IsEmpty()) {
            return;
        }

        string id = NormalizeSkinId(owner.c_idSpriteReplacer);
        if (id.IsEmpty()) {
            return;
        }

        if (id != owner.c_idSpriteReplacer) {
            owner.c_idSpriteReplacer = id;
        }

        var skin = ResolveSkin(id);
        if (skin is null || !HasUsableSkin(skin)) {
            return;
        }

        owner.spriteReplacer = skin;
        var race = owner.sourceCard?.renderData ?? owner.source?.renderData;
        if (race is null) {
            return;
        }

        renderer.data = race;
        renderer.usePass = false;
        if (renderer.isSynced) {
            EnsureNpcActor(renderer);
        }
    }

    /// <summary>
    /// Rebuild the live renderer. Prefix on _CreateRenderer kills the old actor.
    /// </summary>
    internal static void RecreateRenderer(Chara chara)
    {
        chara?._CreateRenderer();
    }

    /// <summary>
    /// Tile-pass NPCs have no CardActor. After a custom skin we turn usePass off,
    /// so an already-on-screen renderer must spawn an actor or the sprite never animates.
    /// </summary>
    private static void EnsureNpcActor(CharaRenderer renderer)
    {
        if (renderer is null || renderer.hasActor || renderer.usePass) {
            return;
        }

        var owner = renderer.owner;
        if (owner is null) {
            return;
        }

        try {
            CardActor? actor = renderer.data?.CreateActor();
            if (actor is null) {
                string actorId = "CharaActor";
                var ids = owner.sourceCard?.idActor;
                if (ids is { Length: > 0 } && !ids[0].IsEmpty()) {
                    actorId = ids[0];
                }

                actor = PoolManager.Spawn(
                    Resources.Load<CardActor>("Scene/Render/Actor/" + actorId));
            }

            if (actor is null) {
                return;
            }

            renderer.actor = actor;
            renderer.hasActor = true;
            actor.SetOwner(owner);
        }
        catch (Exception ex) {
            AcsMod.Warn($"EnsureNpcActor: {ex.Message}");
        }
    }

    private static void RestoreSpriteReplacerId(Card c, string? id)
    {
        if (id.IsEmpty() || c is not Chara chara) {
            return;
        }

        chara.c_idSpriteReplacer = id;
    }

    private static void PrepareSkinSprite(SpriteReplacer? skin)
    {
        if (skin?.data is null || skin.data.path.IsEmpty()) {
            return;
        }

        try {
            if (skin.data.frame < 1) {
                skin.data.frame = 1;
            }

            SkinListPatch.EnsureAcsSprites(skin.data);
            if (skin.data.sprites is { Length: > 0 } && skin.data.sprites[0] is not null) {
                return;
            }

            _ = skin.data.GetSprite();
        }
        catch (Exception) {
        }
    }

    /// <summary>
    /// pcr_xxx_acs_idle#41_0-31 → pcr_xxx；普通 id 原样返回。
    /// </summary>
    internal static string NormalizeSkinId(string id)
    {
        if (id.IsEmpty()) {
            return id;
        }

        try {
            if (id.IndexOfAny(new[] { '/', '\\' }) >= 0) {
                id = Path.GetFileName(id);
            }
        }
        catch (Exception) {
            // ignore
        }

        if (id.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || id.EndsWith(".PNG", StringComparison.Ordinal)) {
            id = Path.GetFileNameWithoutExtension(id);
        }

        int hash = id.IndexOf('#');
        if (hash >= 0) {
            id = id.Substring(0, hash);
        }

        int acsIdx = id.IndexOf("_acs_");
        if (acsIdx > 0) {
            id = id.Substring(0, acsIdx);
        }

        int staticCombatIdx = id.IndexOf(StaticCombatSuffix, StringComparison.Ordinal);
        if (staticCombatIdx > 0) {
            id = id.Substring(0, staticCombatIdx);
        }

        return id;
    }

    private static SpriteReplacer? ResolveSkin(string id)
    {
        if (id.IsEmpty()) {
            return null;
        }

        var skin = SpriteReplacer.dictSkins.TryGetValue(id);
        if (skin is not null) {
            return skin;
        }

        string normalized = NormalizeSkinId(id);
        if (normalized != id && !normalized.IsEmpty()) {
            skin = SpriteReplacer.dictSkins.TryGetValue(normalized);
            if (skin is not null) {
                return skin;
            }
        }

        return TextureSkinCatalog.EnsureSkin(normalized.IsEmpty() ? id : normalized);
    }

    /// <summary>
    /// 动态 ACS（真实 _acs_* clip）或静态底图（data / suffixes[""]）均可。
    /// </summary>
    internal static bool HasUsableSkin(SpriteReplacer skin)
    {
        if (skin is null) {
            return false;
        }

        if (AcsSuffixes.HasRealClip(skin)) {
            return true;
        }

        if (skin.data is not null && !skin.data.path.IsEmpty()) {
            return true;
        }

        if (skin.suffixes is not null
            && skin.suffixes.TryGetValue("", out var baseData)
            && baseData is not null
            && !baseData.path.IsEmpty()) {
            return true;
        }

        return false;
    }

    /// <summary>兼容旧调用；等价于 <see cref="AcsSuffixes.HasRealClip"/>。</summary>
    internal static bool HasAcsSuffixes(SpriteReplacer skin)
        => AcsSuffixes.HasRealClip(skin);

    /// <summary>
    /// 静态底图 SpriteData（无 ACS 时用）。
    /// </summary>
    internal static SpriteData? GetStaticSkinData(SpriteReplacer? skin, Chara? owner = null)
    {
        if (skin is null) {
            return null;
        }

        if (owner is not null && TryGetStaticCombatData(owner, skin, out var combatData)) {
            return combatData;
        }

        if (skin.data is not null && !skin.data.path.IsEmpty()) {
            return skin.data;
        }

        if (skin.suffixes is not null
            && skin.suffixes.TryGetValue("", out var baseData)
            && baseData is not null) {
            return baseData;
        }

        return null;
    }

    internal static bool TryGetStaticCombatData(
        Chara owner,
        SpriteReplacer? skin,
        out SpriteData? data)
    {
        data = null;
        if (owner is null || !AcsStateResolver.IsCombatVisual(owner)) {
            return false;
        }

        // 优先从传入的 skin 查找
        if (skin?.suffixes is not null
            && skin.suffixes.TryGetValue(StaticCombatSuffix, out data)
            && data is not null && !data.path.IsEmpty()) {
            return true;
        }

        // 兜底：从 dictSkins 通过 c_idSpriteReplacer 查找（解决 NPC 运行时实例同步延迟）
        if (!owner.c_idSpriteReplacer.IsEmpty()) {
            string baseId = NormalizeSkinId(owner.c_idSpriteReplacer);
            if (!baseId.IsEmpty()
                && SpriteReplacer.dictSkins.TryGetValue(baseId, out var canonical)
                && canonical.suffixes is not null
                && canonical.suffixes.TryGetValue(StaticCombatSuffix, out data)
                && data is not null && !data.path.IsEmpty()) {
                return true;
            }
        }

        return false;
    }
}
