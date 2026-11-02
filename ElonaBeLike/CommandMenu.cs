using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace ElonaBeLike;

internal static class CommandMenu
{
    public static void Show(Point dest)
    {
        Plugin.Instance.StartCoroutine(ShowNextFrame(Resolve(dest)));
    }

    /// <summary>点到墙里/障碍里，就退到最近一个能站的格子 —— 队友在附近守住就行。</summary>
    private static Point Resolve(Point dest)
    {
        return dest.IsBlocked ? dest.GetNearestPoint(allowBlock: false, allowChara: false, allowInstalled: false) : dest.Copy();
    }

    private static IEnumerator ShowNextFrame(Point dest)
    {
        yield return null;
        ShowNow(dest);
    }

    private static void ShowNow(Point dest)
    {
        Point? origin = EMono.pc?.pos?.Copy();
        Point here = origin ?? dest;
        bool canPatrol = origin != null && !here.Equals(dest);

        List<Chara> allies = CommandRegistry.PartyAllies().ToList();
        List<Chara> commanding = CommandRegistry.AlliesWith(OrderKind.Hold)
            .Concat(CommandRegistry.AlliesWith(OrderKind.Patrol))
            .ToList();

        UIContextMenu menu = EMono.ui.CreateContextMenu();
        menu.AddButton("全部去守住", () => CommandRegistry.HoldAll(dest));
        if (canPatrol)
        {
            menu.AddButton("全部在此巡逻", () => CommandRegistry.PatrolAll(here, dest));
        }

        if (allies.Count > 0)
        {
            UIContextMenu holdChild = menu.AddChild("部分驻守");
            foreach (Chara chara in allies)
            {
                Chara member = chara;
                holdChild.AddButton(member.NameSimple, () => CommandRegistry.Hold(member, dest));
            }

            if (canPatrol)
            {
                UIContextMenu patrolChild = menu.AddChild("部分巡逻");
                foreach (Chara chara in allies)
                {
                    Chara member = chara;
                    patrolChild.AddButton(member.NameSimple, () => CommandRegistry.Patrol(member, here, dest));
                }
            }
        }

        menu.AddButton("召回全部", CommandRegistry.RecallAll);
        if (commanding.Count > 0)
        {
            UIContextMenu recallChild = menu.AddChild("部分召回");
            foreach (Chara chara in commanding)
            {
                Chara member = chara;
                recallChild.AddButton(member.NameSimple, () => CommandRegistry.Recall(member));
            }
        }

        menu.Show();
    }
}
