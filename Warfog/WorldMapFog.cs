using System;
using System.Collections.Generic;
using System.Reflection;
using CreativeSpore.SuperTilemapEditor;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Warfog;

/// <summary>
/// 世界地图（北提里斯大陆图）的战争迷雾。
///
/// 原版大陆图没有任何迷雾：EloMap.Init 把每个 zone 的图标画到 objmap 上，整张图一次看全。
/// 这里给它盖一层遮罩，只揭开 PC 周围 <see cref="RevealRadius"/> 格；走过的格子永久保留。
///
/// 三档明暗照抄原版场景的做法（BaseTileMap 里那套）：
/// - 当前看得见：不压暗 —— 对应 `cell.pcSync` 为真时走 `light`（BaseTileMap.cs:974 前半）。
/// - 见过但当前看不见：压到 <see cref="MemoryAlpha"/> —— 对应 `cell.light / 3 * 2`，即原版
///   「记忆中的地形」亮度降到 2/3。原版是给 tile 逐格染色，这里只能用雾的 alpha 近似。
/// - 没见过：全黑 —— 对应 `isSeen == false && UseFog` 时那块 `rendererFogFloorSolid` 的纯色雾 tile
///   （BaseTileMap.cs:1229）。所以原版的「未见」本来也是纯色块，不是保留地形。
///
/// 视野形状照抄 Fov：原版 Fov.Perform 从原点向方框四边打 Bresenham 射线，
/// SetVisible 里用 `GetVisDistance(origin, x, y) <= range` 卡掉远处 —— 结果是一个**圆**，不是方块。
/// 这里用同一个判定 `dx² + dz² <= radius²`。
///
/// 遮罩本身：一张 (w×Sub)×(h×Sub) 的 Texture2D，每格切 Sub×Sub 个子像素，
/// 贴在 SpriteRenderer 上挂到 transMap 下。pixelsPerUnit 写 Sub，Sprite 的局部尺寸仍是 w×h 格，
/// Place() 的缩放公式不用跟着变。定位靠 TilemapUtils.GetGridWorldPos(fogmap, ...)，与原版 srHighlight 一致。
///
/// 边缘颗粒：原版场景的雾边不呆板，是因为它贴着墙和走廊走，形状天然参差；
/// 大陆图是一张平铺的网格，光有圆边还是规整，所以再叠一层值噪声把边界打散。
///
/// 存档：不改游戏类结构，把「已探索」按位图塞进 Region.mapInt（Spatial 上的
/// `[JsonProperty] Dictionary<int,int>`，原版代码里没有任何地方读写它，正好空出来）。
/// 不能借 EloMap._ints —— `Region.elomap` 这个字段本身没有 [JsonProperty]，
/// 而 EClass 是 MemberSerialization.OptIn，所以 EloMap 整个不进存档：
/// 每次读档都是 new EloMap()，_ints 恒为空，OnSerializing/OnDeserialized 根本不会被调用。
/// 每 32 格压一个 int，键 MapIntBase+1+i；MapIntBase 自己存 `w | (h << 16)` 当尺寸校验码，
/// 尺寸对不上（换版本 / 换地图）就当全迷雾。写入点在 Reveal，走的是实时字典，不需要任何存盘回调。
/// </summary>
[HarmonyPatch]
internal static class WorldMapFog
{
    /// <summary>揭开半径，单位是格。判定同 Fov：欧氏距离，含斜角。</summary>
    private const int RevealRadius = 2;

    /// <summary>每格切多少子像素。越大颗粒越细，纹理内存按平方涨。</summary>
    private const int Sub = 8;

    /// <summary>「见过但当前看不见」的雾浓度，对应原版 light / 3 * 2 的压暗量。</summary>
    private const float MemoryAlpha = 0.35f;

    private static readonly Color32 FogPixel = new Color32(8, 10, 18, 0);

    /// <summary>「已探索」位图在 Region.mapInt 里的起始键，键段留 990000~990999 给本功能。</summary>
    private const int MapIntBase = 990000;

    private static EloMap map = null!;
    private static bool[,] seen = null!;
    private static bool[,] vis = null!;

    /// <summary>逐格明暗：0 = 没去过，1 = 去过但当前看不见，2 = 当前视野内。插值后当 alpha 用。</summary>
    private static float[,] field = null!;

    private static Color32[] block = null!;
    private static Texture2D texture = null!;
    private static SpriteRenderer renderer = null!;
    private static bool flipY;
    private static int lastX = int.MinValue;
    private static int lastZ = int.MinValue;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(EloMap), nameof(EloMap.Init))]
    private static void InitPostfix(EloMap __instance)
    {
        map = __instance;
        seen = LoadSeen();
        vis = new bool[map.w, map.h];
        field = new float[map.w, map.h];
        lastX = int.MinValue;
        lastZ = int.MinValue;

        UpdateField(0, 0, map.w - 1, map.h - 1);

        // Place 会算出 flipY，必须在 Paint 之前跑（纹理行序要靠它）。
        BuildOverlay();
        Place();
        Paint(0, 0, map.w - 1, map.h - 1);
        SyncIcons();
    }

    /// <summary>
    /// 世界地图上 PC 每走一格就揭开一次。
    /// 大陆图是 Scene.Mode.Zone（Player.MoveZone 里写死 Init(Mode.Zone)），所以 ActionMode.UpdateInput
    /// 每帧都会被 Scene 调用；ActionMode.UpdateInput 本身不是 virtual、没有任何子类 override，
    /// postfix 一定执行。（别用 Scene.UpdateCursor：它只在换 ActionMode 和主菜单里调，不是每帧。）
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ActionMode), nameof(ActionMode.UpdateInput))]
    private static void UpdateInputPostfix()
    {
        if (!EClass._zone.IsRegion || map == null || renderer == null)
        {
            return;
        }

        Place();

        Point pos = EClass.pc.pos;
        if (Plugin.FogEnabled && (pos.x != lastX || pos.z != lastZ))
        {
            // 先撤掉上一格留下的「当前视野」，否则走过的地方会一直亮着。
            if (lastX != int.MinValue)
            {
                ClearVis(lastX, lastZ);
                Repaint(lastX, lastZ);
            }

            lastX = pos.x;
            lastZ = pos.z;
            Reveal(pos);
            Repaint(pos.x, pos.z);
            map.objmap.UpdateMeshImmediate();
        }

        // 角色每帧都可能走动，所以放在「PC 有没有换格」的判断之外，每帧都同步。
        SyncCreatures();
    }

    /// <summary>配置界面切换后立刻生效（不用重开世界地图）。</summary>
    public static void OnEnabledChanged()
    {
        if (renderer == null)
        {
            return;
        }

        renderer.enabled = Plugin.FogEnabled;
        SyncIcons();
        SyncCreatures();
    }

    /// <summary>
    /// 走 actor 路径的角色（PC 与带自定义外观的同伴）在这里预同步显隐。
    ///
    /// 真正干活的是 <see cref="RendererDrawPatch"/> —— 野怪根本不建 actor（usePass=true，
    /// 走 MeshPass 批绘），这一层压根看不见它们，所以不能当作唯一手段。
    /// 这里保留的用处：每帧把 actor 的 enabled 提前摆正，以及关掉雾时一次性恢复全图。
    ///
    /// 为什么雾本身就盖不住它们：CardActor 的 SpriteRenderer 材质走 CharaActor（queue 3000）
    /// / CharaActorPCC（queue 3010），不吃遮罩的排序层与 order —— 实测把雾整张贴成不透明纯色，
    /// 同伴与野怪依旧整块浮在雾上面（见 README 的验证记录）。地形/obj/云层那些 MeshRenderer 才吃排序。
    ///
    /// 原版场景的雾本来也不记生物：记忆里留的是地形，会走的角色出了视野就该消失。
    /// 所以按格判定，只有落在当前视野（vis）里的角色才显示；「见过但看不见」的记忆区同样不显示。
    /// </summary>
    private static void SyncCreatures()
    {
        // 还没揭开过（刚进图第一帧），先不动，免得把 PC 和同伴藏掉。
        if (lastX == int.MinValue)
        {
            return;
        }

        bool hide = Plugin.FogEnabled;
        List<Chara> charas = EClass._map.charas;
        for (int i = 0; i < charas.Count; i++)
        {
            Chara chara = charas[i];
            if (chara == null || chara.renderer == null || chara.renderer.actor == null)
            {
                continue;
            }

            // SetVisible 只切 sr/sr2 的 enabled，不动逻辑，原版自己也这么用（CardActor.cs:316）。
            chara.renderer.actor.SetVisible(!hide || InVision(chara.pos));
        }
    }

    private static bool InVision(Point p)
    {
        if (p.x < 0 || p.z < 0 || p.x >= map.w || p.z >= map.h)
        {
            return true;
        }

        return vis[p.x, p.z];
    }

    /// <summary>
    /// 视野外的角色/物品连画都别画。
    ///
    /// 这一层必须拦 Renderer，不能只拦 Actor。大陆图上的 Chara 有两条绘制路
    /// （CardRenderer.Draw：`if (usePass) renderData.Draw(p); else if (hasActor) actor.OnRender(p);`）：
    ///
    /// - PC 和带自定义外观的同伴：`data.pass` 为 null → usePass=false → 走 actor；
    /// - 普通 NPC / 野怪：renderData 带 pass（`pass chara` / `chara_expand_0` / `charaL`）
    ///   → usePass=true → 走 MeshPass 批绘，**根本不经过 CardActor.OnRender**。
    ///
    /// 实测雾里的 hermitcrab：`usePass=True, hasActor=False, actor=null`，actor 压根没建，
    /// 但屏幕上照样画着它。所以只压 actor 对野怪一类角色完全无效 —— 拦在 Renderer 这一层，
    /// 两条路才一起管得住。CharaRenderer.Draw 在 base.Draw 之后还要补帽子与手持物，
    /// 得单独挡，否则人没了帽子还飘在雾里。
    /// </summary>
    [HarmonyPatch]
    private static class RendererDrawPatch
    {
        private static readonly Type[] DrawArgs =
        {
            typeof(RenderParam), typeof(Vector3).MakeByRefType(), typeof(bool)
        };

        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> Targets()
        {
            yield return typeof(CharaRenderer).GetMethod(
                "Draw", BindingFlags.Instance | BindingFlags.Public, null, DrawArgs, null);
            yield return typeof(CardRenderer).GetMethod(
                "Draw", BindingFlags.Instance | BindingFlags.Public, null, DrawArgs, null);
        }

        [HarmonyPrefix]
        private static bool Prefix(CardRenderer __instance)
        {
            return !ShouldHide(__instance);
        }
    }

    private static bool ShouldHide(CardRenderer crd)
    {
        if (!Plugin.FogEnabled || map == null || !EClass._zone.IsRegion)
        {
            return false;
        }

        Card card = crd.owner;
        if (card == null || card == EClass.pc || InVision(card.pos))
        {
            return false;
        }

        if (crd.actor != null)
        {
            crd.actor.SetVisible(false);
        }

        return true;
    }

    private static void BuildOverlay()
    {
        if (renderer != null)
        {
            Object.Destroy(renderer.gameObject);
        }

        if (texture != null)
        {
            Object.Destroy(texture);
        }

        int tw = map.w * Sub;
        int th = map.h * Sub;
        texture = new Texture2D(tw, th, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };

        // FullRect 钉死网格：不让 Unity 按 alpha 裁剪，纹理当时是什么内容都不影响网格大小。
        Sprite sprite = Sprite.Create(
            texture, new Rect(0f, 0f, tw, th), Vector2.zero, Sub, 0, SpriteMeshType.FullRect);
        GameObject go = new GameObject("WarfogOverlay");
        go.transform.SetParent(map.actor.transMap, false);
        renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;

        // 遮罩必须压在所有「画在地图上」的东西上面：fogmap/objmap 那些雾块 tilemap、
        // 城镇的 elolight 光晕（transLight 下的独立 SpriteRenderer）、以及野怪/物品的渲染器。
        // sortingOrder 只在同一 sortingLayer 内比较，层不同时数值再大也会被盖住，
        // 所以要挂到排序层列表的最后一层（SortingLayer.layers 按渲染先后排列），
        // order 再给一个大数 —— 角色/物品的 order 是按 z 逐格算的，比 tilemap 那几个值大得多。
        SortingLayer[] layers = SortingLayer.layers;
        if (layers.Length > 0)
        {
            renderer.sortingLayerID = layers[layers.Length - 1].id;
        }

        renderer.sortingOrder = 30000;
        renderer.enabled = Plugin.FogEnabled;

        // 仅排查用：把场景里 SpriteRenderer 实际用的层和 order 汇总一行，只在 Debug/VerboseLog 打开时输出。
        if (Plugin.VerboseLogEnabled)
        {
            LogLayerSurvey();
        }
    }

    private static void LogLayerSurvey()
    {
        Dictionary<string, int> kinds = new Dictionary<string, int>();
        foreach (SpriteRenderer sr in Object.FindObjectsOfType<SpriteRenderer>())
        {
            string key = SortingLayer.IDToName(sr.sortingLayerID) + "/" + sr.sortingOrder;
            kinds.TryGetValue(key, out int n);
            kinds[key] = n + 1;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder("[Fog] 遮罩 ");
        sb.Append(SortingLayer.IDToName(renderer.sortingLayerID)).Append('/').Append(renderer.sortingOrder).Append("；场景内：");
        foreach (KeyValuePair<string, int> pair in kinds)
        {
            sb.Append(' ').Append(pair.Key).Append('x').Append(pair.Value);
        }

        Plugin.LogVerbose(sb.ToString());
    }

    /// <summary>
    /// 按 fogmap 的单格步长摆遮罩。
    ///
    /// 两个必须照顾到的坑：
    /// - GetGridWorldPos 返回的差值已经含 transMap 自己的 localScale（实测是 2），
    ///   所以算出来的世界尺寸要再除以父级 lossyScale 才是 localScale，否则遮罩会放大一倍盖满整屏。
    /// - 步长可能是负的（y 轴朝下时），用 min() 取真正的左下角、scale 取绝对值，同时把纹理行序翻过来。
    /// </summary>
    private static void Place()
    {
        STETilemap fogmap = map.fogmap;
        Vector3 origin = TilemapUtils.GetGridWorldPos(fogmap, map.minX, map.minY);
        Vector3 stepX = TilemapUtils.GetGridWorldPos(fogmap, map.minX + 1, map.minY) - origin;
        Vector3 stepY = TilemapUtils.GetGridWorldPos(fogmap, map.minX, map.minY + 1) - origin;

        float w = Mathf.Abs(stepX.x);
        float h = Mathf.Abs(stepY.y);
        if (w <= 0f || h <= 0f)
        {
            return;
        }

        float left = Mathf.Min(origin.x - w * 0.5f, origin.x + stepX.x * (map.w - 1) + w * 0.5f);
        float bottom = Mathf.Min(origin.y - h * 0.5f, origin.y + stepY.y * (map.h - 1) + h * 0.5f);

        Vector3 parent = renderer.transform.parent.lossyScale;
        renderer.transform.position = new Vector3(left, bottom, origin.z);
        renderer.transform.localScale = new Vector3(
            parent.x == 0f ? w : w / parent.x,
            parent.y == 0f ? h : h / parent.y,
            1f);
        flipY = stepY.y < 0f;
    }

    private static void Reveal(Point center)
    {
        int r2 = RevealRadius * RevealRadius;
        for (int dz = -RevealRadius; dz <= RevealRadius; dz++)
        {
            for (int dx = -RevealRadius; dx <= RevealRadius; dx++)
            {
                // 同 Fov.SetVisible 的 GetVisDistance <= range：欧氏圆，不是正方形。
                if (dx * dx + dz * dz > r2)
                {
                    continue;
                }

                int x = center.x + dx;
                int z = center.z + dz;
                if (x < 0 || z < 0 || x >= map.w || z >= map.h)
                {
                    continue;
                }

                vis[x, z] = true;
                if (seen[x, z])
                {
                    continue;
                }

                seen[x, z] = true;
                StoreSeen(x, z);

                EloMap.Cell cell = map.cells[x, z];
                if (cell.zone != null)
                {
                    cell.zone.isKnown = true;

                    // Init 时把没探索过的城镇光晕关掉了，这里补回来。
                    ShowLight(x, z);
                }

                if (cell.obj != 0)
                {
                    map.objmap.SetTile(map.minX + x, map.minY + z, cell.obj);
                }
            }
        }
    }

    /// <summary>把这一格对应城镇的夜间光晕打开（Init 时按「没见过」关掉过）。</summary>
    private static void ShowLight(int x, int z)
    {
        int gx = map.minX + x;
        int gz = map.minY + z;
        foreach (EloMapLight light in map.actor.lights)
        {
            if (light.gx == gx && light.gy == gz)
            {
                light.sr.enabled = true;
                return;
            }
        }
    }

    private static void ClearVis(int cx, int cz)
    {
        int r2 = RevealRadius * RevealRadius;
        for (int dz = -RevealRadius; dz <= RevealRadius; dz++)
        {
            for (int dx = -RevealRadius; dx <= RevealRadius; dx++)
            {
                if (dx * dx + dz * dz > r2)
                {
                    continue;
                }

                int x = cx + dx;
                int z = cz + dz;
                if (x >= 0 && z >= 0 && x < map.w && z < map.h)
                {
                    vis[x, z] = false;
                }
            }
        }
    }

    /// <summary>
    /// 重算明暗场并重画 PC 周围这一块。
    /// 视野外最多 RevealRadius + 3 格就饱和成全黑，整图刷一遍纯属浪费（地图上百格见方）。
    /// </summary>
    private static void Repaint(int cx, int cz)
    {
        int pad = RevealRadius + 3;
        int x0 = cx - pad;
        int z0 = cz - pad;
        int x1 = cx + pad;
        int z1 = cz + pad;
        UpdateField(x0, z0, x1, z1);
        Paint(x0, z0, x1, z1);
    }

    private static void UpdateField(int x0, int z0, int x1, int z1)
    {
        x0 = Mathf.Max(x0, 0);
        z0 = Mathf.Max(z0, 0);
        x1 = Mathf.Min(x1, map.w - 1);
        z1 = Mathf.Min(z1, map.h - 1);

        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                field[x, z] = vis[x, z] ? 2f : (seen[x, z] ? 1f : 0f);
            }
        }
    }

    /// <summary>
    /// 把一块格区域画进纹理并提交。
    ///
    /// 每个子像素取双线性插值后的明暗场，再用值噪声推 ±0.35，最后折成 alpha：
    /// 2 = 透明（当前视野），1 = MemoryAlpha（走过的暗区），0 = 全黑（没去过）。
    /// 场是连续插值的，所以「透明→半暗」「半暗→全黑」两道边界都是渐变带，
    /// 再被噪声打散成颗粒，和场景里 Fov 的散点边一个观感。
    /// </summary>
    private static void Paint(int x0, int z0, int x1, int z1)
    {
        x0 = Mathf.Max(x0, 0);
        z0 = Mathf.Max(z0, 0);
        x1 = Mathf.Min(x1, map.w - 1);
        z1 = Mathf.Min(z1, map.h - 1);
        if (x1 < x0 || z1 < z0)
        {
            return;
        }

        int bw = (x1 - x0 + 1) * Sub;
        int bh = (z1 - z0 + 1) * Sub;
        if (block == null || block.Length < bw * bh)
        {
            block = new Color32[bw * bh];
        }

        for (int z = z0; z <= z1; z++)
        {
            // flipY 时 z 越大纹理行越靠下，块内行序要反过来。
            int rowBase = flipY ? (z1 - z) * Sub : (z - z0) * Sub;
            for (int x = x0; x <= x1; x++)
            {
                int colBase = (x - x0) * Sub;
                for (int v = 0; v < Sub; v++)
                {
                    float gz = z + (v + 0.5f) / Sub;
                    int row = (rowBase + v) * bw + colBase;
                    for (int u = 0; u < Sub; u++)
                    {
                        float gx = x + (u + 0.5f) / Sub;
                        float f = SampleField(gx, gz);

                        // 噪声只作用在两道边界的中间：f 的小数部分离整数越近 edge 越小。
                        // f=0（全黑）、f=1（记忆区内部）、f=2（视野内部）都是 0，
                        // 只有 0.5 和 1.5 这种正卡在边界上的地方取满。
                        float g = f - Mathf.Floor(f);
                        float edge = 4f * g * (1f - g);
                        f = Mathf.Clamp(f + (Noise(gx, gz) - 0.5f) * 0.9f * edge, 0f, 2f);

                        float a = f <= 1f ? 1f - (1f - MemoryAlpha) * f : MemoryAlpha * (2f - f);
                        block[row + u] = a <= 0.004f
                            ? default
                            : new Color32(FogPixel.r, FogPixel.g, FogPixel.b, (byte)(a * 255f));
                    }
                }
            }
        }

        texture.SetPixels32(x0 * Sub, flipY ? (map.h - 1 - z1) * Sub : z0 * Sub, bw, bh, block);
        texture.Apply(false);
    }

    /// <summary>格中心为整数坐标，双线性插值；越界按 0（全黑）。</summary>
    private static float SampleField(float gx, float gz)
    {
        float fx = gx - 0.5f;
        float fz = gz - 0.5f;
        int x0 = Mathf.FloorToInt(fx);
        int z0 = Mathf.FloorToInt(fz);
        float tx = fx - x0;
        float tz = fz - z0;

        float bottom = Mathf.Lerp(Field(x0, z0), Field(x0 + 1, z0), tx);
        float top = Mathf.Lerp(Field(x0, z0 + 1), Field(x0 + 1, z0 + 1), tx);
        return Mathf.Lerp(bottom, top, tz);
    }

    private static float Field(int x, int z)
    {
        return x < 0 || z < 0 || x >= map.w || z >= map.h ? 0f : field[x, z];
    }

    /// <summary>两个倍频的平滑值噪声，值域 0~1。</summary>
    private static float Noise(float gx, float gz)
    {
        float a = ValueNoise(gx * 2.1f, gz * 2.1f, 1337);
        float b = ValueNoise(gx * 5.3f, gz * 5.3f, 7907);
        return Mathf.Clamp01(a * 0.65f + b * 0.35f);
    }

    private static float ValueNoise(float x, float z, int seed)
    {
        int xi = Mathf.FloorToInt(x);
        int zi = Mathf.FloorToInt(z);
        float fx = x - xi;
        float fz = z - zi;
        fx = fx * fx * (3f - 2f * fx);
        fz = fz * fz * (3f - 2f * fz);

        float bottom = Mathf.Lerp(Hash(xi, zi, seed), Hash(xi + 1, zi, seed), fx);
        float top = Mathf.Lerp(Hash(xi, zi + 1, seed), Hash(xi + 1, zi + 1, seed), fx);
        return Mathf.Lerp(bottom, top, fz);
    }

    private static float Hash(int x, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + z * 668265263 + seed * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFu) / 65535f;
        }
    }

    /// <summary>没去过的格子把 zone 图标擦掉，去过的补回来。</summary>
    private static void SyncIcons()
    {
        bool hide = Plugin.FogEnabled;
        for (int z = 0; z < map.h; z++)
        {
            for (int x = 0; x < map.w; x++)
            {
                EloMap.Cell cell = map.cells[x, z];
                if (cell.obj == 0)
                {
                    continue;
                }

                if (hide && !seen[x, z])
                {
                    map.objmap.Erase(map.minX + x, map.minY + z);
                }
                else
                {
                    map.objmap.SetTile(map.minX + x, map.minY + z, cell.obj);
                }
            }
        }

        map.objmap.UpdateMeshImmediate();

        // 城镇的夜间光晕是 transLight 下的独立 SpriteRenderer（EloMap.AddLight），
        // 不归 objmap 管，得单独关掉，否则会隔着雾亮着。
        foreach (EloMapLight light in map.actor.lights)
        {
            int x = light.gx - map.minX;
            int z = light.gy - map.minY;
            bool known = x >= 0 && z >= 0 && x < map.w && z < map.h && seen[x, z];
            light.sr.enabled = !hide || known;
        }
    }

    /// <summary>从 Region.mapInt 读回「已探索」位图；尺寸对不上或没存过就是全迷雾。</summary>
    private static bool[,] LoadSeen()
    {
        bool[,] result = new bool[map.w, map.h];
        Dictionary<int, int> store = EClass.world.region.mapInt;
        if (!store.TryGetValue(MapIntBase, out int dims) || dims != (map.w | (map.h << 16)))
        {
            return result;
        }

        int total = map.w * map.h;
        for (int i = 0; i * 32 < total; i++)
        {
            if (!store.TryGetValue(MapIntBase + 1 + i, out int bits))
            {
                continue;
            }

            for (int b = 0; b < 32; b++)
            {
                int idx = i * 32 + b;
                if (idx >= total)
                {
                    break;
                }

                if ((bits & (1 << b)) != 0)
                {
                    result[idx % map.w, idx / map.w] = true;
                }
            }
        }

        return result;
    }

    /// <summary>把一格记进 Region.mapInt 的位图（走实时字典，存档时会跟着 Region 一起写进去）。</summary>
    private static void StoreSeen(int x, int z)
    {
        Dictionary<int, int> store = EClass.world.region.mapInt;
        store[MapIntBase] = map.w | (map.h << 16);

        int idx = z * map.w + x;
        int key = MapIntBase + 1 + idx / 32;
        store.TryGetValue(key, out int bits);
        store[key] = bits | (1 << (idx % 32));
    }
}
