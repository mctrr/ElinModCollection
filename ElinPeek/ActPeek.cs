using UnityEngine;

namespace ElinPeek;

/// <summary>
/// 显示在 PC 动作栏里的“窥视”按钮（Act）。
/// 点击后进入窥视模式：世界冻结、视野跟鼠标重定向到拐角后，右键/Esc/左键退出。
/// </summary>
public class ActPeek : Act
{
    public override string ID => "peek";

    public override TargetType TargetType => TargetType.Self;

    public override bool Perform()
    {
        PeekManager.Enter();
        return false; // 窥视不消耗回合
    }

    public override string GetText(string str = "")
    {
        return GetDisplayName();
    }

    public override Sprite GetSprite()
    {
        return null!; // 纯文字按钮，不需要图标源
    }

    private static string GetDisplayName() => Lang.langCode switch
    {
        "CN" => "窥视",
        "JP" => "覗き見",
        _ => "Peek"
    };
}
