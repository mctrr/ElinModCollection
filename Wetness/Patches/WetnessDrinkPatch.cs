using System;
using HarmonyLib;

namespace Wetness.Patches;

/// <summary>
/// 饮用相关：补水与禁饮。
///
/// 关键分工：
/// - 补水挂 FoodEffect.ProcDrink —— 它是「喝」的统一出口，
///   Chara.Drink（直接喝）和 ActThrow（投掷药水到人身上）都会汇到这里，
///   所以两条途径都能补水，正合原设定「饮用与投掷都能增加湿润度」。
/// - 禁饮只挂 Chara.Drink —— 投掷不经过它，因此偏湿时依然可以投掷（原设定亦如此）。
/// - 井水的「喝」不走 Chara.Drink，是 TraitWell 自己注册的 actPlan 动作，单独拦。
/// </summary>
// 类级 [HarmonyPatch] 是必需的，不是装饰：
// HarmonyX 的 PatchClassProcessor 只在类级属性非空时才继续，否则构造函数直接 return，
// 整个类的补丁被静默跳过（方法级 [HarmonyPatch] 一律作废，连报错都没有）。
// 空着写即可 —— 真正的目标由下面各方法上的属性给。
[HarmonyPatch]
internal static class WetnessDrinkPatch
{
    /// <summary>饮用后按液体种类补充湿润度。</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(FoodEffect), nameof(FoodEffect.ProcDrink))]
    private static void ProcDrinkPostfix(Chara c, Thing food)
    {
        if (food == null || !c.TracksWetness())
        {
            return;
        }

        // 传 food 进去：禁列表要按物品 id 判（水银、耐酸保护液、堕天使之血都带 TraitDrink，
        // 不挡的话会按默认的「药水 = 10」白送水分）。
        EffectId id = (food.trait as TraitDrink)?.IdEffect ?? EffectId.Drink;
        int gain = WetnessWater.GetDrinkGain(food, id);
        if (gain != 0)
        {
            c.wetness().Mod(gain);
        }
    }

    /// <summary>「湿润」及以上不能再直接饮用。</summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.Drink))]
    private static bool DrinkPrefix(Chara __instance)
    {
        if (!__instance.IsPC || !WetnessDef.IsWet(__instance.wetness().GetPhase()))
        {
            return true;
        }

        __instance.Say(WetnessLang.TooWetToDrink, __instance);
        return false;
    }

    /// <summary>
    /// 井水的「喝」是 TraitWell 自己往 ActPlan 里塞的动作，绕过 Chara.Drink。
    /// 这里直接不注册该选项 —— 注意 TrySetAct 在构建菜单时会被反复调用，
    /// 所以不能在这里输出提示（会刷屏），提示交给档位变化时的消息。
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TraitWell), nameof(TraitWell.TrySetAct))]
    private static bool WellTrySetActPrefix()
    {
        Chara pc = EMono.pc;
        if (pc == null || !WetnessDef.IsWet(pc.wetness().GetPhase()))
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// 井水补水。
    ///
    /// 为什么不能只挂 <see cref="ProcDrinkPostfix"/>：`TraitWell.TrySetAct`
    /// 塞进 ActPlan 的那个 `actDrink` 是个自建 <see cref="DynamicAct"/>，
    /// 它的 onPerform 里自己跑完整个流程 —— Say / PlaySound /
    /// ActEffect.Proc(ModPotential) / BadEffect / Say("drinkWater_clear")，
    /// **既不调 FoodEffect.ProcDrink，也不调 Chara.Drink**。
    /// 所以常规那条补水 Postfix 对井水一次都不会触发（表现为「喝井水不加水」）。
    ///
    /// 修法：把原版刚建好的 onPerform 包一层。返回值就是「有没有真的喝到一口」——
    /// 空井那条分支 `return false`，其余分支 `return true`，语义正好。
    /// 顺带白送一个信息：原版在 onPerform 里已经判过 polluted，
    /// 我们在包装时读一次当前状态即可，不用重算。
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(TraitWell), nameof(TraitWell.TrySetAct))]
    private static void WellTrySetActPostfix(TraitWell __instance, ActPlan p)
    {
        if (p == null)
        {
            return;
        }

        // TrySetAct 只会往 list 里追加，不会清空 —— 所以要拿到「本次新加的那一条」。
        // 没有更好的标识可用：Item 里没有名字字段，DynamicAct 只靠静态 lastAct
        // （那是全局最后一次建的，别处的动作也会覆盖它）。
        for (int i = p.list.Count - 1; i >= 0; i--)
        {
            if (p.list[i].act is DynamicAct act
                && act.id == "actDrink"
                && act.onPerform != null
                && !(act.onPerform.Target is WellDrinkWrapper))
            {
                act.onPerform = new WellDrinkWrapper(__instance, act.onPerform).Run;
                return;
            }
        }
    }

    /// <summary>
    /// 包住原版喝井水的动作，跑完按井的状态补一份水。
    /// 用类包一层而不是 lambda，是为了让 <c>onPerform.Target</c> 能认出「已包过」——
    /// TrySetAct 每次构建菜单都会被调用，不做标记会层层套娃。
    /// </summary>
    private sealed class WellDrinkWrapper
    {
        private readonly TraitWell _well;
        private readonly Func<bool> _inner;

        internal WellDrinkWrapper(TraitWell well, Func<bool> inner)
        {
            _well = well;
            _inner = inner;
        }

        internal bool Run()
        {
            bool drank = _inner();

            Chara pc = EMono.pc;
            if (drank && pc != null && pc.TracksWetness())
            {
                int gain = WetnessWater.GetWellGain(_well.IsHoly, _well.polluted);
                if (gain != 0)
                {
                    pc.wetness().Mod(gain);
                    Plugin.LogWellDrink(_well, gain);
                }
            }

            return drank;
        }
    }
}
