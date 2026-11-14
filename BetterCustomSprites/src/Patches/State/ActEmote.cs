using System.Collections.Generic;
using ACS.API;
using ACS.Frame;
using UnityEngine;

namespace ACS.Patches;

/// <summary>
/// PC action-bar emote button. Plays a one-shot ACS emote clip, no turn cost.
/// One clip plays immediately; several clips open a picker menu.
/// </summary>
public class ActEmote : Act
{
    public override string ID => "acs_emote";

    public override TargetType TargetType => TargetType.Self;

    public override bool Perform()
    {
        var pc = CC as Chara ?? EClass.pc;
        if (pc is null) {
            return false;
        }

        TryPlayOrPick(pc);
        return false;
    }

    public override string GetText(string str = "")
        => GetDisplayName();

    public override Sprite GetSprite()
        => null!;

    internal static string GetDisplayName()
        => Lang.langCode switch {
            "CN" => "表情",
            "JP" => "表情",
            _ => "Emote",
        };

    internal static bool TryPlayOrPick(Chara pc, string? state = null)
    {
        if (pc is null) {
            return false;
        }

        if (!state.IsEmpty()) {
            return PlayState.StartEmote(pc, state);
        }

        List<string> states = PlayState.ListEmoteStates(pc);
        if (states.Count == 0) {
            return false;
        }

        if (states.Count == 1) {
            return PlayState.StartEmote(pc, states[0]);
        }

        UIContextMenu menu = EClass.ui.CreateContextMenuInteraction();
        foreach (string clip in states) {
            string locked = clip;
            menu.AddButton(FormatClipName(locked), () => PlayState.StartEmote(pc, locked));
        }

        menu.Show();
        return true;
    }

    internal static string FormatClipName(string state)
    {
        if (state.IsEmpty() || state == "emote") {
            return GetDisplayName();
        }

        if (state.StartsWith("emote.")) {
            return state.Substring("emote.".Length);
        }

        return state;
    }
}
