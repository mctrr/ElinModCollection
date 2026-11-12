using System;
using HarmonyLib;

namespace ACS.Patches;

/// <summary>
/// 23.359.0 起 <c>CardActor.OnRender</c> 只在 <c>sd != spriteData</c> 时切贴图、清零帧序号。
/// 于是贴图被作废、actor 被对象池复用、或角色重新绑定 owner 时，必须显式把
/// <c>spriteData</c> 置 null：否则本体认为「答案没变」，会一直显示旧图 / 停在旧帧，
/// 单帧 clip 与静态 _bcs_combat 尤其明显（作用域外的 RefreshSprite 写的是底图）。
///
/// 这个类整体依赖 23.359.0 的 public 字段。旧本体上必须整类不注册、并且每个
/// 真正的字段访问都留在单独的方法里，否则 JIT 抛 MissingFieldException 会让玩家
/// 进不去存档（2.6.0 的实际事故）。
/// </summary>
[HarmonyPatch]
internal static class SpriteDataInvalidation
{
    /// <summary>旧本体没有这些字段，整类不注册（含下面的 ReloadSprite 前缀）。</summary>
    [HarmonyPrepare]
    internal static bool Prepare() => RenderPath.UseSpriteData;

    /// <summary>让在场角色的 actor 下一帧重新问一次 GetSpriteData（热重载用）。主线程调用。</summary>
    internal static void InvalidateOnScreenActors()
    {
        // 旧本体上到此为止。字段访问在 Core 里，只有这里放行才会被 JIT 编译。
        if (!ApiProbe.HasSpriteDataApi) {
            return;
        }

        InvalidateOnScreenActorsCore();
    }

    /// <summary>把一个 actor 的 <c>spriteData</c> 置 null。旧本体上是空操作。</summary>
    internal static void ClearSpriteData(CardActor? actor)
    {
        if (actor is null || !ApiProbe.HasSpriteDataApi) {
            return;
        }

        ClearSpriteDataCore(actor);
    }

    /// <summary>
    /// 字段访问只能待在这里：旧本体永不调用 → 永不 JIT → 不会解析到不存在/非 public 的字段。
    /// 不要为了「少一个函数」把这一行挪回上面两个方法。
    /// </summary>
    private static void ClearSpriteDataCore(CardActor actor)
    {
        actor.spriteData = null;
    }

    private static void InvalidateOnScreenActorsCore()
    {
        try {
            // EClass._map / core 在模组刷新、读档、场景初始化、无 activeZone 时本身会抛 NRE，
            // 从第一行起就必须在 try 内；这里绝不能把热重载或渲染路径带崩。
            var map = EClass._map;
            if (map?.charas is null) {
                return;
            }

            foreach (var c in map.charas) {
                // 只失效换肤角色：非换肤角色置 null 只会平白多一次 ReloadSprite。
                if (c is null || c.c_idSpriteReplacer.IsEmpty()
                    || c.renderer is not CharaRenderer cr || cr.actor is null) {
                    continue;
                }

                ClearSpriteDataCore(cr.actor);
            }
        }
        catch (Exception ex) {
            AcsMod.Warn("[BCS] spriteData invalidate failed: " + ex.Message);
        }
    }

    /// <summary>
    /// 对象池复用：<c>spriteData</c> 不随 Kill 清空，SetOwner 换的是 owner 而不是数据。
    /// 不清会让新角色沿用上一个角色的 SpriteData 引用，本体因引用相等跳过切换。
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(CardActor), nameof(CardActor.SetOwner))]
    internal static void OnSetOwner(CardActor __instance)
    {
        ClearSpriteData(__instance);
    }

    /// <summary>
    /// 23.359.0 新增的 <c>CharaActor.ReloadSprite</c> 里
    /// <c>mpb.SetTexture("_MainTex", sr.sprite.texture)</c> 是裸取：owner.GetSprite() 返回
    /// null（sprites 为空数组 + 种族行也没贴图）时会在 RefreshSprite 之前就 NRE，
    /// 而 SpriteRefreshGuard 补在 RefreshSprite 上，拦不到这一句。
    /// 命中时跳过本次 ReloadSprite，保留上一帧的 sr.sprite / mpb，坏素材退化成「这一帧不刷新」。
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CharaActor), nameof(CharaActor.ReloadSprite))]
    internal static bool SkipReloadWhenSpriteMissing(CharaActor __instance)
    {
        // PCC 走 RendererReplacer，别碰它。
        if (__instance.isPCC || __instance.owner is null) {
            return true;
        }

        try {
            var sprite = __instance.owner.GetSprite();
            return sprite is not null && sprite.texture is not null;
        }
        catch (Exception) {
            // GetSprite 自己抛了（坏素材 / 表未建好）时同样跳过，避免每帧刷异常。
            return false;
        }
    }
}
