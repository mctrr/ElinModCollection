using System;
using System.Collections.Generic;
using ACS.Frame;
using ACS.Skin;
using HarmonyLib;

namespace ACS.Patches;

/// <summary>
/// 23.359.0 起原版用 <c>Card.GetSpriteData</c> 统一回答「这张卡现在画哪份 SpriteData」：
/// <c>CardActor.OnRender</c> 自己负责比对引用、清零帧序号、调 ReloadSprite、推进多帧动画。
/// BCS 只改这个答案，不再接管帧推进、贴图应用与 _Rect 同步。
///
/// 作用域：只在 <c>CardActor.OnRender</c> 期间回答 ACS clip。UI（立绘 / 热键栏 / 换肤列表 /
/// 居民板）也走 <c>Chara.GetSprite</c> → <c>GetSpriteData</c>，而 <c>GetAcsClip</c> 有清除
/// 一次性标记的副作用，<c>ResolveSuffixes</c> 会回写 spriteReplacer，必须挡在作用域外。
/// </summary>
[HarmonyPatch]
internal class RenderFramePatch
{
    /// <summary>
    /// 旧本体（&lt; 23.359.0）没有 GetSpriteData 与 public spriteData，整类不注册：
    /// 一旦注册，OnRender postfix 里的字段访问会让 JIT 抛 MissingFieldException，
    /// 而它跑在读档 / OnEnterScreen 路径上，玩家会「进不去存档」。
    /// ClearRuntimeCaches 仍可被外部安全调用（方法体不碰新字段）。
    /// </summary>
    [HarmonyPrepare]
    internal static bool Prepare() => RenderPath.UseSpriteData;

    // ---- OnRender 作用域（Unity 主线程，单字段够用）----
    private static CardActor? _scopeActor;
    private static SpriteData? _scopeAnswer;
    private static bool _scopeResolved;

    // 一次性动作签名：actor.InstanceID → 上一帧的 sig。
    // 同一 clip 的新一轮动作（magic/melee 缺片回退共用 combat 图）也要从第 0 帧重播。
    private static readonly Dictionary<int, string?> LastSig = new();

    private static bool _warned;

    /// <summary>切图 / 新游戏时清 per-actor 状态，避免对象池复用带上旧签名。</summary>
    internal static void ClearRuntimeCaches()
    {
        LastSig.Clear();
        ClearScope();

        // 本类在旧本体上整类跳过，但清理入口固定指向这里（两处调用点），
        // 所以旧路径的字典也由这里一起清。方法体不碰新 API，两版都能执行。
        LegacyRenderFramePatch.ClearRuntimeCaches();
    }

    private static void ClearScope()
    {
        _scopeActor = null;
        _scopeAnswer = null;
        _scopeResolved = false;
    }

    private static void SafeWarn(string message)
    {
        if (_warned) {
            return;
        }

        _warned = true;
        try {
            AcsMod.Warn(message);
        }
        catch (Exception) {
            // 日志本身绝不能把渲染路径弄崩。
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(CardActor), nameof(CardActor.OnRender))]
    internal static void RenderScopePrefix(CardActor __instance)
    {
        _scopeActor = __instance;
        _scopeAnswer = null;
        _scopeResolved = false;
    }

    /// <summary>无论原版是否抛异常都要收作用域，否则下一次 UI 取图会被当成渲染调用。</summary>
    [HarmonyFinalizer]
    [HarmonyPatch(typeof(CardActor), nameof(CardActor.OnRender))]
    internal static Exception? RenderScopeFinalizer(Exception? __exception)
    {
        ClearScope();
        return __exception;
    }

    /// <summary>
    /// 选片 postfix。顺序按 handover，不可重排：
    /// ① 作用域 / 早退 → ② GetAcsClip → ③ 一次性签名与末帧冻结 → ④ 静态战斗图
    /// → ⑤ 保留 __result → ⑥ 续期与切帧就绪。
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.GetSpriteData))]
    internal static void OnGetSpriteData(Chara __instance, bool snow, ref SpriteData __result)
    {
        CardActor? actor = _scopeActor;
        // 作用域外，或者作用域内查的是别的角色：完全不干预，也不消耗本帧缓存。
        if (actor is null || !ReferenceEquals(actor.owner, __instance)) {
            return;
        }

        // 作用域内只算一次。ReloadSprite 会嵌套调 Chara.GetSprite → GetSpriteData，
        // 直接返回缓存，保证 OnRender 按引用比较 sd != spriteData 时答案稳定
        // （snow 由调用方传入，OnRender 用 p.snow、GetSprite 用区域 IsSnowCovered）。
        if (_scopeResolved) {
            __result = _scopeAnswer!;
            return;
        }

        _scopeResolved = true;
        _scopeAnswer = __result;

        try {
            // ① 早退：PCC 由 PccRenderPatch 覆盖，原版对 PCC 恒返回 null，不要主动写值。
            //    坐骑被骑乘 / 部分变身时 actor 是 CharaActorPCC 而 Chara.IsPCC 为 false，
            //    所以以 actor.isPCC 为准；两个都判只是更保守。
            if (actor.isPCC || __instance.IsPCC) {
                return;
            }

            if (__instance.mimicry != null
                || PcSkinPatch.IsVanillaAtlasRender(__instance)) {
                return;
            }

            // 23.359.0 的 sprite_override 只在 Chara.GetSprite 里生效（整张 PNG），
            // GetSpriteData 完全不看它。BCS 不应把 clip / 皮肤 replacer 盖上去。
            if (!__instance.GetStr("sprite_override").IsEmpty()) {
                return;
            }

            SpriteData? answer = __result;

            // ② 当前状态的 ACS clip。ResolveSuffixes / GetAcsClip 有副作用，
            //    所以只允许在 OnRender 作用域内跑。
            var suffixes = SkinIndex.ResolveSuffixes(__instance);
            SpriteData? clip = suffixes is null
                ? null
                : __instance.GetAcsClip(snow, suffixes);
            if (clip is not null) {
                answer = clip;
            }

            // ③ 一次性动作签名 + 末帧冻结（必须在 ② 之后：缺片标记与 combat_begin 都在 ② 里写）。
            if (clip is not null) {
                int key = actor.GetInstanceID();
                string? sig = PlayState.GetOneShotSignature(__instance);
                LastSig.TryGetValue(key, out var last);
                LastSig[key] = sig;

                bool newAction = sig != null && sig != last;
                bool clipChanged = !ReferenceEquals(clip, actor.spriteData);

                // 同一 clip 的新一轮一次性动作：clip 引用没变，本体不会重播。
                // 令 spriteData = null 强制它 ReloadSprite + 清零帧序号。
                if (newAction && !clipChanged) {
                    actor.spriteData = null;
                }

                // 本帧会被重播时不要冻结（那时 actor 的帧序号还是上一帧的）。
                // 冻结后设 -delta：原版随后 += delta 仍 < time，本帧不 wrap，停在末帧。
                if (!newAction && !clipChanged && clip.frame > 1
                    && PlayState.FreezeOneShotBeforeWrap(
                        __instance, clip.frame, actor.spriteIndex, actor.spriteTimer, clip.time)) {
                    actor.spriteTimer = -Core.delta;
                    actor.spriteIndex = clip.frame - 1;
                }
            }

            // ④ 静态 _bcs_combat：2.5.0 NPC 路径的实际顺序就是它压过 clip，保持一致。
            //    进出战斗都由本体 OnRender 触发 ReloadSprite，不再需要 _combatSpriteActors 记账。
            var replacer = __instance.spriteReplacer ?? __instance.sourceCard?.replacer;
            if (PcSkinPatch.TryGetStaticCombatData(__instance, replacer, out var combatData)
                && combatData is not null) {
                answer = combatData;
            }

            // ⑤ 其余保留 __result：_snow / _dirN / 皮肤底图 / 别人 mod 的 states 继续生效。
            _scopeAnswer = answer;
            __result = answer!;

            // ⑥ 底图与状态片都要续期；_acs_ 片还要保证 frame/sprites 已切好。
            if (answer is not null) {
                SpriteCacheEvict.Touch(answer);
                if (!answer.path.IsEmpty()
                    && answer.path.IndexOf("_acs_", StringComparison.Ordinal) >= 0) {
                    SkinListPatch.EnsureAcsSprites(answer!);
                }
            }
        }
        catch (Exception ex) {
            // 本体不兜 mod 补丁的异常，抛出去会打断整张地图的绘制。
            __result = _scopeAnswer!;
            SafeWarn("[BCS] GetSpriteData postfix failed: " + ex);
        }
    }

    /// <summary>
    /// 原版只对 frame &gt; 1 推进帧，单帧 clip 的一次性动作要在这里收尾；
    /// 睡觉 / 死亡的横躺姿态也仍由 BCS 摆（原版会把换皮角色 SubPass 重置成 Default）。
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(CardActor), nameof(CardActor.OnRender))]
    internal static void OnRenderPostfix(CardActor __instance, RenderParam p)
    {
        if (__instance is CharaActorPCC) {
            return;
        }

        if (__instance.owner is not Chara chara) {
            return;
        }

        try {
            if (!chara.c_idSpriteReplacer.IsEmpty()) {
                // 必须放在 postfix：prefix 里 spriteData 还是上一帧的，
                // 会拿上一帧的单帧 idle 判断，把刚激活的 greet 直接结束掉。
                var data = __instance.spriteData;
                if (data is not null && data.frame <= 1) {
                    PlayState.StopOneShotIfLastFrame(chara, data.frame, __instance.spriteIndex);
                }

                AcsSpritePresenter.ApplyDeadOrSleepPose(chara, __instance);
                AcsSpritePresenter.ProbePose("NPC", chara, __instance, p.y);
            }
        }
        catch (Exception ex) {
            SafeWarn("[BCS] OnRender postfix failed: " + ex);
        }
    }
}
