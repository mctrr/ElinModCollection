using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ACS.Patches;
using UnityEngine;

namespace ACS.Tiling;

/// <summary>
/// 分片贴图的<b>唯一 store 与唯一接口</b>。
///
/// 以前这张图有 7 个入口（<c>TryGetCached</c> / <c>TryGetCachedByPpu</c> / <c>TryGetByPath</c> /
/// <c>TryAdoptPreloaded</c> / <c>TryAdoptFromPixel</c> / <c>AdoptOffThread</c> / <c>StoreDecoded</c>）
/// 并且「预热」「渲染」「驱逐」各自伸进去看内部表：预热要拿缓存键快照与待建数量，
/// 渲染要自己收养 PixelCache，驱逐要另问字节账。于是每条路径都得假设别人没做成，
/// 兜底越加越多（原来的 <c>TryAdoptPreloaded</c> 是四级串行兜底）。
///
/// 现在外面只有四个动作：
///   · <see cref="Get"/>         —— 取图（命中 / 就地收养 / 判定失败），渲染与原版加载路径共用；
///   · <see cref="TileNow"/>     —— 原版路径遇到超限图时，自己同步解 + 分片 + 入库；
///   · <see cref="Prewarm"/> + <see cref="AdoptPending"/> —— 预热（后台解像素、主线程按预算建贴图）；
///   · <see cref="Release"/> / <see cref="BytesHeld"/> —— 释放与字节账。
///
/// <b>字节账留在内部</b>：<see cref="Entry"/> 自己带 <c>Bytes</c>，
/// 外面只问「这个路径现在占多少」（<see cref="BytesHeld"/>），不再需要拆开 data.tex / 分片两处算。
///
/// 缓存键统一是 <b>png 全路径</b>（带 .png）。<see cref="Release"/> / <see cref="BytesHeld"/>
/// 接受带不带扩展名的路径，内部归一 —— 调用方不必再自己拼 <c>path + ".png"</c>。
/// </summary>
internal static class TiledSprite
{
    /// <summary>一张 png 解出来的全部帧贴图（分片或整条）。解码产物，字节账随它一起给出。</summary>
    internal sealed class TiledSheet
    {
        internal Sprite[] Sprites = Array.Empty<Sprite>();
        internal Texture2D? Tex;

        /// <summary>这套贴图占的显存字节（宽×高×4×片数）。</summary>
        internal long Bytes;

        internal string Note = string.Empty;
    }

    /// <summary>一条缓存：产物 + 判定新鲜度所需的元数据。</summary>
    internal sealed class Entry
    {
        internal int Frame;
        internal int Ppu;
        internal DateTime StampUtc;
        internal TiledSheet Sheet = new();
    }

    /// <summary>已建好贴图的缓存（png 全路径 → 产物）。只在主线程读写。</summary>
    private static readonly Dictionary<string, Entry> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>已记过成功日志的路径，避免每次重载刷屏。</summary>
    private static readonly HashSet<string> Logged = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 后台已解出、还没建贴图的<b>纯像素</b>。
    /// 与 <see cref="Cache"/> 的区别：这里不含任何 Unity 对象，所以可以在解码线程写入、
    /// 跨帧持有；真正的建贴图由 <see cref="AdoptPending"/> 或 <see cref="Get"/> 在主线程完成。
    /// 写入方是解码线程，所以这一张表单独加锁。
    /// </summary>
    private static readonly Dictionary<string, PngTiler.DecodedImage> PixelCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly object PixelGate = new();

    // ───────────────────────── 取图 ─────────────────────────

    /// <summary>
    /// <b>唯一取图入口</b>：把 <paramref name="png"/> 切好的帧挂到 <paramref name="data"/> 上。
    /// 命中顺序（逐步放宽，确保「预热解过就一定用得上」）：
    ///   ① <c>(png, frame, ppu)</c> 精确命中；
    ///   ② <c>(png, ppu)</c> 忽略 frame 命中（少数皮肤底图与 <c>_acs_idle</c> 帧数声明不同步）；
    ///   ③ 像素已解好、贴图未建 → 就地为这一张补建（主线程，每图一次）；
    ///   ④ 按路径命中，忽略 frame/ppu 差异。
    /// 返回 true = 已挂上，调用方应跳过原版 <c>LoadSprites</c>。
    /// </summary>
    internal static bool Get(SpriteData data, string png)
    {
        if (data is null || string.IsNullOrEmpty(png)) {
            return false;
        }

        int frames = Math.Max(1, data.frame);
        int ppu = PngTiler.ResolvePpu(data);

        if (TryGet(png, frames, ppu, out Entry? exact)) {
            Attach(data, exact!);
            if (Logged.Add("pre:" + png)) {
                AcsMod.Log("preloaded: '" + png + "' " + exact!.Sheet.Note + "（跳过原版解码）");
            }

            return true;
        }

        if (TryGetByPpu(png, ppu, out Entry? byPpu)) {
            Attach(data, byPpu!);
            if (Logged.Add("pre:" + png)) {
                AcsMod.Log("preloaded: '" + png + "' " + byPpu!.Sheet.Note + "（跳过原版解码, frame 已校正）");
            }

            return true;
        }

        // 像素已解出但贴图未建：就地为这一张补建。
        // 这是**设计内的兜底**：AdoptPending 按帧预算分批建贴图，渲染若先用到某张，
        // 就在这里把这一张的上传挪到本帧 —— 只多花这一张，不会退回原版同步解码。
        PngTiler.DecodedImage? pending = TakePending(png, frames, ppu);
        if (pending is not null) {
            if (Logged.Add("race:" + png)) {
                AcsMod.Log("[BCS] 预热：渲染先到，就地为 '" + Path.GetFileName(png) + "' 建贴图");
            }

            TiledSheet? sheet = PngTiler.BuildSheet(pending, png, frames, ppu);
            if (sheet is not null) {
                Put(png, frames, ppu, sheet, pending.StampUtc);
                if (TryGet(png, frames, ppu, out Entry? built)) {
                    Attach(data, built!);
                }
            }

            return true;
        }

        if (TryGetByPath(png, out Entry? byPath)) {
            Attach(data, byPath!);
            if (Logged.Add("pre:" + png)) {
                AcsMod.Log("preloaded: '" + png + "' " + byPath!.Sheet.Note + "（按路径命中, frame 已校正）");
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// 超限图的同步路径：自己解 PNG、按帧边界分片、入库并挂到 <paramref name="data"/>。
    /// 未超限 / 解不了返回 false（调用方回落原版）。
    /// </summary>
    internal static bool TileNow(SpriteData data, string png, out string note)
    {
        note = string.Empty;
        if (data is null || string.IsNullOrEmpty(png)) {
            note = "路径为空";
            return false;
        }

        int frames = Math.Max(1, data.frame);
        int ppu = PngTiler.ResolvePpu(data);

        if (TryGet(png, frames, ppu, out Entry? hit)) {
            Attach(data, hit!);
            note = hit!.Sheet.Note + "（缓存）";
            return true;
        }

        TiledSheet? sheet = PngTiler.DecodeSheet(data, png, out note);
        if (sheet is null) {
            return false;
        }

        Put(png, frames, ppu, sheet);
        data.sprites = sheet.Sprites;
        data.tex = sheet.Tex;
        note = sheet.Note;
        return true;
    }

    /// <summary>这张 png 是否已在缓存里且仍然新鲜（帧数 / PPU / mtime 都一致）。</summary>
    internal static bool Fresh(string png, int frames, int ppu) => TryGet(png, frames, ppu, out _);

    private static void Attach(SpriteData data, Entry entry)
    {
        data.sprites = entry.Sheet.Sprites;
        data.tex = entry.Sheet.Tex;
        if (data.frame != entry.Frame) {
            data.frame = entry.Frame;
        }
    }

    // ───────────────────────── 预热 ─────────────────────────

    /// <summary>
    /// 预热一步：把这张 png 解成纯像素（读盘 + inflate + 反 filter）。
    /// <b>可在任意线程调用</b>，绝不碰 Unity API；建贴图留给 <see cref="AdoptPending"/>。
    /// 与渲染路径是同一个 store：先解过的图，<see cref="Get"/> 直接命中（像素阶段见 Get 的 ③）。
    /// </summary>
    internal static void Prewarm(string png, int frames, int ppu)
    {
        if (string.IsNullOrEmpty(png)) {
            return;
        }

        long start = Stopwatch.GetTimestamp();
        PngTiler.DecodedImage? img = PngTiler.DecodeOffThread(png, frames, ppu, out string err);
        long end = Stopwatch.GetTimestamp();

        if (img is null) {
            PrewarmProbe.NoteDecodeFailure(png, err);
            return;
        }

        PrewarmProbe.NoteDecode(png, start, end, img);
        lock (PixelGate) {
            PixelCache[Key(png)] = img;
        }
    }

    /// <summary>
    /// 主线程分批建贴图：把已解好的像素建成贴图上传 GPU。
    /// <paramref name="budgetMs"/> 是每帧预算（<c>int.MaxValue</c> = 一次做完，阻塞预热用），
    /// 一张 16384×512 的 RGBA32 分片上传就是 ~32MB，一次全建会把进场那帧堵成几百毫秒。
    /// </summary>
    internal static void AdoptPending(int budgetMs)
    {
        string[] keys = PendingKeys();
        if (keys.Length == 0) {
            return;
        }

        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < keys.Length; i++) {
            double elapsedMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            if (elapsedMs >= budgetMs) {
                return;
            }

            AdoptOne(keys[i]);
        }
    }

    /// <summary>已解好、还没建贴图的张数（预热流水线用它限流，避免像素在内存里积压）。</summary>
    internal static int PendingCount
    {
        get {
            lock (PixelGate) {
                return PixelCache.Count;
            }
        }
    }

    private static string[] PendingKeys()
    {
        lock (PixelGate) {
            if (PixelCache.Count == 0) {
                return Array.Empty<string>();
            }

            var keys = new string[PixelCache.Count];
            PixelCache.Keys.CopyTo(keys, 0);
            return keys;
        }
    }

    private static bool AdoptOne(string png)
    {
        if (Cache.ContainsKey(png)) {
            lock (PixelGate) {
                PixelCache.Remove(png);
            }

            return true;
        }

        PngTiler.DecodedImage? img;
        lock (PixelGate) {
            if (!PixelCache.TryGetValue(png, out img)) {
                return false;
            }

            PixelCache.Remove(png);
        }

        if (img is null) {
            return false;
        }

        TiledSheet? sheet = PngTiler.BuildSheet(img, png, img.Frames, img.Ppu);
        if (sheet is not null) {
            Put(png, img.Frames, img.Ppu, sheet, img.StampUtc);
        }

        return true;
    }

    /// <summary>就地把「已解好、帧数/PPU 与本次请求一致」的像素取出来（取走即移除）。</summary>
    private static PngTiler.DecodedImage? TakePending(string png, int frames, int ppu)
    {
        string key = Key(png);
        lock (PixelGate) {
            if (!PixelCache.TryGetValue(key, out PngTiler.DecodedImage? img)
                || img is null
                || img.Frames != frames
                || img.Ppu != ppu) {
                return null;
            }

            PixelCache.Remove(key);
            return img;
        }
    }

    // ───────────────────────── 公开表（模块私有使用） ─────────────────────────

    /// <summary>挂上一条已建好的产物（解码路径与收养路径共用）。</summary>
    private static void Put(string png, int frames, int ppu, TiledSheet sheet)
        => Put(png, frames, ppu, sheet, PngTiler.SafeStamp(png));

    /// <summary>
    /// 同上，但 mtime 由调用方带过来：后台解码已经在解码线程取过一次文件时间，
    /// 主线程落地时不必再 stat 一次磁盘（<see cref="PngTiler.DecodedImage.StampUtc"/>）。
    /// </summary>
    private static void Put(string png, int frames, int ppu, TiledSheet sheet, DateTime stampUtc)
    {
        Cache[Key(png)] = new Entry {
            Frame = frames,
            Ppu = ppu,
            StampUtc = stampUtc,
            Sheet = sheet,
        };
    }

    private static bool TryGet(string png, int frames, int ppu, out Entry? entry)
    {
        entry = null;
        if (string.IsNullOrEmpty(png)) {
            return false;
        }

        string key = Key(png);
        if (!Cache.TryGetValue(key, out Entry? c) || c is null) {
            return false;
        }

        if (c.Frame != frames
            || c.Ppu != ppu
            || c.Sheet.Sprites.Length == 0
            || c.Sheet.Sprites[0] is null
            || c.Sheet.Sprites[0].texture is null
            || c.Sheet.Tex is null
            || c.StampUtc != PngTiler.SafeStamp(key)) {
            Cache.Remove(key);
            return false;
        }

        entry = c;
        return true;
    }

    /// <summary>同 <see cref="TryGet"/> 但忽略 frame：只要 png + PPU 一致即视为命中。</summary>
    private static bool TryGetByPpu(string png, int ppu, out Entry? entry)
    {
        entry = null;
        if (string.IsNullOrEmpty(png)) {
            return false;
        }

        string key = Key(png);
        if (!Cache.TryGetValue(key, out Entry? c) || c is null) {
            return false;
        }

        if (c.Ppu != ppu
            || c.Sheet.Sprites.Length == 0
            || c.Sheet.Sprites[0] is null
            || c.Sheet.Sprites[0].texture is null
            || c.Sheet.Tex is null
            || c.StampUtc != PngTiler.SafeStamp(key)) {
            Cache.Remove(key);
            return false;
        }

        entry = c;
        return true;
    }

    /// <summary>按 png 全路径命中（忽略 frame/ppu）。PPU 对同一 png 确定，故安全。</summary>
    private static bool TryGetByPath(string png, out Entry? entry)
    {
        entry = null;
        if (string.IsNullOrEmpty(png)) {
            return false;
        }

        string key = Key(png);
        if (!Cache.TryGetValue(key, out Entry? c) || c is null) {
            return false;
        }

        if (c.Sheet.Sprites.Length == 0
            || c.Sheet.Sprites[0] is null
            || c.Sheet.Sprites[0].texture is null
            || c.Sheet.Tex is null
            || c.StampUtc != PngTiler.SafeStamp(key)) {
            Cache.Remove(key);
            return false;
        }

        entry = c;
        return true;
    }

    // ───────────────────────── 记账 / 释放 ─────────────────────────

    /// <summary>某个路径当前占的显存字节；不在缓存里返回 0。带不带 <c>.png</c> 都行。</summary>
    internal static long BytesHeld(string path)
    {
        if (string.IsNullOrEmpty(path)) {
            return 0L;
        }

        string key = Key(path);
        return Cache.TryGetValue(key, out Entry? c) && c is not null ? c.Sheet.Bytes : 0L;
    }

    /// <summary>缓存当前持有的贴图总字节数（诊断用）。</summary>
    internal static long TotalBytes
    {
        get {
            long sum = 0;
            foreach (var kv in Cache) {
                sum += kv.Value?.Sheet.Bytes ?? 0L;
            }

            return sum;
        }
    }

    /// <summary>
    /// 释放一个路径的分片缓存（按需释放用）。
    /// 分片结果的 <c>Sprites</c>/<c>Tex</c> 是**独立于 <c>data.tex</c> 的第二处强引用** ——
    /// 只把 <c>data.tex = null</c> 是收不回显存的，必须连这里一起移除。
    /// 保留 <see cref="Logged"/>：重新分片时不再刷一遍成功日志。
    /// </summary>
    /// <returns>true = 命中并移除。</returns>
    internal static bool Release(string path)
        => !string.IsNullOrEmpty(path) && Cache.Remove(Key(path));

    /// <summary>换图 / <c>ACS.ReloadAcs</c> 后清空：允许重新分片、重新告警。</summary>
    internal static void Clear()
    {
        Cache.Clear();
        lock (PixelGate) {
            PixelCache.Clear();
        }

        Logged.Clear();
    }

    /// <summary>缓存键统一是 png 全路径；调用方给不带扩展名的路径也能对上。</summary>
    private static string Key(string path)
        => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? path : path + ".png";
}
