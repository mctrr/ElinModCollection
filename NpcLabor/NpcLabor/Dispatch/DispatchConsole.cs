using System;
using System.Collections.Generic;
using System.Text;
using ReflexCLI.Attributes;
using NpcLabor.TownLabor;

namespace NpcLabor.Dispatch;

/// <summary>
/// Reflex console commands — same pattern as BCS PcSkinCommands:
///   [ConsoleCommand("")] on public static methods, method name = command name.
///   Assembly registered via CommandRegistry.assemblies.Add in Plugin.
/// Usage in LayerConsole:
///   NpcLaborHarvest
///   NpcLaborComplete          (dispatch + town labor)
///   NpcLaborCompleteOne <id|uid>
///   NpcLaborExp               (exp preview / to-next for active missions)
/// </summary>
[ConsoleCommandClassCustomizer("")]
internal static class DispatchConsole
{
    /// <summary>Show planned full success harvest for all active dispatch missions.</summary>
    [ConsoleCommand("")]
    public static string NpcLaborHarvest()
    {
        try
        {
            return DungeonDispatchManager.DebugDumpSuccessHarvests();
        }
        catch (System.Exception ex)
        {
            Plugin.LogWarn("NpcLaborHarvest: " + ex.Message);
            return "NpcLaborHarvest failed: " + ex.Message;
        }
    }

    /// <summary>Force-complete all active dispatch + town labor missions as success.</summary>
    [ConsoleCommand("")]
    public static string NpcLaborComplete()
    {
        try
        {
            int d = DungeonDispatchManager.DebugCompleteAllSuccess();
            int t = TownLaborManager.DebugCompleteAll();
            return "completed dispatch " + d + " / townlabor " + t;
        }
        catch (System.Exception ex)
        {
            Plugin.LogWarn("NpcLaborComplete: " + ex.Message);
            return "NpcLaborComplete failed: " + ex.Message;
        }
    }

    /// <summary>Force-complete one dispatch or town labor mission by id/uid.</summary>
    [ConsoleCommand("")]
    public static string NpcLaborCompleteOne(int missionIdOrCharaUid)
    {
        try
        {
            if (DungeonDispatchManager.DebugCompleteOne(missionIdOrCharaUid))
            {
                return "completed dispatch ref " + missionIdOrCharaUid;
            }

            if (TownLaborManager.DebugCompleteOne(missionIdOrCharaUid))
            {
                return "completed townlabor ref " + missionIdOrCharaUid;
            }

            return "mission not found: " + missionIdOrCharaUid;
        }
        catch (System.Exception ex)
        {
            Plugin.LogWarn("NpcLaborCompleteOne: " + ex.Message);
            return "NpcLaborCompleteOne failed: " + ex.Message;
        }
    }

    /// <summary>Live create probe for beach sand / copper / iron / salt identities.</summary>
    [ConsoleCommand("")]
    public static string NpcLaborProbe()
    {
        try
        {
            return DungeonDispatchRewards.DebugProbeMaterialCreates();
        }
        catch (System.Exception ex)
        {
            Plugin.LogWarn("NpcLaborProbe: " + ex.Message);
            return "NpcLaborProbe failed: " + ex.Message;
        }
    }

    /// <summary>
    /// Dump expected skill exp gains and current to-next for active dispatch / town labor workers.
    /// Does not apply exp.
    /// </summary>
    [ConsoleCommand("")]
    public static string NpcLaborExp()
    {
        try
        {
            var sb = new StringBuilder();
            int n = 0;

            foreach (DungeonDispatchMission m in DungeonDispatchManager.All)
            {
                if (m == null)
                {
                    continue;
                }

                n++;
                int weeks = 1;
                try
                {
                    if (m.isRegion)
                    {
                        weeks = Math.Max(1, Math.Min(4, m.exploreWeeks <= 0 ? 1 : m.exploreWeeks));
                    }
                }
                catch
                {
                    weeks = 1;
                }

                string title = m.isRegion
                    ? ("region " + (m.regionKind ?? "?") + " x" + weeks + "w")
                    : ("dungeon danger" + m.dangerLv);
                sb.AppendLine("#" + m.missionId + " " + title
                    + " lockpickChests=" + DungeonDispatchRewards.PlannedLockpickChestCount(m)
                    + " bossLv=" + (m.isRegion ? "-" : DungeonDispatchRewards.BossRewardLv(m).ToString()));

                List<Chara> members;
                try { members = m.GetMembers(); }
                catch { members = new List<Chara>(); }

                if (members == null || members.Count == 0)
                {
                    sb.AppendLine("  (no members resolved)");
                    continue;
                }

                foreach (Chara c in members)
                {
                    sb.AppendLine("  " + PreviewDispatchExp(c, m, weeks));
                }
            }

            foreach (TownLaborMission m in TownLaborManager.All)
            {
                if (m == null)
                {
                    continue;
                }

                n++;
                int hours = Math.Max(1, m.hoursTotal > 0 ? m.hoursTotal : 8);
                int exp = TownLaborRewards.ExpPerHour * hours;
                Chara? worker = m.GetWorker();
                string wname = m.workerName;
                string skillBit = "skill" + m.skillId + "+" + exp;
                if (worker != null && m.skillId > 0)
                {
                    skillBit = FormatSkillPreview(worker, m.skillId, exp);
                }

                int previewMoney = TownLaborRewards.CalcPreviewMoney(Math.Max(0, m.clientInvest), hours);
                sb.AppendLine("#TL" + m.missionId + " " + m.jobTitle + " " + hours + "h shopInv" + m.clientInvest
                    + " skill" + m.workerSkill + "  " + wname + " " + skillBit
                    + " plat=" + TownLaborRewards.CalcRewardPlat(m.clientInvest)
                    + " pay=" + previewMoney);
            }

            if (n == 0)
            {
                return "no active missions";
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("NpcLaborExp: " + ex.Message);
            return "NpcLaborExp failed: " + ex.Message;
        }
    }

    static string PreviewDispatchExp(Chara c, DungeonDispatchMission m, int weeks)
    {
        int exploreBase = LaborConfig.DispatchExpExplore(DispatchSettleKind.Success);
        int lockpickBase = LaborConfig.DispatchExpLockpick(DispatchSettleKind.Success);
        int gatherBase = LaborConfig.DispatchExpGather(DispatchSettleKind.Success);
        int explore = exploreBase * weeks;
        int lockpick = lockpickBase * weeks;
        int gather = gatherBase * weeks;
        int specialtyId = 0;
        int specialtyAmt = 0;
        string specialtyLabel = "";
        int specialtyMult = LaborConfig.RegionSpecialtyMult;
        if (m.isRegion && specialtyMult > 1)
        {
            string rk = (m.regionKind ?? "").Trim().ToLowerInvariant();
            if (rk == "field")
            {
                rk = "plain";
            }

            int extra = exploreBase * weeks * (specialtyMult - 1);
            switch (rk)
            {
                case "plain":
                    specialtyId = DungeonDispatchMission.SkillExplore;
                    specialtyAmt = extra;
                    specialtyLabel = "explore*" + specialtyMult;
                    break;
                case "mountain":
                    specialtyId = DungeonDispatchMission.SkillMining;
                    specialtyAmt = exploreBase * weeks * specialtyMult;
                    specialtyLabel = "mining";
                    break;
                case "forest":
                    specialtyId = DungeonDispatchMission.SkillLumber;
                    specialtyAmt = exploreBase * weeks * specialtyMult;
                    specialtyLabel = "lumber";
                    break;
                case "beach":
                    specialtyId = DungeonDispatchMission.SkillDigging;
                    specialtyAmt = exploreBase * weeks * specialtyMult;
                    specialtyLabel = "digging";
                    break;
            }
        }

        int exploreTotal = explore + (specialtyId == DungeonDispatchMission.SkillExplore ? specialtyAmt : 0);
        string name;
        try { name = c.NameSimple ?? c.Name ?? ("#" + c.uid); }
        catch { name = "#" + c.uid; }

        var parts = new List<string>();
        parts.Add(FormatSkillPreview(c, DungeonDispatchMission.SkillExplore, exploreTotal));
        parts.Add(FormatSkillPreview(c, DungeonDispatchMission.SkillLockpick, lockpick));
        parts.Add(FormatSkillPreview(c, DungeonDispatchMission.SkillGather, gather));
        if (specialtyId > 0 && specialtyId != DungeonDispatchMission.SkillExplore && specialtyAmt > 0)
        {
            parts.Add(specialtyLabel + " " + FormatSkillPreview(c, specialtyId, specialtyAmt));
        }

        return name + " x" + weeks + "w: " + string.Join(" / ", parts);
    }

    static string FormatSkillPreview(Chara c, int skillId, int gained)
    {
        int cur = 0;
        int toNext = 1000;
        int lv = 0;
        string label = "#" + skillId;
        try
        {
            Element? el = c.elements?.GetElement(skillId);
            if (el != null)
            {
                try { cur = el.vExp; } catch { }
                try { toNext = el.ExpToNext; } catch { toNext = 1000; }
                try { lv = el.Value; } catch { lv = c.Evalue(skillId); }
                try { label = el.Name ?? label; } catch { }
            }
            else
            {
                lv = c.Evalue(skillId);
            }
        }
        catch
        {
            try { lv = c.Evalue(skillId); } catch { }
        }

        int remain = Math.Max(0, toNext - cur);
        return label + "+" + gained + "(lv" + lv + " need" + remain + ")";
    }

    /// <summary>Alias: same as NpcLaborComplete (dispatch + town labor).</summary>
    [ConsoleCommand("")]
    public static string NpcLaborTownComplete()
    {
        return NpcLaborComplete();
    }
}