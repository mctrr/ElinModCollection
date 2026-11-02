using HarmonyLib;
using UnityEngine;

namespace ElinPeek;

/// <summary>
/// 窥视模式 = 临时把 PC 视野原点挪到拐角格子。
/// 只需在 Fov.Perform 入口把 (x,z) 换掉，整条渲染/可见性/小地图管线都会跟随。
/// </summary>
[HarmonyPatch(typeof(Fov), nameof(Fov.Perform))]
public static class PatchFovPerform
{
    [HarmonyPrefix]
    private static void Prefix(Fov __instance, ref int _x, ref int _z)
    {
        if (!__instance.isPC)
        {
            return; // 只重定向 PC 自己的视野
        }
        if (PeekManager.HasLayerOpen)
        {
            // 窥视期间打开了真实菜单/层 → 交给 Scene.OnUpdate 的安全网退出；这一帧不重定向。
            return;
        }
        int ox;
        int oz;
        if (PeekManager.TryGetOrigin(out ox, out oz))
        {
            _x = ox;
            _z = oz;
        }
    }
}

/// <summary>
/// 窥视时把 PC 的渲染目标往窥视方向平移半格，做出“站在两格中间探出身子”的效果。
/// 只动 destPos / renderer.position，不动 pc.pos —— 逻辑格子、FOV 原点、
/// 相机焦点（BaseGameScreen.Focus 只认格子）都不受影响。
/// 用真实时间的 MoveTowards 推进：窥视期间 gameDelta=0，游戏自带的移动插值会冻住。
/// </summary>
[HarmonyPatch(typeof(CharaRenderer), nameof(CharaRenderer.UpdatePosition))]
public static class PatchCharaRendererUpdatePosition
{
    [HarmonyPrefix]
    private static void Prefix(CharaRenderer __instance, ref Vector3 destPos)
    {
        if (!PeekManager.IsOffsetTarget(__instance.owner))
        {
            return;
        }
        destPos += PeekManager.RenderOffset;
    }

    [HarmonyPostfix]
    private static void Postfix(CharaRenderer __instance, ref Vector3 destPos, RenderParam p)
    {
        if (!PeekManager.IsOffsetTarget(__instance.owner))
        {
            return;
        }
        Vector3 from = __instance.position;
        Vector3 to = Vector3.MoveTowards(from, destPos, PeekManager.OffsetSpeed * Time.deltaTime);
        if (to == from)
        {
            return;
        }
        __instance.position = to;
        p.x = to.x;
        p.y = to.y;
    }
}

/// <summary>
/// Esc 的系统菜单在 AM_BaseGameMode.OnUpdateInput 里、_OnUpdateInput 之前弹出；
/// 窥视中按 Esc 应只退出窥视，不弹系统菜单。
/// </summary>
[HarmonyPatch(typeof(AM_BaseGameMode), nameof(AM_BaseGameMode.OnUpdateInput))]
public static class PatchBaseGameModeInput
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        if (!PeekManager.Active)
        {
            return true;
        }
        if (EInput.isCancel)
        {
            PeekManager.Exit();
            EInput.Consume();
            return false;
        }
        return true;
    }
}

/// <summary>
/// 窥视中接管冒险模式的输入：吃掉移动/点击，右键/左键退出；退出后恢复原输入。
/// </summary>
[HarmonyPatch(typeof(AM_Adv), nameof(AM_Adv._OnUpdateInput))]
public static class PatchAdvInput
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        if (!PeekManager.Active)
        {
            return true;
        }
        if (EInput.rightMouse.down || EInput.leftMouse.down)
        {
            PeekManager.Exit();
            EInput.Consume();
        }
        return false; // 窥视期间不处理任何普通冒险输入
    }

    [HarmonyPostfix]
    private static void Postfix()
    {
        if (PeekManager.Active)
        {
            PeekManager.Refresh(); // 每帧重算视野，让窥视随鼠标实时移动
        }
    }
}

/// <summary>
/// 安全网：任何异常情况（切模式/开真实图层/死亡/回标题）都会自动结束窥视并解冻世界。
/// </summary>
[HarmonyPatch(typeof(Scene), nameof(Scene.OnUpdate))]
public static class PatchSceneOnUpdate
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        PeekManager.TickRenderOffset(); // 推进“探出身子”的半格偏移（退出后也会平滑收回）
        if (!PeekManager.Active)
        {
            return;
        }
        PeekManager.TickGrace();
        bool invalid = EClass.core == null
            || !EClass.core.IsGameStarted
            || EClass.pc == null
            || EClass.pc.isDead
            || EClass.scene == null
            || EClass.scene.actionMode is not AM_Adv;
        if (invalid)
        {
            PeekManager.Exit();
            return;
        }
        // 宽限期过后，若打开了真实图层（背包/能力/菜单等）→ 自动退出，避免世界一直冻结
        if (PeekManager.GraceElapsed && PeekManager.HasLayerOpen)
        {
            PeekManager.Exit();
        }
    }
}

/// <summary>
/// 在 PC 自己的动作按钮里加一个“窥视”。
/// 只加在“目标是 PC 自己”的计划里（不干扰相邻格子原本的开锁/开门等操作）。
/// </summary>
[HarmonyPatch(typeof(ActPlan), nameof(ActPlan.Update))]
public static class PatchActPlanUpdate
{
    [HarmonyPostfix]
    private static void Postfix(ActPlan __instance)
    {
        if (PeekManager.Active)
        {
            return;
        }
        if (EClass.core == null || !EClass.core.IsGameStarted)
        {
            return;
        }
        if (__instance.input != ActInput.LeftMouse && __instance.input != ActInput.AllAction)
        {
            return;
        }
        if (__instance.dist != 0) // 只作用于 PC 所在格
        {
            return;
        }
        foreach (ActPlan.Item item in __instance.list)
        {
            if (item.act is ActPeek)
            {
                return;
            }
        }
        __instance.TrySetAct(new ActPeek(), EClass.pc);
    }
}
