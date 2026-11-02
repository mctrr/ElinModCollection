using System;
using System.Collections;
using System.Reflection;
using ACS.Patches;
using BepInEx;
using BepInEx.Unity;
using HarmonyLib;
using ReflexCLI;
using UnityEngine;

[assembly: AssemblyVersion("2.5.0.0")]
[assembly: AssemblyFileVersion("2.5.0.0")]
[assembly: AssemblyInformationalVersion("2.5.0")]
[assembly: AssemblyProduct(ACS.ModInfo.Guid)]
[assembly: AssemblyTitle(ACS.ModInfo.Name)]
[assembly: AssemblyMetadata("Repo", "https://github.com/mctrr/BetterCustomSprites")]

namespace ACS;

internal static class ModInfo
{
    internal const string Guid = "mctrr.bettercustomsprites";
    internal const string Name = "Better Custom Sprites";
    internal const string Version = "2.5.0";

    // ---- 已知会抢动画链路的冲突 mod ----

    /// <summary>原版 Animated Custom Sprites（DK）。与 BCS 同源竞争，绝对不能同时启用。
    /// 这是<b>唯一</b>被判定为冲突的 mod —— 加白名单前先去看对方到底 patch 了什么。</summary>
    internal const string OriginalAcsGuid = "dk.elinplugins.animatedcustomsprites";
    internal const string OriginalAcsAssemblyName = "AnimatedCustomSprites";

    /// <summary>必须独占的 mod。只有原生 ACS —— 它同样在 CardActor.OnRender 上换 sprite。</summary>
    internal static readonly string[] ConflictAssemblyMarkers =
    {
        OriginalAcsAssemblyName,
    };
}

[BepInPlugin(ModInfo.Guid, ModInfo.Name, ModInfo.Version)]
internal class AcsMod : BaseUnityPlugin
{
    internal static AcsMod? Instance { get; private set; }

    private static bool _conflictLogged;
    private static bool _conflictPopupShown;

    private void Awake()
    {
        Instance = this;

        // 绑定设置项。原版模组配置页只收录「有 ConfigEntry 的插件」，
        // 不绑定的话 BCS 在模组列表里连「配置」按钮都不会出现。
        BcsConfig.Init(Config);

        // 立即扫一轮（BepInEx plugins）。Package 里的 mod 加载较晚，后面再 watch 一会。
        TryReportConflict("Awake");

        var harmony = new Harmony(ModInfo.Guid);
        harmony.PatchAll();

        CommandRegistry.assemblies.Add(Assembly.GetExecutingAssembly());

        // Package Chainloader 在 ModManager init 之后才加载；持续扫约 10s 覆盖延迟加载。
        StartCoroutine(WatchConflict());

        // 配置页自制控件要在 ModManager 给 dysmod 分好 package 之后才能挂上去，一并交给协程重试。
        StartCoroutine(WatchConfigUi());
    }

    /// <summary>
    /// 等 ModManager 就绪后把自制控件挂进原版配置页。
    /// 原版 <c>RegisterAll</c> 的时机在 ModManager init 之后、具体时刻不保证，
    /// 所以用低频轮询而不是一次性尝试。
    /// </summary>
    private static IEnumerator WatchConfigUi()
    {
        // 与 WatchConflict 同款节奏：0.5s 一轮，约 10s 窗口。
        for (int i = 0; i < 20; i++) {
            try {
                if (BcsConfigUi.TryRegister()) {
                    yield break;
                }
            }
            catch (Exception ex) {
                // 单次失败不该中止后续尝试，但也不该每帧刷日志。
                Warn("[ConfigUI] register attempt #" + i + " threw: " + ex.Message);
                BcsConfigUi.GiveUp();
                yield break;
            }

            yield return new WaitForSecondsRealtime(0.5f);
        }

        // 十秒都没成功 = 这一局大概率版本不支持（或本 mod 不是以 Package 方式加载）。
        BcsConfigUi.GiveUp();
    }

    /// <summary>
    /// 每帧驱动按需释放（内部按秒限流，不扫描时开销只有一次时间比较）。
    /// 原版只懒加载、从不卸载，长局会把摸过的皮肤一路攒在显存里；
    /// 这里把「当前地图用不到的」贴图交还回去。
    /// </summary>
    private void Update()
    {
        // 结算上一帧探测到的开销（Update 早于渲染，读到的正是上一帧的累计值）。
        BcsPerf.FlushFrame();

        // 贴图预热：收集当前场景 NPC 贴图 + 收后台结果 + 派发新任务。
        GreetPrewarm.Tick();

        SpriteCacheEvict.Tick();

        SkinTextureReuse.Tick();

        SkinHotReload.Tick();
    }

    /// <summary>
    /// 退出游戏时把性能报表落进日志。
    ///
    /// <b>为什么需要</b>：探针开着时数据只累积在内存里，玩家看到的是卡顿、
    /// 不是数字。而 dump 需要一个明确的时机触发 —— 退游戏正是最自然的时机
    /// （此时玩家已经完整体验过一轮，不需要再操作界面）。
    /// 有了它，排障流程就是「打开诊断日志开关 → 进游戏 → 退游戏」，
    /// 玩家不需要敲任何控制台命令，也不需要点任何按钮。
    /// </summary>
    private void OnDestroy()
    {
        try {
            if (BcsPerf.Enabled) {
                // 用 this.Logger 而不是 AcsMod.Log：退出时静态 Instance 可能已置空，
                // 而 Logger 是实例成员，一定还活着。
                Logger.LogInfo("[BCS] perf dump (auto on quit)\n" + BcsPerf.Dump());
            }
        }
        catch (Exception) {
            // 退出阶段什么都可能正在销毁，失败就失败，绝不能抛到 Unity 的退出流程里。
        }
    }

    private static IEnumerator WatchConflict()
    {
        // 全程序集反射扫描较重；用少量低频轮询覆盖 Package 延迟加载，避免启动卡顿。
        // 命中冲突后立即弹窗退出；窗口约 10s。
        for (int i = 0; i < 20; i++) {
            TryReportConflict($"watch#{i}");
            if (_conflictPopupShown) {
                yield break;
            }

            yield return new WaitForSecondsRealtime(0.5f);
        }
    }

    private static void TryReportConflict(string reason)
    {
        if (!FindConflict(out string evidence)) {
            return;
        }

        if (!_conflictLogged) {
            _conflictLogged = true;
            Error(
                "CONFLICT: the original Animated Custom Sprites (DK) is loaded (" + evidence + "). " +
                "Both take over character animation; NPC combat/move animation stops switching " +
                "while both are active. Keep only one of them. reason=" + reason);
        }

        TryShowConflictPopup(evidence);
    }

    // ---------------- 检测 ----------------

    private static bool FindConflict(out string evidence)
    {
        evidence = "";

        // 1) BepInEx Chainloader 注册的插件（按 GUID 精确匹配原版 ACS）。
        try {
            var plugins = BepInExInstance.Chainloader?.Plugins;
            if (plugins != null && plugins.ContainsKey(ModInfo.OriginalAcsGuid)) {
                evidence = "BepInEx plugin " + ModInfo.OriginalAcsGuid;
                return true;
            }
        }
        catch (Exception) {
            // Chainloader may be unavailable very early.
        }

        // 2) 程序集名扫描（轻量，不 GetTypes 反射）。原版 ACS 的程序集名带 marker：
        //    AnimatedCustomSprites。改名伪装由下方 package title 兜底。
        try {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                if (asm is null || asm == Assembly.GetExecutingAssembly()) {
                    continue;
                }

                string name;
                try {
                    name = asm.GetName().Name ?? "";
                }
                catch (Exception) {
                    continue;
                }

                if (name.IndexOf("BetterCustomSprites", StringComparison.OrdinalIgnoreCase) >= 0) {
                    continue; // 自己
                }

                if (ContainsAnyMarker(name)) {
                    evidence = "assembly " + name;
                    return true;
                }
            }
        }
        catch (Exception) {
            // ignore domain scan failures
        }

        // 3) 已激活的 Elin Package（workshop / Package 目录），DLL 反射还没就绪时也能抓到。
        try {
            var packages = EClass.core?.mods?.packages;
            if (packages != null) {
                foreach (var package in packages) {
                    if (package is null || !package.activated) {
                        continue;
                    }

                    string pid = package.id ?? "";

                    if (string.Equals(pid, ModInfo.OriginalAcsGuid, StringComparison.OrdinalIgnoreCase)) {
                        evidence = "package id " + pid;
                        return true;
                    }

                    // Title 弱匹配（本地改名副本也抓）：含 "Animated Custom Sprites"，且不含 Better。
                    string title = package.title ?? "";
                    if (title.IndexOf("Animated Custom Sprites", StringComparison.OrdinalIgnoreCase) >= 0
                        && title.IndexOf("Better", StringComparison.OrdinalIgnoreCase) < 0) {
                        evidence = "package title '" + title + "' id=" + pid;
                        return true;
                    }
                }
            }
        }
        catch (Exception) {
            // ModManager may not be ready yet.
        }

        // 4) ModManager 已实例化的插件对象（兼容旧版 BepInEx 路径）。
        try {
            foreach (var obj in ModManager.ListPluginObject) {
                if (obj is null) {
                    continue;
                }

                string objName;
                try {
                    objName = obj.GetType().FullName ?? "";
                }
                catch (Exception) {
                    continue;
                }

                if (ContainsAnyMarker(objName)) {
                    evidence = "ListPluginObject " + objName;
                    return true;
                }

                try {
                    var attrs = obj.GetType().GetCustomAttributes(typeof(BepInPlugin), inherit: false);
                    foreach (var raw in attrs) {
                        if (raw is BepInPlugin plugin
                            && string.Equals(plugin.GUID, ModInfo.OriginalAcsGuid, StringComparison.OrdinalIgnoreCase)) {
                            evidence = "ListPluginObject " + objName;
                            return true;
                        }
                    }
                }
                catch (Exception) {
                    // ignore
                }
            }
        }
        catch (Exception) {
            // ignore
        }

        return false;
    }

    private static bool ContainsAnyMarker(string text)
    {
        if (string.IsNullOrEmpty(text)) {
            return false;
        }

        foreach (var marker in ModInfo.ConflictAssemblyMarkers) {
            if (text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        return false;
    }

    // ---------------- 弹窗 ----------------

    private static void TryShowConflictPopup(string evidence)
    {
        if (_conflictPopupShown) {
            return;
        }

        try {
            if (EClass.ui is null || !EClass.ui) {
                return;
            }

            // 用原文（不查 .lang），保证缺语言键也显示。
            string message =
                "[Better Custom Sprites] 检测到原版 Animated Custom Sprites (DK)。\n" +
                "两者都会接管角色动画，同时启用会让 NPC 战斗 / 移动不再切换动作。\n" +
                "建议只保留其中一个。\n" +
                "(" + evidence + ")";

            try {
                var dialog = Layer.Create<Dialog>();
                if (dialog is not null) {
                    if (dialog.textDetail is not null) {
                        dialog.textDetail.SetText(message);
                    }

                    dialog.list.AddButton(null, Lang.Get("ok"), dialog.Close);
                    EClass.ui.AddLayer(dialog);
                    _conflictPopupShown = true;
                    return;
                }
            }
            catch (Exception ex) {
                Warn("Conflict dialog failed: " + ex.Message);
            }

            try {
                Msg.SayRaw(message.Replace('\n', ' '));
                _conflictPopupShown = true;
            }
            catch (Exception) {
                // Log-only path already emitted in TryReportConflict.
            }
        }
        catch (Exception) {
            // Never break startup because the warning UI failed.
        }
    }

    /// <summary>
    /// 诊断日志总开关，<b>默认关</b>。由「诊断日志」设置项 / <c>ACS.Log</c> 命令控制。
    ///
    /// 为什么要有：BCS 的 Info 级输出里绝大部分是诊断（皮肤表枚举、密度注入、分片结果、
    /// 缓存释放、性能探针…），一次 <c>ListSkins()</c> 重建就是 100+ 行。普通玩家不需要看这些，
    /// 关掉能显著缩短 BepInEx 日志的解析时间。<b>开发者排查问题时用 ACS.Log on 打开。</b>
    ///
    /// 注意：<b>只门控 <see cref="Log"/>（Info）</b>。<see cref="Warn"/> 与 <see cref="Error"/>
    /// 永远输出 —— 那是真出问题的线索，关掉等于把排障的眼睛蒙上。
    /// </summary>
    internal static bool DiagnosticLog;

    internal static void Log(object payload)
    {
        if (!DiagnosticLog) {
            return;
        }

        Instance!.Logger.LogInfo(payload);
    }

    internal static void Warn(object payload)
    {
        Instance!.Logger.LogWarning(payload);
    }

    internal static void Error(object payload)
    {
        Instance!.Logger.LogError(payload);
    }
}
