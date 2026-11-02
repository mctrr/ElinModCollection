using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Wetness.Patches;

/// <summary>
/// 「湿润」及以上的自然回蓝加成（+10%）。
///
/// 只挂在自然回复那一句上，不碰 StatsMana.Mod。
/// 上一版把 Prefix 打在 StatsMana.Mod 上，那是所有 MP 增减的唯一收口 ——
/// 结果药水、睡觉、祈祷、吸魔全都跟着加了一成，范围明显超了。
/// 这里改成只在原版 TickConditions 的自然回复分支里放大那个量。
///
/// 原版那一句（反编译 Chara.cs:4200-4204）是
///     if (EClass.rnd(8) == 0 && mana.value < mana.max)
///     {
///         mana.Mod(EClass.rnd(Evalue(301) / 2 + 1) + 1);
///         elements.ModExp(301, 8f);
///     }
/// 对应的 IL（Elin.dll 实测）：
///     IL_082d: ldarg.0
///     IL_082e: call     Stats Chara::get_mana()
///     IL_0833: ldarg.0
///     IL_0834: ldc.i4   301
///     IL_0839: call     int32 Card::Evalue(int32)
///     IL_083e: ldc.i4.2
///     IL_083f: div
///     IL_0840: ldc.i4.1
///     IL_0841: add
///     IL_0842: call     int32 EClass::rnd(int32)
///     IL_0847: ldc.i4.1
///     IL_0848: add                          <-- 栈上是 [mana, 回复量]
///     IL_0849: callvirt void Stats::Mod(int32)
///
/// 改动就是在 IL_0849 前面插两条指令，把栈顶那个回复量过一手：
///     ldarg.0                               <-- [mana, 回复量, this]
///     call int32 WetnessManaRegen::Boost(int32, Chara)
///     callvirt void Stats::Mod(int32)
/// 参数顺序是「量在前、角色在后」不是随便写的：静态方法的实参按声明顺序压栈，
/// 栈顶要给最后一个参数，而此刻栈顶正好是刚算出来的回复量，
/// 所以角色必须排在最后，才能靠一次 ldarg.0 直接补上。
///
/// 为什么要走 IL 而不是挑个方法挂 Prefix：原版这一句没有任何独立的方法边界，
/// 它内联在 TickConditions 里（该方法 300 多行）。能唯一标识它的只有
/// `ldc.i4 301` + `call Card::Evalue` 这一对 —— Evalue(301) 在整个游戏里
/// 只在这一处出现，所以锚点是稳的。
///
/// 至于 +10% 为什么不能写成 amount * 110 / 100：
/// 原版一次自然回复经常只有 1-6 点，整数除法会把它直接抹成 0，加成等于没有。
/// 所以用 _cints[SlotManaAccum] 存「百分之一 MP」的零头，攒够 100 才吐 1 点 ——
/// 长期严格等于自然回复总量的 110%，而且只在这句回蓝真的触发时才推进，
/// 不会自己凭空生成 MP。
/// </summary>
// 类级 [HarmonyPatch] 必需：缺了它整个类会被 HarmonyX 静默跳过（见 WetnessDrinkPatch 注释）。
[HarmonyPatch]
internal static class WetnessManaRegenPatch
{
    private static readonly MethodInfo BoostMethod =
        AccessTools.Method(typeof(WetnessManaRegenPatch), nameof(Boost));

    /// <summary>
    /// 自然回复的量经手这里。amount 是原版算出来的量，返回值是实际上限后的量。
    /// 参数顺序由注入方式决定，不要调换。
    /// </summary>
    internal static int Boost(int amount, Chara c)
    {
        if (amount <= 0 || c == null || !c.IsPC)
        {
            return amount;
        }

        if (!WetnessDef.HasManaRegenBonus(c.wetness().GetPhase()))
        {
            return amount;
        }

        int total = c._cints[WetnessDef.SlotManaAccum] + amount * WetnessDef.ManaRegenBonusPercent;
        c._cints[WetnessDef.SlotManaAccum] = total % 100;
        return amount + total / 100;
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(Chara), nameof(Chara.TickConditions))]
    private static IEnumerable<CodeInstruction> TickConditionsTranspiler(
        IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> list = new List<CodeInstruction>(instructions);

        int anchor = FindManaRegenAnchor(list);
        if (anchor < 0)
        {
            Plugin.LogWarn("[Wetness] 在 TickConditions 里找不到自然回蓝的锚点，MP 加成已停用。");
            return list;
        }

        for (int i = anchor; i < list.Count; i++)
        {
            if (!IsIntMod(list[i]))
            {
                continue;
            }
            list.Insert(i, new CodeInstruction(OpCodes.Call, BoostMethod));
            list.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
            Plugin.LogInfo("[Wetness] 自然回蓝加成已挂载（TickConditions，注入点 +"
                + (i - anchor) + " 条指令）。");
            return list;
        }

        Plugin.LogWarn("[Wetness] 锚点之后没有找到 Stats.Mod，MP 加成已停用。");
        return list;
    }

    /// <summary>
    /// 定位 `ldc.i4 301` + `call Card::Evalue(int32)` 这一对，
    /// 返回 ldc.i4 的下标。整数 301 落在 ldc.i4（4 字节形式）上。
    /// </summary>
    private static int FindManaRegenAnchor(List<CodeInstruction> list)
    {
        for (int i = 0; i + 1 < list.Count; i++)
        {
            if (list[i].opcode != OpCodes.Ldc_I4 || !(list[i].operand is int n) || n != 301)
            {
                continue;
            }
            if (list[i + 1].opcode == OpCodes.Call &&
                list[i + 1].operand is MethodInfo mi && mi.Name == "Evalue")
            {
                return i;
            }
        }
        return -1;
    }

    private static bool IsIntMod(CodeInstruction ci)
    {
        if (ci.opcode != OpCodes.Call && ci.opcode != OpCodes.Callvirt)
        {
            return false;
        }
        if (!(ci.operand is MethodInfo mi) || mi.Name != "Mod")
        {
            return false;
        }
        ParameterInfo[] ps = mi.GetParameters();
        return ps.Length == 1 && ps[0].ParameterType == typeof(int);
    }
}
