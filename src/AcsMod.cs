using System;
using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Unity;
using HarmonyLib;
using ReflexCLI;
using UnityEngine;

[assembly: AssemblyVersion("2.3.0.0")]
[assembly: AssemblyFileVersion("2.3.0.0")]
[assembly: AssemblyInformationalVersion("2.3.0")]
[assembly: AssemblyProduct(ACS.ModInfo.Guid)]
[assembly: AssemblyTitle(ACS.ModInfo.Name)]
[assembly: AssemblyMetadata("Repo", "https://github.com/mctrr/BetterCustomSprites")]

namespace ACS;

internal static class ModInfo
{
    internal const string Guid = "mctrr.bettercustomsprites";
    internal const string Name = "Better Custom Sprites";
    internal const string Version = "2.3.0";

    /// <summary>Original Animated Custom Sprites (DK). Must not run alongside BCS.</summary>
    internal const string OriginalAcsGuid = "dk.elinplugins.animatedcustomsprites";
    internal const string OriginalAcsAssemblyName = "AnimatedCustomSprites";
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

        // Immediate pass (BepInEx plugins folder). Package mods may load later.
        TryReportOriginalAcsConflict("Awake");

        var harmony = new Harmony(ModInfo.Guid);
        harmony.PatchAll();

        CommandRegistry.assemblies.Add(Assembly.GetExecutingAssembly());

        // Package Chainloader loads after ModManager init; keep scanning briefly.
        StartCoroutine(WatchOriginalAcsConflict());
    }

    private static IEnumerator WatchOriginalAcsConflict()
    {
        // ~10s window covers delayed package plugin load without permanent tick cost.
        for (int i = 0; i < 200; i++) {
            TryReportOriginalAcsConflict($"watch#{i}");
            if (_conflictPopupShown) {
                yield break;
            }

            yield return new WaitForSecondsRealtime(0.05f);
        }
    }

    private static void TryReportOriginalAcsConflict(string reason)
    {
        if (!IsOriginalAcsPresent(out var evidence)) {
            return;
        }

        if (!_conflictLogged) {
            _conflictLogged = true;
            Error(
                $"CONFLICT: original Animated Custom Sprites is also loaded ({evidence}). " +
                "Unload it and keep only Better Custom Sprites. reason=" + reason);
        }

        TryShowConflictPopup(evidence);
    }

    private static bool IsOriginalAcsPresent(out string evidence)
    {
        evidence = "";

        // Elin BepInEx: use BepInExInstance.Chainloader.Plugins (not legacy Chainloader.PluginInfos).
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

        try {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                if (asm is null) {
                    continue;
                }

                string name;
                try {
                    name = asm.GetName().Name ?? "";
                }
                catch (Exception) {
                    continue;
                }

                if (string.Equals(name, ModInfo.OriginalAcsAssemblyName, StringComparison.OrdinalIgnoreCase)) {
                    evidence = "assembly " + name;
                    return true;
                }

                // Package-loaded plugin assemblies sometimes keep a different file name;
                // fall back to BepInPlugin GUID on exported types.
                Type[] types;
                try {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex) {
                    types = ex.Types;
                }
                catch (Exception) {
                    continue;
                }

                if (types is null) {
                    continue;
                }

                foreach (var t in types) {
                    if (t is null) {
                        continue;
                    }

                    try {
                        var attrs = t.GetCustomAttributes(typeof(BepInPlugin), inherit: false);
                        foreach (var raw in attrs) {
                            if (raw is BepInPlugin plugin
                                && string.Equals(plugin.GUID, ModInfo.OriginalAcsGuid, StringComparison.OrdinalIgnoreCase)) {
                                evidence = "type " + t.FullName + " guid=" + plugin.GUID;
                                return true;
                            }
                        }
                    }
                    catch (Exception) {
                        // ignore single-type attribute failures
                    }
                }
            }
        }
        catch (Exception) {
            // ignore domain scan failures
        }

        try {
            // Activated Elin packages (workshop / Package folder), even before DLL reflection settles.
            var packages = EClass.core?.mods?.packages;
            if (packages != null) {
                foreach (var package in packages) {
                    if (package is null || !package.activated) {
                        continue;
                    }

                    if (string.Equals(package.id, ModInfo.OriginalAcsGuid, StringComparison.OrdinalIgnoreCase)) {
                        evidence = "package id " + package.id;
                        return true;
                    }

                    // Title match as weak fallback for renamed local copies.
                    string title = package.title ?? "";
                    if (title.IndexOf("Animated Custom Sprites", StringComparison.OrdinalIgnoreCase) >= 0
                        && title.IndexOf("Better", StringComparison.OrdinalIgnoreCase) < 0) {
                        evidence = "package title '" + title + "' id=" + package.id;
                        return true;
                    }
                }
            }
        }
        catch (Exception) {
            // ModManager may not be ready yet.
        }

        try {
            foreach (var obj in ModManager.ListPluginObject) {
                if (obj is null) {
                    continue;
                }

                var t = obj.GetType();
                var attrs = t.GetCustomAttributes(typeof(BepInPlugin), inherit: false);
                foreach (var raw in attrs) {
                    if (raw is BepInPlugin plugin
                        && string.Equals(plugin.GUID, ModInfo.OriginalAcsGuid, StringComparison.OrdinalIgnoreCase)) {
                        evidence = "ListPluginObject " + t.FullName;
                        return true;
                    }
                }
            }
        }
        catch (Exception) {
            // ignore
        }

        return false;
    }

    private static void TryShowConflictPopup(string evidence)
    {
        if (_conflictPopupShown) {
            return;
        }

        try {
            if (EClass.ui is null || !EClass.ui) {
                return;
            }

            // Prefer a real dialog once UI exists; fall back to message log.
            // Raw text (no .lang lookup) so the warning still shows without lang keys.
            string message =
                "[Better Custom Sprites] Conflict detected.\n" +
                "Please UNINSTALL original Animated Custom Sprites (DK).\n" +
                "Keep only Better Custom Sprites.\n" +
                "Running both breaks sprite animation.\n" +
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
                // Log-only path already emitted in TryReportOriginalAcsConflict.
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
