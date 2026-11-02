using System.Collections.Generic;

namespace NpcLabor.CoCraft;

/// <summary>Which of the NPC's hobbies/works matches the craft/machine skill.</summary>
internal enum HobbyWorkKind
{
    None,
    Hobby,
    Work
}

/// <summary>
/// Relevant hobby/work check for the co-craft + processor picker rows.
/// Elin models both "hobbies" and player-assigned "works" as SourceHobby rows
/// (Chara._hobbies / Chara._works, same source used by vanilla HasHobbyOrWork).
/// A row matches the current craft/machine skill when its elements[] flat
/// [id,value,...] array contains that skill id, or its skill alias resolves to it.
/// </summary>
internal static class HobbyTag
{
    /// <summary>
    /// Hobby when a hobby matches, Work when only an assigned work matches, else None.
    /// </summary>
    internal static HobbyWorkKind GetRelevantKind(Chara? c, int skillId)
    {
        if (c == null || skillId <= 0)
        {
            return HobbyWorkKind.None;
        }

        return GetRelevantKind(c, new int[] { skillId });
    }

    /// <summary>
    /// Hobby when a hobby matches any of the skills, Work when only an assigned
    /// work matches, else None. Used by pickers with several relevant skills
    /// (dispatch), where the single-skill overload is not enough.
    /// </summary>
    internal static HobbyWorkKind GetRelevantKind(Chara? c, IReadOnlyCollection<int> skillIds)
    {
        if (c == null || skillIds == null || skillIds.Count == 0)
        {
            return HobbyWorkKind.None;
        }

        try
        {
            // Vanilla lazy-init: both lists are rolled together.
            if (c._hobbies == null || c._works == null)
            {
                c.RerollHobby();
            }

            bool anyWork = false;
            foreach (int skillId in skillIds)
            {
                if (skillId <= 0)
                {
                    continue;
                }

                if (Scan(c._hobbies, skillId))
                {
                    return HobbyWorkKind.Hobby;
                }

                if (Scan(c._works, skillId))
                {
                    anyWork = true;
                }
            }

            return anyWork ? HobbyWorkKind.Work : HobbyWorkKind.None;
        }
        catch (System.Exception __e) { Plugin.LogDebug("HobbyTag.cs silent catch: " + __e.Message); }

        return HobbyWorkKind.None;
    }

    static bool Scan(List<int>? ids, int skillId)
    {
        if (ids == null || ids.Count == 0)
        {
            return false;
        }

        foreach (int id in ids)
        {
            SourceHobby.Row? row = null;
            try
            {
                if (EClass.sources.hobbies.map.TryGetValue(id, out SourceHobby.Row? r))
                {
                    row = r;
                }
            }
            catch { }

            if (row != null && MatchesSkill(row, skillId))
            {
                return true;
            }
        }

        return false;
    }

    static bool MatchesSkill(SourceHobby.Row row, int skillId)
    {
        // elements is a flat [id, value, id, value, ...] array (Core.ParseElements).
        if (row.elements != null)
        {
            for (int i = 0; i + 1 < row.elements.Length; i += 2)
            {
                if (row.elements[i] == skillId)
                {
                    return true;
                }
            }
        }

        // skill holds a skill alias string (Card.Evalue(string) resolves it the
        // same way: EClass.sources.elements.alias[alias].id).
        if (!string.IsNullOrEmpty(row.skill))
        {
            try
            {
                if (EClass.sources.elements.alias.TryGetValue(row.skill, out SourceElement.Row? er)
                    && er != null && er.id == skillId)
                {
                    return true;
                }
            }
            catch { }
        }

        return false;
    }
}
