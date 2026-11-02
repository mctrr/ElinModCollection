using HarmonyLib;

namespace Wetness.Patches;

/// <summary>
/// 水囊不许当配方材料 —— 尤其不许当「空瓶」。
///
/// 缘起：原版有拿空瓶当材料的配方（药水那一类）。水囊是 ThingV 变体，
/// `_origin = potion_empty`，而「这行是不是别的行的原型」这件事在判定材料时
/// 也被当成「它就是那个东西」：
///
///     // Props.ListThingStack 里的 Find(id)（材料候选列表的唯一来源）
///     bool isOrigin = EClass.sources.cards.map[id].isOrigin;
///     if (!t.isEquipped && (!(t.id != id) || (isOrigin && t.source._origin == id)))
///         TryAdd(t);                                  // ← 水囊以「空瓶」身份进来
///
/// 结果：配方里那个空瓶槽列出水囊、`Recipe.IsCraftable()` 因为水囊而返回 true，
/// 玩家一确认就把装着一肚子药水的水囊当成空瓶消耗掉了。
/// 这就是 `_origin` 的第五个副作用（前四个见 README「外观」一节）。
///
/// 三道口子，堵法各不相同：
///
/// 1. `Card.IsExcludeFromCraft` —— **主修**。它是游戏自己留的「这件东西不能当材料」
///    钩子，Props.ListThingStack 里**每一条**进候选列表的路（Find / FindCat /
///    FindAnyFood，以及各自那条「地盘上/阵营里的仓库」分支）最后都过
///    `TryAdd → !t.IsExcludeFromCraft(ing)` 这一关。返回 true 即可整条链子关掉，
///    不必去改那个带闭包的 `ListThingStack`。顺带把 `Recipe.IsCraftable()`
///    与界面上的材料计数（同一个来源）也一并修好。
///
/// 2. `Recipe.Ingredient.IsValidIngredient` —— 兜底。它是选材料时「回忆上次用了什么」
///    那条路的唯一校验（DropdownGrid 拿 player.recipes.lastIngredients 里的 uid
///    找回物件再问它一遍）。老存档里可能已经记着「这个配方上次用的是水囊」，
///    只堵 (1) 的话那条路仍会把水囊塞回去。
///
/// 3. `ThingContainer.GetThingStack` —— 显示层。配方按钮角上那个数字
///    （ButtonGrid.RefreshCraftable 的 subText）走的是另一套计数：它按 id 数，
///    同样把 `_origin == potion_empty` 的水囊算了进去。不改的话会出现
///    「写着有 1 个、点开却选不到」。
///
/// 水囊自己也走 `Recipe`（做它要锭），但那走的是**产物**那一侧，与这三个判定无关。
///
/// 第四个口子不在配方这条线上，但根因相同 —— 「水囊继承了空瓶的分类」：
/// 井的混合界面收 `t.category.IsChildOf("drink")` 的东西，水囊若真是 drink 类
/// 就会被拖进井里**直接销毁**（连囊里的药水一起）。它不是一次性的饮料，
/// 所以在这里一并挡掉。这条挡不挡得住由分类决定，启动自检会把分类打进日志
/// （[水囊自检] 分类 …），看那一行就知道是不是白写。
/// </summary>
// 类级 [HarmonyPatch] 必需：缺了它整个类会被 HarmonyX 静默跳过（见 WetnessDrinkPatch 注释）。
[HarmonyPatch]
internal static class WetnessNoIngredientPatch
{
    /// <summary>水囊不是材料。</summary>
    private static bool IsWaterskin(Card c)
        => c != null && c.id == WetnessDef.WaterskinId;

    /// <summary>
    /// 主修：把水囊标成「不可用作配方材料」。
    ///
    /// 只在原判为 false 时才动手 —— 原版那些「唯一物品 / 重要物品 / 商店货架上的」
    /// 判断照旧，我们只加一条自己的。
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Card), nameof(Card.IsExcludeFromCraft))]
    private static void IsExcludeFromCraftPostfix(Card __instance, ref bool __result)
    {
        if (!__result && IsWaterskin(__instance))
        {
            __result = true;
        }
    }

    /// <summary>
    /// 兜底：「上次用的是它」那条回忆路径（DropdownGrid）。
    /// 它与上面的候选列表是两套判据，只堵一处会留一个只有老存档才走得通的门。
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Recipe.Ingredient), nameof(Recipe.Ingredient.IsValidIngredient))]
    private static bool IsValidIngredientPrefix(Thing t, ref bool __result)
    {
        if (t != null && t.id == WetnessDef.WaterskinId)
        {
            __result = false;
            return false;
        }
        return true;
    }

    /// <summary>
    /// 显示层：问「我有几个空瓶」时，别把水囊算进去。
    ///
    /// 只在问的是空瓶（本 mod 借用的那一行）时动手；问别的 id 完全不碰。
    /// 递归调用共用同一个 ThingStack，每层都会执行这段，但按对象移除天然幂等
    /// （已移除的不在 list 里，不会重复扣），最外层最后执行，所以结果是对的。
    /// count / max 直接从 list 重算，不做增量减法。
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ThingContainer), nameof(ThingContainer.GetThingStack),
        new[] { typeof(string), typeof(ThingStack), typeof(bool), typeof(int) })]
    private static void GetThingStackPostfix(string id, ThingStack __result)
    {
        if (__result == null || id != WetnessDef.EmptyBottleId || __result.list == null)
        {
            return;
        }

        int removed = __result.list.RemoveAll(t => t != null && t.id == WetnessDef.WaterskinId);
        if (removed == 0)
        {
            return;
        }

        __result.count = 0;
        __result.max = 0;
        foreach (Thing t in __result.list)
        {
            if (t == null)
            {
                continue;
            }
            __result.count += t.Num;
            if (t.Num > __result.max)
            {
                __result.max = t.Num;
            }
        }
    }

    /// <summary>
    /// 水囊也不许「倒进井里」。
    ///
    /// `TraitWell.CanBlend` 的判据是 `t.category.IsChildOf("drink")`，
    /// 而水囊的分类是从空瓶继承来的 —— 空瓶是 `TraitDrink` 那一脉，
    /// 分类多半就是 `drink`。是的话，玩家在井的混合界面里能把水囊拖进去，
    /// `TraitWell.OnBlend` 会把它 `ModNum(-1)` 掉（连同囊里的药水）。
    ///
    /// 与配方那三条同一件事：**水囊看起来像空瓶/饮料，但它是个容器**。
    /// 分类万一不是 drink，这一条永远不会被走到，白写而已，不影响别的。
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TraitWell), nameof(TraitWell.CanBlend))]
    private static bool TraitWellCanBlendPrefix(Thing t, ref bool __result)
    {
        if (t != null && t.id == WetnessDef.WaterskinId)
        {
            __result = false;
            return false;
        }
        return true;
    }
}
