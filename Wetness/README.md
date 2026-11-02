# 湿润度 (Wetness)

模仿 Elona 的「湿润度」状态，为 Elin 补上一条与饥饿并列的生存机制。

- 工程目录：`D:\work\Elin\5\Wetness`
- 包 id：`mctrr.wetness`
- 构建产物：`<Elin>/Package/Mod_Wetness/`

---

## 当前进度

**第一步**：把湿润度接进 Elin 原生的状态体系。

- 七档分级与数值存储（借用 `Chara._cints` 的空闲槽位 26）
- 每回合自然衰减
- 状态栏显示（只在极端档位露面）

**第二步**：让它真的影响玩法。

- 饮用补水，沿用 Elin 自带的饮品分类
- 偏湿时禁止「饮用」，但「使用」与「投掷」不受限
- 呕吐带走水分
- 档位变化时给出提示

**第三步（v0.3.0）**：把收益/惩罚落到原版的惯例位置上。

- 偏干 → 速度惩罚，挂在 `Chara.RefreshSpeed`，与 burden / stamina / sleepiness 并列
- 湿润 → MP 回复 +10%
- SP 相关逻辑**全部拆掉** —— 湿润度不再碰 SP

**第四步（v0.4.0）**：对齐原版的口径。

- 档位边界改回原版的十等分切法（干侧挤低端、湿侧挤高端）
- 速度惩罚数值照抄原版 hunger（−10 / −10 / −30）
- MP 加成从「所有 MP 获取 +10%」收窄到**只在自然回复那一句上生效**
- 加成范围从「仅湿润档」放宽到「湿润及以上」

**第五步（v0.5.0）**：下雨天暂停消耗。

- 露天淋雨时自然衰减完全暂停；室内 / 屋顶下 / 洞里照常消耗
- 判定条件直接复用原版给 `ConWet` 的那一处，不另立定义
- 沙漠白天加速**不做** —— 当前版本的游戏里没有沙漠地形，已把预留常量删掉

**第六步（v0.6.0）**：水囊道具 + LangMod 源表。

- 新增物品 `wetness_waterskin`，走 `ThingV` 一行数据即可，**外观继承空瓶**
- 可以把喝得下的药水倒进去，最多三份，**同时间只能装一种**（不能混装）
- 「使用」它 = 喝下一份药水，效果与原版喝药水完全一致
- 它走「使用」而不是「饮用」，所以**偏湿时仍能靠它补水** —— 这是禁饮规则唯一的后门
- 顺带把 mod 的源表挂上 `LangMod/CN|EN`，物品名与描述本地化

**第七步（v0.7.0）**：水囊的容量与禁忌。

- 配方改成以**锭**为主材 —— 成品材质继承那块锭，容量随之变化（`2 + 硬度 / 15`，夹 2～10）
- 附过魔的水囊再多装一口
- 以太抗体药水倒不进去（静默拒绝，不弹提示）
- 配方挂在制作台上，`recipeKey = *`，一开始就会做

**第八步（本版 v0.8.0）**：混入 / 倒回。

- 水囊挂上原版的 `Trait.IsBlendBase`，于是「混入」界面直接可用（和井、染料同一套机制）
- 拖药水进去 = 收进来；拖空瓶进去 = 把囊里的东西倒回去重新灌成一瓶（**原版没有的功能**）
- 为此在入囊时记下原物的 `id` 与 `refVal`，倒出时按它重建
- 容量系数按真实材质硬度谱定稿（`HardnessPerDose = 15`），原先那段启动自检日志随之删除

**第九步（v0.9.0）**：第一轮实机反馈的修复。

- 衰减逐字照抄原版 hunger（`turn % 50 == 1 && rnd(3) == 0`，平均 150 回合 1 点），
  不加任何自创速率；脱水症照抄原版最饿档的形状（后来在 v0.11.0 里从掉血改成扣魔力）
- 状态栏 Tip 与颜色回到原版口径：给自己补一行 `SourceStat.Row`（`NotificationStats.Interactable`
  要求 `GetDetail()` 非空才让鼠标响应），颜色交给基类按 value/max 取
- 排序交回基类（`source.id`），修掉「buff 图标排在脱水症上面」
- 速度面板补上扣分项；水囊描述收成一句话；倒入时的药水名改成在消耗之前抓

**第十步（本版 v0.10.0）**：含水表 —— 吃东西也能补水。

- 新增 `WetnessWater`：**含水 = 营养 ÷ K**（多汁 10 / 其余 20），
  自动继承原版料理的营养折扣（`nutFactor`），不用拦制作过程
- 带汤水的料理另给一笔兜底：汤 +15、面 +5、米 +5、蔬菜料理 +4、果物料理 +4、蛋 +2
  —— 判据来自原版自己给失败料理起的名字（`fail_drink`「汁」/ `fail_noodle`「泡软的面」/
  `fail_vegi`「湿黏」/ `fail_fruit`「糊状」），只有这四类被原版形容成湿软
- 药水固定 10；饮水表（清水 35 / 牛奶 25 / 汽水 25 / 酒 15 / 脏水 12 / 盐水 −10）不变
- **吃**挂 `FoodEffect.Proc`、**喝**挂 `ProcDrink`、水囊内容物 —— 三处查同一张表，
  修掉「同一瓶药水，直接喝不补水、倒进水囊再喝就补」的不一致
- 物品说明里「营养：11」后面接一行「含水：XX」（0 不显示），
  药水这类没有营养的则单独一行 —— 靠注入 `Thing.WriteNote` 实现

**第十一步（v0.11.0）**：脱水改成扣魔力。

- 脱水症不再 `DamageHP`，改成每回合 1/5 概率扣 `1 + rnd(2) + 最大MP/50`，
  并夹在 0 为止（扣成负数会触发原版「魔力逆流」，掉血绕回来）
- 惩罚线就此分工：饥饿管血与体力，口渴管魔力与速度
- 状态栏 Tip 与档位提示同步改口径

**v0.11.1**：`_cints` 槽位从 22–25 挪到 26–29（数组末尾，离原版占用的 21 最远），
并顺手把旧槽位里的残留数据清掉。

**第十二步（本版 v0.12.0）**：含水表推翻重做 —— 不再按营养算。

- 「含水 = 营养 ÷ K」整条删掉：营养是料不是水，番茄被算成 1、玉米反而更多，
  面包面粉尸体水银也跟着蹭水分
- 改成 **禁列表 → 物品点名 → 类别 → 0** 四级判定，加一道 **低于 5 归零** 的闸门
- 生鲜水果整类 5，料理按做法分（汤 15 / 果物料理 8 / 菜 6 / 面 6 / 米 5，其余 0）
- 尸体、水银、耐酸保护液、堕天使之血、面包、面粉一律不补水；
  水银那类带 `TraitDrink`，必须在饮水侧也挡（否则按「药水 = 10」白送）
- 「湿润」档进状态栏：**禁饮就是从这一档开始的，看不见等于让玩家猜**
- 新增 `[Debug] DumpWaterAudit`：把全部物品的含水判定导成 `water_audit.tsv`，
  见「水分怎么平衡（方法）」

**v0.12.1**：删掉 v0.12.0 临时加的「体内水分已经到顶了」—— 原版没有对应文本，
而它能触发的两条路（投掷 / 水囊）各自已经有提示，加进去只会多一段。

**v0.13.12**：修掉审计表的一个真缺陷 —— **所有饮料原本都被记成 0**。

0.13.11 把类别展开搬进游戏内之后，第一次实机导出就暴露了问题：
`water_audit.tsv` 的 `water` 列走的是 `WetnessWater.FoodWater(id, category)`，
而**那只是「吃」这一条路**。喝的走 `GetDrinkGain(EffectId)`、井水走 `GetWellGain`，
两者都不经过点名表与类别表 —— 于是：

| 类别 | 旧表显示 | 实际 |
|---|---|---|
| `booze`（23 件） | 补水 0 / 0% / 全是 `none` | 20 件补 15（酒）、1 件补 20（汽水） |
| `milk`（14 件） | 补水 0 / 0% | 全部补 20 |
| `_drink`（35 件） | 补水 0 / 0% | 清水 3 件补 35、药水 30 件补 10、脏水 12、海水 −10 |
| `drug`（11 件） | 补水 0 | 2 件能喝的药各补 10 |

合计 **65 件饮料 + 5 处井/喷水** 被写成了 0。一张用来平衡的表把补水的主力来源
全写成 0，比没有表更糟 —— 照着它调数值会得出「这游戏里只有吃的补水」的错误结论，
甚至可能把汽水牛奶当成没用的东西去乱改数值。

修法是**按三条路径分开算**，`water` 列取三者最大：

| 列 | 挂在哪 | 判定依据 |
|---|---|---|
| `吃` | `FoodEffect.Proc` | 点名表 / 料理类别 / 禁列表（原有逻辑，没动） |
| `喝` | `FoodEffect.ProcDrink` | `TraitDrink.IdEffect` |
| `井` | `TraitWell` 自建动作 | 圣井 / 普通 / 污染三档 |

`verdict` 列的格式随之改成 `路径:来源`，例如 `drink:effect`、`food:id-zero`、
`well:holy`、`drink:override`，这样「这个数是谁定的」不会因为取 max 而说错话
（旧版会出现 `water` 写着 35、`verdict` 却是 `category:0` 这种自相矛盾的行）。

「喝」这一列是**离线**算出来的，靠解析源表的 trait 字符串复刻原版那行：

```csharp
TraitDrink.IdEffect => (GetParam(1) ?? "Drink").ToEnum<EffectId>();
Trait.Params        => owner.sourceCard.trait   ← 就是源表第 33 列
```

判据全部对着 `.game-src/` 抄的，三个容易踩的点：

- **只有 Drink 系才算能喝。** 原版 `Trait.CanDrink` 基类返回 `false`，
  `TraitDrink.CanDrink` 才返回 `true`。判错的后果最隐蔽——「雪」（`Snow`）
  会被算成能喝并白送 10 点水。`LumiestCrimAle`（Crim Ale）同样是 `CanDrink`
  但不继承 `TraitDrink`，判定为 0。
- **子类会覆写 `IdEffect`**：牛奶系（`DrinkMilk`/`DrinkMilkMother`）恒为牛奶档 20；
  随机效果药水（`PotionRandom`/`PotionAlchemy`/`Perfume`）读 `source.proc[0]`，
  那是 roll 完才有的元素效果，**离线算不出来**——标 `drink:random` 并给 0，
  不编一个看似精确的假数。
- **禁列表在饮品侧同样生效**：水银 / 耐酸液/ 堕天使之血都带 `TraitDrink`，
  不挡就会按「药水 = 10」白送水分（标 `drink:deny-id`）。
  另外 `PotionEmpty`（空瓶 / 空桶）虽然继承 `TraitDrink`，但 `CanDrink`
  被覆写成 `false`，给 0 才与说明页一致。

井水的 `polluted` 是存档里那口井的**实时状态**，源表里没有，所以 `井` 列
按「未污染」给基准值（真值以游戏内说明页为准）。

**v0.13.11**：2587 件的类别展开搬进游戏内 —— 模组配置页底部多了一块「含水表导出」。

原来的做法是改 `BepInEx/config/mctrr.wetness.cfg` 里的 `[Debug] DumpWaterAudit = true`
再重启游戏导出，平衡一次要重启两轮。现在直接在**模组列表 → Wetness → 配置**
底部点按钮，导出物落在 `Package/Mod_Wetness/`：

| 按钮 | 导出物 | 回答什么问题 |
|---|---|---|
| 导出明细 `water_audit.tsv` | 每件一行，按补水量降序 | 谁补得比水还多、哪些明明多汁却是 0 |
| 导出类别展开 `water_by_category.md` | 每类一节汇总 + 逐件明细 | 某一类整体怎么定的、有没有漏网的 |
| 打开 mod 目录 | — | 懒得找路径 |

> 两份表都按**吃 / 喝 / 井三条路**分别算，`水` 列取三者最大值（v0.13.12 起）。
> 早先只有「吃」一条，饮料会被整体报成 0 —— 别拿旧表做平衡。

`water_by_category.md` 是新增的第二份产物。全部类别全展开会有 2800 多行，
其中绝大多数是「这一类全是 0」，所以形态是：

- **全部类别一览**：类别名 / id / 件数 / 补水件数 / 补水率 / **走哪条路**（吃×N / 喝×N / 井×N）/ 水量分布 / 判定来源
- **有内容的类别**（有补水或命中过任何规则）才给逐件明细，
  明细表是 `水 / 吃 / 喝 / 井 / 判定` 五列

> v0.13.11 首版只有「补」一列，把booze / milk / _drink 三个类别整体报成「补水 0」，
> 因为漏了饮料那条路。已在 v0.13.12 修掉，详见上面那条。

实现上挂在 `ModPackage.onBuildConfig` 上（`Plugin.HookConfigPage`，每帧重试到成功）。
**只 `+=` 不实现 `IModConfig`** —— 官方胶水里 `IModConfig` 分支要自己重画全部
ConfigEntry，漏一步整页设置消失；追加钩子则在官方画完之后接一段，原有设置一项不少。

`DumpWaterAudit` 那个配置文件开关仍然有效（只导明细，导出一次即止）。

**v0.13.10**：补水量的分档线 —— 井水/汽水/牛奶统一降到 20，20 以上只留给特殊的水。

数值表见 `design/water-table.md`（由 `tools/dump_water_table.py` 从
`WetnessWater.cs` 生成，改数值后重跑）。分档规则只有一条：

| 档 | 谁 | 值 |
|---|---|---:|
| 特殊 | 清水、圣井水 | 35 |
| 随手可得 | 井水、汽水（拉姆内）、牛奶 | 20 |
| 差一等 | 酒 | 15 |
| 将就 | 脏水 / 污染的井 | 12 |
| 聊胜于无 | 药水及其它能喝的 | 10 |
| 越喝越渴 | 海水 | −10 |

`WetnessWater.CommonDrinkCap = 20` 就是这条线的代码位置，井水/汽水/牛奶都引用它。
超过去的东西都是稀缺资源（这个世界里干净水只有圣井出），随手能打到的井水
不该和圣井同价。

**v0.13.9**：喝井水不加水；补水量表改成从代码生成。

井水的补水**从来没写过**，不是坏了。原版喝井水是 `TraitWell` 自己往 ActPlan 里
塞的一个 `DynamicAct`，它的 `onPerform` 里把整件事跑完了 ——

```csharp
// TraitWell.TrySetAct
p.TrySetAct("actDrink", delegate {
    EClass.pc.Say("drinkWell", ...);
    if (IsHoly || EClass.rnd(5) == 0) ActEffect.Proc(EffectId.ModPotential, ...);
    else if (EClass.rnd(5) == 0) BadEffect(EClass.pc);
    ...
    ModCharges(-1);
    return true;
}, owner);
```

**既不调 `FoodEffect.ProcDrink`，也不调 `Chara.Drink`** —— 而补水挂在
`ProcDrink` 上，所以井水一次都不会被它看到。之前那个 `TraitWell.TrySetAct`
的 Prefix 只做了禁饮（湿润档把菜单项去掉），补水那半边压根不存在。

修法是 Postfix `TrySetAct`，把原版刚建好的 `onPerform` 包一层：

```csharp
// WetnessDrinkPatch.WellTrySetActPostfix
for (int i = p.list.Count - 1; i >= 0; i--)          // 倒着找本次新加的那条
{
    if (p.list[i].act is DynamicAct act
        && act.id == "actDrink"
        && act.onPerform != null
        && !(act.onPerform.Target is WellDrinkWrapper))   // ← 关键：防重复包装
    { act.onPerform = new WellDrinkWrapper(__instance, act.onPerform).Run; return; }
}
```

三点说明：

- **返回值就是「有没有真喝到」** —— 空井那条分支 `return false`，其余 `return true`，
  语义正好，不用自己判断余量。
- **必须防重复包装** —— `TrySetAct` 每次构建菜单都被调用，不做标记会层层套娃，
  一顿操作补 5 次水。用 `onPerform.Target` 的类型认「已包过」。
- **用类包一层而不是 lambda** —— 就是为了 `Target` 能读出类型名。

井水的量按**井的状态**分三档（`WetnessWater.GetWellGain`）：
圣井 35（等于清水）／普通未污染 **20**／污染 12（走脏水档）。
普通井与汽水牛奶同级（`CommonDrinkCap`），比清水少 15 —— 打井水不花成本，
而原版喝它本来就有 1/5 概率 `BadEffect`、1/4 概率 `Mutation`。

顺带修了一处说明层：`GetNoteWater` 原来只认 `TraitDrink`，井不是，
会落到食物表拿 0 —— 现在认 `TraitWell`，井的说明里也显示当前状态对应的含水量。

**v0.13.8**：水囊能被当成「空瓶」塞进原版配方里。

`_origin` 的副作用第五起案子，也是唯一一起**会吃掉玩家东西**的。
原版有拿空瓶当材料的配方，而判定材料时「这一行是别人的原型」被当成「它就是那个东西」：

```csharp
// Props.ListThingStack 里的 Find(id) —— 材料候选列表的唯一来源
bool isOrigin = EClass.sources.cards.map[id].isOrigin;
if (!t.isEquipped && (!(t.id != id) || (isOrigin && t.source._origin == id)))
    TryAdd(t);                    // ← 水囊以「空瓶」的身份进来了
```

三个后果：空瓶槽里列出水囊、`Recipe.IsCraftable()` 因为水囊而返回 true、
确认制作时把装着一肚子药水的水囊当空瓶消耗掉。

堵法是三步，各堵一条路：

| 挂载点 | 堵的是 | 说明 |
|---|---|---|
| **`Card.IsExcludeFromCraft`** | **主修：候选列表** | 游戏自己留的「这件东西不能当材料」钩子。`Props.ListThingStack` 里**每一条**进列表的路（`Find` / `FindCat` / `FindAnyFood`，以及各自的仓库分支）最后都过 `TryAdd → !t.IsExcludeFromCraft(ing)` 这一关，返回 `true` 就整条链子关掉；`Recipe.IsCraftable()` 与界面上的材料计数同源，一并修好 |
| `Recipe.Ingredient.IsValidIngredient` | 「上次用的是它」那条回忆路径 | 选材料时 `DropdownGrid` 会拿 `player.recipes.lastIngredients` 里的 uid 找回物件再问它一遍。老存档里可能已经记着「这个配方上次用的是水囊」，只堵第一处的话那条路仍会把水囊塞回去。顺带一提，这是 `Ingredient.IsValidIngredient` 唯一的调用点 |
| `ThingContainer.GetThingStack` | 显示层 | 配方按钮角上那个数字（`ButtonGrid.RefreshCraftable` 的 `subText`）走的是另一套按 id 的计数，同样把 `_origin == potion_empty` 的水囊算了进去。不改会出现「写着有 1 个、点开却选不到」。只在问的是空瓶时动手，`count` / `max` 从 list 重算 |
| `TraitWell.CanBlend` | 井 | 不在配方这条线上，但根因相同：井的混合界面收 `t.category.IsChildOf("drink")` 的东西，而水囊的分类是从空瓶继承来的（空瓶是 `TraitDrink` 那一脉）。是 `drink` 的话，玩家能把水囊拖进井里**连同囊里的药水一起销毁**。分类万一不是 `drink` 这条就永不触发，白写而已 |

不用 `Props.ListThingStack` 上的 Prefix 是因为它内部靠两个带闭包的局部函数
（`Find` / `FindCat` / `TryAdd`）+ 一个共享的 `ThingStack` 干活，从外面拦要动返回值的列表，
不如用它自己留的钩子干净。补丁数 14 → 18。

水囊自己也走 `Recipe`（做它要锭），但那走的是**产物**那一侧，与这四个判定无关。

启动日志里多两行 `[水囊自检]`，把上面两件只有游戏能回答的事问清楚：

```
[水囊自检] 分类=…（空瓶的分类=…），IsChildOf("drink")=…
[水囊自检] IsExcludeFromCraft(空瓶材料)=True —— 应为 True；False 说明 WetnessNoIngredientPatch 没生效
```

第一行回答「井那条拦截是不是白写的」，第二行是**行为**验证 ——
拿探针直接问，而不是看 `已挂载 N 处补丁`（那个数字当初骗过我们一次）。

**v0.13.7**：喝完/倒完，右下角的数字不动；倒出来的药水还要重新鉴定。

两个都是「改了数据但没告诉别人」的毛病，各修一处。

**数字不刷新。** 右下角那个口数是 `ButtonGrid.SetCardGrid` 调
`trait.SetMainText` 画的，而格子只在 `UIInventory.dirty` 为真时重建 ——
这个标志**只有** `LayerInventory.SetDirty` / `SetDirtyAll` 会置。
原版带充能的物品改的是 `c_charges`，`Card.SetCharge` / `ModCharge` 里
顺手 `SetDirty(Thing)` 了一次；水囊改的是自己的键值，没有这道通知，
数字就停在原处。改动是 `TraitWaterskin.RefreshDoseDisplay()`：

```csharp
LayerInventory.SetDirtyAll();          // 不是 SetDirty —— 见下
WidgetHotbar.dirtyCurrentItem = true;  // 快捷键栏那格也走 SetMainText
```

为什么不用 `SetDirty`：它开头一句 `if (!ELayer.game.altInv || t == null) return;`，
经典（非浮动）物品栏下什么都不做。`SetDirtyAll` 没有这道门，并且顺带置了
`WidgetCurrentTool.dirty` / `WidgetEquip.dirty`。喝、倒进、倒出三处口数变化后都调。

**倒出来又变未鉴定。** `Card.IsIdentified => c_IDTState == 0`，而
`Thing.OnCreate` 对 `source.unknown` 非空的物品（药水就是）写死
`c_IDTState = 5`（未鉴定）—— 于是「照 id 新造一瓶」这个动作本身
就把玩家的鉴定结果丢了。修法是把它当成 `refVal` 一样的**来源状态**存下来：

- 新增键 `SkinKeySrcIdt`（981010，`int`），`PourIn` 抄 `src.c_IDTState`
- `PourOut` 造完之后 `potion.c_IDTState = 取回来的值`
- 旧存档没这个键 → 取出来是 `0` = 已鉴定。有意的：本 mod 本来就在物品说明里
  直接写内容名，内容对玩家从来不是未知的，没道理倒出来再让人鉴定一遍

`[水囊]` 两条日志都加了 `idt=`，`[药水自检]` 多一行「新造的一瓶默认
c_IDTState=?」—— 那行就是上面这个根因的直接证据。

**v0.13.6**：倒出来的不是原来那瓶药水。

同一个 `isOrigin` 机制打在了**药水**身上。源表里只要有**任何一行**写 `_origin = potion`
（游戏自己的 `Chara.TryPoisonPotion` 就在找这种行：
`a._origin == "potion" && a.tag.Contains("neg") && a.chance > 100`），
`potion` 那一行就会中标，于是：

```csharp
ThingGen.Create("potion", -1, 10)   // ← 以为在造随机药水
// 实际：isOrigin == true → 去 SpawnListThing.Get("origin_potion") 里按 chance 挑一个变体
```

造出来的是**别的行**，而随机药水的口味全靠 `refVal` —— 覆写在别的行上毫无意义。
玩家看到的就是「倒出来的不是原来那瓶」。

修法不是再加一道拦截，而是给 `PourOut` 一个「**就要这一行本身**」的造物入口：

```csharp
// WetnessOriginPatch.CreateExact
Thing thing = new Thing();
_exactRow = true;          // 让 ThingGen._Create 与 Thing.SetSource 一起跳过重定向
try { thing.Create(id, -1, lv); } finally { _exactRow = prev; }
```

- 不是基础行（`isOrigin == false`）→ 退回 `ThingGen.Create`，行为与以前完全一致
- 是基础行 → 原样造出来，这才是玩家原本拿着的那一件

顺带把元素写回改走游戏自己的 `TraitPotion.Create`：它除了写 `refVal`，
还会给带 `noCopy` 标签的元素补一条基础值（原来直接赋 `refVal` 会漏掉那一步）。

`WetnessEmptyBottlePatch` 因此改名为 `WetnessOriginPatch` —— 它管的是
`_origin` 这一列的全部副作用，不止空瓶那一件案子。补丁数仍是 14。

**v0.13.5**：空瓶变水囊 —— 上一个补丁拦错了地方，这次修对。

v0.13.4 的 `WetnessEmptyBottlePatch` 只拦了 `ThingGen._Create` 一处，**实际上完全没生效**。
根因是 `isOrigin` 这个标志在游戏里被当成「去挑变体」用了**四处**，而且
`ThingGen._Create` 末尾那句 `thing.Create(id, idMat, lv)` 内部还会再走一遍：
| 消费点 | 代码 | 后果 |
|---|---|---|
| `ThingGen._Create` | `SpawnListThing.Get("origin_"+id, …).Select(lv)?.id ?? …First().id` | 造物总入口 |
| **`Thing.SetSource`** | `source = firstVariations[id]; id = source.id;` | **`Card.Create` 无条件调它 —— 所有造物都要过这一关** |
| `SerializedCards.Restore` | `if (source.isOrigin) text = SpawnListThing.Get("origin_"+text, …).GetFirst().id` | 读档／进图恢复地图卡片 |
| `ThingContainer.GetThingStack` | `current.id == id \|\| (isOrigin && current.source._origin == id)` | 计数（问「有几个空瓶」时把水囊算进去；v0.13.8 已拦） |
| **`Props.ListThingStack`** | 同上那半句 `isOrigin && t.source._origin == id` | **判定配方材料：水囊被当成空瓶，会被消耗掉（v0.13.8 修）** |

```csharp
// Card.cs:2906
public void Create(string _id, int _idMat = -1, int genLv = -1)
{
    id = _id;
    …
    SetSource();        // ← Thing.cs:164，在这里把 potion_empty 换成了水囊
}
```

所以 v0.13.4 那个「在 `_Create` 的 Prefix 里手动 `new Thing().Create(id, …)`」
是绕开第一道、撞上第二道 —— 补丁数照常 +1，产物照旧是水囊。
**「补丁挂上了」和「补丁有用」是两件事**，数字只证明类级 `[HarmonyPatch]` 没丢。

修法是三个地方各拦一道，只对 `potion_empty` 这一个 id 短路：

1. `ThingGen._Create`（原有，保留 —— 它挡住的是「id 被提前换掉」那一层）
2. **`Thing.SetSource`** —— 新增，主修。`Card.Create` 的必经之路
3. **`SpawnListThing.Get`** —— 新增，读档恢复用

第 3 条还顺手修掉一个更隐蔽的问题：`SerializedCards.Restore` 走的那个
`SpawnListThing.Get("origin_potion_empty", …)` 里，`SpawnList.CreateMaster` 只收
`chance > 0` 的行（`CardFilter.Pass` 还会再剔掉 isOrigin），而水囊的 `chance = 0` ——
于是这个 list 是**空的**，紧接着 `if (spawnList.rows.Count == 0) continue;`
把地图上的空瓶**整张丢掉**：不是变成水囊，是凭空消失。
现在这个 key 会回一个「只含空瓶自己」的 list，Restore 拿到 `text = "potion_empty"`
继续走 `ThingGen.Create`，被第 1 道接住。

补丁数 12 → 14。

顺带加了一层**开机自检**（`[Debug] VerifyEmptyBottle`，默认开）：进存档后自动造一个空瓶
（随即丢弃），把 `ThingGen.Create` 与 `origin_potion_empty` 两条路的结果写进日志 ——
`[空瓶自检] 通过：…` / `失败：…`。为什么非加不可：「补丁数够了」和「补丁拦对了层」
是两件事，v0.13.4 就是被这句话坑掉的。

**v0.13.4**：修掉一个从 v0.6.0 就躺在那里的连带 bug —— **空瓶会变成水囊**。
（⚠️ 事后确认：这一版的修法**没生效**，`_Create` 不是唯一的重定向点，见上一条。）

- 起因是「倒进去要还瓶」这一步：`ThingGen.Create("potion_empty")` 拿回来的居然是水囊
- 根因不在那一行，而在 `_origin = potion_empty`：
  `SourceCard` 会**无条件**把被引用的行标成 `isOrigin`，
  而 `ThingGen._Create` 对 `isOrigin` 行的处理是**重定向到它的变体** ——
  等于宣告「空瓶的变体是水囊」。所以商店进货、生成表里造空瓶全都成了水囊
- 修法：`WetnessEmptyBottlePatch` 只对 `potion_empty` 这一个 id 跳过重定向。
  没有去清 `isOrigin`，因为那个标志同时兼着「跳过、不建配方」的判据，
  清掉会凭空多一条「制作空瓶」
- 补丁数 11 → 12

**v0.13.3**：把多嘴的文案清了一遍。

原则是**能看见的事不重复说**。原来几句都在报玩家眼皮底下的东西：

| 原 | 现 | 为什么 |
|---|---|---|
| 你把X倒进了水囊（1/4），空瓶留下了。 | 你把X倒进了水囊。 | 口数在右下角，瓶子就在背包里 |
| 水囊已经满了（X 4/4）。 | 水囊已经满了。 | 同上 |
| 你喝下一口X（剩 2/4）。 | 你喝下一口X。 | 同上 |
| 你倒出了一瓶X（囊中剩 2/4）。 | 你倒出了一瓶X。 | 同上 |
| 水囊是空的，倒不出东西来。 | 水囊是空的。 | 后半句是废话 |
| Tip：里面装着X（剩 n/m 口）。 | 内容：X n/m | 改成原版说明行的短标签写法 |
| Tip：现在是空的。 | （整行不加） | 没有数字本身就是空 |

只有「喝光了最后一口X」留着 —— 那一刻右下角的数字正好消失，值得说一声。

**v0.13.2**：把「倒进去 → 倒出来」接成闭环。

- 倒进水囊时**倒空的瓶子留下**（`c.Pick(ThingGen.Create("potion_empty"))`）。
  原版喝药水不留瓶（`Chara.Drink` 一句 `t.ModNum(-1)` 就完了），
  不留的话倒出口永远够不着 —— 两头接不上
- 井是靠 `charges` 的（`well?.ModCharges(-1)`），所以这不会变成无限药水
- 配置项 `[Waterskin] ReturnEmptyBottle` 默认开，关掉即回原版口径
- 顺带纠正一处早先的说法：倒出口是 `TraitPotionEmpty`，**空桶也算**（`bucket_empty` 继承它）

**v0.13.1**：把水囊做得更像原版本来就有。

- 描述收成一句：**「喝水的容器。一次喝一口。」**（原来「装药水的皮囊。把药水倒进去…」）
- 状态说明对齐原版 `Stat` 表的口径（hunger「饱腹度。肚子饿会让速度变慢…」那种）：
  **「湿润度。水分不足会让速度变慢并流失魔力，过多则一口也喝不下去。」**，
  并去掉手写的「（状态）」前缀 —— 那是原版按 `contype_` 自己加的
- **存量从物品名挪到格子右下角**：不再有「水囊（治疗药水 1/4）」这种拉长的名字，
  改走原版钩子 `Trait.SetMainText`（`ButtonGrid.SetCard` 里调的），
  和原版堆叠数量 / 充能数同一套位置与字体
- 装的是什么挪进 Tip：`Trait.WriteNote` 里补一行「内容：X n/m」（空的就不加这行 ——
  右下角没数字本身就是「空」）

**第十三步（v0.13.0）**：水囊有了自己的贴图和材质色。

- 桌面那张 32×32 灰阶水囊图进了 `Mod_Wetness/Texture/wetness_waterskin.png`
- `ThingV` 的 `parse` 列填 `render/@obj,color` —— 源表自带的自定义纹理 + 材质染色开关，
  **一行数据，没写一行 C#**
- 于是金锭做的水囊是金色、以太锭做的是青色 —— 和原版锭类物品同一套机制
- 贴图是灰阶的，材质色**乘**上去，两个 Mod 的贴图不会因为颜色打架

**v0.12.2**：提示语收短。禁饮只说 **「一口也喝不下去了。」**，
不再写「体内水分太足…」那种状态描述；「湿润」档的档位消息同样收成
「现在一口也喝不下去了。」。连带：井口那条路本来就不出文案，
`TooWetToDrinkWell` 是死代码，一并删掉。

---

## 水囊（v0.6.0 加入，v0.7.0 容量随材质变，v0.8.0 起可混入／倒回）

### 它是什么

一只可以随身携带的水袋。把「喝得下」的药水倒进去，同一时间只能装一种；
「使用」它喝下一份。能装几口取决于它是用什么锭做的（2～10 口，见下）。
倒进去、倒出来都能用原版的「混入」界面完成。

它同时是**禁饮规则的后门**：湿润度到「湿润」以上后，`Chara.Drink` 被本 mod 拦掉，
井口也喝不了，但水囊走「使用」这条路，不在拦截范围内。

### 怎么拿 —— 制作台

在**制作台**（`workbench`）上做：**2 块锭 + 2 根细绳**，一开始就会，不用先学配方。

配方的**显示与执行都挂在物品行上**（`RecipeManager.BuildList()` 把 `CardRow` 变成
`RecipeSource`，UI 选中后变成 `Recipe`），不需要另建表：

| 列 | 值 | 含义 |
|---|---|---|
| `factory` | `workbench` | 在哪个制作台做，填**制作台物品的 id**。`self` = 快速制作，`x` / `none` = 不上架 |
| `components` | `ingot/2,string/2` | 材料。**第一项必须是锭** —— 它决定成品材质，进而决定容量 |
| `recipeKey` | `*` | 以 `*` 开头 = alwaysKnown（直接可做）；`-` 开头 = 不进随机学配方池 |

> `chance = 0`，所以它**不会**随机出现在商店或生成表里 —— 只能做出来。

`components` 语法（解析见 `RecipeSource.GetIngredients`）：

| 写法 | 含义 |
|---|---|
| `id` / `id/N` | 需要该 id 的物品 / N 个（默认 1） |
| `a\|b` | a 或 b 都行 |
| `#cat` | 任意属于该类别（category）的物品 |
| `+id` | 可选材料，不放也能做 |
| `$id` | 这一项决定成品的颜色 |
| `id@材质` | 要求该物品是对应材质 |

**台与配方的匹配**：`Trait.Contains(r) => r.idFactory == 台的 id`（基类实现，
`Elin/Trait.cs:615`）。也就是说 `factory` 必须**精确等于**制作台物品的 id；
写错了不会报错，只是这个配方在任何台上都不出现。

**哪些行会被造出配方**：`RecipeManager.BuildList()` 遍历所有 `CardRow`，
但跳过 `isOrigin == true` 的行。而 `isOrigin` 标记的是
「**被别人当 `_origin` 引用的行**」（`SourceCard.cs:71`：`row.origin.isOrigin = true`）。
于是：

- 水囊行 → 没被别人引用 → `isOrigin = false` → **有配方** ✓
- 被水囊引用的 `potion_empty` → `isOrigin = true` → 跳过
  （否则游戏里会凭空多出一个「制作空瓶」的配方）

> ⚠️ **同一个 `isOrigin` 标记还有第二个用途，当时没发现**：
> 它同时是「这是基础行，去挑一个变体」的判据，被四处消费 ——
> `ThingGen._Create`、`Thing.SetSource`（`Card.Create` 内部）、
> `SerializedCards.Restore`（读档恢复地图卡片）、
> `ThingContainer.GetThingStack`（计数）。
> 于是**游戏里任何造空瓶的地方都会造出水囊**（商店进货、生成表、还瓶），
> 而地图存档里的空瓶会被直接丢掉。
> 这是 `_origin = potion_empty` 的连带代价，v0.13.5 用
> `WetnessOriginPatch` 的三道拦截收口 —— 详见下面「外观」一节。
>
> 后来又挖出**第五处**：`Props.ListThingStack` 判定配方材料时也用同一句话，
> 于是水囊会以「空瓶」的身份被塞进原版配方里并消耗掉 —— v0.13.8 修，
> 见 `WetnessNoIngredientPatch`。**这张表别当清单用**：
> `isOrigin` 没有集中消费点，每次都得全库 grep 一遍。

顺带：`components` 列的 override 判据是 `!IsEmpty()`，但 `recipeKey` 是**无条件覆盖**
（`row.recipeKey = row3.recipeKey`）—— 留空 = 清空，继承不到 `_origin` 的值。

### 关于 `SourceRecipe` 表（别被它误导）

游戏里**确实**有一张 `SourceRecipe` 表，`TraitCrafter.GetSource(AI_UseCrafter)` 会遍历它 ——
但它**不是**玩家制作走的那条路：

```csharp
// AI_UseCrafter.OnProgressComplete
if (recipe != null)                  // 玩家：UI 里选中的那个配方
    recipe.Craft(blessed, l0, ings, crafter);
else                                 // 没选中配方的 AI 路径
    crafter.Craft(this);             //   ← 只有这条才会走 GetSource → SourceRecipe 表
```

**结论：mod 加可制作物品不用动 `SourceRecipe`**，物品行上填对
`factory` / `components` / `recipeKey` 就够。

> 另外 `Package/_Elona/Data/Source/` 这**整个目录都不能当参照**：里面是旧版表结构，
> 字段对不上现在的 `Source*.cs` —— 例如 `SourceMaterial` 里还有 `idBiome` / `tileColor` /
> `soundDeath` 这些现版根本不存在的字段，连 `hardness` 这个字段名在文件里都不出现
> （按字节硬扫命中 0 次）。想读真实数值只能在游戏里读，别在这份文件上花时间。

### 容量：由做它用的那块锭决定

容量**不是常量，而是这个水囊实例的属性** —— 由它自己的材质硬度算出来：

```
容量 = 2 + 材质硬度 / HardnessPerDose      （夹在 2 ～ 10）
     + 1                                   （附过魔的话）
```

为什么读材质就够了，这条链值得记住：

1. 配方第一项写的是 `ingot`（锭）；
2. `Recipe.GetMainMaterial()` 取的是 **`ingredients[0]`**（第一个材料槽）的材质；
3. `RecipeCard.Craft()` 拿它去 `ThingGen.Create(id, 材质, lv)` ——
   **成品水囊的材质就是那块锭的材质**。

于是铁锭做出来的就是铁水囊（硬度高 → 装得多），软材料做的只有 2 口。
**不需要 hook 制作过程，也不需要把容量存进存档** —— 材质本来就长在 `Thing` 自己身上。

**附魔**：`Thing.encLV > 0` 就再多 1 口。奖励加在夹取之后，所以附魔水囊能到 11 口；
附魔被去掉（`SetEncLv(0)`）容量也跟着退回。物品名由原版逻辑显示 `+N`，看得出附过魔。

两个系数是 **BepInEx 配置项**（`BepInEx/config/mctrr.wetness.cfg` 的 `[Waterskin]` 段）：

| 配置 | 默认 | 作用 |
|---|---|---|
| `HardnessPerDose` | 15 | 每多少点硬度多装一口。**调小 = 同样的锭装得更多** |
| `MaxDoses` | 10 | 容量上限（附魔奖励可再越过 1 口） |

实机觉得整体偏大/偏小，直接改配置就行，不必重新编译。

#### 材质硬度的真实数值

`hardness` 是材质源表的第 18 列（MCP `elin_sheet` 的 `material` 页可确证字段存在，
模组表留空时的默认值是 1）。数值本身在玩家安装目录里是二进制、直接读不出，
但官方源表公开在 Google Sheets 上，且 **ylvapedia** 与 **twinfinite** 两份
互相独立的整理**逐条完全一致**，可以当权威值用：

| 锭的材质 | 硬度 | 水囊容量 | 锭的材质 | 硬度 | 水囊容量 |
|---|---|---|---|---|---|
| 金 gold | 25 | 3 口 | 铬铁 chromite | 85 | 7 口 |
| 银 silver | 27 | 3 口 | 钛 titanium | 89 | 7 口 |
| 铜 copper | **34** | **4 口** | 陨铁 meteorite | 95 | 8 口 |
| 青铜 bronze | 45 | 5 口 | 秘银 mithril | 98 | 8 口 |
| 白金 platinum | 49 | 5 口 | 精金 adamantite | 115 | 9 口 |
| 钴 cobalt | 54 | 5 口 | 以太 ether | **120** | **10 口** |
| 铁 iron | 56 | 5 口 | 暗物质 dark matter | 200 | 10 口（夹住） |
| 黑曜石 obsidian | 60 | 6 口 | （木、布、水一类） | 1–20 | 2 口 |
| 钢 steel | 67 | 6 口 | | | |

所以 `HardnessPerDose = 15` 不是拍的：它让最软的金属（金，25）落在 3 口、
最硬的（以太，120）正好顶到上限 10 口，中间十几档金属各差一口 ——
铺满而不用「大半金属都顶到上限」。**铜锭 4 口、以太锭 10 口**，附魔再各 +1。

> 想换个手感：把 `HardnessPerDose` 设成「你想让它刚好顶到 10 口的那种锭的硬度 ÷ 8」。
> 例如想让钢（67）就是满的，设 8（67/8≈8，2+8=10）。

#### 想再核对一遍时

上面那张表的数值来自外部资料（ylvapedia / 官方 Google Sheet），**插件本身不打印任何东西**。

曾经有过一段启动自检（`Plugin.Update` 轮询源表、把每种材质的硬度与容量打进 BepInEx 日志），
**已删除** —— 它只是诊断手段，每局启动都要刷一百多行，排查别的问题时全是噪音。

将来真要核对「游戏实载值 vs 资料值」，临时加回去即可（代码片段见
skill `elin-custom-item` 的 8.6 节）：

```csharp
// Plugin.Update：每 3 秒试一次，直到 EClass.sources.materials 就绪，只打一次
foreach (SourceMaterial.Row m in EClass.sources.materials.rows)
    Logger.LogInfo($"{m.alias} hardness={m.hardness}");
```

### 三个动作

| 操作 | 触发 | 结果 |
|---|---|---|
| **喝** | 手持水囊，「使用」指向自己 | 喝下一份，`ActEffect.Proc` 走原版药水出口 |
| **倒进去** | 手持水囊「使用」一瓶药水；或对着水囊用「混入」拖入药水 | 那瓶药水被灌进去，+1 口；**倒空的瓶子留下**（v0.13.2） |
| **倒出来** | 手持水囊「使用」一个空瓶／空桶；或对着水囊用「混入」拖进去 | 空容器被灌满，−1 口（**原版没有，本 mod 新增**） |

倒出口是 `TraitPotionEmpty`，**空瓶和空桶都算**（`bucket_empty` 也继承它，
原版舀井水那句 `(owner.id == "bucket_empty") ? "bucket" : "water"` 就是证据）。
两边接上之后就是一个闭环：倒进去 → 瓶子回来 → 倒出来 → 原来那瓶药水回来。

喝和倒进去走原版 `HotItemHeld` 的标准派发（`CanUse(Chara)` / `CanUse(Chara, Card)`），
没有自己塞菜单项；倒出来走原版「混入」界面。

之所以不做成「用药水指水囊」：`HotItemHeld` 先判 `p.IsSelf` 再判鼠标指向的卡，
由水囊这一侧发起更稳，也符合「工具对人/物使用」的直觉。

### 混入：直接白嫖原版的拖拽界面

对着水囊交互时多出来的那一项「混入」不是我们自己造的 —— 它是原版机制，
开关是 `Trait.IsBlendBase`：

```csharp
// ActPlan.cs:808（鼠标指向的物品）与 Trait.cs:807（自身交互列表）
if (t.trait.IsBlendBase)
    TrySetAct("invBlend", () => LayerDragGrid.Create(new InvOwnerBlend(t)), t);
```

点开后是原版的拖拽网格，`InvOwnerBlend.ShouldShowGuide(t)` 用
`owner.trait.CanBlend(t)` 决定哪些物品能拖进去（能拖的会亮），
松手时 `_OnProcess` 调 `owner.trait.OnBlend(t, ...)`。

原版吃这口饭的有：**井**（`TraitWell`）、**染料**（`TraitDye`）、**梦虫**（`TraitDreamBug`），
以及**水／毒药／爱情药水／耐酸药水**这四种药水自己
（`TraitDrink.IsBlendBase => IsBlend()`，往装备上涂：圣水祝福、诅咒水诅咒、毒药下毒）。

水囊照着同一个模式挂上 `IsBlendBase => true`，行为、音效、提示语就都是原版的，
不用自己写菜单。顺带一个安全确认：**NPC 不会来碰它** ——
`Chara.TryUse` 里唯一会触发 `OnBlend` 的分支写死了 `t.id == "338"`。

### 倒回空瓶（原版不支持，本 mod 新增）

**原版没有「把药水倒回空瓶」这回事**，两处证据：

1. `TraitPotionEmpty` 只覆写了 `OnUse(Chara, Point)` —— 对**地块**使用（舀井水／地块水）。
   它根本没有 `OnUse(Chara, Card)` 重载，所以「拿空瓶去指一瓶药水」什么都不会发生。
2. `TraitPotionEmpty` 也不是 `IsBlendBase`（继承 `TraitDrink.IsBlendBase => IsBlend()`，
   而空瓶的效果不在那四种里）—— 所以也没有「对着空瓶混入」这条路。

唯一沾边的是**井**：`TraitWell.CanBlend(t) => t.category.IsChildOf("drink")`，
能把药水倒**进井里** —— 但那只是把药水白白浪费掉并增加井的污染度
（`pollution += rnd(3) + ...`），不会还你一个瓶子。

所以这一条是我们自己加的，实现要点：

- 入囊时必须记下**原物的 `id` 与 `refVal`**（`SkinKeySrcId` / `SkinKeySrcRefVal`）。
  光有 effect / power 不够 —— 玩家喝的是那瓶东西自己的 trait，
  而随机药水的 id 全是 `"potion"`，口味全靠 `refVal` 区分。
  还有**鉴定状态** `c_IDTState`（`SkinKeySrcIdt`）：新造出来的一瓶默认是未鉴定的，
  不还回去就等于把人家的鉴定结果扔掉（v0.13.7）。
- 倒出时用 `WetnessOriginPatch.CreateExact(srcId, 10)` 重建一瓶 ——
  **不是** `ThingGen.Create`：`potion` 那一行被 `isOrigin` 重定向（见 v0.13.6），
  `ThingGen.Create("potion")` 拿到的是它的某个变体，不是原来那瓶。
  元素用 `TraitPotion.Create` 写回（它顺带处理 `noCopy`），
  再按存的祝福状态 `SetBlessedState`、按存的鉴定状态写 `c_IDTState`，
  最后 `container.ModNum(-1)` 消耗掉空瓶。
- 口数变化（喝 / 倒进 / 倒出）之后要显式 `RefreshDoseDisplay()` ——
  右下角那个数字不会自己重画，理由见 v0.13.7。
- 等级没存，重建用 `10` —— 与原版 `TraitPotionEmpty` 造随机药水时写死的值一致。
  药水强度主要由 `refVal` 对应的元素决定，这点差异可以忽略。
- 对账用的 `[Debug] LogPour`：倒进 / 倒出各写一行日志（原物 id + refVal + idt + trait、
  造出的 id + refVal + idt + trait），三段对不上时一眼能看出是哪一段。

> 本功能之前存下的水囊没有 `SkinKeySrcId`，倒出时会提示「分不出原样」而不是崩掉。

#### 倒进去时要把瓶子还回来（v0.13.2）

这是让上面那个闭环成立的关键一环。**原版喝药水是不留瓶子的**：

```csharp
// Elin/Chara.cs:5383
public void Drink(Card t) {
    Say("drink", this, t.Duplicate(1));
    Say("quaff");
    PlaySound("drink");
    t.ModNum(-1);                 // ← 药水和瓶子一起没了
    FoodEffect.ProcDrink(this, t.Thing);
}
```

于是「倒进去」消耗掉整瓶药水之后，玩家手里一个空容器都没有 ——
倒出口就在眼前却永远够不着。所以 `PourIn` 里补一手：消耗原物之后
`c.Pick(ThingGen.Create("potion_empty"))`，并把这件事写进提示语。

**不会因此变成无限药水**：井是靠充能的 ——
`TraitPotionEmpty.OnUse` 末尾那句 `well?.ModCharges(-1)`，
而且 `well.Charges <= 0` 时直接拒绝。还瓶只是免去反复买瓶，不改变井的上限。

开关在 BepInEx 配置里：`[Waterskin] ReturnEmptyBottle`，默认 `true`。
关掉即与原版一致（喝药水不留瓶），但那时候倒出来就只能靠自己另外准备空瓶了。

### 不能混装

内容标识是 `物品 id + refVal + 祝福状态` 三样拼起来的字符串。

- 随机的药水 id 都是 `"potion"`，靠 `refVal`（元素 id）区分，所以 refVal 必须进标识
- 祝福状态会改变 `ActEffect.Proc` 的行为（圣水 / 诅咒水的效果不同），所以也算不同种
- 倒第二种时会提示「水囊里装的是 X，不能和 Y 混在一起」并中止

**另一个是「不许装」**：**以太抗体药水**（源表 id `1165`）倒不进去，**而且什么都不说**。
理由不是技术限制而是平衡：它本来就该当场喝掉起效，
存进囊里等于在「偏湿、什么都咽不下」时留一条随时可用的后路。

判定写在 `CanUse(Chara, Card)` 与 `CanBlend` 里、不弹消息 ——
这是一条**硬性禁止**，不对应任何玩家可解的状态：容量满了可以去倒，装错了可以喝光，
但以太药水就是不行，多说一句只是噪音。与它相对的是容量/混装那两条：
那些玩家能自己解决，所以放在 `OnUse` / `OnBlend` 里发消息
（在 `CanUse` 里返回 false 只会让菜单项消失，什么都不说）。

### 数据存哪

借用 `Card` 从 `BaseCard` 继承的两个字典 —— 它们都带 `[JsonProperty]`，**随存档走**：

```csharp
[JsonProperty(PropertyName = "Y")] public Dictionary<int, int> mapInt;
[JsonProperty(PropertyName = "Z")] public Dictionary<int, string> mapStr;
```

键在 `WetnessDef` 里集中定义（`SkinKeyEffect` / `Power` / `Bless` / `Doses` / `Ident` / `Label` / `N1` /
`SrcId` / `SrcRefVal`）—— 后两个是倒回空瓶用的来源信息。

**键一律用显式整数**，不用 `GetStr("...")` 那个重载 —— 它内部走 `String.GetHashCode()`，
而 .NET 的字符串哈希不保证跨运行时、跨版本一致；存档里存的是哈希值，读回来可能对不上。

存的是**效果参数**（EffectId / Power / Bless / N1），不是物品引用。喝的时候直接调
`ActEffect.Proc`，那是 `TraitDrink.OnDrink` 内部做的事，所以效果与原版喝这瓶药水完全一致，
也不需要为此建临时物品。

### 为什么不继承 `TraitDrink`

`TraitDrink` 会把物品变成「药水」，于是 `Chara.Drink` 也把它当饮品 ——
而 `Chara.Drink` 正是本 mod 在偏湿时拦掉的入口。水囊的存在意义就是偏湿时还能补一口，
所以必须走「使用」。现在 `TraitWaterskin : Trait`，直接调 `ActEffect.Proc`。

顺带：`CanStack` 覆写为 `false`。空瓶那一类是可堆叠的（基类默认
`owner.category.maxStack > 1`），而两个装了不同东西的水囊叠成一格就完蛋了。

### 外观（v0.13.0：独立贴图 + 按材质染色）

`ThingV` 里写 `_origin = potion_empty`，于是**分类、重量、放置参数全部继承空瓶**，
本 mod 只改名字、trait、价值，以及下面这两项外观。

两项都走 **`ThingV` 的 `parse` 列** —— 它是源表自带的通用字段覆盖器
（`SourceThingV.OnAfterImportData` 里对每一项做 `名字/值` 的 switch）：

| parse 值 | 效果 |
|---|---|
| `render/@obj` | 把 `_idRenderData` 设成 `@obj` —— 走「自定义纹理」，读本 mod `Texture/<物品id>.png` |
| `color` | 把 `colorMod` 设成 **100** —— 开启材质染色 |
| `no_color` | 把 `colorMod` 设回 0 —— 关闭染色 |

所以 `PARSE = "render/@obj,color"`（`string[]` 列在表里是逗号分隔的一个字符串）。

#### 自定义贴图怎么被找到

贴图放 `Mod_Wetness/Texture/wetness_waterskin.png` —— **文件名必须等于物品 id、小写 `.png`**，
且必须在 `Texture/` **根目录**（子目录找不到）。名字不用写进源表：

`ModPackage.ParseTexture` 会把 `Texture/` 下所有 png 按「相对路径去扩展名」注册进
`SpriteReplacer.dictModItems`，渲染时 `SpriteReplacer.Reload(id)` 直接按物品 id 取。
`@obj` 里的 `@` 表示「按图片实际尺寸整张居中摆放」。

#### 材质染色是怎么生效的

`Card.RefreshColor()` 对**所有**卡片都要走一遍，物品分支是：

```csharp
// Elin/Card.cs:6473-6484
else if (sourceRenderCard.useRandomColor)
    _colorInt = BaseTileMap.GetColorInt(ref GetRandomColor(), TileRow.colorMod);
else if (sourceRenderCard.useAltColor)
    _colorInt = BaseTileMap.GetColorInt(ref material.altColor, TileRow.colorMod);
else
    _colorInt = BaseTileMap.GetColorInt(ref material.matColor, TileRow.colorMod);   // ← 我们的情况
```

而 `TileRow` 就是卡片自己的源表行（`Card.TileRow => sourceCard`），
`colorMod` 就是那一列。关键在 `GetColorInt`：

```csharp
// Elin/BaseTileMap.cs:3782
public static int GetColorInt(ref Color matColor, int p)
{
    if (p == 0) return 104025;   // ← 写死的中性灰，等于「不染色」
    return p * 262144 + (int)(matColor.r * 50f) * 4096 + (int)(matColor.g * 50f) * 64 + (int)(matColor.b * 50f);
}
```

`colorMod == 0` 时返回固定的 104025（= RGB 25/25/25），也就是原图照旧；
**只要不是 0**，RGB 三通道就取自 `material.matColor` —— 于是物品颜色跟着
「做它用的那块锭」走。这正是原版锭类物品「不同材质颜色不同」的机制。

背包图标也吃这一套：`Card.SetImage(Image)` 用的就是同一个 `GetSprite()` 和同一个 `colorInt`。

> 材质色是**乘算**上去的，所以贴图本身要画成灰阶（素材已按此处理）。
> 想改回固定外观：把 `PARSE` 里的 `color` 删掉即可，不用动 C#。

---

## 自定义物品怎么落地（Elin 机制笔记）

这部分是勘察结论，做后续物品时会反复用到。

### 物品本身是纯数据 —— 不用写 C#

`LangMod/<语言>/Source.xlsx` 里加一行就行。Elin 的 `ThingV`（变体表）机制会把
`_origin` 那一行的**全部字段**复制过来，只跳过 `parse`，之后仅用**非空单元格**覆盖
（见 `SourceThingV.OnAfterImportData`）：

```csharp
foreach (字段 f in new SourceThing.Row().GetRowFields())
    if (f.Name != "parse") f.SetValue(row, f.GetValue(origin));
row.id = row3.id;
if (row3.LV != 0) row.LV = row3.LV;
if (row3.chance != -1) row.chance = row3.chance;
if (row3.value != -1) row.value = row3.value;
else row.value += EClass.rnd(row.value / 2);   // ← 注意这里
if (row3.weight != -1) row.weight = row3.weight;
if (!row3.tiles.IsEmpty()) row.tiles = row3.tiles;
... 其余字段同理
EClass.sources.things.rows.Add(row);
```

**四个数值列的坑**（`value` / `weight` / `chance` / `LV`）：

| 列 | 判据 | 留空时 | 想要「继承」就写 |
|---|---|---|---|
| `value` | `!= -1` | 解析成 0 → 价值 0 | `-1`（会随机 +0~50%） |
| `weight` | `!= -1` | 解析成 0 → 无重量 | `-1` |
| `chance` | `!= -1` | 解析成 0 → 不进随机生成 | `-1` |
| `LV` | `!= 0` | 解析成 0 → 继承 ✓ | 留空 |

原因：空单元格经 `ExcelParser.GetStr` → `rowDefault`（第 3 行）→ 仍是空 → `GetInt` 返回 **0**，
而不是 -1。所以「-1 = 继承」这个哨兵**只能显式写出来**。

另外 `sort` 列在 `OnAfterImportData` 里**根本没被读取**，永远继承 `_origin`，留空即可。

### 原版没有 ThingV 数据

`_Elona/Data/Source/` 里没有 `SourceThingV` 文件（`SourceData.dataPath` 决定文件名 = `Source<类型名>`），
所以 `_origin` **不会**指向另一条 ThingV 行 —— 不存在链式继承的时序问题。

### 表结构

`Mod_MoreSkills` 是现成的范例（`LangMod/CN/Source.xlsx` + `MoreSkills.dll` + `Texture/`）。
每页四段：

| 行 | 内容 |
|---|---|
| 1 | 字段名 |
| 2 | 类型 |
| 3 | **默认值**（空单元格会回退到这里） |
| 4+ | 数据 |

sheet 名 = `SourceData` 类名去掉 `Source` 前缀（`FindSourceByName` 依次试 `Source<名>` / `Lang<名>` / `<名>`）。

### 中文名为什么不在表里

`Thing` 页只有 `name_JP` / `name` 两列，没有 `name_CN`。中文靠
`LangMod/<语言>/SourceLocalization.json` 按 `<SourceType>.<id>.<字段>` 覆写：

```json
{
  "SourceThing.wetness_waterskin.name": "水囊",
  "SourceThing.wetness_waterskin.unit": "个",
  "SourceThing.wetness_waterskin.detail": "..."
}
```

`SourceLocalization.LoadAll(lang)` 会遍历 `LangMod/<lang>/SourceLocalization.json`，
按 `<SourceData 类型名>.<id>.<字段名>` 合并后应用到所有包。

本 mod 的源表由 `tools/build_langmod.py` 生成（可重跑），产物写到 `package/LangMod/`，
由 csproj 的 `CopyAssets` 目标在编译时拷进 `Mod_Wetness/`。

### trait 怎么挂

`Card.ApplyTrait()` 用源表的 `trait[0]` 去找类：

```csharp
trait = ClassCache.Create<Trait>("Trait" + sourceCard.trait[0], "Elin");
// ClassCache.Create: Type.GetType(名 + ", Elin") 失败后 → 遍历 typeLoaders
// 默认 loader = LoadTypeFromGlobalNamespace: 遍历 ClassCache.assemblies 逐个试 GetType
```

两条结论：

1. **自定义 trait 类必须放在全局命名空间** —— 那个回退只按简单类型名找
   （`Mod_MoreSkills` 把 `TraitTrapCraftMine` 放在 `MoreSkills` 命名空间里，
   按理说这条回退找不到它）。
2. **要把自己的程序集登记进 `ClassCache.assemblies`**，回退才会去看你的 dll。
   `Plugin.RegisterTraitAssembly()` 在 `Awake` 里做这件事（包在 try/catch 里）。
   注意 `ClassCache` 会把「查不到」的结果也缓存下来，所以必须尽早登记。

即便如此，本 mod 仍然加了一层 `WetnessWaterskinPatch`（`Card.ApplyTrait` 的 Postfix）
兜底：`ApplyTrait` 在读档建卡时也会跑，所以存档里的旧水囊读回来同样被接管。

---

## 七档分级

| 档位 | 数值区间 | 状态栏 | 效果 |
|---|---|---|---|
| 喝得过多 | 80–100 | 显示 | 不能再「饮用」；**自然回蓝 +10%** |
| 湿润 | 70–79 | 显示 | 不能再「饮用」；**自然回蓝 +10%** |
| 不显示 | 40–69 | 静默 | — |
| 干燥 | 30–39 | 静默 | — |
| 干燥（红） | 20–29 | 显示（红） | 速度 −10% |
| 口渴 | 10–19 | 显示（红） | 速度 −10% |
| 脱水症 | 0–9 | 显示（红） | 速度 −30% |

初始值 60，落在「不显示」区间 —— 开局不会看到任何提示。

分档走的是**原版那套**：先 `10 * value / max` 取 0–9 的桶，再查 `WetnessDef.PhaseMap`
（`{0,1,2,3,4,4,4,5,6,6}`）。干侧挤在低端三桶、湿侧挤在高端两桶，中间三桶并作一档静默。
要调边界改那张表就行。

之所以没直接用基类 `Stats.GetPhase()`，是因为本状态没有自己的 `SourceStat` 行，
`base.source` 会回退到 `ConWet`，读到的 `phase` 数组不是这套七档，所以自己重写了一遍
完全相同算法。

---

## 数值机制

- **自然衰减：与原版饥饿逐字相同** —— 每 50 回合掷一次骰子，1/3 概率 −1，
  即平均 **150 回合掉 1 点**（`DecayCheckEvery` / `DecayChanceDiv`）
- **下雨天露天淋雨时完全暂停**（见下）
- 呕吐 −8
- **脱水症（0–9）会不断流失魔力**：每回合 1/5 概率扣 `1 + rnd(2) + 最大MP/50`，
  形状照抄原版饥饿最饿那一档的掉血式，只是把 HP 换成 MP（见下）

从开局 60 走到见底，默认节奏（平均 150 回合 1 点）：

| 到哪 | 累计回合 |
|---|---|
| 39 干燥（开始静默） | 3150 |
| 29 干燥红（−10% 速度） | 4650 |
| 19 口渴（−10%） | 6150 |
| 9 脱水症（−30%，开始扣蓝） | 7650 |
| 0 见底 | 9000 |

数值到 0 就停 —— `Stats.Mod` 会夹到 `[min, max]`，不会掉成负数，也没有「干死」这种结局。

### 为什么脱水扣的是魔力，不是血

原版 `hunger` 最饿那一档一次管两路：

```csharp
if (phase >= 5) {
    if (rnd(5) == 0) DamageHP(1 + rnd(2) + MaxHP / 50, AttackSource.Hunger);
    if (rnd(3) == 0) stamina.Mod(-1);
}
```

也就是**掉血 + 掉体力**。湿润度要是照抄这套，玩家会被两条掉血线夹着，
而且根本分不清究竟是饿死的还是渴死的 —— 血条只有一根，死因也只报一个。

所以伤害整条换成 MP，惩罚线各管一段：

| | 饥饿 | 口渴 |
|---|---|---|
| 血 | 掉 | — |
| 体力 | 掉 | — |
| 速度 | −10 / −10 / −30% | −10 / −10 / −30% |
| 魔力 | — | 脱水症持续流失 |
| 湿侧 | — | 自然回蓝 +10% |

干侧耗蓝、湿侧回蓝，正好对称。

两个实现上的细节：

- **只扣到 0 为止，绝不扣成负数。** `StatsMana.Mod` 在 `value < 0` 时会走「魔力逆流」：
  弹一句 `mana_overflow`，再 `DamageHP(-value * 400 / ..., ManaBackfire)` ——
  掉血绕回来了，死因还会显示成魔力反噬。夹在 0 就不会触发这条路径。
- **扣空之后就不再有惩罚**，所以脱水本身不致命（致命的是饿 + 流血）。
  想让它也能杀人，就在 `mana.value == 0` 时额外接一小段 `DamageHP`。

> 一开始写成「每 15 回合必扣 1」，实测下来几步就脱水 —— 比原版快了 10 倍。
> 现在直接照抄 `Chara.TickConditions` 里 hunger 那一句，节奏与饿完全一致；
> 想调快就把配置项一起改小。

### 下雨暂停消耗

原设定的「濡湿」：**天气在下雨、人又淋在露天时，湿润度不再下降**。

判定条件逐字照抄原版 `Chara.TickConditions` 里给「淋湿」的那一处
（`Chara.cs:3959`）：

```csharp
if (EClass.world.weather.IsRaining && !EClass._map.IsIndoor && !pos.cell.HasRoof)
    AddCondition<ConWet>(20);
```

所以「淋着雨」的定义和游戏本身完全一致：

- 站在**露天**下雨 → 暂停消耗（同时原版照常给你 `ConWet`）
- **室内**（`Map.IsIndoor`）→ 照常消耗
- **屋顶下**（`pos.cell.HasRoof`，看的是房间的 `idRoofStyle`）→ 照常消耗
- 洞里 = 室内，照常消耗

雨势大小不区分：`Weather.IsRaining` 同时覆盖 `Rain` 与 `RainHeavy`。

**只认雨、不认雪** —— 原版那处 `ConWet` 判定用的就是 `IsRaining`，不含 `Snow`，
本 mod 跟着原版走。注意这有个连带效果：冬天时 `Weather.SetCondition` 会把整批雨
转成雪，于是**冬天本条不会触发**。要不要把雪也算进来，等你实测后再定
（开关是 `WetnessDef.RainPausesDecay`）。

暂停时不推进累加器 `_cints[23]`，所以雨停后是接着原来的余数走，不会攒出一笔「欠账」。

### 饮用补水

沿用 Elin 自带的 `EffectId` 饮品分类，不另造体系：

| 饮品 | 补充 |
|---|---|
| 清水 `DrinkWater` | +35 |
| 拉姆内 / 牛奶 | +25 |
| 酒 `Booze` | +15 |
| 脏水 `DrinkWaterDirty` | +12 |
| 药水（含一般饮料 `Drink`） | +10 |
| 盐水 `SaltWater` | −10 |

清水补得最多 —— 这个世界里干净水只有圣井才出。
盐水是负的：原版里它基本只是蜗牛人的饮品（其他种族喝了只有一句提示，什么也不发生），
做成倒扣既符合「喝海水越喝越渴」的直觉，也算给这东西一个存在理由。

原版对盐水的处理还有一层：蜗牛人（元素 1211）喝盐水会**当场掉血**，
非蜗牛人只是提示一句。这层原版逻辑没有动，只是在它之上加了湿润度惩罚。

从水囊里喝也走同一张表（按存下来的 `EffectId` + 来源 id 取），
所以补水量与直接喝那瓶药水一致 —— 包括「不该补水的那些」（见下一节的禁列表）。

**注意**：饮品走的是 `EffectId` 分类，任何「能喝但不是水」的东西默认都给 10。
所以水银、耐酸保护液、堕天使之血这类必须进禁列表逐个挡掉，否则它们会白送水分。

### 吃饭补水（v0.12.0 重做：不再按营养算）

「吃」与「喝」是两个并列的出口（`FoodEffect.Proc` / `ProcDrink`），
加上水囊内容物与物品说明，四处统一查 `WetnessWater` 这一张表。

**v0.10.0 到 v0.11.x 用的是「含水 = 营养 ÷ K」，这一版整条删掉。**
营养是「料」，不是「水」，两者经常反着走：

| 旧公式的结果 | 现实 |
|---|---|
| 番茄营养低 → 算出 **1** | 咬一口全是水 |
| 玉米营养高 → 反而比番茄补得多 | 谷物，干的 |
| 面包 / 面粉 / 尸体 / 水银 | 全都跟着营养蹭到水分 |

现在改成**三张表 + 两道闸门**，判定路径只有一条：

| 优先级 | 判据 | 结果 |
|---|---|---|
| 1 | **禁列表** `DenyIds` / `DenyCats` | 一律 0 —— 尸体、水银、耐酸保护液、堕天使之血、面包、面粉、面团 |
| 2 | **物品点名** `ById` | 逐个定值（番茄 5、玉米 0） |
| 3 | **类别** `CategoryWater` | 料理按做法分；生鲜水果整类 5 |
| 4 | 其余 | 0 |

料理那一层（做法决定含水量，不看用了什么料）：

| 类别 | 水 | 依据 |
|---|---|---|
| 汤 `meal_soup` | 15 | 原版唯一把失败料理叫作「汁」的（`fail_drink`） |
| 果物料理 `meal_fruit` | 8 | `fail_fruit`「糊状」 |
| 蔬菜料理 `meal_vegi` | 6 | `fail_vegi`「湿黏」 |
| 面条 `meal_noodle` | 6 | `fail_noodle`「泡软塌的面」 |
| 米类 `meal_rice` | 5 | 粥饭本身带水 |
| 其余料理 | 0 | 面包、蛋糕、曲奇、蛋、鱼、肉、套餐都是干的 |

料理必须排在水果前面判：`meal_fruit`（果物料理）可能挂在 `fruit` 之下，
先判 `meal` 才能让它拿到料理的 8，而不是水果的 5。

生鲜只有**水果**整类给 5（`FruitWater`）—— 现实里水果整体就是含水的一类，
苹果、葡萄、橘子不必逐个列。蔬菜与蘑菇不给：同一类里土豆、小麦、玉米与黄瓜
差得太远，只能点名。

**两道闸门**：

- `MinWater = 5` —— **低于 5 一律当 0**。现实里补不出 5 点水的东西
  （一片面包、一把小麦）不该在说明里冒出个「含水：1」，那只会让玩家以为能靠它解渴；
  正是这条把面包、面粉、尸体这些自然挡在外面。
- `MaxFoodWater = 25` —— 单份上限，防止一份大菜顶掉一瓶水。

饮水侧的禁列表同样生效：`GetDrinkGain` 现在收物品 id。
**水银、耐酸保护液（`338`）、堕天使之血都带 `TraitDrink`**，
不挡就会按默认的「药水 = 10」白送水分；水囊也从存下来的来源 id 走同一条判定。
物品 id 全部经 MCP `elin_ids` 核对过（搜中/英/日名任一即可）：

| 名字 | id |
|---|---|
| 尸体 | `meat` / `deadbody` / `_meat`（外加整类 `meat`） |
| 水银 | `mercury` |
| 耐酸保护液 | `338` |
| 堕天使之血 | `blood_angel` |
| 面包 | `666` / `660` / `665` / `671` / `672` / `673` / `1127` |
| 面粉 / 面团 / 面 | `flour` / `dough` / `dough_bread` / `dough_cake` / `noodle` / `tempurako` |
| 番茄 / 玉米 | `tomato` / `corn` |
| 水果 | `apple` `berry` `orange` `grape` `fruit` / `117` 梨 / `118` 菠萝 / `779` 斑斓果 / `785` 柠檬 / `787` 香蕉 / `788` 樱桃 / `805` 牛油果 |
| 多水瓜类（8） | `786` / `791` 西瓜、`792` 哈密瓜、`block_melon` 巨大哈密瓜 |
| 含水蔬菜 | `carrot` `cabbage` `radish` `bamboo_shoot` `vegi` / `697` 茄子 / `781` 笙菜 / `782` 瓜 / `783` 楠瓜 |
| 巨瓜作物 | `block_strawberry` `block_cherry` `block_banana` `block_peach` `block_kiwi` `block_mango` |
| 显式归零 | `corn` 玉米 / `potato` `784` 薯类 / `nuts` `696` 坚果 / `ringoame` 苹果糖 / `1223` 香蕉皮 |

物品说明里会跟着营养显示（见「物品说明」一节）。

### 水分怎么平衡（方法）

**当前所有数值一览：[`design/water-table.md`](design/water-table.md)** ——
喝的 9 档、吃的类别表、39 条点名单、禁列表，每行都标了「相当于几瓶水」。
那张表由 `python tools/dump_water_table.py` 从 `WetnessWater.cs` 正则解析生成，
**改完数值重跑一次即可**，所以不存在「文档写的和代码跑的不是一套数」。

> 手抄表格最常见的失效方式是：改了一次数值忘了改文档，于是照着旧数字
> 再调一轮，最后调成另一个已经改过的值 —— 而且没人发现。
> 这也是为什么这张表要生成而不是维护。

游戏里有 2587 件物品，靠玩是看不到全貌的 —— 你只会注意到「番茄怎么只补 1」
这种碰巧吃到的。所以**逐物品**的核对要靠游戏自己导（`design/water-table.md`
只覆盖规则与点名，不含类别展开）。**v0.13.11 起这项搬进了游戏内的模组配置页**：

模组列表 → Wetness → 配置 → 底部「**含水表导出**」区，两个按钮：

| 按钮 | 导出物 | 什么时候用 |
|---|---|---|
| 导出明细 `water_audit.tsv` | 每件一行，按补水量降序 | 找异常：「谁补得比水还多」「哪些明明多汁却是 0」 |
| 导出类别展开 `water_by_category.md` | 每个类别一节汇总 + 逐件明细 | 查某一类整体怎么定的、有没有漏网的 |

区里还有一行状态显示两份文件上次导出的时间与规模，外加一个「打开 mod 目录」。
点完按钮文件落在 `Package/Mod_Wetness/`，用编辑器或 Excel 打开即可。

<details>
<summary>不想点按钮的话：配置文件的老办法仍然有效</summary>

把 `BepInEx/config/mctrr.wetness.cfg` 里的 `[Debug] DumpWaterAudit = true`，
启动游戏，源表就绪后自动写出明细表（导出一次即止）。
注意这条**只导明细**（`water_audit.tsv`），类别展开目前只有按钮那条路。

</details>

明细表的读法：

```
water  verdict   id        category   nutrition  trait       origin  name_JP
15     category  meal_soup meal_soup  0          Drink                スープ
8      category  meal_fruit meal_fruit 0          Drink                果物料理
5      id        tomato    fruit      12                            トマト
0      deny-id   mercury   ore        0          Drink                水銀
```

- `water` 已过两道闸门（0 = 不补水）
- `verdict` 说明这个数是**谁定的**：`deny-id` / `deny-cat` / `id` / `id-zero` /
  `category` / `none`（`none` = 什么都没命中）
- `trait` 里有 `Drink` 的就是能喝的（水囊收得进、倒得出）
- `origin` 标出被别行当 `_origin` 引用的行 —— 这同时是「按 id 重建会走样」的名单
- `nutrition` 留着做参照：那正是被废掉的旧判据，看一眼就知道它当年错在哪

改数值仍然只动 `WetnessWater` 里的三张表：禁列表、点名单、类别表。
规则改不动的（番茄这种）加点名，整类错的改类别。然后跑
`python tools/dump_water_table.py` 同步 `design/water-table.md`。

三条平衡准则：

| 准则 | 为什么 |
|---|---|
| 一天三顿饭合计 **< 一瓶水（35）** | 吃是辅助、喝才是主力，不然口渴机制被三顿饭顶掉 |
| 单份料理 **≤ 25** | 创作料理一份上千营养，不夹住就是一瓶半水 |
| 刻度统一取 **5 的倍数**，最小可用单位 = 5 | 一口 = 5；不够一口的归零，说明里就不会出现「含水：1」 |
   并重跑 `python tools/dump_water_table.py` 让 `design/water-table.md` 跟上。

#### 改了数值之后

| 改了什么 | 要同步 |
|---|---|
| `WetnessWater` 里任何数值 / 点名 / 禁列表 | `python tools/dump_water_table.py`（必做，否则表格过期） |
| `WetnessWater` 里的**类别** | 同上；另外在配置页导一次「类别展开」看每类明细 |
| 加了新的补水路径（像 v0.13.9 的井水） | 表格里加一行；在 `Plugin.LogPatchedMethods` 的注释里记一笔 |

#### 查 id 不用来回问 MCP

游戏自带的中文语言包里就是一张完整的物品表：

```
<Elin>/Package/_Lang_Chinese/Lang/CN/Game/Thing.xlsx     2589 行 x 21 列
  id | version | filter | name | name_EN | name_JP | detail | ... | name2_JP
```

`tools/dump_thing_ids.py` 把它导成 `tools/thing_ids.tsv`（id + 中/英/日名），
于是 **2587 件物品的三语名字可以离线 grep** —— 比一次问 MCP 几个 id 快得多，
上面那张 id 表就是这么查出来的。

**但它没有 category，也没有营养列**（21 列全是本地化字段，`Food.xlsx` 甚至是空表）——
「某件物品属于哪个类别」只能靠 `water_audit.tsv` 在游戏里打出来。
这也正是含水判定要写成「点名 + 类别」两层的原因：点名这一层完全离线可控，
类别那一层留给原版数据兜底。

> 顺带：`Lang/CN/Game/Stat.xlsx` 列出了全部 **104 个原版 stat id**（含
> `strPhase` / `textPhase` / `textPhase2` 三列）。`StatsWetness` 现在编的
> `id = 99` **不在其中**（列表从 98 跳到 100），所以将来真去注册
> `SourceStat` 行，99 可以直接用，不必再找空位。

### 物品说明里的含水

「营养：11」后面接一行「含水：XX」，两个空格分隔；含水 0 的不显示
（药草、调味料、小麦粉这类说明里干干净净）。药水本身没有营养，
所以是单独一行「含水：10」。

这行靠 transpiler 注入 `Thing.WriteNote` —— 详见 `Patches/WetnessNotePatch.cs`
的注释与 `.tools/ilcheck_note_anchor.py`（锚点 = `ldstr "_nutrition"`）。
注意那三条 brfalse 会跳过 text7 的收尾拼接，注入时必须把跳转标签一并搬走，
否则药水这类物品永远看不到含水行。

### 禁饮

湿润度到「湿润」档就不能再「饮用」，强行饮用只会得到一句「一口也喝不下去了。」

**「湿润」档现在会显示在状态栏上**（v0.12.0 起）。之前它是静默的，
于是「明明一口都喝不下了，状态栏什么都不说」—— 禁饮的档位与显示的档位
必须对齐，否则玩家只能靠猜。

#### 原版喝东西本来就发两句

**别把原版自带的文本当成 mod 的 bug**：`Chara.Drink` 自己每次都发两句话 ——

```csharp
// Elin/Chara.cs:5383
Say("drink", this, t.Duplicate(1));   // 「#1喝下了#2。」
Say("quaff");                          // 「*咕嘟咕嘟*」
```

两个语言键都在 `LangGame`（中文语言包 `Lang/CN/Game/Game.xlsx`：
`drink` = 「#1喝下了#2。」、`quaff` = 「*咕嘟咕嘟*」）。
所以**「饮用一次看到两段文本」是原版一直如此**，本 mod 不会在那里插嘴 ——
它只在**档位跨过边界**时再加一句（`Stats.Mod` 里 `phase != phase2` 才回调）。

顺带记一笔：原版的「饱」文本是 `eat_full`「吃饱了再也吃不下了！」，
而它只出现在 `AI_Eat`（AI 吃东西发现不饿就取消动作）——
玩家自己喝东西、吃满了都不会看到它。所以那跟我们无关，本 mod 也没有对应文本：
「喝不下了」这件事由 `Chara.Drink` 的拦截提示（见上）负责，一个状态一句话就够了。

关键是**只禁「喝」这一个动作**：

- 直接喝 → `Chara.Drink`，被拦
- 投掷药水 → 走 `ActThrow`，**不经过** `Chara.Drink`，仍然生效且照常补水
- 井水的「喝」→ `TraitWell` 自己注册的菜单项，单独拦（**补水也在那一处**，
  不是挂在 `ProcDrink` 上 —— 井水不走那条路，见 v0.13.9）
- **水囊 → 走「使用」，不拦**（v0.6.0 起）

所以偏湿时依然可以投掷，也可以从水囊补水。

**注意**：这条限制对治疗药水同样生效 —— 偏湿时喝不了救命药。原设定就是这样，
但若实测觉得难受，把 `WetnessDef.DrinkLockPhase` 调高一档即可。

### 速度惩罚

挂在 `Chara.RefreshSpeed()` 里，和原版的 burden / stamina / sleepiness / hunger 同处。
数值**照抄原版 hunger 的形状**：

| 档位 | 速度 | 原版对应 |
|---|---|---|
| 干燥（红） | −10% | `hunger` phase 3 Hungry |
| 口渴 | −10% | `hunger` phase 4 VeryHungry |
| 脱水症 | −30% | `hunger` phase 5 Starving |

原版那一坨是：

```csharp
switch (hunger.GetPhase())
{
case 3:
case 4:  num3 -= 10; info?.AddFix(-10, hunger.GetPhaseStr()); break;
case 5:  num3 -= 30; info?.AddFix(-30, hunger.GetPhaseStr()); break;
}
```

即「轻两档各 −10、重档 −30」。湿润度这边三档按同样的形状铺开，
只是方向反过来（干 = 坏，湿 = 好）。

选这个落点的理由：**原版的状态惩罚全都走速度，不走 SP**。这几条都是同一个百分数累加器
（反编译里叫 `num3`，从 100 起算，最后 `_Speed = _Speed * num3 / 100`），并且顺手
`info?.AddFix(-10, ...)`，所以玩家在速度详情面板里能直接看到「湿润度 −30%」这一行。
挂在别处的话，玩家只会觉得「我莫名其妙变慢了」。

**「干燥」（30–39）那一档不给惩罚** —— 它在状态栏本来就不显示，挂了也没人看得见，
反而让玩家莫名其妙变慢。原版 hunger 同样只惩罚它自己会显示出来的那三档。

原版把 hunger / stamina / sleepiness 的速度惩罚**限制在 PC 上**，本 mod 保持一致
（队友的湿润度照常记录，但不吃速度惩罚）。要放开就把 `WetnessSpeedPatch` 里那句
`if (!__instance.IsPC) return;` 删掉。

### 自然回蓝加成（湿润及以上）

湿润（70–79）与喝得过多（80–100）两个档位下，**自然回复 MP 的速度 +10%**。

v0.3.0 里这条是挂在 `StatsMana.Mod` 的 Prefix 上的，那是所有 MP 增减的唯一收口 ——
结果药水、睡觉、祈祷、吸魔全都跟着加了一成，范围明显超了。
现在改成只在原版 `TickConditions` 的自然回复分支里放大那个量：

```csharp
if (EClass.rnd(8) == 0 && mana.value < mana.max)
{
    mana.Mod(EClass.rnd(Evalue(301) / 2 + 1) + 1);
    elements.ModExp(301, 8f);
}
```

这一句内联在 300 多行的 `TickConditions` 里，没有任何独立的方法边界可挂，所以用
transpiler 在 IL 层插入。锚点是 `ldc.i4 301` + `call Card::Evalue` 这一对 ——
`Evalue(301)` 在整个游戏里**只在这一处出现**，所以命中是唯一的
（已用 `.tools/ilcheck_anchor.py` 对着真实 IL 核过）。

+10% 不能用 `amount * 110 / 100`：原版一次自然回复经常只有 1–6 点，整数除法会直接把它
抹成 0，加成等于不存在。所以用 `_cints[29]` 存「百分之一 MP」的零头，攒够 100 才多吐
1 点 —— 长期严格等于自然回复总量的 110%，而且**只在这句回蓝真的触发时才推进**，
不会自己凭空生成 MP。

只有 PC 有（与速度惩罚一致）。

### 呕吐

`Chara.Vomit()` 触发时 −8。喝脏水是最常见的触发源。

顺带一提，原版 `Vomit()` 里藏着一条：`if (hunger.GetPhase() >= 4) DamageHP(9999)` ——
又饿又吐会当场打死。湿润度这边没做对应的东西。

---

## 配置

调参入口在 BepInEx 配置里，首次启动后生成：

```
BepInEx/config/mctrr.wetness.cfg
```

```ini
[General]
# 自然衰减：每多少回合掷一次骰子（默认 50，与原版饥饿一致）
DecayCheckEvery = 50
# 自然衰减：骰子面数，命中才扣 1 点（默认 3，即 1/3 概率，与原版饥饿一致）
# 想把节奏调快用于观察，就把这两个一起改小：5 / 1 = 每 5 回合必掉 1 点
DecayChanceDiv = 3

[Waterskin]
# 水囊：每多少点材质硬度多装一口。改小 = 同样的锭装得更多。
# 15 是从真实硬度谱算出来的：金 25 → 3 口，以太 120 → 10 口（顶到上限）。
HardnessPerDose = 15
# 水囊：容量上限。默认 10。附过魔的水囊会再 +1。
MaxDoses = 10

[Debug]
# 启动后把全部物品的含水判定导出成 water_audit.tsv（放在 mod 目录里）。
# 一次性看清「谁补水、谁不补」。**更推荐用游戏内的按钮**：模组列表 → Wetness → 配置 →
# 底部「含水表导出」区，那边还能导类别展开（这份开关只能导明细）。调完数值就可以关掉。
DumpWaterAudit = false
# 进存档后自动造一个空瓶（随即丢弃），把结果写进日志 —— 不用跑去商店试。
# 通过时只留一行 [空瓶自检] 通过…；失败会把中间值一并打出来。确认无误后可关。
VerifyEmptyBottle = true
# 进存档后造一个井探针（随即销毁），报三档补水量 + actDrink 有没有被包上。
# 井水的补水是「包住原版注册的动作」，没有别的办法能证明它挂对了 —— 必看。
VerifyWellWater = true
```

水囊其余常量（存储键、分档边界等）在 `WetnessDef.cs` 里，没有做成配置项。
材质硬度与口数的对照表在上文「容量：由做它用的那块锭决定」一节，插件本身不打印。

---

## 卸载会不会影响存档

**不会崩档，但水囊会变成橡皮鸭。** 逐条说清楚：

| 存档里留下的东西 | 卸载后 |
|---|---|
| `Chara._cints[26..29]`（湿润度、累加器、初始化标记、回蓝零头） | 原样留着，没人读。重装 mod 后数值接着走 |
| 卡牌上的 `mapInt` / `mapStr`（水囊的 9 个键） | 原样留着，没人读 |
| 水囊这件物品 | **变成 id 869 橡皮鸭**，囊里的药水随之蒸发 |
| 状态栏图标、速度惩罚、回蓝加成、禁饮 | 全部消失（都是运行时效果，不进存档） |

没有写任何独立文件，也没有改动游戏自带的源表 —— 所有数据都落在原版自己的
存档结构里（`_cints` 是 `new int[30]` 的固定数组，`mapInt`/`mapStr` 是 `BaseCard`
上带 `[JsonProperty]` 的字典），所以卸载后剩下的只是几个没人读的整数。

**为什么会变成橡皮鸭**：物品 id 来自本 mod 的 LangMod 源表，卸载后
`ThingGen._Create` 查不到它：

```csharp
CardRow s = EClass.sources.cards.map.TryGetValue(id);
if (s == null) { Debug.LogError("exception: Item not found:" + id); id = "869"; ... }
```

`869` 就是橡皮鸭 —— 原版给「找不到的物品」准备的替身。同样地，
`ApplyTrait()` 里 `ClassCache.Create<Trait>("TraitWaterskin", "Elin")` 也会因为类不再
注册而返回 null，于是回落到基类 `Trait()`。两者都走了兜底分支，**不抛异常**。

**所以**：身上有装着药水的水囊、或者家里摆着水囊时，**卸载前先备份存档**
（`%USERPROFILE%\AppData\LocalLow\Lafrontier\Elin\User\save*.` 之类）。
一旦在无 mod 状态下存过一次盘，那批水囊就永久变成橡皮鸭了，重装也救不回来。
反过来，只在无 mod 时正常游玩、不存水囊，随时装卸都无所谓。

一个远期风险（概率很低，但记在这）：`_cints` 的 26–29 号槽是我们借的空闲位
（原版占用 0–7 / 9 / 10–18 / 20 / 21）。将来游戏若把这几个槽分给新状态，
残留的数值会被当成那个状态的值读出来。真到那一步的处理顺序：

1. 先往前挪 —— 22–25 现在空着，`WetnessDef` 改四个常量即可（**当前首选**）
2. 22–25 也被占满就 `Array.Resize(ref c._cints, 128)`，槽位换到 100+。
   这条路通的：`_cints` 是 public field，`GameIO.jsWriteGame` 用的
   `ShouldSerializeContractResolver`（在 `Plugins.BaseCore.dll`）继承
   `DefaultContractResolver`，public field 参与序列化；而 Json.NET 反序列化
   `int[]` 会按 JSON 长度新建数组，长度不会掉回 30
3. 彻底避开固定索引：改存 `BaseCard.mapInt`（`[JsonProperty("Y")]` 字典），
   但要连 `StatsWetness` 的 `Set(raw, index, chara)` 取值方式一起改

v0.11.1 之前用的是 22–25，那批存档里会留着旧数值。
`WetnessExt.MigrateFromOldSlots()` 在检测到「旧标记亮着、新标记没亮」时把
22–25 一次性清零，之后永不再碰 —— 免得哪天原版拿到 22 号槽读到个残留的 60。

---

## 如何实测

先确认加载正常：看 `BepInEx/LogOutput.log`，应出现

```
[Wetness] 自然回蓝加成已挂载（TickConditions，注入点 +9 条指令）。
[Wetness] Wetness 已挂载 18 处补丁。
[Wetness] Wetness 0.13.11 loaded.
#source workbook ... Mod_Wetness/LangMod/CN/Source.xlsx
[空瓶自检] 通过：Create → potion_empty，origin_potion_empty 首项 → potion_empty。
[井水自检] 通过：actDrink 已包上 WellDrinkWrapper（1 个动作），喝一口会补水量。
```

`已挂载 N 处补丁` 那个数字是**下限检查**，不是功能验证 —— 它只证明补丁类的
类级 `[HarmonyPatch]` 没丢（见「实现备注」的头号坑）。源表那行说明表被读到了。
最后那行 `[空瓶自检]` 是**进存档后**才打的，见下面「空瓶」。

**如果出现这一行**：

```
[Waterskin] 源表 trait 未解析出 TraitWaterskin（实得 Trait），改由补丁接管。
```

说明 `ClassCache.assemblies` 那条路没走通，补丁接管了 —— 功能完全正常，
只是能顺手告诉我们「BepInEx 加载的 dll 不会被自动登记」。**没有这一行**就是源表直连成功。

想把节奏调快方便观察，就把 `DecayCheckEvery` 改成 `5`、`DecayChanceDiv` 改成 `1`
（= 每 5 回合必掉 1 点），退出游戏改完重启即可，不用重编译。

**水囊（v0.8.0）**

1. 在制作台做两个水囊：一个用**铜锭**、一个用**以太锭** ——
   按上面的硬度表，应该分别是 **4 口** 和 **10 口**（格子右下角直接写着 4 / 10，
把鼠标放上去 Tip 里写着「内容：X n/上限」）。
   对不上就调 `HardnessPerDose`（配置项，改完重启即可，不用重编译）。
2. 手持水囊，「使用」指向一瓶药水 → 目标药水消失，**背包里多出一个空瓶**，
格子右下角出现 **1**，Tip 里写着「内容：治疗药水 1/4」。
3. 再倒**同一种**药水 → 2/4、3/4……倒满后再倒会被拒并提示已满。
4. 换**另一种**药水 → 被拒，提示不能混装。
5. **以太抗体药水**（id 1165）→ 倒不进去，而且**不出任何提示**（菜单项根本不出现）。
6. 手持水囊，「使用」指向自己 → 喝下一份，效果与原版喝那瓶药水一致，计数 −1。
7. **关键一条**：把自己喝到「湿润」档，确认「饮用」被拦、井水被拦，
   但**水囊仍然能喝**。
8. 喝空到 0 份 → 右下角数字消失，Tip 里那行「内容：…」也跟着消失，「使用」选项消失。
9. 存档再读档，内容应该还在（数据走 `mapInt` / `mapStr`，随存档序列化）。
10. 对着水囊「混入」→ 原版拖拽网格：拖药水 = 收进来，拖空瓶 = 倒出一瓶。
11. 附魔一个水囊，容量应该 +1（铜锭的那个从 4 变 5）。

**空瓶（v0.13.5 修复项 —— 这三条以前是坏的）**

- **先看日志**：进存档后日志里应出现
  `[空瓶自检] 通过：Create → potion_empty，origin_potion_empty 首项 → potion_empty。`
  这一行同时覆盖两条路（造物入口 + 读档恢复），**不用跑去商店试**。
  失败会变成 `[空瓶自检] 失败：…`，并把「变体是谁 / list 有几行」一并打出来。
  开关是 `[Debug] VerifyEmptyBottle`（默认开）。
- 在**商店**买空瓶 → 拿到的是**空瓶**，不是水囊
- 把空瓶**放在地上**，存档 → 读档（或离开再进这张图）→ 地上还是**空瓶**。
  以前这里有两种坏法：变成水囊，或者整张卡直接消失（`SerializedCards.Restore` 那条）
- 倒进水囊时还回来的那只瓶子也是空瓶

**倒出来的药水（v0.13.6 修复项）**

- 倒一瓶**随机药水**进去（比如从井里舀的、炼金做的）→ 倒出来 → 是**同一种**药水，
  名字一样、喝下去效果一样。以前这里拿到的是别的药水（`potion` 被 `isOrigin` 重定向）
- **看日志对账**：倒进时一行 `[水囊] 倒进：id=… refVal=… trait=…`，
  倒出时一行 `[水囊] 倒出：按 id=… refVal=… 造出 id=… refVal=… trait=…`。
  两行的 `id` / `refVal` / `trait` 应该完全一致；不一致就是重建那一步出的问题。
  开关是 `[Debug] LogPour`（默认开）
- 进存档时还会打印三行 `[药水自检]`：
  ```
  [药水自检] potion: isOrigin=?, chance=?, 变体 N 个 → …
  [药水自检] ThingGen.Create → id=…, refVal=…, trait=…      ← 常规路径（会被重定向）
  [药水自检] CreateExact → id=…, refVal=…, trait=…          ← 倒出走这条（应当就是 potion）
  ```
  第二行 id 不是 `potion` 就说明重定向确实存在，第三行才是倒出真正用的那一行
- 第四行 `[药水自检] 新造的一瓶默认 c_IDTState=5（未鉴定）` —— 这是
  v0.13.7 那个「倒出来还得再鉴定」的直接证据

**口数与鉴定（v0.13.7 修复项）**

- 喝一口水囊 → 物品栏里那个水囊**右下角的数字当场 −1**（以前要等格子因别的原因重建才动）
- 倒一瓶药水出来 → 水囊数字 −1，同样当场更新；快捷键栏里那一格也一样
- 倒一瓶**自己鉴定过的**药水进去 → 再倒出来 → 那瓶**是已鉴定的**，
  没有「未鉴定」标，也不用再鉴定一次。以前这里固定变成未鉴定
- **看日志对账**：倒进的 `idt=` 与倒出的 `idt=` 应当相同（0 = 已鉴定）
- 倒一瓶**没鉴定过**的药水进去 → 倒出来还是未鉴定（状态是照抄的，不是一律改成已鉴定）

**水囊不是材料（v0.13.8 修复项）**

- 打开任一**拿空瓶当材料**的配方（原版药水那一类）→ 空瓶那一格**列不出水囊**，
  按钮角上的数字也不把水囊算进去
- 缺空瓶时那条配方应该显示**做不了**（名字是红的），即使你身上只有水囊
- 以前这里有个很脏的后果：确认制作会把装着一肚子药水的水囊当空瓶**消耗掉**
- 对**井**用「混入」→ 水囊拖不进去（分类若是 `drink` 才需要这一条，
  看 `[水囊自检] … IsChildOf("drink")=…` 那行）
- 反过来要确认没修坏：**做水囊本身的配方照旧**（产物那一侧没动），
  「混入」界面里拖药水/空瓶进水囊也照旧

**井水补水（v0.13.9 修复项）**

- 站在井边喝一口 → 湿润度**当场 +20**（普通未污染）。以前一点不变
- 神圣的井喝一口 → **+35**（等于清水，唯一还在 20 以上的一档）
- 把井污染（往井里丢不干净的东西，`pollution >= 10`）再喝 → **+12**（走脏水档）
- 反复点「喝」同一口菜单 → **一次只补一次**。这条专门验防重复包装：
  `TrySetAct` 每次构建菜单都会被调，包重了会一顿操作补 5 次水
- 井的物品说明里出现「**含水：20**」（`TraitWell` 不是 `TraitDrink`，
  原来会落到食物表拿 0，说明里什么都不显示）
- **看日志**：`[井水自检] 通过：actDrink 已包上 WellDrinkWrapper（1 个动作）…`
  —— 这行是「包上了」的证据；喝一口之后还该有 `[井水] 喝：圣井=…, 污染=…, 余量=… → 补 20`
- 反过来确认没修坏：喝井水的**原有效果照旧**（`ModPotential` / `BadEffect` /
  `Mutation` / 许愿，以及空井那句 `drinkWell_empty`）；余量该减 1 还是减 1

**混入界面（v0.8.0 新增）**

10. 对着水囊交互 → 多出一项「混入」，点开是原版的拖拽网格。
11. 拖一瓶药水进去 → 与「使用」指向药水的效果一样（收进来，+1 口）。
12. 拖一个**空瓶**进去 → 空瓶被灌满成一瓶药水，水囊 −1 口。
13. 装过东西后再开「混入」→ 空瓶仍然能拖（那是倒出口）；
    满囊时药水拖不进去，会弹「已经满了」。
14. 附魔一个水囊 → 容量 +1，名字由原版逻辑显示 `+N`。

**衰减与档位**

15. 在城镇里走约 70 步 —— 状态栏出现红色的「干燥」（20 档）。
16. 继续走：约 80 步「口渴」，约 90 步「脱水症」。每档切换都应该弹一条消息。

**下雨暂停**

17. 站着等一场雨（或调试菜单里直接 `SetCondition(Rain)`），**站在露天**别动 ——
    湿润度应该**纹丝不动**。
18. 走进室内或走到屋檐（有屋顶的房间）下 —— 衰减立刻恢复。
19. 顺带注意：**下雪不算**。冬天雨会整批变成雪，那时候照常消耗 —— 这是跟原版
    `ConWet` 判定保持一致的结果，如果觉得别扭跟我说，把雪也加进暂停条件。

**速度惩罚**

20. 打开角色详情，看速度一栏 —— 应该能看到「湿润度 −10% / −10% / −30%」这行扣分。
21. 喝点水回到 40 以上，扣分应消失。

**自然回蓝**

22. 压进 70 以上（湿润或喝得过多都行）。站着不动把 MP 耗掉，观察回复 ——
    单次回复量小，得看几十回合的累计。掉到 69 以下加成应消失。
23. 顺带确认**喝药水补的 MP 没有变多**。

**盐水 / 呕吐**

24. 喝一瓶盐水（thing id 1142），湿润度应该掉 10。
25. 喝脏水吐一次，数值应该额外掉一截（−8）。

**含水表（v0.12.0）**

26. 吃一个番茄 → 「含水：5」，湿润度 +5。吃一个玉米 → 说明里**没有**含水行。
27. 面包 / 面粉 / 尸体（`meat`）→ 都没有含水行。
28. 喝一口水银 / 耐酸保护液 / 堕天使之血 → 湿润度**不动**（它们带 `TraitDrink`，不挡就会按「药水 = 10」补）。
29. 水果（苹果、葡萄等）→ 「含水：5」；土豆、蘑菇、肉、鱼、蛋 → 没有含水行。
30. 一碗汤 → 「含水：15」，比一份果物料理（8）多。
31. 喝到「湿润」→ **状态栏出现「湿润」**（以前这里是静默的）。
32. 饮用时应该看到**两段**（原版「你喝下了X。」+「*咕嘟咕嘟*」）；
    跨档位时再多一句档位提示 —— 那是原版演出 + 本 mod 的档位消息，不是重复。
33. 想核对全表：**模组列表 → Wetness → 配置 → 底部「含水表导出」**，两个按钮
    （明细 / 类别展开）导出到 mod 目录。（老办法：配置里 `DumpWaterAudit = true` 重启，
    只导明细。）

---

## 编译

```bash
cd /d/work/Elin/5/Wetness
dotnet build Wetness.csproj -c Release --nologo --no-restore \
  '/p:ElinGamePath=E:\SteamLibrary\steamapps\common\Elin'
```

工程里的 `Directory.Build.props` 已把 `ElinGamePath` 默认值设成上面的路径，
同机编译可以省略该参数。

`package/**` 会在编译后由 `CopyAssets` 目标拷进输出目录（即 `Mod_Wetness/`），
所以改完 `LangMod` 的源表要重新编译一次才会部署。
源表是 `tools/build_langmod.py` 生成的，改内容就改那个脚本再跑一遍。

**注意**：本机 `dotnet restore` 不可用（`Value cannot be null (Parameter 'path1')`）。
新建工程时用 `.tools/fixassets.py <工程目录> <工程名>` 从已验证的工程复制
`obj/project.assets.json` 并改写路径即可。详见该脚本头部注释里的坑位说明。

---

## 实现备注（挂载点）

全部复用 Elin 原生机制，没有另起一套：

| 用途 | 挂载点 |
|---|---|
| 数值存储 | `Chara._cints[26]`（原生空闲槽位；10–18 被各 stats 占用） |
| 状态基类 | 继承 `Stats`，复用它的取值与取色机制 |
| 每回合衰减 | `Chara.TickConditions()` 的 Postfix |
| 下雨暂停 | 同上（在同一个 Postfix 里早退，条件照抄原版 `ConWet` 判定） |
| 状态栏 | `WidgetStats._OnActivate()` 的 Postfix，追加 `NotificationStats` |
| 档位静默 | `GetText()` 返回空串 → `NotificationStats.Visible` 为 false |
| 补水 | `FoodEffect.ProcDrink()` 的 Postfix |
| 禁饮 | `Chara.Drink()` 的 Prefix；井水另拦 `TraitWell.TrySetAct` |
| 井水补水 | `TraitWell.TrySetAct` 的 Postfix（包住原版注册的 `DynamicAct.onPerform`）——**不能挂 `ProcDrink`，井水不走那条路** |
| 速度惩罚 | `Chara.RefreshSpeed()` 的 Postfix（反射改私有 `_Speed`） |
| 自然回蓝加成 | `Chara.TickConditions()` 的 **Transpiler**（IL 注入） |
| 呕吐扣减 | `Chara.Vomit()` 的 Postfix |
| 复活后的水分 | `Chara.Revive()` 的 Postfix（照抄原版对 `hunger` 的夹取，改为夹下限） |
| 吃饭补水 | `FoodEffect.Proc()` 的 Postfix |
| 水囊道具 | 源表 `ThingV` 一行 + `TraitWaterskin`；挂载由 `Card.ApplyTrait` 的 Postfix 兜底 |
| 含水判定 | `WetnessWater` 三张表（禁列表 / 点名单 / 类别表）+ 两道闸门；见「水分怎么平衡」 |
| 含水审计 | `WaterAudit.Dump()`（明细）/ `DumpByCategory()`（类别展开）。自动导出走 `Plugin.Update` + `[Debug] DumpWaterAudit`；**推荐用游戏内按钮** —— `Plugin.HookConfigPage` 挂在 `ModPackage.onBuildConfig` 上追加导出区 |
| 配置页导出区 | `Plugin.HookConfigPage`（每帧重试到成功）+ `BuildConfigSection`。只 `+=` 不实现 `IModConfig` |
| 空瓶不被换成水囊 | `WetnessOriginPatch` 三道：`ThingGen._Create` / `Thing.SetSource` / `SpawnListThing.Get` |
| 水囊不被当成材料（尤其空瓶） | `WetnessNoIngredientPatch` 四道：`Card.IsExcludeFromCraft` / `Recipe.Ingredient.IsValidIngredient` / `ThingContainer.GetThingStack` / `TraitWell.CanBlend` |

水囊内部数据用 `Card.mapInt` / `mapStr`（继承自 `BaseCard`，带 `[JsonProperty]`）。

几个坑，写在这免得下次再踩：

- **补丁类必须挂「类级」`[HarmonyPatch]`，不然整个类被静默跳过。（头号坑）**
  本机 BepInEx 6 带的 HarmonyX，`PatchClassProcessor` 的构造函数只读**类级**属性，
  类上一个都没有就直接 `return`，类里所有方法级 `[HarmonyPatch]` 全部作废 ——
  transpiler 连「找不到锚点」都不会打，因为压根没被执行。
  v0.10.0 之前吃 / 喝 / 速度 / 回蓝 / 含水行 / 水囊兜底这六处全躺在这里：
  编译通过、日志干净、功能全无。修法是给类补一个**空的** `[HarmonyPatch]`（目标仍由
  方法级属性给，空类级属性不会触发批量模式）。`Plugin.LogPatchedMethods()` 会在启动时
  数一遍实际生效的补丁数并打印 —— 少于 18 会打 warning。
- **`Chara._Speed` 是 `private long`**，`RefreshSpeed` 没留任何可覆写的缝（惩罚项是硬编码
  switch），只能算完之后反射改字段。
- **速度 Postfix 必须以 `dirtySpeed` 为闸**。`RefreshSpeed` 开头有一个早退分支
  （`currentZone != EClass._zone` 时把 `dirtySpeed` 置回 `true` 就返回），那条路上 `_Speed`
  根本没被重算；不加闸会反复对同一个旧值乘系数，速度被越乘越小。
- **transpiler 注入静态方法调用时，参数顺序得倒着排**：实参按声明顺序压栈、栈顶给最后一个
  参数。注入点栈上正好是 `[mana, 回复量]`，只补一条 `ldarg.0` 的话，`Chara` 就成了栈顶，
  所以 helper 必须写成 `Boost(int amount, Chara c)`，不能写成 `Boost(Chara c, int amount)`。
- **`Stats.OnChangePhase` 基类已实现**（读 `SourceStat.textPhase` / `textPhase2`、
  按 `invert` 判方向、`Msg.SetColor` + `Say` + `PopText`）。现在覆写它只是因为湿润度没有
  自己的 `SourceStat` 行；等以后注册真源表行做多语言，这个覆写可以整个删掉。
- **`Card.sourceCard.id` 覆盖所有卡片类型**，在 `ApplyTrait` 里用它做判断，比转成
  `SourceThing.Row` 安全（角色卡也能走到这个方法）。
- **`Card.ModNum(-1)` 数量归零时会自己 `Destroy()`**，消耗物品不需要额外处理。

其他已勘察确认、后续可能用到的入口：

- 投掷走 `ActThrow` → `FoodEffect.ProcDrink`，**不经过** `Chara.Drink`
- 濡湿：`Chara.isWet`（由 `ConWet` 条件维护，原版下雨每回合 `AddCondition<ConWet>(20)`）；
  投掷药水命中也会 `AddCondition<ConWet>()`
- 液体分类：`EffectId.DrinkWater` / `DrinkWaterDirty` / `DrinkRamune` / `DrinkMilk` / `Booze` / `SaltWater`
- 水源判定：`ActClearWater.HasWaterSource(Point)`（井 / 浴场 / 若干固定 id）
- 装水掷骰：`TraitPotionEmpty.OnUse` —— 圣井给圣水，其他任何水源给随机药水
- 干渴之壶：`TraitToolWaterPot` + `TaskDrawWater` / `TaskPourWater`，
  抽 `IsTopWater` 地块的水存成 `c_charges`（上限 `20 + 材质硬度×2`），再倒到土上
- 空瓶外观的槽位：`potion_empty`（`SourceThingV` 的 `_origin` 指向它）

---

## 离线校验 transpiler 锚点

transpiler 最容易的翻车方式是「锚点没命中」或「命中了错误的调用」——
两种情况编译期都看不出来，只会在游戏里静默失效。

```bash
# 1. 从游戏里那份 Elin.dll 导出 IL
ilspycmd -il -t Chara "E:\SteamLibrary\steamapps\common\Elin\Elin_Data\Managed\Elin.dll" > chara_il.txt

# 2. 校验锚点唯一性与注入点
python "D:\work\Elin\5\needname\.tools\ilcheck_anchor.py" chara_il.txt "Chara::TickConditions" 301
```

脚本会把 C# 里那段匹配逻辑照搬到 IL 文本上跑一遍，输出锚点命中数与注入位置。
本机不能编译临时探针程序（`csc` 被安全策略拦、`dotnet restore` 不可用），
所以只能这样离线核。流程细节见 skill `elin-harmony-transpiler`。

---

## 核对编译产物

**别信 ilspycmd 往管道里输出的非 ASCII** —— 它会按控制台代码页编码，
中文会变成 `£¨` 之类的乱码，看着像源码被写坏了，其实 dll 里是对的。

要确认字符串，直接探 dll 字节（`#US` 堆用 UTF-16LE 存字符串）：

```bash
python -c "
import re
d = open(r'...\Wetness.dll','rb').read()
for s in ['（', '水囊', '脱水症']:
    print(s, len(re.findall(re.escape(s.encode('utf-16-le')), d)))
"
```

要看代码逻辑，导成 UTF-8 文件再读：

```bash
ilspycmd -o <输出目录> <dll>     # 生成 <名>.decompiled.cs
```
