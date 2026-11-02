using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ACS.Patches;

/// <summary>
/// 居民列表右键菜单为 PC / PCC NPC 追加 editSkin。
/// </summary>
[HarmonyPatch]
internal class PeopleMenuPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(BaseListPeople), nameof(BaseListPeople.OnClick))]
    internal static void OnClickPostfix(BaseListPeople __instance, Chara c)
    {
        if (c is null || c.mimicry != null) {
            return;
        }

        // 原版只给非 PCC 居民加 editSkin；这里补 PC 与 PCC NPC。
        bool vanillaHasEditSkin = !c.IsPC
            && c.pccData is null
            && (c.sourceCard is null || c.sourceCard.idActor.IsEmpty());
        if (vanillaHasEditSkin) {
            return;
        }

        if (!c.IsPC && c.pccData is null) {
            return;
        }

        var menu = UIContextMenu.Current;
        if (menu is null || !menu.isActiveAndEnabled) {
            return;
        }

        menu.AddButton("editSkin", () =>
        {
            SkinUi.OpenEditSkin(c, () =>
            {
                try {
                    __instance.list.Refresh();
                }
                catch (Exception) {
                    // ignore
                }

                SkinUi.RebindAfterSkinEdit(c);
            });
        });

        LayoutRebuilder.ForceRebuildLayoutImmediate(menu.layoutGroup.transform as RectTransform);
    }
}
