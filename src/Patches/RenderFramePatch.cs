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

    private static readonly AccessTools.FieldRef<CardActor, float> SpriteTimerRef =
        AccessTools.FieldRefAccess<CardActor, float>("spriteTimer");

    /// <summary>
    /// GetCurrentAnimatedData 被调用说明原版动画块没被 HasSprite / frame 早退跳过。
    /// 自定义皮肤在种族无 Texture ACS 时会早退，需由 Postfix 自己推进帧。
    /// </summary>
    private static bool _vanillaAnimTouched;

    /// <summary>
    /// NPC 侧推进状态：同帧去重 + clip 引用变化时从第 0 帧重播。
    /// 用独立副本而不直接依赖 actor.spriteTimer/spriteIndex：
    /// 1) 换肤角色 vanillaTouched=true 时原版与 Postfix 同帧各推一次 → 双倍速；
    ///    这里仅记录我们这一路的推进，配合 allowAdvance 避免叠加。
    /// 2) 状态切换（combat→melee→magic…）若不重置帧序号，会从中途/末帧续播：
    ///    一次性动画瞬间播完 → 看起来“攻击不触发”。
    /// </summary>
    private sealed class NpcFrameState
    {
        public SpriteData? LastClip;
        public string? LastSig;
        public float Timer;
        public int Index;
        public int LastFrame;
    }

    private static readonly Dictionary<int, NpcFrameState> NpcFrames = new();

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
        _vanillaAnimTouched = true;
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
            // (vanilla current can still be sourceCard combat/walk strip after a no-move reskin).
            if (!chara.c_idSpriteReplacer.IsEmpty()) {
                var baseSheet = ResolveBaseSheet(chara);
                if (baseSheet is not null) {
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

        // 当前皮有 ACS 但缺本状态片（常见：有 idle 无 move）时，GetAcsClip 会回 idle。
        // idle 若是单帧，原版 frame<=1 早退仍会把种族多帧 current 播完。钉在 idle/底图。
        if (data.frame <= 1 && !chara.c_idSpriteReplacer.IsEmpty()
            && !ReferenceEquals(data, current)) {
            return data;
        }

        // 切换 clip（combat→hurt/magic/melee…）时把原版帧序号归零：
        // 否则 actor.spriteIndex 残留上一 clip 的播放位置（例如 combat 25 帧播到第 20 帧，
        // 切到 15 帧的 hurt 时 20 >= 14）→ FreezeOneShotBeforeWrap 首帧就误判“末帧”，
        // 一次性动画刚触发就被清掉（表现为 0.1s 闪完 / 不触发）。
        int nActorKey = actor.GetInstanceID();
        if (!NpcFrames.TryGetValue(nActorKey, out var nSt)) {
            nSt = new NpcFrameState();
            NpcFrames[nActorKey] = nSt;
        }

        if (!ReferenceEquals(nSt.LastClip, data)) {
            nSt.LastClip = data;
            nSt.Index = 0;
            nSt.Timer = 0f;
            SpriteIndexRef(actor) = 0;
            SpriteTimerRef(actor) = 0f;
        }

        // 原版拿到 clip 后还会 spriteIndex++ / wrap。revive/greet 末帧先冻住，避免再闪第 0 帧。
        if (AcsStateResolver.FreezeOneShotBeforeWrap(
                chara, data.frame, SpriteIndexRef(actor), SpriteTimerRef(actor), data.time)) {
            // -delta：原版随后 += delta 后仍 < time，本帧不会 wrap
            SpriteTimerRef(actor) = -Core.delta;
            SpriteIndexRef(actor) = data.frame - 1;
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
        if (spriteData is null) {
            return;
        }

        ApplySpriteToActor(actor, spriteData.GetSprite(), force);
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

    [HarmonyPrefix]
    [HarmonyPatch(typeof(CardActor), nameof(CardActor.OnRender))]
    internal static void OnRenderPrefix()
    {
        _vanillaAnimTouched = false;
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
        bool vanillaTouched = _vanillaAnimTouched;
        _vanillaAnimTouched = false;

        // 静态 _bcs_combat：原版 frame<=1 早退，只能靠 Postfix 覆盖
        if (AcsStateResolver.IsCombatVisual(chara)
            && PcSkinPatch.TryGetStaticCombatData(chara, replacer, out var combatData)
            && combatData is not null) {
            _combatSpriteActors.Add(actorId);
            ApplySpriteToActor(__instance, combatData);
            return;
        }

        // 非 combat：上一帧有战斗 sprite 则恢复为正常贴图
        if (_combatSpriteActors.Remove(actorId)) {
            var normalData = ResolveNonCombatSprite(chara, replacer);
            if (normalData is not null) {
                ApplySpriteToActor(__instance, normalData, force: true);
            }

            _combatAppliedSpriteIds.Remove(actorId);
            _combatAppliedFlip.Remove(actorId);
            return;
        }

        // 原版 CardActor 用 sourceCard.replacer + 种族 id。
        // 手动换肤后若种族没有 Texture ACS，HasSprite 早退，自定义 ACS 永远不播。
        // 有 ACS 但缺 move 时 GetCurrent 可能已把 idle 单帧塞进 current，原版仍早退，
        // 必须再覆盖一次，否则种族行走条会露出来。
        if (!vanillaTouched) {
            // 原版动画块整体没跑（HasSprite / frame 早退）：由我们全权推进。
            TryApplyCustomSkinAnim(__instance, chara, p, allowAdvance: true);
        }
        else if (!chara.c_idSpriteReplacer.IsEmpty()) {
            // 原版动画块跑了：Transpiler 已把 SpriteData 换成 ACS clip 且原版本帧在推进。
            // 这里再 AdvanceAndApply 会与原版各推一次 = 双倍速；只补单帧钉帧场景。
            TryApplyCustomSkinAnim(__instance, chara, p, allowAdvance: false);
        }
    }

    /// <summary>
    /// 原版动画块被跳过时，用当前自定义皮肤的 ACS / 底图推进并覆盖 sr。
    /// <paramref name="allowAdvance"/>：原版未推进时 true（完整推进多帧）；
    /// 原版已推进时 false（仅单帧钉帧，多帧交还原版避免双倍速）。
    /// </summary>
    private static void TryApplyCustomSkinAnim(CardActor actor, Chara chara, RenderParam p, bool allowAdvance)
    {
        if (chara.c_idSpriteReplacer.IsEmpty()) {
            return;
        }

        SpriteData? data = null;
        var suffixes = AcsSuffixes.Resolve(chara);
        if (suffixes is not null) {
            data = chara.GetAcsClip(p.snow, suffixes);
        }

        if (data is null) {
            data = ResolveBaseSheet(chara);
        }

        if (data is null) {
            return;
        }

        if (data.frame > 1) {
            if (allowAdvance) {
                AdvanceAndApply(actor, chara, data);
            }

            return;
        }

        AcsStateResolver.StopOneShotIfLastFrame(chara, data.frame, 0);
        ApplySpriteToActor(actor, data);
    }

    private static void AdvanceAndApply(CardActor actor, Chara chara, SpriteData data)
    {
        int actorKey = actor.GetInstanceID();
        if (!NpcFrames.TryGetValue(actorKey, out var st)) {
            st = new NpcFrameState();
            NpcFrames[actorKey] = st;
        }

        int fc = Time.frameCount;
        if (st.LastFrame != fc) {
            st.LastFrame = fc;
            string? sig = AcsStateResolver.GetOneShotSignature(chara);
            bool newAction = sig != null && sig != st.LastSig;
            if (newAction) {
                // 新一轮一次性动作（攻击/施法/受伤…）：即使 clip 与上一帧是同一个
                // SpriteData 对象（如 magic/melee 回退共用 combat 图），也必须从头播——
                // 否则继承战斗姿态的旧帧序号，一两帧就被判“播完”清掉（视觉上加速/闪完）。
                st.LastSig = sig;
                st.LastClip = data;
                st.Timer = 0f;
                st.Index = 0;
            }
            else if (!ReferenceEquals(st.LastClip, data)) {
                // clip 引用变化（idle↔move↔combat↔melee…）：从第 0 帧重播。
                st.LastClip = data;
                st.Timer = 0f;
                st.Index = 0;
            }
            else {
                st.Timer += Core.delta;
                if (st.Timer >= data.time && data.time > 0f) {
                    st.Timer -= data.time;
                    st.Index = AcsStateResolver.AdvanceClipIndex(chara, data.frame, st.Index);
                }
            }
        }

        // 防御：帧数变少时钳制（例如 move 回 idle）
        if (data.frame > 0 && st.Index >= data.frame) {
            st.Index = 0;
        }

        SpriteTimerRef(actor) = st.Timer;
        SpriteIndexRef(actor) = st.Index;

        Sprite? sprite = null;
        try {
            var sprites = data.GetSprites();
            sprite = sprites.TryGet(st.Index, returnNull: true);
        }
        catch {
            sprite = data.GetSprite();
        }

        if (sprite is not null) {
            ApplySpriteToActor(actor, sprite);
        }
    }

    /// <summary>
    /// Vanilla OnRender animation only swaps sr.sprite / _MainTex.
    /// Do not rewrite _Rect / _PixelHeight: RefreshSprite already laid those out from the base sprite.
    /// Changing them to ACS-frame UVs is what sank the NPC sprite.
    /// </summary>
    private static void ApplySpriteToActor(CardActor actor, Sprite sprite, bool force = false)
    {
        if (actor.sr is null || actor.mpb is null || sprite is null || sprite.texture is null) {
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
        actor.sr.SetPropertyBlock(actor.mpb);
        _combatAppliedSpriteIds[actorId] = spriteId;
        _combatAppliedFlip[actorId] = flip;
    }
}
