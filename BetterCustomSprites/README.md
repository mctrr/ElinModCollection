# Better Custom Sprites

[Elin](https://store.steampowered.com/app/2135150/Elin/) 的 **Animated Custom Sprites** 增强版：角色按状态自动切换动画、主角（PCC）与 NPC 动态 Skin、静态战斗图。

> 基于 [gottyduke/Elin.Plugins](https://github.com/gottyduke/Elin.Plugins) 中的 AnimatedCustomSprites，MIT 协议。  
> 仓库：https://github.com/mctrr/BetterCustomSprites  
> Steam 创意工坊文案：[中文](docs/workshop-description.md) · [English](docs/workshop-description.en.md) · [日本語](docs/workshop-description.ja.md)

当前版本：`2.5.0`（`mctrr.bettercustomsprites`）

---

## 功能概览

| 能力 | 说明 |
|------|------|
| 状态动画 | combat / combat_begin / magic / melee / hurt / revive / move / cmove / greet(greet1-4) / eat / wet / drunk / confused / blind |
| 前缀 | `acs_override`、`snow`、`married` 与状态组合（泳装 / 婚服等整套差分） |
| 战斗武器子键 | `combat.sword` / `.polearm` / `.staff`；只做一套时用 `combat` |
| 主角 / NPC 换肤 | Skin / Texture 目录贴图；创角面板与居民菜单均可换 |
| 静态战斗图 | 无多帧 combat ACS 时用 `{base}_bcs_combat.png` |
| 贴图密度 | `@N` 标注或 `.pref` 的 `scaleTex`：4x 素材占据与 1x 相同的世界尺寸 |
| 超宽分片 | 单边 > 16384px 的横条按帧边界自动内存分片（不改磁盘文件） |
| 贴图管理 | 切区域预热 + 按需释放 + 跨重建复用 + 皮肤热加载（默认关） |
| 设置入口 | 游戏内模组配置页（7 组 23 项）+ `ACS.*` 控制台命令 |

---

## 素材布局

底图（原版动态贴图）放 `Skin/` 或 `Texture/` 目录，**ACS 动画放在与该底图同名的文件夹里**：

```
Skin/ 或 Texture/
├─ pcr_xxx.png             # 原版动态贴图，就是待机（idle 不用单独做）
├─ pcr_xxx.ini             # frame / time / scale（原版格式）
├─ pcr_xxx.pref            # 可选：scaleTex = 400 表示 4x
└─ pcr_xxx/                # 同名文件夹：该角色的 ACS 动画
   ├─ pcr_xxx_acs_combat#66_0-5.png
   ├─ pcr_xxx_acs_move#66_0-5.png
   └─ pcr_xxx_acs_snow.move#66_0-5.png
```

文件名格式：

```
{皮肤id}_acs_[{前缀}.]{状态}#{帧间隔ms}_{起始帧}-{结束帧}[@{密度}]
```

- **`@N`**：贴图密度，值 = 期望渲染 PPU。图放大几倍就写几倍（`@400` = 4x 素材，世界尺寸与 1x 完全相同）。不写时按底图 `.pref` 的 `scaleTex`，再退 100
- 底图文件名**不能**带 `@N`（原版认不出皮肤 id），密度用 `.pref` 声明；同名文件夹里的 clip 照旧用文件名 `@N`，两边填同一个数
- 同名文件夹里的 clip **不需要 ini**（帧数/间隔全在文件名里）

## 状态

| 状态 | 什么时候播 | 资源名 |
|------|-----------|--------|
| combat | 战斗中（PC 主动出手后维持 5 回合，到期扫一次视野，有敌人则续期） | `_acs_combat`（可拆武器子键） |
| combat_begin | 刚进入战斗，播一轮后转 combat；脱离战斗后才会再播 | `_acs_combat_begin` |
| magic | 施法（任意法术） | `_acs_magic` |
| melee | 近战攻击 | `_acs_melee` |
| hurt | 受击 | `_acs_hurt` |
| revive | 复活（播完回到底图） | `_acs_revive` |
| move | 移动中 | `_acs_move` |
| cmove | 战斗中移动（不做就用 `move`） | `_acs_cmove` |
| greet / greet1-4 | NPC 看见玩家打招呼 | `_acs_greet`、`_acs_greet1`… |
| eat | 吃东西 | `_acs_eat` |
| wet / drunk / confused / blind | 潮湿 / 醉酒 / 混乱 / 失明 | 同名 |

战斗、移动、问候、进食都排在异常状态之前，所以**异常状态只在原地不动时**可见。

**PC 的 combat / combat_begin 只在主角主动出手时播放**：近战攻击、主动敌对动作、换主手武器
（`MarkActiveCombat`）。单纯被敌人攻击不会进战斗姿态 —— 原版靠 `combatCount` 判断，但 PC 侧
还要 `acs_combat` 标记，标记只在上面三处写。NPC 走裸 `combatCount > 0`，自己的攻击动作写 10。

**受击（hurt）只认角色造成的伤害**：`Card.DamageHP` 的 `origin` 为空就是环境 / 状态伤害
（中毒、燃烧、出血、过食、挨饿、疲劳、负重、陷阱、摔落），不切受击。这类伤害每回合结算，
一旦切 hurt 会打断 `combat_begin` / `magic` / `melee`，被打断的一次性动作按新签名从第 0 帧重播。

## 前缀（`GetPrefix`，高 → 低）

| 优先级 | 前缀 | 条件 | 示例（都在同名文件夹里） |
|--------|------|------|------|
| 1 | `acs_override` | Drama / `mapStr` 手动指定 | `pcr_xxx_acs_skin.combat#66_0-5.png` |
| 2 | `snow` | 站在雪地 | `pcr_xxx_acs_snow.move#41_0-14.png` |
| 3 | `married` | 已婚 | `pcr_xxx_acs_married.greet#66_0-8.png` |
| — | 无 | 默认 | `pcr_xxx_acs_wet#66_0-5.png` |

## 战斗武器子键（可选）

只看主手装备：

```
_acs_combat.sword / .polearm / .staff   ← 主手类型（skill 101/103/106）
_acs_combat                             ← 通用 / 单套 / 其它武器兜底
```

有前缀时：`_acs_snow.combat.sword` → `_acs_snow.combat` → 无前缀同序。

## 回退规则

```
magic / melee 缺片 → _acs_atk → 按 combat 选片（含武器子键）
cmove 缺片         → move → combat
combat_begin 缺片  → combat（不回底图）
其它非 idle 缺片   → 底图
```

**当前皮肤是权威**：换到没有 combat 动画的动态皮肤后，战斗中保持当前皮外观（底图 / 静态图），**不会**回落到原皮 combat。

## 静态战斗图

无多帧 `_acs_combat*` 时用单帧图：同名文件夹里放 `{base}_bcs_combat.png`，或用 `Skin/bcs_static_map.ini` 把别的皮肤映射过来（只补缺，不覆盖已有）：

```ini
[combat]
baseSkinId=foreignSkinId
```

`ACS.ReloadAcs` 会重读映射表。

---

## 主角 / NPC 动态 Skin

- 文件可放在游戏 / Mod 的 **Skin** 目录（与原版一致），或 **Texture 根目录** / `Texture/Skin`（不递归 `Texture/Item` 等子树，避免把物品图扫进换肤列表）
- **创建角色**面板底部有「更换贴图」入口（原版 `LayerEditSkin`）
- 换肤窗口默认只列 Skin 目录；底部开关打开后才显示 Texture 贴图（列表虚拟滚动，可见项才加载 PNG），Texture 来源会在名字后标 mod 名 / Custom
- **居民列表**右键：PC、PCC NPC、以及原版已支持的非 PCC NPC 都可换肤
- NPC 手动换肤后会走自定义皮肤的 ACS，不再只显示静态底图

拟态、以及原版图集变身/强化（`RendererReplacer` 且无 PCC）会跳过 ACS 覆盖，避免叠到原版 tile 上。

---

## 贴图与性能

### 切区域预热

`Scene.Init(Zone)` 返回前**阻塞预热**（`GreetPrewarm.WarmBlocking`）：把新地图里真实存在的 NPC 会用到的
`_acs_*` 高清贴图先解好。后台 6 条线程做读盘 + inflate + 反 filter（≈ 97% 耗时），主线程只建
`Texture2D` / `Sprite`（Unity 对象只能主线程）。这段耗时算进读条，进图后第一次遇到人不再卡。
`WarmTimeoutMs = 15000` 兜底；进图后 `Tick()` 每 30 帧补扫后进场的 NPC。

预热白名单在配置页「场景预热」组逐项开关：**底图 / idle / greet / move 默认开**，其余默认关。

### 按需释放

原版 `SpriteData` 只懒加载、**从不卸载**，长局显存单调累积。`SpriteCacheEvict` 每 3 秒扫一次：
当前地图在用的 + 宽限期（默认 30s）内取过的保留，其余把 `tex`/`sprites` 交还；
之后仍超预算（默认 2048MB，可调，0 = 不限）时按「最久未用」释放一半。按字节不按张数。
只断托管引用、从不 `Destroy`，所以不会花屏或 NRE；最坏是下次用到时重新解码一次。

### 跨重建复用

`ListSkins()` 每次重建都会造全新 `SpriteData`。`SkinTextureReuse` 在重建时按 png 路径把已解码的
贴图挂到新对象上，消掉「读档 / 切区域回来那一下」的重解码。关掉 = 每次重建都重读。

### 皮肤热加载（默认关）

`FileSystemWatcher` 监视贴图目录，游戏外（Aseprite / 导出脚本）改完 PNG 后**防抖 1.5s**自动作废重载，
角色下次进画面时才重解码。只处理「改已有图」；新增 / 删除 / 改名要敲 `ACS.ReloadAcs`。

### 超宽横条分片

Unity/D3D11 单张贴图每边 ≤ 16384px，超过时原版解码直接失败（表现为空白 / NRE 刷屏）。
BCS 绕开 Unity 解码器自己 inflate + 反 filter，按**帧边界**切成若干 ≤16384 的贴图（像素零损失、
磁盘一个字节都不改），支持 8 位 RGBA / RGB / 调色板（PLTE + tRNS）；不支持隔行与 16 位色深——
那类图会告警一次并落安全态（回落种族图）。

---

## 控制台命令（ReflexCLI）

| 命令 | 作用 |
|------|------|
| `ACS.ListSkins` | 列出所有 Skin（含各自 ACS 片数） |
| `ACS.SetSkin <id>` | 设置主角 Skin |
| `ACS.ClearSkin` | 清除主角 Skin |
| `ACS.Emote [clip]` | 播一次表情；多片时不填则弹菜单 |
| `ACS.ReloadAcs` | 重载素材：皮肤表 / 静态 combat 映射 / 贴图缓存 |
| `ACS.MoveHysteresis <秒>` | NPC 移动粘滞窗口（默认 0.2s，0 = 复位） |
| `ACS.SpriteCache ...` | `on`/`off` / 宽限期秒数 / `0`=复位 / `flush` / `gc` |
| `ACS.KeepTextures on\|off` | 贴图跨重建复用（默认开） |
| `ACS.HotReload on\|off` | 皮肤热加载（默认关） |
| `ACS.FullPortrait on\|off` | 全身立绘兜底（默认开） |
| `ACS.PortraitLayout ...` | 立绘高度 / 偏移 / 左右对齐 |
| `ACS.DiagLog on\|off` | 诊断日志（性能探针同步开启） |
| `ACS.Perf [dump\|clear\|load <ms>\|frame <ms>]` | 看每张图被加载了几次、卡在解码还是上传 |

## 配置页

原版模组配置页（EA 23.350+ 的 ModConfig）会自动出现 BCS 的设置项，7 组 23 项：
**主角贴图 / 移动动画 / 对话立绘 / 贴图缓存 / 热重载 / 诊断 / 场景预热**。
bool → 开关、带范围的数字 → 滑杆，每行自带复位按钮。

配置页、控制台、手改配置文件共用同一份 `ConfigEntry`（`BcsConfig` 是唯一写入口），三者不会互相覆盖。

---

## 仓库结构

```
BetterCustomSprites/
├─ BetterCustomSprites.sln
├─ Directory.Build.props      # 引用 Elin / BepInEx 程序集
├─ global.json                # SDK 版本
├─ LICENSE
├─ README.md
├─ handoff.md                 # 会话交接：环境硬约束、当前形态、待办
├─ docs/
│  ├─ ARCHITECTURE.md         # 接口审计（重构前的定位报告）
│  ├─ workshop-description.md    # 创意工坊文案：中文
│  ├─ workshop-description.en.md # 创意工坊文案：English
│  └─ workshop-description.ja.md # 创意工坊文案：日本語
└─ src/
   ├─ BetterCustomSprites.csproj
   ├─ packages.lock.json
   ├─ package.xml             # Elin Package 元数据 + 游戏内描述
   ├─ AcsMod.cs               # BepInEx 入口、版本、Harmony.PatchAll、冲突检测、每帧驱动
   ├─ BcsConfig.cs            # 设置项（配置页的唯一写入口）
   ├─ API/
   │  └─ AcsSuffixes.cs       # 后缀键词汇：HasRealClip / CountClips / SplitClipName
   ├─ Frame/
   │  ├─ FrameChoice.cs       # 选片（GetAcsClip）+ 静态战斗图映射（bcs_static_map.ini）
   │  ├─ PlayState.cs         # 状态判定 / 前缀 / 各动作窗口与朝向锁
   │  └─ ClipClock.cs         # NPC·PC 共用帧时钟
   ├─ Skin/
   │  └─ SkinIndex.cs         # 皮肤查找唯一入口：id 归一 / 查表 / 编目补建
   ├─ Sprite/
   │  ├─ PngTiler.cs          # 托管 PNG 解码 + 帧几何 + 分片（纯解码，无缓存）
   │  └─ TiledSprite.cs       # 分片贴图 store 唯一入口：Get / 预热 / 释放 / 字节账
   └─ Patches/
      ├─ RenderFramePatch.cs  # NPC：CardActor 帧 → ACS clip
      ├─ PccRenderPatch.cs    # PC：CharaActorPCC 上 ACS 覆盖
      ├─ AcsSpritePresenter.cs# 缩放约定、底边对齐、bottom-pivot
      ├─ SkinLoadPatches.cs   # ListSkins / Reload / ACS 后缀规范化 / 超限分片入口
      ├─ PcSkinSupport.cs     # PC 绑 skin、创角/菜单换肤
      ├─ PcSkinBinder.cs      # PC 皮肤就绪：冷启动重试、ListSkins 后重绑
      ├─ PcSkinCommands.cs    # ACS.SetSkin / ClearSkin / ListSkins / Emote / ReloadAcs
      ├─ StatePatches.cs      # combat 粘滞 + magic/melee/hurt/revive/greet 触发
      ├─ SkinEditUi.cs        # 换肤列表 UI（分组、首帧图标、Texture 开关）
      ├─ SkinUi.cs            # LayerEditSkin 入口 + NPC 批量重绑
      ├─ GreetPrewarm.cs      # 切区域阻塞预热 + 按帧补扫
      ├─ SpriteCacheEvict.cs  # 按需释放 + 字节预算
      ├─ SkinTextureReuse.cs  # 重建皮肤表时按路径复用已解码贴图
      ├─ SkinHotReload.cs     # 皮肤热加载（默认关）
      ├─ FullPortraitPatch.cs # 对话全身立绘兜底
      ├─ PortraitAnimator.cs  # 对话动态立绘（序列帧）
      ├─ SkinPrefPatches.cs / AcsClipIni.cs / TuningCommands.cs
      ├─ ActEmote.cs / EmoteActPlanPatch.cs   # 表情动作 + 悬停按钮
      ├─ PeopleMenuPatch.cs / CreateCharSkinPatch.cs
      ├─ SpriteRefreshGuard.cs / TiledRectGuard.cs  # NRE 保险丝 / 分片 UV 兜底
      └─ Diagnostics/         # BcsPerf（按需加载/每帧耗时）、PrewarmProbe（预热分段）
```

本地目录（**不提交**，见 `.gitignore`）：`.workbuddy/`（会话记忆）、`.tools/`、`covers/`。

---

## 构建

### 环境

- [.NET SDK](https://dotnet.microsoft.com/download)（见 `global.json`，当前 8.0.x，可 rollForward）
- 环境变量 **`ElinGamePath`** 指向游戏根目录，例如：

```
E:\SteamLibrary\steamapps\common\Elin
```

需要：

```
ElinGamePath/
├─ BepInEx/core/*.dll
└─ Elin_Data/Managed/*.dll
```

### 编译

```powershell
dotnet restore src/BetterCustomSprites.csproj
dotnet build src/BetterCustomSprites.csproj -c Release
```

Release 开着 `TreatWarningsAsErrors`，必须 **0 警告 0 错误**。输出（由 csproj `OutputPath` 决定）：

```
%ElinGamePath%/Package/Mod_BetterCustomSprites/BetterCustomSprites.dll
```

并复制 `src/package/package.xml`。

---

## 安装

1. 编译，或从 Release 取包  
2. 确保目录为：

```
ElinGamePath/Package/Mod_BetterCustomSprites/
  BetterCustomSprites.dll
  package.xml
```

（若你使用 BepInEx plugins 布局，按自己的加载方式放置，并保证 `package.xml` 的 id 与 GUID 一致。）

3. 启动游戏

> ⚠️ 请先卸载原版 **Animated Custom Sprites**，否则会冲突（BCS 启动时会检测并弹窗提示）。

---

## 许可证

MIT — 原作者 DK（Elin.Plugins / AnimatedCustomSprites），本衍生作品同样 MIT。

详见 [LICENSE](./LICENSE)。
