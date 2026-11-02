using System;
using System.Collections.Generic;
using ACS.API;
using ACS.Frame;
using ACS.Skin;
using ACS.Tiling;
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

        skinId = SkinIndex.NormalizeId(skinId);
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

        var states = PlayState.ListEmoteStates(pc);
        if (states.Count == 0) {
            return "No emote ACS clip on current skin.";
        }

        if (clip.IsEmpty()) {
            if (states.Count == 1) {
                return PlayState.StartEmote(pc, states[0])
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

        return PlayState.StartEmote(pc, resolved)
            ? "Emote started: " + resolved
            : "Failed to start emote.";
    }

    /// <summary>
    /// Reload PNG without restart: clear runtime caches, ListSkins, rebind PC.
    /// Does not hot-swap the DLL; resources and runtime caches only.
    /// </summary>
    [ConsoleCommand]
    public static string ReloadAcs() => ReloadAcsCore();

    /// <summary>手动全量重载主体（改图/加角色后敲一次 ACS.ReloadAcs）。</summary>
    internal static string ReloadAcsCore()
    {
        try {
            PccRenderPatch.ClearRuntimeCaches();
            RenderFramePatch.ClearRuntimeCaches(); // NPC 侧：帧推进 + 战斗贴图“已应用”去重
            PlayState.ClearHostileScanCache();
            FrameChoice.InvalidateStaticCombatMap();
            TiledSprite.Clear();                   // 分片贴图 store：连字节账一起丢，强制重解

            TextureSkinCatalog.Invalidate();
            SkinListPatch.ClearLoadedSprites();
            SpriteReplacer.ListSkins();
            PcSkinBinder.Ensure("ReloadAcs", forceListSkins: false);

            // 强制在场换肤角色重烘焙（动态底图 idle 的 _Rect / 贴图靠 RefreshSprite
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

}
