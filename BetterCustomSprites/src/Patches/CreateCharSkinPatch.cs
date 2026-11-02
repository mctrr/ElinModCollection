using System;
using HarmonyLib;

namespace ACS.Patches;

/// <summary>
/// 创建角色面板（UICharaMaker）底部追加原版「更换贴图」按钮。
/// Refresh 会 Clear/Build note，故在 postfix 里每次重建后注入。
/// </summary>
[HarmonyPatch]
internal class CreateCharSkinPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(UICharaMaker), nameof(UICharaMaker.Refresh))]
    internal static void OnRefresh(UICharaMaker __instance)
    {
        try {
            if (__instance.note is null || __instance.chara is null) {
                return;
            }

            if (__instance.chara.mimicry != null) {
                return;
            }

            __instance.note.Space(8, 0);

            string label = "editSkin".lang();
            if (label.IsEmpty()) {
                label = "editSkin";
            }

            __instance.note.AddButton(label, () =>
            {
                var c = __instance.chara;
                SkinUi.OpenEditSkin(c, () => SkinUi.RebindAfterSkinEdit(c));
            });

            // Refresh 已 Build 过；追加控件后再 Build 一次以刷新布局
            __instance.note.Build();
        }
        catch (Exception ex) {
            AcsMod.Warn($"CreateCharSkinPatch.OnRefresh: {ex.Message}");
        }
    }
}
