using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace Wetness;

/// <summary>
/// 含水表审计导出 —— 「水分怎么平衡」这件事的闭环工具。
///
/// 为什么要它：含水表现在是三张表（禁列表 / 物品点名 / 料理类别）说了算，
/// 但游戏里有 2587 件物品，光靠玩是看不到全貌的 ——
/// 你只会注意到「番茄怎么只补 1」这种碰巧吃到的。
///
/// 打开 `[Debug] DumpWaterAudit = true` 后启动游戏，会在 mod 目录写出
/// `water_audit.tsv`，每行一件物品：
///
///     water   **实际喝进去**补多少（0 = 不补水）
///     food    走「吃」这条路补多少（FoodEffect.Proc → GetFoodWater）
///     drink   走「喝」这条路补多少（FoodEffect.ProcDrink → GetDrinkGain）
///     well    井/ 喷水的量（TraitWell，不进上面两条）
///     verdict 这个数是谁定的，格式 `路径:来源`：
///             路径 food / drink / well
///             来源 deny-id / deny-cat / id / id-zero / category /
///                   effect（按 trait 参数解析出的 EffectId）/
///                   random（随机效果，值要等 roll 完才知道）/
///                   override（子类覆写 IdEffect）/
///                   none（什么都不命中 → 不补水）
///     id / category / nutrition / name_JP
///     trait   源表的 trait 列。`Drink` = 能喝（水囊收得进、倒得出）；
///             这一列顺便回答了「水银到底算不算 Drink」
///     origin  被别行当 _origin 引用的行。这标记有两个后果，注释在下面，
///             也正好是「按 id 重建会走样」的名单
///
/// 按 water 降序排好，于是「哪些东西补得比水还多」「哪些明明多汁却是 0」
/// 一眼可见，改数值只要动 WetnessWater 里那几张表。营养列留着做参照 ——
/// 它正是被废掉的那个旧判据，看一眼就知道它当年错在哪。
///
/// ---- 为什么必须分 food / drink / well 三列 ----
/// v0.13.12 之前只有一列 water，走的是 FoodWater(id, category)，
/// 于是**所有饮料都被记成 0** —— 74 件（汽水/ 牛奶 / 酒 / 清水 / 药水…），
/// 而它们实际补 10~35 点。一张用来平衡的表把主力的补水来源全写成 0，
/// 比没有表更糟：照着它调数值，会得出「原来游戏里只有吃的补水」的错误结论。
///
/// 根因是 `FoodWater` 只管吃：喝的走 `WetnessWater.GetDrinkGain(EffectId)`，
/// 井水走 `GetWellGain`，两者都不经过类别 / 点名表。
/// 现在三列分开算，`water` 取三者最大 —— 这才是玩家喝进去的真实数字。
/// 纯离线判定（不造 Thing）：drink 列靠**解析源表的 trait 字符串**，
/// 镜像的是 `TraitDrink.IdEffect => (GetParam(1) ?? "Drink").ToEnum<EffectId>()`。
/// 覆写 IdEffect 的三个子类（DrinkMilk / PotionRandom / RodRandom）单列处理，
/// 见 DrinkWaterOf。
/// </summary>
internal static class WaterAudit
{
    /// <summary>上次导出的明细行数，给配置页的状态行用。</summary>
    internal static int LastDumpRows { get; private set; }

    /// <summary>上次导出明细的时间（HH:mm:ss），没导过就是空串。</summary>
    internal static string LastDumpAt { get; private set; } = "";

    /// <summary>上次导出类别展开的时间（HH:mm:ss），没导过就是空串。</summary>
    internal static string LastCatDumpAt { get; private set; } = "";

    /// <summary>上次导出类别展开时的类别数。</summary>
    internal static int LastCatDumpCats { get; private set; }

    /// <summary>源表里的物品件数 / 用到的类别数，给配置页的状态行用。</summary>
    internal static int ThingCount { get; private set; }

    internal static int CategoryCount { get; private set; }

    public static void Dump()
    {
        try
        {
            List<string[]> rows = new List<string[]>();
            HashSet<string> cats = new HashSet<string>();
            foreach (SourceThing.Row r in EClass.sources.things.rows)
            {
                SourceCategory.Row cat = Category(r.category);
                if (!string.IsNullOrEmpty(r.category))
                {
                    cats.Add(r.category);
                }

                string trait = r.trait == null || r.trait.Length == 0 ? "" : string.Join(",", r.trait);
                int food = WetnessWater.FoodWater(r.id, cat);
                int drink = DrinkWaterOf(r.id, r.trait, out string drinkVerdict);
                int well = WellWaterOf(r.trait, out string wellVerdict);

                // water = 三条路径取最大。走哪条由游戏自己决定（吃/喝/打井），
                // 这里只回答「这东西最多能补多少」，也就是排行该按什么排。
                int water = Max3(food, drink, well);
                string verdict = water == 0
                    ? "none"
                    : VerdictOf(water, food, drink, well, drinkVerdict, wellVerdict,
                        WetnessWater.Verdict(r.id, cat));

                rows.Add(new string[]
                {
                    water.ToString(),
                    food.ToString(),
                    drink.ToString(),
                    well.ToString(),
                    verdict,
                    r.id ?? "",
                    r.category ?? "",
                    Nutrition(r).ToString(),
                    trait,
                    // 被别行当 _origin 引用的行。这标记有两个后果：
                    // RecipeManager.BuildList 跳过它；ThingGen._Create 会把创建请求
                    // 重定向到它的「变体」—— 所以这一列同时是「按 id 重建会走样」的名单。
                    r.isOrigin ? "origin" : "",
                    r.name_JP ?? r.name ?? "",
                });
            }

            // 水分降序、同分按 id —— 表头往下就是「谁最补水」的排行榜。
            rows.Sort(delegate (string[] a, string[] b)
            {
                int byWater = int.Parse(b[0]).CompareTo(int.Parse(a[0]));
                // id 在第 5 列（water/food/drink/well/verdict 之后）。
                return byWater != 0 ? byWater : string.CompareOrdinal(a[5], b[5]);
            });

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("water\tfood\tdrink\twell\tverdict\tid\tcategory\tnutrition\ttrait\torigin\tname_JP");
            foreach (string[] row in rows)
            {
                sb.AppendLine(string.Join("\t", row));
            }

            string path = OutputPath("water_audit.tsv");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));

            LastDumpRows = rows.Count;
            ThingCount = rows.Count;
            CategoryCount = cats.Count;
            LastDumpAt = DateTime.Now.ToString("HH:mm:ss");
            Plugin.LogInfo($"[Wetness] 含水审计已导出：{path}（{rows.Count} 行 / {cats.Count} 个类别）");
        }
        catch (Exception e)
        {
            Plugin.LogError("[Wetness] 含水审计导出失败：" + e);
        }
    }

    /// <summary>
    /// 按类别展开的那份 —— 明细表能回答「番茄补多少」，这份回答
    /// 「这一类整体怎么定的、里面有没有漏网的」。
    ///
    /// 形态是每类一节的汇总 + 只对「有补水或命中禁列表」的类别给逐件明细：
    /// 121 个类别全展开会有 2800 多行，其中绝大多数是「这一类全是 0」，
    /// 那部分压成一行反而更看得清问题在哪。
    ///
    /// 与 <see cref="Dump"/> 同源：food / drink / well 三列分别算，
    /// water 取三者最大。少了 drink 那列，这张表会把booze / milk / _drink
    /// 三个类别整体报成「补水 0」—— 而它们恰恰是最强的水源。
    /// </summary>
    public static void DumpByCategory()
    {
        try
        {
            // 类别 id → 该类下的物品（按补水量降序）
            Dictionary<string, List<Item>> byCat = new Dictionary<string, List<Item>>(StringComparer.Ordinal);
            foreach (SourceThing.Row r in EClass.sources.things.rows)
            {
                SourceCategory.Row cat = Category(r.category);
                string key = string.IsNullOrEmpty(r.category) ? "(无类别)" : r.category;
                if (!byCat.TryGetValue(key, out List<Item> list))
                {
                    list = new List<Item>();
                    byCat[key] = list;
                }

                int food = WetnessWater.FoodWater(r.id, cat);
                int drink = DrinkWaterOf(r.id, r.trait, out string drinkVerdict);
                int well = WellWaterOf(r.trait, out string wellVerdict);
                int water = Max3(food, drink, well);

                list.Add(new Item
                {
                    Id = r.id ?? "",
                    Name = r.name_JP ?? r.name ?? r.id ?? "",
                    Water = water,
                    Food = food,
                    Drink = drink,
                    Well = well,
                    Verdict = water == 0
                        ? "none"
                        : VerdictOf(water, food, drink, well, drinkVerdict, wellVerdict,
                            WetnessWater.Verdict(r.id, cat)),
                    CategoryName = cat == null ? "" : CatName(cat),
                });
            }

            foreach (List<Item> list in byCat.Values)
            {
                list.Sort(delegate (Item a, Item b)
                {
                    int byWater = b.Water.CompareTo(a.Water);
                    return byWater != 0 ? byWater : string.CompareOrdinal(a.Id, b.Id);
                });
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# 含水判定 · 按类别展开");
            sb.AppendLine();
            sb.AppendLine($"> 由游戏内 mod 设置页的「导出类别展开」按钮生成，"
                + $"{byCat.Count} 个类别 / {ThingCountOr(byCat)} 件。");
            sb.AppendLine("> 同名文件在 `Package/Mod_Wetness/` 下，可以直接用编辑器打开。");
            sb.AppendLine();
            sb.AppendLine("补水走**三条独立的路**，本表的 `水` 列取三者的最大值：");
            sb.AppendLine();
            sb.AppendLine("| 列 | 谁走的 | 判定依据 |");
            sb.AppendLine("|---|---|---|");
            sb.AppendLine("| `吃` | `FoodEffect.Proc` | 点名表 / 料理类别 / 禁列表 |");
            sb.AppendLine("| `喝` | `FoodEffect.ProcDrink` | `TraitDrink.IdEffect`，由源表 trait 参数解析 |");
            sb.AppendLine("| `井` | `TraitWell` 自建动作 | 按圣井 / 普通 / 污染分档（污染状态离线不可知，此处按未污染） |");
            sb.AppendLine();
            sb.AppendLine("`判定` 列的格式是 `路径:来源`：");
            sb.AppendLine();
            sb.AppendLine("| 值 | 谁定的 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| `food:id` | 物品点名表（`WetnessWater.ById`）给了非零值 |");
            sb.AppendLine("| `food:id-zero` | 点名表里显式写了 0，压过类别 |");
            sb.AppendLine("| `food:category` | 类别表定的（料理做法 / `fruit` 整类） |");
            sb.AppendLine("| `food:deny-id` / `food:deny-cat` | 禁列表（食物侧） |");
            sb.AppendLine("| `drink:effect` | 按 trait 参数解析出的 `EffectId`（水 / 奶 / 酒 / 药水…） |");
            sb.AppendLine("| `drink:override` | 子类覆写了 `IdEffect`（`DrinkMilk` 系列恒为牛奶档） |");
            sb.AppendLine("| `drink:random` | 随机效果药水（`PotionRandom` 等），值要roll 完才知道 |");
            sb.AppendLine("| `drink:deny-id` | 禁列表（饮品侧：水银、耐酸液、堕天使之血） |");
            sb.AppendLine("| `well:holy` / `well:normal` | 圣井 / 普通井 |");
            sb.AppendLine("| `none` | 什么都不命中 → 不补水 |");
            sb.AppendLine();
            sb.AppendLine("## 全部类别一览");
            sb.AppendLine();
            sb.AppendLine("| 类别 | id | 件数 | 补水 | 补水率 | 走哪条路 | 水量分布 | 判定来源 |");
            sb.AppendLine("|---|---|---:|---:|---:|---|---|---|");

            List<string> cats = new List<string>(byCat.Keys);
            cats.Sort(delegate (string a, string b)
            {
                int byCount = byCat[b].Count.CompareTo(byCat[a].Count);
                return byCount != 0 ? byCount : string.CompareOrdinal(a, b);
            });

            foreach (string key in cats)
            {
                List<Item> list = byCat[key];
                int wet = 0;
                int byFood = 0;
                int byDrink = 0;
                int byWell = 0;
                SortedDictionary<int, int> dist = new SortedDictionary<int, int>();
                SortedDictionary<string, int> verdicts = new SortedDictionary<string, int>(StringComparer.Ordinal);
                foreach (Item it in list)
                {
                    if (it.Water > 0)
                    {
                        wet++;
                    }
                    // 三条路各算一次「这一类里有几件走它」—— 一件东西只会真的走一条，
                    // 但表里三条都可能非 0（既是水果又是饮料），所以这里不排他。
                    if (it.Food > 0)
                    {
                        byFood++;
                    }
                    if (it.Drink > 0)
                    {
                        byDrink++;
                    }
                    if (it.Well > 0)
                    {
                        byWell++;
                    }
                    if (!dist.ContainsKey(it.Water))
                    {
                        dist[it.Water] = 0;
                    }
                    dist[it.Water]++;
                    if (!verdicts.ContainsKey(it.Verdict))
                    {
                        verdicts[it.Verdict] = 0;
                    }
                    verdicts[it.Verdict]++;
                }

                SourceCategory.Row row = Category(key);
                sb.Append("| ").Append(CatName(row)).Append(" | `").Append(key).Append("` | ")
                    .Append(list.Count).Append(" | ").Append(wet).Append(" | ")
                    .Append(list.Count == 0 ? "—" : (wet * 100f / list.Count).ToString("0") + "%").Append(" | ")
                    .Append(Paths(byFood, byDrink, byWell)).Append(" | ")
                    .Append(Dist(dist)).Append(" | ").Append(Verdicts(verdicts)).AppendLine(" |");
            }

            sb.AppendLine();
            sb.AppendLine("## 有内容的类别（逐件明细）");
            sb.AppendLine();
            sb.AppendLine("只剩「全部不补水且没命中任何规则」的类别没展开 —— 它们在上面的表里是整齐的 0。");

            foreach (string key in cats)
            {
                List<Item> list = byCat[key];
                if (!NeedsDetail(list))
                {
                    continue;
                }

                SourceCategory.Row row = Category(key);
                sb.AppendLine();
                sb.Append("### ").Append(CatName(row)).Append("  `").Append(key).AppendLine("`");
                sb.AppendLine();
                sb.AppendLine("| 名称 | id | 水 | 吃 | 喝 | 井 | 判定 |");
                sb.AppendLine("|---|---|---:|---:|---:|---:|---|");
                foreach (Item it in list)
                {
                    sb.Append("| ").Append(it.Name).Append(" | `").Append(it.Id).Append("` | ")
                        .Append(it.Water).Append(" | ")
                        .Append(it.Food).Append(" | ").Append(it.Drink).Append(" | ")
                        .Append(it.Well).Append(" | ").Append(it.Verdict).AppendLine(" |");
                }
            }

            string path = OutputPath("water_by_category.md");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            LastCatDumpAt = DateTime.Now.ToString("HH:mm:ss");
            LastCatDumpCats = byCat.Count;
            Plugin.LogInfo($"[Wetness] 类别展开已导出：{path}（{byCat.Count} 个类别）");
        }
        catch (Exception e)
        {
            Plugin.LogError("[Wetness] 类别展开导出失败：" + e);
        }
    }

    private sealed class Item
    {
        internal string Id;
        internal string Name;
        internal int Water;
        internal int Food;
        internal int Drink;
        internal int Well;
        internal string Verdict;
        internal string CategoryName;
    }

    // ================= 三条路径的离线判定 =================
    //
    // 「吃」的那条已经有纯函数了（WetnessWater.FoodWater，只依赖 id 与类别）。
    // 「喝」与「井」没有 —— 原版是拿运行时的 Trait 对象算的：
    //
    //     TraitDrink.IdEffect => (GetParam(1) ?? "Drink").ToEnum<EffectId>()
    //     Trait.Params        => owner.sourceCard.trait   ← 就是源表第 33 列
    //
    // 也就是说，**离线可以从源表的 trait 字符串复刻出同一个 IdEffect**，
    // 不必造出 2824 个 Thing。下面两段就是在做这件事，
 	// 判据全部对着 .game-src 里的真实实现抄，改游戏版本时以源码为准。

    /// <summary>
    /// 「喝一口补多少」——离线复刻 <c>WetnessWater.GetDrinkGain</c>。
    ///
    /// 三个要点，缺一个就会算错：
    ///
    /// 1. **只有 TraitDrink 及其子类才算喝。** 原版 <c>Trait.CanDrink</c> 基类返回
    ///    false，<c>TraitDrink.CanDrink</c> 才返回 true。所以源表 trait 列第一段
    ///    必须在 Drink 系里 —— Food / PotionEmpty / Perfume 之外的杂类都不算。
    ///    这条判错的后果最隐蔽：把「雪」（Snow）算成能喝，给它白送 10 点水。
    ///
    /// 2. **子类会覆写 IdEffect**，这时 trait 参数不参与：
    ///    - DrinkMilk / DrinkMilkMother →硬编码 DrinkMilk
    ///    - PotionRandom / PotionAlchemy / Perfume → 读 <c>source.proc[0]</c>，
    ///      那是**roll 完之后**才有的元素效果，离线不可知→ 报 random
    ///
    /// 3. **禁列表在这里也生效**（与 GetDrinkGain(thingId, id) 同源）：
    ///    水银、耐酸保护液、堕天使之血都带 TraitDrink，不挡就会按「药水 = 10」白送。
    /// </summary>
    private static int DrinkWaterOf(string id, string[] trait, out string verdict)
    {
        verdict = "none";
        if (trait == null || trait.Length == 0)
        {
            return 0;
        }

        string name = trait[0];

        // 要点 1：trait 列第一段必须是 Drink 系。
        if (!IsDrinkTraitName(name))
        {
            return 0;
        }

        // 空瓶 / 空桶：带 TraitDrink，但 CanDrink 被覆写成 false，喝不了。
        // 它在 GetNoteWater 里也被判掉（`drink.CanDrink(pc)` 为假），
        // 所以这里给 0 才是与说明页一致的那个数。
        if (name == "PotionEmpty")
        {
            verdict = "none";
            return 0;
        }

        // 禁列表优先于一切（要点 3）。
        if (WetnessWater.IsDeniedId(id))
        {
            verdict = "drink:deny-id";
            return 0;
        }

        // 要点 2：覆写 IdEffect 的子类。
        if (name == "DrinkMilk" || name == "DrinkMilkMother")
        {
            verdict = "drink:override";
            return WetnessWater.MilkGain;
        }
        if (name == "PotionRandom" || name == "PotionAlchemy" || name == "Perfume")
        {
            // IdEffect 来自 roll 出来的元素（source.proc[0]），离线算不出来。
            // 按最坏情况给 0 会让这 3 件从排行里消失，按 PotionGain 给又会编一个数。
            // 这里报 random 并给 0 —— 判定来源本身就说明了「得进游戏看」。
            verdict = "drink:random";
            return 0;
        }

        // 要点 1+3 之外的正路：(GetParam(1) ?? "Drink").ToEnum<EffectId>()
        EffectId effect = trait.Length > 1 && !string.IsNullOrEmpty(trait[1])
            ? ParseEffectId(trait[1])
            : EffectId.Drink;
        verdict = "drink:effect";
        return WetnessWater.GetDrinkGain(effect);
    }

    /// <summary>
    /// trait 列第一段在不在「能喝」的那一族里。
    ///
    /// 用白名单而不是 `Type.GetType("Trait" + name)` 的原因：那需要真的反射查类型，
    /// 而白名单是**从 .game-src 全量枚举出来的**，改了游戏版本这里就是一处显眼的改动点。
    /// 判定标准 = 该类继承 TraitDrink 且没把 CanDrink 覆写回 false。
    ///
    /// 逐条依据（类名去 .game-src 核对过继承链）：
    ///   Drink / DrinkMilk / DrinkMilkMother / Potion / PotionLove /
    ///   PotionRandom / PotionAlchemy / Perfume / PotionEmpty
    /// 后者 CanDrink=false，所以单独在 DrinkWaterOf 里剔掉，不算「能喝」。
    /// </summary>
    private static bool IsDrinkTraitName(string name)
    {
        switch (name)
        {
            case "Drink":
            case "DrinkMilk":
            case "DrinkMilkMother":
            case "Potion":
            case "PotionLove":
            case "PotionRandom":
            case "PotionAlchemy":
            case "Perfume":
            case "PotionEmpty":
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// trait 参数 → EffectId。
    ///
    /// 对应原版 <c>string.ToEnum&lt;EffectId&gt;()</c>：认不出来的字符串返回
    /// <see cref="EffectId.None"/>（枚举里value 0 就是 None）。这正是我们要的行为 ——
    /// 一个写了错别字的效果名会落到 None → 走 default 分支 → 按「药水 = 10」，
    /// 与游戏实际行为一致。
    /// </summary>
    private static EffectId ParseEffectId(string s)
    {
        switch (s)
        {
            case "DrinkWater": return EffectId.DrinkWater;
            case "DrinkWaterDirty": return EffectId.DrinkWaterDirty;
            case "DrinkRamune": return EffectId.DrinkRamune;
            case "DrinkMilk": return EffectId.DrinkMilk;
            case "Booze": return EffectId.Booze;
            case "SaltWater": return EffectId.SaltWater;
            default: return EffectId.None;
        }
    }

    /// <summary>
    /// 井 / 喷水补多少 —— 离线按「未污染的普通井」算。
    ///
    /// 为什么这么取：<c>TraitWell.polluted</c> 是 <c>owner.GetInt(26)</c>，
    /// 读的是**存档里那口井的实时状态**，源表里没有。一口井可能开局干净、
    /// 第二天被污染，同一个 id 的答案会变 —— 所以离线只能给基准值，
    /// 真值以游戏里说明页显示的为准（GetNoteWater 那条路读的是真实 trait）。
    ///
    /// 圣井认TraitWellHoly（原版 <c>IsHoly => this is TraitWellHoly</c>）。
    /// 井水不经过 ProcDrink，所以这一列独立于 drink。
    /// </summary>
    private static int WellWaterOf(string[] trait, out string verdict)
    {
        verdict = "none";
        if (trait == null || trait.Length == 0)
        {
            return 0;
        }

        if (trait[0] == "WellHoly")
        {
            verdict = "well:holy";
            return WetnessWater.GetWellGain(true, false);
        }
        if (trait[0] == "Well")
        {
            verdict = "well:normal";
            // 按未污染算 —— 污染井在游戏里是 12（脏水档）。
            return WetnessWater.GetWellGain(false, false);
        }
        return 0;
    }

    private static int Max3(int a, int b, int c)
    {
        int m = a > b ? a : b;
        return m > c ? m : c;
    }

    /// <summary>
    /// 拼出「路径:来源」形式的判定说明。
    ///
    /// 三条路径谁的数最大就报谁 —— 这与 <c>water</c> 列取max 的口径一致，
    /// 免得出现「water 写着 35、verdict 却说 category:0」这种自相矛盾的行。
    /// </summary>
    private static string VerdictOf(int water, int food, int drink, int well,
        string drinkVerdict, string wellVerdict, string foodVerdict)
    {
        if (water <= 0)
        {
            return "none";
        }
        if (well == water)
        {
            return wellVerdict;
        }
        if (drink == water)
        {
            return drinkVerdict;
        }
        return "food:" + foodVerdict;
    }

    /// <summary>这一类有没有值得逐件看的理由。</summary>
    private static bool NeedsDetail(List<Item> list)
    {
        foreach (Item it in list)
        {
            if (it.Water > 0 || it.Verdict != "none")
            {
                return true;
            }
        }
        return false;
    }

    private static string Dist(SortedDictionary<int, int> dist)
    {
        List<string> parts = new List<string>();
        foreach (KeyValuePair<int, int> kv in dist)
        {
            parts.Add(kv.Key == 0 ? $"0×{kv.Value}" : $"{kv.Key}×{kv.Value}");
        }
        return string.Join(" / ", parts);
    }

    /// <summary>「走哪条路」那一列：吃 / 喝 / 井 各有几件，三条都为 0 就写个「—」。</summary>
    private static string Paths(int food, int drink, int well)
    {
        if (food == 0 && drink == 0 && well == 0)
        {
            return "—";
        }
        List<string> parts = new List<string>();
        if (food > 0)
        {
            parts.Add($"吃×{food}");
        }
        if (drink > 0)
        {
            parts.Add($"喝×{drink}");
        }
        if (well > 0)
        {
            parts.Add($"井×{well}");
        }
        return string.Join(" / ", parts);
    }

    private static string Verdicts(SortedDictionary<string, int> map)
    {
        List<string> parts = new List<string>();
        foreach (KeyValuePair<string, int> kv in map)
        {
            parts.Add($"{kv.Key}×{kv.Value}");
        }
        return string.Join(" / ", parts);
    }

    private static int ThingCountOr(Dictionary<string, List<Item>> byCat)
    {
        int n = 0;
        foreach (List<Item> list in byCat.Values)
        {
            n += list.Count;
        }
        return n;
    }

    /// <summary>类别显示名。`GetName()` 走游戏自己的本地化，取不到就退回 id。</summary>
    private static string CatName(SourceCategory.Row row)
    {
        if (row == null)
        {
            return "(未知)";
        }
        try
        {
            string n = row.GetName();
            if (!string.IsNullOrEmpty(n) && n != row.id)
            {
                return n + " / " + row.id;
            }
        }
        catch (Exception)
        {
            // GetName 依赖 lang 表，标题界面那种时机可能还没好，退回 id 即可。
        }
        return row.id;
    }

    /// <summary>导出目录 = mod 自己的目录（与原版「打开 mod 文件夹」按钮一致）。</summary>
    internal static string OutputDir()
        => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

    private static string OutputPath(string fileName)
        => Path.Combine(OutputDir(), fileName);

    private static SourceCategory.Row Category(string id)
    {
        if (id == null)
        {
            return null;
        }
        return EClass.sources.categories.map.TryGetValue(id, out SourceCategory.Row row) ? row : null;
    }

    /// <summary>
    /// 营养 = 元素 10。SourceFood.OnImportRow 里 `Add(10, row.NUT)` 把它塞进
    /// SourceThing.Row.elements，所以读源表行就能拿到，不必造 Thing。
    /// </summary>
    private static int Nutrition(SourceThing.Row r)
    {
        int[] elements = r.elements;
        if (elements == null)
        {
            return 0;
        }
        for (int i = 0; i + 1 < elements.Length; i += 2)
        {
            if (elements[i] == 10)
            {
                return elements[i + 1];
            }
        }
        return 0;
    }
}
