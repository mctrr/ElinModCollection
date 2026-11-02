using System.Reflection;

[assembly: AssemblyTitle(NpcLabor.ModInfo.Name)]
[assembly: AssemblyProduct(NpcLabor.ModInfo.Name)]
// AssemblyVersion is the binary identity and stays pinned; the build date lives in
// FileVersion / ProductVersion so every shipped DLL can be identified on sight:
//   FileVersion    -> "1.14.514.412"            (commit count, monotonic)
//   ProductVersion -> "1.14.514+ddc7491 2026-11-01 19:04"
[assembly: AssemblyVersion(NpcLabor.ModInfo.Version)]
[assembly: AssemblyFileVersion(NpcLabor.ModInfo.Version + "." + NpcLabor.BuildStamp.Rev)]
[assembly: AssemblyInformationalVersion(NpcLabor.ModInfo.Version + "+" + NpcLabor.BuildStamp.Id)]
