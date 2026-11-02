using System.Collections.Generic;

namespace Wetness;

/// <summary>
/// 含水表 —— 一件东西里有多少水。
///
/// 单位与饮水表一致：湿润度点数（0-100），一瓶清水 = 35。
/// 吃的、喝的、水囊里的内容物、物品说明，四处全查这一张表。
///
/// ---- 为什么不再「按营养除 K」 ----
/// v0.10.0 用的是「含水 = 营养 ÷ K」。营养是「料」，不是「水」，两者经常反着走：
///     番茄 营养低 → 算出 1 点；玉米 营养高 → 反而补得比番茄多。
/// 面包、面粉、尸体、水银这些也会跟着营养蹭到水分。
/// 所以整条公式删掉，改成**点名**：
///
///   1. 禁列表（DenyIds / DenyCats）—— 一律 0，优先级最高
///   2. 物品点名（ById）—— 番茄 5 这种「现实里咬一口出水」的东西
///   3. 类别（CategoryWater）—— 料理按做法分（汤 15 / 果物料理 8 / 菜 6 / 面 6 / 米 5，
///      其余料理 0），生鲜水果一律 5
///   4. 其余一律 0
///
/// 这样加一项只要在表里加一行，判定路径只有一条，不会出现「同一件东西
/// 直接吃和从水囊里喝补的水不一样」这种事。
/// </summary>
internal static class WetnessWater
{
    // ================= 两道闸门 =================

    /// <summary>
    /// 低于这个数一律当 0。
    ///
    /// 刻度以「一口 = 5」为单位：现实里补不出 5 点水的东西（一片面包、
    /// 一把小麦）就不该在说明里冒出个「含水：1」，那只会让玩家以为能靠它解渴。
    /// 也正是这条把面包、面粉、尸体这些自然挡在外面。
    /// </summary>
    public const int MinWater = 5;

    /// <summary>
    /// 单份食物的上限。
    ///
    /// 防止「一份大菜顶掉一瓶水」：吃是辅助，喝才是主力。
    /// 25 约等于 3/4 瓶清水。
    /// </summary>
    public const int MaxFoodWater = 25;

    // ================= 喝的：沿用原版 EffectId 分类 =================

    /// <summary>清水。这个世界里干净水只有圣井才出，所以给得最高。</summary>
    public const int WaterGain = 35;

    /// <summary>
    /// 分档线：20。
    ///
    /// 「一瓶水」这一档留给特殊的水 —— 清水与圣井水（35）超过去，
    /// 因为这个世界里它们确实是稀缺资源。其余一切能喝的（井水、汽水、
    /// 牛奶、酒、脏水、药水）一律 ≤ 20，井水这种随手可打的不该和圣井同价。
    /// 改数值时顺手扫一眼这张表，别让第二档悄悄爬过这条线。
    /// </summary>
    public const int CommonDrinkCap = 20;

    /// <summary>汽水（拉姆内）。</summary>
    public const int RamuneGain = CommonDrinkCap;

    /// <summary>牛奶。</summary>
    public const int MilkGain = CommonDrinkCap;

    /// <summary>酒。比水少，但仍是正数。</summary>
    public const int BoozeGain = 15;

    /// <summary>脏水。</summary>
    public const int WaterDirtyGain = 12;

    /// <summary>
    /// 药水与其余一切「能喝但不是水」的东西。
    /// 解渴效率远不如真水，但也不是零。
    /// </summary>
    public const int PotionGain = 10;

    /// <summary>盐水：喝海水越喝越渴。</summary>
    public const int SaltWaterGain = -10;

    // ================= 喝的：井水（不走的常规路径，单独一档） =================
    //
    // 井水的水量按**井的状态**分档，而不是按 EffectId ——
    // 原版喝井水走的是 TraitWell 自己注册的 DynamicAct，
    // 全程不产生任何 EffectId、不产生食物对象（见 WetnessDrinkPatch 的 Postfix），
    // 所以它压根进不了上面那张 EffectId 表。

    /// <summary>
    /// 圣井水。给满清水 —— 圣井是这个世界里最干净的水源，
    /// 原版对它的态度也明显更好（喝到 ModPotential 100）。
    /// </summary>
    public const int HolyWellGain = WaterGain;

    /// <summary>
    /// 普通井水（未污染）。
    ///
    /// 井是敞开的、有杂质，原版喝它本来就有 1/5 概率 BadEffect、
    /// 1/4 概率 Mutation 的风险 —— 打井水不花成本也不该有圣井的价，
    /// 所以定在「随手可得」这一档（20），比汽水牛奶同级、比清水少 15。
    /// </summary>
    public const int WellGain = CommonDrinkCap;

    /// <summary>井水的分档查询。污染的井（`pollution >= 10`）按脏水算。</summary>
    public static int GetWellGain(bool holy, bool polluted)
    {
        if (holy)
        {
            return HolyWellGain;
        }
        return polluted ? WaterDirtyGain : WellGain;
    }

    /// <summary>按 EffectId 取「喝一口补多少」。注意这是纯分类，不含禁列表。</summary>
    public static int GetDrinkGain(EffectId id)
    {
        switch (id)
        {
            case EffectId.DrinkWater: return WaterGain;
            case EffectId.DrinkWaterDirty: return WaterDirtyGain;
            case EffectId.DrinkRamune: return RamuneGain;
            case EffectId.DrinkMilk: return MilkGain;
            case EffectId.Booze: return BoozeGain;
            case EffectId.SaltWater: return SaltWaterGain;
            default: return PotionGain;
        }
    }

    /// <summary>
    /// 喝一口某件具体东西补多少。禁列表在这里生效 ——
    /// 水银、耐酸保护液、堕天使之血这类都带 TraitDrink，
    /// 不挡的话会按默认的「药水 = 10」白送水分。
    /// </summary>
    public static int GetDrinkGain(string thingId, EffectId id)
    {
        return IsDeniedId(thingId) ? 0 : GetDrinkGain(id);
    }

    /// <summary>同上，直接拿 Thing。</summary>
    public static int GetDrinkGain(Thing t, EffectId id)
    {
        return t != null && IsDeniedId(t.id) ? 0 : GetDrinkGain(id);
    }

    // ================= 吃的①：料理按类别 =================
    //
    // 料理的含水量由「做法」决定，不由食材决定 —— 一碗汤就是一碗水，
    // 一块烤肉就是干的。所以料理整类走这张表，不看用了什么料。
    //
    // 只列**真的带汤水**的那几类。原版的判据也在失败料理的名字里：
    //   meal_soup   汤   -> fail_drink「汁」
    //   meal_fruit  果物料理 -> fail_fruit「糊状」
    //   meal_vegi   蔬菜料理 -> fail_vegi「湿黏」
    //   meal_noodle 面   -> fail_noodle「泡软塌的面」
    //   meal_rice   米类
    // 其余（面包 / 蛋糕 / 曲奇 / 蛋 / 鱼 / 肉 / 套餐）全是干的，留 0。

    private static readonly string[] MealCats = { "meal_soup", "meal_fruit", "meal_vegi", "meal_noodle", "meal_rice" };

    private static readonly int[] MealWater = { 15, 8, 6, 6, 5 };

    /// <summary>
    /// 生鲜水果一律 5。
    ///
    /// 「多水的水果」这条按**类别**给，而不是一个个点名 ——
    /// 现实里水果整体就是含水的一类，苹果、葡萄、橘子没必要逐个列；
    /// 点名表只留给类别盖不住的例外（番茄若不被归到 fruit，就在点名单里补）。
    /// 蔬菜与蘑菇不给：同一类里土豆、小麦、玉米和黄瓜差得太远，只能点名。
    /// </summary>
    public const int FruitWater = 5;

    // ================= 吃的②：物品点名 =================
    //
    // 「现实里咬一口能出水」的东西。判据是常识，不是营养值。
    // 水果不用列（见 FruitWater，整类 5）；这里放的是类别盖不住的两类：
    //   - 例外：番茄若不在 fruit 类，靠这一行拿 5
    //   - 否决：玉米这种被归进含水类别、但现实里是干的东西
    // 表里没列、类别又没覆盖到的（小麦、土豆、蘑菇、肉、鱼、蛋）一律 0。
    //
    // 加一行 = 给它水分；写 0 = 显式否决（压过类别，包括料理）。

    // id 全部来自游戏自带的中文语言包 `_Lang_Chinese/Lang/CN/Game/Thing.xlsx`
    // （2587 行，含 id + 中/英/日名，见 tools/thing_ids.tsv），不是猜的。

    private static readonly Dictionary<string, int> ById = new Dictionary<string, int>
    {
        // ---- 多水的水果（5）----
        { "apple", 5 },        // 苹果
        { "berry", 5 },        // 蓝莓
        { "orange", 5 },       // 橙子
        { "117", 5 },          // 梨
        { "118", 5 },          // 菠萝
        { "grape", 5 },        // 葡萄
        { "779", 5 },          // 斑斓果
        { "785", 5 },          // 柠檬
        { "787", 5 },          // 香蕉
        { "788", 5 },          // 樱桃
        { "805", 5 },          // 牛油果
        { "fruit", 5 },        // 水果（通用食材）

        // ---- 特别多水的瓜（8）----
        { "786", 8 },          // 西瓜
        { "791", 8 },          // 西瓜（另一个 id）
        { "792", 8 },          // 哈密瓜

        // ---- 巨瓜作物：整个就是一泡水 ----
        { "block_melon", 8 },      // 巨大哈密瓜
        { "block_strawberry", 5 }, // 巨大草莓
        { "block_cherry", 5 },     // 巨大樱桃
        { "block_banana", 5 },     // 巨大香蕉
        { "block_peach", 5 },      // 巨大桃子
        { "block_kiwi", 5 },       // 巨大猕猴桃
        { "block_mango", 5 },      // 巨大芒果

        // ---- 咬下去出水的蔬菜（5）----
        { "tomato", 5 },       // 番茄（用户点名要 5）
        { "carrot", 5 },       // 胡萝卜
        { "cabbage", 5 },      // 卷心菜
        { "radish", 5 },       // 萝卜
        { "697", 5 },          // 茄子
        { "781", 5 },          // 笙菜
        { "782", 5 },          // 瓜
        { "783", 5 },          // 楠瓜
        { "bamboo_shoot", 5 }, // 竹笋
        { "vegi", 5 },         // 蔬菜（通用食材）

        // ---- 显式归零：名字像含水、其实干的 ----
        { "corn", 0 },         // 玉米（用户点名不要）
        { "potato", 0 },       // 土薯
        { "784", 0 },          // 甜土薯
        { "nuts", 0 },         // 果实（坚果）
        { "696", 0 },          // 阿比果实
        { "ringoame", 0 },     // 苹果糖：糖果不是水果
        { "1223", 0 },         // 香蕉皮
        // 蘑菇类（mushroom / 256 / 699 / 700 / mushroom_rare）、药草类（herb*）
        // 不列也在闸门之下 —— 归属于 `fruit` 之外的类别，默认就是 0。
        // 判据是「咬一口有没有汁水」，不是含水量百分比：蘑菇、肉、鱼含水量都不低，
        // 但它们不解渴，不该变成水源（否则渴的机制直接被采集顶掉）。
    };

    // ================= 禁列表 =================
    //
    // 一律 0，优先级高于上面两张表。
    // 分两段：食物侧（面包、面粉、尸体）与饮品侧（水银、耐酸液、堕天使之血）。
    // 饮品侧尤其重要 —— 它们带 TraitDrink，不挡就会按「药水 = 10」补水。

    private static readonly HashSet<string> DenyIds = new HashSet<string>
    {
        // ---- 尸体 ----
        "meat", "deadbody", "_meat", "870",

        // ---- 不是水 ----
        "mercury",      // 水银：重金属
        "338",          // 耐酸保护液（acidproof liquid）：涂装备用的
        "blood_angel",  // 堕天使之血

        // ---- 干主食 / 面团 / 面粉 ----
        "flour", "dough", "dough_bread", "dough_cake", "noodle", "tempurako",
        "660", "665", "666", "671", "672", "673", "1127",
    };

    /// <summary>整个类别一律不补水。尸体走这里（原版所有尸体都是 meat 类）。</summary>
    private static readonly string[] DenyCats = { "meat" };

    // ================= 对外入口 =================

    /// <summary>「吃」下去补多少水。挂在 FoodEffect.Proc 上。</summary>
    public static int GetFoodWater(Thing t)
    {
        return t == null ? 0 : FoodWater(t.id, t.category);
    }

    /// <summary>
    /// 物品说明里要显示的「含水：XX」。0 表示不显示（调用方负责判空）。
    /// 喝的看 EffectId，吃的看上面那三张表 —— 与真正入口用的是同一套判定，不会对不上。
    /// </summary>
    public static int GetNoteWater(Thing t)
    {
        if (t == null || IsDeniedId(t.id))
        {
            return 0;
        }

        if (t.trait is TraitDrink drink)
        {
            // 空瓶、空桶这类「喝不了的容器」不显示含水。
            // CanDrink 由各 trait 自己覆写，个别实现会读传进来的角色，所以先判空。
            Chara pc = EClass.pc;
            return pc != null && drink.CanDrink(pc) ? GetDrinkGain(drink.IdEffect) : 0;
        }

        // 井/圣井不是 TraitDrink，落到下面的食物表只会拿到 0 —— 单独认。
        // 按当前状态取值（圣井 / 普通 / 污染三档），玩家看到的就是喝进去的数。
        if (t.trait is TraitWell well)
        {
            return GetWellGain(well.IsHoly, well.polluted);
        }

        return GetFoodWater(t);
    }

    // ================= 纯函数（审计导出也走这里） =================

    /// <summary>
    /// 食物补水判定。只依赖 id 与类别，不碰 Thing ——
    /// 于是离线审计可以拿源表的行直接算，不必造出 2587 个 Thing。
    /// </summary>
    internal static int FoodWater(string id, SourceCategory.Row cat)
    {
        if (IsDeniedId(id) || IsDeniedCategory(cat))
        {
            return 0;
        }

        if (id != null && ById.TryGetValue(id, out int byId))
        {
            return Clamp(byId);
        }

        return Clamp(CategoryWater(cat));
    }

    internal static bool IsDeniedId(string id)
    {
        return id != null && DenyIds.Contains(id);
    }

    private static bool IsDeniedCategory(SourceCategory.Row cat)
    {
        if (cat == null)
        {
            return false;
        }
        for (int i = 0; i < DenyCats.Length; i++)
        {
            if (cat.IsChildOf(DenyCats[i]))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 类别判定。顺序即优先级：
    ///   料理（meal 之下）→ 只认 MealCats，其余料理一律 0（面包、蛋糕、烤肉都是干的）
    ///   水果（fruit）    → 5
    ///   其余             → 0，要补水就在 ById 里点名
    ///
    /// 料理必须排在水果前面：meal_fruit（果物料理）可能挂在 fruit 之下，
    /// 先判 meal 才能让它拿到料理的 8，而不是水果的 5。
    /// </summary>
    private static int CategoryWater(SourceCategory.Row cat)
    {
        if (cat == null)
        {
            return 0;
        }

        if (cat.IsChildOf("meal"))
        {
            for (int i = 0; i < MealCats.Length; i++)
            {
                if (cat.IsChildOf(MealCats[i]))
                {
                    return MealWater[i];
                }
            }
            return 0;
        }

        return cat.IsChildOf("fruit") ? FruitWater : 0;
    }

    private static int Clamp(int water)
    {
        if (water < MinWater)
        {
            return 0;
        }
        return water > MaxFoodWater ? MaxFoodWater : water;
    }

    /// <summary>审计用：这条数值是哪张表定出来的。</summary>
    internal static string Verdict(string id, SourceCategory.Row cat)
    {
        if (IsDeniedId(id))
        {
            return "deny-id";
        }
        if (IsDeniedCategory(cat))
        {
            return "deny-cat";
        }
        if (id != null && ById.TryGetValue(id, out int byId))
        {
            return byId < MinWater ? "id-zero" : "id";
        }
        return CategoryWater(cat) > 0 ? "category" : "none";
    }
}
