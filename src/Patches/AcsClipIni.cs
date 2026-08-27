using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ACS.Patches;

/// <summary>
/// ACS clip .ini extras. Vanilla only reads frame/time/scale;
/// heldX/heldY are BCS keys for eat (and any clip) held-item offset, in pixels at 100 ppu.
/// </summary>
internal static class AcsClipIni
{
    private static readonly Dictionary<int, Vector2> HeldCache = new Dictionary<int, Vector2>();
    private static readonly HashSet<int> HeldKnown = new HashSet<int>();

    internal static void ClearCaches()
    {
        HeldCache.Clear();
        HeldKnown.Clear();
    }

    internal static bool TryGetHeldOffset(SpriteData? data, out Vector2 world)
    {
        world = Vector2.zero;
        if (data is null || data.path.IsEmpty()) {
            return false;
        }

        int key = RuntimeHelpers.GetHashCode(data);
        if (HeldKnown.Contains(key)) {
            return HeldCache.TryGetValue(key, out world);
        }

        HeldKnown.Add(key);
        var map = ReadMap(data.path + ".ini");
        if (!TryParsePx(map, "heldX", out float x) && !TryParsePx(map, "heldx", out x)) {
            x = 0f;
        }

        if (!TryParsePx(map, "heldY", out float y) && !TryParsePx(map, "heldy", out y)) {
            y = 0f;
        }

        if (x == 0f && y == 0f) {
            return false;
        }

        world = new Vector2(x / 100f, y / 100f);
        HeldCache[key] = world;
        return true;
    }

    internal static void WriteFrameMeta(SpriteData data)
    {
        if (data is null || data.path.IsEmpty()) {
            return;
        }

        string path = data.path + ".ini";
        var map = ReadMap(path);
        map["frame"] = data.frame.ToString(CultureInfo.InvariantCulture);
        map["time"] = data.time.ToString(CultureInfo.InvariantCulture);
        map["scale"] = data.scale.ToString(CultureInfo.InvariantCulture);
        WriteMap(path, map);
    }

    private static bool TryParsePx(Dictionary<string, string> map, string key, out float value)
    {
        value = 0f;
        if (!map.TryGetValue(key, out string? raw) || raw.IsEmpty()) {
            return false;
        }

        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || float.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
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
