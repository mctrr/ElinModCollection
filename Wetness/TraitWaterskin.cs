using Wetness;

/// <summary>
/// 水囊（道具的行为部分）。挂在 wetness_waterskin 上。
///
/// 类放在全局命名空间，而不是和 mod 其余部分一样放 Wetness 命名空间，是有原因的：
/// Elin 解析源表 trait 列走 Card.ApplyTrait -> ClassCache.Create&lt;Trait&gt;("Trait"+名字, "Elin")，
/// 第一步 Type.GetType 只认 "Elin" 里的类型；失败后的回退是 LoadTypeFromGlobalNamespace，
/// 它对每个已登记程序集做 Type.GetType(简单名 + ", " + 程序集) —— 带命名空间的类它找不到。
/// 放全局命名空间，声明式挂载和代码挂载两条路都能走通。
///
/// 为什么不继承 TraitDrink：
/// 那会把物品变成「药水」，于是 Chara.Drink 也对它开放，
/// 而 Chara.Drink 正是本 mod 在偏湿时拦掉的入口（见 WetnessDrinkPatch）。
/// 水囊的意义就是偏湿时还能补一口，必须走「使用」这条不受禁饮影响的路 ——
/// 所以这里直接调 ActEffect.Proc，那正是 TraitDrink.OnDrink 内部做的事。
///
/// 三个入口，两条业务：
///   使用（对自己）    -> 喝一口
///   使用（指向物品）  -> 倒进去 / 倒出来
///   原版「混入」界面  -> 同上（IsBlendBase = true，玩法与原版井、染料一致）
/// </summary>
public class TraitWaterskin : Trait
{
    /// <summary>
    /// 装了内容的水囊绝不能堆叠，否则两瓶不同的东西会被并成一格。
    /// 基类默认是 owner.category.maxStack &gt; 1，而空瓶那一类是可以叠的。
    /// </summary>
    public override bool CanStack => false;

    /// <summary>
    /// 让水囊能被「混入」。原版这个开关挂在 Trait.IsBlendBase 上，
    /// 打开后玩家对水囊交互会多出一项（语言键 invBlend），
    /// 点开是原版的拖拽界面 InvOwnerBlend —— 拖什么进去就混什么。
    /// 井（TraitWell）和染料（TraitDye）走的就是这条路。
    ///
    /// 用原版界面而不是自己造菜单，好处是行为、音效、提示语全都跟游戏一致。
    /// </summary>
    public override bool IsBlendBase => true;

    // ---------------- 内部数据 ----------------

    private int Doses => owner.GetInt(WetnessDef.SkinKeyDoses);

    private string Ident => owner.GetStr(WetnessDef.SkinKeyIdent) ?? "";

    /// <summary>
    /// 这个水囊能装几口。
    ///
    /// 材质决定基础容量：配方第一项是锭，成品水囊的材质就是那块锭的材质
    /// （Recipe.GetMainMaterial 取 ingredients[0]，RecipeCard.Craft 再拿它
    /// 去 ThingGen.Create），所以读 owner.material.hardness 就知道它当初是
    /// 用什么做的。附了魔的再多一口。
    ///
    /// 两项都不落盘 —— 材质和附魔等级本来就长在 Thing 自己身上。
    /// </summary>
    public int MaxDoses
    {
        get
        {
            int hardness = owner.material == null ? 0 : owner.material.hardness;
            int doses = WetnessDef.DosesFromHardness(
                hardness, Plugin.DosesPerHardness, Plugin.MaxSkinDoses);

            // 附魔奖励加在夹取之后，允许比上限多出一口。
            if (WetnessDef.EnchantAddsDose && owner.encLV > 0)
            {
                doses += WetnessDef.EnchantedBonusDoses;
            }
            return doses;
        }
    }

    private string Label => owner.GetStr(WetnessDef.SkinKeyLabel) ?? "";

    /// <summary>
    /// 内容指纹：物品 id + 变体值 + 祝福状态，三样全同才算「同一种东西」。
    /// 随机的药水 id 都是 "potion"，靠 refVal（元素 id）区分，所以 refVal 必须进来；
    /// 祝福状态会改变 ActEffect.Proc 的行为，也算不同。
    /// </summary>
    private static string IdentityOf(Thing t)
    {
        return t.id + "#" + t.refVal + "#" + (int)t.blessedState;
    }

    /// <summary>把内容清空，不留任何残留键。</summary>
    private void Clear()
    {
        owner.SetInt(WetnessDef.SkinKeyDoses, 0);
        owner.SetInt(WetnessDef.SkinKeyEffect, 0);
        owner.SetInt(WetnessDef.SkinKeyPower, 0);
        owner.SetInt(WetnessDef.SkinKeyBless, 0);
        owner.SetInt(WetnessDef.SkinKeySrcRefVal, 0);
        owner.SetInt(WetnessDef.SkinKeySrcIdt, 0);
        owner.SetStr(WetnessDef.SkinKeyIdent, null);
        owner.SetStr(WetnessDef.SkinKeyLabel, null);
        owner.SetStr(WetnessDef.SkinKeyN1, null);
        owner.SetStr(WetnessDef.SkinKeySrcId, null);
    }

    /// <summary>消耗一口；归零则清空整条内容。</summary>
    private void ConsumeDose()
    {
        int left = Doses - 1;
        if (left <= 0)
        {
            Clear();
        }
        else
        {
            owner.SetInt(WetnessDef.SkinKeyDoses, left);
        }
    }

    // ---------------- 格子上的显示 ----------------

    /// <summary>
    /// 物品栏格子右下角那个数字 —— 原版放堆叠数量 / 充能数的地方。
    ///
    /// 走的是原版自己的钩子：ButtonGrid.SetCard() 里那句
    ///     c.trait.SetMainText(mainText, hotitem: false)
    /// 基类实现是「有充能显示充能、否则显示堆叠数量（>1 才显示）」，
    /// 我们只报「还剩几口」。位置、字体、颜色全交给原版，不另起一套。
    ///
    /// 名字里不再缀任何东西（原来的 SetName 覆写已删）——
    /// 「水囊（治疗药水 2/4）」那种拉长物品名的写法不像原版。
    /// </summary>
    public override void SetMainText(UIText t, bool hotitem)
    {
        int doses = Doses;
        if (doses > 0)
        {
            t.SetText(doses.ToString(), FontColor.Charge);
            t.SetActive(enable: true);
        }
        else
        {
            t.SetActive(enable: false);
        }
    }

    /// <summary>
    /// 物品说明里补一行「里面装的是什么」。
    ///
    /// 由 Thing.WriteNote 调过来（trait.WriteNote(n, identified)），
    /// 所以它和物品自带的说明同处一段，风格一致。
    /// 名字不缀存量之后，这里是唯一能看出内容的地方。
    /// </summary>
    public override void WriteNote(UINote n, bool identified)
    {
        int doses = Doses;
        if (doses > 0)
        {
            n.AddText("NoteText_flavor_element", WetnessLang.SkinContents(Label, doses, MaxDoses));
        }
    }
    // ---------------- 「使用」对自己：喝 ----------------

    public override bool CanUse(Chara c)
    {
        return Doses > 0;
    }

    public override bool OnUse(Chara c)
    {
        int doses = Doses;
        if (doses <= 0)
        {
            return false;
        }

        EffectId effect = (EffectId)owner.GetInt(WetnessDef.SkinKeyEffect);
        int power = owner.GetInt(WetnessDef.SkinKeyPower, 100);
        BlessedState bless = (BlessedState)owner.GetInt(WetnessDef.SkinKeyBless);
        string n1 = owner.GetStr(WetnessDef.SkinKeyN1);
        string label = Label;

        // 与原版 TraitDrink.OnDrink 走同一个出口，效果完全一致。
        ActEffect.Proc(effect, power, bless, c, null, new ActRef
        {
            n1 = n1,
            refThing = owner.Thing
        });

        // 湿润度沿用同一张饮水表 —— 从水囊里喝和直接喝这瓶药水，补的水一样多。
        // 早先这里和 Chara.Drink 那条路各算各的，才会出现「同一瓶药水两种结果」。
        if (c.TracksWetness())
        {
            // 内容物的来源 id 一并进判定：与直接喝共用同一张禁列表，
            // 否则「直接喝不补、倒进水囊再喝就补」的老毛病会以新形式回来。
            int gain = WetnessWater.GetDrinkGain(owner.GetStr(WetnessDef.SkinKeySrcId), effect);
            if (gain != 0)
            {
                c.wetness().Mod(gain);
            }
        }

        ConsumeDose();
        RefreshDoseDisplay();
        c.Say(WetnessLang.SkinDrunk(label, Doses <= 0), c, owner);
        return true;
    }

    /// <summary>
    /// 口数变了以后把格子重画一次。
    ///
    /// 右下角那个数字由 ButtonGrid.SetCardGrid 调 trait.SetMainText 画出来，
    /// 而格子只在 UIInventory.dirty 为真时重建 —— 这个标志只有
    /// LayerInventory.SetDirty / SetDirtyAll 会置。原版带充能的物品改的是
    /// c_charges，Card.SetCharge / ModCharge 内部顺手 SetDirty 了一次；
    /// 水囊改的是自己的键值，没有这道通知，数字就停在原处不动。
    ///
    /// 用 SetDirtyAll 而不是 SetDirty：后者开头一句 `if (!game.altInv) return;`，
    /// 经典（非浮动）物品栏下什么都不做，而数字在哪一种物品栏里都要更新。
    /// SetDirtyAll 自己会置 WidgetCurrentTool.dirty / WidgetEquip.dirty，
    /// 快捷键栏那格也走 SetMainText（HotItemThing），所以再补一句
    /// WidgetHotbar.dirtyCurrentItem —— 那一项 SetDirtyAll 不管。
    /// </summary>
    private void RefreshDoseDisplay()
    {
        LayerInventory.SetDirtyAll();
        WidgetHotbar.dirtyCurrentItem = true;
    }

    // ---------------- 「使用」指向某个物品 / 原版「混入」界面 ----------------

    /// <summary>
    /// 可拖进拖出界面（或可被「使用」指向）的东西有两类：
    /// 喝得下的药水（倒进去）与空容器（倒出来）。
    ///
    /// 只判类型，不判容量与混装 ——
    /// 这里返回 false 只会让菜单项消失、什么都不说，
    /// 而玩家需要一句解释「为什么不行」，所以那些检查都放在 OnUse/OnBlend 里发消息。
    ///
    /// 唯一的例外是以太抗体药水：它是硬性禁止，不对应任何玩家可解的状态，
    /// 多说一句反而是噪音，所以直接在 CanUse 里掐掉。
    /// </summary>
    public override bool CanUse(Chara c, Card tg)
    {
        if (c == null || tg == null || !tg.isThing || c.Dist(tg) > 1)
        {
            return false;
        }

        Thing t = tg.Thing;
        if (IsEtherPotion(t))
        {
            return false;
        }
        return IsFillTarget(t, c) || IsPourTarget(t);
    }

    public override bool OnUse(Chara c, Card tg)
    {
        return Blend(c, tg?.Thing);
    }

    /// <summary>
    /// 原版「混入」界面的入口。InvOwnerBlend.ShouldShowGuide 会拿这个决定
    /// 拖拽时哪些物品是亮的，所以判据必须和 CanUse(c, tg) 保持一致。
    /// 这里没有 Chara 参数，用玩家本人判 —— 这个界面本来就只服务玩家。
    /// </summary>
    public override bool CanBlend(Thing t)
    {
        if (t == null || ReferenceEquals(t, owner))
        {
            return false;
        }

        if (IsEtherPotion(t))
        {
            return false;
        }
        return IsFillTarget(t, EClass.pc) || IsPourTarget(t);
    }

    public override void OnBlend(Thing t, Chara c)
    {
        Blend(c, t);
    }

    // ---------------- 两条业务 ----------------

    /// <summary>喝得下的药水才收。空瓶的 CanDrink 是 false，天然被挡在外面。</summary>
    private static bool IsFillTarget(Thing t, Chara c)
    {
        return t != null && t.trait is TraitDrink drink && drink.CanDrink(c);
    }

    /// <summary>空容器（空瓶、空桶）：能往里倒出一份。</summary>
    private static bool IsPourTarget(Thing t)
    {
        return t != null && t.trait is TraitPotionEmpty;
    }

    /// <summary>
    /// 以太抗体药水。它本来就该当场喝掉起效，
    /// 存进囊里等于在「体内水分已经过多、什么都咽不下」时留一条随时可用的后路。
    /// 源表里它有自己的数字 id（不是 id="potion" 的随机药水）。
    /// </summary>
    private static bool IsEtherPotion(Thing t)
    {
        return t != null && t.id == WetnessDef.EtherAntibodyPotionId;
    }

    /// <summary>两个入口（使用 / 混入界面）的共同落点。</summary>
    private bool Blend(Chara c, Thing t)
    {
        if (c == null || t == null || ReferenceEquals(t, owner))
        {
            return false;
        }

        if (IsPourTarget(t))
        {
            return PourOut(c, t);
        }
        return PourIn(c, t);
    }

    /// <summary>倒进去。吃掉了整瓶药水的数量，只多出一口。</summary>
    private bool PourIn(Chara c, Thing src)
    {
        TraitDrink drink = src?.trait as TraitDrink;
        if (drink == null)
        {
            return false;
        }

        // 空瓶之类的「喝不了」的容器不给倒
        if (!drink.CanDrink(c))
        {
            c.Say(WetnessLang.SkinNotDrinkable, c, owner);
            return false;
        }

        if (IsEtherPotion(src))
        {
            return false;
        }

        // 名字要在消耗之前取，而且取「一份」的那种写法 ——
        // 堆叠的药水 Name 是「3瓶轻伤治疗药水」，直接拿来用会让人以为倒进去了三瓶，
        // 实际上一次只倒一瓶（ModNum(-1)）。
        string srcName = src.NameOne;
        string label = Label;
        string ident = IdentityOf(src);

        if (Doses > 0 && !string.IsNullOrEmpty(Ident) && Ident != ident)
        {
            c.Say(WetnessLang.SkinNoMixing(label, srcName), c, owner);
            return false;
        }

        if (Doses >= MaxDoses)
        {
            c.Say(WetnessLang.SkinFull, c, owner);
            return false;
        }

        owner.SetStr(WetnessDef.SkinKeyIdent, ident);
        owner.SetStr(WetnessDef.SkinKeyLabel, srcName);
        owner.SetStr(WetnessDef.SkinKeyN1, drink.N1);
        owner.SetInt(WetnessDef.SkinKeyEffect, (int)drink.IdEffect);
        owner.SetInt(WetnessDef.SkinKeyPower, drink.Power);
        owner.SetInt(WetnessDef.SkinKeyBless, (int)src.blessedState);
        owner.SetInt(WetnessDef.SkinKeyDoses, Doses + 1);

        // 倒出来时要还原成「原来那种药水」，光靠 effect 不够 ——
        // 玩家喝的是那瓶东西自己的 trait，所以原物的 id 与 refVal 都得记下。
        owner.SetStr(WetnessDef.SkinKeySrcId, src.id);
        owner.SetInt(WetnessDef.SkinKeySrcRefVal, src.refVal);

        // 连同「知不知道这是什么」一起记：新造出来的一瓶默认是未鉴定的
        // （Thing.OnCreate 里 c_IDTState = 5），不还回去就等于把人家的
        // 鉴定结果丢了，倒出来还得再鉴定一次。
        owner.SetInt(WetnessDef.SkinKeySrcIdt, src.c_IDTState);

        Wetness.Plugin.LogPourIn(src, ident, srcName);
        RefreshDoseDisplay();

        // Card.ModNum 在数量归零时会自己 Destroy，药水这样就消耗掉了。
        src.ModNum(-1);

        // 瓶子留下。
        //
        // 原版喝药水是不留瓶子的（Chara.Drink 里一句 t.ModNum(-1) 就完了），
        // 而不留的话「倒出来」永远缺一个容器 —— 倒进去和倒出来两头接不上。
        //
        // 不会因此变成无限药水：井是靠 charges 的
        // （TraitPotionEmpty.OnUse 里 well?.ModCharges(-1)），瓶子只是免去反复买而已。
        if (Plugin.ReturnEmptyBottle)
        {
            c.Pick(ThingGen.Create("potion_empty"));
        }

        c.Say(WetnessLang.SkinPoured(srcName), c, owner);
        return true;
    }

    /// <summary>
    /// 倒出来：把一口内容还原成一瓶药水，灌进那个空容器里。
    /// 空容器被消耗掉（瓶子还是那个瓶子，只是重新装满了），水囊少一口。
    ///
    /// 这一步原版没有 —— 原版的空瓶只能装地块上的水或井水
    /// （TraitPotionEmpty.OnUse(Chara, Point)），对药水使用空瓶不会发生任何事。
    /// </summary>
    private bool PourOut(Chara c, Thing container)
    {
        int doses = Doses;
        if (doses <= 0)
        {
            c.Say(WetnessLang.SkinNothingToPourOut, c, owner);
            return false;
        }

        string srcId = owner.GetStr(WetnessDef.SkinKeySrcId);
        if (string.IsNullOrEmpty(srcId))
        {
            // 只有本功能之前存下的水囊会走到这里（那时还没记来源 id）。
            c.Say(WetnessLang.SkinCannotPourOut, c, owner);
            return false;
        }

        // 必须用 CreateExact 而不是 ThingGen.Create —— 见 WetnessOriginPatch 的说明：
        // 只要源表里有任何一行写了 `_origin = potion`，`potion` 就会被点亮 isOrigin，
        // 于是 ThingGen.Create("potion") 不再造「随机药水那一行」，而是从它的变体里挑一个，
        // 造出来的根本不是原来那瓶（refVal 覆写在别的行上没意义）。
        Thing potion = Wetness.Patches.WetnessOriginPatch.CreateExact(srcId, WetnessDef.PourOutLevel);
        if (potion == null)
        {
            c.Say(WetnessLang.SkinCannotPourOut, c, owner);
            return false;
        }

        // 随机药水生成时口味是掷出来的，得覆写成囊里那种。
        int refVal = owner.GetInt(WetnessDef.SkinKeySrcRefVal);
        if (refVal != 0)
        {
            // 走游戏自己的 TraitPotion.Create：它除了写 refVal，
            // 还会给带 noCopy 标签的元素补一条基础值（直接赋 refVal 会漏掉那一步）。
            if (potion.trait is TraitPotion && EClass.sources.elements.map.ContainsKey(refVal))
            {
                TraitPotion.Create(potion, refVal);
            }
            else
            {
                potion.refVal = refVal;
            }
        }
        potion.SetBlessedState((BlessedState)owner.GetInt(WetnessDef.SkinKeyBless));

        // 鉴定状态也还回去。新造出来的一瓶在 Thing.OnCreate 里被写成未鉴定
        // （药水的 source.unknown 非空），照抄回去才是「原来那瓶」——
        // 否则玩家灌进去的是自己鉴定过的药水，倒出来还得再鉴定一次。
        // 旧存档没有这个键，GetInt 给 0，也就是已鉴定。
        potion.c_IDTState = owner.GetInt(WetnessDef.SkinKeySrcIdt);

        container.ModNum(-1);
        c.Pick(potion);

        string poured = potion.Name;
        Wetness.Plugin.LogPourOut(srcId, refVal, potion);
        ConsumeDose();
        RefreshDoseDisplay();
        c.Say(WetnessLang.SkinPouredOut(poured), c, owner);
        return true;
    }
}
