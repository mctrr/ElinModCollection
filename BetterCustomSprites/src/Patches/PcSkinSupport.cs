using System;
using ACS.API;
using ACS.Frame;
using ACS.Skin;
using HarmonyLib;
using UnityEngine;

namespace ACS.Patches;

/// <summary>
/// 主角 skin：绑定、冷启动重试、控制台命令、居民菜单入口。
/// </summary>
[HarmonyPatch]
internal class PcSkinPatch
{

    private static int _lastListSkinsFrame = -999;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(CharaRenderer), nameof(CharaRenderer.SetOwner))]
    internal static void OnSetOwnerPrefix(CharaRenderer __instance, Card c, out string? __state)
    {
        __state = null;
        if (c is not Chara chara || chara.c_idSpriteReplacer.IsEmpty()) {
            return;
        }

        string id = SkinIndex.NormalizeId(chara.c_idSpriteReplacer);
        if (!id.IsEmpty() && id != chara.c_idSpriteReplacer) {
            chara.c_idSpriteReplacer = id;
        }

        // 绑定路径要「表里没有就按编目补建」，所以走 Ensure（渲染热路径走 Get，不碰磁盘）。
        PrepareSkinSprite(SkinIndex.EnsureSkin(id));

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

        // 一次查找拿全：归一 id + 表/编目实例。绑定路径只消费结果，不再自己读 id / 归一。
        var found = SkinIndex.Ensure(owner);
        if (found.Id.IsEmpty()) {
            return;
        }
        if (found.Id != owner.c_idSpriteReplacer) {
            owner.c_idSpriteReplacer = found.Id;
        }

        var skin = found.Equipped;
        if (skin is null || !SkinIndex.Usable(skin)) {
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
            skin = SkinIndex.Ensure(owner).Equipped;
        }
        if (skin is null || !SkinIndex.Usable(skin)) {
            return;
        }

        if (ReferenceEquals(owner.spriteReplacer, skin)) {
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
        if (renderer is null || renderer.pccData is not null || HasRenderReplacerCondition(renderer.owner)) {
            return;
        }

        // 注意：不能在这里用 IsVanillaAtlasRender(renderer.replacer!=null && pccData==null)
        // 拦截——非 PCC 的 NPC 一律 pccData==null，曾把所有 NPC 换肤都误判成“atlas 变身”
        // 跳过（已回滚）。改用 HasRenderReplacerCondition：只有角色身上存在真正提供
        // RendererReplacer 的条件（变形/boost 等，_CreateRenderer 会据此把 data/pccData
        // 切成 replacer 形态）才跳过，天生非 PCC 的 NPC 没有此类条件，换肤不受影响。
        var owner = renderer.owner;
        if (owner is null || owner.mimicry != null) {
            return;
        }

        var found = SkinIndex.Ensure(owner);
        string id = found.Id;
        if (id.IsEmpty()) {
            return;
        }

        if (id != owner.c_idSpriteReplacer) {
            owner.c_idSpriteReplacer = id;
        }

        var skin = found.Equipped;
        if (skin is null || !SkinIndex.Usable(skin)) {
            return;
        }

        owner.spriteReplacer = skin;
        var race = owner.sourceCard?.renderData ?? owner.source?.renderData;
        if (race is null) {
            return;
        }

        // 换贴图后 sprites 是中心 pivot，必须配「中心锚点」的 RenderData，否则整张图掉半个身高。
        // 原版只给 @xxx 行做这一跳（mod 角色正常），原版角色行写 chara → 沉底。
        renderer.data = AcsSpritePresenter.ResolveCenterAnchoredRenderData(race) ?? race;
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
    /// 原版渲染替换态：角色身上存在提供 RendererReplacer 的条件（变形/boost 等）。
    /// 此时 _CreateRenderer 已把 renderer.data / pccData 全部切成 replacer 的形态
    /// （如变猫 → cat_black 的 RenderData、pccData=null）。若此时再把 renderer.data
    /// 覆盖回 sourceCard 的 renderData（人形角色 = pcc 类），后续 OnEnterScreen 里
    /// data.CreateActor() 会造出 pccData==null 的 CharaActorPCC →
    /// CharaActorPCC.OnSetOwner → PCC.Get(null) → pccCache.Add(null) 崩溃。
    /// （变猫/变波球复现；变物品/变婴儿/解除变身均正常。）
    /// 天生非 PCC 的 NPC 没有此类条件，换肤不受影响。
    /// </summary>
    internal static bool HasRenderReplacerCondition(Chara? owner)
    {
        if (owner is null || owner.conditions is null) {
            return false;
        }

        foreach (Condition con in owner.conditions) {
            try {
                if (con is not null && con.GetRendererReplacer() is not null) {
                    return true;
                }
            }
            catch (Exception) {
                // 单个条件异常不影响整体判断
            }
        }
        return false;
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
    /// 动态底图 SpriteData（无 ACS 时用）：战斗态优先静态战斗图，否则就是皮肤底图。
    /// </summary>
    internal static SpriteData? GetStaticSkinData(SpriteReplacer? skin, Chara? owner = null)
    {
        if (skin is null) {
            return null;
        }

        if (owner is not null && TryGetStaticCombatData(owner, skin, out var combatData)) {
            return combatData;
        }

        return SkinIndex.BaseSheet(skin);
    }

    internal static bool TryGetStaticCombatData(
        Chara owner,
        SpriteReplacer? skin,
        out SpriteData? data)
    {
        data = null;
        if (owner is null || !PlayState.IsCombatVisual(owner)) {
            return false;
        }

        // 优先从传入的 skin 查找
        if (skin?.suffixes is not null
            && skin.suffixes.TryGetValue(AcsSuffixes.StaticCombat, out data)
            && data is not null && !data.path.IsEmpty()) {
            return true;
        }

        // 兜底：从 dictSkins 通过 c_idSpriteReplacer 查找（解决 NPC 运行时实例同步延迟）
        if (!owner.c_idSpriteReplacer.IsEmpty()) {
            string baseId = SkinIndex.NormalizeId(owner.c_idSpriteReplacer);
            if (!baseId.IsEmpty()
                && SpriteReplacer.dictSkins.TryGetValue(baseId, out var canonical)
                && canonical.suffixes is not null
                && canonical.suffixes.TryGetValue(AcsSuffixes.StaticCombat, out data)
                && data is not null && !data.path.IsEmpty()) {
                return true;
            }
        }

        return false;
    }
}
