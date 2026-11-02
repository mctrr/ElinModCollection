# 战争迷雾 (Warfog)

给 Elin 的**世界地图**（北提里斯大陆图）盖上战争迷雾。

原版大陆图是打开就一眼看全的：`EloMap.Init` 把每个 zone 的图标画到 `objmap` 上，没有任何遮挡。
这个 mod 给它压一层遮罩，只揭开角色周围 2 格，走过的格子永久保留。

从 Elona Be Like v2.1.1 里拆出来的独立 mod，存档里的探索进度照旧接得上（见「存档」一节）。

## 效果

- **视野**：PC 周围 **2 格**，含斜角 —— 是个圆不是方块（同原版 `Fov` 的 `GetVisibleDistance <= range` 判定）。
- **走过就留下**：揭开过的格子永久保留，不会重新变黑；但当前看不见的会压暗一档。
- **没去过的**：纯色雾块。那里的城镇图标、城名、夜里的灯火一并不显示。
- **视野外的生物**：一律不画。角色会走，记忆里留着只会是假信息；顺带修掉「野怪整块浮在雾上」。
- **边缘**：叠了一层噪声打散，不是一个硬邦邦的方块边。

明暗三档照抄原版场景的做法（`BaseTileMap` 里那套）：

| 状态 | 表现 | 原版对应 |
|---|---|---|
| 当前视野内 | 不压暗 | `cell.pcSync` 为真时的 `light` |
| 见过但看不见 | 压到 35% 雾 | `cell.light / 3 * 2`，即「记忆中的地形」亮度降到 2/3 |
| 没见过 | 全黑 | `isSeen == false` 时那块 `rendererFogFloorSolid` 纯色雾 tile |

原版的「未见」本来就是纯色块，不是保留地形，所以这里也不做「模糊的轮廓」。

## 存档

探索进度写进 `Region.mapInt` —— `Spatial` 上的 `[JsonProperty] Dictionary<int,int>`，原版代码里没有任何地方读写它，正好空出来。每 32 格压一个 int，键 `990000` 存 `w | (h << 16)` 当尺寸校验，`990001+i` 存数据块。

因为是实时字典（走一步就写），存盘时跟着 Region 一起落地，不需要任何存盘回调。

- **从 Elona Be Like 拆过来不用重探**：键段和位序一模一样，存档里的份数据直接继承。
- **尺寸对不上就退回全迷雾**：换了地图或换了游戏版本导致 `w/h` 变化时，按「没探索过」处理，重新探索即可。
- 更早的版本（迷雾还在 `EloMap._ints` 里借位那版）没存下任何东西，救不回来。

## 安装

1. 把整个 `Mod_Warfog` 文件夹放进游戏的 `Package` 目录。
2. 启动游戏，确认 `BepInEx\LogOutput.log` 里有一行 `战争迷雾 1.0.0 loaded.`。
3. 参数在游戏的 **Mod 列表 → 选中本 mod** 里改，会写回 `BepInEx\config\mctrr.warfog.cfg`。

```
[General] Enabled     开关（默认开）
[Debug]   VerboseLog  排查用日志（默认关）
```

关掉开关会立刻撤掉遮罩并恢复城镇图标，不用重开世界地图。

## 实现要点（给以后维护的自己）

- 遮罩是一张 `(w×Sub)×(h×Sub)` 的 Texture2D（`Sub = 8`），贴在 SpriteRenderer 上挂到 `transMap` 下。
  `Sprite.Create` 的 pixelsPerUnit 写 `Sub`，Sprite 的局部尺寸仍是 `w×h` 格。
- 定位走 `TilemapUtils.GetGridWorldPos(fogmap, ...)`，它返回的差值**已经含 `transMap.localScale`**，不能再乘第二次。
- 排序层挂**最后一层**（`SortingLayer.layers[^1]`）+ `sortingOrder = 30000`：
  只在同一层内比较 order，层不对的话数值再大也会被盖住。
- 城镇夜光（`EloMapLight`）是 `transLight` 下的独立 SpriteRenderer，不归 `objmap` 管，得按 `seen` 单独开关。
- 只重画 PC 周围 `±(2+3)` 格：视野外 5 格就饱和成全黑，整图刷一遍纯属浪费。
- **角色盖不住，得单独掐**：大陆图上的人物不吃遮罩的排序层与 order —— 实测把雾纹理整张刷成不透明纯色，同伴和野怪依旧整块浮在雾上（地形 / obj / 云层那些 MeshRenderer 则被老老实实盖住）。
- **掐角色要掐在 Renderer 层，不能掐在 Actor 层**：Chara 有两条绘制路（`CardRenderer.Draw`：`if (usePass) renderData.Draw(p); else if (hasActor) actor.OnRender(p);`）。
  - PC 和带自定义外观的同伴 `data.pass == null` → `usePass=false` → 走 `actor`；
  - **普通 NPC / 野怪 renderData 带 pass（`pass chara` 等）→ `usePass=true` → 走 MeshPass 批绘，压根不建 actor，也完全不经过 `CardActor.OnRender`**。实测雾里的 `hermitcrab`：`usePass=True, hasActor=False, actor=null`，屏幕上照样画着它。
  只压 `CardActor.OnRender` 的那一版等于只修了 PC 和同伴，野怪一个没挡住。现在的做法：在 `CharaRenderer.Draw` 与 `CardRenderer.Draw(RenderParam, ref Vector3, bool)` 上挂 Prefix（`ref` 参数没法写进特性常量，所以用 `HarmonyTargetMethods` 反射取方法）。
  `CharaRenderer.Draw` 在 `base.Draw` 之后还要补帽子和手持物，必须单独挡一层，否则人没了帽子还飘在雾里。
  `SyncCreatures()` 留着做 actor 路径的每帧预同步和「关雾后全图恢复」，但它不是主手段 —— 它连 `actor == null` 的角色都看不见。
- 排查手法（以后再遇到「雾里还能看见东西」）：**先分清是哪个渲染器画的**。把雾纹理整张刷成纯色不透明，谁还浮在上面，谁就没被遮罩住；再 `Resources.FindObjectsOfTypeAll<SpriteRenderer>()` 看有没有对应 SpriteRenderer —— **没有 actor 却还画着，那就是 MeshPass 批绘**，别再去 `CardActor` 上找。

`WorldMapFog.cs` 顶部那段注释写得更细，改公式的话封面生成脚本（`.tools/make_preview.py`）里也有一份同样的公式，两边一起改。
