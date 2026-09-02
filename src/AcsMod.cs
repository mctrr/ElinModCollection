using System;
using System.Collections;
using System.Reflection;
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

    // ---- 已知会 patch CardActor.OnRender / 抢动画链路的冲突 mod ----

    /// <summary>原版 Animated Custom Sprites（DK）。与 BCS 同源竞争，绝对不能同时启用。</summary>
    internal const string OriginalAcsGuid = "dk.elinplugins.animatedcustomsprites";
    internal const string OriginalAcsAssemblyName = "AnimatedCustomSprites";

    /// <summary>CustomWhateverLoader / CustomWhatever：同样 patch CardActor.OnRender，
    /// 缺依赖时会抛 FileNotFoundException 让 Harmony 补丁链失效（NPC 动画静默全挂）。</summary>
    internal const string CustomWhateverAssemblyMarker = "CustomWhatever";

    /// <summary>冲突检测关键词表：assembly 名 / 类型全名 / package 标题任一命中即报警。</summary>
    internal static readonly string[] ConflictAssemblyMarkers =
    {
        OriginalAcsAssemblyName,
        CustomWhateverAssemblyMarker,
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

        // 立即扫一轮（BepInEx plugins）。Package 里的 mod 加载较晚，后面再 watch 一会。
        TryReportConflict("Awake");

        var harmony = new Harmony(ModInfo.Guid);
        harmony.PatchAll();

        CommandRegistry.assemblies.Add(Assembly.GetExecutingAssembly());

        // Package Chainloader 在 ModManager init 之后才加载；持续扫约 10s 覆盖延迟加载。
        StartCoroutine(WatchConflict());
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
                "CONFLICT: a conflicting sprite-animation mod is loaded (" + evidence + "). " +
                "Better Custom Sprites (ACS) patches CardActor.OnRender; running both breaks " +
                "NPC combat/move animation. Unload the other mod and keep only BCS. reason=" + reason);
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

        // 2) 程序集名扫描（轻量，不 GetTypes 反射）。冲突 mod 的程序集名都带 marker：
        //    AnimatedCustomSprites / CustomWhateverLoader 等。改名伪装由下方 package title 兜底。
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

                    // Title 弱匹配（本地改名副本也抓）：含 "Animated Custom Sprites" 或 "CustomWhatever"，且不含 Better。
                    string title = package.title ?? "";
                    bool looksConflict = (title.IndexOf("Animated Custom Sprites", StringComparison.OrdinalIgnoreCase) >= 0
                                          || title.IndexOf(ModInfo.CustomWhateverAssemblyMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                                         && title.IndexOf("Better", StringComparison.OrdinalIgnoreCase) < 0;
                    if (looksConflict) {
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
                "[Better Custom Sprites] 检测到冲突的动画 mod。\n" +
                "请卸载原版 Animated Custom Sprites (DK) / CustomWhateverLoader 等\n" +
                "会 patch CardActor.OnRender 的 mod，只保留 Better Custom Sprites。\n" +
                "同时启用会破坏 NPC 战斗 / 移动动画（表现为 NPC 不切换动画）。\n" +
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

    internal static void Log(object payload)
    {
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
