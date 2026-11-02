using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ElonaBeLike;

/// <summary>
/// 对树「踢」。
///
/// 两个入口：
/// - 中键（默认绑定「全部动作」→ AM_Adv.ShowAllAction → planAll）：
///   postfix ActPlan._Update，只在 input == AllAction 的那份计划里追加「踢」，
///   这样它和原版的伐採 / 採集并排出现在同一个菜单里，不会挤掉原版选项。
/// - R 键：鼠标指着树时直接打开同一个菜单（AM_Adv._OnUpdateInput 前缀）。
///   _OnUpdateInput 只在 EClass.pc.currentZone.IsActiveZone 时被调用，正好排除了世界地图。
///
/// 结算（每棵树只生效一次，用 Cell.isHarvested 记账 —— 原版把它写进 Map 的存档位里）：
/// - 带 harvestTile 的树（果树 / 可采集的树）：只掉成熟果实，果实没熟就只播踢的动作，什么也不给。
///   这样不会把果树的 isHarvested 提前占掉，果实该熟还是能熟。
/// - 普通树：40% 叶子 / 40% 树枝 / 20% 波球。波球就是原版 GrowSystemTree.OnHitFail
///   里那只 putty，连台词都沿用 mobFromTree。
///
/// 产出物不走 CC.PickOrDrop（那样背包有空位就直接进包，看不见掉落）：
/// 用 EClass._zone.AddCard 把东西丢在**树周围 8 格里随机一个空格**，并置 ignoreAutoPick，
/// 免得玩家往前走一步就自动收走 —— 想捡要自己点。周围全被挡住时才落回玩家脚下。
/// isHarvested 一律等产出跑完再置位 —— 中途任何一步出错都只损失这一次，不会把树永久锁死。
/// </summary>
[HarmonyPatch]
internal static class TreeKick
{
    /// <summary>能踢的树：成熟的原版树（幼苗踢了没东西）。</summary>
    public static bool IsKickable(Point p)
    {
        return p.IsValid && p.growth is GrowSystemTree tree && tree.IsMature;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(ActPlan), nameof(ActPlan._Update))]
    private static void AddKickToAllActionMenu(ActPlan __instance)
    {
        if (__instance.input != ActInput.AllAction || !IsKickable(__instance.pos))
        {
            return;
        }

        __instance.list.Add(new ActPlan.Item
        {
            act = new ActTreeKick(),
            pos = __instance.pos.Copy(),
        });
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(AM_Adv), nameof(AM_Adv._OnUpdateInput))]
    private static bool RKeyOpensMenu(AM_Adv __instance)
    {
        if (!Input.GetKeyDown(KeyCode.R) || EClass.ui.IsActive || EClass.ui.isPointerOverUI)
        {
            return true;
        }

        if (!IsKickable(EClass.scene.mouseTarget.pos))
        {
            return true;
        }

        __instance.ShowAllAction();
        return false;
    }
}

internal class ActTreeKick : Act
{
    private const int LeafRoll = 40;
    private const int BranchRoll = 80;

    public override CursorInfo CursorIcon => CursorSystem.Kick;

    public override global::TargetType TargetType => global::TargetType.Ground;

    /// <summary>原版 GetText 的自定义 Act 会拼出「类名/源/0」，这里直接给中文字面量。</summary>
    public override string GetText(string str = "")
    {
        return "踢";
    }

    public override bool CanPerform()
    {
        return Act.TP.Distance(Act.CC.pos) <= 1 && TreeKick.IsKickable(Act.TP);
    }

    public override bool Perform()
    {
        Point pos = Act.TP;
        Cell cell = pos.cell;
        GrowSystem growth = pos.growth;

        CC.PlaySound("kick");
        CC.LookAt(pos);
        CC.renderer.PlayAnime(AnimeID.Attack, pos);
        pos.Animate(AnimeID.HitObj, animeBlock: true);

        if (growth is not GrowSystemTree tree)
        {
            return true;
        }

        // 可采集的树（果树）：只给成熟果实，不参与 40/40/20。
        if (tree.harvestTile != 0)
        {
            if (tree.CanHarvest())
            {
                tree.Harvest(CC);
            }

            return true;
        }

        if (cell.isHarvested)
        {
            Plugin.LogVerbose($"[Kick] ({pos.x},{pos.z}) 这棵树已经踢过了");
            return true;
        }

        int roll = EClass.rnd(100);
        Thing? drop = null;
        if (roll < LeafRoll)
        {
            drop = ThingGen.Create("leaf").SetNum(1 + EClass.rnd(2));
        }
        else if (roll < BranchRoll)
        {
            drop = ThingGen.Create("branch", cell.matObj_fixed.id).SetNum(1 + EClass.rnd(2));
        }

        if (drop != null)
        {
            Point dropPos = PickDropPoint(pos);
            EClass._zone.AddCard(drop, dropPos);
            // 掉在脚下也不会被踩一脚就自动收走；手动拾取不受影响（它会把这个标志清回 false）。
            drop.ignoreAutoPick = true;
            drop.PlaySoundDrop();
            Plugin.LogVerbose($"[Kick] ({pos.x},{pos.z}) 掉落 -> ({dropPos.x},{dropPos.z})");
        }
        else
        {
            SpawnPutty(pos);
        }

        // 产出跑完才记账：上面任何一步抛异常都只丢这一次，不会把整棵树变成永久哑巴。
        cell.isHarvested = true;
        Plugin.LogVerbose($"[Kick] ({pos.x},{pos.z}) roll={roll} -> {(drop != null ? "item" : "putty")}");
        return true;
    }

    /// <summary>树周围 8 格里随机挑一个能放东西的空格；挑不到才退回玩家脚下。</summary>
    private Point PickDropPoint(Point tree)
    {
        List<Point> candidates = new List<Point>();
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0)
                {
                    continue;
                }

                Point p = new Point(tree.x + dx, tree.z + dz);
                if (!p.IsValid || p.IsBlocked)
                {
                    continue;
                }

                // 尽量不压在玩家自己脚下，否则看着还是「掉在我这儿」。
                if (p.x == CC.pos.x && p.z == CC.pos.z)
                {
                    continue;
                }

                candidates.Add(p);
            }
        }

        return candidates.Count > 0 ? candidates[EClass.rnd(candidates.Count)] : CC.pos;
    }

    /// <summary>照抄 GrowSystemTree.OnHitFail：落一只普通波球到最近的空格。</summary>
    private void SpawnPutty(Point pos)
    {
        Chara putty = CharaGen.Create("putty");
        EClass._zone.AddCard(putty, pos.GetNearestPoint(allowBlock: false, allowChara: false));
        putty.renderer.PlayAnime(AnimeID.Fall, force: true);
        CC.Say("mobFromTree", putty);
    }
}
