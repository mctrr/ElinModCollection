using System;
using System.Collections.Generic;
using ACS.Skin;

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
    ///
    /// <b>为什么逐角色 try</b>：<c>ListSkins()</c> 的 Postfix 会调这里，而
    /// <c>chara.c_idSpriteReplacer</c> 的 getter（<c>Card.GetStr(13)</c>）在
    /// <c>mapStr</c> 还没初始化时会 NRE —— 读档 / 重建期间正好是这个窗口。
    /// 以前整个循环只包一层 try：一个刚 new 出来的角色炸掉，<b>后面的 NPC 一个都
    /// 没重绑</b>（日志里 <c>PcSkinBinder.Ensure (ListSkins): Object reference not set
    /// to an instance of an object</c> 就是这么来的，而且它同时是 PC 换肤「点了没反应」
    /// 的原因之一 —— 抛异常后 <c>TryApplyPcSkin</c> 根本没机会跑）。
    /// 现在用 <see cref="SkinIndex.Ensure"/> 一次读出「归一 id + 表/编目实例」（内部安全读 id），
    /// 并逐个隔离异常
    /// （<c>IsPC</c> 也读角色状态，留在 try 内）。
    ///
    /// <b>取 map 那一行同样必须包 try</b>：<c>EClass._map</c> 的 getter 读 <c>EClass.core.map</c>，
    /// 本方法会在 <c>ModManager.RefreshMods</c> 协程里被调到，那一刻 core 可能还没建好 →
    /// <b>getter 自己 NRE</b>，方法直接崩出去。同一个坑 10-04 踩过一次（游戏进不去）。
    /// </summary>
    internal static void RebindNpcCustomSkins()
    {
        List<Chara>? charas;
        try {
            charas = EClass._map?.charas;
        }
        catch (Exception ex) {
            // 地图还没就绪（模组刷新期 / 读档期）：本轮跳过，等下一次 ListSkins 再补。
            AcsMod.Log("RebindNpcCustomSkins: map not ready (" + ex.Message + "); skip");
            return;
        }

        if (charas is null) {
            return;
        }

        foreach (var chara in charas) {
            if (chara is null) {
                continue;
            }

            try {
                if (chara.IsPC) {
                    continue;
                }

                // 一次查找：归一 id + 表/编目实例（表里没有就按编目补建），不再自己读 id。
                if (SkinIndex.Ensure(chara).Id.IsEmpty()) {
                    continue;
                }

                if (chara.renderer is CharaRenderer cr && cr.pccData is not null) {
                    PcSkinPatch.TryApplyPcSkin(cr);
                    continue;
                }

                if (chara.renderer is CharaRenderer npc) {
                    PcSkinPatch.BindNpcCustomSkin(npc);
                }
            }
            catch (Exception ex) {
                AcsMod.Warn($"RebindNpcCustomSkins: one chara failed ({ex.Message}); continue");
            }
        }
    }
}
