using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ACS.Patches;

/// <summary>
/// 性能探针：**只读观测，零行为改动**（不打 ketones / 不改 any 状态 / 不 Destroy 任何东西）。
///
/// 背景：高清动态皮肤「卡一下」有三种候选根因，代码层面<b>看不出来</b>，必须实机实测：
///   ① 首次加载：同步 <c>inflate + 反 filter + GPU 上传</c> 全在主线程，一次 30~72MB raw 的 strip 要几百毫秒到秒级；
///   ② 反复重载：<see cref="SpriteCacheEvict"/> 释放后下次渲染到又从头解一遍（表现为「同一张图越走越卡」）；
///   ③ 每帧开销：不是加载慢，而是某个 per-frame 动作本身贵（会影响所有帧，不只是第 1 帧）。
///
/// 三种的修法完全不同（异步解码 / 预热或延长 grace / 改 per-frame 逻辑），
/// 所以先把事实量出来：本类记录
///   A. 每张 sheet 的<b>加载次数</b>、总耗时、最大耗时，并拆出 CPU（解码）与 GPU（上传）两段；
///   B. 每帧所有 CardActor patch 的合计耗时与最慢单 actor；
///   C. 每次释放掉了哪张图（配合 A 的「同一 path 出现多次」即可坐实 ②）。
///
/// 日志节流：同一张图的高频重复不会刷屏，隔 <c>MinLogGapSec</c> 才打一次并带上期间次数。
/// </summary>
internal static class BcsPerf
{
    internal const float DefaultLoadLogMs = 20f;
    internal const float DefaultFrameLogMs = 6f;

    /// <summary>
    /// 探针开关。<b>默认关</b>，跟随 <see cref="AcsMod.DiagnosticLog"/>（<c>ACS.Log on</c>），
    /// 也可以用 <c>ACS.Perf on|off</c> 单独切。关掉时三个 Record/Flush 入口都是空实现。
    /// </summary>
    internal static bool Enabled;

    /// <summary>单次贴图加载耗时 ≥ 该值才打日志（毫秒）。</summary>
    internal static float LoadLogMs = DefaultLoadLogMs;

    /// <summary>单帧所有 ACS patch 的合计耗时 ≥ 该值才打日志（毫秒）。</summary>
    internal static float FrameLogMs = DefaultFrameLogMs;

    /// <summary>同一张图两次日志之间的最短间隔（秒）。</summary>
    internal const float MinLogGapSec = 3f;

    /// <summary>单次贴图加载 ≥ 该值才<b>抓调用栈</b>（毫秒）。抓栈很贵，所以远高于 <see cref="LoadLogMs"/>。</summary>
    internal const float StackLogMs = 200f;

    /// <summary>每张图只抓一次栈（反复重载时不刷屏）。</summary>
    private static readonly HashSet<string> StackCaptured = new(StringComparer.OrdinalIgnoreCase);

    private const float Never = float.NegativeInfinity;

    private sealed class LoadStat
    {
        public int Count;
        public double TotalMs;
        public double MaxMs;
        public double MaxDecodeMs;
        public double MaxUploadMs;
        public string Via = string.Empty;
        public string Shape = string.Empty;
        public float LastLogAt = Never;
        public int SinceLog;
    }

    private static readonly Dictionary<string, LoadStat> Loads = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>被 <see cref="SpriteCacheEvict"/> 释放过的贴图的次数。</summary>
    private static readonly Dictionary<string, int> Evicted = new(StringComparer.OrdinalIgnoreCase);

    private static double _frameAccMs;
    private static double _frameSlowestMs;
    private static string _frameSlowestWhat = string.Empty;
    private static int _framePoints;
    private static float _lastFrameLogAt = Never;

    // ---------------- A. 贴图加载 ----------------

    /// <summary>记录一次 sheet 加载。<paramref name="decodeMs"/> = CPU（读盘+inflate+反filter），
    /// <paramref name="uploadMs"/> = GPU（Texture2D 创建 + Apply + Sprite.Create）。</summary>
    internal static void ReportLoad(string path, double decodeMs, double uploadMs, string via, string shape)
    {
        if (!Enabled || string.IsNullOrEmpty(path)) {
            return;
        }

        double total = decodeMs + uploadMs;

        if (!Loads.TryGetValue(path, out LoadStat? st)) {
            st = new LoadStat();
            Loads[path] = st;
        }

        st.Count++;
        st.SinceLog++;
        st.TotalMs += total;
        st.Via = via;
        st.Shape = shape;
        if (total > st.MaxMs) {
            st.MaxMs = total;
        }

        if (decodeMs > st.MaxDecodeMs) {
            st.MaxDecodeMs = decodeMs;
        }

        if (uploadMs > st.MaxUploadMs) {
            st.MaxUploadMs = uploadMs;
        }

        if (total < LoadLogMs) {
            return;
        }

        // 慢加载才抓栈：用来回答「谁在加载这张图」。每张图只抓一次。
        if (total >= StackLogMs && StackCaptured.Add(path)) {
            AcsMod.Log("[BCS] 慢加载调用栈 " + Short(path)
                       + " (" + total.ToString("0.#", CultureInfo.InvariantCulture) + "ms, via " + via + "):"
                       + Environment.NewLine + FilterStack());
        }

        float now = Time.realtimeSinceStartup;
        if (now - st.LastLogAt < MinLogGapSec) {
            return;
        }

        st.LastLogAt = now;
        string repeats = st.SinceLog > 1 ? " x" + st.SinceLog.ToString(CultureInfo.InvariantCulture) : "";
        st.SinceLog = 0;

        AcsMod.Log("[BCS] perf load " + total.ToString("0.#", CultureInfo.InvariantCulture) + "ms"
                   + " (cpu " + decodeMs.ToString("0.#", CultureInfo.InvariantCulture)
                   + " / gpu " + uploadMs.ToString("0.#", CultureInfo.InvariantCulture) + ")"
                   + " x" + st.Count.ToString(CultureInfo.InvariantCulture) + " total"
                   + repeats
                   + " [" + via + "] " + shape
                   + " " + Short(path));
    }

    // ---------------- B. 每帧 patch 开销 ----------------

    /// <summary>开一个计时点。探针关闭时返回 0（调用方的 <see cref="Done"/> 会忽略）。</summary>
    internal static long Tick() => Enabled ? Stopwatch.GetTimestamp() : 0L;

    /// <summary>从上一个 <see cref="Tick"/> 到现在的毫秒数。探针关闭或未开点时返回 0。</summary>
    internal static double MsSince(long started)
        => started == 0L ? 0d : (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;

    /// <summary>结束一个计时点并累加到本帧。</summary>
    internal static void Done(long started, string what)
    {
        if (!Enabled || started == 0L) {
            return;
        }

        double ms = MsSince(started);
        _frameAccMs += ms;
        _framePoints++;
        if (ms > _frameSlowestMs) {
            _frameSlowestMs = ms;
            _frameSlowestWhat = what;
        }
    }

    /// <summary>
    /// 结算上一帧（<c>AcsMod.Update</c> 每帧调一次；Unity 的 Update 早于渲染，读到的正是上一帧累计值）。
    /// </summary>
    internal static void FlushFrame()
    {
        if (!Enabled) {
            return;
        }

        double acc = _frameAccMs;
        double slowest = _frameSlowestMs;
        string what = _frameSlowestWhat;
        int points = _framePoints;

        _frameAccMs = 0d;
        _frameSlowestMs = 0d;
        _frameSlowestWhat = string.Empty;
        _framePoints = 0;

        if (acc < FrameLogMs || points == 0) {
            return;
        }

        float now = Time.realtimeSinceStartup;
        if (now - _lastFrameLogAt < MinLogGapSec) {
            return;
        }

        _lastFrameLogAt = now;
        AcsMod.Log("[BCS] perf frame " + acc.ToString("0.#", CultureInfo.InvariantCulture) + "ms"
                   + " / " + points.ToString(CultureInfo.InvariantCulture) + " actors"
                   + "; slowest " + slowest.ToString("0.#", CultureInfo.InvariantCulture) + "ms"
                   + " (" + what + ")");
    }

    // ---------------- C. 释放记录 ----------------

    /// <summary>记一次贴图释放。与 A 的「同一 path 加载多次」对照即可判断是否反复重载。</summary>
    internal static void NoteEvict(string path)
    {
        if (!Enabled || string.IsNullOrEmpty(path)) {
            return;
        }

        Evicted[path] = Evicted.TryGetValue(path, out int n) ? n + 1 : 1;
    }

    // ---------------- 查询 / 命令 ----------------

    internal static string Status()
    {
        return "perf probe = " + (Enabled ? "ON" : "OFF")
               + "; load log >= " + LoadLogMs.ToString("0.#", CultureInfo.InvariantCulture) + "ms"
               + "; frame log >= " + FrameLogMs.ToString("0.#", CultureInfo.InvariantCulture) + "ms"
               + "; tracked loads " + Loads.Count.ToString(CultureInfo.InvariantCulture)
               + "; evictions " + Evicted.Count.ToString(CultureInfo.InvariantCulture);
    }

    internal static void Clear()
    {
        Loads.Clear();
        Evicted.Clear();
        StackCaptured.Clear();
        _frameAccMs = 0d;
        _frameSlowestMs = 0d;
        _frameSlowestWhat = string.Empty;
        _framePoints = 0;
        _lastFrameLogAt = Never;
    }

    /// <summary>
    /// 抓当前调用栈，只保留跟「谁触发了这次加载」有关的帧。
    /// 过滤掉 BCL / 反射 / 任务调度这类噪音，最多 10 帧 —— 要看的是
    /// 「<c>CardActor.OnRender</c> → … → <c>SpriteData.LoadSprites</c>」这条链上
    /// 到底是 BCS 在主动加载，还是原版渲染路径顺手带出来的。
    /// </summary>
    private static string FilterStack()
    {
        var kept = new List<string>();
        string[] raw = (Environment.StackTrace ?? string.Empty).Split('\n');

        foreach (string line in raw)
        {
            string t = line.Trim();
            if (t.Length == 0) {
                continue;
            }

            bool interesting = t.IndexOf("ACS.", StringComparison.Ordinal) >= 0
                || t.IndexOf("SpriteData", StringComparison.Ordinal) >= 0
                || t.IndexOf("PngTiler", StringComparison.Ordinal) >= 0
                || t.IndexOf("SpriteReplacer", StringComparison.Ordinal) >= 0
                || t.IndexOf("CardActor", StringComparison.Ordinal) >= 0
                || t.IndexOf("CharaActor", StringComparison.Ordinal) >= 0
                || t.IndexOf("TextureSkinCatalog", StringComparison.Ordinal) >= 0
                || t.IndexOf("SkinListPatch", StringComparison.Ordinal) >= 0;

            if (!interesting) {
                continue;
            }

            kept.Add("      " + t);
            if (kept.Count >= 10) {
                break;
            }
        }

        return kept.Count == 0
            ? "      (栈里没有相关帧 —— 可能被 Harmony 包装或栈太浅)"
            : string.Join(Environment.NewLine, kept.ToArray());
    }

    /// <summary>汇总报表：按总耗时倒序，重点看 <c>n</c>（同一张图被加载了几次）。</summary>
    internal static string Dump()
    {
        if (Loads.Count == 0 && Evicted.Count == 0) {
            return "perf: nothing recorded yet.";
        }

        var rows = new List<LoadStat>();
        var paths = new List<string>();
        foreach (var kv in Loads) {
            rows.Add(kv.Value);
            paths.Add(kv.Value.Shape + " " + kv.Key);
        }

        // 简单选择排序（条目量是几十，不必上 LINQ）
        for (int i = 0; i < rows.Count; i++) {
            for (int j = i + 1; j < rows.Count; j++) {
                if (rows[j].TotalMs > rows[i].TotalMs) {
                    (rows[i], rows[j]) = (rows[j], rows[i]);
                    (paths[i], paths[j]) = (paths[j], paths[i]);
                }
            }
        }

        var sb = new StringBuilder();
        sb.Append("perf: loads tracked = ").Append(Loads.Count).Append('\n');
        sb.Append("  n = this sheet was loaded this many times (>1 means it was evicted and re-decoded)\n");

        var culture = CultureInfo.InvariantCulture;
        for (int i = 0; i < rows.Count && i < 15; i++) {
            LoadStat s = rows[i];
            sb.Append("  ").Append((i + 1).ToString(culture)).Append(". n=").Append(s.Count.ToString(culture))
              .Append("  total ").Append(s.TotalMs.ToString("0.#", culture)).Append("ms")
              .Append("  max ").Append(s.MaxMs.ToString("0.#", culture)).Append("ms")
              .Append("  (cpu ").Append(s.MaxDecodeMs.ToString("0.#", culture))
              .Append(" / gpu ").Append(s.MaxUploadMs.ToString("0.#", culture)).Append(")")
              .Append("  [").Append(s.Via).Append("] ").Append(s.Shape)
              .Append('\n').Append("       ").Append(Short(paths[i])).Append('\n');
        }

        if (Evicted.Count > 0) {
            sb.Append("evicted (released by cache sweep):\n");
            foreach (var kv in Evicted) {
                sb.Append("  x").Append(kv.Value.ToString(culture)).Append("  ").Append(Short(kv.Key)).Append('\n');
            }
        }

        return sb.ToString();
    }

    /// <summary>只留文件名+尺寸，别把两百多个字符的绝对路径铺满控制台。</summary>
    private static string Short(string path)
    {
        if (string.IsNullOrEmpty(path)) {
            return string.Empty;
        }

        int sep = path.LastIndexOfAny(new[] { '/', '\\' });
        return sep >= 0 ? path.Substring(sep + 1) : path;
    }
}

/// <summary>
/// 探针专用 patch：给整个 <c>CardActor.OnRender</c> 计时（含原版本体 + BCS 打在上面的所有 patch）。
///
/// 为什么不塞进 <c>RenderFramePatch</c>：那边是 IL transpiler + 多出口的 Postfix，
/// 为了计时去改它的控制流不值得。这里单独挂一对 Prefix/Postfix，
/// 优先级别设为 First / Last 尽量贴近整段的真实边界；将来拆探针 = 删这一个文件。
///
/// 只用一个「当前计时点」，异常时（Postfix 不跑）残留的下一次 Begin 会覆盖，无所谓。
/// </summary>
[HarmonyPatch(typeof(CardActor), nameof(CardActor.OnRender))]
internal static class CardActorRenderPerfProbe
{
    private static long _started;
    private static int _actorId;

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Begin(CardActor __instance)
    {
        _started = BcsPerf.Tick();
        _actorId = __instance.GetInstanceID();
    }

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void End(CardActor __instance)
    {
        if (_started == 0L) {
            return;
        }

        // GetInstanceID() 对活对象恒非 0，原来的 id != 0 分支永不成立。
        if (__instance.GetInstanceID() != _actorId) {
            return;
        }

        BcsPerf.Done(_started, "CardActor.OnRender");
        _started = 0L;
    }
}
