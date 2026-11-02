using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace Warfog;

public static class ModInfo
{
    public const string Guid = "mctrr.warfog";
    public const string Name = "战争迷雾";
    public const string Version = "1.0.0";
}

[BepInPlugin(ModInfo.Guid, ModInfo.Name, ModInfo.Version)]
internal class Plugin : BaseUnityPlugin
{
    internal static Plugin Instance = null!;
    internal ConfigEntry<bool> Enabled = null!;
    internal ConfigEntry<bool> VerboseLog = null!;

    internal static bool FogEnabled => Instance.Enabled.Value;

    internal static bool VerboseLogEnabled => Instance.VerboseLog.Value;

    private void Awake()
    {
        Instance = this;

        Enabled = Config.Bind("General", "Enabled", true, "世界地图战争迷雾（视野 2 格，探索过的区域永久保留）");
        VerboseLog = Config.Bind("Debug", "VerboseLog", false, "在日志里打印迷雾遮罩的排序层调查结果（排查问题用）");
        Enabled.SettingChanged += OnEnabledChanged;

        Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), ModInfo.Guid);
        LogInfo($"{ModInfo.Name} {ModInfo.Version} loaded.");
    }

    private static void OnEnabledChanged(object sender, System.EventArgs e)
    {
        WorldMapFog.OnEnabledChanged();
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
