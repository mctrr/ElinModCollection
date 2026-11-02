using System.Collections.Generic;
using ACS.API;
using ACS.Patches;

namespace ACS.Skin;

/// <summary>
/// 一次皮肤查找的<b>完整结果</b>：把「读卡片 → 归一 id → 查表/补建 → 挑渲染实例」
/// 这条链一次走完，调用方（绑定 / 预热列表 / 缓存驱逐）直接读字段，不再各自重读皮肤。
///
/// 为什么要把四个来源都留着：它们本来就是四份不同的东西，
/// 以前每处调用各自去卡片上重取一遍（并且各自重做一次 id 归一），
/// 于是同一个角色在同一帧里被读了两三次、归一了两三次。
///   · <see cref="Id"/>       装备的 skin id（<c>c_idSpriteReplacer</c> 归一）
///   · <see cref="RowId"/>    角色行自带的 id（<c>sourceCard.idSprite</c> 归一）
///   · <see cref="Equipped"/> <c>dictSkins[Id]</c>（必要时按 Texture 编目补建）
///   · <see cref="Bound"/>    <c>chara.spriteReplacer</c> 快照
///   · <see cref="Race"/>     <c>sourceCard.replacer</c> 快照
///   · <see cref="Skin"/>     上面三者裁决出的「渲染该用的实例」
/// </summary>
internal readonly struct SkinRef
{
    /// <summary>装备的皮肤 base id（已归一）；空 = 没装自定义皮肤。</summary>
    internal readonly string Id;

    /// <summary>角色行自带的皮肤 id（已归一）；空 = 角色行没写皮肤。</summary>
    internal readonly string RowId;

    /// <summary>皮肤表里的规范实例（<c>dictSkins[Id]</c>）；查不到为空。</summary>
    internal readonly SpriteReplacer? Equipped;

    /// <summary>查找时角色身上绑着的实例（<c>chara.spriteReplacer</c>）。</summary>
    internal readonly SpriteReplacer? Bound;

    /// <summary>种族默认实例（<c>sourceCard.replacer</c>）。</summary>
    internal readonly SpriteReplacer? Race;

    /// <summary>渲染该用的实例（装备实例优先，其次绑定的非种族实例，最后种族）。</summary>
    internal readonly SpriteReplacer? Skin;

    /// <summary><see cref="Skin"/> 上有真实 <c>_acs_*</c>，等价于旧 <c>AcsSuffixes.Resolve</c> 非空。</summary>
    internal readonly bool HasAcs;

    internal SkinRef(string id, string rowId, SpriteReplacer? equipped, SpriteReplacer? bound,
                     SpriteReplacer? race, SpriteReplacer? skin, bool hasAcs)
    {
        Id = id;
        RowId = rowId;
        Equipped = equipped;
        Bound = bound;
        Race = race;
        Skin = skin;
        HasAcs = hasAcs;
    }

    /// <summary>装了自定义皮肤（id 非空；可能是只有底图、没有 <c>_acs_</c> 的皮肤）。</summary>
    internal bool IsCustom => !Id.IsEmpty();

    /// <summary>渲染实例的 suffixes 表；没有实例返回 null。</summary>
    internal Dictionary<string, SpriteData>? Suffixes => Skin?.suffixes;
}

/// <summary>
/// 皮肤查找模块的<b>唯一入口</b>。回答两个问题：
///   ① 这个角色装的是哪个皮肤（归一后的 base id）；
///   ② 该用哪个 <see cref="SpriteReplacer"/> 实例渲染。
///
/// 三件事必须只有一份实现，所以都收在这里：
///   · <b>id 归一</b>（<see cref="NormalizeId"/>）—— 以前 <c>AcsSuffixes.NormalizeBaseId</c>
///     与 <c>PcSkinPatch.NormalizeSkinId</c> 是两份逐条相同的实现；
///   · <b>安全读 id</b>（<see cref="ReadIdSafe"/>）—— <c>c_idSpriteReplacer</c> 的 getter 在
///     卡片刚 new 出来（<c>mapStr</c> 还是 null）时会 NRE；
///   · <b>查表 + 编目补建</b>（<see cref="Lookup"/> / <see cref="EnsureSkin"/>）——
///     以前绑定路径写一遍、UI 写一遍、驱逐再按路径归一回来。
///
/// 三条入口，区别只在「要不要碰磁盘、要不要完整快照」：
///   · <see cref="ResolveSuffixes(Chara?)"/> —— 渲染热路径：只要那张可播的 <c>_acs_*</c> 表；
///   · <see cref="Get(Chara?)"/>             —— 完整快照，只查已建好的 <c>dictSkins</c>
///                                             （预热 / 驱逐 / 底图兜底）；
///   · <see cref="Ensure(Chara?)"/>          —— 完整快照，表里没有就按 Texture 编目补建（绑定）。
/// </summary>
internal static class SkinIndex
{
    /// <summary>
    /// 路径分隔符表。<b>别写成 <c>new[] { '/', '\\' }</c></b>：那会在每次调用时分配一个
    /// 2 元素数组，而归一在渲染路径上每帧每角色都要跑。
    /// </summary>
    private static readonly char[] PathSeparators = { '/', '\\' };

    /// <summary>
    /// 把任意形态的皮肤 id / 文件名归一到 <c>dictSkins</c> 的 base key：
    /// 去掉目录、扩展名、<c>#N</c> 帧段、<c>_acs_*</c> 与 <c>_bcs_combat</c> 后缀。
    /// 全工程只有这一份实现。
    /// </summary>
    internal static string NormalizeId(string? rawId)
    {
        if (rawId.IsEmpty()) {
            return string.Empty;
        }

        string id = rawId!;
        try {
            if (id.IndexOfAny(PathSeparators) >= 0) {
                id = System.IO.Path.GetFileName(id);
            }
        }
        catch {
            // 非法路径字符：保持原样继续按后缀剥。
        }

        if (id.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)
            || id.EndsWith(".PNG", System.StringComparison.Ordinal)) {
            id = System.IO.Path.GetFileNameWithoutExtension(id);
        }

        int hash = id.IndexOf('#');
        if (hash >= 0) {
            id = id.Substring(0, hash);
        }

        int acsIdx = id.IndexOf("_acs_", System.StringComparison.Ordinal);
        if (acsIdx > 0) {
            id = id.Substring(0, acsIdx);
        }

        int staticCombatIdx = id.IndexOf(AcsSuffixes.StaticCombat, System.StringComparison.Ordinal);
        if (staticCombatIdx > 0) {
            id = id.Substring(0, staticCombatIdx);
        }

        return id;
    }

    /// <summary>
    /// 安全读取 <c>c_idSpriteReplacer</c>，读不到就返回空串，<b>不抛 NRE</b>。
    ///
    /// <b>为什么需要</b>：<c>c_idSpriteReplacer</c> 的 getter 是 <c>Card.GetStr(13)</c>，
    /// 而 <c>BaseCard.GetStr</c> 实际是 <c>mapStr.GetValueOrDefault(id, defaultStr)</c> ——
    /// <c>mapStr</c> 在 <c>Card</c> 还没初始化完时是 <b>null</b>，扩展方法直接在 null 上调用就炸。
    /// 读档 / <c>ListSkins()</c> 重建期间正好是这个窗口。
    ///
    /// <b>为什么不全局包</b>：渲染路径每帧都读 <c>c_idSpriteReplacer</c>，进过画面的角色
    /// <c>mapStr</c> 必然已就绪（否则游戏早就崩了），所以只有<b>启动 / 重建</b>这类
    /// 「角色可能刚 new 出来」的路径需要它。try/catch 不带异常时几乎零成本，但也不该滥用。
    /// </summary>
    internal static string ReadIdSafe(Chara? chara)
    {
        if (chara is null) {
            return string.Empty;
        }

        try {
            return chara.c_idSpriteReplacer ?? string.Empty;
        }
        catch (System.NullReferenceException) {
            // mapStr 未初始化 —— 当作「没装皮肤」处理，与 null 语义一致。
            return string.Empty;
        }
    }

    /// <summary>
    /// 只查已建好的皮肤表，不碰磁盘。返回<b>完整快照</b>（含角色行 id）——
    /// 预热、驱逐、底图兜底这类需要「这个角色还牵扯哪些实例/路径」的调用方用。
    /// 逐帧渲染请用 <see cref="ResolveSuffixes"/>：它只算这一帧真正需要的那部分。
    /// </summary>
    internal static SkinRef Get(Chara? chara) => Lookup(chara, ensure: false, wantRow: true);

    /// <summary>表里没有就按 Texture 编目补建一次。绑定路径用。</summary>
    internal static SkinRef Ensure(Chara? chara) => Lookup(chara, ensure: true, wantRow: true);

    /// <summary>按 id 取实例：皮肤表优先，查不到再按 Texture 编目补建。</summary>
    internal static SpriteReplacer? EnsureSkin(string? rawId)
    {
        string id = NormalizeId(rawId);
        if (id.IsEmpty()) {
            return null;
        }

        return Table(id) ?? TextureSkinCatalog.EnsureSkin(id);
    }

    private static SpriteReplacer? Table(string id)
        => SpriteReplacer.dictSkins is { } dict && dict.TryGetValue(id, out var skin) ? skin : null;

    /// <param name="wantRow">
    /// 要不要算 <see cref="SkinRef.RowId"/>（角色行 <c>sourceCard.idSprite</c> 的归一）。
    /// 只有预热 / 驱逐用它；渲染热路径每帧每角色都要跑，所以那一条不算，省一次字符串归一。
    /// </param>
    private static SkinRef Lookup(Chara? chara, bool ensure, bool wantRow)
    {
        if (chara is null) {
            return default;
        }

        string id = NormalizeId(ReadIdSafe(chara));
        var bound = chara.spriteReplacer;
        var race = chara.sourceCard?.replacer;
        string rowId = wantRow ? NormalizeId(chara.sourceCard?.idSprite) : string.Empty;

        SpriteReplacer? equipped = id.IsEmpty() ? null : Table(id);
        if (equipped is null && ensure && !id.IsEmpty()) {
            equipped = TextureSkinCatalog.EnsureSkin(id);
        }

        SpriteReplacer? skin;
        bool hasAcs;

        if (!id.IsEmpty()) {
            // 装备 id 是唯一真相，绝不信陈旧的 bound：换肤后 bound 可能还是种族 / 上一套皮肤，
            // 拿它兜底会把旧战斗片播出来（详见 FrameChoice 的选片注释）。
            if (AcsSuffixes.HasRealClip(equipped)) {
                skin = equipped;
                hasAcs = true;
            }
            else if (AcsSuffixes.HasRealClip(bound) && !ReferenceEquals(bound, race)) {
                // 表未命中（冷启动）时，已绑的非种族实例仍可用。
                skin = bound;
                hasAcs = true;
            }
            else {
                // 装备了自定义底图但没有真实 _acs_*：停在底图，不回落种族战斗片。
                skin = equipped ?? bound;
                hasAcs = false;
            }
        }
        else if (AcsSuffixes.HasRealClip(bound)) {
            skin = bound;
            hasAcs = true;
        }
        else if (AcsSuffixes.HasRealClip(race)) {
            skin = race;
            hasAcs = true;
        }
        else {
            skin = bound ?? race;
            hasAcs = false;
        }

        return new SkinRef(id, rowId, equipped, bound, race, skin, hasAcs);
    }

    /// <summary>
    /// 当前皮可播的 <c>_acs_*</c> 后缀表；没有真实 ACS 返回 null。
    /// 这是渲染热路径（<c>GetAcsClip</c>）唯一的皮肤入口。
    ///
    /// 副作用与原 <c>AcsSuffixes.Resolve</c> 完全一致：装备实例命中时保持在
    /// <c>chara.spriteReplacer</c> 上（NPC 的 sourceCard.replacer 是另一个实例）。
    /// </summary>
    internal static Dictionary<string, SpriteData>? ResolveSuffixes(Chara? chara)
    {
        var skin = Lookup(chara, ensure: false, wantRow: false);
        if (!skin.HasAcs) {
            return null;
        }

        if (skin.IsCustom && skin.Skin is not null
            && !ReferenceEquals(chara!.spriteReplacer, skin.Skin)) {
            chara.spriteReplacer = skin.Skin;
        }

        return skin.Suffixes;
    }

    /// <summary>
    /// 皮肤的动态底图：<c>replacer.data</c> → <c>suffixes[""]</c>（都要 path 非空）。没有返回 null。
    /// </summary>
    internal static SpriteData? BaseSheet(SpriteReplacer? skin)
    {
        if (skin is null) {
            return null;
        }

        if (skin.data is not null && !skin.data.path.IsEmpty()) {
            return skin.data;
        }

        if (skin.suffixes is not null
            && skin.suffixes.TryGetValue(AcsSuffixes.BaseKey, out var baseData)
            && baseData is not null
            && !baseData.path.IsEmpty()) {
            return baseData;
        }

        return null;
    }

    /// <summary>
    /// 这套皮肤能不能渲染：有真实 <c>_acs_*</c>，或有一张可用的动态底图。
    /// （只给 <c>_bcs_combat</c> 占位、没有任何底图的条目不算。）
    /// </summary>
    internal static bool Usable(SpriteReplacer? skin)
    {
        if (skin is null) {
            return false;
        }

        return AcsSuffixes.HasRealClip(skin) || BaseSheet(skin) is not null;
    }
}
