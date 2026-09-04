using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ACS.Patches;

/// <summary>
/// Texture 角色贴图编目。不并进 ListSkins。
/// 扫非 builtin 的 Texture 根目录、Texture/Skin，以及一层非 Item 子目录。
/// 只有 _acs_ 没有底图的 PCR 也会作为 base 列出。
/// </summary>
internal static class TextureSkinCatalog
{
    internal const int MaxSpriteHeight = 512;

    private static readonly string[] SkipDirNames =
    {
        "Item", "Portrait", "PCC", "Actor", "Map", "Sound", "Lang", "LangMod",
        "Texture Replace", "TalkText", "Map Piece"
    };

    private static Dictionary<string, Entry>? _bases;
    private static readonly Dictionary<string, string> LabelByPath =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 扫描期顺带留底的目录清单：目录全路径 → 该目录全部 *.png 的全路径。
    /// EnsureScanned/AddDir 本来就会对每个目录 GetFiles 一次，把结果留在这，让
    /// EnsureSkin（CollectFilesForBase）与 SkinChangeWatcher.TrackTextureSkins 按
    /// baseId 前缀过滤复用，避免对同一批目录反复枚举。与 _bases 同生命周期，Invalidate 一并清空。
    /// </summary>
    private static Dictionary<string, string[]>? _dirPngFiles;

    internal readonly struct Entry
    {
        public readonly string Id;
        public readonly string Path;
        public readonly string Label;

        public Entry(string id, string path, string label)
        {
            Id = id;
            Path = path;
            Label = label;
        }
    }

    internal static void Invalidate()
    {
        _bases = null;
        _dirPngFiles = null;
        LabelByPath.Clear();
    }

    internal static bool IsTextureId(string? id)
    {
        if (id.IsEmpty()) {
            return false;
        }

        EnsureScanned();
        return _bases is not null && _bases.ContainsKey(PcSkinPatch.NormalizeSkinId(id));
    }

    internal static bool IsTexture(SpriteData? data)
    {
        if (data is null || data.path.IsEmpty()) {
            return false;
        }

        return LabelByPath.ContainsKey(data.path);
    }

    internal static string? GetSourceLabel(SpriteData? data)
    {
        if (data is null || data.path.IsEmpty()) {
            return null;
        }

        if (LabelByPath.TryGetValue(data.path, out var label)) {
            return label;
        }

        return LabelFromSkinPath(data.path);
    }

    internal static string LabelFromSkinPath(string path)
    {
        try {
            string? dir = Path.GetDirectoryName(path);
            if (dir.IsEmpty()) {
                return "Skin";
            }

            string name = Path.GetFileName(dir);
            if (name.Equals("Skin", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Texture", StringComparison.OrdinalIgnoreCase)) {
                string? parent = Path.GetDirectoryName(dir);
                if (!parent.IsEmpty()) {
                    string parentName = Path.GetFileName(parent);
                    if (parentName.Equals("Custom", StringComparison.OrdinalIgnoreCase)
                        || parentName.Equals("Package", StringComparison.OrdinalIgnoreCase)) {
                        return "Custom";
                    }

                    return parentName;
                }
            }

            return name;
        }
        catch (Exception) {
            return "Skin";
        }
    }

    internal static List<Entry> ListBaseItems()
    {
        EnsureScanned();
        var list = new List<Entry>();
        if (_bases is null) {
            return list;
        }

        foreach (var kv in _bases) {
            list.Add(kv.Value);
        }

        list.Sort((a, b) =>
        {
            int c = string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    internal static SpriteReplacer? EnsureSkin(string? rawId)
    {
        if (rawId.IsEmpty()) {
            return null;
        }

        string id = PcSkinPatch.NormalizeSkinId(rawId);
        if (id.IsEmpty()) {
            return null;
        }

        if (SpriteReplacer.dictSkins is not null
            && SpriteReplacer.dictSkins.TryGetValue(id, out var existing)
            && existing is not null) {
            return existing;
        }

        EnsureScanned();
        if (_bases is null || !_bases.TryGetValue(id, out var entry)) {
            return null;
        }

        var subset = CollectFilesForBase(id, entry);
        if (subset.Count == 0) {
            return null;
        }

        if (!subset.ContainsKey(id)) {
            subset[id] = entry.Path;
        }

        var sorted = subset.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        var replacer = new SpriteReplacer
        {
            data = new SpriteData { path = subset[id] }
        };
        replacer.BuildSuffixData(id, subset, sorted);
        if (replacer.suffixes.TryGetValue("", out var baseData) && baseData is not null) {
            replacer.data = baseData;
        }
        else {
            replacer.data.Init();
        }

        if (SpriteReplacer.dictSkins is null) {
            return replacer;
        }

        SpriteReplacer.dictSkins[id] = replacer;
        SkinListPatch.NormalizeAcsSuffixes(replacer, id);
        SkinListPatch.EnsureBaseFromAcsIdle(replacer, id);
        return replacer;
    }

    private static Dictionary<string, string> CollectFilesForBase(string id, Entry entry)
    {
        var subset = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? dir = Path.GetDirectoryName(entry.Path + ".png");
        if (dir.IsEmpty() || !Directory.Exists(dir)) {
            subset[id] = entry.Path;
            return subset;
        }

        try {
            foreach (var file in GetBasePngCandidates(dir, id)) {
                string fileId = Path.GetFileNameWithoutExtension(file);
                if (fileId.IsEmpty()) {
                    continue;
                }

                if (fileId != id
                    && !fileId.StartsWith(id + "_acs_", StringComparison.Ordinal)
                    && !fileId.StartsWith(id + PcSkinPatch.StaticCombatSuffix, StringComparison.Ordinal)) {
                    continue;
                }

                subset[fileId] = Path.ChangeExtension(file, null);
            }
        }
        catch (Exception) {
            subset[id] = entry.Path;
        }

        if (subset.Count == 0) {
            subset[id] = entry.Path;
        }

        return subset;
    }

    /// <summary>
    /// 返回某 base 在同目录下的候选 png 全路径：文件清单以 baseId 开头（等价于对同一份目录
    /// 枚举结果做 GetFiles(dir, id + "*.png")）。目录在扫描缓存里就直接从缓存过滤，不再碰磁盘；
    /// 缓存未覆盖该目录时（一轮扫描后正常不会发生）按旧行为枚举一次 *.png 并回填缓存。
    /// 供 CollectFilesForBase 与 SkinChangeWatcher.TrackTextureSkins 共用，避免重复枚举同一目录。
    /// </summary>
    internal static string[] GetBasePngCandidates(string dir, string id)
    {
        if (!dir.IsEmpty() && _dirPngFiles is not null
            && _dirPngFiles.TryGetValue(dir, out var cached)) {
            return FilterCandidatesByPrefix(cached, id);
        }

        // 目录未在缓存中：退化为现行为（全目录枚举一次），并把结果留底供后续复用。
        string[] files = Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly);
        if (!dir.IsEmpty() && _dirPngFiles is not null) {
            _dirPngFiles[dir] = files;
        }

        return FilterCandidatesByPrefix(files, id);
    }

    /// <summary>
    /// 从一份“全部 *.png”清单里筛出文件名以 id 开头的项。缓存里只有 .png 结尾的文件，
    /// 前缀比较用大小写不敏感，与 Windows 上 GetFiles(dir, id + "*.png") 的通配匹配口径一致。
    /// </summary>
    private static string[] FilterCandidatesByPrefix(string[] pngFiles, string id)
    {
        if (pngFiles.Length == 0) {
            return pngFiles;
        }

        List<string>? hit = null;
        foreach (string f in pngFiles) {
            if (Path.GetFileName(f).StartsWith(id, StringComparison.OrdinalIgnoreCase)) {
                (hit ??= new List<string>()).Add(f);
            }
        }

        return hit is null ? Array.Empty<string>() : hit.ToArray();
    }

    private static void EnsureScanned()
    {
        if (_bases is not null) {
            return;
        }

        _bases = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        _dirPngFiles = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        LabelByPath.Clear();

        try {
            AddTree(new DirectoryInfo(CorePath.custom + "Texture"), "Custom");
        }
        catch (Exception ex) {
            AcsMod.Warn($"TextureSkinCatalog custom: {ex.Message}");
        }

        try {
            foreach (var (dir, package) in PackageIterator.GetDirectoriesEx("Texture", useCache: true)) {
                if (package is { builtin: true }) {
                    continue;
                }

                AddTree(dir, ResolvePackageLabel(package));
            }
        }
        catch (Exception ex) {
            AcsMod.Warn($"TextureSkinCatalog mods: {ex.Message}");
        }
    }

    private static string ResolvePackageLabel(EMod? package)
    {
        if (package is null) {
            return "Texture";
        }

        if (!package.title.IsEmpty()) {
            return package.title;
        }

        if (!package.id.IsEmpty()) {
            return package.id;
        }

        return "Texture";
    }

    private static void AddTree(DirectoryInfo? root, string label)
    {
        if (root is null || !root.Exists) {
            return;
        }

        AddDir(root, label);
        DirectoryInfo[] children;
        try {
            children = root.GetDirectories();
        }
        catch (Exception) {
            return;
        }

        foreach (var child in children) {
            if (ShouldSkipDir(child.Name)) {
                continue;
            }

            AddTree(child, label);
        }
    }

    private static bool ShouldSkipDir(string name)
    {
        foreach (var skip in SkipDirNames) {
            if (name.Equals(skip, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static void AddDir(DirectoryInfo dir, string label)
    {
        if (dir is null || !dir.Exists || _bases is null) {
            return;
        }

        FileInfo[] files;
        try {
            files = dir.GetFiles("*.png", SearchOption.TopDirectoryOnly);
        }
        catch (Exception) {
            return;
        }

        // 把这次枚举结果留底供后续按 base 过滤复用（保留“所有 *.png”而非只留过滤通过的，
        // 与 CollectFilesForBase 的“先按名枚举、再按后缀名过滤”口径保持一致）。
        if (_dirPngFiles is not null) {
            string[] paths = new string[files.Length];
            for (int i = 0; i < files.Length; i++) {
                paths[i] = files[i].FullName;
            }

            _dirPngFiles[dir.FullName] = paths;
        }

        foreach (var file in files) {
            string fileId = Path.GetFileNameWithoutExtension(file.Name);
            if (fileId.IsEmpty() || fileId.IndexOfAny(new[] { '/', '\\' }) >= 0) {
                continue;
            }

            if (fileId.IndexOf("副本", StringComparison.Ordinal) >= 0
                || fileId.IndexOf("copy", StringComparison.OrdinalIgnoreCase) >= 0) {
                continue;
            }

            if (!TryGetPngSize(file.FullName, out _, out int height) || height > MaxSpriteHeight) {
                continue;
            }

            string path = Path.ChangeExtension(file.FullName, null);
            LabelByPath[path] = label;
            string baseId = PcSkinPatch.NormalizeSkinId(fileId);
            if (baseId.IsEmpty()) {
                continue;
            }

            bool isRealBase = fileId.Equals(baseId, StringComparison.OrdinalIgnoreCase);
            if (_bases.TryGetValue(baseId, out var existing)) {
                if (isRealBase) {
                    _bases[baseId] = new Entry(baseId, path, label);
                }

                continue;
            }

            _bases[baseId] = new Entry(baseId, path, label);
        }
    }

    internal static bool TryGetPngHeight(string pngPath, out int height)
        => TryGetPngSize(pngPath, out _, out height);

    internal static bool TryGetPngSize(string pngPath, out int width, out int height)
    {
        width = 0;
        height = 0;
        try {
            using var fs = File.OpenRead(pngPath);
            byte[] buf = new byte[24];
            if (fs.Read(buf, 0, 24) < 24) {
                return false;
            }

            if (buf[0] != 137 || buf[1] != 80 || buf[2] != 78 || buf[3] != 71) {
                return false;
            }

            width = (buf[16] << 24) | (buf[17] << 16) | (buf[18] << 8) | buf[19];
            height = (buf[20] << 24) | (buf[21] << 16) | (buf[22] << 8) | buf[23];
            return width > 0 && height > 0;
        }
        catch (Exception) {
            return false;
        }
    }
}
