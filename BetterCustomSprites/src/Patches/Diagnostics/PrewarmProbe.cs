using System;
using System.Collections.Generic;
using System.Text;
using ACS.Tiling;

namespace ACS.Patches;

/// <summary>
/// 预热性能探针：把一次预热的耗时按「谁在干」拆开记录，队列排空时打一条汇总。
/// 纯计数，不参与任何逻辑判断；只为回答「卡在哪一段」。
/// </summary>
internal static class PrewarmProbe
{
    internal sealed class Sample
    {
        internal string Name = string.Empty;
        internal double Ms;
    }

    private const int MaxSamples = 4000;
    private const int MaxFailures = 40;

    /// <summary>
    /// 探针总开关，跟随 <see cref="AcsMod.DiagnosticLog"/>（<c>ACS.Log on</c>）。
    /// 关闭时所有入口直接返回：不记账、不打报告、<see cref="PngTiler"/> 也不会为计时开表。
    /// </summary>
    internal static volatile bool Enabled;

    private static volatile bool _active;

    /// <summary>预热批次是否在进行 —— 解码线程据此决定要不要记账（避免把日常按需解码算进来）。</summary>
    internal static bool Active
    {
        get => Enabled && _active;
        set => _active = Enabled && value;
    }

    /// <summary>主线程各阶段耗时（Stopwatch tick）。</summary>
    internal static long TScan;
    internal static long TDrain;
    internal static long TDispatch;
    internal static long TAdopt;
    internal static long TSleep;
    internal static int Loops;

    /// <summary>主线程建贴图细分（Stopwatch tick）。</summary>
    internal static long TNewTex;
    internal static long TApply;
    internal static long TSpriteCreate;
    internal static int Textures;
    internal static int Slices;
    internal static long Pixels;

    /// <summary>后台解码统计。</summary>
    internal static long DecodeCpuTicks;
    internal static long DecodePixels;
    internal static int Decoded;
    internal static int DecodeFailed;
    private static long _decodeFirstStart = -1;
    private static long _decodeLastEnd;

    /// <summary>解码内部细分（Stopwatch tick）：读 IDAT / inflate / 反 filter+拷贝。</summary>
    internal static long TIdatRead;
    internal static long TInflate;
    internal static long TRows;
    internal static int InflateManaged;
    internal static int InflateFallback;

    /// <summary>本轮预热的活跃窗口（首/末次 Tick 或 WarmBlocking 的时刻）。</summary>
    private static long _firstActivity = -1;
    private static long _lastActivity;

    /// <summary>阻塞预热净耗时与次数 —— 就是加在读条上的那一段。</summary>
    internal static long BlockTicks;
    internal static int BlockCalls;

    private static readonly List<Sample> DecodeSamples = new();
    private static readonly List<Sample> TexSamples = new();
    private static readonly List<string> Failures = new();

    /// <summary>
    /// 解码线程与主线程都会写这些计数/列表（解码侧的记账现在跑在
    /// <see cref="ACS.Tiling.TiledSprite.Prewarm"/> 的 6 条线程池线程上），
    /// 所以记账与出报告都串行化。只在探针开启时才有实际内容，开销可忽略。
    /// </summary>
    private static readonly object Gate = new();

    internal static double Ms(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    internal static void MarkActivity(long beginTicks, long endTicks, bool blocking)
    {
        if (!Enabled) {
            return;
        }

        if (_firstActivity < 0) {
            _firstActivity = beginTicks;
        }

        _lastActivity = endTicks;

        if (blocking) {
            BlockTicks += endTicks - beginTicks;
            BlockCalls++;
        }
    }

    internal static void NoteDecode(string png, long startTicks, long endTicks, PngTiler.DecodedImage? img)
    {
        if (!Enabled) {
            return;
        }

        lock (Gate) {
            if (img is null) {
                DecodeFailed++;
                return;
            }

            long span = endTicks - startTicks;
            DecodeCpuTicks += span;
            Decoded++;

            if (_decodeFirstStart < 0 || startTicks < _decodeFirstStart) {
                _decodeFirstStart = startTicks;
            }

            if (endTicks > _decodeLastEnd) {
                _decodeLastEnd = endTicks;
            }

            long px = 0;
            foreach (PngTiler.Slice s in img.Slices) {
                px += (long)s.Width * img.Height;
            }

            DecodePixels += px;
            Add(DecodeSamples, System.IO.Path.GetFileName(png), Ms(span));
        }
    }

    internal static void NoteDecodeFailure(string png, string reason)
    {
        if (!Enabled) {
            return;
        }

        lock (Gate) {
            if (Failures.Count >= MaxFailures) {
                return;
            }

            Failures.Add(System.IO.Path.GetFileName(png) + ": "
                         + (string.IsNullOrEmpty(reason) ? "(无错误信息)" : reason));
        }
    }

    internal static void NoteTex(string png, double ms)
        => Add(TexSamples, System.IO.Path.GetFileName(png), ms);

    private static void Add(List<Sample> list, string name, double ms)
    {
        if (!Enabled) {
            return;
        }

        if (list.Count < MaxSamples) {
            list.Add(new Sample { Name = name, Ms = ms });
        }
    }

    internal static void Log(int images, int maxParallel)
    {
        if (!Enabled || _firstActivity < 0) {
            return;
        }

        lock (Gate) {
            LogLocked(images, maxParallel);
        }
    }

    private static void LogLocked(int images, int maxParallel)
    {
        var sb = new StringBuilder(1600);
        sb.Append("[BCS] 预热报告：图 ").Append(images)
          .Append(" 张 → 贴图 ").Append(Textures)
          .Append(" 张（帧 ").Append(Slices)
          .Append("），上传像素 ").Append(Pixels / 1000000).Append(" MPix");

        sb.Append('\n').Append("  墙钟 ").Append(Fmt(Ms(_lastActivity - _firstActivity))).Append("ms");
        if (BlockCalls > 0) {
            sb.Append("（其中同步阻塞 Scene.Init ")
              .Append(Fmt(Ms(BlockTicks))).Append("ms x").Append(BlockCalls).Append("）");
        }

        sb.Append('\n').Append("  主线程：扫描 ").Append(Fmt(Ms(TScan)))
          .Append(" / 入库 ").Append(Fmt(Ms(TDrain)))
          .Append(" / 派发 ").Append(Fmt(Ms(TDispatch)))
          .Append(" / 建贴图 ").Append(Fmt(Ms(TAdopt)))
          .Append(" / 让出 ").Append(Fmt(Ms(TSleep)))
          .Append("ms（").Append(Loops).Append(" 次循环）");

        sb.Append('\n').Append("  建贴图细分：new+LoadRaw ").Append(Fmt(Ms(TNewTex)))
          .Append(" / Apply ").Append(Fmt(Ms(TApply)))
          .Append(" / Sprite.Create ").Append(Fmt(Ms(TSpriteCreate)))
          .Append("（合计 ").Append(Fmt(Ms(TNewTex + TApply + TSpriteCreate))).Append("）");

        sb.Append('\n').Append("  后台解码：").Append(Fmt(Ms(DecodeCpuTicks))).Append("ms CPU / ")
          .Append(Fmt(_decodeFirstStart < 0 ? 0 : Ms(_decodeLastEnd - _decodeFirstStart)))
          .Append("ms 墙钟（").Append(maxParallel).Append(" 线程，").Append(Decoded).Append(" 张");

        if (DecodeFailed > 0) {
            sb.Append("，失败 ").Append(DecodeFailed);
        }

        sb.Append("，解码像素 ").Append(DecodePixels / 1000000).Append(" MPix）");

        sb.Append('\n').Append("  解码细分：读盘 ").Append(Fmt(Ms(TIdatRead)))
          .Append(" / inflate ").Append(Fmt(Ms(TInflate)))
          .Append(" / 反filter+拷贝 ").Append(Fmt(Ms(TRows)))
          .Append("ms（DeflateStream ").Append(InflateManaged)
          .Append(" / 自写 ").Append(InflateFallback).Append("）");

        AppendTop(sb, "解码最慢", DecodeSamples);
        AppendTop(sb, "建贴图最慢", TexSamples);

        if (Failures.Count > 0) {
            sb.Append('\n').Append("  解码失败：").Append(string.Join("，", Failures.ToArray()));
        }

        AcsMod.Log(sb.ToString());
    }

    private static void AppendTop(StringBuilder sb, string label, List<Sample> samples)
    {
        if (samples.Count == 0) {
            return;
        }

        samples.Sort((a, b) => b.Ms.CompareTo(a.Ms));
        int n = Math.Min(5, samples.Count);
        sb.Append('\n').Append("  ").Append(label).Append("(前 ").Append(n).Append(")：");
        for (int i = 0; i < n; i++) {
            if (i > 0) {
                sb.Append(',');
            }

            sb.Append(samples[i].Name).Append(' ').Append(Fmt(samples[i].Ms)).Append("ms");
        }
    }

    private static string Fmt(double ms) => ms.ToString("0.0");

    internal static void Reset()
    {
        lock (Gate) {
            TScan = 0;
            TDrain = 0;
            TDispatch = 0;
            TAdopt = 0;
            TSleep = 0;
            Loops = 0;
            TNewTex = 0;
            TApply = 0;
            TSpriteCreate = 0;
            Textures = 0;
            Slices = 0;
            Pixels = 0;
            DecodeCpuTicks = 0;
            DecodePixels = 0;
            Decoded = 0;
            DecodeFailed = 0;
            _decodeFirstStart = -1;
            _decodeLastEnd = 0;
            TIdatRead = 0;
            TInflate = 0;
            TRows = 0;
            InflateManaged = 0;
            InflateFallback = 0;
            _firstActivity = -1;
            _lastActivity = 0;
            BlockTicks = 0;
            BlockCalls = 0;
            DecodeSamples.Clear();
            TexSamples.Clear();
            Failures.Clear();
        }
    }
}