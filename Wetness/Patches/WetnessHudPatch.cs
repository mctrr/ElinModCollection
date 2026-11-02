using System.Runtime.CompilerServices;
using HarmonyLib;

namespace Wetness.Patches;

/// <summary>
/// 把湿润度挂进 HUD 状态栏。
/// WidgetStats._OnActivate() 是原版逐个 Add(NotificationStats) 的地方
/// （hunger / burden / bladder / hygiene ... 都在那儿），我们在末尾追加一条。
/// </summary>
[HarmonyPatch(typeof(WidgetStats), nameof(WidgetStats._OnActivate))]
internal static class WetnessHudPatch
{
    private static readonly ConditionalWeakTable<WidgetStats, object> Patched = new ConditionalWeakTable<WidgetStats, object>();

    [HarmonyPostfix]
    private static void Postfix(WidgetStats __instance)
    {
        if (Patched.TryGetValue(__instance, out _))
        {
            return;
        }
        Patched.Add(__instance, new object());

        __instance.Add(new NotificationStats
        {
            stats = () => EMono.pc.wetness()
        });

        Plugin.LogInfo("wetness registered on WidgetStats.");
    }
}
