using HarmonyLib;

namespace Wetness.Patches;

/// <summary>
/// 每回合的湿润度变化：自然衰减、下雨暂停，以及脱水症扣魔力。
/// 挂在 Chara.TickConditions() 之后 —— 原版 hunger 的衰减、最饿时的掉血
/// 全都写在这个方法里，所以可以直接照抄它的节奏，不会出现「渴得比饿还快」的错位。
///
/// 这里不碰 HP 与 SP —— 那两条原版 hunger 已经占了。
/// 干侧的惩罚是速度（WetnessSpeedPatch）与魔力（本文件），
/// 湿侧的正面收益是自然回蓝（WetnessManaRegenPatch）。
/// </summary>
[HarmonyPatch(typeof(Chara), nameof(Chara.TickConditions))]
internal static class WetnessTickPatch
{
    [HarmonyPostfix]
    private static void Postfix(Chara __instance)
    {
        if (!__instance.TracksWetness())
        {
            return;
        }

        // 淋着雨就不消耗 —— 原设定的「雨天不降低湿润度」。
        if (WetnessDef.RainPausesDecay && IsRainedOn(__instance))
        {
            return;
        }

        Stats stats = __instance.wetness();

        // 与 hunger 同形：每 N 回合掷一次骰子，命中才扣 1 点。
        // turn 在 TickConditions 开头已经自增过，这里读到的就是原版 switch 用的那个值。
        // 不再自己累加「每 N 回合必扣 1」——那样比原版快 10 倍，几步就脱水。
        if (__instance.turn % Plugin.DecayCheckEvery == 1
            && EClass.rnd(Plugin.DecayChanceDiv) == 0)
        {
            stats.Mod(-1);
        }

        // 脱水症扣魔力：形状照抄原版 hunger 最饿那一档，但扣的是 MP 而不是 HP。
        // 与「湿润以上自然回蓝 +10%」正好对称 —— 干侧耗蓝、湿侧回蓝，
        // 而掉血与掉体力留给 hunger 自己，两条惩罚线不重叠。
        if (__instance.IsPC
            && stats.GetPhase() == WetnessDef.Parched
            && EClass.rnd(WetnessDef.DehydrateManaChance) == 0)
        {
            Stats mana = __instance.mana;
            int left = mana.value;

            // 只扣到 0 为止，不许扣成负数。
            // StatsMana.Mod 在 value < 0 时会走「魔力逆流」：说一句话，
            // 再 DamageHP(-value * 400 / ..., ManaBackfire) —— 那又把掉血绕回来了，
            // 而且死因会显示成魔力反噬，不是脱水。夹住就不会触发。
            if (left > 0)
            {
                int cost = 1 + EClass.rnd(2) + mana.max / WetnessDef.DehydrateManaDivisor;
                if (cost > left)
                {
                    cost = left;
                }
                mana.Mod(-cost);
            }
        }
    }

    /// <summary>
    /// 是否正被雨淋着。
    ///
    /// 逐字照抄原版 Chara.TickConditions 里那一处
    ///     if (EClass.world.weather.IsRaining &amp;&amp; !EClass._map.IsIndoor &amp;&amp; !pos.cell.HasRoof)
    ///         AddCondition&lt;ConWet&gt;(20);
    /// 与游戏对「露天淋雨」的定义保持一致，不另立一套。
    /// 雨势大小不区分：IsRaining 同时覆盖 Rain 与 RainHeavy。
    /// </summary>
    private static bool IsRainedOn(Chara c)
    {
        return EMono.world.weather.IsRaining
            && !EMono._map.IsIndoor
            && !c.pos.cell.HasRoof;
    }
}
