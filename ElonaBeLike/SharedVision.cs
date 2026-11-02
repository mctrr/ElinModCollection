using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ElonaBeLike;

/// <summary>
/// 队友共享视野：PC 点了专长「视域共享」之后，队友看到的区域（含站在那里的敌人）PC 也能看到。
/// 专长 3 级：1~2 级每级 2 格，3 级给该队友的全部视野；没点专长就完全不共享，视野回到原版。
///
/// 三件事凑成「看得见」：
///
/// 1. 让队友有视野（也就是有光照）。原版队友在白天室外 GetLightRadius() 直接返回 0
///    （Card.cs:6944），连 Fov 都不会建，身周一片黑。
///    这里 postfix Card.GetLightRadius，队伍成员至少返回共享半径。
///    光照的加减全部交给原版 Fov 自己管（Perform 加、下次 Perform 的 ClearVisible 减），
///    我们一行都不用记账，也就不会残留或算错。
///    GetLightRadius 的调用点只有 Card.HasLight（全项目无人调用）、Card.CalculateFOV
///    和 Chara.held.GetLightRadius（接收者是手持物，不是角色），所以影响面可控。
///
/// 2. 揭示迷雾。原版 Cell.isSeen 只有带 isPC 标记的 Fov 才会写（Fov.cs:209-218），
///    队友的 Fov 是 isPC=false，只给光不揭雾。所以拿队友 Fov 算出的可见格
///    （Fov.ListPoints，就是它 lastPoints 的键）自己调 Map.SetSeen。
///
/// 3. 让敌人画出来。原版画角色的判定是 BaseTileMap.cs:1686 的
///    `!EMono.player.CanSee(chara)` → continue，而 Player.CanSee(Chara)（Player.cs:2568）
///    还要求 cell.pcSync —— 那是 PC 自己 Fov 的产物，队友的 Fov 不会设它
///    （设了就会和 PC 的 Fov 互相洗掉）。所以再 postfix Player.CanSee：
///    站在队友视野格里的角色，PC 就算看得见。
///
/// 为什么不让队友的 Fov 直接带 isPC：SetVisible 会给格子加 light 并置 pcSync = true，
/// 而 ClearVisible 只在 isPC 时才把 pcSync 清 false —— 两个 Fov 共用一个 bool 字段，
/// 借用它必然要么给 PC 的视野叠一层额外光照，要么把 PC 自己点亮格子的 pcSync 洗掉。
/// 所以队友的 Fov 保持 isPC=false，pcSync 一律不碰。
/// </summary>
[HarmonyPatch]
internal static class SharedVision
{
    /// <summary>专长「视域共享」的 id，对应 package/LangMod/*/Source.xlsx 里 Element 表的那一行。</summary>
    private const int FeatId = 1919901;

    /// <summary>专长满级：到这一级就给该队友的全部视野。</summary>
    private const int MaxLevel = 3;

    private static readonly Dictionary<int, Point> LastPos = new Dictionary<int, Point>();
    private static readonly Dictionary<int, int> LastRadius = new Dictionary<int, int>();

    /// <summary>每个队友上一回合看得见的格子，key = Point.index。</summary>
    private static readonly Dictionary<int, HashSet<int>> AllyCells = new Dictionary<int, HashSet<int>>();

    /// <summary>全体队友可见格子的并集，供 <see cref="Sees"/> 查询。</summary>
    private static readonly HashSet<int> SeesCells = new HashSet<int>();

    private static bool seesDirty = true;

    /// <summary>换区后清缓存（新地图坐标全变）。</summary>
    public static void Reset()
    {
        LastPos.Clear();
        LastRadius.Clear();
        AllyCells.Clear();
        seesDirty = true;
    }

    /// <summary>专长等级换算成共享半径：0 = 不共享，1~2 级每级 2 格，满级给该队友的全部视野。</summary>
    private static int RadiusFor(Chara ally)
    {
        if (!(EMono.pc.elements.GetElement(FeatId) is Feat feat) || feat.Value <= 0)
        {
            return 0;
        }

        return feat.Value >= MaxLevel ? ally.GetSightRadius() : feat.Value * 2;
    }

    /// <summary>该角色是否站在任一队友的视野里。</summary>
    public static bool Sees(Chara chara)
    {
        if (seesDirty)
        {
            Rebuild();
        }

        if (SeesCells.Count == 0)
        {
            return false;
        }

        return SeesCells.Contains(chara.pos.index);
    }

    private static void Rebuild()
    {
        seesDirty = false;
        SeesCells.Clear();
        foreach (Chara ally in CommandRegistry.PartyAllies())
        {
            if (ally.IsAliveInCurrentZone && AllyCells.TryGetValue(ally.uid, out HashSet<int> cells))
            {
                SeesCells.UnionWith(cells);
            }
        }
    }

    /// <summary>队友也要有视野，否则白天室外原版直接返回 0，身周是黑的。</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Card), nameof(Card.GetLightRadius))]
    private static void GetLightRadiusPostfix(Card __instance, ref int __result)
    {
        if (!__instance.isChara || __instance.IsPC || !EMono._zone.isStarted)
        {
            return;
        }

        Chara ally = __instance.Chara;
        if (CommandRegistry.PartyAllies().Contains(ally))
        {
            __result = Mathf.Max(__result, RadiusFor(ally));
        }
    }

    /// <summary>队友看得见的角色，PC 也就看得见（原版靠这个决定画不画，BaseTileMap.cs:1686）。</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.CanSee), new System.Type[] { typeof(Chara) })]
    private static void CanSeePostfix(Chara c, ref bool __result)
    {
        if (__result || c.IsPCParty || EMono.pc.isBlind)
        {
            return;
        }

        __result = Sees(c);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.Tick))]
    private static void AllyTurnPostfix(Chara __instance)
    {
        if (__instance.IsPC || !__instance.IsAliveInCurrentZone || !EMono._zone.isStarted)
        {
            return;
        }

        if (!CommandRegistry.PartyAllies().Contains(__instance))
        {
            return;
        }

        int radius = RadiusFor(__instance);
        Point pos = __instance.pos;
        if (LastRadius.TryGetValue(__instance.uid, out int lastRadius) && lastRadius == radius
            && LastPos.TryGetValue(__instance.uid, out Point lastPos) && lastPos.Equals(pos))
        {
            return;
        }

        LastPos[__instance.uid] = pos.Copy();
        LastRadius[__instance.uid] = radius;

        if (radius == 0)
        {
            // 没点专长（或洗掉了）：恢复原版视野，顺便把之前共享视野点亮的光收回去。
            if (AllyCells.Remove(__instance.uid))
            {
                seesDirty = true;
            }

            __instance.RecalculateFOV();
            return;
        }

        // 原版只在 PC 周围 RefreshFOV，远处的队友不会自己重算，所以这里主动算一次。
        __instance.CalculateFOV();
        if (__instance.fov == null)
        {
            return;
        }

        AllyCells[__instance.uid] = Reveal(__instance, __instance.fov);
        seesDirty = true;
    }

    /// <summary>把队友 Fov 看到的格子揭出来，并返回这些格子的 key。</summary>
    private static HashSet<int> Reveal(Chara ally, Fov fov)
    {
        HashSet<int> cells = new HashSet<int>();
        Room room = ally.pos.cell.room;
        foreach (Point p in fov.ListPoints())
        {
            cells.Add(p.index);
            Cell cell = p.cell;
            if (cell.isSeen || (cell.HasWall && !EMono._zone.UseFog && cell.room != room && !cell.hasDoor))
            {
                continue;
            }

            EMono._map.SetSeen(p.x, p.z);
        }

        return cells;
    }
}
