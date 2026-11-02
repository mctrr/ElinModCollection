using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using HarmonyLib;
using NpcLabor.Dispatch;
using NpcLabor.Patches;
using NpcLabor.TownLabor;
using NpcLabor.Process;
using ReflexCLI;

namespace NpcLabor;

public static class ModInfo
{
    public const string Guid = "com.elin.npclabor";
    public const string Name = "NPC Labor";
    public const string Version = "1.14.514";
}

[BepInPlugin(ModInfo.Guid, ModInfo.Name, ModInfo.Version)]
internal class Plugin : BaseUnityPlugin
{
    internal static Plugin? Instance;
    internal static Harmony? Harmony;
    static bool _reflexRegistered;
    static bool _configHooked;
    static bool _configHookWarned;

    /// <summary>
    /// Fills the config tab that <c>LayerModConfig</c> renders for
    /// <c>ModPackage.onBuildConfig</c>.
    ///
    /// Deliberately NOT an <c>IModConfig</c> implementor. That interface is not the
    /// binding contract - the delegate field is - and it exists purely so the shipped
    /// ModdingKit package (<c>Package/_ModdingKit</c>, <c>EModding.ModConfig.RegisterAll</c>)
    /// can auto-wire mods. It would append <c>+=</c> to the very delegate we assign, and
    /// its single registration point is <c>Core.StartCase</c> - too late for the mod list
    /// on the title screen. Two registrants on one delegate means every row is drawn
    /// twice, so we take the whole job ourselves and skip the interface.
    /// </summary>
    public void OnBuildConfig(UINote note)
        => ModConfigUi.Build(note);

    private void Awake()
    {
        Instance = this;
        Harmony = new Harmony(ModInfo.Guid);
        // Register Reflex BEFORE PatchAll so a Harmony failure cannot skip console cmds.
        // BCS only does assemblies.Add; we also Rebuild for late/early Init order.
        RegisterReflexCommands("Awake-pre");
        try
        {
            // Per-class mount + audit instead of Harmony.PatchAll: a single signature
            // drift must not silently kill every patch class after it.
            PatchAudit.ApplyAll(Harmony, Assembly.GetExecutingAssembly());
        }
        catch (System.Exception ex)
        {
            LogError("Harmony patching failed (console still registered): " + ex.Message);
        }

        RegisterReflexCommands("Awake-post");
        // BuildStamp = short git sha + build time; lets a shipped DLL be identified
        // from Player.log alone (the mod version itself never changes).
        LogInfo($"NPC Labor {ModInfo.Version} (build {BuildStamp.Id}): slice A co-craft + B processor + D dungeon dispatch + E town labor ready.");
        LaborConfig.EnsureLoaded();
        SpecialRewardConfig.EnsureLoaded();
    }

    private void Start()
    {
        // After EModdingKit.Start -> CommandRegistry.Init(), force one more scan.
        // Init rebuilds from assemblies list; if we only Add'ed earlier we are fine,
        // but if Init order was weird, this guarantees bare NpcLabor* commands exist.
        _reflexRegistered = false;
        RegisterReflexCommands("Start");
    }

    private void Update()
    {
        // The mod list is not built when Awake runs, so the config delegate is attached
        // lazily. Both calls are a bool check on the hot path once they are done.
        HookModConfig();
        LaborConfig.TickAutoSave();

        // Row faces are painted a few per frame so a 96-person picker does not spend
        // ten seconds decoding sprites on the frame it opens.
        NpcLabor.CoCraft.PersonPickerUi.DrainFaces();

        // A picker dismissal owns its click until the player starts the next one: until
        // then, a press on the craft button cannot be one the player meant.
        NpcLabor.CoCraft.PersonPickerUi.TickStrayCraftGuard();
    }

    /// <summary>
    /// Hand our config tab to the package row. Silently retries every frame until the
    /// mod list exists: ModManager builds its packages during game boot, well after
    /// BepInEx calls Awake, and ModPackage.onBuildConfig must be non-null before the
    /// mod list is drawn or the row renders without a config button.
    /// </summary>
    void HookModConfig()
    {
        if (_configHooked)
        {
            return;
        }

        try
        {
            ModPackage? pkg = FindOwnPackage();
            if (pkg == null)
            {
                return;
            }

            pkg.onBuildConfig = OnBuildConfig;
            pkg.onResetConfig = ModConfigUi.Reset;
            pkg.configPath = LaborConfig.ConfigPath;
            _configHooked = true;
            LogInfo("ModConfig tab attached to package " + pkg.id);
        }
        catch (Exception ex)
        {
            // Keep retrying - a failure here is usually transient (model not ready yet).
            // Warn once so a permanent failure is visible without flooding the log.
            if (!_configHookWarned)
            {
                _configHookWarned = true;
                LogWarn("ModConfig tab hook failed: " + ex.Message);
            }
        }
    }

    static ModPackage? FindOwnPackage()
    {
        // Preferred: the official lookup, which is authoritative once packages are mapped.
        try
        {
            ModPackage? byId = ModUtil.GetModPackage("mctrr.npclabor");
            if (byId != null)
            {
                return byId;
            }
        }
        catch
        {
        }

        // Fallback: scan the live package list by id, then by our own folder name, so a
        // renamed folder or a stale id mapping cannot leave the tab unattached.
        try
        {
            List<BaseModPackage>? all = EClass.core?.mods?.packages;
            if (all == null)
            {
                return null;
            }

            foreach (BaseModPackage p in all)
            {
                if (p is ModPackage mp && mp.id == "mctrr.npclabor")
                {
                    return mp;
                }
            }

            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            foreach (BaseModPackage p in all)
            {
                if (p is ModPackage mp && mp.dirInfo != null && dir.Length > 0
                    && string.Equals(mp.dirInfo.FullName.TrimEnd('\\', '/'), dir.TrimEnd('\\', '/'),
                                     StringComparison.OrdinalIgnoreCase))
                {
                    return mp;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private void OnDestroy()
    {
        // Each step is isolated: this runs while the game is tearing itself down
        // (ApplicationQuit), so any single call can throw on an already-null EClass
        // and a bare sequence would silently skip every step after it — including
        // the config flush at the end, which is the one that must not be lost.
        RunCleanup("UnpatchSelf", () => Harmony?.UnpatchSelf());
        Harmony = null;

        RunCleanup("CoCraftSession.Clear", () => CoCraft.CoCraftSession.Clear("plugin-destroy"));
        RunCleanup("ProcessorJobSession.ClearAll", () => ProcessorJobSession.ClearAll("plugin-destroy", announce: false));
        RunCleanup("DungeonDispatchManager.ClearAllRuntime", () => DungeonDispatchManager.ClearAllRuntime());
        RunCleanup("TownLaborManager.ClearAllRuntime", () => TownLaborManager.ClearAllRuntime());
        RunCleanup("CraftEngine.Invalidate", () => Craft.CraftEngine.Invalidate());

        // Drop the config delegates so a hot reload cannot call into an unloaded assembly,
        // and flush any save a throttled slider drag still had pending.
        try
        {
            ModPackage? pkg = FindOwnPackage();
            if (pkg != null)
            {
                pkg.onBuildConfig = null;
                pkg.onResetConfig = null;
            }
        }
        catch
        {
        }

        RunCleanup("LaborConfig.Save", () => LaborConfig.Save(immediate: true));
    }

    /// <summary>
    /// OnDestroy step that must not abort the remaining steps when the world is
    /// already gone. Logs instead of throwing so the teardown list always completes.
    /// </summary>
    static void RunCleanup(string step, Action action)
    {
        try
        {
            action();
        }
        catch (System.Exception ex)
        {
            LogWarn("OnDestroy " + step + " failed: " + ex.GetType().Name + " " + ex.Message);
        }
    }

    internal static void RegisterReflexCommands(string phase)
    {
        try
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            if (!CommandRegistry.assemblies.Contains(asm))
            {
                CommandRegistry.assemblies.Add(asm);
                _reflexRegistered = false;
            }

            // Always rebuild once after we are sure our assembly is listed.
            // Safe to call multiple times; BCS only Adds, but Init may have already run.
            if (!_reflexRegistered)
            {
                CommandRegistry.Rebuild();
                _reflexRegistered = true;
                LogInfo("Reflex commands registered (" + phase + "): NpcLaborHarvest / NpcLaborComplete / NpcLaborCompleteOne / NpcLaborPatches");
            }
        }
        catch (System.Exception ex)
        {
            _reflexRegistered = false;
            LogWarn("Reflex CommandRegistry register failed (" + phase + "): " + ex.Message);
        }
    }

    internal static void LogDebug(object message, [CallerMemberName] string caller = "")
        => Instance?.Logger.LogDebug($"[{caller}] {message}");

    internal static void LogInfo(object message)
        => Instance?.Logger.LogInfo(message);

    internal static void LogWarn(object message)
        => Instance?.Logger.LogWarning(message);

    internal static void LogError(object message)
        => Instance?.Logger.LogError(message);
}
