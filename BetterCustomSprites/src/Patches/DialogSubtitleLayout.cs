using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ACS.Patches;

/// <summary>
/// 对话「电影字幕」模式：原版对话框底板**彻底隐藏**，说话人 / 正文 / 选项 / 好感面板
/// 全部搬到屏幕底部的半透明字幕条上。
///
/// <b>设计原则 —— 只动布局，不动逻辑</b>：
/// 这些组件（<see cref="DialogDrama.textName"/> / <see cref="DialogDrama.textMain"/> /
/// <see cref="DialogDrama.transChoices"/> / <see cref="DialogDrama.goAffinity"/>）本身原样保留
/// —— 对象、事件、打字机、1-9 快捷键、取消跳转全在，只是换了个父节点与位置。
/// 因此原版功能一个都不会坏，这也正是用户要求的「不影响原版对话框的功能」。
///
/// <b>为什么必须换父节点</b>（这是上一版失效的根因，务必不要改回去）：
/// 原版对话面板（实测 <c>DialogDefault</c>）是一个 <b>居中</b> 的 890x575 框，
/// 不是整屏 RectTransform。若只改 <c>rectTransform</c> 而不换父节点，
/// 锚点 <c>(0,0)</c> 指的是「面板左下角」（屏幕中部），
/// 于是 <c>BottomMargin + h</c> 这类「屏幕底边坐标」全部落在屏幕中间 ——
/// 看起来就像没生效。搬到字幕条（真正贴屏底的全宽节点）之下后，坐标才是屏幕坐标。
///
/// <b>渲染层级</b>（从下到上）：
/// <code>
///   原版对话面板（底图 alpha=0，仅剩布局容器，不可见）
///     全身立绘（由 FullPortraitPatch 垫到最底之上）
///       字幕条衬底（自建，半透明黑，raycastTarget=false）
///         说话人 / 正文（原版组件，已搬入）
///       选项区 / 好感面板（原版组件，已搬入，浮在字幕条上方）
/// </code>
///
/// <b>每帧重算，不做快照缓存</b>：原版每次说话都会
/// <c>DramaActor.Talk()</c> → <c>SetText</c> → <c>RebuildLayoutTo</c>，
/// 会把布局冲掉；而面板 prefab 有 5 个变体（DialogDefault / DialogAdv / DialogNerun /
/// DialogMono / DialogMono2），各自的子节点命名与结构都不同。
/// 所以这里每帧从「当前激活的面板」现场推导要动谁，而不是记一份可能过期的清单。
/// 原始位置只在**第一次**动某个节点时记一次（见 <see cref="Origin"/>），可无损还原。
/// </summary>
internal sealed class DialogSubtitleLayout : MonoBehaviour
{
    // ---------------- 配置（由 BcsConfig 推入） ----------------

    /// <summary>总开关。关掉时本组件不做任何事，由 <see cref="SetEnabled"/> 负责还原。</summary>
    internal static bool Enabled;

    /// <summary>字幕条高度 = 屏高 × 该比例。</summary>
    internal static float HeightRatio = 0.22f;

    /// <summary>字幕条黑度（0=全透明，1=纯黑）。留一点保证浅色场景里文字可读。</summary>
    internal static float BackdropAlpha = 0.62f;

    /// <summary>衬底顶边的不透明度：0 = 从纯透明开始渐入（默认）。</summary>
    internal static float GradientTopAlpha = 0f;

    /// <summary>渐变起始位置（衬底高度比例）：0 = 从衬底顶边开始渐入，0.35 = 上面 35% 全透明。</summary>
    internal static float GradientStart = 0f;

    /// <summary>正文与说话人名的强制颜色。默认近白 + 全不透明。</summary>
    internal static Color TextColor = new(0.94f, 0.94f, 0.94f, 1f);

    /// <summary>
    /// 「布局调整」开关：打开后选项块与好感块可以**直接用鼠标拖**，
    /// 拖出来的偏移存在 <see cref="SubtitleLayoutStore"/>（延时落 JSON）。默认关。
    /// </summary>
    internal static bool EditMode;

    // ---------------- 布局常量 ----------------

    private const float BottomMargin = 0f;
    private const float TextPaddingX = 28f;

    /// <summary>
    /// 正文的行距**倍率**。
    ///
    /// 口径（反编译 <c>Plugins.UI!UIText</c> 确认，别再猜）：
    /// <c>public class UIText : Text</c> —— 继承的是 Unity 的 <see cref="Text"/>，
    /// 而 Unity 的 <c>Text.lineSpacing</c> 是「**字体行高的倍率**」，不是像素。
    /// 🔴 上一版这里按「像素」算，推出 80.35 倍 → 每行字高 17×80 = 1360px，
    /// 正文整块被撑爆到容器外（日志里 <c>main h=329 onScreen=False</c> 就是这个），
    /// 撑大的行框还顺带把旁边的块顶出画面。倍率口径下正常值就是 1.0~1.6。
    ///
    /// 为什么必须每帧重写而不是「设一次」：
    /// <see cref="UIText.ApplySkin"/> 会在每次 <c>SetText</c> 时被 <c>DialogDrama</c> 调用，
    /// 重新写回 <c>lineSpacing = orgSpacing * fontSource.lineSpacing</c>（换肤口径，
    /// 可能远大于我们要的密度）。所以这里不协商，每帧盖回去。
    /// </summary>
    private const float TextLineSpacing = 1.28f;

    private const float NameBottomGap = 4f;
    private const float ChoiceGapY = 8f;

    /// <summary>好感块的宽度。它现在搬出字幕条、贴条上沿放左侧（跟立绘同侧）。</summary>
    private const float AffinityWidth = 220f;

    /// <summary>好感块的高度 = 字幕条高度 × 该比例。</summary>
    private const float AffinityHeightRatio = 0.62f;

    /// <summary>选项块的宽度 = 屏宽 × 该比例（再夹一个像素上下限）。</summary>
    private const float ChoiceWidthRatio = 0.44f;

    /// <summary>选项块的高度 = 屏高 × 该比例。</summary>
    private const float ChoiceHeightRatio = 0.24f;

    /// <summary>选项块衬底的黑度。留足避免白字压浅色场景看不清（用户反馈点）。</summary>
    private const float ChoiceBgAlpha = 0.58f;

    /// <summary>
    /// 好感块的衬底：黑度故意比选项块淡 —— 它压在立绘身上，太黑会挡角色。
    /// 同时这块背板也是「布局调整」模式下的**拖动把手**（Text 自己不能承接射线）。
    /// </summary>
    private const float AffinityBgAlpha = 0.30f;

    /// <summary>原版底板的目标不透明度：0 = 彻底看不见。</summary>
    private const float PanelAlpha = 0f;

    /// <summary>
    /// 「底板」判据：面积占屏比 ≥ 该值，且不带文字 / 遮罩、不在立绘与 CG 子树内。
    /// 1.2% 是照着实测树定的：DialogDefault 的 BG(28%)/Frame(28%)/BG (1)(21%) 全部命中，
    /// 而头像框(2.4%)、按钮、图标远在阈值之下（头像框单独处理）。
    /// </summary>
    private const float PanelMinAreaRatio = 0.012f;

    private const string BackdropName = "BCS_SubtitleBackdrop";

    /// <summary>选项块的衬底节点名（<see cref="HideVanillaPanel"/> 靠它把自己排除掉）。</summary>
    private const string ChoiceBoxName = "BCS_ChoiceBox";

    /// <summary>好感块的衬底节点名（顺带当拖动把手）。</summary>
    private const string AffinityBoxName = "BCS_AffinityBox";

    // ---------------- 实例状态 ----------------

    /// <summary>按 InstanceID 记的已挂组件（仅用于还原时遍历）。</summary>
    private static readonly Dictionary<int, DialogSubtitleLayout> Instances = new();

    /// <summary>
    /// 被我们搬过家的节点的**原始**位置，只在首次搬动时记一次。
    /// 用静态表而不是实例表：面板 prefab 会被反复重建，实例可能换代，
    /// 但「原版本来长什么样」是全局唯一的事实。
    /// </summary>
    private static readonly Dictionary<Transform, NodeState> Origin = new();

    /// <summary>被我们改过 alpha 的 Image 的原始 alpha（同样只在首次改时记一次）。</summary>
    private static readonly Dictionary<Image, float> OriginAlpha = new();

    /// <summary>被钉过颜色的 UIText 的原始颜色（只在首次钉时记一次）。</summary>
    private static readonly Dictionary<UIText, Color> OriginTextColor = new();

    /// <summary>被我们强制打开过的 Renderer 的原始开关（只在首次打开时记一次）。</summary>
    private static readonly Dictionary<Component, bool> OriginTextEnabled = new();

    /// <summary>被我们临时停掉的 ContentSizeFitter 的原始开关（选项块接管，见 <see cref="TakeControl"/>）。</summary>
    private static readonly Dictionary<ContentSizeFitter, bool> OriginFitter = new();

    /// <summary>渐变贴图缓存 + 量化键（键不变就复用，不做无谓重建）。</summary>
    private static Sprite? GradientCache;
    private static int GradientKey = -1;

    /// <summary>
    /// 选项块衬底用的 1x1 纯白贴图。
    /// 不给 sprite 让 <see cref="Image"/> 走自带白底在实机上靠不住（不同版本行为不一致），
    /// 这里显式建一张，衬底颜色靠 <c>Image.color</c> 控。
    /// </summary>
    private static Sprite? BoxCache;

    private sealed class NodeState
    {
        public Transform? Parent;
        public int Sibling;
        public Vector2 AnchorMin;
        public Vector2 AnchorMax;
        public Vector2 Pivot;
        public Vector2 Pos;
        public Vector2 Size;
    }

    private DialogDrama? _dialog;
    private Image? _backdrop;
    private Image? _choiceBox;
    private Image? _affBox;
    private UIDragPanel? _dragChoice;
    private UIDragPanel? _dragAffinity;
    private bool _warned;
    private int _movedCount;
    private int _diagSig = -1;

    private readonly List<Image> _panelHits = new();
    private readonly List<Transform> _choiceTargets = new();

    // ---------------- 挂载 / 还原 ----------------

    /// <summary>确保某个对话面板已挂上本组件（幂等：先查组件，再查表，避免重复挂载）。</summary>
    internal static void Attach(DialogDrama dialog)
    {
        if (dialog == null) {
            return;
        }

        try {
            var comp = dialog.gameObject.GetComponent<DialogSubtitleLayout>();
            if (comp == null) {
                comp = dialog.gameObject.AddComponent<DialogSubtitleLayout>();
                AcsMod.Log("[Subtitle] attached to " + dialog.gameObject.name);
            }

            comp._dialog = dialog;
            comp._warned = false;
            Instances[dialog.GetInstanceID()] = comp;
        }
        catch (Exception ex) {
            AcsMod.Warn("[Subtitle] attach failed: " + ex);
        }
    }

    /// <summary>开关切换：关掉时把动过的所有东西一次性还原。</summary>
    internal static void SetEnabled(bool on)
    {
        if (Enabled == on) {
            return;
        }

        Enabled = on;
        if (!on) {
            RestoreAll();
        }
    }

    /// <summary>「布局调整」开关切换。同时会把已有的拖动偏移落一次盘。</summary>
    internal static void SetEditMode(bool on)
    {
        if (EditMode == on) {
            return;
        }

        EditMode = on;
        AcsMod.Log(on
            ? "[Subtitle] edit mode ON: 直接拖动选项块 / 好感块，位置会被记住。"
            : "[Subtitle] edit mode OFF: 已停用拖动并放开点击。");

        if (!on) {
            SubtitleLayoutStore.Save();
        }
    }

    /// <summary>把被接管的原版节点全部写回原位（组件销毁 / 玩家关掉开关时用）。</summary>
    internal static void RestoreAll()
    {
        foreach (var kv in OriginTextEnabled) {
            if (kv.Key != null && kv.Key is Component c) {
                var r = c.GetComponent<Renderer>();
                if (r != null) {
                    r.enabled = kv.Value;
                }
            }
        }

        OriginTextEnabled.Clear();

        foreach (var kv in OriginFitter) {
            if (kv.Key != null) {
                kv.Key.enabled = kv.Value;
            }
        }

        OriginFitter.Clear();

        foreach (var kv in OriginTextColor) {
            if (kv.Key != null) {
                kv.Key.color = kv.Value;
            }
        }

        OriginTextColor.Clear();

        foreach (var kv in OriginAlpha) {
            if (kv.Key != null) {
                SetAlpha(kv.Key, kv.Value);
            }
        }

        OriginAlpha.Clear();

        foreach (var kv in Origin) {
            Transform? t = kv.Key;
            if (t == null) {
                continue;
            }

            NodeState s = kv.Value;
            try {
                if (s.Parent != null && t.parent != s.Parent) {
                    t.SetParent(s.Parent, false);
                }

                if (t is RectTransform rt) {
                    SetRect(rt, s.AnchorMin, s.AnchorMax, s.Pivot, s.Pos, s.Size);
                }

                if (s.Parent != null) {
                    t.SetSiblingIndex(s.Sibling);
                }
            }
            catch (Exception ex) {
                AcsMod.Warn("[Subtitle] restore failed: " + ex.Message);
            }
        }

        Origin.Clear();

        foreach (var kv in Instances) {
            if (kv.Value != null) {
                kv.Value.DestroyBackdrop();
                kv.Value.DestroyChoiceBox();
                kv.Value.DestroyAffinityBox();
            }
        }
    }

    private void OnDestroy()
    {
        Instances.Remove(gameObject.GetInstanceID());
        DestroyBackdrop();
        DestroyChoiceBox();
        DestroyAffinityBox();
    }

    private void DestroyBackdrop()
    {
        if (_backdrop != null) {
            Destroy(_backdrop.gameObject);
        }

        _backdrop = null;
    }

    private void DestroyChoiceBox()
    {
        if (_choiceBox != null) {
            Destroy(_choiceBox.gameObject);
        }

        _choiceBox = null;
        _dragChoice = null;
    }

    private void DestroyAffinityBox()
    {
        if (_affBox != null) {
            Destroy(_affBox.gameObject);
        }

        _affBox = null;
        _dragAffinity = null;
    }

    // ---------------- 每帧 ----------------

    private void Update()
    {
        // 延时落盘放在这里（而不是 Apply 里）：拖动后 0.8s 内就关掉对话也能存下来。
        SubtitleLayoutStore.Tick();

        if (_backdrop == null) {
            return;
        }

        bool show = Enabled && _dialog != null && _dialog.gameObject.activeInHierarchy;
        if (_backdrop.gameObject.activeSelf != show) {
            _backdrop.gameObject.SetActive(show);
        }
    }

    private void LateUpdate()
    {
        if (!Enabled) {
            return;
        }

        try {
            Apply();
        }
        catch (Exception ex) {
            if (!_warned) {
                _warned = true;
                AcsMod.Warn("[Subtitle] apply failed: " + ex);
            }
        }
    }

    private void Apply()
    {
        if (_dialog == null || !_dialog.gameObject.activeInHierarchy) {
            if (_backdrop != null && _backdrop.gameObject.activeSelf) {
                _backdrop.gameObject.SetActive(false);
            }

            return;
        }

        EnsureBackdrop();
        if (_backdrop == null) {
            return;
        }

        float h = Mathf.Max(64f, Screen.height * HeightRatio);
        float w = Screen.width;

        _movedCount = 0;

        // 关掉「布局调整」时必须立刻停掉拖动组件并放开射线 —— 否则它们会一直吃掉点击。
        if (!EditMode) {
            DisableDrags();
        }

        SubtitleLayoutStore.Load();
        SubtitleLayoutStore.Tick();

        LayoutBackdrop(h);

        LayoutTexts(w, h);
        LayoutChoices(w, h);
        LayoutAffinity(w, h);
        LayoutNext();
        HideVanillaPanel();
        HidePortraitBox();
        Diag(h);
    }

    /// <summary>
    /// 自建底部衬底：贴屏幕底边、横贯整屏，**带向上渐隐的渐变**（用户要求「底边做成渐变的」）。
    /// 挂在与立绘同层的节点下，排在最后 → 盖住底板。
    /// </summary>
    private void EnsureBackdrop()
    {
        if (_backdrop != null) {
            if (!_backdrop.gameObject.activeSelf) {
                _backdrop.gameObject.SetActive(true);
            }

            return;
        }

        if (_dialog == null) {
            return;
        }

        Transform host = FindCanvasChild(_dialog.transform);
        var go = new GameObject(BackdropName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(host, false);
        go.transform.SetAsLastSibling();

        var img = go.GetComponent<Image>();
        img.color = Color.white;

        // 渐变由 sprite 的顶点色承担（见 BuildGradientSprite）；Image.color 保持白色不参与调色。
        img.sprite = EnsureGradientSprite();

        // 绝不能吃掉点击 —— 否则「点击推进对话」直接失效。
        img.raycastTarget = false;

        _backdrop = img;
    }

    private void LayoutBackdrop(float h)
    {
        // 左右锚点拉满 + sizeDelta.x=0 → 宽度恒等于父容器宽度，任何分辨率都横贯整屏。
        SetRect(_backdrop!.rectTransform,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, BottomMargin), new Vector2(0f, h));

        // 每帧只重建「数字变了」的那一档渐变贴图（值不变时连 sprite 都不碰，零 GC）。
        Sprite want = EnsureGradientSprite();
        if (_backdrop.sprite != want) {
            _backdrop.sprite = want;
        }
    }

    /// <summary>退出「布局调整」：停掉拖动组件并放开射线，保证平时零干扰。</summary>
    private void DisableDrags()
    {
        if (_dragChoice != null && _dragChoice.enable) {
            _dragChoice.enable = false;
        }

        if (_dragAffinity != null && _dragAffinity.enable) {
            _dragAffinity.enable = false;
        }

        if (_choiceBox != null && _choiceBox.raycastTarget) {
            _choiceBox.raycastTarget = false;
        }

        if (_affBox != null && _affBox.raycastTarget) {
            _affBox.raycastTarget = false;
        }
    }

    /// <summary>选项块的半透明衬底（幂等创建，挂在字幕条下、排在最后）。</summary>
    private Image? EnsureChoiceBox()
    {
        if (_choiceBox != null) {
            return _choiceBox;
        }

        if (_backdrop == null) {
            return null;
        }

        var go = new GameObject(ChoiceBoxName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(_backdrop.transform, false);
        go.transform.SetAsLastSibling();

        var img = go.GetComponent<Image>();
        img.color = new Color(0.02f, 0.02f, 0.03f, ChoiceBgAlpha);
        img.sprite = EnsureBoxSprite();

        // 绝不能吃掉点击 —— 否则选项按钮全点不动。
        img.raycastTarget = false;

        _choiceBox = img;
        return img;
    }

    /// <summary>
    /// 当前是不是真的有选项可见。
    /// 不能用「容器存在」判断：5 个 prefab 变体里 <c>transChoices</c> 常驻存在，
    /// 没选项时只是按钮被清空/隐藏 —— 那样衬底会一直杵在屏幕上。
    /// </summary>
    private bool HasVisibleChoice()
    {
        foreach (Transform t in _choiceTargets) {
            if (t != null && AnyActiveButton(t)) {
                return true;
            }
        }

        return false;
    }

    private static bool AnyActiveButton(Transform t)
    {
        var b = t.GetComponent<Button>();
        if (b != null && b.gameObject.activeInHierarchy) {
            return true;
        }

        for (int i = 0; i < t.childCount; i++) {
            Transform c = t.GetChild(i);
            if (c != null && AnyActiveButton(c)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 接管某个节点的**尺寸控制权**：临时停掉它身上的 ContentSizeFitter。
    /// 我们每帧写 sizeDelta，fitter 每帧按内容再写回去 → 两边打架（下一行rst疗效各不相同）。
    /// 只停这一个节点上的 fitter，不动子节点；原始开关记账，可无损还原。
    /// </summary>
    private static void TakeControl(Transform t)
    {
        if (t == null) {
            return;
        }

        var f = t.GetComponent<ContentSizeFitter>();
        if (f == null || !f.enabled) {
            return;
        }

        if (!OriginFitter.ContainsKey(f)) {
            OriginFitter[f] = true;
        }

        f.enabled = false;
    }

    /// <summary>好感块的衬底（幂等创建，挂在字幕条下）。好感容器本身搬进它。</summary>
    private Image? EnsureAffinityBox()
    {
        if (_affBox != null) {
            return _affBox;
        }

        if (_backdrop == null) {
            return null;
        }

        var go = new GameObject(AffinityBoxName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(_backdrop.transform, false);

        var img = go.GetComponent<Image>();
        img.color = new Color(0.02f, 0.02f, 0.03f, AffinityBgAlpha);
        img.sprite = EnsureBoxSprite();
        img.raycastTarget = false;

        _affBox = img;
        return img;
    }

    /// <summary>
    /// 给某块衬底挂上原版的拖动组件 <see cref="UIDragPanel"/>。
    ///
    /// <b>为什么用它</b>：手感跟原版 HUD 拖动完全一致（同一个 <c>OnDrag</c> 实现），
    /// 而且比自己写鼠标监听更稳 —— 它自己处理 <c>pressEventCamera</c>、<c>useDragThreshold</c>。
    /// <b>为什么不能登记成原生 widget</b>：见 MEMORY.md §13 —— 原版 widget 必须从
    /// <c>UI/Widget/&lt;id&gt;</c> 的 prefab 实例化并入 <c>EMono.player.widgets.dict</c>，mod 没 prefab 资源。
    ///
    /// <c>clamp=false</c>：它的 <c>ClampToWindow()</c> 会把目标按 20px 余量夹回父容器内，
    /// 我们要的是「能拖到屏幕上任意位置」，不能夹。
    /// <c>autoAnchor=false</c>：否则松手时它会自己 <c>SetAnchor()</c> 改写锚点，跟我们的 point anchor 打架。
    /// </summary>
    private static UIDragPanel? EnsureDrag(Image owner, UIDragPanel? existing, bool enable)
    {
        if (!enable || owner == null) {
            if (existing != null) {
                existing.enable = false;
            }

            return existing;
        }

        if (existing == null) {
            existing = owner.gameObject.GetComponent<UIDragPanel>();
            if (existing == null) {
                existing = owner.gameObject.AddComponent<UIDragPanel>();
            }

            existing.SetTarget(owner.rectTransform);
            existing.autoAnchor = false;
            existing.clamp = false;
            existing.axisX = true;
            existing.axisY = true;
        }

        existing.enable = true;

        // 必须可射线命中，否则 OnPointerDown 永远不触发（UIDragPanel 不含任何 Graphic）。
        if (!owner.raycastTarget) {
            owner.raycastTarget = true;
        }

        return existing;
    }

    /// <summary>
    /// 把玩家拖出来的位移折算成「相对基准位置的偏移量」。
    ///
    /// 每帧的流程：<c>SetRect</c> 写 <c>base + offset</c> → 玩家拖动时 <see cref="UIDragPanel"/>
    /// 直接改 <c>localPosition</c> → 下一帧我们发现实际位置跟要写的不一致，
    /// 差值一定是「这一帧拖出来的增量」，累加进 offset，之后再由 <c>base + offset</c> 正常写出。
    /// 点锚（anchorMin == anchorMax）下 ΔlocalPosition ≡ ΔanchoredPosition，所以这个口径成立。
    ///
    /// <b>只在 EditMode 下做这个反算</b>：平时 laying out 会因为分辨率变化 / 换预设而让 base 挪动，
    /// 那时把差值当成「玩家拖的」会污染 offset。
    /// </summary>
    private static Vector2 TrackDrag(Slot slot, RectTransform rt, Vector2 basePos)
    {
        SubtitleLayoutStore.Load();

        Vector2 offset = slot == Slot.Choice
            ? new Vector2(SubtitleLayoutStore.ChoiceX, SubtitleLayoutStore.ChoiceY)
            : new Vector2(SubtitleLayoutStore.AffinityX, SubtitleLayoutStore.AffinityY);

        Vector2 want = basePos + offset;

        if (!EditMode) {
            return want;
        }

        Vector2 cur = rt.anchoredPosition;
        if (Mathf.Abs(cur.x - want.x) > 0.5f || Mathf.Abs(cur.y - want.y) > 0.5f) {
            offset += cur - want;
            want = cur;

            if (slot == Slot.Choice) {
                SubtitleLayoutStore.ChoiceX = offset.x;
                SubtitleLayoutStore.ChoiceY = offset.y;
            }
            else {
                SubtitleLayoutStore.AffinityX = offset.x;
                SubtitleLayoutStore.AffinityY = offset.y;
            }

            SubtitleLayoutStore.MarkDirty();
        }

        return want;
    }

    private enum Slot
    {
        Choice,
        Affinity,
    }

    private void LayoutTexts(float w, float h)
    {
        // 说话人名字实际长度可以很长（日志实测「被诅咒的少女『梅丽利丝』 - 艾沃达纳人 532…」= 26 字符），
        // 原版容器只有 100~104px 宽。搬进字幕条后给足整条宽度，右侧留出「继续」箭头的空间。
        float nameW = Mathf.Max(160f, w - TextPaddingX * 2f - 64f);

        // 正文用满整条：好感块已经搬出字幕条去左侧了（见 LayoutAffinity），不再占条内宽度。
        float textW = Mathf.Max(160f, w - TextPaddingX * 2f);
        float textH = Mathf.Max(48f, h - NameBottomGap * 4f - 12f);

        // 说话人：字幕条左上。
        MoveInto(_dialog!.textName != null ? _dialog.textName.transform : null,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(TextPaddingX, -NameBottomGap),
            new Vector2(nameW, Mathf.Max(24f, (h - textH) - NameBottomGap * 2f)));

        // 正文：说话人下方。宽度写死 —— 打字机换行依赖它，搬出原父级后没人再撑这个宽度。
        var main = _dialog.textMain;
        if (main != null) {
            MoveInto(main.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(TextPaddingX, -(NameBottomGap * 2f + (h - textH))),
                new Vector2(textW, textH));

            // 🔴 每帧盖回行距倍率：DialogDrama.SetText 会调 ApplySkin 把它改回换肤口径。
            //    注意它是「倍率」不是「像素」（UIText : Text），别再按容器高度反推。
            ForceLineSpacing(main);
            MakeReadable(main);
        }
    }

    /// <summary>
    /// 选项块：**贴字幕条上沿、右对齐**，并且给它垫一层半透明黑底
    /// （用户反馈「选项放到右边 + 没有背景色看不清楚」）。
    ///
    /// 衬底单独建一个节点 <see cref="ChoiceBoxName"/> 承托，而不是给选项容器自身加 Image：
    /// 选项容器是原版节点（可能是 ScrollRect 根 / LayoutGroup 容器），
    /// 往它身上挂 Image 会参与它的排序、被 ScrollRect 的 mask 裁掉、还可能吃掉按钮点击。
    /// 衬底 <c>raycastTarget=false</c>，按钮照旧可点。
    /// </summary>
    private void LayoutChoices(float w, float h)
    {
        Image? box = EnsureChoiceBox();
        if (box == null) {
            return;
        }

        CollectChoiceTargets();

        bool show = HasVisibleChoice();
        if (box.gameObject.activeSelf != show) {
            box.gameObject.SetActive(show);
        }

        if (!show) {
            return;
        }

        float bw = Mathf.Clamp(w * ChoiceWidthRatio, 320f, Mathf.Max(320f, w - TextPaddingX * 2f));
        float bh = Mathf.Max(120f, Screen.height * ChoiceHeightRatio);

        // 锚父级（字幕条）右下角 + pivot 右下角 → 块向右上长出，贴条上沿。
        Vector2 basePos = new Vector2(-TextPaddingX, h + ChoiceGapY);
        basePos = TrackDrag(Slot.Choice, box.rectTransform, basePos);

        SetRect(box.rectTransform,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
            basePos, new Vector2(bw, bh));

        // 「布局调整」模式：把原版拖动组件挂到衬底上（关掉时停用它，避免吃掉点击）。
        _dragChoice = EnsureDrag(box, _dragChoice, EditMode);

        foreach (Transform t in _choiceTargets) {
            // 拉满衬底；forceSize=true 绕开「有 ContentSizeFitter 就不写尺寸」的规矩 ——
            // 这里我们要的是「填满衬底」，它自己算的大小会把衬底戳破。
            TakeControl(t);
            MoveInto(t, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(-12f, -12f), host: box.transform, forceSize: true);
        }
    }

    /// <summary>
    /// 选项容器：<see cref="DialogDrama.transChoices"/> 是原版字段，但**5 个 prefab 变体接的不是同一个节点**，
    /// 且它可能被 ScrollRect 管着（ScrollRect 每帧会抢回内容位置）。
    /// 所以：以字段为准，若它处在 ScrollRect 之下则改搬 ScrollRect 根；再补一个「含 ScrollRect+Button」的退路。
    /// </summary>
    private void CollectChoiceTargets()
    {
        _choiceTargets.Clear();

        if (_dialog == null) {
            return;
        }

        Transform? field = _dialog.transChoices;
        if (field != null) {
            AddChoiceTarget(NormalizeChoice(field));
        }

        foreach (Transform child in _dialog.transform) {
            if (child == null) {
                continue;
            }

            if (child.GetComponentInChildren<ScrollRect>(true) == null
                || child.GetComponentInChildren<Button>(true) == null) {
                continue;
            }

            AddChoiceTarget(NormalizeChoice(child));
        }
    }

    private void AddChoiceTarget(Transform? t)
    {
        if (t == null || _choiceTargets.Contains(t)) {
            return;
        }

        _choiceTargets.Add(t);
    }

    private Transform? NormalizeChoice(Transform t)
    {
        if (_dialog == null) {
            return t;
        }

        // 内容节点被 ScrollRect 托管时，搬内容没用 —— ScrollRect 每帧会把它的位置写回去。搬 ScrollRect 根。
        var sr = t.GetComponentInParent<ScrollRect>();
        if (sr != null && sr.transform != t && sr.transform != _dialog.transform
            && sr.transform.IsChildOf(_dialog.transform)) {
            return sr.transform;
        }

        return t;
    }

    /// <summary>
    /// 好感 / 兴趣块：**贴字幕条上沿、左对齐** —— 用户要「好感显示应该跟立绘一起放左边」。
    /// 立绘锚在屏幕左下 (AnchorRight=false)，字幕条之上露出来的正是它的身体，
    /// 好感块摆在条上沿的左侧 = 视觉上跟角色同侧、同一坨。
    ///
    /// <b>锚点必须取父级左下 + pivot 左下</b>（这一点踩过坑、别改回去）：
    /// 它的子项（goInterest / goAffinity / goFav / goRank）锚点都是 <c>(0,1)</c>，
    /// 也就是「以父节点**左上角**为原点往下排」。若把父节点顶边压在字幕条上沿，
    /// 整块会从那里往下长 → 落进字幕条里跟正文打架；
    /// 上一版把底边压在条顶边、块又被撑到屏幕外，实测表现就是「好感没了」。
    /// 现在：底边钉在条上沿，块向上长，子项从块顶往下排 → 整块稳定落在条上方的左侧。
    /// </summary>
    private void LayoutAffinity(float w, float h)
    {
        GameObject? go = _dialog!.goAffinity;
        if (go == null) {
            return;
        }

        Image? box = EnsureAffinityBox();
        float ah = Mathf.Max(72f, h * AffinityHeightRatio);

        Vector2 basePos = new Vector2(TextPaddingX, h + ChoiceGapY);
        basePos = box != null
            ? TrackDrag(Slot.Affinity, box.rectTransform, basePos)
            : basePos;

        if (box != null) {
            SetRect(box.rectTransform,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                basePos, new Vector2(AffinityWidth, ah));

            // 好感块跟着原版自己的显隐走 —— 没有雇佣信息时不留一块空黑框。
            if (box.gameObject.activeSelf != go.activeInHierarchy) {
                box.gameObject.SetActive(go.activeInHierarchy);
            }

            _dragAffinity = EnsureDrag(box, _dragAffinity, EditMode);
        }

        MoveInto(go.transform,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
            Vector2.zero, new Vector2(AffinityWidth, ah), host: box?.transform);

        // 白字：雇佣成本和正文一个口径，否则浅色场景里那几行灰字读不出来。
        foreach (var t in go.GetComponentsInChildren<UIText>(true)) {
            MakeReadable(t);
        }
    }

    private void LayoutNext()
    {
        GameObject? next = _dialog!.iconNext;
        if (next == null) {
            return;
        }

        // 「继续」箭头：字幕条右下角。
        MoveInto(next.transform,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
            new Vector2(-TextPaddingX, 10f), null);
    }

    /// <summary>
    /// 原版底板彻底隐藏。判据是「面积占屏比够大 + 不承载文字 + 不是遮罩 + 不在立绘/CG 子树内」，
    /// 不靠名字猜 —— 名字随 prefab 变体变，面积不会。
    /// </summary>
    private void HideVanillaPanel()
    {
        _panelHits.Clear();

        if (_dialog == null) {
            return;
        }

        float screen = Mathf.Max(1f, (float)Screen.width * Screen.height);
        Image? cg = _dialog.imageBgAdv;
        Portrait? portrait = _dialog.portrait;

        foreach (var img in _dialog.GetComponentsInChildren<Image>(true)) {
            if (img == null || img.gameObject.name == BackdropName) {
                continue;
            }

            if (_backdrop != null && img.transform.IsChildOf(_backdrop.transform)) {
                continue;
            }

            // 场景 CG 是内容不是框，绝不能跟着底板一起抹掉。
            if (cg != null && (img == cg || img.transform.IsChildOf(cg.transform))) {
                continue;
            }

            // 头像框单独处理（要按「有没有全身立绘」决定去留），这里不碰。
            if (portrait != null && img.transform.IsChildOf(portrait.transform)) {
                continue;
            }

            // 遮罩图（UIMask）改 alpha 会改掉裁剪形状，必须跳过。
            if (img.GetComponent<Mask>() != null || img.GetComponent<RectMask2D>() != null) {
                continue;
            }

            if (img.GetComponent<Text>() != null || img.GetComponent<UIText>() != null) {
                continue;
            }

            Rect r = img.rectTransform.rect;
            if (Mathf.Abs(r.width * r.height) / screen < PanelMinAreaRatio) {
                continue;
            }

            SaveAlpha(img);
            _panelHits.Add(img);
        }

        foreach (var img in _panelHits) {
            SetAlpha(img, PanelAlpha);
        }
    }

    /// <summary>
    /// 小头像框：只有在「已经有一张全身立绘顶上来」时才藏，
    /// 否则玩家既看不到全身立绘、又丢了头像框。
    /// </summary>
    private void HidePortraitBox()
    {
        Portrait? p = _dialog!.portrait;
        if (p == null) {
            return;
        }

        Image? full = p.imageFull;
        if (full == null || !full.gameObject.activeInHierarchy) {
            return;
        }

        foreach (var img in p.GetComponentsInChildren<Image>(true)) {
            if (img == null || img == full) {
                continue;
            }

            SaveAlpha(img);
            SetAlpha(img, 0f);
        }
    }

    // ---------------- 文字可读性 ----------------

    /// <summary>
    /// 每帧盖回正文行距。
    ///
    /// <b>为什么是倍率不是像素</b>：<c>UIText : Text</c>，<c>lineSpacing</c> 跟的是 Unity
    /// <see cref="Text"/> 的语义 —— 「字体行高的倍率」，正常值 1.0~1.6，默认 1。
    /// 上一版按像素推，算出 80.35 倍 → 每行 17×80 ≈ 1360px，正文整块飞出容器、
    /// 连带把相邻块顶出画面（日志里 <c>main onScreen=False h=329</c>）。
    ///
    /// <b>为什么要每帧写</b>：<see cref="UIText.ApplySkin"/> 会在每次 <c>SetText</c>
    /// 时被 <see cref="DialogDrama"/> 调用，把行距改回 <c>orgSpacing * fontSource.lineSpacing</c>
    /// 的换肤口径。所以这里不协商，直接盖。
    /// </summary>
    private static void ForceLineSpacing(UIText t)
    {
        if (t == null) {
            return;
        }

        if (!Mathf.Approximately(t.lineSpacing, TextLineSpacing)) {
            t.lineSpacing = TextLineSpacing;
        }
    }

    /// <summary>
    /// 强制正文／说话人名的颜色与可见性。
    ///
    /// 原版 <see cref="UIText"/> 的颜色来自 <c>SkinColorProfile.GetTextColor(FontColor)</c> 再叠对比度，
    /// 在浅色场景 + 半透明条上会不够亮；同时本项目的 <c>DialogProbe</c> 实测这两个 <c>Text</c>
    /// 的 <c>enabled</c> 都可能被原版或其它 mod 置 false（日志里大量 <c>enabled=False</c>）。
    /// 用户明确要求「字应该是白色的」，所以这里直接钉死颜色与开关，不参与换肤协商。
    /// 原始值由 <see cref="OriginTextColor"/> / <see cref="OriginTextEnabled"/> 记账，可无损还原。
    /// </summary>
    private static void MakeReadable(Component? text)
    {
        if (text == null) {
            return;
        }

        if (text is UIText ui) {
            if (!OriginTextColor.ContainsKey(ui)) {
                OriginTextColor[ui] = ui.color;
            }

            if (ui.color != TextColor) {
                ui.color = TextColor;
            }
        }

        if (!OriginTextEnabled.ContainsKey(text)) {
            OriginTextEnabled[text] = text.GetComponent<Renderer>() != null;
        }

        var r = text.GetComponent<Renderer>();
        if (r != null && !r.enabled) {
            r.enabled = true;
        }
    }

    /// <summary>
    /// 底部渐变衬底。Unity 的 <see cref="Image"/> 有 <c>type</c> 字段但没有渐变类型，
    /// 所以自建一张 2px 高的纯白 sprite，靠**顶点色**做上→下 alpha 渐变：
    /// 底边 = <see cref="BackdropAlpha"/>（可读性衬底），顶边 = <see cref="GradientTopAlpha"/>（默认全透明）。
    /// 贴图很小且只在参数变化时重建，不占显存。
    /// </summary>
    private static Sprite EnsureGradientSprite()
    {
        // 量化成整数档，避免浮点抖动导致每帧重建。
        int topQ = Mathf.RoundToInt(Mathf.Clamp01(GradientTopAlpha) * 32f);
        int botQ = Mathf.RoundToInt(Mathf.Clamp01(BackdropAlpha) * 32f);
        int startQ = Mathf.RoundToInt(Mathf.Clamp01(GradientStart) * 32f);
        int key = (topQ << 12) | (botQ << 6) | startQ;

        if (GradientCache != null && GradientKey == key) {
            return GradientCache;
        }

        GradientKey = key;

        // 采样点越多渐入越平滑；64 对一张横条绰绰有余。
        const int H = 64;
        var tex = new Texture2D(1, H, TextureFormat.RGBA32, false) {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };

        float topA = topQ / 32f;
        float botA = botQ / 32f;
        float start = startQ / 32f;

        // 贴图 y=0 是底部，与 UI 坐标一致，不需翻转。
        for (int y = 0; y < H; y++) {
            float u = y / (float)(H - 1);
            float a = u <= start ? topA
                : Mathf.Lerp(topA, botA, (u - start) / Mathf.Max(1e-4f, 1f - start));
            tex.SetPixel(0, y, new Color(1f, 1f, 1f, a));
        }

        tex.Apply(false, false);

        var sp = Sprite.Create(tex, new Rect(0f, 0f, 1f, H), new Vector2(0.5f, 0.5f), 100f,
            0, SpriteMeshType.FullRect);
        sp.hideFlags = HideFlags.HideAndDontSave;

        if (GradientCache != null) {
            DestroySprite(GradientCache);
        }

        GradientCache = sp;
        return sp;
    }

    /// <summary>选项衬底用的 1x1 纯白 sprite（静态缓存，重建周期和渐变贴图一样长）。</summary>
    private static Sprite EnsureBoxSprite()
    {
        if (BoxCache != null) {
            return BoxCache;
        }

        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };

        tex.SetPixel(0, 0, Color.white);
        tex.Apply(false, false);

        var sp = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 100f,
            0, SpriteMeshType.FullRect);
        sp.hideFlags = HideFlags.HideAndDontSave;

        BoxCache = sp;
        return sp;
    }

    private static void DestroySprite(Sprite sp)
    {
        try {
            if (sp.texture != null) {
                Destroy(sp.texture);
            }

            Destroy(sp);
        }
        catch {
            // 退出流程里对象可能已被销毁，忽略。
        }
    }

    // ---------------- 工具 ----------------

    /// <summary>把节点搬进字幕条（默认）或指定容器，并按给定的锚点 / pivot 摆位。原始位置只在首次搬动时记一次（可无损还原）。</summary>
    private void MoveInto(Transform? t, Vector2 amin, Vector2 amax, Vector2 pivot, Vector2 pos, Vector2? size,
        Transform? host = null, bool forceSize = false)
    {
        Transform parent = host ?? _backdrop!.transform;

        if (t == null || _backdrop == null || !(t is RectTransform rt)) {
            return;
        }

        if (!Origin.TryGetValue(t, out NodeState? s)) {
            s = new NodeState {
                Parent = t.parent,
                Sibling = t.GetSiblingIndex(),
                AnchorMin = rt.anchorMin,
                AnchorMax = rt.anchorMax,
                Pivot = rt.pivot,
                Pos = rt.anchoredPosition,
                Size = rt.sizeDelta,
            };
            Origin[t] = s;
        }

        if (t.parent != parent) {
            t.SetParent(parent, false);
        }

        // 自带 ContentSizeFitter 的容器由它自己决定高度：我们每帧写、它每帧改回去，
        // 只会把布局反复置脏。选项块是例外 —— 它必须填满衬底（forceSize）。
        Vector2? want = size;
        if (want.HasValue && !forceSize && t.GetComponent<ContentSizeFitter>() != null) {
            want = null;
        }

        SetRect(rt, amin, amax, pivot, pos, want ?? s.Size);
        _movedCount++;
    }

    private static void SetRect(RectTransform rt, Vector2 amin, Vector2 amax, Vector2 pivot,
        Vector2 pos, Vector2 size)
    {
        if (rt.anchorMin != amin) { rt.anchorMin = amin; }
        if (rt.anchorMax != amax) { rt.anchorMax = amax; }
        if (rt.pivot != pivot) { rt.pivot = pivot; }
        if (rt.anchoredPosition != pos) { rt.anchoredPosition = pos; }
        if (rt.sizeDelta != size) { rt.sizeDelta = size; }
    }

    /// <summary>只在首次见到时记原始 alpha —— 反复写会把「原版值」污染成我们写进去的值。</summary>
    private static void SaveAlpha(Image img)
    {
        if (!OriginAlpha.ContainsKey(img)) {
            OriginAlpha[img] = img.color.a;
        }
    }

    private static void SetAlpha(Image img, float a)
    {
        Color c = img.color;
        if (Mathf.Approximately(c.a, a)) {
            return;
        }

        c.a = a;
        img.color = c;
    }

    /// <summary>沿层级向上找「Canvas 的直接子物体」——立绘也挂在那一层。</summary>
    private static Transform FindCanvasChild(Transform t)
    {
        while (t.parent != null) {
            if (t.parent.GetComponent<Canvas>() != null) {
                return t;
            }

            t = t.parent;
        }

        return t;
    }

    /// <summary>
    /// 只在「接管的规模」变化时打一行。面板 prefab 是推断出来的层级，
    /// 这行日志让实机截图能直接对上「到底动了谁」，省一轮来回。
    ///
    /// 同时把**实测几何**打出来（正文的世界坐标矩形、是否在屏内、字号行距）——
    /// 上一轮就是靠这行才发现「日志说 moves=5、画面上什么都没动」，
    /// 根因是行距没重算而不是节点没搬。下一轮有任何偏移，先看这里。
    /// </summary>
    private void Diag(float h)
    {
        UIText? main = _dialog!.textMain;
        float spacing = main != null ? main.lineSpacing : -1f;
        int sig = (_panelHits.Count * 97) + (_movedCount * 7) + Origin.Count
            + (int)(spacing * 10f) + (int)(h * 2f);
        if (sig == _diagSig) {
            return;
        }

        _diagSig = sig;

        var sb = new StringBuilder("[Subtitle] ");
        sb.Append(_dialog.gameObject.name);
        sb.Append(" bar=").Append(Screen.width).Append('x').Append((int)h);
        sb.Append(" moves=").Append(_movedCount);
        sb.Append(" grad(top=").Append(GradientTopAlpha.ToString("0.00"))
            .Append(",bot=").Append(BackdropAlpha.ToString("0.00"))
            .Append(",start=").Append(GradientStart.ToString("0.00")).Append(')');
        sb.Append(" panel=[");

        for (int i = 0; i < _panelHits.Count; i++) {
            if (i > 0) {
                sb.Append(',');
            }

            sb.Append(_panelHits[i].gameObject.name);
        }

        sb.Append(']');
        AcsMod.Log(sb.ToString());

        // 几何校验：正文若不在屏内或高宽为 0，就是布局算错了 —— 直接点名。
        LogGeometry(main, "main");
        LogGeometry(_dialog.textName, "name");
        LogGeometry(_choiceBox, "choiceBox");
        LogGeometry(_dialog.goAffinity != null ? _dialog.goAffinity.transform : null, "affinity");
    }

    private static void LogGeometry(Component? text, string tag)
    {
        if (text == null) {
            AcsMod.Log("[Subtitle]   " + tag + " = (null)");
            return;
        }

        if (!(text.transform is RectTransform rt)) {
            return;
        }

        // 世界坐标四角 → 屏幕像素，判断是否真的落在屏内。
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        var canvas = text.GetComponentInParent<Canvas>();
        Camera? cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? (canvas.worldCamera != null ? canvas.worldCamera : Camera.main) : null;

        // worldCamera 为 null 的 ScreenSpace-Camera 画布会让 WorldToScreenPoint 直接返回「世界坐标当像素」，
        // 于是得到负宽 / 负高这种一看就不可能的结果 —— 上一版日志里 main w=-304 就是这么来的，
        // 布局其实没问题，是 diagnostic 骗了我。补 Camera.main 兜底，并把本地 rect 一起打出来。
        Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 max = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);

        bool onScreen = max.x > 0f && min.x < Screen.width && max.y > 0f && min.y < Screen.height;

        string extra = text is UIText ui
            ? $" size={ui.fontSize} spacing={ui.lineSpacing:0.00} lines={ui.text.Split('\n').Length} chars={ui.text.Length}"
            : "";

        Rect lr = rt.rect;
        AcsMod.Log($"[Subtitle]   {tag} scr=[{(int)min.x},{(int)min.y}]..[{(int)max.x},{(int)max.y}]"
            + $" local={(int)lr.width}x{(int)lr.height}"
            + $" onScreen={onScreen}"
            + $" color={ColorUtility.ToHtmlStringRGBA((text as Graphic)?.color ?? Color.white)}{extra}");
    }
}
