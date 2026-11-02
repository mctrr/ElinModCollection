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
/// （<c>ACS.MoveHysteresis</c> / <c>ACS.PortraitLayout</c> / <c>ACS.SpriteCache</c>），
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
    private const string SecPcc = "主角贴图";
    private const string SecMove = "移动动画";
    private const string SecPortrait = "对话立绘";
    private const string SecCache = "贴图缓存";
    private const string SecHot = "热重载";
    private const string SecDiag = "诊断";
    private const string SecPrewarm = "场景预热";

    private static ConfigEntry<float>? _pcYAdjust;
    private static ConfigEntry<float>? _moveHysteresis;
    private static ConfigEntry<bool>? _fullPortrait;
    private static ConfigEntry<bool>? _subtitleMode;
    private static ConfigEntry<float>? _subtitleHeight;
    private static ConfigEntry<float>? _subtitleAlpha;
    private static ConfigEntry<float>? _subtitleGradTop;
    private static ConfigEntry<float>? _subtitleGradStart;
    private static ConfigEntry<bool>? _subtitleDrag;
    private static ConfigEntry<float>? _heightRatio;
    private static ConfigEntry<float>? _offsetX;
    private static ConfigEntry<float>? _offsetY;
    private static ConfigEntry<bool>? _anchorRight;
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

        var fullPortrait = cfg.Bind(
            SecPortrait,
            "全身立绘兜底",
            true,
            new ConfigDescription(
                "对话时显示 mod 提供的 -full 全身立绘（补原版缺的 enableFull / imageFull 配置）。"
                + "关掉 = 完全走原版行为，可用于确认原版自己能不能显示。"));

        var subtitleMode = cfg.Bind(
            SecPortrait,
            "透明对话框(字幕模式)",
            false,
            new ConfigDescription(
                "把原版对话框改成屏幕底部的半透明字幕条：名字、正文、选项全部搬到条形衬底里，"
                + "全身立绘不再被对话框挡住。**只改布局不改逻辑** —— 打字机、选项点击、数字快捷键、"
                + "取消跳转全部照常。关掉（默认）= 完全走原版对话框。"
                + "注意：原版每次说话都会重建对话布局，本项开启后每帧纠偏。"));
        var subtitleHeight = cfg.Bind(
            SecPortrait,
            "字幕条高度(屏高比例)",
            DialogSubtitleLayout.HeightRatio,
            new ConfigDescription(
                "底部半透明衬底的高度 = 屏幕高度 × 该比例。文字放不下就调大。",
                new AcceptableValueRange<float>(0.08f, 0.6f)));

        var subtitleAlpha = cfg.Bind(
            SecPortrait,
            "字幕条不透明度",
            DialogSubtitleLayout.BackdropAlpha,
            new ConfigDescription(
                "衬底最底部（最黑处）的不透明度。0 = 全透明，1 = 纯黑。默认留一点保证文字可读。",
                new AcceptableValueRange<float>(0f, 1f)));

        var subtitleGradTop = cfg.Bind(
            SecPortrait,
            "字幕条顶部不透明度",
            DialogSubtitleLayout.GradientTopAlpha,
            new ConfigDescription(
                "衬底顶边的不透明度，用来做向上渐隐。0 = 顶边完全透明（默认，过渡自然），"
                + "1 = 整条实心（等于没有渐变）。",
                new AcceptableValueRange<float>(0f, 1f)));

        var subtitleGradStart = cfg.Bind(
            SecPortrait,
            "字幕条渐变起点",
            DialogSubtitleLayout.GradientStart,
            new ConfigDescription(
                "从衬底顶边往下多少比例开始渐入。0 = 从最顶就开始（默认），0.35 = 上面 35% 全透明、"
                + "下面 65% 渐入到最黑。调大 = 透明区更高。",
                new AcceptableValueRange<float>(0f, 0.9f)));

        var subtitleDrag = cfg.Bind(
            SecPortrait,
            "字幕布局调整(可直接拖动)",
            false,
            new ConfigDescription(
                "打开后，字幕条上的「选项块」和「好感块」可以直接用鼠标拖着摆位置，"
                + "拖动结果会记下来（换分辨率也不丢，存的是相对默认位置的偏移）。"
                + "**默认关闭 —— 不调整布局时请保持关闭**，否则这两块会吃掉射线检测。"
                + "命令 ACS.Subtitle drag 可临时开关，ACS.Subtitle resetlayout 复位。"));

        var heightRatio = cfg.Bind(            SecPortrait,
            "立绘高度(屏高比例)",
            FullPortraitPatch.DefaultHeightRatio,
            new ConfigDescription(
                "全身立绘高度 = 屏幕高度 × 该比例。改完重新打开对话生效。",
                new AcceptableValueRange<float>(0.1f, 2f)));

        var offsetX = cfg.Bind(
            SecPortrait,
            "立绘水平偏移(像素)",
            FullPortraitPatch.DefaultOffsetX,
            new ConfigDescription(
                "距离屏幕左边缘（勾选右下角对齐时为右边缘）的像素距离。",
                new AcceptableValueRange<float>(-2000f, 2000f)));

        var offsetY = cfg.Bind(
            SecPortrait,
            "立绘垂直偏移(像素)",
            FullPortraitPatch.DefaultOffsetY,
            new ConfigDescription(
                "距屏幕底边。负值 = 沉到屏幕外，正值 = 抬起。",
                new AcceptableValueRange<float>(-2000f, 2000f)));

        var anchorRight = cfg.Bind(
            SecPortrait,
            "立绘右下角对齐",
            false,
            new ConfigDescription("勾选 = 贴屏幕右下角；不勾 = 贴左下角（默认）。改完重新打开对话生效。"));

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
                "监视贴图目录，在游戏外（Aseprite / 资源管理器 / 导出脚本）改完 PNG 后，"
                + "游戏内会自动重新读盘解码，角色下次进画面就生效。改完有 1.5 秒防抖，"
                + "等导出工具把文件写完再动手。只处理「改已有图」；新增/删除/改名要敲 ACS.ReloadAcs。"));

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
            new ConfigDescription("NPC 的默认立绘（<id>.png）。关掉 = 首次见到该 NPC 时现解一次。"));

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
        Watch(pcYAdjust);
        Watch(moveHysteresis);
        Watch(fullPortrait);
        Watch(subtitleMode);
        Watch(subtitleHeight);
        Watch(subtitleAlpha);
        Watch(subtitleGradTop);
        Watch(subtitleGradStart);
        Watch(subtitleDrag);
        Watch(heightRatio);
        Watch(offsetX);
        Watch(offsetY);
        Watch(anchorRight);
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

        _pcYAdjust = pcYAdjust;
        _moveHysteresis = moveHysteresis;
        _fullPortrait = fullPortrait;
        _subtitleMode = subtitleMode;
        _subtitleHeight = subtitleHeight;
        _subtitleAlpha = subtitleAlpha;
        _subtitleGradTop = subtitleGradTop;
        _subtitleGradStart = subtitleGradStart;
        _subtitleDrag = subtitleDrag;
        _heightRatio = heightRatio;
        _offsetX = offsetX;
        _offsetY = offsetY;
        _anchorRight = anchorRight;
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
        PlayState.NpcMoveHysteresis = _moveHysteresis?.Value ?? PlayState.DefaultNpcMoveHysteresis;
        AcsSpritePresenter.PcYAdjust = _pcYAdjust?.Value ?? 0f;
        FullPortraitPatch.Enabled = _fullPortrait?.Value ?? true;
        DialogSubtitleLayout.SetEnabled((_subtitleMode?.Value ?? false)
            || (_subtitleDrag?.Value ?? false));
        DialogSubtitleLayout.SetEditMode(_subtitleDrag?.Value ?? false);
        DialogSubtitleLayout.HeightRatio = _subtitleHeight?.Value ?? 0.22f;
        DialogSubtitleLayout.BackdropAlpha = _subtitleAlpha?.Value ?? 0.62f;
        DialogSubtitleLayout.GradientTopAlpha = _subtitleGradTop?.Value ?? 0f;
        DialogSubtitleLayout.GradientStart = _subtitleGradStart?.Value ?? 0f;
        FullPortraitPatch.HeightRatio = _heightRatio?.Value ?? FullPortraitPatch.DefaultHeightRatio;
        FullPortraitPatch.OffsetX = _offsetX?.Value ?? FullPortraitPatch.DefaultOffsetX;
        FullPortraitPatch.OffsetY = _offsetY?.Value ?? FullPortraitPatch.DefaultOffsetY;
        FullPortraitPatch.AnchorRight = _anchorRight?.Value ?? false;
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

        // 字幕模式的位置每帧现算，参数改动下一帧自然生效，不需要额外刷新。

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


    internal static void SetFullPortrait(bool on) => Set(_fullPortrait, on);

    internal static void SetSubtitleMode(bool on) => Set(_subtitleMode, on);

    internal static void SetSubtitleHeight(float ratio) => Set(_subtitleHeight, ratio);

    internal static void SetSubtitleAlpha(float alpha) => Set(_subtitleAlpha, alpha);

    internal static void SetSubtitleGradTop(float a) => Set(_subtitleGradTop, a);

    internal static void SetSubtitleGradStart(float r) => Set(_subtitleGradStart, r);

    /// <summary>切「布局调整」（<c>ACS.Subtitle drag</c>）。</summary>
    internal static void SetSubtitleDrag(bool on) => Set(_subtitleDrag, on);

    /// <summary>
    /// 一次性套用一套字幕参数（含打开字幕模式）。
    /// 单独走 <see cref="Set"/> 会写四次配置文件并触发四轮 Apply；这里合并成「改完 → 存一次 → Apply 一次」。
    /// </summary>
    internal static void SetSubtitlePreset(float height, float alpha, float gradTop, float gradStart)
    {
        ConfigFile? file = null;
        bool dirty = Assign(_subtitleMode, true, ref file);
        dirty |= Assign(_subtitleHeight, height, ref file);
        dirty |= Assign(_subtitleAlpha, alpha, ref file);
        dirty |= Assign(_subtitleGradTop, gradTop, ref file);
        dirty |= Assign(_subtitleGradStart, gradStart, ref file);

        if (dirty && file is not null && !file.SaveOnConfigSet) {
            file.Save();
        }

        // 值没变时 BepInEx 不会触发 SettingChanged，统一兜一次；幂等。
        Apply();
    }

    private static bool Assign<T>(ConfigEntry<T>? entry, T value, ref ConfigFile? file)
    {
        if (entry is null || Equals(entry.Value, value)) {
            return entry is not null;
        }

        entry.Value = value;
        file = entry.ConfigFile;
        return true;
    }

    internal static void SetHeightRatio(float ratio) => Set(_heightRatio, ratio);

    internal static void SetOffsetX(float px) => Set(_offsetX, px);

    internal static void SetOffsetY(float px) => Set(_offsetY, px);

    internal static void SetAnchorRight(bool right) => Set(_anchorRight, right);

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
