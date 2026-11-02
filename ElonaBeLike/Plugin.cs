using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace ElonaBeLike;

public static class ModInfo
{
    public const string Guid = "elonabelike.elonabelike";
    public const string Name = "Elona Be Like";
    public const string Version = "2.2.0";
}

[BepInPlugin(ModInfo.Guid, ModInfo.Name, ModInfo.Version)]
internal class Plugin : BaseUnityPlugin
{
    internal static Plugin Instance = null!;
    internal ConfigEntry<bool> ShowAllies = null!;
    internal ConfigEntry<bool> ShowEnemies = null!;
    internal ConfigEntry<bool> VerboseLog = null!;

    internal static bool ShowAlliesEnabled => Instance.ShowAllies.Value;

    internal static bool ShowEnemiesEnabled => Instance.ShowEnemies.Value;

    internal static bool VerboseLogEnabled => Instance.VerboseLog.Value;

    private void Awake()
    {
        Instance = this;
        ShowAllies = Config.Bind("Minimap", "ShowAllies", true, "在小地图上显示队友");
        ShowEnemies = Config.Bind("Minimap", "ShowEnemies", false, "在小地图上显示敌人");
        VerboseLog = Config.Bind("Debug", "VerboseLog", false, "在日志里打印踢树等逐步细节（排查问题用）");
        ShowAllies.SettingChanged += OnMarkerSettingChanged;
        ShowEnemies.SettingChanged += OnMarkerSettingChanged;

        Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), ModInfo.Guid);
        LogInfo($"{ModInfo.Name} {ModInfo.Version} loaded.");
    }

    private static void OnMarkerSettingChanged(object sender, System.EventArgs e)
    {
        if ((bool)WidgetMinimap.Instance)
        {
            WidgetMinimap.Instance.RefreshMarkers();
        }
    }

    internal static void LogDebug(object message, [CallerMemberName] string caller = "")
    {
        Instance.Logger.LogDebug($"[{caller}] {message}");
    }

    internal static void LogInfo(object message)
    {
        Instance.Logger.LogInfo(message);
    }

    /// <summary>逐步细节日志：只有模组设置里把 Debug/VerboseLog 打开才输出，默认完全静默。</summary>
    internal static void LogVerbose(object message)
    {
        if (Instance.VerboseLog.Value)
        {
            Instance.Logger.LogInfo(message);
        }
    }

    internal static void LogWarn(object message)
    {
        Instance.Logger.LogWarning(message);
    }

    internal static void LogError(object message)
    {
        Instance.Logger.LogError(message);
    }
}
