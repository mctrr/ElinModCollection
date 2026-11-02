namespace Wetness;

/// <summary>
/// 湿润度的全部可调参数与分档定义。
/// 数值区间刻意保持 0-100，与 Elin 原生 BaseStats 的 max 一致。
/// </summary>
internal static class WetnessDef
{
    // ---- Chara._cints 存储槽位 ----
    //
    // 数组是 new int[30]，上限就是 29，想用 50/100 得先 Array.Resize —— 没做，见 README。
    // 原生占用：0-7 / 9 / 10-18(stats) / 20 / 21
    // 空闲：8 / 19 / 22-29
    //
    // 取最末尾的 26-29：原版要新增 cint 是从 19 往上填的，排在我们前面，
    // 于是「它再要 4 个以上 int」才轮到撞我们。这是不扩容的前提下能拿到的最大缓冲。
    // 真撞上了的补救：把这四个常量整体前移（22 起仍空着），或直接 Resize 到 128 用 100+。
    public const int SlotValue = 26;
    public const int SlotAccum = 27;
    public const int SlotInit = 28;

    /// <summary>MP 回复加成的零头累加器，单位是「百分之一 MP」。</summary>
    public const int SlotManaAccum = 29;

    public const int Min = 0;
    public const int Max = 100;

    /// <summary>初始值，落在「不显示」区间，玩家开局不会看到任何提示。</summary>
    public const int Initial = 60;

    /// <summary>
    /// 复活后的水分下限。
    ///
    /// 照抄原版 Revive 里对饥饿的那句
    ///     if (hunger.value > 30) hunger.value = 30;
    /// 意思是「复活后最多只让你有点饿，不许一睁眼就饿死」。
    /// 湿润度方向相反（越大越湿），所以这里夹的是下限：低于这个值就拉上来。
    ///
    /// 45 落在静默区（40-69）靠下沿 —— 渴死过一次的人不该睁眼就又渴，
    /// 但也不该直接给到「湿润」（70 起就一口都喝不下了）。
    /// </summary>
    public const int ReviveValue = 45;

    // ---- 七档 ----
    public const int Parched = 0;  // 脱水症（红）
    public const int Thirsty = 1;  // 口渴（红）
    public const int DryRed = 2;   // 干燥（红）
    public const int Dry = 3;      // 干燥
    public const int Neutral = 4;  // 不显示
    public const int Moist = 5;    // 湿润
    public const int Drenched = 6; // 喝得过多

    /// <summary>
    /// 数值桶（0-9）→ 档位。与 Stats 基类的分档方式一致：
    /// 先把 0-100 十等分，再查这张表。
    ///
    /// 用原版 Elona 的切法，各档区间为
    ///   脱水症 0-9 / 口渴 10-19 / 干燥 20-29 / 干燥 30-39 /
    ///   不显示 40-69 / 湿润 70-79 / 喝得过多 80-100
    /// 干侧挤在低端、湿侧挤在高端，中间 40-69 三桶并作一档保持静默。
    /// </summary>
    public static readonly int[] PhaseMap = { 0, 1, 2, 3, 4, 4, 4, 5, 6, 6 };

    /// <summary>桶号 → 档位。越界夹到两端。</summary>
    public static int FromBucket(int bucket)
    {
        if (bucket < 0)
        {
            bucket = 0;
        }
        else if (bucket > 9)
        {
            bucket = 9;
        }
        return PhaseMap[bucket];
    }

    /// <summary>档位名。索引与档位常量对应。</summary>
    public static readonly string[] Names =
    {
        "脱水症", "口渴", "干燥", "干燥", "不显示", "湿润", "喝得过多"
    };

    /// <summary>
    /// 是否在状态栏显示该档位。
    ///
    /// 判据是「玩家在这一档有没有必须知道的后果」，不是「数值离中间有多远」：
    ///   脱水症 / 口渴 / 干燥红 —— 有速度惩罚，必须显示
    ///   喝得过多               —— 不能再饮用，必须显示
    ///   **湿润（70-79）        —— 禁饮就是从这一档开始的，同样必须显示**
    ///
    /// 「湿润」早先是静默的，于是「明明一口都喝不下了，状态栏什么都不说」。
    /// 禁饮的档位与显示的档位必须对齐，否则玩家只能靠猜。
    /// 单纯「数值偏高」的中间状态（干燥 30-39、不显示 40-69）仍然静默。
    /// </summary>
    public static bool IsVisible(int phase)
    {
        return phase == Parched || phase == Thirsty || phase == DryRed
            || phase == Moist || phase == Drenched;
    }

    /// <summary>「湿润」及以上，原设定中不可再饮用。</summary>
    public static bool IsWet(int phase) => phase >= Moist;

    /// <summary>
    /// 速度惩罚（百分数，取负数），数值照抄原版 hunger。
    ///
    /// 原版 Chara.RefreshSpeed 里 hunger 的写法是
    ///     case 3: case 4: num3 -= 10;   // Hungry / VeryHungry
    ///     case 5:         num3 -= 30;   // Starving
    /// 即「轻两档各 -10、重档 -30」。这里按同样的形状铺到干侧三档上：
    /// 干燥 -10、口渴 -10、脱水症 -30。
    ///
    /// 「干燥」那一档（30-39）不给惩罚 —— 它在状态栏本来就不显示
    /// （IsVisible 只放行红色档），挂了也没人看得见，反而让玩家莫名其妙变慢。
    /// 原版 hunger 也只惩罚它自己会显示出来的那三档。
    /// </summary>
    public static int SpeedPenalty(int phase)
    {
        switch (phase)
        {
            case DryRed:
            case Thirsty:
                return -10;
            case Parched:
                return -30;
            default:
                return 0;
        }
    }

    // ---- 每回合衰减 ----
    //
    // 逐字照抄原版 hunger，不做任何自创的速率。
    // Chara.TickConditions 里 hunger 那一处写的是
    //     switch (turn % 50) { case 1: if (EClass.rnd(3) == 0) hunger.Mod(1); }
    // 即「每 50 回合掷一次骰子，1/3 概率 +1」→ 平均每 150 回合掉 1 点。
    // 从 100 掉到 0 要约 15000 回合，跟饿死一样慢。

    /// <summary>每多少回合掷一次骰子（原版 hunger 是 50）。</summary>
    public const int DecayCheckEvery = 50;

    /// <summary>骰子面数：rnd(N) == 0 才扣 1 点（原版 hunger 是 3，即 1/3 概率）。</summary>
    public const int DecayChanceDiv = 3;

    // ---- 脱水症的惩罚：扣魔力 ----
    //
    // 原版 hunger 最饿那一档是
    //     if (phase >= 5) { rnd(5)==0 → DamageHP(1 + rnd(2) + MaxHP/50, Hunger);
    //                       rnd(3)==0 → stamina.Mod(-1); }
    // 即「掉血 + 掉体力」两路一起上。
    // 湿润度不能再走这两条 —— 玩家会被两条掉血线夹着，而且根本分不清是谁在杀他。
    // 所以把伤害整条换成 MP：饥饿负责血与体力，口渴只负责魔力。

    /// <summary>每回合 rnd(N) == 0 时扣一次魔力（原版 hunger 掉血用的是 5）。</summary>
    public const int DehydrateManaChance = 5;

    /// <summary>
    /// 单次扣除量 = 1 + rnd(2) + max / N。
    /// 除数据照抄原版掉血式的 MaxHP / 50，只是把 HP 换成 MP。
    /// </summary>
    public const int DehydrateManaDivisor = 50;

    // 沙漠白天加速衰减原本在此预留 DecayIntervalDesert = 5。
    // 当前版本的游戏里没有沙漠地形，这一条不做，常量已删除。
    // 将来真有沙漠了，在这里加回常量，并在 WetnessTickPatch 里按地形分流间隔即可。

    /// <summary>
    /// 下雨时是否暂停自然衰减。
    ///
    /// 判定条件照抄原版 Chara.TickConditions 里给「淋湿」的那一处
    /// （天气在下雨 and 不在室内 and 头顶没有屋顶），
    /// 所以「淋着雨」的定义与游戏本身完全一致 —— 屋檐下、洞里、室内照常消耗。
    ///
    /// 只认雨、不认雪：原版那处 ConWet 判定用的就是 Weather.IsRaining，不含 Snow。
    /// 另外冬天的雨会在 Weather.SetCondition 里被整批转成雪，届时本条不会触发。
    /// </summary>
    public const bool RainPausesDecay = true;

    // ---- 饮用补水 ----
    //
    // 饮水表与「食物含水表」原本在这里，现已整体搬到 WetnessWater ——
    // 吃、喝、水囊、物品说明四处要查的是同一张表，放一个地方才不会走岔。
    // 这里只留「喝多少算水」这件事的边界条件。

    /// <summary>达到这一档就不能再「饮用」，但「使用」与「投掷」不受限。</summary>
    public const int DrinkLockPhase = Moist;

    /// <summary>呕吐扣减的湿润度。</summary>
    public const int VomitCost = 8;

    // ---- 「湿润」及以上的正面收益 ----
    /// <summary>
    /// 自然回复 MP 的加成是否生效。湿润（70-79）与喝得过多（80-100）都生效。
    /// </summary>
    public static bool HasManaRegenBonus(int phase) => phase >= Moist;

    /// <summary>自然回复 MP 提升的百分比。</summary>
    public const int ManaRegenBonusPercent = 10;

    // ---- 水囊 ----
    /// <summary>
    /// 水囊的源表 id。声明在 LangMod 的 ThingV 页，_origin = potion_empty，
    /// 所以外观、分类、重量、放置参数全都继承空瓶；本 mod 只改名字与行为。
    /// </summary>
    public const string WaterskinId = "wetness_waterskin";

    /// <summary>
    /// 空瓶的源表 id —— 水囊借它当 ThingV 的模板（_origin）。
    ///
    /// 代价是 SourceCard 会把它标记成 isOrigin，而 ThingGen._Create 对 isOrigin
    /// 行会重定向到「变体」，也就是水囊。所以这个 id 需要 WetnessEmptyBottlePatch
    /// 兜一手，否则游戏里任何造空瓶的地方都会造出水囊。
    /// </summary>
    public const string EmptyBottleId = "potion_empty";

    // ---- 水囊容量：由「做它用的那块锭」决定 ----
    //
    // 配方第一项是 ingot，而 Recipe.GetMainMaterial() 取的正是 ingredients[0] 的材质，
    // RecipeCard.Craft 又拿它去 ThingGen.Create(id, 材质, lv) ——
    // 于是成品水囊的 material 就是那块锭的材质，容量可以直接读
    // owner.material.hardness 算出来。既不用 hook 制作过程，也不用存任何东西。
    //
    // 用铁锭做出来的就是铁水囊，比用软材料做的能多装几口；锭越硬装得越多。
    // 玩家把水囊拿去做别的事（改材质）容量也会跟着变，符合直觉。

    /// <summary>容量下限（软材料：木、布之类）。</summary>
    public const int MinSkinDoses = 2;

    /// <summary>容量上限（最硬的金属）。附魔奖励可以再越过它 1 口。</summary>
    public const int MaxSkinDoses = 10;

    /// <summary>
    /// 每这么多点材质硬度多装一口。
    ///
    /// 取 15 是根据材质的真实硬度谱算的：金的硬度 25 是全游戏最软的金属之一，
    /// 以太 120 是最硬的，而 2 + 25/15 = 3、2 + 120/15 = 10 ——
    /// 正好让「金锭 → 3 口」到「以太锭 → 10 口」铺满 2-10 这个区间，
    /// 不会出现大半金属都顶到上限、几口之差看不出来的情况。
    ///
    /// 常用锭材质的实际硬度和对应容量（硬度取自游戏源表 SourceMaterial）：
    ///     金 25 → 3 口     银 27 → 3 口     铜 34 → 4 口     青铜 45 → 5 口
    ///     白金 49 → 5 口   铁 56 → 5 口     黑曜石 60 → 6 口  钢 67 → 6 口
    ///     铬铁 85 → 7 口   钛 89 → 7 口     陨铁 95 → 8 口    秘银 98 → 8 口
    ///     精金 115 → 9 口  以太 120 → 10 口 暗物质 200 → 10 口（夹住）
    /// 木头、布料一类（硬度 1-20）都是 2 口。
    /// </summary>
    public const int HardnessPerDose = 15;

    /// <summary>附了魔的水囊再多装一口。</summary>
    public const int EnchantedBonusDoses = 1;

    /// <summary>
    /// 材质硬度 → 容量，夹在 [MinSkinDoses, maxDoses]。
    /// perDose / maxDoses 由调用方传入（来自 Plugin 的配置项），这里只做纯计算。
    /// </summary>
    public static int DosesFromHardness(int hardness, int perDose, int maxDoses)
    {
        if (perDose < 1)
        {
            perDose = 1;
        }
        if (maxDoses < MinSkinDoses)
        {
            maxDoses = MinSkinDoses;
        }
        if (hardness < 0)
        {
            hardness = 0;
        }

        int doses = MinSkinDoses + hardness / perDose;
        if (doses < MinSkinDoses)
        {
            return MinSkinDoses;
        }
        return doses > maxDoses ? maxDoses : doses;
    }

    /// <summary>附魔状态从哪读。encLV &gt; 0 即「附过魔」，原版物品名会显示 +N。</summary>
    public const bool EnchantAddsDose = true;

    /// <summary>
    /// 以太抗体药水。这种药不让倒进水囊 —— 它本来就该当场喝掉起效，
    /// 存进囊里等于在「体内水分已经过多、什么都咽不下」时留一条随时可用的后路。
    /// 源表里它有自己的数字 id（不是 id="potion" 的随机药水）。
    /// </summary>
    public const string EtherAntibodyPotionId = "1165";

    // 水囊内部的数据存哪儿？
    // Card 从 BaseCard 继承了三个字典，都带 [JsonProperty]，会随存档一起走：
    //     mapObj("X") / mapInt("Y") / mapStr("Z")
    // 直接借用就够了，不必另加字段，也不会和别的 mod 抢位置。
    //
    // 键一律用显式整数：Card 上另有一个 GetStr(string) 重载会走 String.GetHashCode()，
    // 而 .NET 的字符串哈希不保证跨运行时、跨版本一致 —— 存档读回来可能取不到。

    /// <summary>int：(int)EffectId。</summary>
    public const int SkinKeyEffect = 981001;

    /// <summary>int：效果强度，对应 TraitDrink.Power。</summary>
    public const int SkinKeyPower = 981002;

    /// <summary>int：(int)BlessedState，喝的时候要原样交回 ActEffect.Proc。</summary>
    public const int SkinKeyBless = 981003;

    /// <summary>int：剩余口数。</summary>
    public const int SkinKeyDoses = 981004;

    /// <summary>str：内容标识，用于「不能混装」的比对。</summary>
    public const int SkinKeyIdent = 981005;

    /// <summary>str：内容的显示名。</summary>
    public const int SkinKeyLabel = 981006;

    /// <summary>str：效果参数 N1（TraitDrink.N1，多数药水为空）。</summary>
    public const int SkinKeyN1 = 981007;

    /// <summary>
    /// str：内容来源的物品 id。
    /// 倒回空瓶时要靠它重建一瓶出来 —— 光有 effect/power 是不够的，
    /// 玩家喝的是那瓶药水自己的 trait（IdEffect / Power 由 id + refVal 决定）。
    /// </summary>
    public const int SkinKeySrcId = 981008;

    /// <summary>
    /// int：内容来源的 refVal（元素 id）。
    /// 随机药水的 id 全是 "potion"，ThingGen.Create 出来的那一瓶是随机口味，
    /// 必须把这个值覆写回去才是原来那种。
    /// </summary>
    public const int SkinKeySrcRefVal = 981009;

    /// <summary>
    /// int：内容来源的鉴定状态（Card.c_IDTState）。
    ///
    /// 0 = 已鉴定，5 = 未鉴定，1 / 3 = 已鉴定但带稀有度标记。
    /// 必须记：倒出来是「照 id 新造一瓶」，而 Thing.OnCreate 对
    /// `source.unknown` 非空的物品（药水就是）默认写 c_IDTState = 5，
    /// 于是玩家灌进去的明明是自己鉴定过的那瓶，倒出来却又是未鉴定，
    /// 还得再鉴定一次。
    ///
    /// 旧存档（v0.13.7 之前的水囊）没有这个键，取出来是 0 = 已鉴定 ——
    /// 这是有意的：本 mod 本来就在物品说明里直接写出内容名，
    /// 内容对玩家从来不是未知的，没必要倒出来再让人鉴定一遍。
    /// </summary>
    public const int SkinKeySrcIdt = 981010;

    /// <summary>
    /// 倒回空瓶时重建药水用的等级。
    /// 取 10 与原版空瓶装水的做法一致（TraitPotionEmpty 里写死的就是 10）；
    /// 原瓶真实的 lv 没有存 —— 药水的强度主要由 refVal 对应的元素决定，
    /// 而喝下去的效果在入囊时已经连 power 一起抄下来了。
    /// </summary>
    public const int PourOutLevel = 10;
}
