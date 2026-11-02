using System.Collections.Generic;
using NpcLabor.TownLabor;

namespace NpcLabor.CoCraft;

internal static class AssistantResolver
{
    /// <summary>
    /// Runtime pin only (v1 non-save).
    /// null / OffSentinel = Off (default; canceling the picker must not auto-enable assist).
    /// AutoSentinel = Auto best-match.
    /// positive uid = pinned NPC.
    /// </summary>
    internal static int? PinnedUid;

    internal const int OffSentinel = -1;
    internal const int AutoSentinel = -100;

    /// <summary>Off is both explicit OffSentinel and default null.</summary>
    internal static bool IsOff => !PinnedUid.HasValue || PinnedUid == OffSentinel;

    internal static bool IsAuto => PinnedUid == AutoSentinel;

    internal static bool IsValidAssistant(Chara? c)
    {
        if (c == null)
        {
            return false;
        }

        // Never offer the PC as an assistant/worker. Guard every known PC identity path
        // so party/branch enumeration cannot produce a second "me" row.
        if (PersonPickerUi.IsPcLike(c) || c.isDead)
        {
            return false;
        }

        if (PersonPickerUi.IsStayHomeUnique(c))
        {
            return false;
        }

        if (!c.IsAliveInCurrentZone)
        {
            return false;
        }

        try
        {
            if (LaborBusy.IsBusy(c.uid))
            {
                return false;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("AssistantResolver.cs silent catch: " + __e.Message); }
return true;
    }

    internal static Chara? GetPinned()
    {
        if (!PinnedUid.HasValue || PinnedUid == OffSentinel || PinnedUid == AutoSentinel)
        {
            return null;
        }

        Chara? c = RefChara.Get(PinnedUid.Value);
        if (!IsValidAssistant(c))
        {
            // Invalid pin falls back to Off, never Auto.
            PinnedUid = OffSentinel;
            return null;
        }

        return c;
    }

    internal static bool IsPartyMember(Chara c)
    {
        Party? party = EClass.pc?.party;
        if (party?.members == null)
        {
            return false;
        }

        return party.members.Contains(c) || c.IsPCParty;
    }

    internal static bool IsResident(Chara c)
    {
        if (c.IsPCFaction)
        {
            return true;
        }

        FactionBranch? branch = EClass.BranchOrHomeBranch ?? EClass.Branch;
        if (branch?.members != null && branch.members.Contains(c))
        {
            return true;
        }

        return false;
    }

    /// <summary>Party companions first, then same-zone residents/faction members.</summary>
    internal static IEnumerable<Chara> EnumerateEligible()
    {
        var seen = new HashSet<int>();

        Party? party = EClass.pc?.party;
        if (party?.members != null)
        {
            foreach (Chara member in party.members)
            {
                if (!IsValidAssistant(member) || !seen.Add(member.uid))
                {
                    continue;
                }

                yield return member;
            }
        }

        FactionBranch? branch = EClass.BranchOrHomeBranch ?? EClass.Branch;
        if (branch?.members != null)
        {
            foreach (Chara member in branch.members)
            {
                if (!IsValidAssistant(member) || !seen.Add(member.uid))
                {
                    continue;
                }

                yield return member;
            }
        }

        if (EClass._map?.charas != null)
        {
            foreach (Chara c in EClass._map.charas)
            {
                if (!IsValidAssistant(c) || !IsResident(c) || !seen.Add(c.uid))
                {
                    continue;
                }

                yield return c;
            }
        }
    }

    internal static List<Chara> ListEligible()
    {
        // Party companions first, then stable uid order. Auto mode picks the
        // highest-skill NPC (FindBestForSkill), so the list itself is not sorted
        // by skill — that ordering never ran (callers pass no skillId).
        var list = new List<Chara>();
        foreach (Chara c in EnumerateEligible())
        {
            list.Add(c);
        }

        list.Sort((a, b) =>
        {
            int pa = IsPartyMember(a) ? 0 : 1;
            int pb = IsPartyMember(b) ? 0 : 1;
            int cmp = pa.CompareTo(pb);
            return cmp != 0 ? cmp : a.uid.CompareTo(b.uid);
        });
        return list;
    }

    internal static Chara? FindBestForSkill(int skillId)
    {
        Chara? best = null;
        int bestSkill = 0;
        foreach (Chara c in EnumerateEligible())
        {
            int skill = skillId > 0 ? c.Evalue(skillId) : 0;
            // Skill 0 means "cannot meaningfully assist this recipe".
            if (skill <= 0)
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

    /// <summary>pin (if valid and skill>0) / auto best / none. Default Off never auto-picks.</summary>
    internal static Chara? Resolve(int skillId)
    {
        if (IsOff)
        {
            return null;
        }

        if (IsAuto)
        {
            return FindBestForSkill(skillId);
        }

        Chara? pinned = GetPinned();
        if (pinned != null)
        {
            if (skillId <= 0 || pinned.Evalue(skillId) > 0)
            {
                return pinned;
            }

            // Pinned NPC has 0 in this recipe skill - do not use them.
            return null;
        }

        // Unknown/corrupt pin state: stay Off rather than inventing Auto.
        return null;
    }

    internal static void SetPin(int? uid)
    {
        if (!uid.HasValue || uid == OffSentinel)
        {
            PinnedUid = OffSentinel;
            return;
        }

        if (uid == AutoSentinel)
        {
            PinnedUid = AutoSentinel;
            return;
        }

        Chara? c = RefChara.Get(uid.Value);
        if (!IsValidAssistant(c))
        {
            PinnedUid = OffSentinel;
            return;
        }

        PinnedUid = uid;
    }

    internal static string NameOf(Chara c)
        => c.NameSimple ?? c.Name ?? ("#" + c.uid);

    internal static Chara? ResolveForCraft(int skillId)
    {
        if (IsOff)
        {
            return null;
        }

        return Resolve(skillId);
    }

    /// <summary>Preview eff without opening a session. Uses raw PC skill + npc contribution.</summary>
    internal static bool TryPreviewEff(int skillId, out int pcSkill, out int npcSkill, out int eff, out Chara? assistant)
    {
        pcSkill = 0;
        npcSkill = 0;
        eff = 0;
        assistant = null;

        if (skillId <= 0 || EClass.pc == null)
        {
            return false;
        }

        // Read raw PC skill (bypass any active rewrite by using elements.Value when possible).
        Element? el = EClass.pc.elements?.GetOrCreateElement(skillId);
        pcSkill = el != null ? el.Value : EClass.pc.Evalue(skillId);

        assistant = ResolveForCraft(skillId);
        if (assistant == null)
        {
            eff = pcSkill;
            return false;
        }

        npcSkill = assistant.Evalue(skillId);
        eff = pcSkill + UnityEngine.Mathf.FloorToInt(npcSkill * CoCraftSession.NpcSkillWeight);
        return true;
    }
}
