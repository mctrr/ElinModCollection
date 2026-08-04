using System;
using System.Collections.Generic;
using System.IO;

namespace ACS.API;

/// <summary>
/// Skin/bcs_static_map.ini - map foreign static skins onto BCS keys.
/// v1 only consumes [combat] -> _bcs_combat; other sections are ignored for later use.
/// </summary>
internal static class StaticCombatMap
{
    internal const string FileName = "bcs_static_map.ini";
    internal const string CombatSection = "combat";

    private static readonly Dictionary<string, string> CombatByBase =
        new(StringComparer.OrdinalIgnoreCase);

    // path -> last write utc of every file merged into the cache
    private static readonly Dictionary<string, DateTime> LoadedFiles =
        new(StringComparer.OrdinalIgnoreCase);

    private static bool _hadAnyFile;

    /// <summary>base skin id -> foreign skin id (no extension).</summary>
    internal static IReadOnlyDictionary<string, string> CombatMappings => CombatByBase;

    /// <summary>
    /// Reload map files when set of paths or any mtime changed.
    /// <paramref name="extraSkinDirs"/> = directories that currently host skin pngs (package Skin etc.).
    /// </summary>
    internal static void Refresh(IEnumerable<string>? extraSkinDirs = null)
    {
        try {
            var paths = CollectMapPaths(extraSkinDirs);
            if (paths.Count == 0) {
                if (CombatByBase.Count > 0 || _hadAnyFile) {
                    CombatByBase.Clear();
                    LoadedFiles.Clear();
                    _hadAnyFile = false;
                    ACS.AcsMod.Log($"static combat map cleared (no {FileName})");
                }
                return;
            }

            if (!NeedsReload(paths)) {
                return;
            }

            CombatByBase.Clear();
            LoadedFiles.Clear();
            int totalRows = 0;
            int skippedDup = 0;

            foreach (var path in paths) {
                var writeUtc = File.GetLastWriteTimeUtc(path);
                skippedDup += ParseFile(path, ref totalRows);
                LoadedFiles[path] = writeUtc;
            }

            _hadAnyFile = true;
            ACS.AcsMod.Log($"static combat map loaded: {totalRows} combat entrie(s) from {paths.Count} file(s)"
                + (skippedDup > 0 ? $", {skippedDup} duplicate(s) skipped" : string.Empty));
        }
        catch (Exception ex) {
            ACS.AcsMod.Warn($"StaticCombatMap.Refresh: {ex.Message}");
        }
    }

    /// <summary>Force re-read on next Refresh (e.g. ACS.ReloadAcs).</summary>
    internal static void Invalidate()
    {
        LoadedFiles.Clear();
        _hadAnyFile = false;
    }

    private static bool NeedsReload(List<string> paths)
    {
        if (LoadedFiles.Count != paths.Count) {
            return true;
        }

        foreach (var path in paths) {
            if (!LoadedFiles.TryGetValue(path, out var prevUtc)) {
                return true;
            }

            if (File.GetLastWriteTimeUtc(path) != prevUtc) {
                return true;
            }
        }

        return false;
    }

    private static List<string> CollectMapPaths(IEnumerable<string>? extraSkinDirs)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAddDir(string? dir)
        {
            if (dir.IsEmpty()) {
                return;
            }

            string full;
            try {
                full = Path.GetFullPath(Path.Combine(dir, FileName));
            }
            catch (Exception) {
                return;
            }

            if (!seen.Add(full)) {
                return;
            }

            if (File.Exists(full)) {
                paths.Add(full);
            }
        }

        // 1) Custom/Skin (user overrides first in list order when parsing:
        //    first writer wins for duplicate base keys, so Custom should load first.)
        try {
            string custom = CorePath.custom;
            if (!custom.IsEmpty()) {
                TryAddDir(Path.Combine(custom, "Skin"));
            }
        }
        catch (Exception) {
            // ignore
        }

        // 2) Activated package Skin folders (ini-only compat packages have no pngs in dictSkins).
        try {
            var mods = ModManager.Instance;
            var packages = mods?.packages;
            if (packages is not null) {
                foreach (var pkg in packages) {
                    if (pkg is null || !pkg.activated || pkg.dirInfo is null) {
                        continue;
                    }

                    TryAddDir(Path.Combine(pkg.dirInfo.FullName, "Skin"));
                }
            }
        }
        catch (Exception) {
            // ModManager not ready yet
        }

        // 3) Skin dirs inferred from currently loaded png skins.
        if (extraSkinDirs is not null) {
            foreach (var dir in extraSkinDirs) {
                TryAddDir(dir);
            }
        }

        return paths;
    }

    /// <returns>number of duplicate base rows skipped in this file</returns>
    private static int ParseFile(string path, ref int totalRows)
    {
        string? section = null;
        int skippedDup = 0;
        int fileRows = 0;

        foreach (var raw in File.ReadAllLines(path)) {
            string line = raw.Trim();
            if (line.Length == 0) {
                continue;
            }

            if (line[0] == ';' || line[0] == '#' || line.StartsWith("//", StringComparison.Ordinal)) {
                continue;
            }

            if (line[0] == '[' && line.EndsWith("]", StringComparison.Ordinal)) {
                section = line.Substring(1, line.Length - 2).Trim();
                continue;
            }

            // Only [combat] is active in v1; snow/wet/etc. ignored.
            if (!string.Equals(section, CombatSection, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq <= 0) {
                continue;
            }

            string baseId = NormalizeId(line.Substring(0, eq));
            string sourceId = NormalizeId(line.Substring(eq + 1));
            if (baseId.IsEmpty() || sourceId.IsEmpty()) {
                continue;
            }

            if (string.Equals(baseId, sourceId, StringComparison.OrdinalIgnoreCase)) {
                ACS.AcsMod.Warn($"static combat map skip self-map '{baseId}' in {path}");
                continue;
            }

            if (CombatByBase.ContainsKey(baseId)) {
                skippedDup++;
                ACS.AcsMod.Warn($"static combat map duplicate base '{baseId}' -> keep '{CombatByBase[baseId]}', skip '{sourceId}' ({path})");
                continue;
            }

            CombatByBase[baseId] = sourceId;
            fileRows++;
            totalRows++;
        }

        if (fileRows > 0) {
            ACS.AcsMod.Log($"static combat map: {fileRows} from {path}");
        }

        return skippedDup;
    }

    private static string NormalizeId(string id)
    {
        if (id.IsEmpty()) {
            return string.Empty;
        }

        id = id.Trim().Trim('"');
        if (id.IsEmpty()) {
            return string.Empty;
        }

        try {
            if (id.IndexOfAny(new[] { '/', '\\' }) >= 0) {
                id = Path.GetFileName(id);
            }
        }
        catch (Exception) {
            // keep trimmed id
        }

        if (id.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || id.EndsWith(".PNG", StringComparison.Ordinal)) {
            id = Path.GetFileNameWithoutExtension(id);
        }

        return id.Trim();
    }
}
