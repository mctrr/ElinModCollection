using System;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using ReflexCLI;

[assembly: AssemblyVersion("2.4.0.0")]
[assembly: AssemblyFileVersion("2.4.0.0")]
[assembly: AssemblyInformationalVersion("2.4.0")]
[assembly: AssemblyProduct(ACS.ModInfo.Guid)]
[assembly: AssemblyTitle(ACS.ModInfo.Name)]
[assembly: AssemblyMetadata("Repo", "https://github.com/mctrr/BetterCustomSprites")]

namespace ACS;

internal static class ModInfo
{
    internal const string Guid = "mctrr.bettercustomsprites";
    internal const string Name = "Better Custom Sprites";
    internal const string Version = "2.4.0";
}

[BepInPlugin(ModInfo.Guid, ModInfo.Name, ModInfo.Version)]
internal class AcsMod : BaseUnityPlugin
{
    internal static AcsMod? Instance { get; private set; }

    private void Awake()
    {
        Instance = this;

        var harmony = new Harmony(ModInfo.Guid);
        harmony.PatchAll();

        CommandRegistry.assemblies.Add(Assembly.GetExecutingAssembly());
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
