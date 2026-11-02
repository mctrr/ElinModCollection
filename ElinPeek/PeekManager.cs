using UnityEngine;

namespace ElinPeek;

/// <summary>
/// CDDA 式“窥视”核心状态机：
/// 进入窥视 → 冻结世界（gameSpeed=0）→ 把 PC 的视野原点临时重定向到拐角后的格子。
/// 敌人 AI 只看自己能否看见 PC（墙挡视线），窥视只改“玩家看到什么”，因此不会被发现。
/// </summary>
public static class PeekManager
{
    public static bool Active { get; private set; }

    private static int _prevSpeedIndex = 1;

    /// <summary>进入后的宽限帧：避免“从右键菜单点窥视”时菜单关闭的那一帧被当成异常自动退出。</summary>
    private static int _graceFrames;

    /// <summary>窥视时 PC 贴图的额外渲染偏移（屏幕空间），进出都做平滑过渡。</summary>
    public static Vector3 RenderOffset { get; private set; }

    /// <summary>偏移是否仍在生效：进入后为 true，退出后一直保持到偏移回落到 0。</summary>
    private static bool _offsetEngaged;

    /// <summary>位移占一格的比例。0.5 = 站在两格正中间；调小就是“稍微探出一点”。</summary>
    public const float OffsetRatio = 0.5f;

    /// <summary>位移移动速度：每秒多少格（屏幕单位用 tileAlign 换算）。</summary>
    private const float OffsetSpeedTilesPerSec = 12f;

    private static readonly (int, int)[] Dirs =
    {
        (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)
    };

    public static void Enter()
    {
        if (Active)
        {
            return;
        }
        if (!EClass.core.IsGameStarted || EClass.pc == null || EClass.pc.isDead)
        {
            return;
        }
        if (EClass.scene == null || EClass.scene.actionMode is not AM_Adv)
        {
            return;
        }
        Active = true;
        _graceFrames = 3;
        // 保存原速度并暂停世界：敌人回合计时 gameDelta = 0，无法行动，也就“不会发现你”。
        _prevSpeedIndex = EClass.game.gameSpeedIndex;
        EClass.game.gameSpeedIndex = 0;
        Refresh();
        Msg.Say(PeekText.T("peek.hint.enter"));
        SE.ClickOk();
    }

    public static void Exit()
    {
        if (!Active)
        {
            return;
        }
        Active = false;
        _graceFrames = 0;
        EClass.game.gameSpeedIndex = _prevSpeedIndex;
        Refresh();
        SE.CancelAction();
    }

    /// <summary>每帧调用，递减宽限帧。</summary>
    public static void TickGrace()
    {
        if (_graceFrames > 0)
        {
            _graceFrames--;
        }
    }

    /// <summary>宽限期是否已过（可以安全自动退出了）。</summary>
    public static bool GraceElapsed => _graceFrames <= 0;

    /// <summary>是否有真实图层打开（右键菜单不算）。</summary>
    public static bool HasLayerOpen => EClass.ui != null && EClass.ui.layers != null && EClass.ui.layers.Count > 0;

    /// <summary>立即按当前鼠标位置重新计算一次视野（进入/退出/每帧跟随鼠标）。</summary>
    public static void Refresh()
    {
        if (!EClass.core.IsGameStarted || EClass.pc == null || EClass.pc.isDead)
        {
            return;
        }
        EClass.pc.RecalculateFOV();
    }

    /// <summary>每帧推进“探出身子”的渲染偏移（用真实时间，不受窥视冻结影响）。</summary>
    public static void TickRenderOffset()
    {
        if (EClass.core == null || !EClass.core.IsGameStarted || EClass.pc == null || EClass.screen == null)
        {
            RenderOffset = Vector3.zero;
            _offsetEngaged = false;
            return;
        }
        Vector3 target = Vector3.zero;
        if (Active && TryGetOrigin(out int ox, out int oz))
        {
            Point pc = EClass.pc.pos;
            int dx = ox - pc.x;
            int dz = oz - pc.z;
            if (dx != 0 || dz != 0)
            {
                // Point.Position() 的屏幕映射：x = (X+Z)*tileAlign.x，y = (Z-X)*tileAlign.y
                Vector2 align = EClass.screen.tileAlign;
                target.x = (dx + dz) * align.x * OffsetRatio;
                target.y = (dz - dx) * align.y * OffsetRatio;
            }
        }
        if (!_offsetEngaged && target == Vector3.zero)
        {
            return;
        }
        _offsetEngaged = true;
        RenderOffset = Vector3.MoveTowards(RenderOffset, target, OffsetSpeed * Time.deltaTime);
        if (target == Vector3.zero && RenderOffset == Vector3.zero)
        {
            _offsetEngaged = false;
        }
    }

    /// <summary>该角色的渲染位置是否要被窥视偏移接管（只对 PC 生效）。</summary>
    public static bool IsOffsetTarget(Chara? c) => _offsetEngaged && c != null && c.IsPC;

    /// <summary>偏移速度（屏幕单位/秒）。tileAlign.x 即“走一格”的屏幕位移。</summary>
    public static float OffsetSpeed
    {
        get
        {
            Vector2 align = EClass.screen == null ? Vector2.one : EClass.screen.tileAlign;
            float tile = align.x > 0.0001f ? align.x : 1f;
            return tile * OffsetSpeedTilesPerSec;
        }
    }

    /// <summary>
    /// 根据鼠标相对 PC 的方向，计算窥视原点（墙后的拐角格子）。
    /// 返回 false 表示不重定向（鼠标在 PC 身上/不可窥视 → 保持正常视野）。
    /// </summary>
    public static bool TryGetOrigin(out int x, out int z)
    {
        x = 0;
        z = 0;
        if (!Active || !EClass.core.IsGameStarted || EClass.pc == null || EClass.pc.isDead)
        {
            return false;
        }
        if (EClass.scene == null || EClass.scene.mouseTarget == null)
        {
            return false;
        }
        Point pc = EClass.pc.pos;
        x = pc.x;
        z = pc.z;
        Point m = EClass.scene.mouseTarget.pos;
        if (m == null || !m.IsValid || !m.IsInBounds)
        {
            return false;
        }
        int dx = Mathf.Clamp(m.x - pc.x, -1, 1);
        int dz = Mathf.Clamp(m.z - pc.z, -1, 1);
        if (dx == 0 && dz == 0)
        {
            return false; // 鼠标在 PC 自己身上 → 看正常视野
        }
        int idx = IndexOf(dx, dz);
        // 1) 正前方格子（能走/能看 → 相当于探出一步）
        if (TryCandidate(pc.x + dx, pc.z + dz, out int ox, out int oz))
        {
            x = ox;
            z = oz;
            return true;
        }
        // 2) 正前方是墙 → 窥视左右两个斜角（CDDA 拐角窥视）
        (int, int) d1 = Dirs[(idx + 1) % 8];
        (int, int) d2 = Dirs[(idx + 7) % 8];
        if (TryCandidate(pc.x + d1.Item1, pc.z + d1.Item2, out ox, out oz))
        {
            x = ox;
            z = oz;
            return true;
        }
        if (TryCandidate(pc.x + d2.Item1, pc.z + d2.Item2, out ox, out oz))
        {
            x = ox;
            z = oz;
            return true;
        }
        return false;
    }

    private static int IndexOf(int dx, int dz)
    {
        for (int i = 0; i < Dirs.Length; i++)
        {
            if (Dirs[i].Item1 == dx && Dirs[i].Item2 == dz)
            {
                return i;
            }
        }
        return 0;
    }

    private static bool TryCandidate(int tx, int tz, out int x, out int z)
    {
        x = tx;
        z = tz;
        if (tx < 0 || tz < 0 || tx >= EClass._map.Size || tz >= EClass._map.Size)
        {
            return false;
        }
        Cell c = EClass._map.cells[tx, tz];
        if (c == null || c.blockSight || c.outOfBounds)
        {
            return false;
        }
        return true;
    }
}
