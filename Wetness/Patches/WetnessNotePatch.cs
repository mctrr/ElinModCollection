using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Wetness.Patches;

/// <summary>
/// 物品说明里那行「含水：XX」。
///
/// 位置：跟「营养：11」同一行，接在它后面（两个空格分隔，与游戏自己的段间分隔一致）；
/// 没有营养的东西（药水、井水）就单独成行，只显示「含水：10」。
///
/// 为什么必须上 transpiler：WriteNote 是 2448 条 IL 的大方法，没有能挂 Postfix 的方法边界，
/// 而 text7 这一行是内联在里面的局部变量。
///
/// 注入点（用 .tools/ilcheck_note_anchor.py 对着真实 IL 校验过）：
///
///     ldstr "_nutrition"                          ← 锚点 1：整个方法只此一处
///     … 拼营养 → Concat(string,string,string) → stloc.s 18（写回 text7）
///     … 拼硬度 / 拼最高品质（各自同样的 Concat + stloc.s 18）
///     stloc.s 18                                  ← text7 收尾
///     ldloc.s 18                                  ← 锚点 2 定位到这里：判空之前
///     call IsEmpty(string)
///     brtrue.s IL_07c3
///     ldloc.0 / ldfld n / ldstr "NoteText_eqstats" / ldloc.s 18 / AddText
///   ↑ 在 `ldloc.s 18` 之前插「读 text7 → 过 hook → 写回 text7」。
///
/// 关键：那三条 brfalse（没有营养也没有硬度时）**直接跳到 `ldloc.s 18`**，
/// 所以必须把这个 ldloc 身上的 labels 搬到注入序列的第一条上 ——
/// 否则药水、空瓶这类 text7 从头到尾为空的物品会绕开注入点，
/// 「药水也要显示含水」就永远看不到。
/// </summary>
// 类级 [HarmonyPatch] 必需：缺了它整个类会被 HarmonyX 静默跳过（见 WetnessDrinkPatch 注释）。
[HarmonyPatch]
internal static class WetnessNotePatch
{
    private static readonly MethodInfo HookMethod =
        AccessTools.Method(typeof(WetnessNotePatch), nameof(AppendWater));

    /// <summary>分隔符与原版一致：同一行内的小段之间用两个空格。</summary>
    private const string Separator = "  ";

    /// <summary>
    /// 把「含水：XX」接到这一行后面。含水为 0 就原样返回。
    /// 参数顺序由注入方式决定：栈上是 [text7, this]，所以签名必须是 (string, Thing)。
    /// </summary>
    internal static string AppendWater(string text, Thing t)
    {
        if (t == null)
        {
            return text;
        }

        int water = WetnessWater.GetNoteWater(t);
        if (water == 0)
        {
            return text;
        }

        string line = WetnessLang.WaterNote(water);
        return string.IsNullOrEmpty(text) ? line : text + Separator + line;
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(Thing), nameof(Thing.WriteNote))]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var list = new List<CodeInstruction>(instructions);

        int anchor = FindNutritionAnchor(list);
        if (anchor < 0)
        {
            Plugin.LogWarn("[Wetness] 找不到锚点 ldstr \"_nutrition\"，物品说明里的含水行已停用。");
            return list;
        }

        int addText = FindEqStatsAddText(list, anchor);
        if (addText < 0)
        {
            Plugin.LogWarn("[Wetness] 锚点之后没找到 ldstr \"NoteText_eqstats\"，含水行已停用。");
            return list;
        }

        int isEmpty = FindIsEmptyBefore(list, addText, anchor);
        if (isEmpty < 2)
        {
            Plugin.LogWarn("[Wetness] text7 判空调用没找到，含水行已停用。");
            return list;
        }

        CodeInstruction load = list[isEmpty - 1];
        CodeInstruction store = list[isEmpty - 2];
        if (!(load.operand is LocalBuilder slot) || !(store.operand is LocalBuilder storeSlot)
            || slot != storeSlot)
        {
            Plugin.LogWarn("[Wetness] 判空之前不是同一个局部变量的 stloc/ldloc 对，含水行已停用。");
            return list;
        }

        // 插在 `ldloc.s text7` 之前：ldloc(text7) / ldarg.0(this) / call hook / stloc(text7)
        // 顺序即执行顺序，不用倒着 Insert —— 这里用 InsertRange 一次放进去。
        var injected = new List<CodeInstruction>
        {
            new CodeInstruction(OpCodes.Ldloc_S, slot),
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Call, HookMethod),
            new CodeInstruction(OpCodes.Stloc_S, slot),
        };

        // 把「跳过 text7 收尾」的那几条 brfalse 从原来的 ldloc 挪到注入序列第一条，
        // 这样无论从哪条路过来，都会先过一遍含水拼接。
        injected[0].labels.AddRange(load.labels);
        load.labels.Clear();

        int at = isEmpty - 1;
        list.InsertRange(at, injected);

        Plugin.LogInfo($"[Wetness] 含水行已注入 Thing.WriteNote（+{injected.Count} 条指令，局部变量槽 {slot.LocalIndex}，锚点 +{at - anchor}）。");
        return list;
    }

    /// <summary>锚点 1：ldstr "_nutrition"。</summary>
    private static int FindNutritionAnchor(List<CodeInstruction> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].opcode == OpCodes.Ldstr && list[i].operand is string s && s == "_nutrition")
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>锚点 2：营养行之后、text7 最终交给 AddText 的那一处。</summary>
    private static int FindEqStatsAddText(List<CodeInstruction> list, int start)
    {
        for (int i = start; i < list.Count; i++)
        {
            if (list[i].opcode == OpCodes.Ldstr && list[i].operand is string s && s == "NoteText_eqstats")
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>AddText 之前最近的那次 text7 判空。</summary>
    private static int FindIsEmptyBefore(List<CodeInstruction> list, int addText, int anchor)
    {
        for (int i = addText - 1; i > anchor; i--)
        {
            if (list[i].opcode != OpCodes.Call && list[i].opcode != OpCodes.Callvirt)
            {
                continue;
            }
            if (!(list[i].operand is MethodInfo mi) || mi.Name != "IsEmpty")
            {
                continue;
            }
            ParameterInfo[] ps = mi.GetParameters();
            if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
            {
                return i;
            }
        }
        return -1;
    }
}
