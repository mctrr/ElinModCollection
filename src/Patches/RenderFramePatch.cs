using System.Collections.Generic;
using System.Reflection.Emit;
using ACS.API;
using HarmonyLib;
using UnityEngine;

namespace ACS.Patches;

/// <summary>
/// 原版 <see cref="CardActor.OnRender"/> 只播放 replacer.data 的多帧动画。
/// 本 patch 用 IL 注入，在帧推进逻辑读到 SpriteData 时，换成当前 ACS clip，
/// 让 NPC / 非 PCC 角色也能按 combat、greet 等状态播动画。
///
/// 主角（PCC）由 <see cref="PccRenderPatch"/> 单独覆盖 SpriteRenderer；
/// 这里对 CharaActorPCC 直接 return current，避免两套逻辑双重推进帧。
/// </summary>
[HarmonyPatch]
internal class RenderFramePatch
{
    /// <summary>
    /// 缓存 private 字段访问器。
    /// 若每次 AccessTools.FieldRefAccess 都会反射查找，greet 结束检测会变慢。
    /// </summary>
    private static readonly AccessTools.FieldRef<CardActor, int> SpriteIndexRef =
        AccessTools.FieldRefAccess<CardActor, int>("spriteIndex");

    /// <summary>
    /// Transpiler：在原版“读取 data.frame”附近插入委托调用，
    /// 把局部变量里的 SpriteData 替换为 GetCurrentAnimatedData 的返回值。
    /// 具体 IL 匹配依赖原版方法结构；游戏大版本更新后若 patch 失败会 ThrowIfInvalid。
    /// </summary>
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(CardActor), nameof(CardActor.OnRender))]
    internal static IEnumerable<CodeInstruction> OnRenderMpb(IEnumerable<CodeInstruction> instructions)
    {
        var cm = new CodeMatcher(instructions);
        return cm
            .End()
            .MatchStartBackwards(
                new CodeMatch(i => i.IsLdloc()),
                new CodeMatch(i => i.opcode == OpCodes.Ldfld && i.operand.ToString().Contains("frame")),
                new CodeMatch(OpCodes.Ldc_I4_1))
            .ThrowIfInvalid("replace sprite data")
            .Advance(1)
            .InsertAndAdvance(
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldarg_1),
                Transpilers.EmitDelegate(GetCurrentAnimatedData),
                new CodeInstruction(OpCodes.Dup),
                new CodeInstruction(OpCodes.Stloc_S, cm.InstructionAt(-1).operand))
            .InstructionEnumeration();
    }

    /// <summary>
    /// 运行时：给定原版 current 数据，返回应播放的 ACS 数据；没有 ACS 则原样返回 current。
    /// 参数 actor / p 由 IL 注入的 Ldarg 传入。
    ///
    /// 注意：原版 CardActor.OnRender 在 frame &lt;= 1 时直接 return，
    /// 所以单帧 _bcs_combat 不会经过这里，而由 <see cref="OnRenderPostfix"/> 处理。
    /// 这里只负责多帧 ACS 选片；有多帧 _acs_combat 时优先用它，
    /// 不要在已有动画 clip 时再抢成静态战斗图。
    /// </summary>
    private static SpriteData GetCurrentAnimatedData(SpriteData current, CardActor actor, RenderParam p)
    {
        var owner = actor.owner;
        if (owner is null) {
            return current;
        }

        // PCC 主角整段渲染由 PccRenderPatch 接管
        if (actor is CharaActorPCC) {
            return current;
        }

        if (owner is not Chara chara) {
            return current;
        }

        // 无真实 _acs_* 时不改原版动画
        var suffixes = AcsSuffixes.Resolve(chara);
        if (suffixes is null) {
            // Custom base without ACS: never keep playing race / foreign multi-frame current
            // (vanilla current can still be sourceCard combat strip after a no-combat reskin).
            if (!chara.c_idSpriteReplacer.IsEmpty()) {
                var baseSheet = ResolveBaseSheet(chara);
                if (baseSheet is not null && !ReferenceEquals(current, baseSheet)) {
                    return baseSheet;
                }
            }

            return current;
        }

        var data = chara.GetAcsClip(p.snow, suffixes);
        if (data is null) {
            // Same rule as Resolve null: equipped custom base must not fall through to race current.
            if (!chara.c_idSpriteReplacer.IsEmpty()) {
                var baseSheet = ResolveBaseSheet(chara);
                if (baseSheet is not null) {
                    return baseSheet;
                }
            }

            return current;
        }

        // greet 播到最后一帧附近时清标记，下次回到 idle/其他状态
        if (AcsStateResolver.IsGreetActive(chara) && data.frame > 0) {
            int spriteIndex = SpriteIndexRef(actor);
            if (spriteIndex >= data.frame - 1) {
                AcsStateResolver.StopGreet(chara);
            }
        }

        return data;
    }

    /// <summary>
    /// Current base skin non-ACS sheet (replacer.data or suffixes[""]).
    /// Used when custom base has no real _acs_* so we still leave race multi-frame combat.
    /// </summary>
    private static SpriteData? ResolveBaseSheet(Chara chara)
    {
        if (chara is null) {
            return null;
        }

        var bound = chara.spriteReplacer;
        if (bound is not null
            && !ReferenceEquals(bound, chara.sourceCard?.replacer)
            && TryGetSheet(bound, out var boundSheet)) {
            return boundSheet;
        }

        if (chara.c_idSpriteReplacer.IsEmpty()
            || SpriteReplacer.dictSkins is null) {
            return null;
        }

        string baseId = PcSkinPatch.NormalizeSkinId(chara.c_idSpriteReplacer);
        if (baseId.IsEmpty()
            || !SpriteReplacer.dictSkins.TryGetValue(baseId, out var skin)
            || skin is null) {
            return null;
        }

        // Keep runtime bind on the equipped base (NPC may still hold race replacer).
        if (!ReferenceEquals(chara.spriteReplacer, skin)) {
            chara.spriteReplacer = skin;
        }

        return TryGetSheet(skin, out var sheet) ? sheet : null;
    }

    private static bool TryGetSheet(SpriteReplacer replacer, out SpriteData? sheet)
    {
        sheet = null;
        if (replacer is null) {
            return false;
        }

        if (replacer.data is not null && !replacer.data.path.IsEmpty()) {
            sheet = replacer.data;
            return true;
        }

        if (replacer.suffixes is not null
            && replacer.suffixes.TryGetValue("", out var baseData)
            && baseData is not null
            && !baseData.path.IsEmpty()) {
            sheet = baseData;
            return true;
        }

        return false;
    }

    // 跟踪上一帧应用过战斗 sprite 的 actor，用于 combat 结束时恢复原始 sprite
    private static readonly HashSet<int> _combatSpriteActors = new();

    // 同一 actor 连续战斗帧时，避免每帧重复 SetTexture / SetPropertyBlock
    private static readonly Dictionary<int, int> _combatAppliedSpriteIds = new();
    private static readonly Dictionary<int, bool> _combatAppliedFlip = new();

    // 将 SpriteData 的第一帧应用到 CardActor 的 sr 和 mpb
    private static void ApplySpriteToActor(CardActor actor, SpriteData spriteData, bool force = false)
    {
        if (actor.sr is null || actor.mpb is null || spriteData is null) {
            return;
        }

        var sprite = spriteData.GetSprite();
        if (sprite is null || sprite.texture is null) {
            return;
        }

        int actorId = actor.GetInstanceID();
        int spriteId = sprite.GetInstanceID();
        bool flip = actor.sr.flipX;
        if (!force
            && _combatAppliedSpriteIds.TryGetValue(actorId, out int lastId)
            && lastId == spriteId
            && _combatAppliedFlip.TryGetValue(actorId, out bool lastFlip)
            && lastFlip == flip
            && ReferenceEquals(actor.sr.sprite, sprite)) {
            return;
        }

        actor.sr.sprite = sprite;
        if (sprite.texture.filterMode != FilterMode.Point) {
            sprite.texture.filterMode = FilterMode.Point;
        }

        actor.mpb.SetTexture("_MainTex", sprite.texture);
        var tex = sprite.texture;
        var tr = sprite.textureRect;
        float x0 = tr.x / (float)tex.width;
        float x1 = tr.xMax / (float)tex.width;
        float y0 = tr.yMin / (float)tex.height;
        float y1 = tr.yMax / (float)tex.height;
        var rect = flip ? new Vector4(x1, y0, x0, y1) : new Vector4(x0, y0, x1, y1);
        actor.mpb.SetVector("_Rect", rect);
        actor.mpb.SetFloat("_PixelHeight", sprite.rect.height);
        actor.sr.SetPropertyBlock(actor.mpb);
        _combatAppliedSpriteIds[actorId] = spriteId;
        _combatAppliedFlip[actorId] = flip;
    }

    private static SpriteData? ResolveNonCombatSprite(Chara chara, SpriteReplacer? replacer)
    {
        var suffixes = AcsSuffixes.Resolve(chara);
        if (suffixes is not null) {
            var clip = chara.GetAcsClip(false, suffixes);
            if (clip is not null) {
                return clip;
            }
        }

        if (replacer?.data is not null && !replacer.data.path.IsEmpty()) {
            return replacer.data;
        }

        if (replacer?.suffixes is not null
            && replacer.suffixes.TryGetValue("", out var baseData)
            && baseData is not null
            && !baseData.path.IsEmpty()) {
            return baseData;
        }

        return null;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CardActor), nameof(CardActor.OnRender))]
    internal static void OnRenderPostfix(CardActor __instance, RenderParam p)
    {
        if (__instance is CharaActorPCC) {
            return;
        }

        var owner = __instance.owner;
        if (owner is not Chara chara) {
            return;
        }

        int actorId = __instance.GetInstanceID();
        var replacer = chara.spriteReplacer ?? chara.sourceCard?.replacer;

        // 静态 _bcs_combat：原版 frame<=1 早退，只能靠 Postfix 覆盖
        if (AcsStateResolver.IsCombatVisual(chara)
            && PcSkinPatch.TryGetStaticCombatData(chara, replacer, out var combatData)
            && combatData is not null) {
            _combatSpriteActors.Add(actorId);
            ApplySpriteToActor(__instance, combatData);
            return;
        }

        // 非 combat：上一帧有战斗 sprite 则恢复为正常贴图
        if (!_combatSpriteActors.Remove(actorId)) {
            return;
        }

        var normalData = ResolveNonCombatSprite(chara, replacer);
        if (normalData is not null) {
            ApplySpriteToActor(__instance, normalData, force: true);
        }

        _combatAppliedSpriteIds.Remove(actorId);
        _combatAppliedFlip.Remove(actorId);
    }
}
