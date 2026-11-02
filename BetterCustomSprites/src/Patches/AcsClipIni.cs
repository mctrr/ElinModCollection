using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ACS.Patches;

/// <summary>
/// ACS clip 的 .ini 补写。
/// 只有「文件名里读不出帧数」的片才需要它：帧数/时长/缩放能由路径解析出来的，一律不写盘
/// （原版 LoadAnimationIni 之后 BCS postfix 会按文件名把三键盖回去，ini 纯冗余）。
/// </summary>
internal static class AcsClipIni
{
    internal static void WriteFrameMeta(SpriteData data)
    {
        if (data is null || data.path.IsEmpty()) {
            return;
        }

        // 文件名带 _acs_xxx#interval_begin-end → 帧数/时长/缩放全在文件名里，
        // 不写 ini、也不创建空文件（clip 收进子目录后保持目录干净）。
        bool isAcs = data.path.IndexOf("_acs_", StringComparison.Ordinal) >= 0;
        if (isAcs && SkinListPatch.ResolveClipFromPath(data) is { Length: > 0 }) {
            return;
        }

        string path = data.path + ".ini";
        var map = ReadMap(path);
        map["frame"] = data.frame.ToString(CultureInfo.InvariantCulture);
        map["time"] = data.time.ToString(CultureInfo.InvariantCulture);
        map["scale"] = data.scale.ToString(CultureInfo.InvariantCulture);
        WriteMap(path, map);
    }

    private static Dictionary<string, string> ReadMap(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try {
            if (!File.Exists(path)) {
                return map;
            }

            foreach (string line in File.ReadAllLines(path)) {
                string trim = line.Trim();
                if (trim.Length == 0 || trim.StartsWith(";") || trim.StartsWith("#")) {
                    continue;
                }

                int eq = trim.IndexOf('=');
                if (eq <= 0) {
                    continue;
                }

                string k = trim.Substring(0, eq).Trim();
                string v = trim.Substring(eq + 1).Trim();
                if (!k.IsEmpty()) {
                    map[k] = v;
                }
            }
        }
        catch (Exception) {
        }

        return map;
    }

    private static void WriteMap(string path, Dictionary<string, string> map)
    {
        try {
            var lines = new List<string>(map.Count);
            foreach (string key in new[] { "frame", "time", "scale" }) {
                if (map.TryGetValue(key, out string? value)) {
                    lines.Add(key + "=" + value);
                    map.Remove(key);
                }
            }

            foreach (var kv in map) {
                lines.Add(kv.Key + "=" + kv.Value);
            }

            File.WriteAllLines(path, lines);
        }
        catch (Exception) {
        }
    }
}
