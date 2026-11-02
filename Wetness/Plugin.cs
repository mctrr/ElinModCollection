using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace Wetness;

public static class ModInfo
{
    public const string Guid = "mctrr.wetness";
    public const string Name = "Wetness";
    public const string Version = "0.13.12";
}

[BepInPlugin(ModInfo.Guid, ModInfo.Name, ModInfo.Version)]
internal class Plugin : BaseUnityPlugin
{
    internal static Plugin Instance;

    internal static ConfigEntry<int> CfgDecayCheckEvery;

    internal static ConfigEntry<int> CfgDecayChanceDiv;

    internal static ConfigEntry<int> CfgDosesPerHardness;

    internal static ConfigEntry<int> CfgMaxSkinDoses;

    /// <summary>倒进水囊时是否把倒空的瓶子留下。</summary>
    internal static ConfigEntry<bool> CfgReturnBottle;

    /// <summary>含水表审计开关，见 WaterAudit。</summary>
    internal static ConfigEntry<bool> CfgDumpWaterAudit;

    /// <summary>空瓶自检开关，见 VerifyEmptyBottle。</summary>
    internal static ConfigEntry<bool> CfgVerifyEmptyBottle;

    /// <summary>倒进/倒出的诊断日志开关，见 LogPourIn / LogPourOut。</summary>
    internal static ConfigEntry<bool> CfgLogPour;

    /// <summary>井水自检开关，见 VerifyWellWater。</summary>
    internal static ConfigEntry<bool> CfgVerifyWellWater;

    private bool _auditDumped;

    private bool _bottleVerified;

    private bool _wellVerified;

    /// <summary>配置页追加区只挂一次 —— LayerModConfig.Show 每次都会重新 Invoke 那个链。</summary>
    private bool _configHooked;

    /// <summary>自然衰减：每多少回合掷一次骰子（原版 hunger 是 50）。</summary>
    internal static int DecayCheckEvery => CfgDecayCheckEvery?.Value ?? WetnessDef.DecayCheckEvery;

    /// <summary>自然衰减：骰子面数，rnd(N)==0 才扣 1 点（原版 hunger 是 3）。</summary>
    internal static int DecayChanceDiv => CfgDecayChanceDiv?.Value ?? WetnessDef.DecayChanceDiv;

    /// <summary>水囊：每多少点材质硬度多装一口。</summary>
    internal static int DosesPerHardness => CfgDosesPerHardness?.Value ?? WetnessDef.HardnessPerDose;

    /// <summary>
    /// 倒进水囊时把倒空的瓶子留下。
    /// 原版喝药水是不留瓶子的（Chara.Drink 直接 ModNum(-1)），
    /// 不留的话「倒出来」永远缺一个容器 —— 两头接不上。默认开。
    /// 配置缺失时也当作开（Awake 之前被调用到）。
    /// </summary>
    internal static bool ReturnEmptyBottle => CfgReturnBottle == null || CfgReturnBottle.Value;

    /// <summary>水囊：容量上限（附魔奖励可再越过它 1 口）。</summary>
    internal static int MaxSkinDoses => CfgMaxSkinDoses?.Value ?? WetnessDef.MaxSkinDoses;

    private void Awake()
    {
        Instance = this;

        // 默认与原版 hunger 完全一致：每 50 回合 1/3 概率 -1，平均 150 回合掉 1 点。
        // 想快速观察状态变化就把两个值一起调小（比如 5 / 1 = 每 5 回合必掉 1 点）。
        CfgDecayCheckEvery = Config.Bind(
            "General",
            "DecayCheckEvery",
            WetnessDef.DecayCheckEvery,
            "自然衰减：每多少回合掷一次骰子。默认 50（与原版饥饿一致）。调小 = 渴得更快。");

        CfgDecayChanceDiv = Config.Bind(
            "General",
            "DecayChanceDiv",
            WetnessDef.DecayChanceDiv,
            "自然衰减：骰子面数，命中才扣 1 点。默认 3（即 1/3 概率，与原版饥饿一致）。调小 = 渴得更快。");

        // 水囊容量 = MinSkinDoses + 材质硬度 / HardnessPerDose（见 WetnessDef）。
        // 15 是按真实硬度谱定的：金 25 → 3 口，铜 34 → 4 口，铁 56 → 5 口，
        // 钢 67 → 6 口，秘银 98 → 8 口，精金 115 → 9 口，以太 120 → 10 口（顶到上限）。
        CfgDosesPerHardness = Config.Bind(
            "Waterskin",
            "HardnessPerDose",
            WetnessDef.HardnessPerDose,
            "水囊：每多少点材质硬度多装一口。改小 = 同样的锭装得更多。"
            + "默认 15：金锭 3 口、铜锭 4 口、铁锭 5 口、钢锭 6 口、秘银 8 口、以太 10 口。"
            + "想让某种锭刚好顶到上限，把它设成「那种锭的硬度 / 8」。");

        CfgMaxSkinDoses = Config.Bind(
            "Waterskin",
            "MaxDoses",
            WetnessDef.MaxSkinDoses,
            "水囊：容量上限。默认 10。附过魔的水囊会再 +1。");

        CfgReturnBottle = Config.Bind(
            "Waterskin",
            "ReturnEmptyBottle",
            true,
            "倒进水囊时把倒空的瓶子留下。默认开 —— 不留的话水囊里的东西就再也倒不出来了"
            + "（倒出需要一个空容器）。关掉即与原版一致：喝药水不留瓶。");

        CfgDumpWaterAudit = Config.Bind(
            "Debug",
            "DumpWaterAudit",
            false,
            "启动后把全部物品的含水判定导出成 water_audit.tsv（放在 mod 目录里）。"
            + "用来一次性看清「谁补水、谁不补」，调完数值就可以关掉。");

        CfgVerifyEmptyBottle = Config.Bind(
            "Debug",
            "VerifyEmptyBottle",
            true,
            "进存档后自动造一个空瓶（随即丢弃），把结果写进日志 ——"
            + "用来确认 isOrigin 重定向的三道拦截真的生效，不必跑去商店试。"
            + "同一段还会报一次药水行（potion）的 isOrigin / 变体情况，"
            + "那是「倒出来不是原来那瓶」的诊断依据。确认无误后可以关掉。");

        CfgLogPour = Config.Bind(
            "Debug",
            "LogPour",
            true,
            "倒进水囊 / 倒出来时把「存了什么、造出了什么」写一行日志。"
            + "喝井水也复用这个开关（那一行是 [井水]）。"
            + "用来对账「原物 → 囊中 → 倒出」三段是否一致，查完可以关掉。");

        CfgVerifyWellWater = Config.Bind(
            "Debug",
            "VerifyWellWater",
            true,
            "进存档后造一个井探针（随即销毁），把三档补水量与"
            + "「actDrink 有没有被包上 WellDrinkWrapper」写进日志 ——"
            + "确认喝井水真的补水。确认无误后可以关掉。");

        Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), ModInfo.Guid);
        RegisterTraitAssembly();
        LogPatchedMethods();
        LogInfo($"{ModInfo.Name} {ModInfo.Version} loaded.");
    }

    /// <summary>
    /// 让 Elin 的 ClassCache 能在本程序集里找 trait 类。
    ///
    /// 源表的 trait 列解析成 Card.ApplyTrait 里的一句：
    ///     ClassCache.Create&lt;Trait&gt;("Trait" + 名字, "Elin")
    /// 它先试 Type.GetType(名字 + ", Elin")，失败后遍历 ClassCache.assemblies
    /// 逐个试 Type.GetType(名字 + ", " + 程序集) —— 所以只要把本程序集登记进去，
    /// 源表里写 trait = Waterskin 就能直接找到 TraitWaterskin
    /// （这也是为什么 TraitWaterskin 必须放在全局命名空间：那个回退只认简单类型名）。
    ///
    /// 注意 ClassCache 会把「查不到」的结果也缓存下来，一旦先查后登记就永远查不到；
    /// 所以这里在 Awake 里尽早登记。万一失败也不能连累整个 mod，用 catch 兜住 ——
    /// WetnessWaterskinPatch 会接管，功能不受影响。
    /// </summary>
    private static void RegisterTraitAssembly()
    {
        try
        {
            ClassCache.assemblies.Add(typeof(Plugin).Assembly.GetName().Name);
        }
        catch (System.Exception e)
        {
            LogWarn("登记 ClassCache.assemblies 失败，源表 trait 列将不可用（改由 WetnessWaterskinPatch 兜底）：" + e.Message);
        }
    }

    // 材质硬度自检（Update 轮询 + DumpMaterialHardness）已删除。
    //
    // 它原本的用途是「在游戏外读不出材质硬度，只好让游戏自己报数」。后来发现
    // 硬度值在公开资料里就有（ylvapedia 的 Elin:Materials、官方 Google Sheet），
    // 系数也就据此定稿在 WetnessDef.HardnessPerDose 的注释里，不再需要这层自检。
    //
    // 留着反而有害：每次启动往 BepInEx 日志刷一百多行，排查别的问题时全是噪音。
    // 将来若真要再核对「游戏实载值 vs 资料值」，能跑的代码片段记在
    // skill `elin-custom-item` 的 8.6 节。

    /// <summary>
    /// 启动后数一遍「本 mod 真正挂上了多少处补丁」，写一行日志。
    ///
    /// 为什么非数不可：HarmonyX 的 PatchClassProcessor 只认**类级** [HarmonyPatch]，
    /// 类上没有时整个类被静默跳过 —— 方法级属性、transpiler 里的日志全都不会执行，
    /// 表现出来就是「编译通过、日志干净、功能全无」。v0.10.0 之前有六处就这么躺着：
    /// 吃、喝、速度、回蓝、含水行全都没挂上，而日志里连一句警告都没有。
    /// 有了这一行，数字不对一眼就能看出来，不必再逐个去猜。
    ///
    /// 正常是 18 处（同一方法上的多个补丁算一处）：
    /// FoodEffect.Proc / ProcDrink、Chara.Drink / RefreshSpeed / TickConditions /
    /// Vomit / Revive、TraitWell.TrySetAct、WidgetStats._OnActivate、
    /// Card.ApplyTrait、Thing.WriteNote、ThingGen._Create、
    /// Thing.SetSource、SpawnListThing.Get（后两个见 WetnessOriginPatch）、
    /// Card.IsExcludeFromCraft、Recipe.Ingredient.IsValidIngredient、
    /// ThingContainer.GetThingStack、TraitWell.CanBlend（后四个见 WetnessNoIngredientPatch）。
    /// </summary>
    private static void LogPatchedMethods()
    {
        int count = 0;
        foreach (MethodBase method in Harmony.GetAllPatchedMethods())
        {
            HarmonyLib.Patches info = Harmony.GetPatchInfo(method);
            if (info != null && info.Owners.Contains(ModInfo.Guid))
            {
                count++;
            }
        }

        if (count < 18)
        {
            LogWarn($"{ModInfo.Name} 只有 {count} 处补丁生效（预期 18）—— 多半是某个补丁类少了类级 [HarmonyPatch]。");
            return;
        }
        LogInfo($"{ModInfo.Name} 已挂载 {count} 处补丁。");
    }

    /// <summary>
    /// 等源表就绪后跑一些一次性的自检／导出。
    /// Awake 时 EClass.sources 还没加载，只能轮询；各自跑过就置位，不再回头。
    /// </summary>
    private void Update()
    {
        HookConfigPage();

        if (!_bottleVerified && (CfgVerifyEmptyBottle?.Value ?? false))
        {
            VerifyEmptyBottle();
        }

        if (!_wellVerified && (CfgVerifyWellWater?.Value ?? false))
        {
            VerifyWellWater();
        }

        DumpWaterAudit();
    }

    /// <summary>
    /// 在原版模组配置页末尾追加一块「导出」区。
    ///
    /// 为什么用 `+=` 挂钩子而不是实现 `IModConfig`：官方胶水里
    /// `if (plugin is IModConfig) 自绘; else note.AddAll(plugin.Config);`
    /// —— 一旦实现接口就得把每个 ConfigEntry 重新画一遍，漏一步整页设置消失。
    /// `onBuildConfig` 是 Delegate 链，官方先挂（画完所有 ConfigEntry），
    /// 我们后挂（追加），顺序有保证且原有设置一项不少。
    ///
    /// 时机：`RegisterAll` 由 `Core.StartCase` 触发（真的开一局才跑），
    /// ModManager 也建得比 BepInEx Awake 晚 —— 所以每帧重试，成功即止。
    /// </summary>
    private void HookConfigPage()
    {
        if (_configHooked)
        {
            return;
        }
        if (!SourcesReady())
        {
            return;
        }

        ModPackage pkg = ModUtil.FindFileProviderPackage(new FileInfo(Info.Location));
        if (pkg == null)
        {
            return;
        }

        pkg.onBuildConfig += BuildConfigSection;
        _configHooked = true;
        LogInfo("[Wetness] 模组配置页已挂上导出区（含水明细 / 类别展开 / 打开目录）。");
    }

    /// <summary>
    /// 追加区里画的东西：状态行 + 三个按钮。
    ///
    /// 每次打开配置页都会重新 Invoke 一遍，控件句柄随之重建，
    /// 所以这里不缓存任何 UIItem —— 状态直接现算。
    /// </summary>
    private void BuildConfigSection(UINote note)
    {
        try
        {
            note.Space();
            note.AddHeader("含水表导出");
            note.AddText(
                "2587 件物品的含水判定全在这里 —— 「明细」按补水量排行（找异常），"
                + "「类别展开」按类别分组并逐件列出（查某一类整体怎么定的）。"
                + "导出物落在 mod 目录里，用编辑器或 Excel 打开。",
                FontColor.Passive);

            note.AddTopic(
                "明细 water_audit.tsv",
                WaterAudit.LastDumpAt.Length > 0
                    ? $"{WaterAudit.LastDumpAt} · {WaterAudit.LastDumpRows} 件"
                    : "未导出");

            note.AddTopic(
                "类别展开 water_by_category.md",
                WaterAudit.LastCatDumpAt.Length > 0
                    ? $"{WaterAudit.LastCatDumpAt} · {WaterAudit.LastCatDumpCats} 个类别"
                    : "未导出");

            note.AddButton("导出明细 water_audit.tsv（按补水量排行）", delegate
            {
                WaterAudit.Dump();
                EClass.ui.Say(WaterAudit.LastDumpAt.Length > 0
                    ? $"已导出含水明细：{WaterAudit.LastDumpAt} · {WaterAudit.LastDumpRows} 件"
                    : "导出失败，详见 BepInEx 日志。");
            });

            note.AddButton("导出类别展开 water_by_category.md", delegate
            {
                WaterAudit.DumpByCategory();
                EClass.ui.Say("已导出类别展开，见 mod 目录里的 water_by_category.md。");
            });

            note.AddButton("打开 mod 目录", delegate
            {
                Util.ShowExplorer(WaterAudit.OutputDir());
            });
        }
        catch (System.Exception e)
        {
            // 配置页画挂了不能连累整页设置，也别把 mod 带走。
            LogWarn("[Wetness] 配置页导出区绘制失败（原有设置不受影响）：" + e.Message);
        }
    }

    /// <summary>
    /// 等源表就绪后导出一次含水审计（见 WaterAudit）。
    /// </summary>
    private void DumpWaterAudit()
    {
        if (_auditDumped || CfgDumpWaterAudit == null || !CfgDumpWaterAudit.Value)
        {
            return;
        }
        if (!SourcesReady())
        {
            return;
        }

        _auditDumped = true;
        WaterAudit.Dump();
    }

    /// <summary>
    /// 空瓶自检：造一个 `potion_empty`，看拿回来的是不是真的空瓶。
    ///
    /// 为什么非要有这一层：`_origin = potion_empty` 点亮的那个 `isOrigin` 标志，
    /// 被游戏当成「这是基础行，去挑一个变体」用了**三处** ——
    /// `ThingGen._Create` / `Thing.SetSource`（`Card.Create` 内部）/
    /// `SpawnListThing.Get`（读档恢复走的那条）。只拦一处等于没修，
    /// 而「补丁数够了」证明不了拦对了层：v0.13.4 就是这么翻的车 ——
    /// 日志里 12 处补丁一切正常，产物照旧是水囊。
    ///
    /// 这里把两条路都走一遍：
    ///   ① `ThingGen.Create("potion_empty")` —— 覆盖 _Create 与 SetSource
    ///   ② `SpawnListThing.Get("origin_potion_empty", …)` —— `SerializedCards.Restore` 走的那条
    /// 通过只留一行，失败则把中间值（变体是谁、list 有几行）一并打出来。
    ///
    /// v0.13.6 起同一段还会报一次 `potion` 行 —— 倒出来的药水不对，
    /// 就靠那三行判断「potion 是不是被 isOrigin 重定向了」。
    /// </summary>
    private void VerifyEmptyBottle()
    {
        if (!SourcesReady() || EClass.game?.cards == null)
        {
            return;   // 还没进存档，下一帧再看
        }

        _bottleVerified = true;

        try
        {
            string variant = EClass.sources.cards.firstVariations.TryGetValue(
                WetnessDef.EmptyBottleId, out CardRow first)
                ? first?.id
                : "(无)";

            Thing probe = ThingGen.Create(WetnessDef.EmptyBottleId);
            string got = probe?.id ?? "(null)";
            probe?.Destroy();

            SourceThing.Row bottleRow = EClass.sources.things.map[WetnessDef.EmptyBottleId];
            SpawnList list = SpawnListThing.Get(
                "origin_" + WetnessDef.EmptyBottleId, a => a.origin == bottleRow);
            string firstInList = list.rows.Count > 0 ? list.GetFirst().id : "(空表)";

            if (got == WetnessDef.EmptyBottleId && firstInList == WetnessDef.EmptyBottleId)
            {
                LogInfo("[空瓶自检] 通过：Create → potion_empty，origin_potion_empty 首项 → potion_empty。");
            }
            else
            {
                LogWarn(
                    $"[空瓶自检] 失败：Create 拿到 {got}；origin_potion_empty 首项 {firstInList}；"
                    + $"potion_empty 的变体是 {variant} —— 三道拦截没拦住（见 WetnessOriginPatch）。");
            }
        }
        catch (System.Exception e)
        {
            // 自检本身不能连累游戏
            LogWarn("[空瓶自检] 抛异常（自检失败，不影响 mod 其余功能）：" + e);
        }

        try
        {
            DumpPotionRow();
        }
        catch (System.Exception e)
        {
            LogWarn("[药水自检] 抛异常（不影响 mod 其余功能）：" + e);
        }

        try
        {
            DumpWaterskinRow();
        }
        catch (System.Exception e)
        {
            LogWarn("[水囊自检] 抛异常（不影响 mod 其余功能）：" + e);
        }
    }

    /// <summary>
    /// 井水自检：喝井水到底补不补水。
    ///
    /// 为什么非要单独验：这条链路上**没有 `已挂载 N 处补丁` 能作数**。
    /// 井水的补水不是挂在 `FoodEffect.ProcDrink` 上（那条路径井水根本不走），
    /// 而是把 `TraitWell` 注册的 `DynamicAct.onPerform` 包一层 ——
    /// 补丁挂上了不代表包住了，包住了也不代表喝到的那一口真的加了水。
    ///
    /// 这里不真的去喝一口井（那会消耗 Charges），而是问三件事：
    ///   ① 三档数值对不对（普通 / 圣井 / 污染）
    ///   ② 包装标记类在不在（说明下次构建菜单会套上 wrapper）
    ///   ③ 说明里的含水行认不认井（`GetNoteWater` 走过 `TraitWell` 分支）
    /// </summary>
    private void VerifyWellWater()
    {
        _wellVerified = true;

        LogInfo(
            $"[井水自检] 三档：圣井={WetnessWater.GetWellGain(true, false)}"
            + $"（应 {WetnessWater.HolyWellGain}）、普通={WetnessWater.GetWellGain(false, false)}"
            + $"（应 {WetnessWater.WellGain}）、污染={WetnessWater.GetWellGain(false, true)}"
            + $"（应 {WetnessWater.WaterDirtyGain}）");

        // 探针：造一口普通井，看包装与说明两件事。
        Thing well = null;
        try
        {
            well = ThingGen.Create("well", -1, 1);
        }
        catch (System.Exception e)
        {
            LogWarn("[井水自检] 造井探针失败（不影响 mod 其余功能）：" + e.Message);
        }

        if (well?.trait is not TraitWell tw)
        {
            LogInfo("[井水自检] 源表里没有 well 这一行（或不是 TraitWell），跳过行为验证 —— 三档数值已打印。");
            return;
        }

        LogInfo(
            $"[井水自检] 井探针：IsHoly={tw.IsHoly}, polluted={tw.polluted}, "
            + $"说明含水={WetnessWater.GetNoteWater(well)}"
            + $"（应 {WetnessWater.GetWellGain(tw.IsHoly, tw.polluted)}）");

        // 真正验包装：走一次游戏自己的注册逻辑，看 actDrink 的 onPerform 有没有被套上。
        // 包装在 Postfix 上，这里调 TrySetAct 会连带把 Postfix 跑一遍。
        try
        {
            ActPlan plan = new ActPlan();
            tw.TrySetAct(plan);

            bool wrapped = false;
            for (int i = 0; i < plan.list.Count; i++)
            {
                if (plan.list[i].act is DynamicAct da && da.id == "actDrink" && da.onPerform != null)
                {
                    wrapped = da.onPerform.Target?.GetType().Name == "WellDrinkWrapper";
                    break;
                }
            }

            LogInfo(
                wrapped
                    ? $"[井水自检] 通过：actDrink 已包上 WellDrinkWrapper（{plan.list.Count} 个动作），喝一口会补水量。"
                    : "[井水自检] 失败：actDrink 没有被包装 —— WetnessDrinkPatch 的 Postfix 没跑起来，"
                      + "喝井水仍然不加水。");
        }
        catch (System.Exception e)
        {
            LogWarn("[井水自检] 抛异常（不影响 mod 其余功能）：" + e);
        }
        finally
        {
            well.Destroy();
        }
    }

    /// <summary>
    /// 水囊自检：分类是什么、能不能被当材料。
    ///
    /// 两件事都只能由游戏自己回答：
    ///
    /// 1. **分类**。水囊是 ThingV 变体，分类从空瓶继承。若它是 `drink` 的子类，
    ///    那么除了配方材料，井的混合界面也会收它（`TraitWell.CanBlend` 的判据
    ///    就是 `category.IsChildOf("drink")`），拖进去就直接销毁。
    ///    `IsChildOf("drink")` 打出来是 True，说明 `WetnessNoIngredientPatch`
    ///    里那道针对井的拦截不是白写的。
    ///
    /// 2. **`IsExcludeFromCraft`**。这是「配方不收水囊」的主修，可它是**行为**，
    ///    `已挂载 N 处补丁` 那个数字证明不了它拦对了 —— 拿个探针直接问一遍
    ///    才有意义（当初空瓶那起就是被那个数字骗过去的）。
    /// </summary>
    private static void DumpWaterskinRow()
    {
        SourceThing.Row bottle = EClass.sources.things.map.TryGetValue(WetnessDef.EmptyBottleId, out SourceThing.Row b)
            ? b
            : null;

        Thing probe = Wetness.Patches.WetnessOriginPatch.CreateExact(WetnessDef.WaterskinId, 1);
        if (probe == null)
        {
            LogWarn($"[水囊自检] 造不出水囊探针（源表里没有 {WetnessDef.WaterskinId} 这一行？），跳过。");
            return;
        }

        string cat = probe.category == null ? "(无)" : probe.category.id;
        bool isDrink = probe.category != null && probe.category.IsChildOf("drink");
        LogInfo(
            $"[水囊自检] 分类={cat}（空瓶的分类={bottle?.category ?? "?"}），"
            + $"IsChildOf(\"drink\")={isDrink}");

        bool excluded = probe.IsExcludeFromCraft(new Recipe.Ingredient
        {
            id = WetnessDef.EmptyBottleId,
            req = 1
        });
        LogInfo(
            $"[水囊自检] IsExcludeFromCraft(空瓶材料)={excluded} —— 应为 True；"
            + "False 说明 WetnessNoIngredientPatch 没生效，配方仍会把水囊当空瓶收走。");

        probe.Destroy();
    }

    /// <summary>
    /// 药水自检：`potion` 这一行到底会不会被 `isOrigin` 重定向。
    ///
    /// 为什么需要：`TraitWaterskin.PourOut` 要「把囊里那口还原成原来那瓶」，
    /// 而这件事只有在「照着原 id 造出来的就是原来那一行」的前提下成立。
    /// 一旦 `potion` 是基础行（源表里有任何一行写 `_origin = potion`），
    /// `ThingGen.Create("potion")` 就会去挑变体 —— 造出来的是别的药水，
    /// 而且日志上一切正常，只有玩家能看出「倒出来的不是原来那瓶」。
    ///
    /// 三行结论直接对照：
    /// - 第一行：`potion` 行本身的状态 + 它有几个变体（变体是谁）
    /// - 第二行：游戏常规路径造出来的东西
    /// - 第三行：`CreateExact` 造出来的东西（倒出时走的就是这条）
    /// 第二行与第三行 id 不同就说明重定向确实发生了，第三行才是对的。
    /// </summary>
    private static void DumpPotionRow()
    {
        const string PotionId = "potion";

        SourceThing.Row row = Wetness.Patches.WetnessOriginPatch.Resolve(PotionId);
        if (row == null)
        {
            LogWarn($"[药水自检] 源表里没有 {PotionId} 这一行，跳过。");
            return;
        }

        string variants = "";
        int count = 0;
        foreach (SourceThing.Row r in EClass.sources.things.rows)
        {
            if (!ReferenceEquals(r.origin, row))
            {
                continue;
            }
            count++;
            if (count <= 6)
            {
                variants += (variants.Length == 0 ? "" : ", ") + $"{r.id}(chance={r.chance})";
            }
        }

        LogInfo(
            $"[药水自检] {PotionId}: isOrigin={row.isOrigin}, chance={row.chance}, "
            + $"变体 {count} 个{(count > 0 ? " → " + variants : "")}");

        Thing normal = ThingGen.Create(PotionId, -1, WetnessDef.PourOutLevel);
        LogInfo($"[药水自检] ThingGen.Create → id={normal?.id}, refVal={normal?.refVal}, trait={TraitName(normal)}");
        normal?.Destroy();

        Thing exact = Wetness.Patches.WetnessOriginPatch.CreateExact(PotionId, WetnessDef.PourOutLevel);
        LogInfo($"[药水自检] CreateExact → id={exact?.id}, refVal={exact?.refVal}, trait={TraitName(exact)}");
        LogInfo(
            $"[药水自检] 新造的一瓶默认 c_IDTState={exact?.c_IDTState}"
            + $"（0=已鉴定，5=未鉴定）—— 倒出时水囊会把自己记下的值覆写回来。");
        exact?.Destroy();
    }

    private static string TraitName(Thing t)
        => t?.trait == null ? "(无)" : t.trait.GetType().Name;

    /// <summary>
    /// 倒进去时记一行账：原物是谁、记下了什么。
    /// 与 <see cref="LogPourOut"/> 成对，用来核对「原物 → 囊中 → 倒出」三段。
    /// </summary>
    internal static void LogPourIn(Thing src, string ident, string label)
    {
        if (!(CfgLogPour?.Value ?? false))
        {
            return;
        }
        LogInfo(
            $"[水囊] 倒进：id={src?.id}, refVal={src?.refVal}, bless={src?.blessedState}, "
            + $"idt={src?.c_IDTState}, trait={TraitName(src)}, 显示名={label}, ident={ident}");
    }

    /// <summary>
    /// 倒出来时记一行账：照什么造的、造出了什么。
    /// id / refVal / idt / trait 里任何一项和倒进那行对不上，就是这里出的问题。
    /// </summary>
    internal static void LogPourOut(string srcId, int refVal, Thing made)
    {
        if (!(CfgLogPour?.Value ?? false))
        {
            return;
        }
        LogInfo(
            $"[水囊] 倒出：按 id={srcId}, refVal={refVal} 造出 "
            + $"id={made?.id}, refVal={made?.refVal}, idt={made?.c_IDTState}, "
            + $"trait={TraitName(made)}, 名字={made?.Name}");
    }

    /// <summary>
    /// 喝井水记一行账：这次喝到多少、按哪一档算的。
    /// 挂在 [Debug] LogPour 上 —— 井水的补水走的是另一条链路（不是 ProcDrink），
    /// 复用同一个开关省一个配置项。
    /// </summary>
    internal static void LogWellDrink(TraitWell well, int gain)
    {
        if (!(CfgLogPour?.Value ?? false))
        {
            return;
        }
        LogInfo(
            $"[井水] 喝：圣井={well.IsHoly}, 污染={well.polluted}({well.pollution}), "
            + $"余量={well.Charges} → 补 {gain}");
    }

    /// <summary>源表就绪了没 —— Awake 时 EClass.sources 还是 null，只能轮询。</summary>
    private static bool SourcesReady()
        => EClass.sources?.things?.rows != null && EClass.sources.things.rows.Count > 0;

    internal static void LogInfo(object message) => Instance?.Logger.LogInfo(message);

    internal static void LogWarn(object message) => Instance?.Logger.LogWarning(message);

    internal static void LogError(object message) => Instance?.Logger.LogError(message);
}
