using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using ACS.Patches;
using UnityEngine;

namespace ACS.Tiling;

/// <summary>
/// 超宽横条 strip 的**内存分片器**（零损失，磁盘一个字节都不改）。
///
/// 背景：Unity/D3D11 单张贴图每条边 ≤ 16384px（<c>D3D11_REQ_TEXTURE2D_U_OR_V_DIMENSION</c>），
/// 超过时 <c>Texture2D</c> 构造直接失败 → 原版 <c>IO.LoadPNG</c> 返回 null → 原版
/// <c>SpriteData.LoadSprites</c> 半途抛异常 → sprites 只剩空槽 → 原版
/// <c>CardActor.OnRender</c> 无 null 检查地把 null 写进 sr.sprite → 下一帧
/// <c>CharaActor.RefreshSprite</c> 取 sprite.texture 直接 NRE 刷屏。
///
/// 本节做法：**绕开 Unity 的 PNG 解码器**，自己 inflate + 反 filter，
/// 把一条超宽 strip 按**帧边界**切成若干张 ≤16384 的贴图，逐帧建 Sprite。
/// 渲染端拿到的仍是普通 <c>Sprite</c>（自带 texture / textureRect），像素零损失。
///
/// 为什么必须自己解：<c>ImageLoader.LoadPNG</c> 是
/// <c>new Texture2D(w, h, ...)</c> → 第一句就撞上限，<c>LoadImage</c> 永远跑不到。
///
/// 为什么连 inflate 也自己写：不依赖 <c>System.IO.Compression</c>（Mono / .NET 对 zlib 头
/// 的处理有历史差异，且本工程关掉了隐式框架引用），也不依赖 SharpZipLib。
/// 全部是 RFC 1950/1951 的确定行为，可离线逐像素对拍验证。
///
/// 三条铁律：
/// ① **切点只落帧边界**（按 frameW 整帧分配，绝不按像素切）；
/// ② **PPU 与 vanilla 同源**（读 <c>pref.ints[25]</c> = scaleTex，acs 片走 @N 链），
///    否则 4x 素材会渲染成 4 倍大的角色；
/// ③ **PNG 顶行 → 贴图顶行**（Unity 原点在左下，与 <c>Texture2D.LoadImage</c> 同向）。
///
/// 支持范围（实测 14 张超限图 100% 落在其中）：8 位、非隔行、colorType 6(RGBA)/2(RGB)。
/// </summary>
internal static class PngTiler
{
    /// <summary>Unity / D3D11 单张贴图最大边长。</summary>
    internal const int MaxDim = 16384;

    /// <summary>单张图解码后的过滤流上限（防御性，正常最大 ~54MB）。</summary>
    private const long MaxRawBytes = 256L * 1024 * 1024;

    /// <summary>已记过「已在内存分片」日志的路径，避免每次重载刷屏。</summary>
    private static readonly HashSet<string> Logged = new(StringComparer.OrdinalIgnoreCase);

    private struct Header
    {
        public int Width;
        public int Height;
        public int BitDepth;
        public int ColorType;
        public int Interlace;

        /// <summary>
        /// colorType=3（调色板）的 256 项 RGBA 查表（PLTE + 可选 tRNS 展开）。其它 colorType 为 null。
        /// 调色板图的像素是 1 字节索引，查这张表即得 RGBA。
        /// </summary>
        public byte[]? Palette;
    }

    /// <summary>一片 = 原 strip 的一段连续帧，落到一张独立贴图上。</summary>
    internal sealed class Slice
    {
        public int FirstFrame;
        public int Frames;
        public int SourceX;
        public int Width;
        public byte[] Pixels = Array.Empty<byte>();
    }

    /// <summary>
    /// 后台线程解码的产物：<b>纯 byte[]，不含任何 Unity 对象</b>。
    /// 由 <see cref="DecodeOffThread"/> 在工作线程产出，交 <see cref="BuildSheet"/>
    /// 在主线程消费（建 Texture2D / Sprite 是 Unity API，必须主线程）。
    /// </summary>
    internal sealed class DecodedImage
    {
        public int Width;
        public int Height;
        public int Frames;
        public int Ppu;
        public int NominalW;
        public bool Fractional;
        public int BitDepth;
        public int ColorType;
        public DateTime StampUtc;
        public Slice[] Slices = Array.Empty<Slice>();

        /// <summary>
        /// true = 整条未切图（未超限的宽条）。这种产物**不能**用分片那套
        /// 「每帧 rect.x = frameX[f] − SourceX」的算法 —— 整条就是一张贴图，
        /// 帧起点直接是 <c>f * NominalW</c>，切法与原版 <c>SpriteData.LoadSprites</c> 一致。
        /// </summary>
        public bool SingleWhole;
    }

    /// <summary>
    /// 在<b>任意线程</b>解一张 strip：读盘 + inflate + 反 filter。
    ///
    /// <b>本方法绝不碰 Unity API</b>（只做字节运算），所以可以安全地丢到线程池。
    /// 帧几何算法与 <see cref="DecodeSheet"/> 完全一致（同一段代码，抽成
    /// <see cref="Geometry"/>），保证预热产物与按需加载逐像素相同。
    ///
    /// <b>不限于超限图</b>：未超限的宽条（例如 16384×512 的 greet）同样能整条解成一片，
    /// 走这条路的产物能被 <see cref="TiledSprite.Get"/> 命中 →
    /// 原版 <c>IO.LoadPNG</c> 被完全跳过（实测这类图在 Unity 解码器里要 190ms+）。
    /// </summary>
    internal static DecodedImage? DecodeOffThread(string png, int frames, int ppu, out string err)
    {
        err = string.Empty;
        try {
            if (!TryReadHeader(png, out Header h)) {
                err = "PNG 头不可读";
                return null;
            }

            // mtime 在后台线程取：主线程落地时直接用，省掉每张一次文件 stat。
            DateTime stamp = SafeStamp(png);

            if (h.Width <= MaxDim && h.Height <= MaxDim) {
                // 未超限：整条仍是一张贴图，但帧宽必须是 width/frames。
                // NominalW 写成整图宽会让 Sprite.Create 的 rect 超出贴图，预热全部失败。
                if (!Geometry(h, frames, ppu, out _, out _, out _, out err)) {
                    return null;
                }

                int frameW = Math.Max(1, h.Width / Math.Max(1, frames));
                var whole = new List<Slice> {
                    new Slice {
                        FirstFrame = 0,
                        Frames = frames,
                        SourceX = 0,
                        Width = h.Width,
                    },
                };

                whole[0].Pixels = new byte[h.Width * h.Height * 4];
                if (!DecodeInto(png, h, whole, out err)) {
                    return null;
                }

                return new DecodedImage {
                    Width = h.Width,
                    Height = h.Height,
                    Frames = frames,
                    Ppu = ppu,
                    NominalW = frameW,
                    Fractional = false,
                    BitDepth = h.BitDepth,
                    ColorType = h.ColorType,
                    StampUtc = stamp,
                    Slices = whole.ToArray(),
                    SingleWhole = true,
                };
            }

            if (!Geometry(h, frames, ppu, out int nominalW, out int[] frameX, out bool fractional, out err)) {
                return null;
            }

            var slices = BuildSlices(frameX, nominalW, frames);
            foreach (Slice s in slices) {
                s.Pixels = new byte[s.Width * h.Height * 4];
            }

            if (!DecodeInto(png, h, slices, out err)) {
                return null;
            }

            return new DecodedImage {
                Width = h.Width,
                Height = h.Height,
                Frames = frames,
                Ppu = ppu,
                NominalW = nominalW,
                Fractional = fractional,
                BitDepth = h.BitDepth,
                ColorType = h.ColorType,
                StampUtc = stamp,
                Slices = slices.ToArray(),
                SingleWhole = false,
            };
        }
        catch (Exception ex) {
            err = "后台解码异常：" + ex.Message;
            return null;
        }
    }

    /// <summary>
    /// 在<b>主线程</b>把 <see cref="DecodeOffThread"/> 的产物变成真正的 Sprite。
    /// 只负责「像素 → 贴图」，不碰任何缓存：存哪、算多少字节由 <see cref="TiledSprite"/> 决定。
    ///
    /// 失败时只记日志、不抛 —— 预热失败等于「这次没预热到」，游戏走原来的按需加载。
    /// </summary>
    internal static TiledSprite.TiledSheet? BuildSheet(DecodedImage img, string png, int frames, int ppu)
    {
        try {
            if (img is null || img.Slices.Length == 0) {
                return null;
            }

            var slices = new List<Slice>(img.Slices);
            int nominalW = img.NominalW;

            // 帧起点表：与 DecodeOffThread 同一算法重算（不能只信 img —— Sprite.rect.x
            // 必须用每帧自己的起点，切错会让整条 UV 偏移）。
            var frameX = new int[frames];
            if (img.SingleWhole) {
                // 整条未切：帧宽 = width / frames，与原版 SpriteData.LoadSprites 一致。
                // 不用 NominalW 当帧宽：它可能大于单帧，rect 会超出贴图。
                int numWhole = Math.Max(1, img.Width / Math.Max(1, frames));
                if (numWhole > img.Width) {
                    numWhole = img.Width;
                }

                for (int f = 0; f < frames; f++) {
                    int x = f * numWhole;
                    if (x + numWhole > img.Width) {
                        x = img.Width - numWhole;
                    }

                    frameX[f] = x < 0 ? 0 : x;
                }

                nominalW = numWhole;
            }

            else {
                int xCap = img.Width - nominalW;
                if (xCap < 0) {
                    xCap = 0;
                }

                for (int k = 0; k < frames; k++) {
                    int x0 = (int)Math.Round((double)k * img.Width / frames);
                    frameX[k] = x0 > xCap ? xCap : x0;
                }
            }

            Sprite[] sprites = new Sprite[frames];
            Texture2D? firstTex = null;
            var pivot = new Vector2(0.5f, 0.5f);

            long texStart = System.Diagnostics.Stopwatch.GetTimestamp();
            long texBytes = 0;
            for (int si = 0; si < slices.Count; si++) {
                Slice s = slices[si];
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                var tex = new Texture2D(s.Width, img.Height, TextureFormat.RGBA32, false) {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = img.SingleWhole ? "bcs_whole" : "bcs_tile" + si,
                };
                tex.LoadRawTextureData(s.Pixels);
                long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
                tex.Apply(false, true);
                long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
                firstTex ??= tex;

                for (int i = 0; i < s.Frames; i++) {
                    int f = s.FirstFrame + i;
                    sprites[f] = Sprite.Create(
                        tex,
                        new Rect(frameX[f] - s.SourceX, 0f, nominalW, img.Height),
                        pivot,
                        ppu,
                        0u,
                        SpriteMeshType.FullRect);
                }

                long t3 = System.Diagnostics.Stopwatch.GetTimestamp();
                if (PrewarmProbe.Enabled) {
                    PrewarmProbe.TNewTex += t1 - t0;
                    PrewarmProbe.TApply += t2 - t1;
                    PrewarmProbe.TSpriteCreate += t3 - t2;
                    PrewarmProbe.Textures++;
                    PrewarmProbe.Slices += s.Frames;
                    PrewarmProbe.Pixels += (long)s.Width * img.Height;
                }

                texBytes += (long)s.Width * img.Height * 4;

                s.Pixels = Array.Empty<byte>();
            }

            PrewarmProbe.NoteTex(png, PrewarmProbe.Ms(System.Diagnostics.Stopwatch.GetTimestamp() - texStart));
            // 头和 mtime 都由解码线程带过来：这里不再重开文件，省掉每张一次 open+stat。
            var hh = new Header {
                Width = img.Width,
                Height = img.Height,
                BitDepth = img.BitDepth,
                ColorType = img.ColorType,
            };

            AcsMod.Log("[BCS] prewarmed '" + Path.GetFileName(png) + "' " + frames + "帧");
            return new TiledSprite.TiledSheet {
                Sprites = sprites,
                Tex = firstTex,
                Bytes = texBytes,
                Note = img.SingleWhole
                    ? (img.Width + "x" + img.Height + " 整条预热")
                    : Describe(hh, frames, nominalW, slices.Count, ppu, img.Fractional),
            };
        }
        catch (Exception ex) {
            AcsMod.Warn("[BCS] 预热落地失败 '" + Path.GetFileName(png) + "': " + ex.Message);
            return null;
        }
    }

    /// <summary>读头 + 算帧几何（DecodeSheet 与 DecodeOffThread 共用，保证两边一致）。</summary>
    /// <remarks>
    /// <b>帧几何：每帧起点按累积比例取整。</b>
    /// Width % frames == 0 时 FrameX(k) == k*(Width/frames)，与原版逐字节一致 → 零回归。
    /// 不可除时帧距是<b>连续小数</b>，既不是 floor 也不是 ceil，典型来源是整条 strip 撞上
    /// 32767（int16）上限后被导出工具<b>等比压缩</b>（宽 40960→32767、高 512→409，宽高同时缩小 = 等比铁证）：
    ///   · 固定用 floor(409)：每帧偏 +0.5875px，80 帧后累积偏出 46px；
    ///   · 固定用 ceil(410) ：每帧偏 -0.4125px，末帧反向偏出 33px；
    ///   · 累积起点 round(k*Width/frames)：任意 k 的误差 &lt; 0.5px，永不累积。← 采用这个
    /// 窗口宽度取 ceil：相邻帧最多重叠 1px，绝不漏列；取 round 时若帧距小数 &lt; 0.5 会漏 1px 空列。
    /// </remarks>
    /// <summary>
    /// PNG colorType → 每像素字节数。3（调色板）是 1 字节索引，靠 <see cref="Header.Palette"/> 变 RGBA。
    /// </summary>
    private static int Channels(int colorType) => colorType == 6 ? 4 : colorType == 3 ? 1 : 3;

    private static bool Geometry(Header h, int frames, int ppu, out int nominalW,
                                out int[] frameX, out bool fractional, out string err)
    {
        err = string.Empty;
        nominalW = 0;
        frameX = Array.Empty<int>();
        fractional = false;
        _ = ppu;

        if (h.Height > MaxDim) {
            err = $"高度 {h.Height} 也超限，纵向无法分片";
            return false;
        }

        if (h.BitDepth != 8) {
            err = $"只支持 8 位色深（实际 {h.BitDepth}）";
            return false;
        }

        if (h.Interlace != 0) {
            err = "不支持隔行（Adam7）";
            return false;
        }

        if (h.ColorType != 6 && h.ColorType != 2 && h.ColorType != 3) {
            err = $"只支持 RGBA/RGB/调色板（实际 colorType={h.ColorType}）";
            return false;
        }

        if (frames < 1) {
            frames = 1;
        }

        nominalW = (int)Math.Ceiling((double)h.Width / frames);
        if (nominalW <= 0) {
            err = $"帧数 {frames} 大于宽度 {h.Width}";
            return false;
        }

        if (nominalW > MaxDim) {
            err = $"每帧 {nominalW}px 已超上限（整条被当成一帧？检查 .ini 的 frame）";
            return false;
        }

        fractional = h.Width % frames != 0;

        int xCap = h.Width - nominalW;
        if (xCap < 0) {
            xCap = 0;
        }

        frameX = new int[frames];
        for (int k = 0; k < frames; k++) {
            int x0 = (int)Math.Round((double)k * h.Width / frames);
            frameX[k] = x0 > xCap ? xCap : x0;
        }

        return true;
    }

    /// <summary>按"源 x 跨度 ≤ MaxDim"分组切片（DecodeSheet 与 DecodeOffThread 共用）。</summary>
    private static List<Slice> BuildSlices(int[] frameX, int nominalW, int frames)
    {
        var slices = new List<Slice>();
        int f0 = 0;
        while (f0 < frames) {
            int xStart = frameX[f0];
            int last = f0;
            while (last + 1 < frames && frameX[last + 1] + nominalW - xStart <= MaxDim) {
                last++;
            }

            slices.Add(new Slice {
                FirstFrame = f0,
                Frames = last - f0 + 1,
                SourceX = xStart,
                Width = frameX[last] + nominalW - xStart,
            });
            f0 = last + 1;
        }

        return slices;
    }

    /// <summary>
    /// 超限时内存分片：读盘 → 按帧边界切 ≤<see cref="MaxDim"/> 的片 → 每片建一张贴图。
    /// <b>只解不存</b>：产物交给 <see cref="TiledSprite"/> 入库（存档、字节账、释放都在那边）。
    /// 未超限 / 解不了返回 null（调用方回落原版 <c>IO.LoadPNG</c>）。
    /// </summary>
    /// <param name="note">诊断用说明（成功=分片摘要；失败=原因）。</param>
    internal static TiledSprite.TiledSheet? DecodeSheet(SpriteData data, string png, out string note)
    {
        note = string.Empty;
        if (data is null || string.IsNullOrEmpty(png)) {
            note = "路径为空";
            return null;
        }

        if (!TryReadHeader(png, out Header h)) {
            note = "PNG 头不可读";
            return null;
        }

        if (h.Width <= MaxDim && h.Height <= MaxDim) {
            note = "未超限（不该走分片）";
            return null;
        }

        // 帧几何与切片布局：与后台预热路径**共用同一段实现**（Geometry / BuildSlices），
        // 否则两条路径一旦漂移就是「预热出来的图和按需加载的图不一样」这种极难查的 bug。
        // 详细推导（为什么用 round(k*W/n) 而不是 floor/ceil）见 Geometry 的注释。
        int frames = Math.Max(1, data.frame);
        if (!Geometry(h, frames, ResolvePpu(data), out int nominalW, out int[] frameX,
                       out bool fractional, out string geoErr)) {
            note = geoErr;
            return null;
        }

        int ppu = ResolvePpu(data);

        var slices = BuildSlices(frameX, nominalW, frames);

        // 像素缓冲总量 = 原图像素数（按片分摊，不翻倍）
        foreach (Slice s in slices) {
            s.Pixels = new byte[s.Width * h.Height * 4];
        }

        // —— 性能探针：把 CPU（读盘 + inflate + 反 filter）与 GPU（建贴图 + Apply + Sprite.Create）
        // 分开计时，才能判断「卡」到底卡在解码还是卡在上传。
        long perfDecode = BcsPerf.Tick();
        if (!DecodeInto(png, h, slices, out string err)) {
            note = err;
            return null;
        }

        double perfDecodeMs = BcsPerf.MsSince(perfDecode);

        Sprite[] sprites;
        Texture2D? firstTex = null;
        var pivot = new Vector2(0.5f, 0.5f);
        long perfUpload = BcsPerf.Tick();
        long texBytes = 0;
        try {
            sprites = new Sprite[frames];
            for (int si = 0; si < slices.Count; si++) {
                Slice s = slices[si];
                var tex = new Texture2D(s.Width, h.Height, TextureFormat.RGBA32, false) {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "bcs_tile" + si,
                };
                tex.LoadRawTextureData(s.Pixels);
                // makeNoLongerReadable=true：上传后释放 CPU 侧副本（最大 16384×512×4 ≈ 32MB/片）。
                // 原先必须保持可读，是给 AcsSpritePresenter 量帧底留白用的；那段读像素代码已移除。
                tex.Apply(false, true);
                firstTex ??= tex;

                for (int i = 0; i < s.Frames; i++) {
                    int f = s.FirstFrame + i;
                    // 帧距非整数时 rect.x 必须用该帧自己的起点，不能是 f*标称宽
                    sprites[f] = Sprite.Create(
                        tex,
                        new Rect(frameX[f] - s.SourceX, 0f, nominalW, h.Height),
                        pivot,
                        ppu,
                        0u,
                        SpriteMeshType.FullRect);
                }

                texBytes += (long)s.Width * h.Height * 4;
                s.Pixels = Array.Empty<byte>(); // 尽早放开 CPU 侧副本
            }

        }
        catch (Exception ex) {
            note = "建贴图失败：" + ex.Message;
            return null;
        }

        double perfUploadMs = BcsPerf.MsSince(perfUpload);

        BcsPerf.ReportLoad(
            png,
            perfDecodeMs,
            perfUploadMs,
            "tiled",
            h.Width + "x" + h.Height + " " + frames + "帧→" + slices.Count + "片");

        note = Describe(h, frames, nominalW, slices.Count, ppu, fractional);

        if (Logged.Add(png)) {
            AcsMod.Log("oversize: 已在内存分片 '" + png + "' " + note);
        }

        return new TiledSprite.TiledSheet {
            Sprites = sprites,
            Tex = firstTex,
            Bytes = texBytes,
            Note = note,
        };
    }

    private static string Describe(Header h, int frames, int frameW, int sliceCount, int ppu,
                                  bool fractional = false)
    {
        string head = fractional
            ? $"{h.Width}x{h.Height} {frames}帧 帧距{h.Width / (double)frames:F4}（非整数→按累积起点对齐，窗口 {frameW}px）"
            : $"{h.Width}x{h.Height} {frames}帧/{frameW}px";
        return $"{head} → {sliceCount} 片，PPU={ppu}";
    }

    /// <summary>
    /// 文件最后写入时间（UTC，读不到给 <see cref="DateTime.MinValue"/>）。
    /// 缓存新鲜度判据的唯一来源：<see cref="TiledSprite"/> 写入时记一次、命中时比一次。
    /// </summary>
    internal static DateTime SafeStamp(string png)
    {
        try {
            return File.GetLastWriteTimeUtc(png);
        }
        catch (Exception) {
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// 分片用的 PPU —— 必须与 vanilla <c>LoadSprites</c> 取值一致，否则 4x 素材会渲染成 4 倍大的角色。
    /// <b>同时是预热缓存的唯一权威 PPU 来源</b>：预热写入缓存与可见路径查缓存都走它，
    /// 否则两边 PPU 算出来不一致（底图：预热走 ResolveDensityPpu 得 100、可见走 ReadPrefScaleTex 得 400）
    /// → 缓存永远命中不了 → 进视野第一帧仍在主线重解 → 卡。
    /// 顺序刻意与 BCS 既有约定对齐：
    ///   ① acs 片 → <c>ResolveDensityPpu</c>（文件名 @N &gt; 底图 pref scaleTex &gt; 100）；
    ///   ② 底图 → 作者写在 <c>.pref</c> 里的 scaleTex 文本（最直接，也最不易被语义变化影响）；
    ///   ③ 已加载的 <c>pref.ints[25]</c>（= vanilla 的 scaleTex）兜底；
    ///   ④ 100。
    /// </summary>
    internal static int ResolvePpu(SpriteData data)
    {
        try {
            if (!string.IsNullOrEmpty(data.path)
                && data.path.IndexOf("_acs_", StringComparison.Ordinal) >= 0) {
                int d = SkinListPatch.ResolveDensityPpu(data);
                if (d > 0) {
                    return d;
                }
            }

        }
        catch (Exception) {
        }

        try {
            int v = SkinListPatch.ReadPrefScaleTex(data.path + ".pref");
            if (v > 0) {
                return v;
            }

        }
        catch (Exception) {
        }

        try {
            SourcePref? pref = data.pref;
            if (pref?.ints is { Length: > 25 } arr && arr[25] > 0) {
                return arr[25];
            }
        }
        catch (Exception) {
        }

        return 100;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 首帧图标：换肤列表的图标/预览
    //
    // 列表一个条目 = 一张 sheet。若走 SpriteData.GetSprite()，会把**整条**
    // sheet（40960×512 那种）解码、建贴图、传 GPU 全做一遍，滚过去就再也收不回来
    // —— 109 个条目合计 350MB 显存，而列表是滚动驱动的，逐行累积就是卡顿来源。
    //
    // 这里只取**最左侧一帧**（strip 的第一帧就是左边的竖条），并且：
    //   · 用流式 inflate，工作内存 ≈ 前缀行宽，而不是整份过滤流；
    //   · 反 filter 只算首帧那几列，不碰右侧 99% 的像素；
    //   · 输出首帧**原尺寸**像素，不缩放、不裁剪（缩到 128/256 槽位由 UI 的
    //     FitImage 负责，那是原有行为）。
    // 全程不碰 data.sprites / data.tex —— 图标与真实皮肤数据完全解耦。
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 生成「首帧图标」。<paramref name="frames"/> 由调用方按素材元数据解析
    /// （`.ini` 的 frame / `_acs_` 文件名尾段），本方法**不读像素做任何判断**。
    /// </summary>
    /// <param name="png">PNG 全路径（带不带 .png 都行）。</param>
    /// <param name="note">说明：成功=尺寸链路；失败=原因。</param>
    /// <returns>失败返回 null（调用方据此隐藏图标）。</returns>
    internal static Sprite? TryCreateThumb(string png, int frames, out string note)
    {
        note = string.Empty;
        if (string.IsNullOrEmpty(png)) {
            note = "路径为空";
            return null;
        }

        string path = png;
        if (!File.Exists(path)) {
            if (!File.Exists(path + ".png")) {
                note = "找不到 PNG";
                return null;
            }

            path += ".png";
        }

        if (!TryReadHeader(path, out Header h)) {
            note = "PNG 头不可读";
            return null;
        }

        if (h.BitDepth != 8) {
            note = $"只支持 8 位色深（实际 {h.BitDepth}）";
            return null;
        }

        if (h.Interlace != 0) {
            note = "不支持隔行（Adam7）";
            return null;
        }

        if (h.ColorType != 6 && h.ColorType != 2 && h.ColorType != 3) {
            note = $"只支持 RGBA/RGB/调色板（实际 colorType={h.ColorType}）";
            return null;
        }

        if (frames < 1) {
            frames = 1;
        }

        int nominalW = (int)Math.Ceiling((double)h.Width / frames);
        if (nominalW < 1) {
            nominalW = 1;
        }

        if (nominalW > h.Width) {
            nominalW = h.Width;
        }

        // 没切开的长条（缺 .ini frame 却按 1 帧算）不出图标：
        // 把整条 4096px 压进 128px 只会是一条糊色带，不如空着让缺 ini 暴露出来。
        // 用整数比较（nominalW*2 > h*3 ⟺ nominalW > h*1.5），避免浮点口径差。
        if (frames <= 1 && (long)nominalW * 2 > (long)h.Height * 3) {
            note = $"未切开横条 {h.Width}x{h.Height}（缺 frame，按 1 帧算）";
            return null;
        }

        if (!DecodeFirstFrame(path, h, nominalW, out byte[] rgba, out note)) {
            return null;
        }

        try {
            var tex = new Texture2D(nominalW, h.Height, TextureFormat.RGBA32, false) {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "bcs_thumb",
            };
            tex.LoadRawTextureData(rgba);
            tex.Apply(false, true);

            note = $"{h.Width}x{h.Height} {frames}帧 → 首帧 {nominalW}x{h.Height}";
            return Sprite.Create(
                tex,
                new Rect(0f, 0f, nominalW, h.Height),
                new Vector2(0.5f, 0.5f),
                100f,
                0u,
                SpriteMeshType.FullRect);
        }
        catch (Exception ex) {
            note = "建首帧图标失败：" + ex.Message;
            return null;
        }
    }

    /// <summary>
    /// 解出首帧（最左侧 <paramref name="nominalW"/> 列）的**原尺寸**像素，
    /// 行序翻成 Unity 贴图方向（PNG 第 0 行 → 贴图顶行）。不做缩放/裁剪。
    /// 逐行流式处理：任一时刻只持有「当前行的前 nominalW 列」+ 输出缓冲本身。
    /// </summary>
    private static bool DecodeFirstFrame(string png, Header h, int nominalW, out byte[] rgba, out string err)
    {
        rgba = Array.Empty<byte>();
        err = string.Empty;
        int channels = Channels(h.ColorType);
        int rowBytes = h.Width * channels + 1;
        long rawLen = (long)h.Height * rowBytes;
        if (rawLen > MaxRawBytes) {
            err = $"过滤流 {rawLen / 1048576}MB 过大，拒绝解码";
            return false;
        }

        if (!TryCollectIdat(png, out byte[] idat, out err)) {
            return false;
        }

        int curLen = nominalW * channels;
        var cur = new byte[curLen];
        var prev = new byte[curLen];
        var line = new byte[nominalW * 4];
        var outPx = new byte[nominalW * h.Height * 4];
        var inf = new RawInflator(idat, 2, idat.Length - 2);

        try {
            inf.InflateRows(rawLen, rowBytes, curLen, (y, filter) =>
            {
                if (filter > 4) {
                    throw new InvalidDataException($"未知行过滤器 {filter}（第 {y} 行）");
                }

                byte[] src = inf.RowPrefix;
                for (int i = 0; i < curLen; i++) {
                    int rb = src[i];
                    int a = i >= channels ? cur[i - channels] : 0;
                    int b = prev[i];
                    int c = i >= channels ? prev[i - channels] : 0;

                    int v;
                    switch (filter) {
                        case 0:
                            v = rb;
                            break;
                        case 1:
                            v = rb + a;
                            break;
                        case 2:
                            v = rb + b;
                            break;
                        case 3:
                            v = rb + ((a + b) >> 1);
                            break;
                        default:
                            v = rb + Paeth(a, b, c);
                            break;
                    }

                    cur[i] = (byte)v;
                }

                if (channels == 4) {
                    Buffer.BlockCopy(cur, 0, line, 0, curLen);
                }
                else if (channels == 3) {
                    for (int px = 0; px < nominalW; px++) {
                        int s = px * 3;
                        int d = px * 4;
                        line[d] = cur[s];
                        line[d + 1] = cur[s + 1];
                        line[d + 2] = cur[s + 2];
                        line[d + 3] = 255;
                    }
                }
                else {
                    // colorType=3：1 字节索引 → PLTE(+tRNS) 查表得 RGBA
                    byte[] pal = h.Palette!;
                    for (int px = 0; px < nominalW; px++) {
                        int p = cur[px] * 4;
                        int d = px * 4;
                        line[d] = pal[p];
                        line[d + 1] = pal[p + 1];
                        line[d + 2] = pal[p + 2];
                        line[d + 3] = pal[p + 3];
                    }
                }

                // PNG 第 0 行（顶）→ 贴图顶行；Unity 贴图原点在左下。
                Buffer.BlockCopy(line, 0, outPx, (h.Height - 1 - y) * nominalW * 4, line.Length);

                byte[] swap = prev;
                prev = cur;
                cur = swap;
            });
        }
        catch (Exception ex) {
            err = "首帧解码失败：" + ex.Message;
            return false;
        }

        rgba = outPx;
        return true;
    }

    /// <summary>
    /// 读签名 + IHDR（宽/高/位深/色彩类型/隔行）；colorType=3 时继续扫 PLTE/tRNS 建 RGBA 查表。
    /// </summary>
    private static bool TryReadHeader(string path, out Header h)
    {
        h = default;
        try {
            using var fs = File.OpenRead(path);
            var buf = new byte[29];
            if (!ReadExact(fs, buf, buf.Length)) {
                return false;
            }

            if (buf[0] != 0x89 || buf[1] != 0x50 || buf[2] != 0x4E || buf[3] != 0x47) {
                return false;
            }

            h.Width = (buf[16] << 24) | (buf[17] << 16) | (buf[18] << 8) | buf[19];
            h.Height = (buf[20] << 24) | (buf[21] << 16) | (buf[22] << 8) | buf[23];
            h.BitDepth = buf[24];
            h.ColorType = buf[25];
            h.Interlace = buf[28];
            if (h.Width <= 0 || h.Height <= 0) {
                return false;
            }

            // 调色板图：像素是索引，必须把 PLTE(+tRNS) 一起读出来才能变 RGBA。
            if (h.ColorType == 3) {
                return TryReadPalette(fs, ref h);
            }

            return true;
        }
        catch (Exception) {
            return false;
        }
    }

    /// <summary>
    /// 紧接 IHDR 之后逐个 chunk 找 PLTE / tRNS（PNG 规定二者必须排在 IDAT 之前），
    /// 展开成 256 项 RGBA 查表。读不动 / 没有 PLTE → false（调用方回退原版解码）。
    /// </summary>
    private static bool TryReadPalette(Stream fs, ref Header h)
    {
        var crc = new byte[4];
        if (!ReadExact(fs, crc, 4)) {
            return false;
        }

        byte[]? plte = null;
        int plteEntries = 0;
        byte[]? trns = null;
        var head = new byte[8];

        while (ReadExact(fs, head, 8)) {
            long len = ((long)head[0] << 24) | ((long)head[1] << 16) | ((long)head[2] << 8) | head[3];
            if (len < 0 || len > 16L * 1024 * 1024) {
                return false;
            }

            string type = string.Concat((char)head[4], (char)head[5], (char)head[6], (char)head[7]);
            if (type == "PLTE") {
                var data = new byte[(int)len];
                if (!ReadExact(fs, data, data.Length)) {
                    return false;
                }

                plte = data;
                plteEntries = data.Length / 3;
                if (plteEntries > 256) {
                    plteEntries = 256;
                }
            }
            else if (type == "tRNS") {
                var data = new byte[(int)len];
                if (!ReadExact(fs, data, data.Length)) {
                    return false;
                }

                trns = data;
            }
            else if (type == "IDAT" || type == "IEND") {
                break;
            }
            else if (!SkipExact(fs, len)) {
                return false;
            }

            if (!ReadExact(fs, crc, 4)) {
                return false;
            }
        }

        if (plte is null || plteEntries <= 0) {
            return false;
        }

        var table = new byte[256 * 4];
        for (int i = 0; i < plteEntries; i++) {
            table[i * 4] = plte[i * 3];
            table[i * 4 + 1] = plte[i * 3 + 1];
            table[i * 4 + 2] = plte[i * 3 + 2];
            table[i * 4 + 3] = trns is not null && i < trns.Length ? trns[i] : (byte)255;
        }

        h.Palette = table;
        return true;
    }

    /// <summary>跳过 <paramref name="count"/> 字节（不一定可 Seek，逐块读丢弃）。</summary>
    private static bool SkipExact(Stream fs, long count)
    {
        var scratch = new byte[4096];
        while (count > 0) {
            int want = count < scratch.Length ? (int)count : scratch.Length;
            if (!ReadExact(fs, scratch, want)) {
                return false;
            }

            count -= want;
        }

        return true;
    }

    /// <summary>收集 IDAT（流式读文件，不整文件进内存）→ inflate → 逐行反 filter 并按片拷贝。</summary>
    /// <summary>
    /// 用 BCL 的 <see cref="DeflateStream"/> 解压 IDAT（跳过 2 字节 zlib 头）。
    /// 失败一律返回 false 由调用方回退到自写 <c>RawInflator</c> —— <b>不抛、不告警</b>，
    /// 因为它只是快路径。
    /// </summary>
    private static bool TryInflateManaged(byte[] idat, byte[] raw)
    {
        try {
            using var ms = new MemoryStream(idat, 2, idat.Length - 2, false);
            using var ds = new DeflateStream(ms, CompressionMode.Decompress);

            int total = 0;
            while (total < raw.Length) {
                int n = ds.Read(raw, total, raw.Length - total);
                if (n <= 0) {
                    break;
                }

                total += n;
            }

            return total == raw.Length;
        }
        catch (Exception) {
            // 环境缺 System.IO.Compression / 流损坏 → 交给自写 inflate 兜底。
            return false;
        }
    }

    private static bool DecodeInto(string png, Header h, List<Slice> slices, out string err)
    {
        err = string.Empty;
        bool probe = PrewarmProbe.Active;
        long tIdat = 0;
        long tInflate = 0;
        long tRows = 0;
        int channels = Channels(h.ColorType);
        int stride = h.Width * channels;
        int rowBytes = stride + 1;
        long rawLen = (long)h.Height * rowBytes;
        if (rawLen > MaxRawBytes) {
            err = $"过滤流 {rawLen / 1048576}MB 过大，拒绝分片";
            return false;
        }

        long probeT = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!TryCollectIdat(png, out byte[] idat, out err)) {
            return false;
        }

        tIdat = System.Diagnostics.Stopwatch.GetTimestamp() - probeT;

        byte[] raw = new byte[(int)rawLen];

        // 首选 BCL 的 DeflateStream（跳过 2 字节 zlib 头后就是裸 deflate）。
        // 实测（.tools/tiler_test bench）自写 inflate 比它慢 2~3 倍：
        //   yadianna.png 72MB 原始 → 自写 1603ms / DeflateStream 712ms，逐字节一致。
        // 自写那份**保留不删**：① 流式首帧图标路径（InflateRows）还要它；
        // ② 万一某个 Unity/Mono 环境没带 System.IO.Compression，这里自动回退。
        probeT = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!TryInflateManaged(idat, raw)) {
            if (probe) {
                PrewarmProbe.InflateFallback++;
            }

            try {
                var inf = new RawInflator(idat, 2, idat.Length - 2);
                int produced = inf.Inflate(raw);
                if (produced != raw.Length) {
                    err = $"inflate 输出 {produced} 字节，应为 {raw.Length}";
                    return false;
                }
            }

            catch (Exception ex) {
                err = "inflate 失败：" + ex.Message;
                return false;
            }

        }
        else {
            if (probe) {
                PrewarmProbe.InflateManaged++;
            }
        }

        tInflate = System.Diagnostics.Stopwatch.GetTimestamp() - probeT;

        probeT = System.Diagnostics.Stopwatch.GetTimestamp();
        byte[] cur = new byte[stride];
        byte[] prev = new byte[stride];
        byte[] rgba = new byte[h.Width * 4];

        try {
            for (int y = 0; y < h.Height; y++) {
                int off = y * rowBytes;
                int filter = raw[off];
                int src = off + 1;

                // 把 switch 提到行级别：原来是「每像素一次 switch + 三次下标判断」，
                // 72MB 的图上那是几千万次分支。按过滤器类型各写一条直线循环，
                // 前 channels 字节单独处理（它们的 a/c 恒为 0），其余走无分支热循环。
                switch (filter) {
                    case 0:
                        Buffer.BlockCopy(raw, src, cur, 0, stride);
                        break;

                    case 1: {
                        for (int i = 0; i < channels && i < stride; i++) {
                            cur[i] = raw[src + i];
                        }

                        for (int i = channels; i < stride; i++) {
                            cur[i] = (byte)(raw[src + i] + cur[i - channels]);
                        }

                        break;
                    }

                    case 2: {
                        for (int i = 0; i < stride; i++) {
                            cur[i] = (byte)(raw[src + i] + prev[i]);
                        }

                        break;
                    }

                    case 3: {
                        for (int i = 0; i < channels && i < stride; i++) {
                            cur[i] = (byte)(raw[src + i] + (prev[i] >> 1));
                        }

                        for (int i = channels; i < stride; i++) {
                            int a = cur[i - channels];
                            int b = prev[i];
                            cur[i] = (byte)(raw[src + i] + ((a + b) >> 1));
                        }

                        break;
                    }

                    case 4: {
                        for (int i = 0; i < channels && i < stride; i++) {
                            // a = c = 0 时 Paeth 退化成 b
                            cur[i] = (byte)(raw[src + i] + prev[i]);
                        }

                        for (int i = channels; i < stride; i++) {
                            int a = cur[i - channels];
                            int b = prev[i];
                            int c = prev[i - channels];
                            cur[i] = (byte)(raw[src + i] + Paeth(a, b, c));
                        }

                        break;
                    }

                    default:
                        err = $"未知行过滤器 {filter}（第 {y} 行）";
                        return false;
                }

                // channels==4 时 cur 的布局就是 RGBA，直接当源用，省一次整行拷贝。
                byte[] row;
                if (channels == 4) {
                    row = cur;
                }
                else if (channels == 3) {
                    for (int px = 0; px < h.Width; px++) {
                        int s = px * 3;
                        int d = px * 4;
                        rgba[d] = cur[s];
                        rgba[d + 1] = cur[s + 1];
                        rgba[d + 2] = cur[s + 2];
                        rgba[d + 3] = 255;
                    }

                    row = rgba;
                }
                else {
                    // colorType=3：1 字节索引 → PLTE(+tRNS) 查表得 RGBA
                    byte[] pal = h.Palette!;
                    for (int px = 0; px < h.Width; px++) {
                        int p = cur[px] * 4;
                        int d = px * 4;
                        rgba[d] = pal[p];
                        rgba[d + 1] = pal[p + 1];
                        rgba[d + 2] = pal[p + 2];
                        rgba[d + 3] = pal[p + 3];
                    }

                    row = rgba;
                }

                // PNG 第 0 行是图像顶部；Unity 贴图第 0 行在底部 → 上下翻转
                int ty = h.Height - 1 - y;

                for (int si = 0; si < slices.Count; si++) {
                    Slice s = slices[si];
                    // 片宽是按最大帧距预留的，末片可能伸到文件右界之外 → 只拷有效部分，
                    // 余下留 0（透明）。这样 Sprite.Create 的 rect 始终落在贴图内。
                    int avail = h.Width - s.SourceX;
                    int copy = s.Width < avail ? s.Width : avail;
                    if (copy > 0) {
                        Buffer.BlockCopy(row, s.SourceX * 4, s.Pixels, ty * s.Width * 4, copy * 4);
                    }
                }

                byte[] swap = prev;
                prev = cur;
                cur = swap;
            }

        }
        catch (Exception ex) {
            err = "反 filter 失败：" + ex.Message;
            return false;
        }

        tRows = System.Diagnostics.Stopwatch.GetTimestamp() - probeT;

        if (probe) {
            PrewarmProbe.TIdatRead += tIdat;
            PrewarmProbe.TInflate += tInflate;
            PrewarmProbe.TRows += tRows;
        }

        return true;
    }

    /// <summary>只把 IDAT 数据段拼进内存（其余块靠 Seek 跳过 + 读 CRC），避免整文件驻留。</summary>
    private static bool TryCollectIdat(string png, out byte[] idat, out string err)
    {
        idat = Array.Empty<byte>();
        err = string.Empty;
        try {
            using var fs = File.OpenRead(png);
            var sig = new byte[8];
            if (!ReadExact(fs, sig, 8)
                || sig[0] != 0x89 || sig[1] != 0x50 || sig[2] != 0x4E || sig[3] != 0x47) {
                err = "不是 PNG";
                return false;
            }

            using var ms = new MemoryStream(1 << 20);
            var head = new byte[8];
            var buf = new byte[1 << 16];
            while (ReadExact(fs, head, 8)) {
                uint len = ((uint)head[0] << 24) | ((uint)head[1] << 16)
                    | ((uint)head[2] << 8) | head[3];
                string type = Encoding.ASCII.GetString(head, 4, 4);
                long skip = (long)len;
                if (type == "IDAT") {
                    long left = skip;
                    while (left > 0) {
                        int n = fs.Read(buf, 0, (int)Math.Min(buf.Length, left));
                        if (n <= 0) {
                            err = "IDAT 数据截断";
                            return false;
                        }

                        ms.Write(buf, 0, n);
                        left -= n;
                    }
                }
                else {
                    fs.Seek(skip, SeekOrigin.Current);
                }

                fs.Seek(4, SeekOrigin.Current); // CRC

                if (type == "IEND") {
                    break;
                }
            }

            if (ms.Length < 6) {
                err = "没有 IDAT（无像素数据）";
                return false;
            }

            // 末尾补 4 个 0：Peek 最多预读 15 位，留出字节边界余量，避免在流尾误判「输入提前结束」
            idat = new byte[ms.Length + 4];
            ms.Position = 0;
            ReadExact(ms, idat, (int)ms.Length);
            return true;
        }
        catch (Exception ex) {
            err = "解析 PNG 块失败：" + ex.Message;
            return false;
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) {
            return a;
        }

        return pb <= pc ? b : c;
    }

    private static bool ReadExact(Stream s, byte[] buf, int count)
    {
        int read = 0;
        while (read < count) {
            int n = s.Read(buf, read, count - read);
            if (n <= 0) {
                return false;
            }

            read += n;
        }

        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 极简 DEFLATE (RFC 1951) 解压：输入为裸 deflate 流（zlib 头由调用方跳过）。
    // 输出长度已知（= 过滤流长度），所以直接写进定长缓冲，无需滑动窗口。
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class RawInflator
    {
        private static readonly int[] LenBase = {
            3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31,
            35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258,
        };

        private static readonly int[] LenExtra = {
            0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2,
            3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0,
        };

        private static readonly int[] DistBase = {
            1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193,
            257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577,
        };

        private static readonly int[] DistExtra = {
            0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6,
            7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13,
        };

        private static readonly int[] ClcOrder = {
            16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15,
        };

        private static short[]? _fixedLit;
        private static short[]? _fixedDist;

        private readonly byte[] _src;
        private readonly int _end;
        private int _p;
        private int _bitBuf;
        private int _bitCnt;

        /// <summary>DEFLATE 最大回引距离 = 32768，流式模式用它作环形窗口。</summary>
        private const int RingSize = 32768;

        private const int RingMask = RingSize - 1;

        private byte[] _dst = Array.Empty<byte>();
        private int _q;

        // —— 流式模式（首帧图标路径用）：不落整份输出 ——
        private bool _streaming;
        private byte[] _ring = Array.Empty<byte>();
        private byte[] _prefix = Array.Empty<byte>();
        private long _limit;
        private int _rowBytes;
        private int _prefixBytes;
        private int _rowPos;
        private int _rowIdx;
        private byte _rowFilter;
        private Action<int, byte>? _onRow;

        public RawInflator(byte[] src, int start, int length)
        {
            _src = src;
            _p = start;
            _end = Math.Min(src.Length, start + length);
        }

        /// <summary>流式模式下当前行的前缀缓冲（长度 = prefixBytes），仅在行回调期间有效。</summary>
        public byte[] RowPrefix => _prefix;

        /// <summary>解压到恰好填满 <paramref name="dst"/>；返回实际写入字节数。</summary>
        public int Inflate(byte[] dst)
        {
            _dst = dst;
            _streaming = false;
            _q = 0;
            RunBlocks();
            return _q;
        }

        /// <summary>
        /// 流式解压：输出**不落整份缓冲**，只在 32KB 环里保留 DEFLATE 回引所需的窗口，
        /// 并把每行的**前 <paramref name="prefixBytes"/> 个行数据字节**通过
        /// <paramref name="onRow"/>(行号, filter 字节) 回调出去。
        ///
        /// 用途：一条 40960×512 的长条，只要看最左侧那一帧（首帧图标），
        /// 用这个路径的工作内存 ≈ 64KB，而不是「80MB 过滤流 + 80MB 像素缓冲」。
        /// 注意 inflate 本身仍要跑完整条流——DEFLATE 是串行的，跳不过去；
        /// 省下的是**存储**、反 filter 的行宽（只处理前 N 列）以及大贴图的创建与上传。
        /// </summary>
        /// <returns>产出的行数。</returns>
        public int InflateRows(long limit, int rowBytes, int prefixBytes, Action<int, byte> onRow)
        {
            int cap = rowBytes - 1;
            if (prefixBytes > cap) {
                prefixBytes = cap;
            }

            if (prefixBytes < 0) {
                prefixBytes = 0;
            }

            _streaming = true;
            _dst = Array.Empty<byte>();
            _ring = new byte[RingSize];
            _prefix = new byte[prefixBytes];
            _limit = limit;
            _rowBytes = rowBytes;
            _prefixBytes = prefixBytes;
            _onRow = onRow;
            _q = 0;
            _rowPos = 0;
            _rowIdx = 0;
            _rowFilter = 0;
            RunBlocks();
            return _rowIdx;
        }

        private void RunBlocks()
        {
            bool last = false;
            while (!last) {
                last = Bits(1) == 1;
                int type = Bits(2);
                switch (type) {
                    case 0:
                        Stored();
                        break;
                    case 1:
                        Huffman(FixedLit(), 9, FixedDist(), 5);
                        break;
                    case 2:
                        Dynamic();
                        break;
                    default:
                        throw new InvalidDataException("非法 block type " + type);
                }
            }

        }

        private void Emit(byte b)
        {
            if (!_streaming) {
                if (_q >= _dst.Length) {
                    throw new InvalidDataException("输出超出预期长度");
                }

                _dst[_q++] = b;
                return;
            }

            if (_q >= _limit) {
                throw new InvalidDataException("输出超出预期长度");
            }

            _ring[_q & RingMask] = b;
            _q++;

            if (_rowPos == 0) {
                _rowFilter = b;
            }

            else if (_rowPos <= _prefixBytes) {
                _prefix[_rowPos - 1] = b;
            }

            _rowPos++;
            if (_rowPos == _rowBytes) {
                _rowPos = 0;
                _onRow?.Invoke(_rowIdx, _rowFilter);
                _rowIdx++;
            }

        }

        private int Bits(int need)
        {
            while (_bitCnt < need) {
                if (_p >= _end) {
                    throw new InvalidDataException("deflate 输入提前结束");
                }

                _bitBuf |= _src[_p++] << _bitCnt;
                _bitCnt += 8;
            }

            int v = _bitBuf & ((1 << need) - 1);
            _bitBuf >>= need;
            _bitCnt -= need;
            return v;
        }

        private int Peek(int need)
        {
            while (_bitCnt < need) {
                if (_p >= _end) {
                    throw new InvalidDataException("deflate 输入提前结束");
                }

                _bitBuf |= _src[_p++] << _bitCnt;
                _bitCnt += 8;
            }

            return _bitBuf & ((1 << need) - 1);
        }

        private void Drop(int n)
        {
            _bitBuf >>= n;
            _bitCnt -= n;
        }

        private void Stored()
        {
            _bitBuf = 0;
            _bitCnt = 0;
            if (_p + 4 > _end) {
                throw new InvalidDataException("stored 块头截断");
            }

            int len = _src[_p] | (_src[_p + 1] << 8);
            int nlen = _src[_p + 2] | (_src[_p + 3] << 8);
            _p += 4;
            if ((len ^ 0xFFFF) != nlen) {
                throw new InvalidDataException("stored 块 LEN/NLEN 不匹配");
            }

            if (_p + len > _end) {
                throw new InvalidDataException("stored 块数据截断");
            }

            for (int i = 0; i < len; i++) {
                Emit(_src[_p + i]);
            }

            _p += len;
        }

        private static short[] FixedLit()
        {
            if (_fixedLit is not null) {
                return _fixedLit;
            }

            var lens = new byte[288];
            for (int i = 0; i < 144; i++) {
                lens[i] = 8;
            }

            for (int i = 144; i < 256; i++) {
                lens[i] = 9;
            }

            for (int i = 256; i < 280; i++) {
                lens[i] = 7;
            }

            for (int i = 280; i < 288; i++) {
                lens[i] = 8;
            }

            _fixedLit = BuildTable(lens, 288, out _);
            return _fixedLit;
        }

        private static short[] FixedDist()
        {
            if (_fixedDist is not null) {
                return _fixedDist;
            }

            var lens = new byte[30];
            for (int i = 0; i < 30; i++) {
                lens[i] = 5;
            }

            _fixedDist = BuildTable(lens, 30, out _);
            return _fixedDist;
        }

        private void Dynamic()
        {
            int hlit = Bits(5) + 257;
            int hdist = Bits(5) + 1;
            int hclen = Bits(4) + 4;

            var clc = new byte[19];
            for (int i = 0; i < hclen; i++) {
                clc[ClcOrder[i]] = (byte)Bits(3);
            }

            short[] clTable = BuildTable(clc, 19, out int clBits);
            var lens = new byte[hlit + hdist];
            int n = 0;
            while (n < lens.Length) {
                int sym = Decode(clTable, clBits);
                if (sym < 16) {
                    lens[n++] = (byte)sym;
                }
                else if (sym == 16) {
                    if (n == 0) {
                        throw new InvalidDataException("码长重复无前值");
                    }

                    byte prev = lens[n - 1];
                    int rep = 3 + Bits(2);
                    while (rep-- > 0 && n < lens.Length) {
                        lens[n++] = prev;
                    }
                }
                else if (sym == 17) {
                    int rep = 3 + Bits(3);
                    while (rep-- > 0 && n < lens.Length) {
                        lens[n++] = 0;
                    }
                }
                else {
                    int rep = 11 + Bits(7);
                    while (rep-- > 0 && n < lens.Length) {
                        lens[n++] = 0;
                    }
                }
            }

            var litLens = new byte[hlit];
            var distLens = new byte[hdist];
            Array.Copy(lens, 0, litLens, 0, hlit);
            Array.Copy(lens, hlit, distLens, 0, hdist);

            Huffman(
                BuildTable(litLens, hlit, out int litBits), litBits,
                BuildTable(distLens, hdist, out int distBits), distBits);
        }

        private void Huffman(short[] lit, int litBits, short[] dist, int distBits)
        {
            while (true) {
                int sym = Decode(lit, litBits);
                if (sym < 256) {
                    Emit((byte)sym);
                    continue;
                }

                if (sym == 256) {
                    return;
                }

                int li = sym - 257;
                if (li >= LenBase.Length) {
                    throw new InvalidDataException("非法长度码 " + sym);
                }

                int length = LenBase[li] + Bits(LenExtra[li]);
                int dsym = Decode(dist, distBits);
                if (dsym >= DistBase.Length) {
                    throw new InvalidDataException("非法距离码 " + dsym);
                }

                int d = DistBase[dsym] + Bits(DistExtra[dsym]);
                if (d > _q) {
                    throw new InvalidDataException("距离超出已输出数据");
                }

                int from = _q - d;
                for (int i = 0; i < length; i++) {
                    Emit(_streaming ? _ring[(from + i) & RingMask] : _dst[from + i]);
                }
            }

        }

        private int Decode(short[] table, int maxBits)
        {
            int idx = Peek(maxBits);
            short e = table[idx];
            if (e == 0) {
                throw new InvalidDataException("非法 Huffman 码");
            }

            Drop(e & 0xF);
            return e >> 4;
        }

        /// <summary>
        /// 反射填表：索引 = 「按读取顺序、首比特当最低位」的 maxBits 位值，
        /// 于是同长度码共享低 len 位 → 用步长 2^len 覆盖。
        /// <paramref name="maxBits"/> 返回实际最大码长：查表只预读这么多位，
        /// 否则在流尾会越过输入末尾误报截断。
        /// </summary>
        private static short[] BuildTable(byte[] lengths, int count, out int maxBits)
        {
            maxBits = 0;
            for (int i = 0; i < count; i++) {
                if (lengths[i] > maxBits) {
                    maxBits = lengths[i];
                }
            }

            if (maxBits == 0) {
                return new short[1];
            }

            var blCount = new int[maxBits + 1];
            for (int i = 0; i < count; i++) {
                blCount[lengths[i]]++;
            }

            blCount[0] = 0;
            var nextCode = new int[maxBits + 1];
            int code = 0;
            for (int b = 1; b <= maxBits; b++) {
                code = (code + blCount[b - 1]) << 1;
                nextCode[b] = code;
            }

            int size = 1 << maxBits;
            var table = new short[size];
            for (int sym = 0; sym < count; sym++) {
                int len = lengths[sym];
                if (len == 0) {
                    continue;
                }

                int rev = ReverseBits(nextCode[len]++, len);
                short entry = (short)((sym << 4) | len);
                for (int j = rev; j < size; j += 1 << len) {
                    table[j] = entry;
                }
            }

            return table;
        }

        private static int ReverseBits(int v, int len)
        {
            int r = 0;
            for (int i = 0; i < len; i++) {
                r = (r << 1) | (v & 1);
                v >>= 1;
            }

            return r;
        }
    }
}
