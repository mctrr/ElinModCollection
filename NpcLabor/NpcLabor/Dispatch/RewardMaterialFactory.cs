using System;
using System.Collections.Generic;
using UnityEngine;

namespace NpcLabor.Dispatch;

/// <summary>
/// PARTIAL: material/thing factory + predicates.
/// Split from DungeonDispatchRewards.cs (2026-08-07).
/// </summary>
internal static partial class DungeonDispatchRewards
{
    /// <summary>
    /// Resolved material ids by alias. Game source data is static at runtime, so a
    /// per-alias cache avoids repeated dictionary lookups in hot predicate paths.
    /// </summary>
    static readonly Dictionary<string, int> MaterialIdCache = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// True when the thing's live material id matches one of the given aliases.
    /// Prefer this over name matching: id equality is authoritative, name contains
    /// checks are only a last-resort fallback for rows without a resolvable material.
    /// </summary>
    static bool HasMaterialAlias(Thing t, params string[] aliases)
    {
        if (t == null || aliases == null || aliases.Length == 0)
        {
            return false;
        }

        int id = LiveMaterialId(t);
        if (id <= 0)
        {
            return false;
        }

        for (int i = 0; i < aliases.Length; i++)
        {
            if (ResolveMaterialId(aliases[i]) == id)
            {
                return true;
            }
        }

        return false;
    }

    static bool IsScrapLikeThing(Thing t)
    {
        if (t == null)
        {
            return false;
        }


            string id = (t.id ?? "").ToLowerInvariant();
            string nm = "";
            try { nm = (t.NameOne ?? t.NameSimple ?? t.Name ?? "").ToLowerInvariant(); } catch { nm = ""; }
            if (id == "scrap" || id.Contains("scrap") || id == "microchip")
            {
                return true;
            }

            if (nm.Contains("\u5e9f\u94c1") || nm.Contains("scrap"))
            {
                return true;
            }

return false;
    }


    static void StripSaltLikeThings(List<Thing> things)
    {
        if (things == null || things.Count == 0)
        {
            return;
        }

        for (int i = things.Count - 1; i >= 0; i--)
        {
            Thing t = things[i];
            if (t == null)
            {
                things.RemoveAt(i);
                continue;
            }

            if (!IsSaltLikeThing(t))
            {
                continue;
            }

             if (t.parent == null) t.Destroy();
            things.RemoveAt(i);
        }
    }


    static void StripGoldLikeThings(List<Thing> things)
    {
        if (things == null || things.Count == 0)
        {
            return;
        }

        for (int i = things.Count - 1; i >= 0; i--)
        {
            Thing t = things[i];
            if (t == null)
            {
                things.RemoveAt(i);
                continue;
            }

            if (!IsGoldLikeThing(t))
            {
                continue;
            }

             if (t.parent == null) t.Destroy();
            things.RemoveAt(i);
        }
    }


    static bool IsGoldLikeThing(Thing t)
    {
        if (t == null)
        {
            return false;
        }

        // Authoritative material-id check first; alias/name checks are fallback only.
        if (HasMaterialAlias(t, "gold", "platinum", "adamantite", "elder gold", "gold_elder", "orichalcum", "netherite"))
        {
            return true;
        }


            string id = (t.id ?? "").ToLowerInvariant();
            string nm = "";
            try { nm = (t.NameOne ?? t.NameSimple ?? t.Name ?? "").ToLowerInvariant(); } catch { nm = ""; }
            string mat = "";
            try { mat = t.material != null ? (t.material.alias ?? "").ToLowerInvariant() : ""; } catch { mat = ""; }

            if (IsForbiddenHighOreMaterial(mat) || mat == "elder gold")
            {
                return true;
            }

            if (id == "goldbar" || id.Contains("goldbar") || id.Contains("gold_bar"))
            {
                return true;
            }

            // Any ore-like carrier with gold-family name/mat counts.
            bool oreLike = id == "ore" || id.StartsWith("ore") || id == "rock" || id == "chunk"
                || id.Contains("ore") || nm.Contains("矿") || nm.Contains("ore");
            if (oreLike
                && (mat == "gold" || mat == "platinum" || mat == "adamantite"
                    || nm.Contains("金") || nm.Contains("gold") || nm.Contains("platinum")))
            {
                if (nm.Contains("铜") || nm.Contains("copper")
                    || ((nm.Contains("铁") || nm.Contains("iron")) && !nm.Contains("废") && !nm.Contains("金"))
                    || nm.Contains("sulf") || nm.Contains("硫"))
                {
                    return false;
                }

                return true;
            }

            if (nm.Contains("金矿") || nm.Contains("gold ore") || nm.Contains("platinum")
                || nm.Contains("精金") || nm.Contains("adamant"))
            {
                return true;
            }

return false;
    }


    static Thing? CreatePlasticOre()
    {
        string[] matAliases = { "plastic", "plastics", "resin" };
        for (int i = 0; i < matAliases.Length; i++)
        {
            string a = matAliases[i];
            if (EClass.sources?.materials?.alias != null
                && EClass.sources.materials.alias.ContainsKey(a))
            {
                Thing? t = null;
                try { t = ThingGen.Create("ore", a, 1); } catch { t = null; }
                if (t == null)
                {
                    t = CreateMaterialThing(a);
                }

                if (t != null && !IsMudLikeThing(t) && !IsGoldLikeThing(t))
                {
                    string mat = "";
                     mat = t.material != null ? (t.material.alias ?? "").ToLowerInvariant() : "";
                    if (mat.Contains("plast") || mat.Contains("resin") || !IsScrapLikeThing(t))
                    {
                        return t;
                    }

                     if (t.parent == null) t.Destroy();
                }
            }
        }

        string[] thingIds = { "plastic", "plastic_scrap", "plastics", "resin" };
        for (int i = 0; i < thingIds.Length; i++)
        {
            Thing? t = TryCreate(thingIds[i], 1);
            if (t != null && !IsMudLikeThing(t) && !IsGoldLikeThing(t))
            {
                return t;
            }

             if (t != null && t.parent == null) t.Destroy();
        }

        Thing? g = CreateMaterialThing("glass") ?? TryCreate("glass", 1) ?? TryCreate("chunk", 1);
        if (g != null && !IsMudLikeThing(g) && !IsGoldLikeThing(g))
        {
            return g;
        }

         if (g != null && g.parent == null) g.Destroy();
        return CreateMaterialThing("chromite") ?? CreateMaterialThing("mica");
    }


    static Thing? CreateMountainSulfur()
    {
        // Thing.xlsx id "sulfur" => CN 硫黄/硫磺. Never fake with crystal/gem stand-ins.
        // Trust id / CN name first: mud/scrap false-positives must not erase real sulfur.
        // Runtime also has sulfur as a material (mill recipes use pebble/rock + sulfur).
        void Reject(ref Thing? victim)
        {
            if (victim == null)
            {
                return;
            }

             if (victim.parent == null) victim.Destroy();
            victim = null;
        }

        bool LooksLikeSulfur(Thing t)
        {
            if (t == null)
            {
                return false;
            }


                string liveId = "";
                try { liveId = (t.id ?? "").ToLowerInvariant(); } catch { liveId = ""; }
                if (liveId == "sulfur" || liveId == "sulphur" || liveId == "brimstone" || liveId.Contains("sulf"))
                {
                    return true;
                }

                // ThingGen falls back to placeholder "869" when the card is missing.
                if (liveId == "869" || liveId == "scrap" || liveId.Contains("scrap"))
                {
                    return false;
                }

                string mat = "";
                try { mat = t.material != null ? (t.material.alias ?? "").ToLowerInvariant() : ""; } catch { mat = ""; }
                if (mat == "sulfur" || mat == "sulphur" || mat.Contains("sulf"))
                {
                    return true;
                }

                string nm = "";
                try { nm = t.NameOne ?? t.NameSimple ?? t.Name ?? ""; } catch { nm = ""; }
                string nmLow = nm.ToLowerInvariant();
                if (nmLow.Contains("sulf") || nmLow.Contains("sulphur") || nmLow.Contains("brimstone")
                    || nm.Contains("硫黄") || nm.Contains("硫磺") || nm.Contains("硫"))
                {
                    return true;
                }

return false;
        }

        string[] ids = { "sulfur", "sulphur", "brimstone" };
        for (int i = 0; i < ids.Length; i++)
        {
            string id = ids[i];
            Thing? t = null;
            try { t = ThingGen.Create(id, -1, 1); } catch { t = null; }
            if (t == null)
            {
                t = TryCreate(id, 1);
            }

            if (t != null)
            {
                if (LooksLikeSulfur(t))
                {
                    return t;
                }

                Reject(ref t);
            }
        }

        // Material form: recipes pin sulfur onto pebble/rock/chunk carriers.
        int sulfurMatId = ResolveMaterialId("sulfur");
        if (sulfurMatId <= 0)
        {
            sulfurMatId = ResolveMaterialId("sulphur");
        }

        if (sulfurMatId > 0)
        {
            Thing? matThing = CreateByMaterialId(
                sulfurMatId,
                preferredCarriers: new[] { "pebble", "rock", "chunk", "ore", "stone" });
            if (matThing != null)
            {
                if (LooksLikeSulfur(matThing) || LiveMaterialId(matThing) == sulfurMatId)
                {
                    return matThing;
                }

                Reject(ref matThing);
            }

            Thing? pinned = CreatePinnedMaterialThing("sulfur", "pebble", "rock", "chunk", "ore")
                ?? CreatePinnedMaterialThing("sulphur", "pebble", "rock", "chunk", "ore");
            if (pinned != null)
            {
                if (LooksLikeSulfur(pinned) || LiveMaterialId(pinned) == sulfurMatId)
                {
                    return pinned;
                }

                Reject(ref pinned);
            }
        }

        if (EClass.sources?.materials?.alias != null)
        {
            string[] matIds = { "sulfur", "sulphur", "brimstone" };
            for (int i = 0; i < matIds.Length; i++)
            {
                string id = matIds[i];
                if (!EClass.sources.materials.alias.ContainsKey(id))
                {
                    continue;
                }

                Thing? t = CreateMaterialThing(id);
                if (t != null)
                {
                    if (LooksLikeSulfur(t))
                    {
                        return t;
                    }

                    Reject(ref t);
                }
            }
        }

        // Card-map probe: accept only genuine sulfur rows, never the 869 placeholder.

            if (EClass.sources?.cards?.map != null && EClass.sources.cards.map.ContainsKey("sulfur"))
            {
                Thing? t = ThingGen.Create("sulfur");
                if (t != null)
                {
                    if (LooksLikeSulfur(t))
                    {
                        return t;
                    }

                    Reject(ref t);
                }
            }

Plugin.LogWarn("CreateMountainSulfur failed: sulfur card/material unavailable"
            + " matId=" + sulfurMatId
            + " hasCard=" + (EClass.sources?.cards?.map != null && EClass.sources.cards.map.ContainsKey("sulfur")));
        return null;
    }


    static bool IsSulfurLikeThing(Thing t)
    {
        if (t == null)
        {
            return false;
        }

        try
        {
            if (HasMaterialAlias(t, "sulfur", "sulphur", "brimstone"))
            {
                return true;
            }

            string id = (t.id ?? "").ToLowerInvariant();
            if (id == "sulfur" || id == "sulphur" || id == "brimstone" || id.Contains("sulf"))
            {
                return true;
            }

            string mat = "";
            try { mat = t.material != null ? (t.material.alias ?? "").ToLowerInvariant() : ""; } catch { mat = ""; }
            if (mat == "sulfur" || mat == "sulphur" || mat.Contains("sulf"))
            {
                return true;
            }


                int sulfurMatId = ResolveMaterialId("sulfur");
                if (sulfurMatId <= 0)
                {
                    sulfurMatId = ResolveMaterialId("sulphur");
                }

                if (sulfurMatId > 0 && t.material != null && t.material.id == sulfurMatId)
                {
                    return true;
                }

string nm = "";
            try { nm = t.NameOne ?? t.NameSimple ?? t.Name ?? ""; } catch { nm = ""; }
            string nmLow = nm.ToLowerInvariant();
            if (nmLow.Contains("sulf") || nmLow.Contains("sulphur") || nmLow.Contains("brimstone")
                || nm.Contains("硫黄") || nm.Contains("硫磺") || nm.Contains("硫"))
            {
                return true;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchRewards.cs silent catch: " + __e.Message); }
return false;
    }


    /// <summary>
    /// Named corpse meat. Prefer MakeFoodFrom (sets food identity + ref); MakeRefFrom alone can stay anonymous.
    /// </summary>
    static Thing? CreateNamedMeat(string charaId)
    {
        if (string.IsNullOrWhiteSpace(charaId))
        {
            return null;
        }

        string key = charaId.Trim();
        if (key.StartsWith("meat:", StringComparison.OrdinalIgnoreCase))
        {
            key = key.Substring(5);
        }

        string[] ids;
        if (string.Equals(key, "putty", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "ball", StringComparison.OrdinalIgnoreCase))
        {
            ids = new[] { "putty", "ball", "slime", "yeek" };
        }
        else if (string.Equals(key, "chicken", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "bird", StringComparison.OrdinalIgnoreCase))
        {
            ids = new[] { "chicken", "bird", "chicken_wild" };
        }
        else if (string.Equals(key, "sheep", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "lamb", StringComparison.OrdinalIgnoreCase))
        {
            ids = new[] { "sheep", "lamb", "goat", "pig" };
        }
        else if (string.Equals(key, "cow", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "beef", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "bull", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "cattle", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "ox", StringComparison.OrdinalIgnoreCase))
        {
            ids = new[] { "cow", "bull", "cattle", "ox", "pig", "sheep" };
        }
        else
        {
            ids = new[] { key, "putty", "chicken", "sheep", "cow", "bull" };
        }

        for (int i = 0; i < ids.Length; i++)
        {
            string refId = ids[i];
            if (string.IsNullOrEmpty(refId))
            {
                continue;
            }

            Thing? t = TryCreate("_meat", 1) ?? TryCreate("meat_marble", 1) ?? TryCreate("meat", 1);
            if (t == null)
            {
                continue;
            }

            bool ok = false;
            try
            {
                t.MakeFoodFrom(refId);
                ok = true;
            }
            catch
            {
                try
                {
                    t.MakeRefFrom(refId);
                    ok = true;
                }
                catch
                {
                    ok = false;
                }
            }

            if (!ok)
            {
                 if (t.parent == null) t.Destroy();
                continue;
            }

             KeepFoodFresh(t);

            try
            {
                string nm = t.NameOne ?? t.NameSimple ?? t.Name ?? "";
                if (!string.IsNullOrEmpty(nm)
                    && (nm.Contains("\u66fe\u4e3a\u751f\u547d")
                        || nm.Contains("once living")
                        || nm.Contains("former life")))
                {
                     if (t.parent == null) t.Destroy();
                    continue;
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchRewards.cs silent catch: " + __e.Message); }

                if (string.IsNullOrEmpty(t.c_idRefCard))
                {
                    t.c_idRefCard = refId;
                }

return t;
        }

        return null;
    }



    /// <summary>
    /// Force material on a thing and verify the live alias. Ore/chunk cards may be
    /// fixedMaterial (Create(id, mat) is ignored); ChangeMaterial(ignoreFixed:true) is required.
    /// </summary>
    static bool ForcePinnedMaterial(Thing t, string wantAlias)
    {
        if (t == null || string.IsNullOrEmpty(wantAlias))
        {
            return false;
        }

        string want = ResolveMaterialAlias(wantAlias);
        if (string.IsNullOrEmpty(want))
        {
            want = wantAlias;
        }

        try
        {
            SourceMaterial.Row? row = null;
            if (EClass.sources?.materials?.alias != null
                && EClass.sources.materials.alias.ContainsKey(want))
            {
                row = EClass.sources.materials.alias[want];
            }
            else if (IsSeaSandMaterialAlias(want) || want == "sand_sea" || want == "sea sand"
                || want.Equals("sea sand", StringComparison.OrdinalIgnoreCase))
            {
                row = ResolveSeaSandMaterialRow();
            }

            if (row == null && EClass.sources?.materials?.map != null
                && int.TryParse(want, out int mid)
                && EClass.sources.materials.map.ContainsKey(mid))
            {
                row = EClass.sources.materials.map[mid];
            }

            if (row == null)
            {
                return false;
            }

             t.ChangeMaterial(row, ignoreFixedMaterial: true);
             t.ChangeMaterial(row.id, ignoreFixedMaterial: true);
            if (!string.IsNullOrEmpty(row.alias))
            {
                 t.ChangeMaterial(row.alias, ignoreFixedMaterial: true);
            }

            string now = "";
            try { now = t.material != null ? (t.material.alias ?? "") : ""; } catch { now = ""; }
            if (string.Equals(now, row.alias ?? "", StringComparison.OrdinalIgnoreCase)
                || string.Equals(now, want, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }


                if (t.material != null && t.material.id == row.id)
                {
                    return true;
                }

return false;
        }
        catch
        {
            return false;
        }
    }


    static string LiveMaterialAlias(Thing t)
    {
        if (t == null)
        {
            return "";
        }

        try
        {
            return t.material != null ? (t.material.alias ?? "") : "";
        }
        catch
        {
            return "";
        }
    }


    static bool IsSeaSandMaterialAlias(string alias)
    {
        string a = NormalizeAliasToken(alias ?? "");
        return a == "sandsea" || a == "seasand";
    }


    static bool IsSeaSandThing(Thing t)
    {
        if (t == null)
        {
            return false;
        }

        try
        {
            // Runtime sea-sand material id (MATERIAL.sand_sea / alias sand_sea).

                int seaId = SeaSandMaterialId();
                if (seaId > 0 && t.material != null && t.material.id == seaId)
                {
                    return true;
                }

string mat = LiveMaterialAlias(t).ToLowerInvariant();
            if (IsSeaSandMaterialAlias(mat) || mat == "sand_sea" || mat == "sea sand")
            {
                return true;
            }


                string matName = t.material != null ? ((t.material.name ?? "") + " " + (t.material.alias ?? "")) : "";
                string mn = matName.ToLowerInvariant();
                if (mn.Contains("sea sand") || matName.Contains("海沙") || matName.Contains("海砂")
                    || IsSeaSandMaterialAlias(matName))
                {
                    return true;
                }

string nm = "";
            try { nm = (t.NameOne ?? t.NameSimple ?? t.Name ?? ""); } catch { nm = ""; }
            // CN 海沙 / EN sea sand — reject plain 沙
            if (nm.Contains("海沙") || nm.Contains("海砂") || nm.ToLowerInvariant().Contains("sea sand"))
            {
                return true;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchRewards.cs silent catch: " + __e.Message); }
return false;
    }


    static bool IsForbiddenHighOreMaterial(string alias)
    {
        string a = (alias ?? "").ToLowerInvariant();
        return a == "gold" || a == "platinum" || a == "adamantite"
            || a == "elder gold" || a == "gold_elder" || a == "orichalcum" || a == "netherite";
    }


    /// <summary>
    /// Create a material-bearing stack and refuse wrong materials
    /// (especially gold when copper/iron/sand_sea was requested).
    /// Preferred bases are a thin post-CreateRawMaterial carrier override only.
    /// </summary>
    static Thing? CreatePinnedMaterialThing(string alias, params string[] preferredBaseIds)
    {
        return CreateMaterialThing(alias, preferredBaseIds);
    }



    /// <summary>Console probe: show live create results for sand/copper/iron/salt.</summary>
    internal static string DebugProbeMaterialCreates()
    {
        var sb = new System.Text.StringBuilder();
        void Line(string label, Thing? t)
        {
            if (t == null)
            {
                sb.AppendLine(label + " => NULL");
                return;
            }

            string id = t.id ?? "?";
            string mat = "";
            int mid = -1;
            string nm = "";
             mat = t.material != null ? (t.material.alias ?? "") : "";
             mid = t.material != null ? t.material.id : -1;
             nm = t.NameOne ?? t.NameSimple ?? t.Name ?? "";
            bool mud = false, sand = false, gold = false, sea = false;
             mud = IsMudLikeThing(t);
             sand = IsSandLikeThing(t);
             sea = IsSeaSandThing(t);
             gold = IsGoldLikeThing(t);
            sb.AppendLine(label + " => id=" + id + " mat=" + mat + "#" + mid
                + " name=" + nm + " mud=" + mud + " sand=" + sand + " sea=" + sea + " gold=" + gold);
             if (t.parent == null) t.Destroy();
        }

        // Material row dump
        try
        {
            var row = ResolveSeaSandMaterialRow();
            if (row == null) sb.AppendLine("seaRow => NULL");
            else sb.AppendLine("seaRow => id=" + row.id + " alias=" + (row.alias ?? "") + " thing=" + (row.thing ?? "") + " cat=" + (row.category ?? "") + " name=" + (row.name ?? ""));
        }
        catch (Exception ex) { sb.AppendLine("seaRow err " + ex.Message); }

        try
        {
            foreach (var key in new[] { "copper", "iron", "sand_sea", "sea sand", "salt" })
            {
                bool has = EClass.sources?.materials?.alias != null && EClass.sources.materials.alias.ContainsKey(key);
                string resolved = ResolveMaterialAlias(key);
                sb.AppendLine("alias[" + key + "] has=" + has + " resolved=" + resolved);
            }
        }
        catch (Exception ex) { sb.AppendLine("alias dump err " + ex.Message); }

        Line("CreateBeachSand", CreateBeachSand());
         sb.AppendLine("SeaSandMaterialId=" + SeaSandMaterialId() + " copper=" + CopperMaterialId() + " iron=" + IronMaterialId());
        Line("CreateByMaterialId(sea)", CreateByMaterialId(SeaSandMaterialId()));
        Line("CreateByMaterialId(copper)", CreateByMaterialId(CopperMaterialId()));
        Line("CreateByMaterialId(iron)", CreateByMaterialId(IronMaterialId()));
        Line("CreateBeachSandHard", CreateBeachSandHard());
        Line("CreateBeachSalt", CreateBeachSalt());
        Line("CreateMetalHard(copper)", CreateMetalHard("copper"));
        Line("CreateMetalHard(iron)", CreateMetalHard("iron"));
        Line("CreateMountainSulfur", CreateMountainSulfur());
        Line("CreateMaterialThing(sulfur)", CreateMaterialThing("sulfur"));
        try
        {
            bool hasSulfurCard = EClass.sources?.cards?.map != null && EClass.sources.cards.map.ContainsKey("sulfur");
            bool hasSulfurMat = EClass.sources?.materials?.alias != null && EClass.sources.materials.alias.ContainsKey("sulfur");
            sb.AppendLine("sulfur card=" + hasSulfurCard + " matAlias=" + hasSulfurMat
                + " matId=" + ResolveMaterialId("sulfur"));
        }
        catch (Exception ex) { sb.AppendLine("sulfur meta err " + ex.Message); }
        Line("CreateMaterialThing(copper)", CreateMaterialThing("copper"));
        Line("CreateWeightedOre(common)", CreateWeightedOre(common: true));
        try
        {
            Thing? t = ThingGen.Create("chunk", SeaSandMaterialId(), 1);
            Line("ThingGen.Create(chunk,seaSandId)", t);
        }
        catch (Exception ex) { sb.AppendLine("chunk seaSand err " + ex.Message); }
        try
        {
            Thing? t = ThingGen.Create("ingot", "copper", 1);
            if (t != null) {  t.ChangeMaterial("copper", ignoreFixedMaterial: true);  }
            Line("ingot+copper pin", t);
        }
        catch (Exception ex) { sb.AppendLine("ingot copper err " + ex.Message); }
        try
        {
            Thing? t = ThingGen.Create("ore", "copper", 1);
            if (t != null) {  t.ChangeMaterial("copper", ignoreFixedMaterial: true);  }
            Line("ore+copper pin", t);
        }
        catch (Exception ex) { sb.AppendLine("ore copper err " + ex.Message); }

        return sb.ToString();
    }


    /// <summary>
    /// v15 core create: pin by runtime material id (from alias / MATERIAL constants).
    /// ThingGen.Create(id, idMat) then ChangeMaterial(row, ignoreFixedMaterial:true).
    /// Accepts only when live material.id matches.
    /// </summary>
    static Thing? CreateByMaterialId(int matId, string[]? preferredCarriers = null)
    {
        if (matId <= 0 || EClass.sources?.materials?.map == null)
        {
            return null;
        }

        SourceMaterial.Row? row = null;
        try
        {
            if (EClass.sources.materials.map.ContainsKey(matId))
            {
                row = EClass.sources.materials.map[matId];
            }
        }
        catch
        {
            row = null;
        }

        if (row == null)
        {
            return null;
        }

        string alias = !string.IsNullOrEmpty(row.alias) ? row.alias : matId.ToString();
        return CreateMaterialFromRow(row, alias, preferredCarriers);
    }


    static int ResolveMaterialId(string alias)
    {
        if (string.IsNullOrEmpty(alias) || EClass.sources?.materials == null)
        {
            return -1;
        }

        string cacheKey = alias.Trim();
        if (MaterialIdCache.TryGetValue(cacheKey, out int cached))
        {
            return cached;
        }

        int id = ResolveMaterialIdUncached(alias);
        MaterialIdCache[cacheKey] = id;
        return id;
    }

    static int ResolveMaterialIdUncached(string alias)
    {

            string want = ResolveMaterialAlias(alias);
            if (string.IsNullOrEmpty(want)) want = alias;
            if (EClass.sources.materials.alias != null && EClass.sources.materials.alias.ContainsKey(want))
            {
                return EClass.sources.materials.alias[want].id;
            }

            if (EClass.sources.materials.alias != null && EClass.sources.materials.alias.ContainsKey(alias))
            {
                return EClass.sources.materials.alias[alias].id;
            }

            string norm = NormalizeAliasToken(alias);
            if (EClass.sources.materials.rows != null)
            {
                for (int i = 0; i < EClass.sources.materials.rows.Count; i++)
                {
                    SourceMaterial.Row r = EClass.sources.materials.rows[i];
                    if (r == null) continue;
                    if (NormalizeAliasToken(r.alias ?? "") == norm
                        || NormalizeAliasToken(r.name ?? "") == norm)
                    {
                        return r.id;
                    }
                }
            }


        return -1;
    }


    static int LiveMaterialId(Thing t)
    {
        if (t == null) return -1;
        try { return t.material != null ? t.material.id : -1; }
        catch { return -1; }
    }


    /// <summary>
    /// Runtime material id for beach 海沙. Prefer game constant MATERIAL.sand_sea, else alias lookup.
    /// </summary>
    static int SeaSandMaterialId()
    {
        try
        {
            return MATERIAL.sand_sea;
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchRewards.cs silent catch: " + __e.Message); }
int id = ResolveMaterialId("sand_sea");
        if (id > 0)
        {
            return id;
        }

        return ResolveMaterialId("sea sand");
    }


    static int CopperMaterialId()
    {
        return ResolveMaterialId("copper");
    }


    static int IronMaterialId()
    {
        return ResolveMaterialId("iron");
    }


    static Thing? CreateMetalHard(string alias)
    {
        if (string.IsNullOrEmpty(alias))
        {
            return null;
        }

        // Prefer raw ore/rock carriers; finished ingot is smelter product, not haul.
        Thing? t = CreateMaterialThing(alias, new[] { "ore", "rock", "chunk", "cutstone" });
        if (t == null)
        {
            return null;
        }

        try
        {
            string idNow = (t.id ?? "").ToLowerInvariant();
            if (idNow == "ingot" || idNow.Contains("ingot") || idNow == "bar")
            {
                Thing? ore = CreateMaterialThing(alias, new[] { "ore", "rock", "chunk" });
                if (ore != null)
                {
                    if (t.parent == null) t.Destroy();
                    t = ore;
                }
            }
        }
        catch (System.Exception __e)
        {
            Plugin.LogDebug("CreateMetalHard: " + __e.Message);
        }

        string live = LiveMaterialAlias(t).ToLowerInvariant();
        if (IsGoldLikeThing(t) || IsForbiddenHighOreMaterial(live))
        {
            string a = (alias ?? "").ToLowerInvariant();
            if (a != "gold" && a != "platinum" && a != "adamantite")
            {
                if (t.parent == null) t.Destroy();
                return null;
            }
        }

        return t;
    }


    /// <summary>
    /// Beach sea sand — single CreateRawMaterial path (same as CreateBeachSand).
    /// </summary>
    static Thing? CreateBeachSandHard()
    {
        return CreateBeachSand();
    }


    static Thing? CreateBeachSand()
    {
        // Player digs beach floor: Map.MineFloor (region) =>
        // ThingGen.CreateRawMaterial(matFloor) + ChangeMaterial(matFloor.alias).
        SourceMaterial.Row? row = ResolveSeaSandMaterialRow();
        if (row == null)
        {
            Plugin.LogDebug("CreateBeachSand: sea sand material row missing");
            return null;
        }

        string wantAlias = !string.IsNullOrEmpty(row.alias) ? row.alias : "sand_sea";
        Thing? t = CreateMaterialFromRow(row, wantAlias, new[] { "chunk", "rock", "pebble", "stone" });
        if (t != null && AcceptSeaSandThing(t, row) && !IsScrapLikeThing(t) && !IsGoldLikeThing(t))
        {
            return t;
        }

        if (t != null && t.parent == null)
        {
            t.Destroy();
        }

        return CreateBeachSandMandatoryFallback();
    }


    /// <summary>
    /// Resolve the live SourceMaterial row for beach 海沙.
    /// Prefers alias sand_sea, then EN "sea sand", then MATERIAL.sand_sea, then name scan.
    /// </summary>
    static SourceMaterial.Row? ResolveSeaSandMaterialRow()
    {
        try
        {
            if (EClass.sources?.materials == null)
            {
                return null;
            }

            string[] keys =
            {
                "sand_sea", "sea sand", "seasand", "sea_sand",
                ResolveMaterialAlias("sand_sea"),
                ResolveMaterialAlias("sea sand"),
            };

            if (EClass.sources.materials.alias != null)
            {
                for (int i = 0; i < keys.Length; i++)
                {
                    string k = keys[i];
                    if (string.IsNullOrEmpty(k))
                    {
                        continue;
                    }

                    if (EClass.sources.materials.alias.ContainsKey(k))
                    {
                        return EClass.sources.materials.alias[k];
                    }
                }
            }

            // Game constant / live alias id for 海沙 / sea sand.

                int seaId = MATERIAL.sand_sea;
                if (EClass.sources.materials.map != null
                    && EClass.sources.materials.map.ContainsKey(seaId))
                {
                    return EClass.sources.materials.map[seaId];
                }

int resolvedSea = ResolveMaterialId("sand_sea");
            if (resolvedSea <= 0)
            {
                resolvedSea = ResolveMaterialId("sea sand");
            }
            if (resolvedSea > 0
                && EClass.sources.materials.map != null
                && EClass.sources.materials.map.ContainsKey(resolvedSea))
            {
                return EClass.sources.materials.map[resolvedSea];
            }

            if (EClass.sources.materials.rows != null)
            {
                for (int i = 0; i < EClass.sources.materials.rows.Count; i++)
                {
                    SourceMaterial.Row r = EClass.sources.materials.rows[i];
                    if (r == null)
                    {
                        continue;
                    }

                    string a = (r.alias ?? "").ToLowerInvariant();
                    string n = (r.name ?? "").ToLowerInvariant();
                    string nj = r.name_JP ?? "";
                    if (IsSeaSandMaterialAlias(a)
                        || a == "sand_sea"
                        || n == "sea sand"
                        || n.Contains("sea sand")
                        || nj.Contains("海砂")
                        || (resolvedSea > 0 && r.id == resolvedSea))
                    {
                        return r;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("ResolveSeaSandMaterialRow: " + ex.Message);
        }

        return null;
    }




    static bool AcceptSeaSandThing(Thing t, SourceMaterial.Row? row = null)
    {
        if (t == null)
        {
            return false;
        }

        if (IsSeaSandThing(t))
        {
            return true;
        }


            if (row != null && t.material != null && t.material.id == row.id)
            {
                return true;
            }

return false;
    }



    static string ResolveMaterialAlias(string want)
    {
        if (string.IsNullOrWhiteSpace(want) || EClass.sources?.materials?.alias == null)
        {
            return want ?? "";
        }

        // Lang name_EN ("sea sand") != runtime alias ("sand_sea"). Map known display names first.
        string mapped = MapDisplayMaterialAlias(want);
        if (!string.IsNullOrEmpty(mapped) && EClass.sources.materials.alias.ContainsKey(mapped))
        {
            return mapped;
        }

        if (EClass.sources.materials.alias.ContainsKey(want))
        {
            return want;
        }

        string[] candidates =
        {
            mapped,
            want,
            want.ToLowerInvariant(),
            want.Replace('_', ' '),
            want.Replace('-', ' '),
            want.Replace("_", ""),
            want.Replace(" ", ""),
            want.Replace("_", " "),
            // sea sand -> sand_sea, white sand -> sand_white, quartz sand stays multi-word if present
            FlipSandAlias(want),
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            string c = candidates[i];
            if (!string.IsNullOrEmpty(c) && EClass.sources.materials.alias.ContainsKey(c))
            {
                return c;
            }
        }

        string normWant = NormalizeAliasToken(want);

            foreach (var kv in EClass.sources.materials.alias)
            {
                string key = kv.Key ?? "";
                if (NormalizeAliasToken(key) == normWant)
                {
                    return key;
                }
            }

return want;
    }


    static string NormalizeAliasToken(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "";
        }

        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            if (ch == ' ' || ch == '_' || ch == '-')
            {
                continue;
            }

            sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
    }


    /// <summary>
    /// Map localization / plan display material names onto live SourceMaterial.alias keys.
    /// </summary>
    static string MapDisplayMaterialAlias(string want)
    {
        if (string.IsNullOrWhiteSpace(want))
        {
            return "";
        }

        string n = NormalizeAliasToken(want);
        switch (n)
        {
            case "seasand":
            case "sandsea":
                return "sand_sea";
            case "whitesand":
            case "sandwhite":
                return "sand_white";
            case "quartzsand":
            case "sandquartz":
                return "quartz sand";
            case "eternalsand":
                return "eternal sand";
            case "seawater":
            case "watersea":
                return "water_sea";
            case "eldergold":
            case "goldelder":
                return "gold_elder";
            case "rosequartz":
                return "rosequartz";
            case "dragonscale":
            case "dragons hide":
            case "dragonhide":
                return "hide_dragon";
            case "lapislazuli":
                return "lapis";
            default:
                return want.Trim();
        }
    }


    static string FlipSandAlias(string want)
    {
        if (string.IsNullOrWhiteSpace(want))
        {
            return want ?? "";
        }

        string t = want.Trim().ToLowerInvariant().Replace('-', ' ');
        if (t == "sea sand" || t == "sea_sand" || t == "seasand")
        {
            return "sand_sea";
        }

        if (t == "white sand" || t == "white_sand" || t == "whitesand")
        {
            return "sand_white";
        }

        if (t == "sand_sea")
        {
            return "sea sand";
        }

        if (t == "sand_white")
        {
            return "white sand";
        }

        // generic "foo sand" <-> "sand_foo"
        if (t.EndsWith(" sand"))
        {
            string head = t.Substring(0, t.Length - 5).Trim().Replace(' ', '_');
            if (!string.IsNullOrEmpty(head))
            {
                return "sand_" + head;
            }
        }

        if (t.StartsWith("sand_"))
        {
            string tail = t.Substring(5).Replace('_', ' ').Trim();
            if (!string.IsNullOrEmpty(tail))
            {
                return tail + " sand";
            }
        }

        return t;
    }


    /// <summary>
    /// Absolute last resort so beach plans never ship without a 海沙 line.
    /// Prefer a live pinned chunk; otherwise return null and let plan entry carry identity.
    /// </summary>
    static Thing? CreateBeachSandMandatoryFallback()
    {
        SourceMaterial.Row? row = ResolveSeaSandMaterialRow();
        if (row == null)
        {
            return null;
        }

        try
        {
            Thing? t = ThingGen.Create("chunk", row.id, 1)
                ?? ThingGen.Create("rock", row.id, 1)
                ?? ThingGen.Create("pebble", row.id, 1);
            if (t == null)
            {
                return null;
            }

            t.ChangeMaterial(row, ignoreFixedMaterial: true);
            if (!string.IsNullOrEmpty(row.alias))
            {
                t.ChangeMaterial(row.alias, ignoreFixedMaterial: true);
            }

            return t;
        }
        catch
        {
            return null;
        }
    }


    static Thing? CreateBeachSalt()
    {
        // Salt may be a thing id rather than a material alias in some data builds.
        Thing? t = TryCreate("salt", 1);
        if (t == null)
        {
            t = CreateMaterialThing("salt");
        }

        if (t == null)
        {
            // Crystal-ish mineral as last beach mineral garnish is better than mud.
            t = CreateMaterialThing("crystal");
        }

        if (t == null)
        {
            return null;
        }

        if (IsMudLikeThing(t))
        {
             if (t.parent == null) t.Destroy();
            return null;
        }

        return t;
    }


    static bool IsMudLikeMaterial(string alias, string thing, string name)
    {
        string a = (alias ?? "").ToLowerInvariant().Trim();
        string th = (thing ?? "").ToLowerInvariant().Trim();
        string nm = (name ?? "").ToLowerInvariant().Trim();

        // True sand family is never mud.
        if (a.Contains("sand") || th.Contains("sand") || nm.Contains("sand")
            || a.Contains("沙") || th.Contains("沙") || nm.Contains("沙")
            || a.Contains("quartz") || nm.Contains("quartz"))
        {
            return false;
        }

        // Ore / metal / salt materials are never mud even if the carrier card is "chunk"
        // (vanilla chunk EN name is literally "soil" / CN "土块").
        if (IsOreMetalAlias(a) || a == "salt" || a == "sulfur" || a == "sulphur"
            || a == "plastic" || a.Contains("plast") || a == "glass" || a == "crystal"
            || IsGemAlias(a) || a == "stone" || a == "granite" || a == "basalt"
            || a == "bronze" || a == "steel" || a == "silver" || a == "mithril")
        {
            return false;
        }

        // Carrier id "chunk" alone is NOT mud — only real mud/soil MATERIALS are.
        if (th == "chunk" || th == "rock" || th == "pebble" || th == "stone" || th == "ingot" || th == "ore")
        {
            // Judge by material alias only.
            return a == "mud" || a == "soil" || a.Contains("mud") || a.Contains("silt")
                || a == "dirt" || a == "clay" || a.EndsWith(" soil") || a.StartsWith("soil")
                || a.Contains("pale soil") || a.Contains("red soil") || a.Contains("light soil")
                || a.Contains("yellow soil");
        }

        // Free-text / unknown: require mud tokens on material or explicit mud name,
        // but do NOT treat bare English word "soil" on a named carrier as mud when
        // material is empty (too many false positives on chunk).
        if (a == "mud" || a == "soil" || a.Contains("mud") || a.Contains("silt")
            || a == "dirt" || a == "clay" || a.Contains(" soil") || a.StartsWith("soil"))
        {
            return true;
        }

        if (nm == "泥" || nm.Contains("泥") || nm.Contains("mud")
            || (nm.Contains("dirt") && !nm.Contains("sand")))
        {
            return true;
        }

        // Only treat name "soil" as mud when material is missing/unknown or also soil-ish.
        if ((nm == "soil" || nm.Contains(" soil") || nm.StartsWith("soil"))
            && (string.IsNullOrEmpty(a) || a == "soil" || a.Contains("mud") || a.Contains("dirt") || a.Contains("clay")))
        {
            return true;
        }

        return false;
    }


    static bool IsMudLikeThing(Thing t)
    {
        if (t == null)
        {
            return false;
        }

        try
        {
            string id = (t.id ?? "").ToLowerInvariant();
            string nm = "";
            try { nm = (t.NameOne ?? t.NameSimple ?? t.Name ?? "").ToLowerInvariant(); } catch { nm = ""; }
            string mat = "";
            try { mat = t.material != null ? (t.material.alias ?? "").ToLowerInvariant() : ""; } catch { mat = ""; }
            if (IsSandLikeThing(t) || IsSaltLikeThing(t) || IsSeaSandThing(t) || IsSulfurLikeThing(t))
            {
                return false;
            }


                int mid = t.material != null ? t.material.id : -1;
                // sea sand / copper / iron / gold / sulfur are never mud (carrier chunk name "soil" is fine).
                int seaId = SeaSandMaterialId();
                int copperId = CopperMaterialId();
                int ironId = IronMaterialId();
                int goldId = ResolveMaterialId("gold");
                int sulfurId = ResolveMaterialId("sulfur");
                if (sulfurId <= 0) sulfurId = ResolveMaterialId("sulphur");
                if ((seaId > 0 && mid == seaId)
                    || (copperId > 0 && mid == copperId)
                    || (ironId > 0 && mid == ironId)
                    || (goldId > 0 && mid == goldId)
                    || (sulfurId > 0 && mid == sulfurId))
                {
                    return false;
                }


            // Authoritative mud/soil/dirt material ids first; text matching is fallback.
            if (HasMaterialAlias(t, "mud", "soil", "dirt", "clay", "silt"))
            {
                return true;
            }

            return IsMudLikeMaterial(mat, id, nm)
                || nm == "\u6ce5"
                || nm.Contains("\u6ce5");
        }
        catch
        {
            return false;
        }
    }


    static bool IsSandLikeThing(Thing t)
    {
        if (t == null)
        {
            return false;
        }

        try
        {
            // Authoritative material ids first; text fallback only.
            if (HasMaterialAlias(t, "sand", "sand_sea", "sand_white", "sea sand", "white sand"))
            {
                return true;
            }

            string id = (t.id ?? "").ToLowerInvariant();
            string nm = "";
            try { nm = (t.NameOne ?? t.NameSimple ?? t.Name ?? "").ToLowerInvariant(); } catch { nm = ""; }
            string mat = "";
            try { mat = t.material != null ? (t.material.alias ?? "").ToLowerInvariant() : ""; } catch { mat = ""; }
            return mat.Contains("sand")
                || id.Contains("sand")
                || nm.Contains("\u6c99")
                || nm.Contains("sand");
        }
        catch
        {
            return false;
        }
    }


    static bool IsSaltLikeThing(Thing t)
    {
        if (t == null)
        {
            return false;
        }

        try
        {
            if (HasMaterialAlias(t, "salt"))
            {
                return true;
            }

            string id = (t.id ?? "").ToLowerInvariant();
            string nm = "";
            try { nm = (t.NameOne ?? t.NameSimple ?? t.Name ?? "").ToLowerInvariant(); } catch { nm = ""; }
            string mat = "";
            try { mat = t.material != null ? (t.material.alias ?? "").ToLowerInvariant() : ""; } catch { mat = ""; }
            return mat == "salt"
                || id.Contains("salt")
                || nm.Contains("\u76d0")
                || nm.Contains("salt");
        }
        catch
        {
            return false;
        }
    }


    static Thing? TryCreateRawByMatAlias(string alias)
    {
        return CreateMaterialThing(alias);
    }

    /// <summary>
    /// Create a raw material stack for alias via vanilla CreateRawMaterial-first.
    /// Many mineral rows have empty thing, so fall back to one carrier + ChangeMaterial.
    /// Never returns mud/soil freebies when alias itself is not mud.
    /// </summary>
    static Thing? CreateMaterialThing(string alias, string[]? preferredCarriers = null)
    {
        if (string.IsNullOrEmpty(alias))
        {
            return null;
        }

        alias = ResolveMaterialAlias(alias);
        string a = (alias ?? "").ToLowerInvariant();
        if (a == "mud" || a == "soil" || a.Contains("mud") || a == "dirt" || a == "clay")
        {
            return null;
        }

        try
        {
            if (EClass.sources?.materials?.alias == null
                || string.IsNullOrEmpty(alias)
                || !EClass.sources.materials.alias.ContainsKey(alias))
            {
                return TryCreate(alias, 1);
            }

            SourceMaterial.Row row = EClass.sources.materials.alias[alias];

            // Sea sand always goes through the strict beach helper.
            if (IsSeaSandMaterialAlias(alias) || a == "sand_sea" || a == "sea sand")
            {
                Thing? sea = CreateBeachSand();
                if (sea != null)
                {
                    return sea;
                }
            }

            return CreateMaterialFromRow(row, alias, preferredCarriers);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Single material factory: CreateRawMaterial(row) then ChangeMaterial(alias),
    /// with one carrier fallback when row.thing is empty.
    /// </summary>
    static Thing? CreateMaterialFromRow(SourceMaterial.Row row, string wantAlias, string[]? preferredCarriers = null)
    {
        if (row == null)
        {
            return null;
        }

        string want = ResolveMaterialAlias(wantAlias);
        if (string.IsNullOrEmpty(want))
        {
            want = !string.IsNullOrEmpty(row.alias) ? row.alias : wantAlias;
        }

        string a = (want ?? "").ToLowerInvariant();
        Thing? t = null;

        try
        {
            if (!string.IsNullOrEmpty(row.thing))
            {
                try { t = ThingGen.CreateRawMaterial(row); } catch { t = null; }
            }
        }
        catch
        {
            t = null;
        }

        if (t != null)
        {
            try
            {
                if (!string.IsNullOrEmpty(row.alias))
                {
                    t.ChangeMaterial(row.alias, ignoreFixedMaterial: true);
                }
                else
                {
                    t.ChangeMaterial(row.id, ignoreFixedMaterial: true);
                }
            }
            catch
            {
            }

            if (AcceptCreatedMaterial(t, want, a))
            {
                return t;
            }

            if (t.parent == null) t.Destroy();
            t = null;
        }

        // Carrier path when CreateRawMaterial is unavailable or rejected.
        var carriers = new List<string>();
        void AddCarrier(string c)
        {
            if (string.IsNullOrEmpty(c)) return;
            for (int i = 0; i < carriers.Count; i++)
            {
                if (string.Equals(carriers[i], c, StringComparison.OrdinalIgnoreCase)) return;
            }
            carriers.Add(c);
        }

        if (preferredCarriers != null)
        {
            for (int i = 0; i < preferredCarriers.Length; i++) AddCarrier(preferredCarriers[i]);
        }

        if (!string.IsNullOrEmpty(row.thing)) AddCarrier(row.thing);

        string cat = (row.category ?? "").ToLowerInvariant();
        if (a.Contains("sand"))
        {
            AddCarrier("chunk"); AddCarrier("rock"); AddCarrier("pebble"); AddCarrier("stone");
        }
        else if (IsOreMetalAlias(a) || cat == "ore" || cat.Contains("mineral"))
        {
            AddCarrier("ore"); AddCarrier("rock"); AddCarrier("chunk"); AddCarrier("cutstone");
        }
        else if (cat.Contains("gem") || IsGemAlias(a))
        {
            AddCarrier("ore_gem"); AddCarrier("gem"); AddCarrier("chunk");
        }
        else if (cat.Contains("wood") || a.StartsWith("wood"))
        {
            AddCarrier("log"); AddCarrier("branch");
        }
        else if (a == "salt")
        {
            AddCarrier("rock"); AddCarrier("chunk"); AddCarrier("pebble");
        }
        else
        {
            AddCarrier("rock"); AddCarrier("chunk"); AddCarrier("pebble"); AddCarrier("stone");
        }

        for (int i = 0; i < carriers.Count; i++)
        {
            string baseId = carriers[i];
            t = null;
            try { t = ThingGen.Create(baseId, row.id, 1); } catch { t = null; }
            if (t == null)
            {
                try { t = ThingGen.Create(baseId, want, 1); } catch { t = null; }
            }
            if (t == null)
            {
                try { t = ThingGen.Create(baseId, -1, 1); } catch { t = null; }
            }
            if (t == null) continue;

            try
            {
                t.ChangeMaterial(row, ignoreFixedMaterial: true);
                t.ChangeMaterial(row.id, ignoreFixedMaterial: true);
                if (!string.IsNullOrEmpty(row.alias))
                {
                    t.ChangeMaterial(row.alias, ignoreFixedMaterial: true);
                }
            }
            catch
            {
            }

            if (AcceptCreatedMaterial(t, want, a))
            {
                return t;
            }

            if (t.parent == null) t.Destroy();
        }

        return null;
    }

    static bool AcceptCreatedMaterial(Thing t, string want, string wantLower)
    {
        if (t == null)
        {
            return false;
        }

        if (!ForcePinnedMaterial(t, want))
        {
            return false;
        }

        string live = LiveMaterialAlias(t).ToLowerInvariant();
        if (IsForbiddenHighOreMaterial(live) && !IsForbiddenHighOreMaterial(wantLower))
        {
            return false;
        }

        if (IsMudLikeThing(t) && !(wantLower.Contains("sand") || wantLower == "salt"))
        {
            return false;
        }

        if (IsScrapLikeThing(t)
            && wantLower != "scrap"
            && !wantLower.Contains("iron")
            && wantLower != "steel"
            && !IsOreMetalAlias(wantLower))
        {
            return false;
        }

        try
        {
            if (t.material != null && t.material.id == ResolveMaterialId(want))
            {
                return true;
            }
        }
        catch
        {
        }

        if (string.Equals(live, want, StringComparison.OrdinalIgnoreCase)
            || string.Equals(live, wantLower, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (wantLower.Contains("sand") && IsSeaSandThing(t))
        {
            return true;
        }

        return false;
    }


    static bool IsOreMetalAlias(string alias)
    {
        string a = (alias ?? "").ToLowerInvariant();
        return a == "copper" || a == "iron" || a == "bronze" || a == "steel"
            || a == "silver" || a == "gold" || a == "platinum" || a == "adamantite"
            || a == "chromite" || a == "cobalt" || a == "meteorite" || a == "titanium"
            || a == "mithril";
    }


    static bool IsGemAlias(string alias)
    {
        string a = (alias ?? "").ToLowerInvariant();
        return a == "crystal" || a == "diamond" || a == "emerald" || a == "jade"
            || a == "lapis" || a == "mica" || a == "onyx" || a == "opal" || a == "pearl"
            || a == "rubinus" || a == "saphire" || a == "topaz" || a == "turquoise"
            || a == "aquamarine" || a == "quartz";
    }


    static Thing? CreateWoodLog(string woodAlias)
    {
        Thing? t = CreateMaterialThing(woodAlias);
        if (t != null)
        {
            return t;
        }

        t = TryCreate("log", 1);
        if (t == null)
        {
            return null;
        }


            if (EClass.sources?.materials?.alias != null
                && EClass.sources.materials.alias.ContainsKey(woodAlias))
            {
                t.ChangeMaterial(woodAlias, ignoreFixedMaterial: true);
            }

return t;
    }


    static Thing? CreateWeightedOre(bool common)
    {
        // Weighted metal picks. Common: copper/iron heavy. Uncommon: bronze/steel/silver, rare gold, tiny adamantite.
        string alias;
        if (common)
        {
            int r = EClass.rnd(100);
            if (r < 55) alias = "copper";
            else if (r < 90) alias = "iron";
            else if (r < 97) alias = "bronze";
            else alias = "steel";
        }
        else
        {
            int r = EClass.rnd(100);
            if (r < 45) alias = "iron";
            else if (r < 70) alias = "bronze";
            else if (r < 85) alias = "steel";
            else if (r < 93) alias = "silver";
            else if (r < 98) alias = "gold";
            else if (r < 99) alias = "platinum";
            else alias = "adamantite"; // 1%
        }

        Thing? ore = CreateMetalHard(alias)
            ?? CreateMaterialThing(alias)
            ?? CreateMetalHard("copper")
            ?? CreateMaterialThing("copper")
            ?? CreateMetalHard("iron")
            ?? CreateMaterialThing("iron");
        if (ore != null && (IsGoldLikeThing(ore) || IsForbiddenHighOreMaterial(LiveMaterialAlias(ore))))
        {
             if (ore.parent == null) ore.Destroy();
            ore = CreateMetalHard("copper") ?? CreateMaterialThing("copper")
                ?? CreateMetalHard("iron") ?? CreateMaterialThing("iron");
        }

        return ore;
    }


    /// <summary>
    /// Danger-scaled ore for dungeon dispatch mineral packets.
    /// Material tier rises with danger: low=copper/iron, mid=bronze/steel, high=silver+.
    /// Never produces sulfur, gold ore, or scrap (banned via IsBannedDungeonStapleThing / IsForbiddenHighOreMaterial).
    /// </summary>
    static Thing? CreateWeightedOreDangerScaled(int danger)
    {
        string alias;
        if (danger < 30)
        {
            // Low danger: copper/iron heavy, small bronze.
            int r = EClass.rnd(100);
            if (r < 55) alias = "copper";
            else if (r < 90) alias = "iron";
            else alias = "bronze";
        }
        else if (danger < 80)
        {
            // Mid danger: iron/bronze/steel.
            int r = EClass.rnd(100);
            if (r < 35) alias = "iron";
            else if (r < 65) alias = "bronze";
            else if (r < 90) alias = "steel";
            else alias = "copper";
        }
        else if (danger < 150)
        {
            // High danger: bronze/steel/silver.
            int r = EClass.rnd(100);
            if (r < 30) alias = "bronze";
            else if (r < 60) alias = "steel";
            else if (r < 85) alias = "silver";
            else alias = "iron";
        }
        else
        {
            // Very high danger: steel/silver/platinum, rare adamantite.
            int r = EClass.rnd(100);
            if (r < 30) alias = "steel";
            else if (r < 60) alias = "silver";
            else if (r < 85) alias = "platinum";
            else if (r < 97) alias = "bronze";
            else alias = "adamantite";
        }

        Thing? ore = CreateMetalHard(alias)
            ?? CreateMaterialThing(alias)
            ?? CreateMetalHard("copper")
            ?? CreateMaterialThing("copper")
            ?? CreateMetalHard("iron")
            ?? CreateMaterialThing("iron");
        if (ore != null && (IsGoldLikeThing(ore) || IsForbiddenHighOreMaterial(LiveMaterialAlias(ore)) || IsBannedDungeonStapleThing(ore)))
        {
            if (ore.parent == null) ore.Destroy();
            ore = CreateMetalHard("copper") ?? CreateMaterialThing("copper")
                ?? CreateMetalHard("iron") ?? CreateMaterialThing("iron");
        }

        return ore;
    }


    static Thing? CreateMountainGem()
    {
        // Prefer ore_gem card (vanilla rolls gem category mat) but pin a modest gem alias.
        string[] gems =
        {
            "crystal", "jade", "lapis", "turquoise", "topaz", "opal", "onyx",
            "pearl", "aquamarine", "mica", "emerald", "saphire", "rubinus", "diamond"
        };
        // Bias common crystals first.
        int r = EClass.rnd(100);
        string alias;
        if (r < 40) alias = "crystal";
        else if (r < 70) alias = gems[EClass.rnd(Math.Min(8, gems.Length))];
        else alias = gems[EClass.rnd(gems.Length)];

        Thing? t = null;
        try { t = ThingGen.Create("ore_gem", alias, 1); } catch { t = null; }
        if (t == null)
        {
            t = CreateMaterialThing(alias);
        }

        if (t == null)
        {
            t = TryCreate("ore_gem", 1) ?? TryCreate("gem", 1);
            if (t != null)
            {
                 t.ChangeMaterial(alias, ignoreFixedMaterial: true);
            }
        }

        return t;
    }


    static Thing? CreateForestVine()
    {
        string[] ids = { "vine", "vine", "weed", "grass", "fiber" };
        for (int i = 0; i < ids.Length; i++)
        {
            Thing? t = TryCreate(ids[i], 1);
            if (t != null)
            {
                 ForceFreshProduceMaterial(t);
                 KeepFoodFresh(t);
                if (!IsMudLikeThing(t))
                {
                    return t;
                }

                 if (t.parent == null) t.Destroy();
            }
        }

        return TryCreateFromCategorySafe("plant", 1) ?? TryCreate("grass", 1);
    }


    static Thing? CreateForestFruit()
    {
        string[] ids =
        {
            "grape", "apple", "berry", "banana", "palulu", "fruit",
            "orange", "peach", "cactus"
        };
        int start = ids.Length > 0 ? EClass.rnd(ids.Length) : 0;
        for (int i = 0; i < ids.Length; i++)
        {
            string id = ids[(start + i) % ids.Length];
            Thing? t = TryCreate(id, 1);
            if (t == null)
            {
                continue;
            }

             ForceFreshProduceMaterial(t);
             KeepFoodFresh(t);
            if (!IsMudLikeThing(t))
            {
                return t;
            }

             if (t.parent == null) t.Destroy();
        }

        return TryCreateFromCategorySafe("fruit", 1)
            ?? TryCreateFromCategorySafe("food", 1);
    }


    static void StripMudLikeThings(List<Thing> things)
    {
        if (things == null || things.Count == 0)
        {
            return;
        }

        for (int i = things.Count - 1; i >= 0; i--)
        {
            Thing t = things[i];
            if (t == null)
            {
                things.RemoveAt(i);
                continue;
            }

            if (!IsMudLikeThing(t))
            {
                continue;
            }

             if (t.parent == null) t.Destroy();
            things.RemoveAt(i);
        }
    }


    static void StripScrapAndRubber(List<Thing> things)
    {
        if (things == null || things.Count == 0)
        {
            return;
        }

        for (int i = things.Count - 1; i >= 0; i--)
        {
            Thing t = things[i];
            if (t == null)
            {
                things.RemoveAt(i);
                continue;
            }

            bool drop = false;

                if (IsScrapLikeThing(t))
                {
                    drop = true;
                }
                else
                {
                    string id = (t.id ?? "").ToLowerInvariant();
                    string nm = "";
                    try { nm = (t.NameOne ?? t.NameSimple ?? t.Name ?? "").ToLowerInvariant(); } catch { nm = ""; }
                    if (id.Contains("rubber") || id.Contains("duck")
                        || nm.Contains("橡皮") || nm.Contains("鸭"))
                    {
                        drop = true;
                    }
                }

if (!drop)
            {
                continue;
            }

             if (t.parent == null) t.Destroy();
            things.RemoveAt(i);
        }
    }


    static bool IsRegionStapleThing(Thing t)
    {
        if (t == null)
        {
            return false;
        }

        try
        {
            string id = (t.id ?? "").ToLowerInvariant();
            if (id.Contains("sand") || id.Contains("salt")
                || id.Contains("log") || id.Contains("ore") || id.Contains("rock")
                || id.Contains("sulfur") || id.Contains("plastic") || id.Contains("flower")
                || id.Contains("mushroom") || id.Contains("pasture") || id.Contains("vine")
                || id.Contains("fruit") || id.Contains("grape") || id.Contains("apple")
                || id.Contains("berry") || id.Contains("meat") || id.Contains("gem"))
            {
                return true;
            }

            string nm = (t.NameOne ?? t.NameSimple ?? t.Name ?? "").ToLowerInvariant();
            if (nm.Contains("沙") || nm.Contains("盐") || nm.Contains("砂")
                || nm.Contains("原木") || nm.Contains("矿") || nm.Contains("土")
                || nm.Contains("硫") || nm.Contains("花") || nm.Contains("蘑菇") || nm.Contains("菌"))
            {
                return true;
            }


                string mat = t.material != null ? (t.material.alias ?? "") : "";
                mat = mat.ToLowerInvariant();
                if (mat == "sand" || mat == "sand_sea" || mat == "sand_white" || mat.Contains("sand")
                    || mat == "salt" || mat == "sulfur"
                    || mat.Contains("wood") || mat.Contains("oak")
                    || mat.Contains("iron") || mat.Contains("copper") || mat.Contains("bronze")
                    || mat == "stone")
                {
                    return true;
                }


                string cat = t.category != null ? (t.category.id ?? "") : "";
                cat = cat.ToLowerInvariant();
                if (cat.Contains("flower") || cat.Contains("mushroom"))
                {
                    return true;
                }

}
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchRewards.cs silent catch: " + __e.Message); }
return false;
    }


    static bool IsFishLike(Thing t)
    {
        if (t == null)
        {
            return false;
        }


            string id = (t.id ?? "").ToLowerInvariant();
            if (id.Contains("fish") || id.Contains("seafood"))
            {
                return true;
            }

            string cat = t.category != null ? (t.category.id ?? "") : "";
            if (cat.ToLowerInvariant().Contains("fish"))
            {
                return true;
            }

return false;
    }


    /// <summary>
    /// Region dispatch mail should arrive edible: zero decay + fresh material when possible.
    /// </summary>
    static void KeepFoodFresh(Thing t)
    {
        if (t == null)
        {
            return;
        }

        try
        {
            t.decay = 0;
        }
        catch
        {
            try { t.Decay(0); } catch { }
        }

        // Soft produce material reset for edible mail contract.
        try
        {
            string cat = t.category != null ? (t.category.id ?? "") : "";
            bool foodish = false;
            try { foodish = t.IsFood; } catch { foodish = false; }
            if (!foodish)
            {
                string id = (t.id ?? "").ToLowerInvariant();
                foodish = cat.ToLowerInvariant().Contains("food")
                    || id.Contains("fish") || id.Contains("fruit") || id.Contains("berry")
                    || id.Contains("mushroom") || id.Contains("meat") || id.Contains("egg");
            }

            if (foodish)
            {
                ForceFreshProduceMaterial(t);
            }
        }
        catch
        {
        }
    }


    static void SpoilFood(Thing t)
    {
        if (t == null)
        {
            return;
        }

        try
        {
            int max = 1000;
            try
            {
                max = Math.Max(10, t.MaxDecay);
            }
            catch
            {
                max = 1000;
            }

            int target = (max / 4) * 3 + Math.Max(1, max / 20);
            if (target >= max)
            {
                target = max - 1;
            }

            try
            {
                t.decay = target;
            }
            catch
            {

                    t.Decay(target);

}
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchRewards.cs silent catch: " + __e.Message); }
}
}
