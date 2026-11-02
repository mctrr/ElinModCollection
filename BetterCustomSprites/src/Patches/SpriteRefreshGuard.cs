using System;
using HarmonyLib;

namespace ACS.Patches;

/// <summary>
/// 保险丝：原版 <c>CardActor.OnRender</c> 里是
/// <c>sr.sprite = spriteData.GetSprites().TryGet(spriteIndex, true)</c> —— **无 null 检查**。
/// 只要 SpriteData 的切片出现空槽（贴图超过 Unity 16384 上限、坏图、LoadSprites 抛异常被兜住……），
/// sr.sprite 就会被写成 null，下一帧任何 RefreshSprite 都会在
/// <c>Sprite sprite = sr.sprite; Texture2D texture = sprite.texture;</c> 处
/// NullReferenceException 刷屏（实测：Player.log 里一串 CharaActor.RefreshSprite 的 NRE）。
///
/// 这里在进入原版前把 null 修回来（从 owner 重取），实在拿不到就跳过本帧 ——
/// 让坏数据退化成「这一帧不刷新」，而不是每帧丢一条异常。
/// </summary>
[HarmonyPatch]
internal static class SpriteRefreshGuard
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CharaActor), nameof(CharaActor.RefreshSprite))]
    internal static bool Prefix(CharaActor __instance)
    {
        if (__instance.sr is null || __instance.mpb is null) {
            return false;
        }

        // 正常路径（sr.sprite 由 OnSetOwner / 渲染补丁写好）：零开销放过
        if (__instance.sr.sprite is not null) {
            return true;
        }

        try {
            var sprite = __instance.owner?.GetSprite();
            if (sprite is not null && sprite.texture is not null) {
                __instance.sr.sprite = sprite;
                __instance.mpb.SetTexture("_MainTex", sprite.texture);
                return true;
            }
        }
        catch (Exception) {
        }

        // 仍然拿不到可用 sprite：跳过原版，别在 null 上取 .texture
        return false;
    }
}
