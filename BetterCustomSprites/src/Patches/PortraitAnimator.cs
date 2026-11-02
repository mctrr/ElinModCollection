using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace ACS.Patches;

/// <summary>
/// 对话动态立绘（序列帧播放）。
///
/// 思路：复用 ACS 那套“横条 strip + 等宽切帧 + 按间隔换帧”，但作用于对话立绘
/// <see cref="Portrait.imageFull"/> 上的普通 Image（不走 SpriteData / 状态机）。
///
/// 素材约定（mod 的 <c>Portrait/</c> 目录）：
/// <list type="bullet">
/// <item><c>{id}-full.png</c>：立绘本体。若是横条（宽 / 高 为 &gt;=2 的整数）→ 自动
/// 按帧播放；否则按静态图显示（原版行为）。</item>
/// <item><c>{id}-full#41.png</c>：显式动画条，文件名里的数字 = 每帧毫秒
///（缺省 41ms）。帧数按 宽/高 整除推断，或文件名里的 <c>_0-29</c> 指定。</item>
/// </list>
///
/// 播放更新由 <see cref="GameStartPatch.OnSceneUpdate"/> 驱动（对话时 Scene 仍在更新）。
/// </summary>
internal static class PortraitAnimator
{
    private const float DefaultInterval = 0.041f; // 41ms ≈ 24fps

    private sealed class Track
    {
        public Image? Img;
        public Sprite[] Frames = Array.Empty<Sprite>();
        public float Interval = DefaultInterval;
        public int Index;
        public float Timer;
    }

    private static readonly List<Track> Active = new();

    /// <summary>id → 已切好的帧（null = 静态，无动画）。</summary>
    private static readonly Dictionary<string, Sprite[]?> Cache = new();

    /// <summary>id → 每帧间隔（秒）。</summary>
    private static readonly Dictionary<string, float> IntervalCache = new();

    /// <summary>
    /// 立绘显示后登记播放。无动画帧（静态图 / 没找到条）时什么都不做。
    /// </summary>
    internal static void Register(Image? img, string id)
    {
        if (img is null) {
            return;
        }

        for (int i = Active.Count - 1; i >= 0; i--) {
            if (ReferenceEquals(Active[i].Img, img)) {
                Active.RemoveAt(i);
            }
        }

        Sprite[]? frames = GetFrames(id, img.sprite);
        if (frames is null || frames.Length < 2) {
            return;
        }

        Active.Add(new Track {
            Img = img,
            Frames = frames,
            Interval = IntervalCache.TryGetValue(id, out float v) ? v : DefaultInterval,
        });

        // 首帧：避免整条 strip 被当成一张图拉伸显示
        img.sprite = frames[0];
    }

    /// <summary>每帧推进：轮换已登记立绘的 sprite。立绘失活（对话关闭）即移除。</summary>
    internal static void Tick()
    {
        if (Active.Count == 0) {
            return;
        }

        float dt = Core.delta > 0f ? Core.delta : Time.deltaTime;
        for (int i = Active.Count - 1; i >= 0; i--) {
            Track t = Active[i];
            if (t.Img is null || !t.Img.gameObject.activeInHierarchy) {
                Active.RemoveAt(i);
                continue;
            }

            t.Timer += dt;
            if (t.Timer < t.Interval) {
                continue;
            }

            t.Timer -= t.Interval;
            if (t.Timer > t.Interval) {
                t.Timer = 0f; // 切图/长卡顿后不追帧
            }

            t.Index = (t.Index + 1) % t.Frames.Length;
            t.Img.sprite = t.Frames[t.Index];
        }
    }

    /// <summary>换图 / 重载后清缓存（帧 sprite 复用的是原纹理，清引用即可）。</summary>
    internal static void ClearCaches()
    {
        Active.Clear();
        Cache.Clear();
        IntervalCache.Clear();
    }

    private static Sprite[]? GetFrames(string id, Sprite? current)
    {
        if (Cache.TryGetValue(id, out Sprite[]? cached)) {
            return cached;
        }

        Sprite[]? frames = null;
        try {
            // 1) 立绘本体就是横条 → 直接切（零额外文件）
            if (current is not null && current.texture is not null) {
                frames = Slice(current, DefaultInterval, id, out float iv);
                if (frames is not null) {
                    IntervalCache[id] = iv;
                }
            }

            // 2) 否则找显式的 {id}-full#ms.png 动画条
            if (frames is null) {
                string? strip = FindStripFile(id);
                if (strip is not null) {
                    Sprite? sp = LoadSpriteFromFile(strip);
                    if (sp is not null) {
                        float iv = ParseInterval(Path.GetFileNameWithoutExtension(strip), DefaultInterval);
                        frames = Slice(sp, iv, id, out float iv2);
                        IntervalCache[id] = iv2 > 0f ? iv2 : iv;
                    }
                }
            }
        }
        catch (Exception ex) {
            AcsMod.Warn("[PortraitAnim] load failed for " + id + ": " + ex.Message);
        }

        Cache[id] = frames;
        return frames;
    }

    /// <summary>
    /// 把横条 sprite 切成等宽帧。返回 null = 不是横条（按静态图处理）。
    /// </summary>
    private static Sprite[]? Slice(Sprite src, float fallbackInterval, string id, out float interval)
    {
        interval = fallbackInterval;
        if (src.texture is null) {
            return null;
        }

        Rect r = src.textureRect;
        if (r.height <= 0f || r.width <= 0f) {
            return null;
        }

        float ratio = r.width / r.height;
        int n;
        string? stripName = FindStripFile(id);
        Match? range = stripName is null
            ? null
            : Regex.Match(Path.GetFileNameWithoutExtension(stripName), @"_(\d+)-(\d+)$");
        if (range is not null && range.Success
            && int.TryParse(range.Groups[1].Value, out int b)
            && int.TryParse(range.Groups[2].Value, out int e)
            && e >= b && e - b + 1 > 1) {
            n = e - b + 1;
        }
        else {
            n = (int)Math.Round(ratio);
        }

        // 不是横条（竖图 / 单帧）→ 静态
        if (n < 2 || Mathf.Abs(ratio - n) > 0.02f) {
            return null;
        }

        float fw = r.width / n;
        if (fw < 1f) {
            return null;
        }

        Texture2D tex = src.texture;
        tex.wrapMode = TextureWrapMode.Clamp;

        var frames = new Sprite[n];
        for (int i = 0; i < n; i++) {
            var rect = new Rect(r.x + i * fw, r.y, fw, r.height);
            frames[i] = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
        }

        AcsMod.Log($"[PortraitAnim] {id}: {n} frames @ {(int)(fallbackInterval * 1000)}ms");
        return frames;
    }

    /// <summary>在已装 mod 的 Portrait 目录里找 {id}-full#*.png。</summary>
    private static string? FindStripFile(string id)
    {
        try {
            string? root = Path.GetDirectoryName(Application.dataPath);
            if (root.IsEmpty()) {
                return null;
            }

            string pkg = Path.Combine(root, "Package");
            if (!Directory.Exists(pkg)) {
                return null;
            }

            foreach (string dir in Directory.GetDirectories(pkg)) {
                string pd = Path.Combine(dir, "Portrait");
                if (!Directory.Exists(pd)) {
                    continue;
                }

                foreach (string f in Directory.GetFiles(pd, id + "-full#*.png")) {
                    return f;
                }
            }
        }
        catch {
            // 目录枚举失败按无动画处理
        }

        return null;
    }

    private static Sprite? LoadSpriteFromFile(string path)
    {
        try {
            byte[] bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes)) {
                return null;
            }

            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), 100f, 0u, SpriteMeshType.FullRect);
        }
        catch (Exception ex) {
            AcsMod.Warn("[PortraitAnim] load " + path + " failed: " + ex.Message);
            return null;
        }
    }

    /// <summary>从文件名解析 #毫秒。</summary>
    private static float ParseInterval(string nameWithoutExt, float fallback)
    {
        Match m = Regex.Match(nameWithoutExt, @"#(\d+)");
        if (m.Success && int.TryParse(m.Groups[1].Value, out int ms) && ms > 0) {
            return ms / 1000f;
        }

        return fallback;
    }
}
