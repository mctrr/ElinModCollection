using System;
using ACS.API;
using ACS.Skin;
using UnityEngine;

namespace ACS.Patches;

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

            // 安全读取 + 归一只有一份实现：读档 / ListSkins 重建期间 pc 可能刚 new 出来，
            // c_idSpriteReplacer 的 getter（Card.GetStr → mapStr.GetValueOrDefault）在
            // mapStr 还是 null 时会 NRE，那样连「PC 有没有装皮肤」都判断不了，
            // PC 绑定整条链就废了。这里只查表（不碰磁盘），绑定由下方 TryApplyPcSkin 做。
            string pcSkinId = SkinIndex.Get(pc).Id;
            if (!pcSkinId.IsEmpty()) {
                if (pc.renderer is CharaRenderer cr) {
                    PcSkinPatch.TryApplyPcSkin(cr, forceListSkins: forceListSkins);
                }

                var bound = pc.spriteReplacer;
                bool ok = bound is not null && SkinIndex.Usable(bound);
                if (ok) {
                    int clipCount = AcsSuffixes.CountClips(bound);
                    bool hasStatic = PcSkinPatch.GetStaticSkinData(bound) is not null;
                    AcsMod.Log(
                        $"PC skin ready ({reason}): {pcSkinId} clips={clipCount} static={hasStatic}");
                    _ensureUntilFrame = -1;
                }
                else if (forceListSkins) {
                    string detail = "null";
                    if (bound is not null) {
                        int keys = bound.suffixes?.Count ?? 0;
                        detail = $"no-usable-skin (suffixKeys={keys}, data={(bound.data is null ? "null" : "set")})";
                    }
                    AcsMod.Warn(
                        $"PC skin not ready ({reason}): id={pcSkinId}, replacer={detail}");
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
