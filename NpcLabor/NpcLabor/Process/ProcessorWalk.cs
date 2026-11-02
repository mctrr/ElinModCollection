namespace NpcLabor.Process;

/// <summary>
/// Walk-end decisions for processor jobs. Isolated so we can lock the
/// "player bumped the walker" case: machine still there, Dist > 1, must
/// retry — never report machine-gone / refund mats.
/// </summary>
internal static class ProcessorWalk
{
    internal enum Outcome
    {
        Arrived,
        Retry,
        MachineGone,
        Stuck,
        WorkerLost
    }

    internal static Outcome Classify(
        bool workerLost,
        bool machineGone,
        int dist,
        string? walkFailReason)
    {
        if (workerLost)
        {
            return Outcome.WorkerLost;
        }

        if (machineGone)
        {
            return Outcome.MachineGone;
        }

        if (dist <= 1)
        {
            return Outcome.Arrived;
        }

        if (walkFailReason == "stuck")
        {
            return Outcome.Stuck;
        }

        // AI_Goto ended early (player bump / path fail / false arrival).
        // The machine is still here — keep the job and walk again.
        return Outcome.Retry;
    }
}
