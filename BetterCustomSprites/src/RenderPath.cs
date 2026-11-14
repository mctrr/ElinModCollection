namespace ACS;

/// <summary>
/// 这次启动走哪套渲染路径。两套实现都编在同一个 DLL 里，靠 HarmonyPrepare 二选一：
///
/// <list type="bullet">
/// <item><c>RenderFramePatch</c> —— 夜间版：本体 23.359.0+ 的 SpriteData 路径。</item>
/// <item><c>LegacyRenderFramePatch</c> —— 稳定版：2.5.0 的 IL 注入路径，逐字恢复。</item>
/// </list>
///
/// 判定顺序：
/// <list type="number">
/// <item>本体缺少 23.359.0 的 API → <b>强制稳定版</b>。没得选，走夜间版就是
/// <c>MissingFieldException</c>，玩家进不去存档（2.6.0 的真实事故）。</item>
/// <item>否则看配置页开关 <see cref="ForceLegacy"/>：默认关 = 用夜间版；勾上 = 回稳定版。</item>
/// </list>
///
/// 结果只在启动时定一次：两套是**不同的补丁类**，补丁注册后再改就来不及了，
/// 所以配置页改完必须重开游戏才生效。
/// </summary>
internal static class RenderPath
{
    /// <summary>配置页开关的镜像，只由 <see cref="BcsConfig.Apply"/> 写。</summary>
    internal static bool ForceLegacy;

    private static bool _resolved;
    private static bool _useSpriteData;

    /// <summary>true = 用夜间版（<c>RenderFramePatch</c>）。补丁类的 Prepare 门禁读它。</summary>
    internal static bool UseSpriteData
    {
        get
        {
            EnsureResolved();
            return _useSpriteData;
        }
    }

    /// <summary>给启动日志用的一句话：走哪条、为什么。</summary>
    internal static string Describe()
    {
        EnsureResolved();

        string why;
        if (!ApiProbe.HasSpriteDataApi) {
            // 本体版本不足时开关无意义，说清楚免得玩家以为开关坏了。
            why = ForceLegacy
                ? "稳定版（配置要求 + 本体低于 23.359.0）"
                : "稳定版（本体低于 23.359.0，自动回退）";
        }
        else {
            why = ForceLegacy ? "稳定版（配置页手动指定）" : "夜间版（本体 23.359.0+）";
        }

        return "[BCS] 渲染路径：" + why + "。";
    }

    private static void EnsureResolved()
    {
        if (_resolved) {
            return;
        }

        // 先置位再算，防止间接回头访问本类时无限递归。
        _resolved = true;

        try {
            _useSpriteData = ApiProbe.HasSpriteDataApi && !ForceLegacy;
        }
        catch (System.Exception) {
            // 意外时按探测结果走：ApiProbe 自带 try/catch，不会抛。
            // 不能一律退稳定版 —— 稳定版在新本体上会因为 transpiler 锚点失配而崩。
            _useSpriteData = ApiProbe.HasSpriteDataApi;
        }
    }
}
