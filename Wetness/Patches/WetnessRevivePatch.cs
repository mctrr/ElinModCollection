using HarmonyLib;

namespace Wetness.Patches;

/// <summary>
/// 复活后的水分值。
///
/// 照抄原版 Revive 对饥饿的处理：
///     if (hunger.value > 30) hunger.value = 30;     // 复活后最多「有点饿」
/// 原版让玩家复活时不至于再被饥饿追着跑，水分同理 —— 死过一次的人不该一睁眼就渴死。
///
/// 方向相反：饥饿是「越大越饿」，湿润度是「越大越湿」，所以这里夹的是下限。
/// 取 WetnessDef.ReviveValue（45）：刚过「干燥」的边界，落在静默区里 ——
/// 不渴、也不显示、离「湿润（70，喝不下东西）」还有一大截。
///
/// 原版那句只夹上限（已经比 30 饱就不再动），这里也只夹下限（已经够湿就不再动），
/// 喝水喝到「湿润」的玩家复活后不会莫名其妙被灌回中间值。
/// </summary>
[HarmonyPatch(typeof(Chara), nameof(Chara.Revive))]
internal static class WetnessRevivePatch
{
    [HarmonyPostfix]
    private static void Postfix(Chara __instance)
    {
        if (!__instance.TracksWetness())
        {
            return;
        }

        Stats wet = __instance.wetness();
        if (wet.value < WetnessDef.ReviveValue)
        {
            wet.value = WetnessDef.ReviveValue;
        }
    }
}
