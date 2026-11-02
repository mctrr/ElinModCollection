using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace ACS.Patches;

/// <summary>
/// 字幕模式里「玩家用鼠标拖出来的位置增量」的持久化。
///
/// <b>为什么不用 ConfigEntry</b>：
/// BepInEx 的 ConfigEntry 在每次 <c>Value =</c> 赋值时都会触发
/// <c>SettingChanged</c>（本项目接了 “Apply() + 落盘”）。鼠标拖动是每帧事件，
/// 用它存会导致每帧一次 IO —— 所以偏移量单独落一个 JSON，并且**延时合并写入**（松手 ~0.8s 后写一次）。
///
/// <b>为什么不存绝对坐标</b>：只存相对我们算出的基准位置的**增量**。
/// 分辨率一变（1920→2560）或字幕条高度一改，绝对坐标立刻错位；
/// 增量则跟着基准自动换算，永远描述「比默认位置偏多少」。
/// </summary>
internal static class SubtitleLayoutStore
{
    private const string FileName = "mctrr.bettercustomsprites.subtitle.json";

    // ---- 各块的像素增量。真值保存在这里，拖动期间只动内存，由 Tick() 落盘 ----

    internal static float ChoiceX;
    internal static float ChoiceY;
    internal static float AffinityX;
    internal static float AffinityY;

    private static bool _loaded;
    private static float _saveAt = -1f;

    private sealed class Data
    {
        public float choiceX;
        public float choiceY;
        public float affinityX;
        public float affinityY;
    }

    private static string Dir()
    {
        try {
            string p = BepInEx.Paths.ConfigPath;
            if (!string.IsNullOrEmpty(p)) {
                return p;
            }
        }
        catch {
            // 旧版 BepInEx 没有 Paths，退回程序集所在目录。
        }

        try {
            return new FileInfo(typeof(SubtitleLayoutStore).Assembly.Location).DirectoryName ?? ".";
        }
        catch {
            return ".";
        }
    }

    private static string FilePath() => Path.Combine(Dir(), FileName);

    /// <summary>首次访问时读一次。缺文件 / 坏文件都静默按「零偏移」处理 —— 拖坏也不会卡住游戏。</summary>
    internal static void Load()
    {
        if (_loaded) {
            return;
        }

        _loaded = true;

        try {
            string path = FilePath();
            if (!File.Exists(path)) {
                return;
            }

            Data? d = JsonConvert.DeserializeObject<Data>(File.ReadAllText(path));
            if (d == null) {
                return;
            }

            ChoiceX = d.choiceX;
            ChoiceY = d.choiceY;
            AffinityX = d.affinityX;
            AffinityY = d.affinityY;

            AcsMod.Log($"[Subtitle] layout loaded: choice=({ChoiceX:0},{ChoiceY:0})"
                + $" affinity=({AffinityX:0},{AffinityY:0})");
        }
        catch (Exception ex) {
            AcsMod.Warn("[Subtitle] layout load failed, using zero offsets: " + ex.Message);
        }
    }

    /// <summary>拖动后调用：合并延时落盘，避免每帧 IO。</summary>
    internal static void MarkDirty()
    {
        _saveAt = Time.unscaledTime + 0.8f;
    }

    /// <summary>由 <see cref="DialogSubtitleLayout"/> 每帧轮询。</summary>
    internal static void Tick()
    {
        if (_saveAt < 0f || Time.unscaledTime < _saveAt) {
            return;
        }

        _saveAt = -1f;
        Save();
    }

    internal static void Save()
    {
        try {
            string json = JsonConvert.SerializeObject(new Data {
                choiceX = ChoiceX,
                choiceY = ChoiceY,
                affinityX = AffinityX,
                affinityY = AffinityY,
            });

            File.WriteAllText(FilePath(), json);
            AcsMod.Log($"[Subtitle] layout saved: choice=({ChoiceX:0},{ChoiceY:0})"
                + $" affinity=({AffinityX:0},{AffinityY:0})");
        }
        catch (Exception ex) {
            AcsMod.Warn("[Subtitle] layout save failed: " + ex.Message);
        }
    }

    /// <summary>归零并立刻落盘（<c>ACS.Subtitle resetlayout</c>）。</summary>
    internal static void Reset()
    {
        ChoiceX = ChoiceY = AffinityX = AffinityY = 0f;
        _saveAt = -1f;
        Save();
    }

    internal static string Status()
        => $"dragOffset choice=({ChoiceX:0},{ChoiceY:0}) affinity=({AffinityX:0},{AffinityY:0})";
}
