using ACS.API;
using ACS.Frame;
using HarmonyLib;
using UnityEngine;

namespace ACS.Patches;

/// <summary>
/// 冷启动/读档后强制 ListSkins + 重绑 PC skin（ACS 或静态）。
/// </summary>
[HarmonyPatch]
internal class GameStartPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Game), nameof(Game.Load))]
    internal static void OnGameLoad()
    {
        PcSkinBinder.Schedule("Game.Load", forceListSkins: true);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Scene), nameof(Scene.Init))]
    internal static void OnSceneInit(Scene.Mode newMode)
    {
        // 切图 / 新游戏：所有角色渲染器重建，per-actor 帧推进/朝向/战斗贴图缓存作废。
        // 不清会按 GetInstanceID 越攒越多（NPC 进出场后字典只增不删）。
        PccRenderPatch.ClearRuntimeCaches();
        RenderFramePatch.ClearRuntimeCaches();
        AcsSpritePresenter.ClearCenterAnchoredCache();
        // 状态解析器里按 chara.uid 缓存的武器键/施法·近战·远程朝向也随角色重建作废；
        // 顺带清掉 PC 残留的战斗/攻击窗口（切图不该带着旧战斗状态进新场景）。
        PlayState.ClearHostileScanCache();

        if (newMode == Scene.Mode.StartGame) {
            PcSkinBinder.Schedule($"Scene.Init({newMode})", forceListSkins: !PcSkinBinder.HasCachedSkins());
        }
        else if (newMode == Scene.Mode.Zone) {
            // Zone travel must rebind only. ListSkins rebuilds every PCR clip from scratch.
            PcSkinBinder.Schedule($"Scene.Init({newMode})", forceListSkins: false);
        }

        // 任何场景切换都重新预热：解**新场景里真实存在的 NPC** 会用到的高清动态贴图，
        // 后台并行（inflate + 反 filter 占 95% 耗时），主线程只建贴图。
        GreetPrewarm.OnSceneChanged();

        // 进 Zone 时（StartGame 内部就是 Init(Zone)）当场把这一批预热**做完再返回**：
        // 这点时间算进读条，进图后第一次遇到人就不会卡。地图和角色在 Scene.Init 里
        // 已经就绪（原版 Init 自己就在遍历 _map.things），所以这里拿到的是完整名单。
        if (newMode == Scene.Mode.Zone) {
            GreetPrewarm.WarmBlocking();
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Scene), nameof(Scene.OnUpdate))]
    internal static void OnSceneUpdate()
    {
        PcSkinBinder.TickRetry();
        PortraitAnimator.Tick();
    }
}
