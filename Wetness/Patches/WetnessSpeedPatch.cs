using HarmonyLib;
using UnityEngine;

namespace Wetness.Patches;

/// <summary>
/// 速度惩罚。对齐原版 burden / stamina / sleepiness / hunger 的落点。
///
/// 原版这几条的惩罚全部写在 Chara.RefreshSpeed 里：一个从 100 起步的百分数
/// 累加器（反编译里叫 num3，本方法 2127-2199 行），最后
///     _Speed = _Speed * num3 / 100;
/// 并且顺手 info?.AddFix(-10, xxx.GetPhaseStr())，所以玩家在速度详情面板里
/// 能直接看到「饱食度 -10%」这类扣分原因。挂在这里，湿润度才会和它们并列。
///
/// 为什么得动私有字段：_Speed 是 private long，RefreshSpeed 没有给外部留任何
/// 可覆写的缝（惩罚项是硬编码的 switch）。唯一可用的着力点就是算完之后改结果。
///
/// 为什么 Postfix 里要先看 dirtySpeed：RefreshSpeed 开头有一个早退分支
/// （currentZone != EClass._zone 时把 dirtySpeed 置回 true 然后 return），
/// 那条路上 _Speed 完全没有被重算。若不加这个闸，反复读到同一个旧值、每次都乘一遍
/// 系数，速度会被越乘越小，最后撞到下限 10。而正常算完的那次会把 dirtySpeed
/// 置 false（方法末尾 2229 行），据此判断「这一次真的重算了」。
/// </summary>
// 类级 [HarmonyPatch] 必需：缺了它整个类会被 HarmonyX 静默跳过（见 WetnessDrinkPatch 注释）。
[HarmonyPatch]
internal static class WetnessSpeedPatch
{
    private static readonly bool Ready;

    private static readonly AccessTools.FieldRef<Chara, long> SpeedRef;

    static WetnessSpeedPatch()
    {
        try
        {
            SpeedRef = AccessTools.FieldRefAccess<Chara, long>("_Speed");
            Ready = SpeedRef != null;
        }
        catch (System.Exception e)
        {
            Ready = false;
            Plugin.LogWarn("找不到 Chara._Speed，速度惩罚已停用：" + e.Message);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.RefreshSpeed))]
    private static void RefreshSpeedPostfix(Chara __instance, Element.BonusInfo info)
    {
        if (!Ready || __instance.dirtySpeed)
        {
            return;
        }

        // 原版把 hunger / stamina / sleepiness 的速度惩罚也限制在 PC 上，保持一致。
        if (!__instance.IsPC)
        {
            return;
        }

        int penalty = WetnessDef.SpeedPenalty(__instance.wetness().GetPhase());
        if (penalty == 0)
        {
            return;
        }

        long speed = SpeedRef(__instance) * (100 + penalty) / 100;
        SpeedRef(__instance) = speed < 10 ? 10 : speed;

        // AddFix 自己会补上 "%"，所以这里只给名字。
        info?.AddFix(penalty, WetnessLang.WetnessName);
    }
}
