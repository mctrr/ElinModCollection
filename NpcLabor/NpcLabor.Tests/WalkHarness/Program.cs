using NpcLabor.Process;

static class Program
{
    static int fails;

    static void Expect(string name, ProcessorWalk.Outcome actual, ProcessorWalk.Outcome expected)
    {
        if (actual == expected)
        {
            Console.WriteLine("PASS " + name);
            return;
        }

        fails++;
        Console.WriteLine("FAIL " + name + " expected=" + expected + " actual=" + actual);
    }

    static int Main()
    {
        // Steam report: player meets the NPC on the way; machine is still there.
        Expect(
            "player-bump-keeps-job",
            ProcessorWalk.Classify(workerLost: false, machineGone: false, dist: 8, walkFailReason: null),
            ProcessorWalk.Outcome.Retry);

        // AI_Goto.Restart hits MaxRestart and returns Success without arriving.
        Expect(
            "goto-false-success-keeps-job",
            ProcessorWalk.Classify(workerLost: false, machineGone: false, dist: 4, walkFailReason: null),
            ProcessorWalk.Outcome.Retry);

        Expect(
            "goto-fail-keeps-job",
            ProcessorWalk.Classify(workerLost: false, machineGone: false, dist: 4, walkFailReason: "ai-fail"),
            ProcessorWalk.Outcome.Retry);

        Expect(
            "stuck-gives-up",
            ProcessorWalk.Classify(workerLost: false, machineGone: false, dist: 5, walkFailReason: "stuck"),
            ProcessorWalk.Outcome.Stuck);

        Expect(
            "true-machine-gone",
            ProcessorWalk.Classify(workerLost: false, machineGone: true, dist: 1, walkFailReason: null),
            ProcessorWalk.Outcome.MachineGone);

        Expect(
            "arrived",
            ProcessorWalk.Classify(workerLost: false, machineGone: false, dist: 1, walkFailReason: null),
            ProcessorWalk.Outcome.Arrived);

        Expect(
            "on-tile",
            ProcessorWalk.Classify(workerLost: false, machineGone: false, dist: 0, walkFailReason: null),
            ProcessorWalk.Outcome.Arrived);

        Expect(
            "worker-lost",
            ProcessorWalk.Classify(workerLost: true, machineGone: false, dist: 3, walkFailReason: null),
            ProcessorWalk.Outcome.WorkerLost);

        if (fails != 0)
        {
            Console.WriteLine("FAILED " + fails);
            return 1;
        }

        Console.WriteLine("OK");
        return 0;
    }
}
