using System.Collections.Generic;

namespace ElonaBeLike;

internal class AI_Patrol : AIAct
{
    private const int MaxFailedGoto = 4;

    public Point a;
    public Point b;

    private bool headingToB;
    private int failCount;

    public AI_Patrol(Point a, Point b)
    {
        this.a = a.Copy();
        this.b = b.Copy();
    }

    public override bool CancelOnAggro => true;

    public override bool CancelWhenDamaged => false;

    public override bool InformCancel => false;

    public override bool IsIdle => !IsChildRunning;

    public override string Name => "巡逻";

    public override IEnumerable<Status> Run()
    {
        if (!a.IsValid || !b.IsValid)
        {
            yield return Cancel();
            yield break;
        }

        headingToB = owner.pos.Distance(a) > owner.pos.Distance(b);
        failCount = 0;
        while (true)
        {
            Point dest = headingToB ? b : a;
            if (!owner.pos.Equals(dest))
            {
                yield return DoGoto(dest, 0, false, OnGotoFailed);
                continue;
            }

            failCount = 0;
            if (owner.enemy != null && owner.enemy.IsAliveInCurrentZone)
            {
                owner.SetAIAggro();
                yield return Success();
                yield break;
            }

            headingToB = !headingToB;
            yield return DoWait(2);
        }
    }

    private Status OnGotoFailed()
    {
        headingToB = !headingToB;
        if (++failCount >= MaxFailedGoto)
        {
            return Cancel();
        }

        return Status.Running;
    }
}
