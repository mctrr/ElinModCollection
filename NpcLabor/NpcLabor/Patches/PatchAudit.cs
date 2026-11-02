using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using ReflexCLI.Attributes;

namespace NpcLabor.Patches;

/// <summary>
/// Mounts our Harmony patches one patch class at a time, then verifies that every
/// declared patch method actually landed on a target.
///
/// Why this exists: <c>Harmony.PatchAll(assembly)</c> walks every [HarmonyPatch] class
/// in a single pass. When a game update changes a signature (parameter added, type
/// renamed, method made generic), the offending class throws while mounting and the
/// walk aborts - every patch class after it is never applied, and the only trace is one
/// catch-all log line. The symptom is then "a feature acts weird" with nothing pointing
/// at the real cause.
///
/// Mounting per class keeps one dead patch from taking the rest down with it, and the
/// audit that follows names every patch method that did not land.
/// </summary>
internal static class PatchAudit
{
    static readonly Type[] PatchMethodAttributes =
    {
        typeof(HarmonyPrefix),
        typeof(HarmonyPostfix),
        typeof(HarmonyTranspiler),
        typeof(HarmonyFinalizer),
    };

    /// <summary>
    /// Mount every [HarmonyPatch] class in <paramref name="asm"/> with a per-class
    /// isolation boundary, then audit the result.
    /// </summary>
    internal static void ApplyAll(Harmony harmony, Assembly asm)
    {
        int classes = 0;
        int failed = 0;

        foreach (Type type in AccessTools.GetTypesFromAssembly(asm))
        {
            if (!type.IsDefined(typeof(HarmonyPatch), true))
            {
                continue;
            }

            classes++;
            try
            {
                harmony.CreateClassProcessor(type).Patch();
            }
            catch (Exception ex)
            {
                failed++;
                Plugin.LogError($"[patch] {type.Name} failed to mount, every hook in it is dead: {ex.Message}");
            }
        }

        string line = $"[patch] mounted {classes - failed}/{classes} patch classes";
        if (failed > 0)
        {
            line += $" ({failed} failed, see errors above)";
        }

        Plugin.LogInfo(line);
        Verify(asm);
    }

    /// <summary>
    /// Cross-checks every declared patch method against the methods Harmony actually
    /// patched. A declared patch method missing from the global patch list means its
    /// target never resolved - almost always a signature change in the game assembly.
    /// </summary>
    internal static string Verify(Assembly asm)
    {
        List<MethodBase> declared = ListDeclaredPatchMethods(asm);
        HashSet<string> mounted = ListMountedPatchMethods();

        List<string> missing = new List<string>();
        foreach (MethodBase m in declared)
        {
            if (!mounted.Contains(Key(m)))
            {
                missing.Add(Name(m));
            }
        }

        if (missing.Count == 0)
        {
            string ok = $"[patch] audit ok: all {declared.Count} patch methods mounted";
            Plugin.LogInfo(ok);
            return ok;
        }

        StringBuilder sb = new StringBuilder();
        sb.Append($"[patch] audit: {missing.Count}/{declared.Count} patch methods NOT mounted ");
        sb.Append("(game signature change?): ");
        sb.Append(string.Join(", ", missing));

        string msg = sb.ToString();
        Plugin.LogError(msg);
        return msg;
    }

    /// <summary>Every method in <paramref name="asm"/> carrying a Harmony patch attribute.</summary>
    static List<MethodBase> ListDeclaredPatchMethods(Assembly asm)
    {
        List<MethodBase> list = new List<MethodBase>();

        foreach (Type type in AccessTools.GetTypesFromAssembly(asm))
        {
            if (!type.IsDefined(typeof(HarmonyPatch), true))
            {
                continue;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                                     | BindingFlags.Static | BindingFlags.Instance
                                     | BindingFlags.DeclaredOnly;

            foreach (MethodInfo m in type.GetMethods(flags))
            {
                foreach (Type attr in PatchMethodAttributes)
                {
                    if (m.IsDefined(attr, true))
                    {
                        list.Add(m);
                        break;
                    }
                }
            }
        }

        return list;
    }

    /// <summary>
    /// Every patch method Harmony has actually registered, across all Harmony instances.
    /// Comparing against this global list avoids re-deriving the declared target of each
    /// patch, which is the part that would silently drift on a signature change.
    /// </summary>
    static HashSet<string> ListMountedPatchMethods()
    {
        HashSet<string> set = new HashSet<string>();

        foreach (MethodBase target in Harmony.GetAllPatchedMethods())
        {
            // Fully qualified: our own namespace is also called NpcLabor.Patches.
            HarmonyLib.Patches? info = Harmony.GetPatchInfo(target);
            if (info == null)
            {
                continue;
            }

            Collect(set, info.Prefixes);
            Collect(set, info.Postfixes);
            Collect(set, info.Transpilers);
            Collect(set, info.Finalizers);
        }

        return set;
    }

    static void Collect(HashSet<string> set, IEnumerable<HarmonyLib.Patch>? patches)
    {
        if (patches == null)
        {
            return;
        }

        foreach (HarmonyLib.Patch p in patches)
        {
            MethodBase? m = p != null ? p.PatchMethod : null;
            if (m != null)
            {
                set.Add(Key(m));
            }
        }
    }

    /// <summary>
    /// Identity for a patch method. MetadataToken (not reference equality) so a
    /// MethodInfo fetched here and the one Harmony stored compare equal.
    /// </summary>
    static string Key(MethodBase m)
    {
        int token;
        try
        {
            token = m.MetadataToken;
        }
        catch
        {
            token = 0;
        }

        Type? declaring = m.DeclaringType;
        string type = declaring != null ? declaring.FullName ?? declaring.Name : "";
        return type + "::" + m.Name + ":" + token.ToString();
    }

    static string Name(MethodBase m)
    {
        Type? declaring = m.DeclaringType;
        return (declaring != null ? declaring.Name : "?") + "." + m.Name;
    }
}

[ConsoleCommandClassCustomizer("")]
internal static class PatchAuditConsole
{
    /// <summary>Re-run the Harmony mount audit: NpcLaborPatches</summary>
    [ConsoleCommand("")]
    public static string NpcLaborPatches()
    {
        try
        {
            return PatchAudit.Verify(Assembly.GetExecutingAssembly());
        }
        catch (Exception ex)
        {
            Plugin.LogWarn("NpcLaborPatches: " + ex.Message);
            return "NpcLaborPatches failed: " + ex.Message;
        }
    }
}
