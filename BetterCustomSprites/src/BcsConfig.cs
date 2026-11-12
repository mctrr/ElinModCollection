using BepInEx.Configuration;
using ACS.API;
using ACS.Frame;
using ACS.Patches;

namespace ACS;

/// <summary>
/// BCS 设置项：把可调参数交给原版模组配置页绘制（EA 23.350+ 的 ModConfig GUI）。
///
/// 背景：原版 <c>EModding.ModConfig.RegisterAll</c> 只收录「有 ConfigEntry 的插件」
/// （<c>plugin.Config.Count &gt; 0</c>）。BCS 之前把可调项全放在 ReflexCLI 控制台
/// （<c>ACS.MoveHysteresis</c> / <c>ACS.SpriteCache</c> / <c>ACS.HotReload</c>），
/// 普通玩家根本不会知道它们存在。这里绑定 ConfigEntry 后，配置页会自动出现这些控件：
/// bool → 开关、带范围的数字 → 滑杆，每行右侧自带「复位」按钮。
///
/// <b>单一真值</b>：值住在 ConfigEntry 里（自动持久化到 BepInEx 配置）。
/// 各 Patch 里的 static 字段只是缓存，且<b>只允许 <see cref="Apply"/> 写</b>。
/// 无论是 GUI 拖动、控制台命令还是手改配置文件，走的都是
/// <c>SettingChanged → Apply</c> 同一条路径，所以三者不会互相覆盖。
/// </summary>
internal static class BcsConfig
{
    // 分组标题。ModConfig 用 Definition.Section 分组，顺序 = 下面 Bind 的先后顺序。
    // 标题与键名都直接进配置页（走 Lang.Get，缺语言键时原样返回），所以这里写中文。
    private const string SecRender = "渲染路径";
    private const string SecPcc = "主角贴图";
    private const string SecMove = "移动动画";
    private const string SecCache = "贴图缓存";
    private const string SecHot = "热重载";
    private const string SecDiag = "诊断";
    private const string SecPrewarm = "场景预热";

    private static ConfigEntry<bool>? _useLegacyPath;
    private static ConfigEntry<float>? _pcYAdjust;
    private static ConfigEntry<float>? _moveHysteresis;
    private static ConfigEntry<bool>? _cacheEnabled;
    private static ConfigEntry<float>? _cacheGrace;
    private static ConfigEntry<float>? _cacheMemory;
    private static ConfigEntry<bool>? _keepOnRebuild;
    private static ConfigEntry<bool>? _hotReload;
    private static ConfigEntry<bool>? _diagnosticLog;
    private static ConfigEntry<bool>? _warmBase;
    private static ConfigEntry<bool>? _warmIdle;
    private static ConfigEntry<bool>? _warmGreet;
    private static ConfigEntry<bool>? _warmMove;
    private static ConfigEntry<bool>? _warmAtk;
    private static ConfigEntry<bool>? _warmCombat;
    private static ConfigEntry<bool>? _warmHurt;
    private static ConfigEntry<bool>? _warmRevive;
    private static ConfigEntry<bool>? _warmEat;
    private static ConfigEntry<bool>? _warmCmove;

    /// <summary>绑定设置项。在 <see cref="AcsMod.Awake"/> 里调用一次。</summary>
    internal static void Init(ConfigFile cfg)
    {
        // 放在第一个绑定 = 配置页最上面一行。出问题时玩家一眼能找到这个开关。
        var useLegacyPath = cfg.Bind(
            SecRender,
            "使用稳定版渲染路径",
            false,
            new ConfigDescription(
                "默认关（夜间版）：游戏 23.359.0 及以上用本体新的 SpriteData 接口绘制，"
                + "由本体统一切贴图与推进帧。"
                + "勾上 = 退回 2.5.0 的旧渲染路径（自建 IL 注入 + 自建帧推进），"
                + "只在新版路径在你的机器上出问题时才需要。"
                + "游戏低于 23.359.0 时本项不生效，会自动使用旧路径。"
                + "**改完需要重开游戏才生效（补丁在启动时注册）。**"));

        var pcYAdjust = cfg.Bind(
            SecPcc,
            "垂直微调",
            0f,
            new ConfigDescription(
                "PC 使用自定义皮肤时的额外垂直偏移（世界单位）。0 = 与 NPC 完全一致，贴地由素材 .pref 的 offset.y 决定。"
                + "只在你的图仍然偏高/偏低时微调：正数上抬、负数下沉。"
                + "BCS 不再自动加任何抬升补偿（那会导致 PC 浮空）。"));

        var moveHysteresis = cfg.Bind(
            SecMove,
            "移动粘滞(秒)",
            PlayState.DefaultNpcMoveHysteresis,
            new ConfigDescription(
                "NPC 停下后仍按「移动中」处理的时长。调大 = 跟队更平滑，但停稳后会原地空走一段；"
                + "调小 = 更灵敏，但走一步一停会让移动动画反复从第 0 帧重播。0.2 秒为实测甜点。",
                new AcceptableValueRange<float>(0f, 2f)));

        var cacheEnabled = cfg.Bind(
            SecCache,
            "按需释放贴图",
            true,
            new ConfigDescription(
                "角色不再用到某张皮肤贴图时把它交还显存，避免长局显存单调累积。"
                + "关掉 = 原版行为（渲染到才载入，但永不释放）。"));

        var cacheGrace = cfg.Bind(
            SecCache,
            "释放宽限期(秒)",
            SpriteCacheEvict.DefaultGraceSeconds,
            new ConfigDescription(
                "角色不再使用某张图后，仍保留多久才释放。调小 → 走回头路 / 反复切图会重新解码，可能卡顿；"
                + "调大 → 回收不及时。",
                new AcceptableValueRange<float>(1f, 600f)));

        var cacheMemory = cfg.Bind(
            SecCache,
            "显存上限(MB)",
            (float)SpriteCacheEvict.DefaultMemoryBudgetMb,
            new ConfigDescription(
                "BCS 缓存的皮肤贴图总量上限。超过时按「最久没用到」顺序释放一半；"
                + "正在用的（当前地图上的角色）永不逐出。0 = 不限（只按宽限期回收）。"
                + "高清皮肤一屏常驻就可能 1.2~1.5GB，16GB 内存的机器给 2048 比较合适；"
                + "调小 = 省显存但更容易反复解码（卡顿），调大 = 更稳但吃内存。",
                new AcceptableValueRange<float>(0f, 8192f)));

        var keepOnRebuild = cfg.Bind(
            SecCache,
            "贴图跨重建复用",
            true,
            new ConfigDescription(
                "游戏重建皮肤表时（读档 / 切区域 / 开换肤界面），把已经解码好的贴图直接挂到新对象上，"
                + "不再重新读盘解码 —— 切区域回来那一下的卡顿就是它。关掉 = 每次重建都重读一遍。"
                + "注意：本项开着时若没开「皮肤热加载」，换了 PNG 仍要敲一次 ACS.ReloadAcs 才能看到新图。"));

        var hotReload = cfg.Bind(
            SecHot,
            "皮肤热加载",
            false,
            new ConfigDescription(
                "监视各包的 Texture 与 Skin 目录，在游戏外（Aseprite / 资源管理器 / 导出脚本）"
                + "改完 PNG、.ini 或 .pref 后，游戏内会自动重新读盘，角色下次进画面就生效。"
                + "改完有 1.5 秒防抖，等导出工具把文件写完再动手。只处理「改已有文件」；"
                + "新增/删除/改名要敲 ACS.ReloadAcs。"
                + "注意：原版切回游戏时只刷新原版贴图，不刷新自定义皮肤 —— 本项是皮肤热更新的唯一通道。"));

        var diagnosticLog = cfg.Bind(
            SecDiag,
            "诊断日志",
            false,
            new ConfigDescription(
                "输出 BCS 的排查用信息（皮肤表枚举、密度注入、分片结果、缓存释放…）"
                + "以及**贴图性能探针**（每张贴图被解码几次、耗时多少）。"
                + "排查卡顿只要这一个开关：打开 → 进游戏走动 → 退游戏，"
                + "退出时自动把性能报表写进 BepInEx/LogOutput.log。"));

        var warmBase = cfg.Bind(
            SecPrewarm,
            "预热底图",
            true,
            new ConfigDescription("NPC 的默认立绘（<id>.png）。关掉 = 首次见到该 NPC 时现解一次。"
                + "注意：本组各项只决定「哪些状态预热」，**任何 ≥2MB 的贴图不论勾选与否都会预热**"
                + "（大图解码最贵，漏一次就是进视野时的一次卡顿）。"));

        var warmIdle = cfg.Bind(
            SecPrewarm,
            "预热idle",
            true,
            new ConfigDescription(
                "_acs_idle 是 ACS【老格式】的 idle 片；新格式不再单独出 idle，idle 就是底图（由「预热底图」负责）。"
                + "本项默认开，覆盖老资源 —— 你包里只有 _acs_idle、没有底图的老皮肤有 "
                + "pcr_chuyin / pcr_jinghua / pcr_kekeluo / pcr_lanfa / pcr_meili / pcr_meimei / pcr_yinlian / pcr_yuan。"
                + "没有状态段的差分包（_acs_yongzhuang / _acs_xn / _acs_xingnian …）是该前缀的旧式 idle，一并归本项。"
                + "只预热「本场景真有角色在用」的前缀（acs_override / snow / married），没在用的一律不预热。"));

        var warmGreet = cfg.Bind(
            SecPrewarm,
            "预热greet",
            true,
            new ConfigDescription("打招呼动画。NPC 一进视野就播，建议常开。"));

        var warmMove = cfg.Bind(
            SecPrewarm,
            "预热move",
            true,
            new ConfigDescription(
                "行走动画。NPC 一动就播，建议常开。"
                + "带前缀的差分片（yongzhuang.move / snow.move …）同属本项 —— 前缀是素材自己起的名字，不是状态。"));

        var warmAtk = cfg.Bind(
            SecPrewarm,
            "预热atk",
            false,
            new ConfigDescription("攻击动画。默认关：只有进战斗才播；不预热则第一次挥击前解码一次。"));

        var warmCombat = cfg.Bind(
            SecPrewarm,
            "预热combat",
            false,
            new ConfigDescription("战斗待机动画。默认关，同上。"));

        var warmHurt = cfg.Bind(
            SecPrewarm,
            "预热hurt",
            false,
            new ConfigDescription("受击动画。默认关。"));

        var warmRevive = cfg.Bind(
            SecPrewarm,
            "预热revive",
            false,
            new ConfigDescription("复活动画。默认关。"));

        var warmEat = cfg.Bind(
            SecPrewarm,
            "预热eat",
            false,
            new ConfigDescription("进食动画。默认关。"));

        var warmCmove = cfg.Bind(
            SecPrewarm,
            "预热cmove",
            false,
            new ConfigDescription("战斗中的移动动画。默认关。"));

        // 全部绑定完再挂事件，避免绑定过程中触发一遍 Apply。
        Watch(useLegacyPath);
        Watch(pcYAdjust);
        Watch(moveHysteresis);
        Watch(cacheEnabled);
        Watch(cacheGrace);
        Watch(cacheMemory);
        Watch(keepOnRebuild);
        Watch(hotReload);
        Watch(diagnosticLog);
        Watch(warmBase);
        Watch(warmIdle);
        Watch(warmGreet);
        Watch(warmMove);
        Watch(warmAtk);
        Watch(warmCombat);
        Watch(warmHurt);
        Watch(warmRevive);
        Watch(warmEat);
        Watch(warmCmove);

        _useLegacyPath = useLegacyPath;
        _pcYAdjust = pcYAdjust;
        _moveHysteresis = moveHysteresis;
        _cacheEnabled = cacheEnabled;
        _cacheGrace = cacheGrace;
        _cacheMemory = cacheMemory;
        _keepOnRebuild = keepOnRebuild;
        _hotReload = hotReload;
        _diagnosticLog = diagnosticLog;
        _warmBase = warmBase;
        _warmIdle = warmIdle;
        _warmGreet = warmGreet;
        _warmMove = warmMove;
        _warmAtk = warmAtk;
        _warmCombat = warmCombat;
        _warmHurt = warmHurt;
        _warmRevive = warmRevive;
        _warmEat = warmEat;
        _warmCmove = warmCmove;

        // 用配置文件里的值覆盖各字段的初始默认值（配置文件可能已被玩家改过）。
        Apply();
    }

    /// <summary>
    /// 把 ConfigEntry 的值推到各 Patch 的静态字段。幂等，可在任何时机重复调用。
    /// 未初始化（<see cref="Init"/> 之前）时全部落回各自的内置默认值。
    /// </summary>
    internal static void Apply()
    {
        // 渲染路径只在启动时读一次（补丁类注册后就换不了了），这里只更新镜像；
        // 玩家改完配置页要重开游戏才生效 —— 配置项说明里已写明。
        RenderPath.ForceLegacy = _useLegacyPath?.Value ?? false;

        PlayState.NpcMoveHysteresis = _moveHysteresis?.Value ?? PlayState.DefaultNpcMoveHysteresis;
        AcsSpritePresenter.PcYAdjust = _pcYAdjust?.Value ?? 0f;
        SpriteCacheEvict.Enabled = _cacheEnabled?.Value ?? true;
        SpriteCacheEvict.GraceSeconds = _cacheGrace?.Value ?? SpriteCacheEvict.DefaultGraceSeconds;
        SpriteCacheEvict.MemoryBudgetMb = (int)(_cacheMemory?.Value ?? SpriteCacheEvict.DefaultMemoryBudgetMb);
        SkinTextureReuse.Enabled = _keepOnRebuild?.Value ?? true;

        GreetPrewarm.WarmBase = _warmBase?.Value ?? true;
        GreetPrewarm.WarmIdle = _warmIdle?.Value ?? true;
        GreetPrewarm.WarmGreet = _warmGreet?.Value ?? true;
        GreetPrewarm.WarmMove = _warmMove?.Value ?? true;
        GreetPrewarm.WarmAtk = _warmAtk?.Value ?? false;
        GreetPrewarm.WarmCombat = _warmCombat?.Value ?? false;
        GreetPrewarm.WarmHurt = _warmHurt?.Value ?? false;
        GreetPrewarm.WarmRevive = _warmRevive?.Value ?? false;
        GreetPrewarm.WarmEat = _warmEat?.Value ?? false;
        GreetPrewarm.WarmCmove = _warmCmove?.Value ?? false;
        SkinHotReload.Enabled = _hotReload?.Value ?? false;

        // 监视器是真资源（FileSystemWatcher 句柄），跟着开关启停；放在 Apply 末尾保证字段都就位。
        SkinHotReload.Sync();

        // 诊断总开关。只门控 Info，Warn/Error 永远输出。
        AcsMod.DiagnosticLog = _diagnosticLog?.Value ?? false;

        // 探针跟随诊断总开关：<b>只有一个开关</b>。玩家不该为了排障去点两下。
        // 关闭时也一并关掉，否则上次开的探针会在无日志的情况下继续累积（看不见也白占内存）。
        BcsPerf.Enabled = AcsMod.DiagnosticLog;
        PrewarmProbe.Enabled = AcsMod.DiagnosticLog;
        if (!AcsMod.DiagnosticLog) {
            PrewarmProbe.Reset();
        }
    }

    /// <summary>值被改动时（配置页滑杆 / 手改配置文件后重载）同步到各字段。</summary>
    private static void Watch<T>(ConfigEntry<T> entry) => entry.SettingChanged += (sender, args) => Apply();

    // ---------------- 写入口（控制台命令用；GUI 直接改 ConfigEntry） ----------------

    internal static void SetMoveHysteresis(float sec) => Set(_moveHysteresis, sec);

    internal static void SetCacheEnabled(bool on) => Set(_cacheEnabled, on);

    internal static void SetGraceSeconds(float sec) => Set(_cacheGrace, sec);

    internal static void SetKeepOnRebuild(bool on) => Set(_keepOnRebuild, on);

    internal static void SetHotReload(bool on) => Set(_hotReload, on);

    internal static void SetDiagnosticLog(bool on) => Set(_diagnosticLog, on);


    private static void Set<T>(ConfigEntry<T>? entry, T value)
    {
        if (entry is not null && !Equals(entry.Value, value)) {
            entry.Value = value;

            // 插件自己的 ConfigFile 建的时候 SaveOnConfigSet=false（BepInEx 故意关掉自动保存），
            // 不显式 Save 的话改完的值一重启就没了。原版 ModdingKit 也是手动 Save。
            if (!entry.ConfigFile.SaveOnConfigSet) {
                entry.ConfigFile.Save();
            }
        }

        // 值未变化时 BepInEx 不触发 SettingChanged，这里统一兜一次；幂等，重复调无副作用。
        Apply();
    }
}
