using HarmonyLib;

namespace Wetness.Patches;

/// <summary>
/// 管住源表 `_origin` 那一列带来的**副作用**：它会给被引用的行点亮 `isOrigin`，
/// 而游戏把 `isOrigin == true` 当成「这是基础行，请去挑一个变体」用了四处。
///
/// ---- 这个标志哪来的 ----
/// SourceCard 的导入循环（SourceCard.cs:62-71）**无条件**给被引用的那一行打标：
///
///     if (!row._origin.IsEmpty())
///     {
///         row.origin = EClass.sources.things.map[row._origin];
///         firstVariations[row._origin] = row;      // ← 「基础行 → 第一个变体」
///         row.origin.isOrigin = true;              // ← 被引用的行中标
///     }
///
/// 只要**任何一行**写了 `_origin = X`，X 那一行就变成「基础行」。
///
/// ---- 四个消费点 ----
/// | 位置 | 代码 | 后果 |
/// |---|---|---|
/// | `ThingGen._Create` | `SpawnListThing.Get("origin_"+id, …).Select(lv)?.id ?? …First().id` | 造物入口把 id 换成某个变体 |
/// | `Thing.SetSource`  | `source = firstVariations[id]; id = source.id;` | **所有 `Card.Create` 都要过这里** |
/// | `SerializedCards.Restore` | `if (source.isOrigin) text = SpawnListThing.Get("origin_"+text, …).GetFirst().id` | 读档恢复地图卡片 |
/// | `ThingContainer.GetThingStack` / `Props` | `current.source._origin == id` | 统计/计数（`isOrigin` 为真时把变体算成本体） |
///
/// ---- 两起案子 ----
/// ① **空瓶变成水囊**（v0.13.4 没修好，v0.13.5 修好）
/// 水囊的 ThingV 行写了 `_origin = potion_empty`，于是 `potion_empty` 中标。
/// 「造空瓶」于是变成「挑一个空瓶的变体」—— 而唯一的变体就是水囊本身。
///
/// ② **倒出来的不是原来那瓶药水**（v0.13.6）
/// 同一套机制打在药水上：只要有**任何一行**写 `_origin = potion`（例如
/// `Chara.TryPoisonPotion` 就在找这种行：`a._origin == "potion" && a.tag.Contains("neg")`），
/// `potion` 就中标。于是 `ThingGen.Create("potion", …)` **不会**造出随机药水那一行，
/// 而是从「药水的变体」里按权重挑一个 —— 造出来的根本不是原来那瓶。
/// 随机药水的口味全靠 `refVal`，可 `refVal` 覆写在**另一行**上毫无意义。
///
/// ---- 修法 ----
/// 不清 `isOrigin`（它还兼着 `RecipeManager.BuildList` 的「跳过、不建配方」判据，
/// 清掉会凭空多出一条配方），而是：
///   ① 空瓶：三处消费点各拦一道，只对 `potion_empty` 短路；
///   ② 重建物品（`CreateExact`）：谁要求「就要这一行本身」，就把两处重定向一起绕开。
/// 只有 `PourOut` 用 ② —— 它要还原的是玩家原本拿着的那一行。
/// </summary>
// 类级 [HarmonyPatch] 必需：缺了它整个类会被 HarmonyX 静默跳过（见 WetnessDrinkPatch 注释）。
[HarmonyPatch]
internal static class WetnessOriginPatch
{
    /// <summary>
    /// 「就要这一行本身」模式。只由 <see cref="CreateExact"/> 开关，
    /// 环绕 `Card.Create` 那一小段 —— 不能常开，那会把游戏的造物语义整个改掉。
    ///
    /// 用 [ThreadStatic] 而不是普通静态字段：游戏主线程是唯一的写入者，
    /// 但 mod 没有任何理由去赌这个假设，代价只有一个 slot。
    /// </summary>
    [System.ThreadStatic]
    private static bool _exactRow;

    /// <summary>
    /// 照着 id 造一个 Thing，**不做 `isOrigin` 重定向**。
    ///
    /// 与 `ThingGen.Create` 的区别：
    /// - 这一行是「基础行」时，`ThingGen.Create` 会把它换成某个变体，这里原样造出来；
    ///   不是基础行时两者完全等价（重定向本来也不会发生）。
    /// - **表里没有这个 id 时返回 null**，不会像 `ThingGen.Create` 那样塞一个
    ///   "869" 兜底物品出来。调用方要的是「原来那件东西」，退化成垃圾物品不如报错。
    ///
    /// 用在 `TraitWaterskin.PourOut`：玩家倒进去的那瓶药水**本来就是这一行**，
    /// 换行就等于换药。
    /// </summary>
    public static Thing CreateExact(string id, int lv)
    {
        SourceThing.Row row = string.IsNullOrEmpty(id) ? null : Resolve(id);
        if (row == null)
        {
            return null;
        }

        if (!row.isOrigin)
        {
            return ThingGen.Create(id, -1, lv);
        }

        Thing thing = new Thing();
        bool prev = _exactRow;
        _exactRow = true;
        try
        {
            thing.Create(id, -1, lv < 1 ? 1 : lv);
        }
        finally
        {
            _exactRow = prev;
        }
        return thing;
    }

    /// <summary>空瓶自检与药水自检共用的查表。</summary>
    public static SourceThing.Row Resolve(string id)
    {
        return EClass.sources?.things?.map != null
            && EClass.sources.things.map.TryGetValue(id, out SourceThing.Row row)
            ? row
            : null;
    }

    /// <summary>
    /// ① 造物总入口：`ThingGen.Create(id)` → 这里。
    ///
    /// 两条短路：
    /// - `id == potion_empty`：原方法会把空瓶换成变体（水囊），必须拦。
    /// - `_exactRow`：调用方要求「原样造」，同样跳过重定向。
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(ThingGen), nameof(ThingGen._Create))]
    private static bool CreatePrefix(string id, int idMat, int lv, ref Thing __result)
    {
        bool exact = _exactRow;
        if (!exact && id != WetnessDef.EmptyBottleId)
        {
            return true;
        }

        // 表里没有这个 id 就交给原方法 —— 它有 "869" 兜底，我们自己抄一份只会抄坏。
        if (Resolve(id) == null)
        {
            return true;
        }

        // 原方法里除重定向之外就这几步，照抄。
        Thing thing = new Thing();
        thing.Create(id, idMat, lv < 1 ? 1 : lv);
        __result = thing;
        return false;
    }

    /// <summary>
    /// ② `Card.Create` 内部无条件调用的 `SetSource()`。
    ///
    /// 原实现（Thing.cs:164）：
    ///     source = EClass.sources.things.map.TryGetValue(id);
    ///     if (source != null && source.isOrigin)
    ///     {
    ///         source = EClass.sources.cards.firstVariations[id] as SourceThing.Row;
    ///         id = source.id;                 // ← 空瓶在此变成水囊
    ///     }
    ///
    /// 这是**所有** `new Thing().Create(...)` 的必经之路 —— 包括①里我们自己
    /// 手动 Create 的那一次。跳过重定向，①才有可能真的生效
    /// （v0.13.4 就是只拦了①，产物照旧是水囊）。
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Thing), nameof(Thing.SetSource))]
    private static bool SetSourcePrefix(Thing __instance)
    {
        bool exact = _exactRow;
        if (!exact && __instance.id != WetnessDef.EmptyBottleId)
        {
            return true;
        }

        SourceThing.Row row = Resolve(__instance.id);
        if (row == null)
        {
            return true;   // 交给原方法走它的 ash3 兜底，别在这里制造 null
        }

        __instance.source = row;
        return false;
    }

    /// <summary>
    /// ③ 读档/进图时恢复地图卡片，`SerializedCards.Restore` 自己查了一次变体表
    /// （SerializedCards.cs:737-742），**不经过** ThingGen：
    ///
    ///     if (source.isOrigin)
    ///     {
    ///         SpawnList spawnList = SpawnListThing.Get("origin_" + text, a => a.origin == source);
    ///         if (spawnList.rows.Count == 0) continue;      // ← 这张卡直接丢掉
    ///         text = spawnList.GetFirst().id;
    ///     }
    ///     card2 = ThingGen.Create(text, -1, EClass._zone.DangerLv);
    ///
    /// 而 `SpawnList.CreateMaster` 只收 `chance > 0 && !isOrigin` 的行（CardFilter.Pass），
    /// 水囊在源表里的 `chance = 0` —— 于是这个 list 是**空的**，
    /// `continue` 把地图上的空瓶整张丢掉了（不是变成水囊，是凭空消失，更难发现）。
    ///
    /// 这里对 `origin_potion_empty` 直接回一个「只含空瓶自己」的 list，
    /// 于是 Restore 拿到 `text = "potion_empty"` → 继续走 `ThingGen.Create` → 被①接住。
    ///
    /// 只处理空瓶：药水那一行没有这个问题（玩家手上的东西本来就不是地图卡片）。
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(SpawnListThing), nameof(SpawnListThing.Get))]
    private static bool SpawnListPrefix(string id, ref SpawnList __result)
    {
        if (id != "origin_" + WetnessDef.EmptyBottleId)
        {
            return true;
        }

        SourceThing.Row row = Resolve(WetnessDef.EmptyBottleId);
        if (row == null)
        {
            return true;
        }

        // 现搭一个只含空瓶的 list。不写进 SpawnList.allList 缓存 ——
        // 缓存里那条（空表或含水囊）留给别的调用方，别互相污染。
        SpawnList list = new SpawnList();
        list.id = id;
        list.Add(row);
        __result = list;
        return false;
    }
}

// 关于第四个消费点（ThingContainer.GetThingStack / Props）：
// 那里是 `current.id == id || (isOrigin && current.source._origin == id)`，
// 意思是「问空瓶有多少个时，水囊也被算进去」。它不会把空瓶变成水囊，
// 所以不在修复范围内 —— 但那是个真实的计数错误（配方材料、交付任务会误判），
// 等实测撞上再单独处理。
