using System.Collections.Generic;
using System.Linq;

namespace ElonaBeLike;

internal enum OrderKind
{
    Hold,
    Patrol,
}

internal sealed class Order
{
    public OrderKind Kind;
    public Point A;
    public Point B;

    public Order(OrderKind kind, Point a, Point b)
    {
        Kind = kind;
        A = a;
        B = b;
    }

    public bool IsValid => A.IsValid && B.IsValid;
}

internal static class CommandRegistry
{
    private static readonly Dictionary<int, Order> Orders = new Dictionary<int, Order>();

    public static Order? Get(Chara chara)
    {
        Orders.TryGetValue(chara.uid, out Order order);
        return order;
    }

    public static bool IsCommanded(Chara chara)
    {
        return Get(chara) != null;
    }

    public static void Hold(Chara chara, Point dest)
    {
        if (chara.IsPC || !dest.IsValid)
        {
            return;
        }

        Point copy = dest.Copy();
        Orders[chara.uid] = new Order(OrderKind.Hold, copy, copy);
        chara.SetAIImmediate(new AI_HoldPosition(copy));
    }

    public static void HoldAll(Point dest)
    {
        foreach (Chara chara in PartyAllies())
        {
            Hold(chara, dest);
        }
    }

    public static void Patrol(Chara chara, Point a, Point b)
    {
        if (chara.IsPC || !a.IsValid || !b.IsValid)
        {
            return;
        }

        if (a.Equals(b))
        {
            Hold(chara, b);
            return;
        }

        Point start = a.Copy();
        Point end = b.Copy();
        Orders[chara.uid] = new Order(OrderKind.Patrol, start, end);
        chara.SetAIImmediate(new AI_Patrol(start, end));
    }

    public static void PatrolAll(Point a, Point b)
    {
        foreach (Chara chara in PartyAllies())
        {
            Patrol(chara, a, b);
        }
    }

    public static void Remove(Chara chara)
    {
        Orders.Remove(chara.uid);
    }

    public static void Recall(Chara chara)
    {
        Orders.Remove(chara.uid);
        if (chara.IsAliveInCurrentZone)
        {
            chara.SetAI(new GoalIdle());
        }
    }

    public static void RecallAll()
    {
        List<int> uids = Orders.Keys.ToList();
        Orders.Clear();
        if (EMono.pc?.party == null)
        {
            return;
        }

        foreach (Chara chara in EMono.pc.party.members)
        {
            if (!chara.IsPC && uids.Contains(chara.uid) && chara.IsAliveInCurrentZone)
            {
                chara.SetAI(new GoalIdle());
            }
        }
    }

    public static IEnumerable<Chara> PartyAllies()
    {
        if (EMono.pc?.party == null)
        {
            yield break;
        }

        foreach (Chara chara in EMono.pc.party.members)
        {
            if (!chara.IsPC)
            {
                yield return chara;
            }
        }
    }

    /// <summary>是否有被命令的队友正在交战（守住 / 巡逻都算）。用于战略经验结算。</summary>
    public static bool AnyCommandedInCombat()
    {
        foreach (Chara chara in PartyAllies())
        {
            if (IsCommanded(chara) && chara.IsAliveInCurrentZone && chara.IsInCombat)
            {
                return true;
            }
        }

        return false;
    }

    public static IEnumerable<Chara> AlliesWith(OrderKind kind)
    {
        foreach (Chara chara in PartyAllies())
        {
            Order? order = Get(chara);
            if (order != null && order.Kind == kind)
            {
                yield return chara;
            }
        }
    }
}
