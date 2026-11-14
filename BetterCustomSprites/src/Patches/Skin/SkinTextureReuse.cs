using System;
using System.Collections.Generic;
using HarmonyLib;

namespace ACS.Patches;

/// <summary>
/// 让已解码的贴图跨 <c>SpriteReplacer.ListSkins()</c> 重建存活。<b>默认开启</b>，由
/// <c>贴图跨重建复用</c> 设置项（或 <c>ACS.KeepTextures</c> 命令）控制。
///
/// <b>为什么需要</b>：<c>ListSkins()</c> 是原版的「读一遍磁盘、重建整张 dictSkins」入口
/// （<c>PcSkinBinder.Ensure</c> 在读档 / 切区域 / 换肤 UI 都会打它）。
/// 重建会把所有 <c>SpriteData</c> 换成<b>全新对象</b>，新对象 <c>sprites/tex</c> 都是 null，
/// 于是用到时全部重新解码 —— 实测切一次区域回来要重解 greet 片 690ms、底图 962ms。
/// 旧对象被换下后，同一轮里 <c>SpriteCacheEvict</c> 还因为角色还没重新绑定而 <c>pinned 0</c>，
/// 把仅剩的贴图也释放掉 → 切场景必卡。
///
/// <b>怎么做的</b>：Prefix 记下当前所有 <c>path → SpriteData</c>（<b>只存引用，不复制像素</b>），
/// Postfix 在新表里按 path 把 <c>tex/sprites</c> 挂回去。零解码。
///
/// <b>安全边界</b>：
///   ① 只搬<b>同 path</b> 的对象 —— 帧数/密度都由文件名决定，path 相同即语义相同；
///   ② 新对象已自带贴图（重建过程里自己又加载过）时跳过，不覆盖；
///   ③ <c>ACS.ReloadAcs</c> 会调 <see cref="Suppress"/>，明确跳过复用 ——
///      否则换了 PNG 却不敲命令就看不到新图；
///   ④ 只断/接托管引用，从不 <c>Destroy</c>，最坏结果是多留一份引用等 GC。
/// </summary>
[HarmonyPatch(typeof(SpriteReplacer), nameof(SpriteReplacer.ListSkins))]
internal static class SkinTextureReuse
{
    /// <summary>总开关，默认开（保持「不因重建而无谓重解码」）。</summary>
    internal static bool Enabled;

    /// <summary>重建前抓拍：path → 仍持有贴图的 SpriteData。只存引用，像素零复制。</summary>
    private static readonly Dictionary<string, SpriteData> Snapshot = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>下次重建跳过复用（ACS.ReloadAcs 换过图，要读磁盘上的新图）。</summary>
    private static bool _suppress;

    /// <summary>快照抓取时刻。Postfix 一定会清；若 Harmony 没挂上则由 <see cref="Tick"/> 兜底丢弃。</summary>
    private static float _snapshotAt = -1f;

    /// <summary>快照存活上限（秒）。超过就说明 Postfix 没跑到，再挂着只会白占显存。</summary>
    private const float SnapshotTtlSeconds = 10f;

    /// <summary>换过素材后调用：下一次重建不做复用（<c>ACS.ReloadAcs</c> 路径）。</summary>
    internal static void Suppress()
    {
        _suppress = true;
        Snapshot.Clear();
        _snapshotAt = -1f;
    }

    /// <summary>每帧调用（<c>AcsMod.Update</c>）：快照没人消费就丢掉，防止 patch 失效时泄漏。</summary>
    internal static void Tick()
    {
        if (_snapshotAt < 0f) {
            return;
        }

        if (UnityEngine.Time.realtimeSinceStartup - _snapshotAt < SnapshotTtlSeconds) {
            return;
        }

        Snapshot.Clear();
        _snapshotAt = -1f;
        AcsMod.Warn("[BCS] 贴图复用快照未被 Postfix 消费，已丢弃");
    }

    [HarmonyPrefix]
    internal static void Prefix()
    {
        Snapshot.Clear();
        _snapshotAt = -1f;

        if (!Enabled) {
            return;
        }

        var dict = SpriteReplacer.dictSkins;
        if (dict is null || dict.Count == 0) {
            return;
        }

        foreach (var kv in dict)
        {
            var skin = kv.Value;
            if (skin is null) {
                continue;
            }

            Collect(skin.data);

            var subs = skin.suffixes;
            if (subs is null || subs.Count == 0) {
                continue;
            }

            foreach (var sub in subs.Values) {
                Collect(sub);
            }
        }

        _snapshotAt = UnityEngine.Time.realtimeSinceStartup;
    }

    [HarmonyPostfix]
    internal static void Postfix()
    {
        try {
            if (Snapshot.Count == 0) {
                return;
            }

            if (_suppress) {
                AcsMod.Log("[BCS] 贴图复用：已按 ACS.ReloadAcs 跳过（读取磁盘上的新图）");
                return;
            }

            if (!Enabled) {
                return;
            }

            var dict = SpriteReplacer.dictSkins;
            if (dict is null || dict.Count == 0) {
                return;
            }

            int restored = 0;
            int skipped = 0;

            foreach (var kv in dict)
            {
                var skin = kv.Value;
                if (skin is null) {
                    continue;
                }

                Restore(skin.data, ref restored, ref skipped);

                var subs = skin.suffixes;
                if (subs is null || subs.Count == 0) {
                    continue;
                }

                foreach (var sub in subs.Values) {
                    Restore(sub, ref restored, ref skipped);
                }
            }

            AcsMod.Log("[BCS] 贴图复用：重建后挂回 " + restored + " 张"
                + (skipped > 0 ? "（" + skipped + " 张新对象已自带，跳过）" : string.Empty)
                + "，省去同量重新解码");
        }
        finally {
            Snapshot.Clear();
            _snapshotAt = -1f;
            _suppress = false;
        }
    }

    private static void Collect(SpriteData? data)
    {
        if (data is null) {
            return;
        }

        // 没加载过的就别记了 —— 否则会凭空造出一堆「可复用」的空壳。
        if (data.tex is null && data.sprites is null) {
            return;
        }

        string path = data.path;
        if (path.IsEmpty()) {
            return;
        }

        Snapshot[path] = data;
    }

    private static void Restore(SpriteData? data, ref int restored, ref int skipped)
    {
        if (data is null) {
            return;
        }

        string path = data.path;
        if (path.IsEmpty() || !Snapshot.TryGetValue(path, out var old)) {
            return;
        }

        // 新对象自己已经加载过了：不覆盖。
        if (data.sprites is not null || data.tex is not null) {
            skipped++;
            return;
        }

        data.tex = old.tex;
        data.sprites = old.sprites;
        restored++;
    }
}