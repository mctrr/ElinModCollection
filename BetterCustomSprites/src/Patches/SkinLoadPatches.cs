using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ACS.API;
using ACS.Frame;
using ACS.Skin;
using HarmonyLib;
using UnityEngine;
using ACS.Tiling;

namespace ACS.Patches;

/// <summary>
/// Skin 扫描与后缀加载：ListSkins 合并 ACS 文件、Reload 解析 _acs_ 后缀。
/// </summary>
[HarmonyPatch]
internal class SkinListPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(SpriteReplacer), nameof(SpriteReplacer.ListSkins))]
    internal static void OnListSkins(Dictionary<string, SpriteReplacer> __result)
    {
        var staticCombatMerges = new List<StaticCombatMerge>();

        var staticCombatSkinIds = __result.Keys
            .Where(id => id.EndsWith(AcsSuffixes.StaticCombat, StringComparison.Ordinal))
            .ToArray();
        foreach (var staticCombatSkinId in staticCombatSkinIds) {
            if (TryMergeStaticCombatSkinIntoBase(staticCombatSkinId, __result) is { } merged) {
                staticCombatMerges.Add(merged);
            }
        }

        var acsSkinIds = __result.Keys
            .Where(id => id.Contains("_acs_"))
            .ToArray();
        foreach (var acsSkinId in acsSkinIds) {
            MergeAcsSkinIntoBase(acsSkinId, __result, load: false);
        }

        foreach (var entry in __result) {
            NormalizeAcsSuffixes(entry.Value, entry.Key, load: false);
            EnsureBaseFromAcsIdle(entry.Value, entry.Key);
        }

        // After native *_bcs_combat: optional Skin/bcs_static_map.ini [combat] fill-ins.
        foreach (var mapped in FrameChoice.ApplyStaticCombatMap(__result)) {
            staticCombatMerges.Add(new StaticCombatMerge(mapped.BaseId, mapped.Data));
        }

        // _bcs_combat 仅写入 dictSkins[baseId]；NPC 的 sourceCard.replacer 由 Elin 单独 Reload
        // 创建，并非同一实例，所以合并不会自动覆盖。
        // 把结果广播给当前已加载且匹配的 NPC 运行时实例。
        if (staticCombatMerges.Count > 0) {
            BroadcastStaticCombatToCharaInstances(staticCombatMerges);
        }

        // ListSkins 完成后立刻给已在场的 PC 重绑
        try {
            PcSkinBinder.Ensure("ListSkins", forceListSkins: false);
        }
        catch (Exception) {
            // 主菜单等尚未有 pc
        }
    }

    /// <summary>
    /// 单次 _bcs_combat 合并结果，供 ListSkins 末尾广播给已加载 NPC 实例。
    /// </summary>
    private readonly struct StaticCombatMerge
    {
        public string BaseId { get; }
        public SpriteData Data { get; }
        public StaticCombatMerge(string baseId, SpriteData data)
        {
            BaseId = baseId;
            Data = data;
        }
    }

    /// <summary>
    /// 把静态战斗 skin（如 pcr_jinghua_bcs_combat）合并到底 base skin（pcr_jinghua）的 suffixes。
    /// 合并成功时返回 (基名, 战斗数据)，用于 ListSkins 末尾广播到已加载 NPC 运行时实例。
    /// </summary>
    private static StaticCombatMerge? TryMergeStaticCombatSkinIntoBase(
        string combatSkinId,
        Dictionary<string, SpriteReplacer> dictSkins)
    {
        int suffixIdx = combatSkinId.IndexOf(AcsSuffixes.StaticCombat, StringComparison.Ordinal);
        if (suffixIdx <= 0) {
            return null;
        }

        string baseId = combatSkinId.Substring(0, suffixIdx);
        if (!dictSkins.TryGetValue(baseId, out var baseSkin)
            || !dictSkins.TryGetValue(combatSkinId, out var combatSkin)) {
            return null;
        }

        var combatData = combatSkin.suffixes.TryGetValue("");
        if (combatData is null) {
            return null;
        }

        baseSkin.suffixes[AcsSuffixes.StaticCombat] = combatData;
        dictSkins.Remove(combatSkinId);

        AcsMod.Log($"skin '{baseId}' static combat image (from {combatSkinId})");
        return new StaticCombatMerge(baseId, combatData);
    }

    /// <summary>
    /// 把 ListSkins 合并得到的 _bcs_combat 复制到当前地图上已加载 NPC 的运行时 replacer。
    /// 这些 replacer 与 dictSkins[baseId] 通常不是同一实例（Elin 内部 Reload 各自重建），
    /// 所以合并不会自动覆盖。匹配走 c_idSpriteReplacer 经 SkinIndex 归一后的基名。
    /// PC 跳过：PC 的 spriteReplacer 已被 TryApplyPcSkin 直接绑定到 dictSkins[baseId]。
    ///
    /// <b>为什么逐角色 try</b>：同 <see cref="SkinUi.RebindNpcCustomSkins"/> ——
    /// 读档 / 重建期间半初始化的 <c>Chara</c>（<c>IsPC</c>、<c>sourceCard</c> 都可能 NRE）
    /// 不能打断整批广播，否则排在它后面的 NPC 全部拿不到战斗贴图，且只在日志里留一条
    /// <c>BroadcastStaticCombatToCharaInstances: Object reference not set</c>。
    /// <b>为什么连取 map 都要包 try</b>：<c>EClass._map</c> 的 getter（<c>EClass.get__map</c>）
    /// 读的是 <c>EClass.core.map</c>，而本方法会在 <c>ModManager.RefreshMods</c> 的协程里
    /// 被调到 —— 那一刻 <c>EClass.core</c> 可能还没建好，<b>getter 自己就 NRE</b>。
    /// 10-04 实测：把它挪出 try 后游戏直接进不去（Player.log 第 1370 行）。
    /// 所以「循环体内逐角色隔离」之外，<b>取 map 这一行也必须包起来</b>。
    /// </summary>
    private static void BroadcastStaticCombatToCharaInstances(List<StaticCombatMerge> merges)
    {
        List<Chara>? charas;
        try
        {
            charas = EClass._map?.charas;
        }
        catch (Exception ex)
        {
            // 地图还没就绪（模组刷新期 / 读档期）：本轮跳过，等下一次 ListSkins 再补。
            AcsMod.Log("BroadcastStaticCombat: map not ready (" + ex.Message + "); skip");
            return;
        }

        if (charas is null)
        {
            return;
        }

        int updated = 0;
        foreach (var chara in charas)
        {
            if (chara is null) {
                continue;
            }

            try
            {
                if (chara.IsPC)
                {
                    continue;
                }

                // 安全读取 + 归一只有一份实现：读档 / 重建期间新 new 出来的 Chara，
                // mapStr 可能还是 null（c_idSpriteReplacer 的 getter 会 NRE），
                // 不能让它把整个广播循环打断。
                string normalized = SkinIndex.Get(chara).Id;
                if (normalized.IsEmpty())
                {
                    continue;
                }

                foreach (var merge in merges)
                {
                    if (normalized != merge.BaseId)
                    {
                        continue;
                    }

                    var replacer = chara.spriteReplacer ?? chara.sourceCard?.replacer;
                    if (replacer?.suffixes is null)
                    {
                        break;
                    }

                    // 仅当缺位时注入：后续 ListSkins 不覆盖已写入的战斗数据
                    if (!replacer.suffixes.TryGetValue(AcsSuffixes.StaticCombat, out var existing)
                        || existing is null)
                    {
                        replacer.suffixes[AcsSuffixes.StaticCombat] = merge.Data;
                        updated++;
                    }

                    break;
                }
            }
            catch (Exception ex)
            {
                AcsMod.Warn($"BroadcastStaticCombat: one chara failed ({ex.Message}); continue");
            }
        }

        if (updated > 0)
        {
            AcsMod.Log($"broadcast static combat to {updated} runtime chara instance(s)");
        }
    }

    private static void MergeAcsSkinIntoBase(string acsSkinId, Dictionary<string, SpriteReplacer> dictSkins, bool load = true)
    {
        int acsIdx = acsSkinId.IndexOf("_acs_");
        if (acsIdx < 0) {
            return;
        }

        string baseId = acsSkinId.Substring(0, acsIdx);
        string acsPart = acsSkinId.Substring(acsIdx);

        var clip = AcsClip.CreateFromFormat(acsPart);
        if (clip is not { Length: > 0 }) {
            return;
        }

        if (!dictSkins.TryGetValue(acsSkinId, out var acsSkin)) {
            return;
        }

        var acsData = acsSkin.suffixes.TryGetValue("");
        if (acsData is null) {
            return;
        }

        // 文件名优先；覆盖错误/过期 ini，避免整条图当 1 帧导致巨大
        ApplyClipMeta(acsData, clip, load);

        if (!dictSkins.TryGetValue(baseId, out var baseSkin)) {
            // 无底图时用 ACS skin 充当 base，保证 LayerEditSkin 列表可显示
            baseSkin = acsSkin;
            acsData.id = baseId;
            baseSkin.data = acsData;
            dictSkins[baseId] = baseSkin;
            dictSkins.Remove(acsSkinId);
        }

        string suffixKey = $"_acs_{clip.Name}";
        baseSkin.suffixes[suffixKey] = acsData;
        baseSkin.suffixes[FrameChoice.ReservedSuffix] = null;

        dictSkins.Remove(acsSkinId);

        AcsMod.Log($"skin '{baseId}' clip '{clip.Name}' with {clip.Length} frames @ {clip.Interval}ms (from {acsSkinId})");
    }

    /// <summary>
    /// 长格式 _acs_idle#41_0-31 → 短 key _acs_idle + ApplyClipMeta。
    /// 短 key 解析失败绝不能 Remove，否则只剩 ReservedSuffix、画面仍是 PCC。
    /// 短 key 仍可从 data.path 文件名恢复 #interval_begin-end（Reload 后常见）。
    /// </summary>
    internal static void NormalizeAcsSuffixes(SpriteReplacer replacer, string logId, bool load = true)
    {
        if (replacer?.suffixes is null || replacer.suffixes.Count == 0) {
            return;
        }

        var acsSuffixes = replacer.suffixes
            .Where(kv => kv.Key != null && kv.Key.StartsWith("_acs_"))
            .ToArray();
        if (acsSuffixes.Length == 0) {
            return;
        }

        foreach (var entry in acsSuffixes) {
            var suffix = entry.Key;
            var data = entry.Value;
            var clip = ResolveClip(suffix, data);
            if (clip is not { Length: > 0 }) {
                // 短 key 且 path 也解析不出：保留条目，勿 Remove
                continue;
            }

            if (data is not null) {
                ApplyClipMeta(data, clip, load);
            }

            string shortKey = $"_acs_{clip.Name}";
            replacer.suffixes[shortKey] = data;
            replacer.suffixes[FrameChoice.ReservedSuffix] = null;

            if (suffix != shortKey) {
                replacer.suffixes.Remove(suffix);
            }

            AcsMod.Log($"skin '{logId}' clip '{clip.Name}' with {clip.Length} frames @ {clip.Interval}ms");
        }

        EnsureBaseFromAcsIdle(replacer, logId);
    }

    /// <summary>
    /// No base png: use _acs_idle (else first real ACS clip) as suffixes[""] / data
    /// so HasSprite(id) succeeds for NPC tables that still point at the short id.
    /// SpriteData.id is rewritten to the short id because vanilla HasSprite reloads
    /// whenever data.id != requested id.
    /// </summary>
    internal static void EnsureBaseFromAcsIdle(SpriteReplacer replacer, string id)
    {
        if (replacer?.suffixes is null || id.IsEmpty()) {
            return;
        }

        replacer.suffixes.TryGetValue("", out var existing);
        bool existingIsRealBase = existing is not null
            && (existing.path.IsEmpty()
                || existing.path.IndexOf("_acs_", StringComparison.Ordinal) < 0);

        if (existingIsRealBase) {
            if (replacer.data is null) {
                replacer.data = existing;
            }

            if (replacer.data is not null && replacer.data.id != id) {
                replacer.data.id = id;
            }

            return;
        }

        SpriteData? fallback = null;
        if (replacer.suffixes.TryGetValue("_acs_idle", out var idle) && idle is not null) {
            fallback = idle;
        }
        else {
            foreach (var kv in replacer.suffixes) {
                if (kv.Key is null || kv.Value is null || !kv.Key.StartsWith("_acs_")) {
                    continue;
                }

                fallback = kv.Value;
                if (kv.Key == "_acs_idle" || kv.Key.StartsWith("_acs_idle.")) {
                    break;
                }
            }
        }

        if (fallback is null) {
            fallback = existing;
        }

        if (fallback is null) {
            return;
        }

        fallback.id = id;
        replacer.suffixes[""] = fallback;
        replacer.data = fallback;
        if (!ReferenceEquals(existing, fallback)) {
            string file = fallback.path.IsEmpty() ? "_acs_idle" : Path.GetFileName(fallback.path);
            AcsMod.Log($"skin '{id}' base from ACS idle ({file})");
        }
    }

    /// <summary>
    /// 优先后缀键；短键（_acs_move）则从 path 文件名取 _acs_move#41_0-14。
    /// </summary>
    internal static AcsClip? ResolveClip(string suffix, SpriteData? data)
    {
        var clip = AcsClip.CreateFromFormat(suffix);
        if (clip is { Length: > 0 }) {
            return clip;
        }

        return ResolveClipFromPath(data);
    }

    /// <summary>
    /// 取不带目录、不带 .png 的文件名。
    /// 不能用 Path.GetFileNameWithoutExtension：状态名带点的 clip
    /// （pcr_xxx_acs_yongzhuang.greet#41_0-38）会被它把 ".greet#41_0-38"
    /// 当成扩展名切走，只剩 "pcr_xxx_acs_yongzhuang" → 正则匹配不到 # → clip 解析成 null。
    /// 原版 SpriteData.path 本身就不含扩展名，这里只是兜底剥一下 .png。
    /// </summary>
    internal static string FileNameNoExt(string path)
    {
        if (path.IsEmpty()) {
            return string.Empty;
        }

        int sep = path.LastIndexOfAny(new[] { '/', '\\' });
        string file = sep >= 0 ? path.Substring(sep + 1) : path;
        return file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? file.Substring(0, file.Length - 4)
            : file;
    }

    /// <summary>
    /// 仅从 path 文件名解析 ACS clip（显示热路径 / LoadAnimationIni 后校正用）。
    /// </summary>
    internal static AcsClip? ResolveClipFromPath(SpriteData? data)
    {
        if (data is null || data.path.IsEmpty()) {
            return null;
        }

        try {
            string file = FileNameNoExt(data.path);
            int acsIdx = file.IndexOf("_acs_", StringComparison.Ordinal);
            if (acsIdx < 0) {
                return null;
            }

            return AcsClip.CreateFromFormat(file.Substring(acsIdx));
        }
        catch (Exception) {
            return null;
        }
    }

    /// <summary>
    /// 文件名解析的帧数优先于 .ini。
    /// 错误 frame（常见 1）会把整条横图当单帧 → move 1920 宽整条显示，远大于 idle 128 格。
    ///
    /// 关键：禁止 data.GetSprites() 做强制重切。
    /// GetSprites → Load → LoadAnimationIni 会先读 ini 再 LoadSprites；
    /// 若 ini 缺失/解析失败会把 frame 打成 1，随后 sprites 按整条 strip 创建，
    /// 即使之后再改 data.frame，已有 sprites 也不会自动重切。
    /// 正确做法：<paramref name="load"/> 时写好 frame 后直接 LoadSprites()。
    /// </summary>
    /// <param name="load">
    /// true = 立即 ForceReloadSprites（这一次就解码）。单皮肤路径（Reload / EnsureSkin）用它。
    /// false = 只写 meta，不碰贴图 —— ListSkins 的全表遍历用，解码留给渲染首帧或场景预热。
    /// </param>
    internal static void ApplyClipMeta(SpriteData data, AcsClip clip, bool load = true)
    {
        if (data is null || clip is null || clip.Length <= 0) {
            return;
        }

        data.frame = clip.Length;
        data.time = clip.Interval / 1000f;
        data.scale = 100;

        try {
            AcsClipIni.WriteFrameMeta(data);
        }
        catch (Exception) {
            // 只读路径等：内存 frame 仍正确
        }

        // load=false：ListSkins 的全表遍历只写 meta，不在这里解码。
        // 真正解码交给渲染首帧（GetSprites → Load）或场景预热。否则打开换肤界面 /
        // 重建皮肤表一次就把所有皮肤的 _acs_ 片全解一遍，主线程直接卡住。
        if (load) {
            ForceReloadSprites(data);
        }
    }

    /// <summary>
    /// 按当前 data.frame 直接重切，不经过 LoadAnimationIni。
    /// 文件名带 @N 时按 N 的 PPU 切帧（nightly scaleTex 语义）：图放大了 N/100 倍，
    /// 世界尺寸不变。无标注走原逻辑（PPU=100），渲染数学与旧版逐位相同。
    /// clip 旁若有 .pref → LoadPref 并保留（scaleTex 已被 LoadPref postfix 剥掉，
    /// 密度只认文件名 @N/100；shadow/x/y/z/pivot 等键交给 vanilla Chara.Pref 走皮肤级语义）；
    /// 无 .pref → 维持 null（回落 replacer1，由 Chara.Pref postfix 再兜原皮链）。
    /// </summary>
    internal static void ForceReloadSprites(SpriteData data)
    {
        if (data is null) {
            return;
        }

        try {
            // **先问分片贴图 store，命中就直接挂上，根本不用进 LoadSprites。**
            //
            // 背景：<c>ListSkins()</c> 每次重建 dictSkins 都会造**全新的 SpriteData 对象**，
            // 它们的 <c>sprites</c>/<c>tex</c> 一律是 null。所以「看 data.sprites 判断要不要重切」
            // 这个判据在重建路径上<b>永远不成立</b>（实测同图仍被解 2~4 次）。
            //
            // 而 <c>TiledSprite</c> 的缓存是<b>按 png 路径</b>的静态表，跨 SpriteData 实例存活 ——
            // 它才是「这张图已经解好了吗」的正确判据。
            //
            // 顺序很关键：必须在 <c>data.sprites = null</c> <b>之前</b>问，
            // 否则清掉了就白问。
            int wantFrames = Math.Max(1, data.frame);
            if (TiledSprite.Get(data, data.path + ".png")) {
                _ = wantFrames;
                return;
            }

            // 缓存没有，再看这个对象自己是不是已经切好了（同一实例被重复 ApplyClipMeta 的情况）。
            var existing = data.sprites;
            if (existing is { Length: > 0 }
                && existing.Length == wantFrames
                && existing[0] is not null
                && data.tex is not null) {
                return;
            }

            data.sprites = null;

            // 皮肤级 .pref：文件在磁盘上就 LoadPref（首次），保留到 data.pref。
            // 之后再切帧/被 vanilla 懒加载读到都是同一份（scaleTex 已被 LoadPref postfix 剥掉）。
            if (data.pref is null && File.Exists(data.path + ".pref")) {
                data.LoadPref();
            }

            // @N 密度注入统一在 SpriteData.LoadSprites 前后缀做（vanilla 任意入口都覆盖），
            // 这里只负责按文件名把帧切对：LoadSprites 内部会按需以 @N PPU 重建切片。
            data.LoadSprites();
        }
        catch (Exception) {
            // path 未就绪等：留给显示路径再试
        }
    }

    /// <summary>无 @N 的 clip 跟随所属皮肤底图 pref 的 scaleTex；缓存避免每次 LoadSprites 都读盘。</summary>
    private static readonly Dictionary<string, int> SkinBasePpuCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 文件名 @N（clip.ScaleTex）优先；没标 @N 时回落到「本皮肤底图的 .pref scaleTex」——
    /// 作者只在底图上声明一次密度（scaleTex = 400），所有 _acs_ 片都按同一 PPU 切。
    /// 底图路径推导：clip 文件名里第一个 _acs_ 之前即皮肤 id，同目录优先、上一级次之
    /// （分别对应「acs 片平铺在 Texture/」与「acs 片收在 Texture/&lt;id&gt;/」两种布局）。
    /// 没有 pref 或没有 @N 的老皮肤返回 0 → 调用方按 100 处理，行为与旧版一致。
    /// </summary>
    internal static int ResolveDensityPpu(SpriteData data) => ResolveDensityPpu(data, out _);

    /// <param name="source">密度来源，仅供日志：filename(@N) / basePref(&lt;id&gt;.pref) / default(100)。</param>
    internal static int ResolveDensityPpu(SpriteData data, out string source)
    {
        source = "default";
        try {
            var clip = ResolveClipFromPath(data);
            if (clip is { ScaleTex: > 0 } && clip.ScaleTex != 100) {
                source = "filename";
                return clip.ScaleTex;
            }

            int skinPpu = ResolveSkinBasePpu(data);
            if (skinPpu > 0) {
                source = "basePref";
                return skinPpu;
            }
        }
        catch (Exception) {
        }

        return 100;
    }

    internal static int ResolveSkinBasePpu(SpriteData? data)
    {
        try {
            if (data is null || data.path.IsEmpty()) {
                return 0;
            }

            if (SkinBasePpuCache.TryGetValue(data.path, out int cached)) {
                return cached;
            }

            int ppu = 0;
            string file = FileNameNoExt(data.path);
            int acsIdx = file.IndexOf("_acs_", StringComparison.Ordinal);
            string? dir = Path.GetDirectoryName(data.path);
            if (acsIdx > 0 && !dir.IsEmpty()) {
                // base id 的推导口径只有一份（SkinIndex.NormalizeId）。
                string id = SkinIndex.NormalizeId(file);
                string? parent = Path.GetDirectoryName(dir);

                foreach (string candidate in new[] {
                    Path.Combine(dir, id),
                    parent.IsEmpty() ? string.Empty : Path.Combine(parent, id) }) {
                    if (candidate.IsEmpty() || !File.Exists(candidate + ".png")) {
                        continue;
                    }

                    int v = ReadPrefScaleTex(candidate + ".pref");
                    if (v > 0) {
                        ppu = v;
                        break;
                    }
                }
            }

            // 只缓存有效值：0（没 pref / pref 为空）不缓存，
            // 否则作者中途补写 .pref 后本局内一直吃不到，必须重启游戏。
            if (ppu > 0) {
                SkinBasePpuCache[data.path] = ppu;
            }

            return ppu;
        }
        catch (Exception) {
            return 0;
        }
    }

    /// <summary>
    /// 读 key=value 文本里的一个整数键（注释行以 ; / # 开头）。
    /// 找不到键、文件不存在、值不是整数一律返回 0。
    /// </summary>
    internal static int ReadIniInt(string iniPath, string key)
    {
        if (iniPath.IsEmpty() || key.IsEmpty()) {
            return 0;
        }

        try {
            if (!File.Exists(iniPath)) {
                return 0;
            }

            foreach (string raw in File.ReadAllLines(iniPath)) {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') {
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0) {
                    continue;
                }

                if (!line.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                if (int.TryParse(line.Substring(eq + 1).Trim(), out int v)) {
                    return v;
                }

                return 0;
            }
        }
        catch (Exception) {
        }

        return 0;
    }

    /// <summary>读 .pref 里唯一的 scaleTex 键（key=value 文本；注释行以 ; / # 开头）。</summary>
    internal static int ReadPrefScaleTex(string prefPath)
    {
        int v = ReadIniInt(prefPath, "scaleTex");
        return v > 0 ? v : 0;
    }

    /// <summary>只写 ints[25]（scaleTex 键）的 pref。vanilla：ScaleTex=ints[25]*0.01f，LoadSprites PPU=100*ScaleTex=ints[25]。</summary>
    internal static SourcePref? CreateDensityPref(int ppu)
    {
        if (ppu <= 0) {
            return null;
        }

        try {
            var pref = new SourcePref { ints = new int[26] }; // SourcePref.TotalPrefInts（nightly 版）
            pref.ints[25] = ppu;
            return pref;
        }
        catch (Exception) {
            return null;
        }
    }

    /// <summary>
    /// 确保 ACS clip 的 frame/scale/time/sprites 一致：
    /// 按文件名 clip 修正 frame 数并切好 sprites（横向 strip 等分）；
    /// meta（时长/缩放）已就绪的不重复处理。
    /// 纹理 filterMode 不在这里改：原版 IO.LoadPNG 默认就是 Point，BCS 不覆盖。
    /// </summary>
    internal static void EnsureAcsSprites(SpriteData data)
    {
        if (data is null || data.path.IsEmpty()) {
            return;
        }

        // 按需释放的「在用」信号：渲染路径与换肤列表图标路径都会经过这里。
        // 放在早退之前，已切好的片也要持续续期，否则正在播的动画会被判成冷数据。
        SpriteCacheEvict.Touch(data);

        // meta 已就绪（frame/scale/time 正确且 sprites 已按帧切开）：只补 Point 过滤后返回
        if (data.frame > 0
            && data.scale == 100
            && data.time > 0f
            && data.sprites is { Length: > 0 } sprites
            && sprites.Length == data.frame
            && sprites[0] is not null) {
            // 已切好但可能是整条未切 strip（frame==1 且宽高异常）时仍需处理
            bool looksLikeUncutStrip = data.frame == 1
                && sprites[0].texture is not null
                && sprites[0].rect.width > sprites[0].texture.height * 1.5f;

            // PPU 不符（vanilla Load() 等入口按 100 重切过 @N 图，会变巨大）→ 强制重切
            bool ppuOk = true;
            if (!looksLikeUncutStrip
                && data.path.IndexOf("_acs_", StringComparison.Ordinal) >= 0
                && sprites[0].pixelsPerUnit > 0f) {
                int expected = ResolveDensityPpu(data);
                if (expected > 0
                    && !Mathf.Approximately(sprites[0].pixelsPerUnit, expected)) {
                    ppuOk = false;
                }
            }

            if (!looksLikeUncutStrip && ppuOk) {
                return;
            }
        }

        var clip = ResolveClipFromPath(data);
        if (clip is not { Length: > 0 }) {
            // 非 ACS：帧数/PPU 由原版 Load 负责，BCS 不改
            return;
        }

        bool metaDirty = data.frame != clip.Length
            || data.scale != 100
            || data.time <= 0f;

        if (metaDirty) {
            data.frame = clip.Length;
            data.time = clip.Interval / 1000f;
            data.scale = 100;
        }

        bool needSlice = data.sprites is null
            || data.sprites.Length != data.frame
            || data.sprites.Length == 0
            || data.sprites[0] is null;

        // meta 骗过但实际是整条 strip：frame 记为 1、单帧宽度异常时强制重新切分
        if (!needSlice && data.sprites is { Length: 1 } && data.sprites[0] is not null) {
            try {
                var s0 = data.sprites[0];
                if (s0.texture is not null
                    && s0.rect.width > s0.texture.height * 1.5f
                    && clip.Length > 1) {
                    needSlice = true;
                }
            }
            catch (Exception) {
            }
        }

        if (metaDirty || needSlice) {
            NoteRecut(data, clip.Length, metaDirty, needSlice);
            ForceReloadSprites(data);
        }
    }

    private sealed class RecutStat
    {
        public int Count;
        public float NextLog;
    }

    private static readonly Dictionary<string, RecutStat> RecutStats = new(StringComparer.Ordinal);

    /// <summary>
    /// 探针：<see cref="ForceReloadSprites"/> 会 <c>sprites = null</c> 然后<b>立即 LoadSprites()</b>，
    /// 调用一次就等于完整重新解码一次（实测 move 片 142ms、greet 片 486~1194ms）。
    /// 而 <see cref="EnsureAcsSprites"/> 是每帧调的 —— 一旦判据恒成立，就是每帧解码，
    /// 也就是「每走一步卡一下 / 打招呼就卡」的直接来源。
    /// 按 path 节流记日志并带上三个判据，用来区分是 meta 不一致还是切片缺失。
    /// </summary>
    private static void NoteRecut(SpriteData data, int clipFrames, bool metaDirty, bool needSlice)
    {
        string path = data.path ?? string.Empty;
        if (path.Length == 0) {
            return;
        }

        float now = Time.realtimeSinceStartup;
        if (!RecutStats.TryGetValue(path, out RecutStat? st)) {
            st = new RecutStat();
            RecutStats[path] = st;
        }

        st.Count++;
        if (now < st.NextLog) {
            return;
        }

        st.NextLog = now + 5f;
        AcsMod.Log("[BCS] recut " + Path.GetFileName(path)
            + " x" + st.Count
            + " meta=" + (metaDirty ? "1" : "0")
            + " slice=" + (needSlice ? "1" : "0")
            + " frame " + data.frame + "/" + clipFrames
            + " scale " + data.scale
            + " time " + data.time.ToString("0.###")
            + " sprites " + (data.sprites?.Length.ToString() ?? "-"));
    }

    /// <summary>
    /// Drop already-sliced ACS sprites so the next ListSkins / display path recuts.
    /// Used by ACS.ReloadAcs after the user replaces PNGs on disk.
    /// </summary>
    internal static void ClearLoadedSprites()
    {
        var dict = SpriteReplacer.dictSkins;
        // 这次是用户主动换素材后要重读磁盘：贴图跨重建复用必须让路，否则新图看不到。
        SkinTextureReuse.Suppress();
        SkinBasePpuCache.Clear(); // pref scaleTex 可能刚改过
        SpriteDataLoadSpritesPatch.ClearFailureCaches(); // 素材换好了允许再试一次
        SkinEditUiPatch.ClearThumbs(); // 换肤列表的首帧缩略图一起失效
        if (dict is null) {
            return;
        }

        foreach (var skin in dict.Values) {
            if (skin is null) {
                continue;
            }

            ClearLoaded(skin.data);
            if (skin.suffixes is null) {
                continue;
            }

            foreach (var data in skin.suffixes.Values) {
                ClearLoaded(data);
            }
        }
    }

    private static void ClearLoaded(SpriteData? data)
    {
        if (data is null) {
            return;
        }

        data.sprites = null;
        data.tex = null;
    }
}

/// <summary>
/// 按需释放的「在用」信号：原版 <c>GetSprites()</c> 是渲染取贴图的漏斗，在这里续期，
/// 任何渲染路径都覆盖得到 —— 包括那些 <c>c_idSpriteReplacer</c> 为空、靠
/// <c>SkinIndex.ResolveSuffixes</c> 兜底显示皮肤的角色（它们在 <c>RenderFramePatch</c>
/// 第一行就 return，压根走不到 BCS 里那两处 Touch）。
/// </summary>
[HarmonyPatch(typeof(SpriteData), nameof(SpriteData.GetSprites))]
internal static class SpriteDataGetSpritesTouchPatch
{
    [HarmonyPostfix]
    internal static void Postfix(SpriteData __instance)
    {
        SpriteCacheEvict.Touch(__instance);
    }
}

/// <summary>
/// Vanilla LoadSprites NREs when IO.LoadPNG returns null or frame &lt; 1.
/// Keep the picker / GetSprite path from blowing up the whole list.
/// Also the single funnel for @N density: vanilla Load()/Validate->Load/
/// LoadPrefAndFixDensity all call LoadSprites, and each one would otherwise
/// recut a 4x sheet at PPU=100 (giant). Inject a scaleTex-only pref for the
/// duration of the call so every entry point slices at the filename @N PPU.
/// </summary>
[HarmonyPatch(typeof(SpriteData), nameof(SpriteData.LoadSprites))]
internal static class SpriteDataLoadSpritesPatch
{
    private static readonly Dictionary<SpriteData, SourcePref?> SavedPrefs = new();
    private static readonly HashSet<string> LoggedDensity = new();

    /// <summary>性能探针：走原版 <c>LoadSprites</c>（即 <c>IO.LoadPNG</c>）时的计时点与对应文件。
    /// 清零点归零，所以 Postfix / Finalizer 都会跑也不会重复计数。</summary>
    private static long _vanillaPerfStart;
    private static string _vanillaPerfPath = string.Empty;

    [HarmonyPrefix]
    internal static bool Prefix(SpriteData __instance)
    {
        if (__instance.frame < 1) {
            __instance.frame = 1;
        }

        // acs clip 的 ini 已不再生成（帧数全在文件名里）。若某个入口绕过了
        // LoadAnimationIni 直接 LoadSprites，frame 会停在 1 → 整条 strip 当一帧。
        // 这里按文件名兜底推导，保证切帧正确；真正的单帧 clip 不会被误改。
        if (__instance.frame == 1
            && !__instance.path.IsEmpty()
            && __instance.path.IndexOf("_acs_", StringComparison.Ordinal) >= 0) {
            var clip = SkinListPatch.ResolveClipFromPath(__instance);
            if (clip is { Length: > 0 }) {
                __instance.frame = clip.Length;
                if (__instance.time <= 0f) {
                    __instance.time = clip.Interval / 1000f;
                }
            }
        }

        if (__instance.path.IsEmpty()) {
            EnsureSprites(__instance);
            return false;
        }

        string png = __instance.path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? __instance.path
            : __instance.path + ".png";
        if (!File.Exists(png)) {
            EnsureSprites(__instance);
            return false;
        }

        // 密度注入**必须在预热/分片检查之前**：两者的 PPU 都由
        // ResolvePpu 读 pref.ints[25]（scaleTex）算出来，密度没注进去就算错，
        // 预热缓存按 (png, frame, ppu) 查 → 查不中 → 白解一场。
        InjectDensity(__instance);

        // 预热命中**必须排在分片之前**：预热已解好的图（含超限分片）直接挂上，
        // 跳过原版 IO.LoadPNG 与本类的同步分片解码，实测这类图一次要 200~900ms。
        // 分片分支只查已建好的 Cache，查不到就在主线程重解一遍 —— 而「预热解完、
        // 贴图还没建」的那几百毫秒窗口里渲染必然追上，正是残留卡顿的来源。
        if (TiledSprite.Get(__instance, png)) {
            return false;
        }

        // Unity 单张贴图有上限（16384px）。超了 Texture2D 构造直接失败，原版 LoadSprites
        // 半途抛异常 → sprites 只剩空槽 → 原版 CardActor.OnRender 无保护地写进 sr.sprite
        // → 下一帧 CharaActor.RefreshSprite 取 sprite.texture 直接 NullReference 刷屏。
        // 处置顺序：① BCS 自己解 PNG 按帧边界切成多张 ≤16384 的贴图（零损失，首选）；
        //          ② 分不了（坏图 / 16 位 / 隔行 / 高度也超限）→ 告警一次 + 安全态（frame=1 原版早退）。
        if (TryHandleOversize(__instance, png)) {
            return false;
        }

        // 之前切图失败过（坏图 / 超限）：不再反复读盘重试，否则每帧一次 Unity 报错 + 异常
        if (LoadFailedPaths.Contains(png)) {
            EnsureSprites(__instance);
            return false;
        }

        _vanillaPerfStart = BcsPerf.Tick();
        _vanillaPerfPath = png;
        return true;
    }

    [HarmonyPostfix]
    internal static void Postfix(SpriteData __instance)
    {
        RestorePref(__instance);
        ReportVanillaLoad(__instance);
    }

    /// <summary>
    /// 结算一次原版路径（<c>IO.LoadPNG</c>）的加载耗时。未超限的 sheet 全走这里，
    /// 但它同样可以在 Unity 原生解码器里吃掉几百毫秒 —— 不测就无法排除。
    /// 幂等：计完就把计时点清零，Finalizer 再调一次也不会重复计数。
    /// </summary>
    private static void ReportVanillaLoad(SpriteData instance)
    {
        long start = _vanillaPerfStart;
        string path = _vanillaPerfPath;
        _vanillaPerfStart = 0L;
        _vanillaPerfPath = string.Empty;

        if (start == 0L || path.Length == 0) {
            return;
        }

        string shape = instance?.tex is Texture2D t
            ? t.width + "x" + t.height
            : "?";

        BcsPerf.ReportLoad(path, BcsPerf.MsSince(start), 0d, "vanilla", shape);
    }

    [HarmonyFinalizer]
    internal static Exception? Finalizer(SpriteData __instance, Exception? __exception)
    {
        RestorePref(__instance);
        ReportVanillaLoad(__instance);

        if (__exception is null) {
            return null;
        }

        try {
            if (__instance is not null && !__instance.path.IsEmpty()) {
                LoadFailedPaths.Add(__instance.path);
            }

            EnsureSprites(__instance);
        }
        catch (Exception) {
        }

        return null;
    }

    /// <summary>Unity 单张贴图最大边长；超过它 Texture2D 构造直接失败。</summary>
    internal const int MaxTextureSize = 16384;

    /// <summary>已告警过的超限图（按 png 全路径），避免每帧刷日志。</summary>
    private static readonly HashSet<string> OversizeWarned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>切图失败过的路径：跳过重试（热重载时清空，允许改完素材再试一次）。</summary>
    private static readonly HashSet<string> LoadFailedPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>换图 / 热重载后允许对之前失败的贴图重试一次。</summary>
    internal static void ClearFailureCaches()
    {
        LoadFailedPaths.Clear();
        OversizeWarned.Clear();
    }

    /// <summary>
    /// 超限处置：先让 <see cref="TiledSprite.TileNow"/> 解 PNG 按帧边界分片（零损失，磁盘不动）；
    /// 分不了才告警一次并落地安全态。
    /// 返回 true 表示已处理（调用方应 return false 跳过原版 LoadSprites）。
    /// </summary>
    private static bool TryHandleOversize(SpriteData data, string png)
    {
        try {
            if (!TryReadPngSize(png, out int w, out int h)) {
                return false;
            }

            if (w <= MaxTextureSize && h <= MaxTextureSize) {
                return false;
            }

            // 密度必须与 vanilla 同源：acs 片的 @N 链 / 底图 pref 的 scaleTex。
            // InjectDensity 把 scaleTex 注入 data.pref，分片器再从 ints[25] 取回来。
            InjectDensity(data);

            if (TiledSprite.TileNow(data, png, out string note)) {
                return true;
            }

            LoadFailedPaths.Add(data.path);
            if (OversizeWarned.Add(png)) {
                int frames = data.frame > 0 ? data.frame : 1;
                int per = (MaxTextureSize / frames) / 128 * 128;
                string hint = per >= 128
                    ? $"请把每帧像素降到 {per}px（文件名加 @{per / 128 * 100}），或减少帧数"
                    : $"帧数太多（{frames} 帧），{MaxTextureSize}px 放不下任何 ≥128px 的帧，必须拆状态或大幅减帧";
                AcsMod.Warn(
                    $"贴图超出 Unity 单张上限 {MaxTextureSize}px 且无法内存分片：{w}x{h} '{png}'"
                    + $"（{frames} 帧 → 每帧 {w / frames}px；原因：{note}）。"
                    + $"已跳过切帧，角色回落种族图。{hint}");
            }

            EnsureSprites(data);
            return true;
        }
        catch (Exception ex) {
            try {
                if (OversizeWarned.Add(png)) {
                    AcsMod.Warn($"贴图超限处置异常（{png}）：{ex.Message}。已落安全态。");
                }

                EnsureSprites(data);
                return true;
            }
            catch (Exception) {
                return false;
            }
        }
    }

    /// <summary>PNG 宽高（8 字节签名 + IHDR 里的大端 4 字节宽/高）。非 PNG / 读不到返回 false。</summary>
    private static bool TryReadPngSize(string path, out int width, out int height)
    {
        width = 0;
        height = 0;
        try {
            using var fs = File.OpenRead(path);
            var buf = new byte[24];
            int read = 0;
            while (read < buf.Length) {
                int n = fs.Read(buf, read, buf.Length - read);
                if (n <= 0) {
                    break;
                }

                read += n;
            }

            if (read < buf.Length
                || buf[0] != 0x89 || buf[1] != 0x50 || buf[2] != 0x4E || buf[3] != 0x47) {
                return false;
            }

            width = (buf[16] << 24) | (buf[17] << 16) | (buf[18] << 8) | buf[19];
            height = (buf[20] << 24) | (buf[21] << 16) | (buf[22] << 8) | buf[23];
            return width > 0 && height > 0;
        }
        catch (Exception) {
            return false;
        }
    }

    private static void InjectDensity(SpriteData data)
    {
        try {
            if (data.path.IndexOf("_acs_", StringComparison.Ordinal) < 0) {
                return;
            }

            int ppu = SkinListPatch.ResolveDensityPpu(data, out string source);
            if (ppu <= 0 || ppu == 100) {
                return;
            }

            lock (SavedPrefs) {
                SavedPrefs[data] = data.pref;
            }

            data.pref = SkinListPatch.CreateDensityPref(ppu);

            if (LoggedDensity.Add(data.path)) {
                AcsMod.Log($"density: slicing '{data.path}' at PPU={ppu} (from {source})");
            }
        }
        catch (Exception) {
            // 注入失败就按原逻辑切（PPU=100），显示端 EnsureAcsSprites 会再校正
        }
    }

    private static void RestorePref(SpriteData data)
    {
        if (data is null) {
            return;
        }

        lock (SavedPrefs) {
            if (SavedPrefs.TryGetValue(data, out var old)) {
                data.pref = old;
                SavedPrefs.Remove(data);
            }
        }
    }

    /// <summary>
    /// 贴图拿不到 / 切不了时的安全落地。**绝不能**造「长度 = frame、内容全 null」的数组：
    /// 原版 CardActor.OnRender 是 <c>sr.sprite = spriteData.GetSprites().TryGet(spriteIndex, true)</c>
    /// （**没有 null 检查**），一旦把 null 写进 sr.sprite，下一帧任何 RefreshSprite 都会在
    /// <c>Sprite sprite = sr.sprite; sprite.texture</c> 处 NullReferenceException 刷屏
    /// （实测症状：贴图超 Unity 16384 上限 → 日志 "Texture has out of range width / height"
    /// + 一堆 CharaActor.RefreshSprite 的 NRE）。
    ///
    /// 这里改成：frame 钳到 1（原版 <c>if (spriteData.frame &lt;= 1) return;</c> 会早退，不再写 sr.sprite），
    /// sprites 留长度 1 的空槽 → GetSprite() 返回 null → Chara.GetSprite 回落种族图，角色仍可见。
    /// </summary>
    private static void EnsureSprites(SpriteData data)
    {
        if (data is null) {
            return;
        }

        // 已切好且首帧有效 → 不动
        if (data.sprites is { Length: > 0 } ok && ok[0] is not null) {
            return;
        }

        data.sprites = new Sprite[1];
        data.frame = 1;
    }
}

/// <summary>
/// 原版 LoadAnimationIni 无文件时 frame=1；有文件也可能被错误 ini 覆盖。
/// ACS 路径在读完 ini 后立刻用文件名帧数盖回去，避免后续 LoadSprites 切成整条。
/// </summary>
[HarmonyPatch(typeof(SpriteData), nameof(SpriteData.LoadAnimationIni))]
internal static class SpriteDataLoadAnimationIniPatch
{
    [HarmonyPostfix]
    internal static void Postfix(SpriteData __instance)
    {
        if (__instance.path.IsEmpty()) {
            return;
        }

        // 快速过滤：path 不含 _acs_ 则不是 ACS 横条
        if (__instance.path.IndexOf("_acs_", StringComparison.Ordinal) < 0) {
            return;
        }

        var clip = SkinListPatch.ResolveClipFromPath(__instance);
        if (clip is not { Length: > 0 }) {
            return;
        }

        if (__instance.frame != clip.Length) {
            __instance.frame = clip.Length;
        }

        __instance.time = clip.Interval / 1000f;
        if (__instance.scale != 100) {
            __instance.scale = 100;
        }
    }
}

/// <summary>
/// Beta：Reload(string id, RenderData) 后补齐 ACS 后缀；无底图种族也可从 dictModItems 建后缀。
/// </summary>
[HarmonyPatch]
internal class ReloadSuffixPatch
{
    private static readonly FieldInfo SortedModIdsField =
        AccessTools.Field(typeof(SpriteReplacer), "sortedModIds");

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SpriteReplacer), nameof(SpriteReplacer.Reload))]
    internal static void OnReloadSuffixes(SpriteReplacer __instance, string id, RenderData renderData)
    {
        // 无底图 PCC 种族仍可能在 dictModItems 有 _acs_ 贴图
        if (__instance.suffixes.Count == 0) {
            var dictModItems = SpriteReplacer.dictModItems;
            if (dictModItems is null || id.IsEmpty()) {
                // 即使无底图也尝试合并 _bcs_combat
                TryMergeBcsCombatOnReload(__instance, id);
                return;
            }

            bool hasAcsTexture = dictModItems.Keys.Any(k =>
                k != null && k.StartsWith(id) && k.Contains("_acs_"));
            if (hasAcsTexture) {
                try {
                    __instance.SortModItemIds();
                    var sorted = SortedModIdsField?.GetValue(null) as List<string>;
                    if (sorted is null) {
                        sorted = dictModItems.Keys.OrderBy(k => k).ToList();
                    }
                    __instance.BuildSuffixData(id, dictModItems, sorted);
                }
                catch (Exception ex) {
                    AcsMod.Warn($"BuildSuffixData fallback failed for '{id}': {ex.Message}");
                }
            }
        }

        if (__instance.suffixes.Count == 0) {
            TryMergeBcsCombatOnReload(__instance, id);
            return;
        }

        // 子目录布局：原版 Reload 只查 dictModItems，收不到 Texture/<id>/ 里的 clip，
        // 建出来的 suffixes 往往只有底图。用 BCS 自己的编目把 _acs_ 后缀补回来。
        if (!AcsSuffixes.HasRealClip(__instance)) {
            TryFillAcsSuffixesFromCatalog(__instance, id);
        }

        // load: false —— 只写 clip meta，不在这里把整套 _acs_ 片解出来。
        // Reload 是「角色首次用这个皮肤」时由渲染链触发的，一次解 6 片就是 300~600ms 的单帧卡
        // （实测 pcr_fenghuang 一次 645ms，全是这次 Reload 带出来的）。
        // 真正要显示哪一片，交给 EnsureAcsSprites 在该片首次渲染时就地补齐：
        // 白名单（底图/greet/move）吃场景预热缓存，于是「进视野」只花当前这一片的钱，不是整套。
        SkinListPatch.NormalizeAcsSuffixes(__instance, id, load: false);
        SkinListPatch.EnsureBaseFromAcsIdle(__instance, id);
        TryInjectStaticCombatFromDictSkins(__instance, id);
        TryMergeBcsCombatOnReload(__instance, id);
    }

    /// <summary>
    /// 从 <c>SkinIndex</c> 的编目结果把 _acs_* 后缀补进当前 Reload 出的实例。
    /// 只补缺失的键、只补真实 clip，已经建出 acs 的实例完全不受影响（幂等）。
    /// 典型触发：clip 收进了 <c>Texture/&lt;id&gt;/</c> 子目录，原版 dictModItems 扫不到。
    /// </summary>
    private static void TryFillAcsSuffixesFromCatalog(SpriteReplacer replacer, string id)
    {
        try {
            if (replacer?.suffixes is null || id.IsEmpty()) {
                return;
            }

            var canonical = SkinIndex.EnsureSkin(id);
            if (canonical is null
                || ReferenceEquals(canonical, replacer)
                || canonical.suffixes is null
                || !AcsSuffixes.HasRealClip(canonical)) {
                return;
            }

            int added = 0;
            foreach (var kv in canonical.suffixes) {
                if (kv.Key is null || kv.Value is null || !kv.Key.StartsWith("_acs_")) {
                    continue;
                }

                if (replacer.suffixes.ContainsKey(kv.Key)) {
                    continue;
                }

                replacer.suffixes[kv.Key] = kv.Value;
                added++;
            }

            if (added > 0) {
                replacer.suffixes[FrameChoice.ReservedSuffix] = null;
                AcsMod.Log($"skin '{id}' acs suffixes filled from catalog ({added})");
            }
        }
        catch (Exception ex) {
            AcsMod.Warn($"TryFillAcsSuffixesFromCatalog: {ex.Message}");
        }
    }

    private static void TryMergeBcsCombatOnReload(SpriteReplacer replacer, string id)
    {
        try {
            if (replacer?.suffixes is null || id.IsEmpty()) {
                return;
            }

            // 路径为空 / 加载失败：跳过
            if (replacer.suffixes.TryGetValue(AcsSuffixes.StaticCombat, out var existing)
                && existing is not null) {
                return;
            }

            // ListSkins ?? *_bcs_combat ??? base ? Remove ?? key?
            // 修正 data.id 对齐 dictSkins[id] 的 key（防原版 HasSprite 按错误 id 反复重载）
            SpriteData? combatData = null;
            string? source = null;

            if (SpriteReplacer.dictSkins.TryGetValue(id, out var baseSkin)
                && !ReferenceEquals(baseSkin, replacer)
                && baseSkin.suffixes is not null
                && baseSkin.suffixes.TryGetValue(AcsSuffixes.StaticCombat, out combatData)
                && combatData is not null) {
                source = id;
            }

            if (combatData is null) {
                string combatSkinId = id + AcsSuffixes.StaticCombat;
                if (SpriteReplacer.dictSkins.TryGetValue(combatSkinId, out var combatSkin)) {
                    combatData = combatSkin.data;
                    if (combatData is null
                        && combatSkin.suffixes is not null
                        && combatSkin.suffixes.TryGetValue("", out var raw)
                        && raw is not null) {
                        combatData = raw;
                    }

                    if (combatData is not null) {
                        source = combatSkinId;
                    }
                }
            }

            if (combatData is null || source is null) {
                return;
            }

            replacer.suffixes[AcsSuffixes.StaticCombat] = combatData;
            AcsMod.Log($"skin '{id}' static combat merged on Reload (from {source})");
        }
        catch (Exception ex) {
            AcsMod.Warn($"TryMergeBcsCombatOnReload: {ex.Message}");
        }
    }


    /// <summary>
    /// 从 <c>SpriteReplacer.dictSkins[id]</c> 取 _bcs_combat 注入当前 Reload 出的实例。
    /// 跳过：当前实例已是 dictSkins[id]（PC 等）；已存在 _bcs_combat；dictSkins 中没合并。
    /// </summary>
    private static void TryInjectStaticCombatFromDictSkins(SpriteReplacer replacer, string id)
    {
        try {
            if (replacer?.suffixes is null || id.IsEmpty()) {
                return;
            }

            if (replacer.suffixes.TryGetValue(AcsSuffixes.StaticCombat, out var existing)
                && existing is not null) {
                return;
            }

            if (SpriteReplacer.dictSkins.TryGetValue(id) is not { } canonical) {
                return;
            }

            if (ReferenceEquals(canonical, replacer)) {
                return;
            }

            if (canonical.suffixes is null
                || !canonical.suffixes.TryGetValue(AcsSuffixes.StaticCombat, out var combatData)
                || combatData is null) {
                return;
            }

            replacer.suffixes[AcsSuffixes.StaticCombat] = combatData;
        }
        catch (Exception ex) {
            AcsMod.Warn($"TryInjectStaticCombatFromDictSkins: {ex.Message}");
        }
    }
}
