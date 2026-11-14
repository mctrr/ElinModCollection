using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ACS.Tiling;

namespace ACS.Patches;

/// <summary>
/// 分片贴图的 UV 兜底（只对 <see cref="PngTiler"/> 造的 <c>bcs_tile*</c> 生效）。
///
/// 起因：一条 strip 被分到多张贴图后，<c>CharaActor.RefreshSprite</c> 只在「需要刷新」时
/// 按 <c>sr.sprite</c> 烘焙 <c>_Rect</c> / <c>_PixelHeight</c>；而原版
/// <c>CardActor.OnRender</c> 的逐帧动画只换 <c>sr.sprite</c> + <c>_MainTex</c>，
/// **不碰 <c>_Rect</c>**（单张贴图时这没问题，因为 region 不变）。
/// 跨片（帧序号越过片边界）时 <c>_MainTex</c> 换了、<c>_Rect</c> 还指在上一片上 → 采样错位。
///
/// 这里在 <c>CardActor.OnRender</c> 之后补一刀：**只在「本 actor 的贴图实例变了」时**
/// 按 <c>RefreshSprite</c> 的算法重写 <c>_Rect</c>。单张贴图（99% 的皮肤）永远不触发，
/// 零回归面；PCC 一律跳过（<c>PccRenderPatch</c> 每帧自己按 flip 写 <c>_Rect</c>，别抢）。
/// </summary>
[HarmonyPatch]
internal static class TiledRectGuard
{
    private const string TileTexturePrefix = "bcs_tile";

    private static readonly Dictionary<int, int> LastSpriteId = new();
    private static readonly Dictionary<int, int> LastTextureId = new();

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CardActor), nameof(CardActor.OnRender))]
    internal static void Postfix(CardActor __instance)
    {
        try {
            if (__instance is CharaActorPCC) {
                return;
            }

            SpriteRenderer? sr = __instance.sr;
            MaterialPropertyBlock? mpb = __instance.mpb;
            if (sr is null || mpb is null) {
                return;
            }

            Sprite? sprite = sr.sprite;
            Texture2D? tex = sprite?.texture;
            if (sprite is null || tex is null || !IsTiledTexture(tex)) {
                return;
            }

            int actorId = __instance.GetInstanceID();
            if (LastSpriteId.TryGetValue(actorId, out int lastSprite)
                && lastSprite == sprite.GetInstanceID()
                && LastTextureId.TryGetValue(actorId, out int sameTex)
                && sameTex == tex.GetInstanceID()) {
                return;
            }

            LastSpriteId[actorId] = sprite.GetInstanceID();

            int texId = tex.GetInstanceID();
            if (LastTextureId.TryGetValue(actorId, out int lastTex) && lastTex == texId) {
                return; // 同片内换帧：vanilla 行为，无需干预
            }

            LastTextureId[actorId] = texId;

            // 与 CharaActor.RefreshSprite 同一算法（NPC 不交换 x，左右靠 sr.flipX）
            Rect r = sprite.textureRect;
            mpb.SetVector("_Rect", new Vector4(
                r.x / tex.width,
                r.yMin / tex.height,
                r.xMax / tex.width,
                r.yMax / tex.height));
            mpb.SetFloat("_PixelHeight", sprite.rect.height);
            sr.SetPropertyBlock(mpb);
        }
        catch (Exception) {
            // 渲染兜底绝不允许把异常抛回原版渲染循环
        }
    }

    private static bool IsTiledTexture(Texture2D tex)
    {
        string n = tex.name;
        return !string.IsNullOrEmpty(n)
            && n.StartsWith(TileTexturePrefix, StringComparison.Ordinal);
    }
}
