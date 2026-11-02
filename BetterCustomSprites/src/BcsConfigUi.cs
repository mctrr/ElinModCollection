using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine.UI;

namespace ACS;

/// <summary>
/// 在原版「模组配置」页里追加一段**自制控件**（<see cref="UINote"/>），给对话字幕做快速预设。
///
/// <b>为什么不用 <c>IModConfig</c></b>（这是本模块最关键的取舍，改动前务必先看）：
/// 原版 <c>EModding.ModConfig.RegisterAll</c> 对每个插件做的是
/// <code>
///     package.onBuildConfig += note =&gt; Build(plugin, note);
///     // Build(): if (plugin is IModConfig custom) custom.OnBuildConfig(note);
///     //          else note.AddAll(plugin.Config);
/// </code>
/// 也就是说 —— **实现了 <c>IModConfig</c> 会整段顶掉自动绘制**，
/// BCS 现有的那几十条 ConfigEntry（立绘高度、预热、缓存、诊断…）会全部从配置页消失，
/// 得自己一条条重画。我们不想要这个结果。
/// 所以这里改成往同一个 <c>onBuildConfig</c> 委托上**再挂一个**处理器：
/// 委托按订阅顺序触发，原版先画完全部自动项，我们追加在后面 —— 两者共存。
///
/// <b>版本安全</b>：<c>onBuildConfig</c> 字段是新版（EA 23.350+ 自带 ModConfig 辅助层）才有的。
/// 旧版上它压根不存在 —— 但因为本模块<b>没有</b>在类型签名里实现 <c>IModConfig</c>，
/// 不会触发 <c>TypeLoadException</c>（那会直接让整个插件加载失败），
/// 只会在调用时抛 <c>MissingFieldException</c>，被这里的 try 吞掉 → 优雅降级为「没有这段 UI」。
/// 另外我们以「<c>onBuildConfig</c> 是否已非空」作为新版能力探测：原版没挂过就说明不支持，不着急重试。
///
/// <b>为什么这里只放预设、不重画滑杆</b>：那 4 个参数本身已经是带
/// <c>AcceptableValueRange</c> 的 ConfigEntry，原版会把它们画成滑杆，
/// 而拖动会触发 <c>SettingChanged → BcsConfig.Apply</c>，布局又是每帧重算的 ——
/// <b>实时调整早就有了</b>。真正缺的只有「一次套一套」，也就是下面这些按钮。
/// </summary>
internal static class BcsConfigUi
{
    /// <summary>预设：名字 + 四个参数（高度比 / 底部不透明度 / 顶部不透明度 / 渐变起点）。</summary>
    private sealed class Preset
    {
        public string Name = "";
        public string Desc = "";
        public float Height;
        public float Alpha;
        public float GradTop;
        public float GradStart;
    }

    private static readonly Preset[] Presets =
    [
        new() { Name = "标准", Desc = "底部黑条 + 向上自然渐隐，通用", Height = 0.22f, Alpha = 0.62f, GradTop = 0f, GradStart = 0f },
        new() { Name = "沉浸", Desc = "条更薄更透，渐隐更高，几乎不挡画面", Height = 0.16f, Alpha = 0.45f, GradTop = 0f, GradStart = 0.30f },
        new() { Name = "影院", Desc = "更厚更黑，浅色场景里最清楚", Height = 0.30f, Alpha = 0.82f, GradTop = 0.15f, GradStart = 0.35f },
        new() { Name = "纯文字", Desc = "完全不留衬底，文字直接浮在画面上", Height = 0.18f, Alpha = 0f, GradTop = 0f, GradStart = 0f },
    ];

    private const string SecTitle = "对话字幕 · 快速预设";

    private static bool _registered;
    private static bool _gaveUp;
    private static UIText? _status;

    /// <summary>
    /// 尝试把自己挂到 mod 的配置页上。幂等；成功后不再做任何事。
    /// 失败多半只是「ModManager 还没建好」，交给外层重试，不到最后一轮不算放弃。
    /// </summary>
    internal static bool TryRegister()
    {
        if (_registered || _gaveUp) {
            return _registered;
        }

        try {
            var plugin = AcsMod.Instance;
            string? location = plugin?.Info.Location;
            if (location is null || location.Length == 0) {
                return false;
            }

            // 原版 RegisterAll 用的就是这一个查找；保持一致才能保证挂的是同一个 package。
            ModPackage? pkg = ModUtil.FindFileProviderPackage(new FileInfo(location));
            if (pkg is null) {
                return false;
            }

            // 能力探测 + 时序判据：新版的 RegisterAll 挂过之后这里才非空。
            // 为空说明还没到（继续重试）或版本不支持（永远为空，重试有上限）。
            if (pkg.onBuildConfig is null) {
                return false;
            }

            pkg.onBuildConfig += Build;
            _registered = true;
            AcsMod.Log("[ConfigUI] attached to mod config page.");
            return true;
        }
        catch (Exception ex) {
            // MissingFieldException = 旧版没有 onBuildConfig → 永久放弃，不再重试。
            _gaveUp = true;
            AcsMod.Warn("[ConfigUI] cannot attach custom config UI (unsupported game version?): " + ex.Message);
            return false;
        }
    }

    /// <summary>放弃前的最后一次重试失败时调用 —— 不让「不支持」变成每帧刷日志。</summary>
    internal static void GiveUp() => _gaveUp = true;

    private static void Build(UINote note)
    {
        try {
            note.Space();
            note.AddHeader(SecTitle);
            note.AddText("一键套用整套参数。上面的滑杆仍可继续微调，改动即时生效。", FontColor.Passive);

            _status = null;

            foreach (Preset p in Presets) {
                note.AddButton(p.Name, () => Apply(p));
            }

            UIItem? item = note.AddText(CurrentLine(), FontColor.Passive);
            _status = item?.text1;
        }
        catch (Exception ex) {
            AcsMod.Warn("[ConfigUI] build failed: " + ex.Message);
        }
    }

    private static void Apply(Preset p)
    {
        try {
            // 走批量写入：四个参数一次写 + 一次落盘，避免连开四次配置文件 IO。
            BcsConfig.SetSubtitlePreset(p.Height, p.Alpha, p.GradTop, p.GradStart);
            Refresh();
        }
        catch (Exception ex) {
            AcsMod.Warn("[ConfigUI] preset failed: " + ex.Message);
        }
    }

    private static void Refresh()
    {
        UIText? t = _status;
        if (t is not null) {
            t.SetText(CurrentLine());
        }
    }

    /// <summary>当前状态行：命中预设就报名字，否则报「自定义」并列出四个值。</summary>
    private static string CurrentLine()
    {
        string now = Describe();
        Preset? hit = Match();
        return hit is not null ? $"当前：{hit.Name} —— {hit.Desc}" : $"当前：自定义（{now}）";
    }

    private static string Describe()
        => $"高度 {DialogSubtitleHeight:0.00}, 底 {DialogSubtitleAlpha:0.00}, "
           + $"顶 {DialogSubtitleGradTop:0.00}, 起点 {DialogSubtitleGradStart:0.00}";

    private static float DialogSubtitleHeight => Patches.DialogSubtitleLayout.HeightRatio;
    private static float DialogSubtitleAlpha => Patches.DialogSubtitleLayout.BackdropAlpha;
    private static float DialogSubtitleGradTop => Patches.DialogSubtitleLayout.GradientTopAlpha;
    private static float DialogSubtitleGradStart => Patches.DialogSubtitleLayout.GradientStart;

    private static Preset? Match()
    {
        foreach (Preset p in Presets) {
            if (Math.Abs(p.Height - DialogSubtitleHeight) < 0.002f
                && Math.Abs(p.Alpha - DialogSubtitleAlpha) < 0.002f
                && Math.Abs(p.GradTop - DialogSubtitleGradTop) < 0.002f
                && Math.Abs(p.GradStart - DialogSubtitleGradStart) < 0.002f) {
                return p;
            }
        }

        return null;
    }
}
