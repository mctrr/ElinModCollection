namespace Wetness;

/// <summary>
/// 本 mod 的文本。
///
/// Elin 的 Lang.Get(id) 在查不到 key 时会原样返回 id（见 SourceLang.Get 的 fallback），
/// 所以这里直接给出当前语言的成句文本即可，不必往语言字典里注入词条。
/// 以后要接 LangMod 表时，把这里的 Pick 换成查表即可。
/// </summary>
internal static class WetnessLang
{
    private static bool IsJP => Lang.langCode == "JP";

    private static bool IsEN => Lang.langCode == "EN";

    private static string Pick(string cn, string en, string jp)
    {
        if (IsJP)
        {
            return jp;
        }
        return IsEN ? en : cn;
    }

    /// <summary>
    /// 速度详情面板里那一行扣分的名字。
    /// Element.BonusInfo.AddFix 会自己补上 "%"，所以这里只给名字。
    /// </summary>
    public static string WetnessName => Pick("湿润度", "Wetness", "湿潤度");

    /// <summary>
    /// 鼠标悬停时 Tip 里那一行说明。
    /// 也是 EnsureSource 填进 SourceStat.Row.detail 的内容 —— 它非空才有 Tip 可显示。
    ///
    /// 口径逐条对齐原版 Stat 表里的 detail：
    ///     hunger「饱腹度。肚子饿会让速度变慢，停止自愈，最终会饿死。」
    ///     burden「行李太重。越重速度越慢。」
    ///     ConWet「湿身状态。电击抗性减弱。」
    /// 即「短名词。一句后果。」——**不写「（状态）」前缀**，那是原版按
    /// contype_&lt;Type&gt; 自己加的，我们手写就重复了。
    /// </summary>
    public static string NoteDetail => Pick(
        "湿润度。水分不足会让速度变慢并流失魔力，过多则一口也喝不下去。",
        "Wetness. Too little slows you down and drains your mana; too much and you cannot drink.",
        "湿潤度。水分が足りないと動きが鈍り、魔力も失われる。多すぎると飲めない。");

    /// <summary>
    /// 偏湿时直接饮用被拒。
    /// 只说「喝不下去」这一件事 —— 不解释体内水分如何如何：
    /// 玩家要的是「为什么这个动作没发生」，不是一段状态描述。
    /// </summary>
    public static string TooWetToDrink => Pick(
        "一口也喝不下去了。",
        "You cannot swallow another drop.",
        "もう一滴も飲めない。");

    // 偏湿时井口也喝不了，但那条路是直接把「喝」这个菜单项去掉
    // （见 WetnessDrinkPatch.WellTrySetActPrefix），一声不吭 ——
    // 井口的 TrySetAct 会被反复调用，在那里 Say 会刷屏。
    // 所以这里没有对应的文案，玩家看到的是 Chara.Drink 那一条统一提示。

    /// <summary>档位变化时该说的话。没有对应文本就返回 null。</summary>
    public static string GetPhaseMessage(int phase, bool worsening)
    {
        if (worsening)
        {
            switch (phase)
            {
                case WetnessDef.Parched:
                    return Pick("严重脱水 —— 手脚发软，动作明显慢了下来，魔力也在不断流失。",
                        "Severely dehydrated — your limbs feel weak, your movements slow, and your mana keeps draining away.",
                        "重度の脱水症状だ。手足がふらつき、動きが目に見えて鈍い。魔力も失われていく。");
                case WetnessDef.Thirsty:
                    return Pick("你渴得厉害。",
                        "You are terribly thirsty.",
                        "ひどく喉が渇いている。");
                case WetnessDef.DryRed:
                    return Pick("喉咙发干。",
                        "Your throat is dry.",
                        "喉が乾いてきた。");
                default:
                    return null;
            }
        }

        switch (phase)
        {
            case WetnessDef.Dry:
                return Pick("喉咙不那么干了。",
                    "Your throat feels less parched.",
                    "喉の渇きが少し和らいだ。");
            case WetnessDef.Neutral:
                return Pick("口渴感消退了。",
                    "Your thirst has passed.",
                    "渇きは収まった。");
            case WetnessDef.Moist:
                return Pick("现在一口也喝不下去了。",
                    "You cannot swallow another drop now.",
                    "もう一滴も飲めない。");
            case WetnessDef.Drenched:
                return Pick("你喝得太多了，走动时几乎能听见水声。",
                    "You have drunk far too much — you can almost hear the water sloshing.",
                    "飲みすぎだ。歩くたびに水の音が聞こえる。");
            default:
                return null;
        }
    }

    // ---- 水囊 ----
    // 这些消息带运行时参数，所以直接在 C# 里拼成句，
    // 不走 LangGeneral 的 #1/#2 占位符 —— 少一层不确定。

    /// <summary>
    /// 物品说明里那一行「里面装的是什么」。
    ///
    /// 名字不缀存量之后，这里是唯一能看出内容的地方（右下角只显示数字）。
    /// 形状照抄原版说明行：短标签 + 冒号 + 内容，不写成句子。
    /// **空的时候不加这一行** —— 右下角没有数字，本身就是「空」。
    /// </summary>
    public static string SkinContents(string label, int doses, int max) => Pick(
        $"内容：{label} {doses}/{max}",
        $"Contents: {label} {doses}/{max}",
        $"中身：{label} {doses}/{max}");

    /// <summary>想倒进去的东西不能喝（比如空瓶）。</summary>
    public static string SkinNotDrinkable => Pick(
        "那里面倒不出能喝的东西。",
        "There is nothing drinkable in that.",
        "そこからは飲めるものが出てこない。");

    /// <summary>水囊里已有别的东西，不允许混装。</summary>
    public static string SkinNoMixing(string current, string adding) => Pick(
        $"水囊里装的是{current}，不能和{adding}混在一起。",
        $"The waterskin holds {current}. You cannot mix in the {adding}.",
        $"水袋には{current}が入っている。{adding}を混ぜるわけにはいかない。");

    /// <summary>
    /// 水囊满了。只报这件事 —— 装的是什么、满到几口，
    /// 说明行和右下角的数字里都有，重复一遍就成了啰嗦。
    /// </summary>
    public static string SkinFull => Pick(
        "水囊已经满了。",
        "The waterskin is full.",
        "水袋はもう満杯だ。");

    // 以太抗体药水没有文案：它在 CanUse 里就被掐掉了，不弹任何提示。
    // 那是硬性禁止（不对应任何玩家可解的状态），多说一句只是噪音。

    /// <summary>
    /// 倒进去成功。只报「谁进了哪儿」。
    /// 口数在右下角，瓶子就在背包里 —— 都不必说。
    /// </summary>
    public static string SkinPoured(string name) => Pick(
        $"你把{name}倒进了水囊。",
        $"You pour the {name} into the waterskin.",
        $"{name}を水袋に注いだ。");

    /// <summary>
    /// 从水囊喝一口。剩余口数在右下角，不写进句子里。
    /// 只有喝光了那一次值得说一声 —— 因为那一刻数字消失了。
    /// </summary>
    public static string SkinDrunk(string name, bool last) => last
        ? Pick($"你喝光了最后一口{name}。",
            $"You drain the last of the {name}.",
            $"最後の一口の{name}を飲み干した。")
        : Pick($"你喝下一口{name}。",
            $"You drink a mouthful of the {name}.",
            $"{name}を一口飲んだ。");

    /// <summary>水囊是空的。说清状态就够，「倒不出东西来」是废话。</summary>
    public static string SkinNothingToPourOut => Pick(
        "水囊是空的。",
        "The waterskin is empty.",
        "水袋は空だ。");

    /// <summary>
    /// 囊里的东西还原不出原样（本功能之前存下的水囊没有记录来源）。
    /// </summary>
    public static string SkinCannotPourOut => Pick(
        "这囊里的东西已经分不出原样了。",
        "Whatever is in there can no longer be separated out.",
        "この水袋の中身はもう元の形には戻せない。");

    /// <summary>倒出来，重新灌了一瓶。倒出来的那瓶就在背包里，只报动作。</summary>
    public static string SkinPouredOut(string name) => Pick(
        $"你倒出了一瓶{name}。",
        $"You pour out a bottle of {name}.",
        $"{name}を一瓶注ぎ出した。");

    // ---- 物品说明里的「含水」 ----

    /// <summary>
    /// 物品说明里跟在「营养：11」后面的那一行，例如「含水：12」。
    /// 形状照抄原版 _nutrition 的写法（名字 + 冒号 + 数值），各自成句，
    /// 不走 LangGeneral 的占位符 —— 少一层不确定。
    /// </summary>
    public static string WaterNote(int value) => Pick(
        $"含水：{value}",
        $"Water: {value}",
        $"水分：{value}");
}
