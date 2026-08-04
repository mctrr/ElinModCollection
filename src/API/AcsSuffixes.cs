using System.Collections.Generic;

namespace ACS.API;

/// <summary>
/// suffixes 上真实 _acs_* clip 的统一查询（不含 ReservedSuffix 占位）。
/// </summary>
public static class AcsSuffixes
{
    /// <summary>是否存在至少一条真实 _acs_* 数据（Value 非 null）。</summary>
    public static bool HasRealClip(Dictionary<string, SpriteData>? suffixes)
    {
        if (suffixes is null || suffixes.Count == 0) {
            return false;
        }

        foreach (var kv in suffixes) {
            if (kv.Key is null || kv.Value is null) {
                continue;
            }

            // ReservedSuffix 只是占位（Value 常为 null），这里已被 Value 检查跳过
            if (kv.Key.StartsWith("_acs_")) {
                return true;
            }
        }

        return false;
    }

    public static bool HasRealClip(SpriteReplacer? replacer)
        => HasRealClip(replacer?.suffixes);

    /// <summary>
    /// Resolve ACS suffixes for the current base skin only.
    /// Equipped custom skin never silently falls back to race / sourceCard.replacer clips
    /// (that pull is what made combat play the original skin after a no-combat reskin).
    /// </summary>
    public static Dictionary<string, SpriteData>? Resolve(Chara? owner)
    {
        if (owner is null) {
            return null;
        }

        // Custom base id is the source of truth. Do NOT trust a stale spriteReplacer first:
        // after reskin, bound may still be race / previous skin and would leak old combat ACS.
        if (!owner.c_idSpriteReplacer.IsEmpty()) {
            string baseId = NormalizeBaseId(owner.c_idSpriteReplacer);
            if (!baseId.IsEmpty()
                && SpriteReplacer.dictSkins is not null
                && SpriteReplacer.dictSkins.TryGetValue(baseId, out var skin)
                && HasRealClip(skin)) {
                // Keep runtime instance on the current base (NPC sourceCard.replacer is separate).
                if (!ReferenceEquals(owner.spriteReplacer, skin)) {
                    owner.spriteReplacer = skin;
                }

                return skin.suffixes;
            }

            // dictSkins miss (cold start): allow bound only if it is not the race replacer.
            var bound = owner.spriteReplacer;
            if (HasRealClip(bound)
                && !ReferenceEquals(bound, owner.sourceCard?.replacer)) {
                return bound!.suffixes;
            }

            // Custom base equipped but no real _acs_* : stay on base look.
            // null -> render keeps current / static base; never race combat ACS.
            return null;
        }

        // No custom base: bound first (rare non-c_id bind), then race replacer.
        if (HasRealClip(owner.spriteReplacer)) {
            return owner.spriteReplacer!.suffixes;
        }

        var race = owner.sourceCard?.replacer;
        return HasRealClip(race) ? race!.suffixes : null;
    }

    private static string NormalizeBaseId(string id)
    {
        // Keep Resolve free of Patches dependency; mirror PcSkinPatch.NormalizeSkinId essentials.
        if (id.IsEmpty()) {
            return id;
        }

        try {
            if (id.IndexOfAny(new[] { '/', '\\' }) >= 0) {
                id = System.IO.Path.GetFileName(id);
            }
        }
        catch {
            // ignore
        }

        if (id.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)
            || id.EndsWith(".PNG", System.StringComparison.Ordinal)) {
            id = System.IO.Path.GetFileNameWithoutExtension(id);
        }

        int hash = id.IndexOf('#');
        if (hash >= 0) {
            id = id.Substring(0, hash);
        }

        int acsIdx = id.IndexOf("_acs_");
        if (acsIdx > 0) {
            id = id.Substring(0, acsIdx);
        }

        const string staticCombat = "_bcs_combat";
        int staticCombatIdx = id.IndexOf(staticCombat, System.StringComparison.Ordinal);
        if (staticCombatIdx > 0) {
            id = id.Substring(0, staticCombatIdx);
        }

        return id;
    }

    public static int CountClips(Dictionary<string, SpriteData>? suffixes)
    {
        if (suffixes is null) {
            return 0;
        }

        int n = 0;
        foreach (var key in suffixes.Keys) {
            if (key != null && key.StartsWith("_acs_")) {
                n++;
            }
        }

        return n;
    }

    public static int CountClips(SpriteReplacer? replacer)
        => CountClips(replacer?.suffixes);
}
