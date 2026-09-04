using System;
using ACS.API;
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
