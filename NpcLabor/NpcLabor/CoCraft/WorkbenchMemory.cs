using System;

namespace NpcLabor.CoCraft;

/// <summary>
/// Per-workbench last-worker memory, persisted via Card.SetInt(string, int).
/// Keys: "npclabor_cocraft" / "npclabor_processor".
/// Values: 0/missing = Off; -100 = Auto; positive = NPC uid.
/// </summary>
internal static class WorkbenchMemory
{
    const string KeyCoCraft = "npclabor_cocraft";
    const string KeyProcessor = "npclabor_processor";

    // --- Co-craft (factory LayerCraft) ---

    internal static int? ReadCoCraft(Card workbench)
    {
        if (workbench == null) return null;
        int v = workbench.GetInt(KeyCoCraft);
        return v == 0 ? null : v;
    }

    internal static void WriteCoCraft(Card workbench, int? mode)
    {
        if (workbench == null) return;
        workbench.SetInt(KeyCoCraft, mode ?? 0);
    }

    // --- Processor (LayerDragGrid machine) ---

    internal static int? ReadProcessor(Card workbench)
    {
        if (workbench == null) return null;
        int v = workbench.GetInt(KeyProcessor);
        return v == 0 ? null : v;
    }

    internal static void WriteProcessor(Card workbench, int? mode)
    {
        if (workbench == null) return;
        workbench.SetInt(KeyProcessor, mode ?? 0);
    }
}
