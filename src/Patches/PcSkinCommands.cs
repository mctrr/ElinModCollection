using System;
using System.Collections.Generic;
using ACS.API;
using ReflexCLI.Attributes;

namespace ACS.Patches;

/// <summary>
/// 控制台：ACS.SetSkin / ClearSkin / ListSkins。
/// </summary>
/// <summary>
/// 控制台命令。类级 <see cref="ConsoleCommandClassCustomizerAttribute"/> 把 ReflexCLI
/// 默认的“命名空间.类名”前缀（ACS.Patches.PcSkinCommands）覆盖成短前缀 ACS，
/// 命令即注册为 ACS.SetSkin / ACS.ReloadAcs 等。
/// </summary>
[ConsoleCommandClassCustomizer("ACS")]
internal static class PcSkinCommands
{
    [ConsoleCommand]
    public static string SetSkin(string skinId)
    {
        var pc = EClass.pc;
        if (pc is null) {
            return "PC not found.";
        }

        if (skinId.IsEmpty()) {
            return "Usage: ACS.SetSkin <skinId>";
        }

        skinId = PcSkinPatch.NormalizeSkinId(skinId);
        SpriteReplacer.ListSkins();
        var skin = SpriteReplacer.dictSkins.TryGetValue(skinId);
        if (skin is null) {
            return $"Skin '{skinId}' not found in Skin / Texture folders.";
        }

        pc.c_idSpriteReplacer = skinId;
        PcSkinPatch.RecreateRenderer(pc);
        if (pc.renderer is CharaRenderer cr) {
            PcSkinPatch.TryApplyPcSkin(cr);
        }
        return $"PC skin set to '{skinId}'.";
    }

    [ConsoleCommand]
    public static string ClearSkin()
    {
        var pc = EClass.pc;
        if (pc is null) {
            return "PC not found.";
        }

        pc.c_idSpriteReplacer = null;
        PcSkinPatch.RecreateRenderer(pc);
        return "PC skin cleared.";
    }

    [ConsoleCommand]
    public static string ListSkins()
    {
        SpriteReplacer.ListSkins();
        var skins = SpriteReplacer.dictSkins;
        if (skins.Count == 0) {
            return "No skins found in Skin / Texture folders.";
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Found {skins.Count} skin(s):");
        foreach (var entry in skins) {
            int acsCount = AcsSuffixes.CountClips(entry.Value);
            sb.AppendLine($"  {entry.Key}" + (acsCount > 0 ? $" ({acsCount} ACS clip(s))" : ""));
        }
        return sb.ToString();
    }

    [ConsoleCommand]
    public static string Emote(string clip = "")
    {
        var pc = EClass.pc;
        if (pc is null) {
            return "PC not found.";
        }

        var states = AcsStateResolver.ListEmoteStates(pc);
        if (states.Count == 0) {
            return "No emote ACS clip on current skin.";
        }

        if (clip.IsEmpty()) {
            if (states.Count == 1) {
                return AcsStateResolver.StartEmote(pc, states[0])
                    ? "Emote started: " + states[0]
                    : "Failed to start emote.";
            }

            return "Usage: ACS.Emote <clip>  clips: " + string.Join(", ", states);
        }

        string resolved = clip;
        if (!states.Contains(resolved)) {
            if (states.Contains("emote." + clip)) {
                resolved = "emote." + clip;
            }
            else {
                return "Unknown clip '" + clip + "'. clips: " + string.Join(", ", states);
            }
        }

        return AcsStateResolver.StartEmote(pc, resolved)
            ? "Emote started: " + resolved
            : "Failed to start emote.";
    }

    /// <summary>
    /// Reload PNG without restart: clear lift/pad/frame caches, ListSkins, rebind PC.
    /// Does not hot-swap the DLL; resources and runtime caches only.
    /// </summary>
    [ConsoleCommand]
    public static string ReloadAcs() => ReloadAcsCore();

    /// <summary>手动命令与自动热重载共用的重载主体。</summary>
    internal static string ReloadAcsCore()
    {
        try {
            AcsSpritePresenter.ClearCaches();
            PccRenderPatch.ClearRuntimeCaches();
            RenderFramePatch.ClearRuntimeCaches(); // NPC 侧：帧推进 + 战斗贴图“已应用”去重
            AcsStateResolver.ClearHostileScanCache();
            ACS.API.StaticCombatMap.Invalidate();

            TextureSkinCatalog.Invalidate();
            SkinListPatch.ClearLoadedSprites();
            SpriteReplacer.ListSkins();
            // ListSkins 已把 watcher 文件基线更新为磁盘最新值；清 pending 防自触发
            SkinChangeWatcher.ResetBaseline();
            PcSkinBinder.Ensure("ReloadAcs", forceListSkins: false);

            // 强制在场换肤角色重烘焙（静态底图 idle 的 _Rect / 贴图靠 RefreshSprite
            // 刷新；改图后不主动刷会一直显示旧烘焙结果）。
            int refreshed = 0;
            var map = EClass._map;
            if (map?.charas != null) {
                foreach (var c in map.charas) {
                    if (c is null || c.IsPC || c.c_idSpriteReplacer.IsEmpty()) {
                        continue;
                    }

                    if (c.renderer is CharaRenderer cr) {
                        cr.RefreshSprite();
                        refreshed++;
                    }
                }
            }

            var pc = EClass.pc;
            if (pc is null) {
                return $"ACS caches cleared + ListSkins done (no PC). NPC refreshed={refreshed}";
            }

            pc.renderer?.RefreshSprite();
            int clips = AcsSuffixes.CountClips(pc.spriteReplacer);
            bool hasStatic = PcSkinPatch.GetStaticSkinData(pc.spriteReplacer) is not null;
            return $"ACS reloaded: id={pc.c_idSpriteReplacer} clips={clips} static={hasStatic} npcRefreshed={refreshed}";
        }
        catch (Exception ex) {
            return $"ReloadAcs failed: {ex.Message}";
        }
    }

    /// <summary>
    /// 热更两级入口：
    ///  - <paramref name="structural"/>（ini 变更 / 文件删除 / 无变更集）→ 全量 ReloadAcsCore
    ///  - 纯 PNG 内容变化 → 只失效对应 SpriteData + 只刷新用到它的在场角色，避免
    ///    ListSkins 重建数百 PCR 皮肤导致的主线程 1-2 秒卡顿。
    /// </summary>
    internal static string ReloadChangedSkins(bool structural, string[] changedFiles)
    {
        try {
            if (structural || changedFiles is null || changedFiles.Length == 0) {
                return ReloadAcsCore();
            }

            // 变更路径归一为“无 .png 后缀”以便与 SpriteData.path 匹配
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (string f in changedFiles) {
                string p = f;
                if (p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) {
                    p = p.Substring(0, p.Length - 4);
                }

                paths.Add(p);
            }

            // 1) 失效命中的 SpriteData（懒加载路径会在下次渲染时重读磁盘新 PNG）
            int invalidated = 0;
            var skins = SpriteReplacer.dictSkins;
            if (skins != null) {
                foreach (var skin in skins.Values) {
                    if (skin is null) {
                        continue;
                    }

                    if (InvalidateIfHit(skin.data, paths)) {
                        invalidated++;
                    }

                    if (skin.suffixes != null) {
                        foreach (var sd in skin.suffixes.Values) {
                            if (InvalidateIfHit(sd, paths)) {
                                invalidated++;
                            }
                        }
                    }
                }
            }

            // 1.5) 清 NPC 侧帧推进/战斗去重缓存，防改图后旧帧被“已应用”去重钉住
            RenderFramePatch.ClearRuntimeCaches();

            // 2) 只刷新“用到被改皮肤的”在场角色（静态底图靠 RefreshSprite 重烘焙 _Rect）
            int refreshed = 0;
            var map = EClass._map;
            if (map?.charas != null) {
                foreach (var c in map.charas) {
                    if (c is null || c.IsPC || c.c_idSpriteReplacer.IsEmpty()) {
                        continue;
                    }

                    if (ReplacerTouches(c.spriteReplacer, paths)
                        && c.renderer is CharaRenderer cr) {
                        cr.RefreshSprite();
                        refreshed++;
                    }
                }
            }

            var pc = EClass.pc;
            if (pc is not null
                && ReplacerTouches(pc.spriteReplacer, paths)
                && pc.renderer is CharaRenderer pcr) {
                pcr.RefreshSprite();
                refreshed++;
            }

            return $"ACS hot-reloaded: {invalidated} sprite(s) invalidated, {refreshed} actor(s) refreshed"
                   + (structural ? " (full)" : " (light)");
        }
        catch (Exception ex) {
            AcsMod.Warn($"ReloadChangedSkins failed: {ex.Message}; falling back to full reload");
            return ReloadAcsCore();
        }
    }

    private static bool InvalidateIfHit(SpriteData? data, HashSet<string> paths)
    {
        if (data is null || data.path.IsEmpty() || !paths.Contains(data.path)) {
            return false;
        }

        SkinListPatch.InvalidateData(data);
        return true;
    }

    private static bool ReplacerTouches(SpriteReplacer? replacer, HashSet<string> paths)
    {
        if (replacer is null) {
            return false;
        }

        if (DataTouches(replacer.data, paths)) {
            return true;
        }

        if (replacer.suffixes is null) {
            return false;
        }

        foreach (var sd in replacer.suffixes.Values) {
            if (DataTouches(sd, paths)) {
                return true;
            }
        }

        return false;
    }

    private static bool DataTouches(SpriteData? data, HashSet<string> paths)
        => data is not null && !data.path.IsEmpty() && paths.Contains(data.path);
}
