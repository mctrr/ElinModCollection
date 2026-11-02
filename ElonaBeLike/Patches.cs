using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ElonaBeLike;

[HarmonyPatch]
internal static class MinimapPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(WidgetMinimap), nameof(WidgetMinimap.OnPointerDown))]
    private static bool OnPointerDownPrefix(WidgetMinimap __instance, PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Right)
        {
            return true;
        }

        if (!EMono.pc.HasNoGoal || EMono.game.activeZone.IsRegion || EMono.ui.IsActive)
        {
            SE.BeepSmall();
            return false;
        }

        // 原版对左键走路要求 !outOfBounds && isSeen（未探索的格子不给走）。
        // 这里的右键是派队友，不是玩家自己走 —— 允许点未探索的区域，队友才能去探图，
        // 探出来的区域再由共享视野反馈给 PC。
        if (!TryGetClickedPoint(__instance, e, out Point dest) || dest.cell.outOfBounds)
        {
            SE.BeepSmall();
            return false;
        }

        EInput.rightMouse.Consume();
        CommandMenu.Show(dest);
        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(WidgetMinimap), nameof(WidgetMinimap.RefreshMarkers))]
    private static void RefreshMarkersPostfix(WidgetMinimap __instance)
    {
        if (!__instance.gameObject.activeInHierarchy)
        {
            return;
        }

        if (Plugin.ShowAlliesEnabled)
        {
            foreach (Chara chara in CommandRegistry.PartyAllies())
            {
                if (chara.IsAliveInCurrentZone)
                {
                    __instance.EmitParticle(chara, __instance.psAlly, __instance.colorAlly);
                }
            }
        }

        if (!Plugin.ShowEnemiesEnabled || EMono._zone.ShowEnemyOnMinimap)
        {
            return;
        }

        foreach (Chara chara in EMono._map.charas)
        {
            if (chara.IsHostile())
            {
                __instance.EmitParticle(chara, __instance.psEnemy, __instance.colorEnemy);
            }
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(WidgetMinimap), nameof(WidgetMinimap.OnMoveZone))]
    private static void OnMoveZonePrefix()
    {
        CommandRegistry.RecallAll();
        SharedVision.Reset();
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), nameof(Player.MoveZone))]
    private static void PlayerMoveZonePrefix()
    {
        CommandRegistry.RecallAll();
        SharedVision.Reset();
    }

    private static bool TryGetClickedPoint(WidgetMinimap widget, PointerEventData e, out Point dest)
    {
        dest = Point.Invalid;
        Vector2 size = widget.rectMap.rect.size;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(widget.rectMap, e.position, e.pressEventCamera, out Vector2 localPoint))
        {
            return false;
        }

        if (localPoint.x < 0f)
        {
            localPoint.x = 0f;
        }
        else if (localPoint.x >= size.x)
        {
            localPoint.x = size.x;
        }

        if (localPoint.y < 0f)
        {
            localPoint.y = 0f;
        }
        else if (localPoint.y >= size.y)
        {
            localPoint.y = size.y;
        }

        dest = new Point(
            (int)((float)widget.Size * localPoint.x / size.x),
            (int)((float)widget.Size * localPoint.y / size.y));
        dest.Clamp();
        if (widget.preview.limitBounds)
        {
            dest.x += EMono._map.bounds.x - widget.preview.offsetX;
            dest.z += EMono._map.bounds.z - widget.preview.offsetZ;
        }

        return dest.IsValid;
    }
}

[HarmonyPatch]
internal static class CommandAiPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(AI_Idle), nameof(AI_Idle.Run))]
    private static bool IdleRunPrefix(AI_Idle __instance, ref IEnumerable<AIAct.Status> __result)
    {
        if (!CommandRegistry.IsCommanded(__instance.owner))
        {
            return true;
        }

        __result = StayPut();
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.ChooseNewGoal))]
    private static bool ChooseNewGoalPrefix(Chara __instance)
    {
        Order? order = CommandRegistry.Get(__instance);
        if (order == null)
        {
            return true;
        }

        if (!__instance.IsAliveInCurrentZone || !order.IsValid)
        {
            CommandRegistry.Remove(__instance);
            return true;
        }

        if (order.Kind == OrderKind.Patrol)
        {
            __instance.SetAI(new AI_Patrol(order.A, order.B));
        }
        else
        {
            __instance.SetAI(new AI_HoldPosition(order.B));
        }

        return false;
    }

    private static IEnumerable<AIAct.Status> StayPut()
    {
        yield return AIAct.Status.Running;
    }
}
