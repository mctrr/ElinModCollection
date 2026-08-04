using System;
using System.Collections.Generic;
using System.IO;
using ACS.API;

namespace ACS.Patches;

/// <summary>
/// Apply StaticCombatMap [combat] rows onto dictSkins as _bcs_combat.
/// Native {base}_bcs_combat always wins; map only fills missing slots.
/// </summary>
internal static class StaticCombatMapApply
{
    /// <returns>Newly applied (baseId, combatData) pairs for NPC broadcast.</returns>
    internal static List<(string BaseId, SpriteData Data)> ApplyToDictSkins(
        Dictionary<string, SpriteReplacer> dictSkins)
    {
        var applied = new List<(string BaseId, SpriteData Data)>();
        if (dictSkins is null) {
            return applied;
        }

        StaticCombatMap.Refresh(CollectSkinDirs(dictSkins));
        var map = StaticCombatMap.CombatMappings;
        if (map.Count == 0) {
            return applied;
        }

        foreach (var pair in map) {
            string baseId = pair.Key;
            string sourceId = pair.Value;
            if (baseId.IsEmpty() || sourceId.IsEmpty()) {
                continue;
            }

            try {
                if (dictSkins.TryGetValue(baseId, out var existingBase)
                    && HasStaticCombat(existingBase)) {
                    continue;
                }

                if (!dictSkins.TryGetValue(sourceId, out var sourceSkin) || sourceSkin is null) {
                    AcsMod.Warn($"static combat map source '{sourceId}' not found for base '{baseId}'");
                    continue;
                }

                var combatData = GetBaseSpriteData(sourceSkin);
                if (combatData is null || combatData.path.IsEmpty()) {
                    AcsMod.Warn($"static combat map source '{sourceId}' has no sprite data for base '{baseId}'");
                    continue;
                }

                if (!dictSkins.TryGetValue(baseId, out var baseSkin) || baseSkin is null) {
                    baseSkin = new SpriteReplacer {
                        data = combatData,
                    };
                    baseSkin.suffixes[""] = combatData;
                    baseSkin.suffixes[PcSkinPatch.StaticCombatSuffix] = combatData;
                    dictSkins[baseId] = baseSkin;
                    applied.Add((baseId, combatData));
                    AcsMod.Log($"skin '{baseId}' created from map source '{sourceId}' with static combat");
                    continue;
                }

                if (baseSkin.suffixes is null || HasStaticCombat(baseSkin)) {
                    continue;
                }

                baseSkin.suffixes[PcSkinPatch.StaticCombatSuffix] = combatData;
                applied.Add((baseId, combatData));
                AcsMod.Log($"skin '{baseId}' static combat from map source '{sourceId}'");
            }
            catch (Exception ex) {
                AcsMod.Warn($"static combat map apply '{baseId}'<-'{sourceId}': {ex.Message}");
            }
        }

        return applied;
    }

    private static List<string> CollectSkinDirs(Dictionary<string, SpriteReplacer> dictSkins)
    {
        var dirs = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in dictSkins) {
            var skin = kv.Value;
            string? path = skin?.data?.path;
            if (path.IsEmpty()) {
                if (skin?.suffixes is not null
                    && skin.suffixes.TryGetValue("", out var baseData)
                    && baseData is not null) {
                    path = baseData.path;
                }
            }

            if (path.IsEmpty()) {
                continue;
            }

            try {
                string? dir = Path.GetDirectoryName(path);
                if (dir.IsEmpty()) {
                    continue;
                }

                string full = Path.GetFullPath(dir);
                if (seen.Add(full)) {
                    dirs.Add(full);
                }
            }
            catch (Exception) {
                // ignore bad paths
            }
        }

        return dirs;
    }

    private static bool HasStaticCombat(SpriteReplacer? skin)
    {
        if (skin?.suffixes is null) {
            return false;
        }

        return skin.suffixes.TryGetValue(PcSkinPatch.StaticCombatSuffix, out var data)
            && data is not null
            && !data.path.IsEmpty();
    }

    private static SpriteData? GetBaseSpriteData(SpriteReplacer skin)
    {
        if (skin.suffixes is not null
            && skin.suffixes.TryGetValue("", out var baseData)
            && baseData is not null
            && !baseData.path.IsEmpty()) {
            return baseData;
        }

        if (skin.data is not null && !skin.data.path.IsEmpty()) {
            return skin.data;
        }

        return null;
    }
}
