using NpcLabor.CoCraft;
using NpcLabor.TownLabor;

namespace NpcLabor.Process;

/// <summary>
/// Runtime processor operator mode for LayerDragGrid (slice B 加工).
/// null = off (PC processes). uid = pinned worker. AutoSentinel = auto-pick.
/// Player-facing name is LaborTerms.Process ("加工"), not 外包.
/// </summary>
internal static class ProcessorOutsourceMode
{
    /// <summary>null = 自己(PC); AutoSentinel = 自动选人; uid = 钉选 NPC。</summary>
    internal static int? ModeUid;

    internal const int AutoSentinel = -100;

    internal static bool IsOff => !ModeUid.HasValue;

    internal static bool IsAuto => ModeUid == AutoSentinel;

    internal static bool IsEnabled => ModeUid.HasValue;

    internal static void SetOff()
    {
        ModeUid = null;
    }

    internal static void SetAuto()
    {
        ModeUid = AutoSentinel;
    }

    internal static void SetPin(int uid)
    {
        Chara? c = RefChara.Get(uid);
        if (!AssistantResolver.IsValidAssistant(c))
        {
            ModeUid = null;
            return;
        }

        ModeUid = uid;
    }

    internal static Chara? GetPinned()
    {
        if (!ModeUid.HasValue || ModeUid == AutoSentinel)
        {
            return null;
        }

        Chara? c = RefChara.Get(ModeUid.Value);
        if (!AssistantResolver.IsValidAssistant(c))
        {
            ModeUid = null;
            return null;
        }

        return c;
    }

    /// <summary>Resolve worker for a machine skill. Off → null.</summary>
    internal static Chara? ResolveWorker(int skillId)
    {
        if (IsOff)
        {
            return null;
        }

        if (IsAuto)
        {
            return FindBest(skillId);
        }

        Chara? pinned = GetPinned();
        if (pinned == null)
        {
            return null;
        }

        if (skillId > 0 && pinned.Evalue(skillId) <= 0)
        {
            return null;
        }

        if (pinned.ai is AI_NpcProcess)
        {
            return null;
        }

        return pinned;
    }

    internal static Chara? FindBest(int skillId)
    {
        Chara? best = null;
        int bestSkill = 0;
        foreach (Chara c in AssistantResolver.EnumerateEligible())
        {
            if (c.ai is AI_NpcProcess)
            {
                continue;
            }

            try
            {
                if (LaborBusy.IsBusy(c.uid))
                {
                    continue;
                }
            }
            catch
            {
            }

            int skill = skillId > 0 ? c.Evalue(skillId) : 1;
            if (skillId > 0 && skill <= 0)
            {
                continue;
            }

            if (best == null || skill > bestSkill || (skill == bestSkill && c.uid < best.uid))
            {
                best = c;
                bestSkill = skill;
            }
        }

        return best;
    }

    internal static string Describe(int skillId = 0)
    {
        if (IsOff)
        {
            return NpcLabor.LaborText.T("proc.mode.self");
        }

        if (IsAuto)
        {
            Chara? auto = FindBest(skillId);
            if (auto == null)
            {
                return NpcLabor.LaborText.T("proc.mode.autoNone");
            }

            return NpcLabor.LaborText.T("proc.mode.auto");
        }

        Chara? pinned = GetPinned();
        if (pinned == null)
        {
            return NpcLabor.LaborText.T("proc.mode.self");
        }

        string name = AssistantResolver.NameOf(pinned);
        if (name.Length > 4)
        {
            name = name.Substring(0, 4);
        }

        return name;
    }
}
