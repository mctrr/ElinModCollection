using System;
using System.IO;
using ACS.API;
using HarmonyLib;
using ReflexCLI.Attributes;
using UnityEngine;
using UnityEngine.UI;

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

        // Vanilla atlas transmute/boost：无 PCC 的 RendererReplacer，跳过防 NRE
        if (IsVanillaAtlasRender(renderer)) {
            return;
        }

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

/// <summary>
/// PC skin 就绪的统一入口：ListSkins / 冷启动 / 渲染重绑 都走这里。
/// </summary>
internal static class PcSkinBinder
{
    private static int _ensureUntilFrame = -1;
    private const int EnsureFrames = 90;

    internal static bool HasCachedSkins()
        => SpriteReplacer.dictSkins is { Count: > 0 };

    internal static void Schedule(string reason, bool forceListSkins = true)
    {
        _ensureUntilFrame = Time.frameCount + EnsureFrames;
        Ensure(reason, forceListSkins);
    }

    /// <summary>
    /// 尝试 ListSkins（可选）+ 绑定 PC spriteReplacer。
    /// 成功则停止冷启动重试窗口。
    /// </summary>
    internal static void Ensure(string reason, bool forceListSkins = false)
    {
        try {
            if (forceListSkins) {
                try {
                    SpriteReplacer.ListSkins();
                }
                catch (Exception ex) {
                    AcsMod.Warn($"ListSkins during ensure ({reason}): {ex.Message}");
                }
            }

            var pc = EClass.pc;
            if (pc is not null && !pc.c_idSpriteReplacer.IsEmpty()) {
                if (pc.renderer is CharaRenderer cr) {
                    PcSkinPatch.TryApplyPcSkin(cr, forceListSkins: forceListSkins);
                }

                bool ok = pc.spriteReplacer is not null && PcSkinPatch.HasUsableSkin(pc.spriteReplacer);
                if (ok) {
                    int clipCount = AcsSuffixes.CountClips(pc.spriteReplacer);
                    bool hasStatic = PcSkinPatch.GetStaticSkinData(pc.spriteReplacer) is not null;
                    AcsMod.Log(
                        $"PC skin ready ({reason}): {pc.c_idSpriteReplacer} clips={clipCount} static={hasStatic}");
                    _ensureUntilFrame = -1;
                }
                else if (forceListSkins) {
                    string detail = "null";
                    if (pc.spriteReplacer is not null) {
                        int keys = pc.spriteReplacer.suffixes?.Count ?? 0;
                        detail = $"no-usable-skin (suffixKeys={keys}, data={(pc.spriteReplacer.data is null ? "null" : "set")})";
                    }
                    AcsMod.Warn(
                        $"PC skin not ready ({reason}): id={pc.c_idSpriteReplacer}, replacer={detail}");
                }
            }

            if (forceListSkins || reason == "retry" || reason == "ListSkins"
                || reason.StartsWith("Scene.Init", StringComparison.Ordinal)) {
                SkinUi.RebindNpcCustomSkins();
            }
        }
        catch (Exception ex) {
            AcsMod.Warn($"PcSkinBinder.Ensure ({reason}): {ex.Message}");
        }
    }

    internal static void TickRetry()
    {
        try {
            if (_ensureUntilFrame < 0 || Time.frameCount > _ensureUntilFrame) {
                return;
            }

            if (Time.frameCount % 5 != 0) {
                return;
            }

            if (EClass.pc is null || EClass.core?.IsGameStarted != true) {
                return;
            }

            Ensure("retry", forceListSkins: false);
        }
        catch (Exception) {
            // 绝不让 postfix 拖垮 Scene.OnUpdate
        }
    }
}

/// <summary>
/// 冷启动/读档后强制 ListSkins + 重绑 PC skin（ACS 或静态）。
/// </summary>
[HarmonyPatch]
internal class GameStartPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Game), nameof(Game.Load))]
    internal static void OnGameLoad()
    {
        PcSkinBinder.Schedule("Game.Load", forceListSkins: true);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Scene), nameof(Scene.Init))]
    internal static void OnSceneInit(Scene.Mode newMode)
    {
        if (newMode == Scene.Mode.StartGame) {
            PcSkinBinder.Schedule($"Scene.Init({newMode})", forceListSkins: !PcSkinBinder.HasCachedSkins());
        }
        else if (newMode == Scene.Mode.Zone) {
            // Zone travel must rebind only. ListSkins rebuilds every PCR clip from scratch.
            PcSkinBinder.Schedule($"Scene.Init({newMode})", forceListSkins: false);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Scene), nameof(Scene.OnUpdate))]
    internal static void OnSceneUpdate()
    {
        PcSkinBinder.TickRetry();
    }
}

/// <summary>
/// 控制台：ACS.SetSkin / ClearSkin / ListSkins。
/// </summary>
internal static class PcSkinCommands
{
    [ConsoleCommand("")]
    public static string SetSkin(string skinId)
    {
        var pc = EClass.pc;
        if (pc is null) {
            return "PC not found.";
        }

        if (skinId.IsEmpty()) {
            return "Usage: ACS.SetSkin <skinId>";
        }

        skinId = PcSkinPatch.NormalizeSkinId(skinId);
        SpriteReplacer.ListSkins();
        var skin = SpriteReplacer.dictSkins.TryGetValue(skinId);
        if (skin is null) {
            return $"Skin '{skinId}' not found in Skin / Texture folders.";
        }

        pc.c_idSpriteReplacer = skinId;
        PcSkinPatch.RecreateRenderer(pc);
        if (pc.renderer is CharaRenderer cr) {
            PcSkinPatch.TryApplyPcSkin(cr);
        }
        return $"PC skin set to '{skinId}'.";
    }

    [ConsoleCommand("")]
    public static string ClearSkin()
    {
        var pc = EClass.pc;
        if (pc is null) {
            return "PC not found.";
        }

        pc.c_idSpriteReplacer = null;
        PcSkinPatch.RecreateRenderer(pc);
        return "PC skin cleared.";
    }

    [ConsoleCommand("")]
    public static string ListSkins()
    {
        SpriteReplacer.ListSkins();
        var skins = SpriteReplacer.dictSkins;
        if (skins.Count == 0) {
            return "No skins found in Skin / Texture folders.";
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Found {skins.Count} skin(s):");
        foreach (var entry in skins) {
            int acsCount = AcsSuffixes.CountClips(entry.Value);
            sb.AppendLine($"  {entry.Key}" + (acsCount > 0 ? $" ({acsCount} ACS clip(s))" : ""));
        }
        return sb.ToString();
    }

    [ConsoleCommand("")]
    public static string Emote(string clip = "")
    {
        var pc = EClass.pc;
        if (pc is null) {
            return "PC not found.";
        }

        var states = AcsStateResolver.ListEmoteStates(pc);
        if (states.Count == 0) {
            return "No emote ACS clip on current skin.";
        }

        if (clip.IsEmpty()) {
            if (states.Count == 1) {
                return AcsStateResolver.StartEmote(pc, states[0])
                    ? "Emote started: " + states[0]
                    : "Failed to start emote.";
            }

            return "Usage: ACS.Emote <clip>  clips: " + string.Join(", ", states);
        }

        string resolved = clip;
        if (!states.Contains(resolved)) {
            if (states.Contains("emote." + clip)) {
                resolved = "emote." + clip;
            }
            else {
                return "Unknown clip '" + clip + "'. clips: " + string.Join(", ", states);
            }
        }

        return AcsStateResolver.StartEmote(pc, resolved)
            ? "Emote started: " + resolved
            : "Failed to start emote.";
    }

    /// <summary>
    /// Reload PNG without restart: clear lift/pad/frame caches, ListSkins, rebind PC.
    /// Does not hot-swap the DLL; resources and runtime caches only.
    /// </summary>
    [ConsoleCommand("")]
    public static string ReloadAcs()
    {
        try {
            AcsSpritePresenter.ClearCaches();
            PccRenderPatch.ClearRuntimeCaches();
            AcsStateResolver.ClearHostileScanCache();
            ACS.API.StaticCombatMap.Invalidate();

            TextureSkinCatalog.Invalidate();
            SkinListPatch.ClearLoadedSprites();
            SpriteReplacer.ListSkins();
            PcSkinBinder.Ensure("ReloadAcs", forceListSkins: false);

            var pc = EClass.pc;
            if (pc is null) {
                return "ACS caches cleared + ListSkins done (no PC).";
            }

            int clips = AcsSuffixes.CountClips(pc.spriteReplacer);
            bool hasStatic = PcSkinPatch.GetStaticSkinData(pc.spriteReplacer) is not null;
            return $"ACS reloaded: id={pc.c_idSpriteReplacer} clips={clips} static={hasStatic}";
        }
        catch (Exception ex) {
            return $"ReloadAcs failed: {ex.Message}";
        }
    }
}

/// <summary>
/// 打开原版 <see cref="LayerEditSkin"/>（创建角色 / 居民菜单共用）。
/// </summary>
internal static class SkinUi
{
    internal static void OpenEditSkin(Chara c, Action? onKill = null)
    {
        if (c is null) {
            return;
        }

        var layer = EClass.ui.AddLayer<LayerEditSkin>();
        if (onKill is not null) {
            layer.SetOnKill(onKill);
        }

        layer.Activate(c);
    }

    /// <summary>
    /// 选肤后重绑 ACS/静态 skin（创建阶段可能尚未 IsGameStarted）。
    /// </summary>
    internal static void RebindAfterSkinEdit(Chara? c)
    {
        if (c is null) {
            return;
        }

        try {
            if (c.renderer is CharaRenderer { pccData: not null } cr) {
                PcSkinPatch.TryApplyPcSkin(cr, forceListSkins: true);
                c.renderer?.RefreshSprite();
                return;
            }

            if (c.renderer is CharaRenderer npc) {
                PcSkinPatch.BindNpcCustomSkin(npc);
            }

            c.renderer?.RefreshSprite();
        }
        catch (Exception ex) {
            AcsMod.Warn($"RebindAfterSkinEdit: {ex.Message}");
        }
    }

    /// <summary>
    /// 读档时 dictSkins 可能还空，NPC SetOwner 绑不上自定义皮肤。
    /// ListSkins 之后补一次：PCC 叠 ACS，非 PCC 若仍走种族 pass 则重建 renderer。
    /// </summary>
    internal static void RebindNpcCustomSkins()
    {
        try {
            var map = EClass._map;
            if (map?.charas is null) {
                return;
            }

            foreach (var chara in map.charas) {
                if (chara is null || chara.IsPC || chara.c_idSpriteReplacer.IsEmpty()) {
                    continue;
                }

                TextureSkinCatalog.EnsureSkin(chara.c_idSpriteReplacer);

                if (chara.renderer is CharaRenderer cr && cr.pccData is not null) {
                    PcSkinPatch.TryApplyPcSkin(cr);
                    continue;
                }

                if (chara.renderer is CharaRenderer npc) {
                    PcSkinPatch.BindNpcCustomSkin(npc);
                }
            }
        }
        catch (Exception ex) {
            AcsMod.Warn($"RebindNpcCustomSkins: {ex.Message}");
        }
    }
}

/// <summary>
/// 创建角色面板（UICharaMaker）底部追加原版「更换贴图」按钮。
/// Refresh 会 Clear/Build note，故在 postfix 里每次重建后注入。
/// </summary>
[HarmonyPatch]
internal class CreateCharSkinPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UICharaMaker), nameof(UICharaMaker.Refresh))]
    internal static void OnRefresh(UICharaMaker __instance)
    {
        try {
            if (__instance is null || __instance.note is null || __instance.chara is null) {
                return;
            }

            if (__instance.chara.mimicry != null) {
                return;
            }

            __instance.note.Space(8, 0);

            string label = "editSkin".lang();
            if (label.IsEmpty()) {
                label = "editSkin";
            }

            __instance.note.AddButton(label, () =>
            {
                var c = __instance.chara;
                SkinUi.OpenEditSkin(c, () => SkinUi.RebindAfterSkinEdit(c));
            });

            // Refresh 已 Build 过；追加控件后再 Build 一次以刷新布局
            __instance.note.Build();
        }
        catch (Exception ex) {
            AcsMod.Warn($"CreateCharSkinPatch.OnRefresh: {ex.Message}");
        }
    }
}

/// <summary>
/// 居民列表右键菜单为 PC / PCC NPC 追加 editSkin。
/// </summary>
[HarmonyPatch]
internal class PeopleMenuPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(BaseListPeople), nameof(BaseListPeople.OnClick))]
    internal static void OnClickPostfix(BaseListPeople __instance, Chara c)
    {
        if (c is null || c.mimicry != null) {
            return;
        }

        // 原版只给非 PCC 居民加 editSkin；这里补 PC 与 PCC NPC。
        bool vanillaHasEditSkin = !c.IsPC
            && c.pccData is null
            && (c.sourceCard is null || c.sourceCard.idActor.IsEmpty());
        if (vanillaHasEditSkin) {
            return;
        }

        if (!c.IsPC && c.pccData is null) {
            return;
        }

        var menu = UIContextMenu.Current;
        if (menu is null || !menu.isActiveAndEnabled) {
            return;
        }

        menu.AddButton("editSkin", () =>
        {
            SkinUi.OpenEditSkin(c, () =>
            {
                try {
                    __instance.list.Refresh();
                }
                catch (Exception) {
                    // ignore
                }

                SkinUi.RebindAfterSkinEdit(c);
            });
        });

        LayoutRebuilder.ForceRebuildLayoutImmediate(menu.layoutGroup.transform as RectTransform);
    }
}
