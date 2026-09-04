using System;

namespace ACS.Patches;

/// <summary>
/// 打开原版 <see cref="LayerEditSkin"/>（创建角色 / 居民菜单共用）。
/// </summary>
internal static class SkinUi
{
    internal static void OpenEditSkin(Chara c, Action? onKill = null)
    {
        if (c is null) {
            return;
        }

        var layer = EClass.ui.AddLayer<LayerEditSkin>();
        if (onKill is not null) {
            layer.SetOnKill(onKill);
        }

        layer.Activate(c);
    }

    /// <summary>
    /// 选肤后重绑 ACS/静态 skin（创建阶段可能尚未 IsGameStarted）。
    /// </summary>
    internal static void RebindAfterSkinEdit(Chara? c)
    {
        if (c is null) {
            return;
        }

        try {
            if (c.renderer is CharaRenderer { pccData: not null } cr) {
                PcSkinPatch.TryApplyPcSkin(cr, forceListSkins: true);
                c.renderer?.RefreshSprite();
                return;
            }

            if (c.renderer is CharaRenderer npc) {
                PcSkinPatch.BindNpcCustomSkin(npc);
            }

            c.renderer?.RefreshSprite();
        }
        catch (Exception ex) {
            AcsMod.Warn($"RebindAfterSkinEdit: {ex.Message}");
        }
    }

    /// <summary>
    /// 读档时 dictSkins 可能还空，NPC SetOwner 绑不上自定义皮肤。
    /// ListSkins 之后补一次：PCC 叠 ACS，非 PCC 若仍走种族 pass 则重建 renderer。
    /// </summary>
    internal static void RebindNpcCustomSkins()
    {
        try {
            var map = EClass._map;
            if (map?.charas is null) {
                return;
            }

            foreach (var chara in map.charas) {
                if (chara is null || chara.IsPC || chara.c_idSpriteReplacer.IsEmpty()) {
                    continue;
                }

                TextureSkinCatalog.EnsureSkin(chara.c_idSpriteReplacer);

                if (chara.renderer is CharaRenderer cr && cr.pccData is not null) {
                    PcSkinPatch.TryApplyPcSkin(cr);
                    continue;
                }

                if (chara.renderer is CharaRenderer npc) {
                    PcSkinPatch.BindNpcCustomSkin(npc);
                }
            }
        }
        catch (Exception ex) {
            AcsMod.Warn($"RebindNpcCustomSkins: {ex.Message}");
        }
    }
}
