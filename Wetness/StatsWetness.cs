using System;

namespace Wetness;

/// <summary>
/// 湿润度状态。继承 Elin 原生的 Stats，复用它的 phase / 颜色 / 取值机制。
/// 与 Stats.Hunger 等一样是全局单例，通过 Set(raw, index, chara) 绑定到某个角色的 _cints 数组。
///
/// 本状态是纯代码造出来的，源表里没有它的行。为免各处回退到 ConWet 而行为异常，
/// 这里自己补一行最小的 SourceStat.Row（见 EnsureSource）—— 只填真正被读到的字段。
/// </summary>
public class StatsWetness : Stats
{
    public static StatsWetness Instance = new StatsWetness { id = 99 };

    public override int max => WetnessDef.Max;

    public override int min => WetnessDef.Min;

    /// <summary>档位变化时说话。只在 PC 上提示，避免队友刷屏。</summary>
    public override bool TrackPhaseChange => BaseStats.CC != null && BaseStats.CC.IsPC;

    /// <summary>
    /// 补一行自己的 SourceStat。
    ///
    /// 为什么非要不可：BaseStats.source 在查不到 id 时会回退到 ConWet，而
    /// NotificationStats.Interactable 的判据是
    ///     !stats().source.GetDetail().IsEmpty()
    /// 它为空时 WidgetStats.Add 会把按钮的 raycastTarget 关掉 ——
    /// 结果就是鼠标根本放不上去，Tip 永远出不来（不是 Tip 内容没写，是压根不响应）。
    ///
    /// 顺带解决颜色：colors 留空之后 GetColor(SkinColorProfile) 取的是 "default"
    /// 渐变，与 hunger / hygiene 等原版状态同一条色带 —— 于是本类不再覆写 GetColor，
    /// 交给基类按 value/max 取色，颜色和原版就完全一致了。
    ///
    /// 图标沿用 ConWet（alias 指向它）：idSprite 取的就是 alias，
    /// 而 ConWet 在 icon_elements 里有现成的图，不必另外准备素材。
    ///
    /// 三个 detail 字段填同一份文本，是因为 GetDetail() 具体读哪一个取决于语言设置，
    /// 全填就不会出现「中文玩家读到空字符串」这种事。
    /// </summary>
    public static void EnsureSource()
    {
        if (Instance._source != null)
        {
            return;
        }

        string detail = WetnessLang.NoteDetail;

        Instance._source = new SourceStat.Row
        {
            id = 99,
            alias = "ConWet",
            name = WetnessLang.WetnessName,
            detail = detail,
            detail_JP = detail,
            detail_L = detail,
        };
    }

    /// <summary>
    /// 分档走原版那套：先 10 等分取桶，再查 WetnessDef.PhaseMap。
    /// 之所以不能直接用基类实现，是因为本状态没有自己的源表行，
    /// base.source 会回退到 ConWet，读到的 phase 数组不是这套七档。
    /// </summary>
    public override int GetPhase()
    {
        return WetnessDef.FromBucket(10 * value / max);
    }

    public override string GetText()
    {
        int phase = GetPhase();
        return WetnessDef.IsVisible(phase) ? WetnessDef.Names[phase] : "";
    }

    public override string GetPhaseStr() => GetText();

    // GetSortVal 没有覆写，走基类实现（返回 source.id）。
    //
    // 之前写的 5000000+12 会把湿润度顶到整个列表最前面（比 buff 的约 900 万还大），
    // 排序是 b-a 降序，于是它挤到了 buff 前头 —— 也就是玩家看到的「buff 在脱水症上面」。
    // 用 source.id（99）后，湿润度落回原版 stats 那一组里，buff 排到它后面。

    /// <summary>
    /// 鼠标悬停时显示的 Tip。格式与原版一致：标题是状态名，下面一行是说明。
    /// （玩家看到的「重荷 / （状态）行李太重。越重速度越慢。」就是这个结构。）
    /// </summary>
    public override void WriteNote(UINote n, Action<UINote> onWriteNote = null)
    {
        n.Clear();
        n.AddHeader(WetnessLang.WetnessName);
        n.AddText("NoteText_flavor_element", WetnessLang.NoteDetail);
        n.Build();
    }

    /// <summary>
    /// 基类实现会去读 source 的 textPhase / textPhase2，
    /// 而本状态没有自己的源表行（会拿错文本），所以整段自己来。
    ///
    /// 注：基类 Stats.OnChangePhase 本身已实现（读 textPhase / textPhase2、
    /// 按 invert 判方向、Msg.SetColor + Say + PopText）。等以后给湿润度注册
    /// 真正的源表行，这个覆写就能整个删掉，改成填那三个字段。
    /// </summary>
    public override void OnChangePhase(int phase, int lastPhase)
    {
        Chara cc = BaseStats.CC;
        if (cc == null || !cc.IsPC)
        {
            return;
        }

        bool worsening = phase < lastPhase;
        string message = WetnessLang.GetPhaseMessage(phase, worsening);
        if (message == null)
        {
            return;
        }

        Msg.SetColor(worsening ? "negative" : "positive");
        cc.Say(message, cc);
        if (cc.ShouldShowMsg)
        {
            PopText();
        }
    }
}
