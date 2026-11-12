using System;
using HarmonyLib;

namespace ACS.Patches;

/// <summary>
/// 23.359.0 起 <c>CardActor.OnRender</c> 只在 <c>sd != spriteData</c> 时切贴图、清零帧序号。
/// 于是贴图被作废、actor 被对象池复用、或角色重新绑定 owner 时，必须显式把
/// <c>spriteData</c> 置 null：否则本体认为「答案没变」，会一直显示旧图 / 停在旧帧，
/// 单帧 clip 与静态 _bcs_combat 尤其明显（作用域外的 RefreshSprite 写的是底图）。
/// </summary>
[HarmonyPatch]
internal static class SpriteDataInvalidation
{
    /// <summary>让在场角色的 actor 下一帧重新问一次 GetSpriteData（热重载用）。主线程调用。</summary>
    internal static void InvalidateOnScreenActors()
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

                cr.actor.spriteData = null;
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
        __instance.spriteData = null;
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
