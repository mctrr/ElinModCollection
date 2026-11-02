using NpcLabor.TownLabor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace NpcLabor.Dispatch;

/// <summary>
/// PARTIAL: EloMap region pin icons: draw, restore, restyle, scrub.
/// Split from DungeonDispatchManager.cs (2026-10-02).
/// </summary>
internal static partial class DungeonDispatchManager
{
    /// <summary>
    /// Pure visual overworld pins for active region dispatches.
    /// Uses the 旅行商人营地 (tinkerCamp) zone icon tile so the outing is easy to
    /// spot on EloMap. Never pass "iconFlag" as an AddLight prefab id — that is a
    /// zone tile tag and leaves null SpriteRenderers that NRE in OnChangeHour.
    /// Old elolight pins are still scrubbed/removed for save/session safety.
    /// </summary>
    internal static void RefreshRegionMapMarkers()
    {
        try
        {
            EloMap? map = TryGetEloMap();
            if (map == null)
            {
                return;
            }

            // Restore previous pins, scrub broken lights, then re-pin as camp icons.
            ClearRegionMapMarkers();
            ScrubBrokenEloMapLights(map);

            foreach (DungeonDispatchMission m in Missions)
            {
                if (m == null || !m.isRegion)
                {
                    continue;
                }

                if (!TryAddRegionMarker(map, m.regionGx, m.regionGy))
                {
                    Plugin.LogDebug("region marker add failed @" + m.regionGx + "," + m.regionGy);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("region markers: " + ex.Message);
        }
    }

    /// <summary>
    /// Defensive repair for saves/sessions that already polluted actor.lights with null sr.
    /// Safe to call every hour before vanilla EloMapActor.OnChangeHour.
    /// </summary>
    internal static void SanitizeRegionMapLights()
    {
        try
        {
            EloMap? map = TryGetEloMap();
            if (map == null)
            {
                return;
            }

            ScrubBrokenEloMapLights(map);
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
    }

    /// <summary>
    /// One-shot repair for saves written by older builds (workshop report 2026-10-30:
    /// "a tent near my base that will not go away, world regen does not help").
    ///
    /// <para>
    /// Old builds wrote the region-dispatch pin icon straight into
    /// <c>EloMap.Cell.obj</c>. That field is serialized (<c>EloMap</c> packs
    /// <c>obj * 10</c> into <c>_ints</c> on save and rebuilds cells from it on load)
    /// while <c>Cell.zone</c> is not — so the tent survived recall, world regen and
    /// reload with nothing left to undo it.
    /// </para>
    ///
    /// <para>
    /// Such leftovers are identifiable: <c>cell.obj</c> equals our pin icon (the
    /// tinkerCamp tile art) AND <c>cell.zone == null</c> (real tinkerCamp towns and
    /// dungeons get their zone bound to the cell by <c>EloMap.Refresh</c>, because
    /// they are not instances; field cells never are) AND the coordinate is not an
    /// active outing's pin. Reset those to 0 = empty and erase their tile. Also undo
    /// any <c>zone.icon</c> rewrite on field-like zones from the same era.
    /// </para>
    ///
    /// Run once after load, BEFORE <see cref="RefreshRegionMapMarkers"/> pins anything.
    /// </summary>
    internal static void RepairStuckRegionPins()
    {
        try
        {
            EloMap? map = TryGetEloMap();
            if (map == null)
            {
                return;
            }

            int icon = GetRegionDispatchIcon();
            if (icon <= 0)
            {
                return;
            }

            var active = new HashSet<long>();
            foreach (DungeonDispatchMission m in Missions)
            {
                if (m != null && m.isRegion)
                {
                    active.Add(RegionPinKey(m.regionGx, m.regionGy));
                }
            }

            int w, h, minX, minY;
            try
            {
                w = map.w;
                h = map.h;
                minX = map.minX;
                minY = map.minY;
            }
            catch
            {
                return;
            }

            if (w <= 0 || h <= 0)
            {
                return;
            }

            int scrubbed = 0;
            for (int gy = minY; gy < minY + h; gy++)
            {
                for (int gx = minX; gx < minX + w; gx++)
                {
                    EloMap.Cell? cell = null;
                    try { cell = map.GetCell(gx, gy); } catch { cell = null; }
                    if (cell == null)
                    {
                        continue;
                    }

                    int obj = 0;
                    try { obj = cell.obj; } catch { obj = 0; }

                    Zone? zone = null;
                    try { zone = cell.zone; } catch { zone = null; }

                    // Undo zone.icon contamination wherever a field-like zone sits,
                    // independent of what the tile currently shows (old builds did
                    // zone.icon = pin, and EloMap.Refresh rebuilds cell.obj from it).
                    if (zone != null && IsFieldLikeZone(zone) && zone.icon == icon)
                    {
                        try { zone.icon = 0; } catch { }
                    }

                    if (obj != icon)
                    {
                        continue;
                    }

                    if (active.Contains(RegionPinKey(gx, gy)))
                    {
                        continue;
                    }

                    // Real tinkerCamp / town tiles keep a non-field zone bound: leave them.
                    // A bound *field-like* zone carrying our pin icon is contamination
                    // (field's source icon is 0), so its cell is scrubbed too.
                    if (zone != null && !IsFieldLikeZone(zone))
                    {
                        continue;
                    }

                    try { cell.obj = 0; } catch { }
                    try
                    {
                        if (map.objmap != null)
                        {
                            map.objmap.Erase(gx, gy);
                        }
                    }
                    catch (System.Exception __e) { Plugin.LogDebug("region map repair erase: " + __e.Message); }
                    scrubbed++;
                }
            }

            if (scrubbed > 0)
            {
                try
                {
                    if (map.objmap != null)
                    {
                        map.objmap.UpdateMeshImmediate();
                    }
                }
                catch (System.Exception __e) { Plugin.LogDebug("region map repair mesh: " + __e.Message); }
                Plugin.LogWarn("region map: scrubbed " + scrubbed
                    + " stuck dispatch pin tile(s) left by an older build");
            }

            // Undo zone.icon rewrites on field-like zones (old code did zone.icon = pin).
            try
            {
                Region? region = EClass.world?.region;
                List<Spatial>? kids = null;
                try { kids = region?.children; } catch { kids = null; }
                if (kids != null)
                {
                    foreach (Spatial child in kids)
                    {
                        Zone? z = child as Zone;
                        if (z == null || !IsFieldLikeZone(z))
                        {
                            continue;
                        }

                        if (z.icon != icon)
                        {
                            continue;
                        }

                        // SourceZone "field" icon is 0 (verified in EA 23.356.1).
                        try { z.icon = 0; } catch { }
                    }
                }
            }
            catch (System.Exception __e) { Plugin.LogDebug("region map zone.icon repair: " + __e.Message); }
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("region map repair: " + ex.Message);
        }
    }

    internal static void ClearRegionMapMarkers()
    {
        try
        {
            if (_pinnedRegionMarks == null)
            {
                _pinnedRegionMarks = new List<long>();
            }

            EloMap? map = TryGetEloMap();
            if (map == null)
            {
                _pinnedRegionMarks.Clear();
                return;
            }

            foreach (long key in _pinnedRegionMarks.ToList())
            {
                int gx = (int)(key >> 32);
                int gy = unchecked((int)(key & 0xffffffffL));
                try
                {
                    TryRestoreRegionMarker(map, gx, gy);
                }
                catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
            }

            _pinnedRegionMarks.Clear();
            ScrubBrokenEloMapLights(map);
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
    }

    static List<long> _pinnedRegionMarks = new List<long>();

    // NOTE (2026-10-30): the old _pinnedRegionPrevObj dictionary is gone on purpose.
    // It was a *static in-memory* snapshot of the tile under each pin, used to restore
    // the pin on clear. That is wrong for two reasons:
    //   1. EloMap.Cell.obj is a SAVE field ([OnSerializing] packs obj*10 into _ints,
    //      [OnDeserialized] rebuilds cells from it) while Cell.zone is NOT saved. So any
    //      pin written into cell.obj lands in the save file and outlives the session.
    //   2. The snapshot itself never survives a reload / world regen, so after one
    //      reload the "previous tile" is unknown and the pin can never be undone —
    //      exactly the un-scrubbable tent a workshop player reported.
    // Pins are now display-only (objmap tiles). The truth for restoring a cell is
    // cell.obj itself, which is stable across our writes.

    /// <summary>Cached tinkerCamp SourceZone icon id; 0 means unresolved.</summary>
    static int _regionDispatchIcon;

    static EloMap? TryGetEloMap()
    {
        try
        {
            return EClass.world?.region?.elomap ?? EClass.scene?.elomap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Icon tile for region-dispatch pins = 旅行商人营地 (tinkerCamp) art.
    /// Runtime-resolved from SourceZone.pos[2]; fallback 334.
    /// </summary>
    static int GetRegionDispatchIcon()
    {
        if (_regionDispatchIcon > 0)
        {
            return _regionDispatchIcon;
        }

        try
        {
            // Elin SourceDataString.map.TryGetValue(id) returns the row (not Dictionary out-bool).
            SourceZone.Row? row = null;
            try
            {
                row = EClass.sources?.zones?.map?.TryGetValue("tinkerCamp");
            }
            catch
            {
                row = null;
            }

            if (row == null)
            {
                try
                {
                    if (EClass.sources?.zones?.map != null
                        && EClass.sources.zones.map.ContainsKey("tinkerCamp"))
                    {
                        row = EClass.sources.zones.map["tinkerCamp"];
                    }
                }
                catch
                {
                    row = null;
                }
            }

            if (row?.pos != null && row.pos.Length >= 3 && row.pos[2] > 0)
            {
                _regionDispatchIcon = row.pos[2];
                return _regionDispatchIcon;
            }
        }
        catch (System.Exception __e)
        {
            Plugin.LogDebug("region icon resolve: " + __e.Message);
        }

        _regionDispatchIcon = 334;
        return _regionDispatchIcon;
    }

    static long RegionPinKey(int gx, int gy)
    {
        return ((long)gx << 32) | (uint)gy;
    }

    static bool TryAddRegionMarker(EloMap map, int gx, int gy)
    {
        if (map == null)
        {
            return false;
        }

        int icon = GetRegionDispatchIcon();
        if (icon <= 0)
        {
            return false;
        }

        EloMap.Cell? cell = null;
        try
        {
            cell = map.GetCell(gx, gy);
        }
        catch
        {
            cell = null;
        }

        if (cell == null)
        {
            return false;
        }

        // Drop any legacy light pin on this tile (pre-icon builds).
        try
        {
            TryRemoveOurLight(map, gx, gy);
        }
        catch
        {
        }

        int currentObj = 0;
        try { currentObj = cell.obj; } catch { currentObj = 0; }

        Zone? zone = null;
        try
        {
            zone = cell.zone;
        }
        catch
        {
            zone = null;
        }

        // Do not stomp real town/dungeon icons. Field (and empty) tiles are fair game.
        if (!CanRestyleRegionCell(zone, currentObj, icon))
        {
            Plugin.LogDebug("region marker skip real zone @" + gx + "," + gy
                + " obj=" + currentObj
                + (zone != null ? (" id=" + (zone.id ?? "?")) : ""));
            return false;
        }

        // Display-only overlay. Deliberately never writes cell.obj or zone.icon:
        // Cell.obj is serialized into the save (EloMap packs obj*10 into _ints), so a
        // pin that lands there outlives the mission and cannot be scrubbed after a
        // reload — the reported "un-scrubbable tent". Writing zone.icon has the same
        // problem via EloMap.Refresh, which rebuilds cell.obj from zone.icon.
        try
        {
            ForceRegionIconTile(map, cell, gx, gy, icon);
            RememberRegionPin(gx, gy);
            return true;
        }
        catch (Exception ex)
        {
            ScrubBrokenEloMapLights(map);
            Plugin.LogDebug("region marker tile: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Repaint one pinned tile from <c>cell.obj</c>, which is the save-authoritative
    /// value. Nothing needs remembering: our pins never write cell.obj, so whatever
    /// sits there is exactly what vanilla would have drawn.
    /// </summary>
    static void TryRestoreRegionMarker(EloMap map, int gx, int gy)
    {
        if (map == null)
        {
            return;
        }

        // Always drop legacy light pins at this coordinate.
        try { TryRemoveOurLight(map, gx, gy); } catch { }

        EloMap.Cell? cell = null;
        try { cell = map.GetCell(gx, gy); } catch { cell = null; }
        if (cell == null)
        {
            return;
        }

        int real = 0;
        try { real = cell.obj; } catch { real = 0; }

        try
        {
            ForceRegionIconTile(map, cell, gx, gy, real);
        }
        catch (Exception ex)
        {
            Plugin.LogDebug("region marker restore tile: " + ex.Message);
        }
    }

    /// <summary>
    /// Draw (icon &gt; 0) or erase (icon == 0) one overworld tile WITHOUT touching
    /// <c>cell.obj</c>. Display-only by contract — see the note on _pinnedRegionMarks.
    /// <c>cell</c> is kept in the signature for call-site clarity but is never written.
    /// </summary>
    static void ForceRegionIconTile(EloMap map, EloMap.Cell cell, int gx, int gy, int icon)
    {
        if (map == null)
        {
            return;
        }

        try
        {
            if (map.objmap == null)
            {
                return;
            }

            if (icon <= 0)
            {
                map.objmap.Erase(gx, gy);
            }
            else
            {
                map.objmap.SetTile(gx, gy, icon);
            }

            map.objmap.UpdateMeshImmediate();
        }
        catch
        {
        }
    }

    static bool CanRestyleRegionCell(Zone? zone, int currentObj, int pinIcon)
    {
        if (currentObj == 0 || currentObj == pinIcon)
        {
            return true;
        }

        if (zone == null)
        {
            // Bare tile with some decorative obj — still allow pin so the outing is visible.
            return true;
        }

        return IsFieldLikeZone(zone);
    }

    static bool IsFieldLikeZone(Zone zone)
    {
        if (zone == null)
        {
            return false;
        }

        try
        {
            if (zone is Region)
            {
                return false;
            }
        }
        catch
        {
        }

        string id = "";
        try { id = zone.id ?? ""; } catch { id = ""; }

        if (string.Equals(id, "field", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Transient region outing fields sometimes keep a synthetic id like "region:plain".
        if (id.StartsWith("region:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    static void TryRemoveOurLight(EloMap map, int gx, int gy)
    {
        if (map == null)
        {
            return;
        }

        EloMapActor? actor = null;
        try
        {
            actor = map.actor;
        }
        catch
        {
            return;
        }

        if (actor?.lights == null)
        {
            return;
        }

        try
        {
            for (int i = actor.lights.Count - 1; i >= 0; i--)
            {
                EloMapLight light = actor.lights[i];
                if (light == null)
                {
                    try { actor.lights.RemoveAt(i); } catch { }
                    continue;
                }

                if (light.gx != gx || light.gy != gy)
                {
                    continue;
                }

                // Destroy only entries we own at this pin. If sr is already null, just drop the slot.
                try
                {
                    if (light.sr != null && light.sr.gameObject != null)
                    {
                        UnityEngine.Object.DestroyImmediate(light.sr.gameObject);
                    }
                }
                catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
                try
                {
                    actor.lights.RemoveAt(i);
                }
                catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
                // One pin per cell for our markers.
                break;
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
    }

    static void ScrubBrokenEloMapLights(EloMap? map)
    {
        if (map == null)
        {
            return;
        }

        EloMapActor? actor = null;
        try
        {
            actor = map.actor;
        }
        catch
        {
            return;
        }

        if (actor?.lights == null || actor.lights.Count == 0)
        {
            return;
        }

        try
        {
            for (int i = actor.lights.Count - 1; i >= 0; i--)
            {
                EloMapLight? light = null;
                try
                {
                    light = actor.lights[i];
                }
                catch
                {
                    continue;
                }

                bool broken = light == null;
                if (!broken && light != null)
                {
                    try
                    {
                        broken = light.sr == null;
                    }
                    catch
                    {
                        broken = true;
                    }
                }

                if (!broken)
                {
                    continue;
                }

                try
                {
                    actor.lights.RemoveAt(i);
                }
                catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
            }
        }
        catch (System.Exception __e) { Plugin.LogDebug("DungeonDispatchManager.cs silent catch: " + __e.Message); }
    }

    static bool IsOurRegionPin(int gx, int gy)
    {
        if (_pinnedRegionMarks == null)
        {
            return false;
        }

        long key = RegionPinKey(gx, gy);
        return _pinnedRegionMarks.Contains(key);
    }

    static void RememberRegionPin(int gx, int gy)
    {
        if (_pinnedRegionMarks == null)
        {
            _pinnedRegionMarks = new List<long>();
        }

        long key = RegionPinKey(gx, gy);
        if (!_pinnedRegionMarks.Contains(key))
        {
            _pinnedRegionMarks.Add(key);
        }
    }
}
