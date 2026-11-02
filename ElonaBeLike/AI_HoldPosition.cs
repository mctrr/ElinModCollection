using System.Collections.Generic;

namespace ElonaBeLike;

internal class AI_HoldPosition : AIAct
{
    public Point dest;

    public AI_HoldPosition(Point dest)
    {
        this.dest = dest.Copy();
    }

    public override bool CancelOnAggro => true;

    public override bool CancelWhenDamaged => false;

    public override bool InformCancel => false;

    public override bool IsIdle => !IsChildRunning;

    public override string Name => "守住";

    public override IEnumerable<Status> Run()
    {
        if (!dest.IsValid)
        {
            yield return Cancel();
            yield break;
        }

        if (!owner.pos.Equals(dest))
        {
            yield return Do(new AI_Goto(dest, 0));
        }

        while (true)
        {
            if (!owner.pos.Equals(dest))
            {
                yield return DoGoto(dest);
                continue;
            }

            if (owner.enemy != null && owner.enemy.IsAliveInCurrentZone)
            {
                owner.SetAIAggro();
                yield return Success();
                yield break;
            }

            yield return DoIdle();
        }
    }
}
