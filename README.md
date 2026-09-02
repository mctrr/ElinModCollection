# Better Custom Sprites

[Elin](https://store.steampowered.com/app/2135150/Elin/) 的 **Animated Custom Sprites** 增强版：状态感知动画、主角（PCC）动态 Skin、问候与移动等扩展。

> 基于 [gottyduke/Elin.Plugins](https://github.com/gottyduke/Elin.Plugins) 中的 AnimatedCustomSprites，MIT 协议。  
> 仓库：https://github.com/mctrr/BetterCustomSprites


当前版本：`2.5.0`（`mctrr.bettercustomsprites`）

---

## 功能概览

| 能力 | 说明 |
|------|------|
| 状态动画 | revive / emote / mcombat / combat / move / greet(greet1-4) / eat / wet / drunk / confused / blind / idle |
| 前缀 | `snow`、`married`、`acs_override` 等与状态组合 |
| 主角 Skin | PCC 可用 Skin / Texture 根目录贴图；创角与居民菜单可打开换肤 UI |
| NPC 换肤 | 居民菜单可给 NPC 换肤；自定义皮肤会播 ACS，不再停在静态底图 |

---

## 命名格式

```
_acs_{前缀}.{状态}#{帧间隔ms}_{起始帧}-{结束帧}
```

- **前缀**（可选）：外观修饰（雪地、已婚、手动 override 等）
- **状态**：`idle` 为默认，**可不写**；非默认状态需写出
- **帧**：`#66_0-5` → 66ms/帧，第 0～5 帧循环
- **无底图**：NPC 表仍填短 id（`pcr_aimeisi`）。只放 `pcr_aimeisi_acs_idle#66_0-0.png` 时，BCS 会把它当作底图，原版 `HasSprite` 也能认

### 前缀（`GetPrefix`，高 → 低）

| 优先级 | 前缀 | 条件 | 示例 |
|--------|------|------|------|
| 1 | `acs_override` | Drama / `mapStr` 手动指定 | `_acs_skin.combat#66_0-5` |
| 2 | `married` | 已婚相关 | `_acs_married.idle#66_0-3` |
| 3 | `snow` | 站在雪地 | `_acs_snow#66_0-5` |
| — | 无 | 默认 | `_acs_wet#66_0-5` |

### 状态（`GetState`，高 → 低）

| 优先级 | 状态 | 条件 | 备注 |
|--------|------|------|------|
| 1 | `revive` | **被复活**（`Chara.Revive` 真正从死亡复活） | PC/NPC 通用；一次性动作，播完一轮回其它状态 |
| 2 | `emote` / `emote.*` / `greet` | **PC 点表情按钮**（当前皮肤有 emote 或 greet 片才出现） | 一次性动作；多片时弹菜单自选；移动会打断，战斗中点按钮仍会播 |
| 3 | `mcombat` | **施放敌对法术**（魔法攻击）后粘滞 5 回合 | PC/NPC 通用；优先级高于 `combat`；缺 `_acs_mcombat` 片回退 combat |
| 4 | `combat` | 战斗 / 主动窗口 | PC：攻击或**装备主手**粘滞 5 回合（卸下不播）；可按主手拆 `combat.sword` 等 |
| 5 | `move` | **PC / NPC** 移动中 | 资源名 `_acs_move`；PC 用轴/寻路粘滞，NPC 看 `CharaRenderer.isMoving` |
| 6 | `greet` / `greet1`-`greet4` | **仅 NPC** 见面问候 | 触发时在已有的 `greet` / `greet1`-`greet4` 中随机锁一张；播完一轮回 idle |
| 7 | `eat` | 进食中（PC/NPC） | 自动检测 `AI_Eat` 吃进度；循环播放，吃完/取消自动回 idle |
| 8 | `wet` | 潮湿 / 曾在水中 | |
| 9 | `drunk` | 醉酒 | |
| 10 | `confused` | 混乱 | |
| 11 | `blind` | 失明 | |
| 12 | `idle` | 默认 | 文件名可省略状态 |

异常状态只在非战斗时生效；战斗中固定 `combat`（施法窗口则固定 `mcombat`）。`emote` 高于战斗，所以按钮点下去一定能看到表情；走动会取消。

### 查找顺序

```
1. _acs_{前缀}.{状态}   最精确
2. _acs_{前缀}          该前缀的通用片（视为 idle）
3. _acs_{状态}          无前缀状态片
（都没有则保持原版外观）
```

**combat 武器子键**（可选；只做一套战斗动画时仍用 `_acs_combat`）：

```
_acs_combat.sword / .polearm / .staff   ← 主手类型（skill 101/106/103）
_acs_combat                             ← 通用 / 单套 / 其它武器兜底
```

有前缀时：`_acs_snow.combat.sword` → `_acs_snow.combat` → 无前缀同上。  
只看**主手装备**，不看本次攻击方式。装备/更换主手会开 5 回合 combat；从主手卸下不播。

非 idle 缺片时会回退到对应 idle，避免整段 ACS 失效。

### mcombat（魔法攻击）

- 施放**敌对法术**（`Spell` 且原版 `abilityType` 为 attack/debuff/dot，即打敌人的魔法）后，
  combat 状态被 `mcombat` 覆盖 **5 回合**：优先播 `_acs_mcombat`，优先级高于 `_acs_combat`
- 判定走原版统一施法入口 `Chara.UseAbility(Act,…)`，PC（法术栏 / 快捷键）与 NPC（战斗 AI）都生效；
  治疗 / 增益 / 位移等非攻击法术**不**触发
- 持续施法会续期；停止施法 5 回合后自动回落到普通 `combat`（战斗中）或 `idle`
- 资源命名：`_acs_mcombat#66_0-5`；前缀同样支持（`_acs_snow.mcombat`、`_acs_married.mcombat`）；
  缺 `_acs_mcombat` 片时按 `_acs_combat` 选片回退（含主手武器子键）
- 测试 / 强制：`AcsStateResolver.ForceMagicCombat / ClearMagicCombat`
- PC 施法朝向：敌对法术成功后按目标相对格子的屏幕水平方向转身（右上/右下朝右，左上/左下朝左；纯上下不改）。ACS 不读原版 `angle`（等距 W 会误翻），所以单独锁朝向；键盘 A/D 仍优先，`mcombat` 窗口结束自动清除

### emote（表情）

- 当前 PC 皮肤有 `_acs_emote` / `_acs_emote.*` / `_acs_greet` / `_acs_greet1`-`_acs_greet4` 时，悬停自己会出现「表情」按钮（左键 HUD / 全部动作菜单；默认中键常绑定全部动作）
- 点击后播一次，不消耗回合；多片时弹菜单自选（`emote` / `emote.wave` / `greet` / `greet1`-`greet4` 同等参与）
- 资源名：`_acs_emote#66_0-5`、`_acs_emote.wave#66_0-8`、`_acs_greet#66_0-8`；前缀同样支持（`_acs_snow.emote`、`_acs_yongzhuang.emote.wave`）
- 没有该片时不显示按钮；播到一半走动会打断
- 控制台：`ACS.Emote`

### revive

- **判定**：Harmony 补丁打在原版 `Chara.Revive(Point, bool)`。Prefix 记下调用前 `isDead`，Postfix 仅当「调用前已死、调用后活着」才 `StartRevive`
- 因此会覆盖所有真正复活入口（居民复活、`GetRevived`、区域自动复活、PC 死亡后爬起等），**不会**对活人空调用 `Revive` 误触发
- 一次性动作，优先级最高（压过 `mcombat` / `melee` / `combat` / `move`）；播完最后一帧停在末帧并清标记，不绕回第 0 帧
- 资源命名：`_acs_revive#66_0-5`；前缀同样支持（`_acs_snow.revive`、`_acs_married.revive`）
- 无 revive 资源则立刻清标记并回退其它状态，不会卡住

### greet

- NPC 触发 `TalkTopic("fov")`（看见玩家气泡）后延迟约 0.2s 进入 greet
- 触发时在当前皮肤实际存在的 `_acs_greet` / `_acs_greet1` … `_acs_greet4` 中随机锁一张，播完前不会换片
- 播完一轮自动清除；无 greet 资源则回退 idle

- PC 也可把这些 greet 片当表情播（见 emote）
### eat

- 自动检测原版 `AI_Eat` 的「吃」进度子行为：`ai is AI_Eat` + 当前子行为是进度（`AIProgress`）+ 手持食物
- 做菜 / 去餐桌阶段不会误触（做菜时食物在灶上 `held` 为空；走路是 `AI_Goto`）
- 吃完 / 吃撑取消 / 被打断（移动、受伤）→ 进度结束，下一帧自动回 idle，无需手动 `StopEat`
- 资源命名：`_acs_eat#66_0-5`；前缀同样支持（`_acs_snow.eat`、`_acs_married.eat`）
- 手动 override（测试 / 外部调用）：`AcsStateResolver.StartEat / StopEat / IsEatActive`
- 全身 ACS 叠在 PCC 脚底锚点上，原版手持点会落在脚边。可在同名 `.ini` 里加像素偏移（100 ppu，朝右时 `heldX` 自动镜像）：

```ini
frame=8
time=0.041
scale=100
heldX=0
heldY=40
```

  `heldY=40` 大约上移 40 像素。改完后控制台 `ACS.ReloadAcs` 即可，不必重开游戏。原版只认 `frame` / `time` / `scale`；`heldX` / `heldY` 是本 mod 扩展，加载时会保留，不会被覆盖。


---

## 主角 / NPC 动态 Skin

- 文件可放在游戏 / Mod 的 **Skin** 目录（与原版一致）
- 也可放在 **Texture 根目录** 或 `Texture/Skin`（不递归 `Texture/Item` 等子树，避免把物品图扫进换肤列表）
- 支持 `基础名_acs_idle#…` 等形式；加载时会合并进基础 skin 的 suffixes
- 可以不放 `基础名.png`：只留 `基础名_acs_idle#66_0-0.png` 时，换肤列表和 NPC 表短 id 都认这张 idle
- **创建角色**面板底部有「更换贴图」入口（原版 `LayerEditSkin`）
- 换肤窗口默认只列 Skin 目录；底部开关打开后才显示 Texture 贴图（列表虚拟滚动，可见项才加载 PNG）
- Texture 来源会在名字后标 mod 名 / Custom，方便区分
- **居民列表**右键：PC、PCC NPC、以及原版已支持的非 PCC NPC 都可换肤
- NPC 手动换肤后会走自定义皮肤的 ACS（战斗 / move / greet / idle 等），不再只显示静态底图
- 控制台（ReflexCLI）：`ACS.SetSkin` / `ACS.ClearSkin` / `ACS.ListSkins` / `ACS.ReloadAcs` / `ACS.Emote`

拟态、以及原版图集变身/强化（`RendererReplacer` 且无 PCC）会跳过 ACS 覆盖，避免叠到原版 tile 上。


---

## 静态 Combat Skin

战斗时可用**单帧静态图**替换外观（无多帧 ACS combat 时生效；有 `_acs_combat*` 动画时仍优先动画）。

### 优先级

1. 规范文件：`{base}_bcs_combat.png`（加载时合并进 base 的 `suffixes["_bcs_combat"]`）
2. 映射表：`Skin/bcs_static_map.ini` 的 `[combat]`（仅当该 base 还没有 `_bcs_combat`）
3. 都没有 → 无静态 combat

### 映射表（兼容其它 mod 命名）

把文件放在下列任一位置（可同时存在；同一 base 以**先解析到的**为准，Custom 优先）：

- **Custom/Skin/bcs_static_map.ini**
- **已启用 Package 的 Skin/bcs_static_map.ini**（纯 ini 兼容包也行，不必带 png）
- 其它已加载 skin png 所在目录下的同名 ini

```ini
[combat]
baseSkinId=foreignSkinId
; 预留（当前忽略，以后可扩展）:
; [snow]
; [wet]
```

- `baseSkinId` / `foreignSkinId`：无扩展名，与 `dictSkins` 的 key 一致（png 文件名去掉 `.png`）
- 同一 base 多行：保留首次，其余跳过并打日志
- 无 base 底图时：用 foreign 图登记 base，并挂上 `_bcs_combat`（换肤列表可见）
- 有 base 时：只写入 `_bcs_combat`，**不删除** foreign 独立 skin 条目
- 持武多套图：在 map 里显式指定**一张**代表图即可（静态侧不按武器拆键）

`ACS.ReloadAcs` 会失效映射缓存并重新 `ListSkins`。

---

## 仓库结构

```
BetterCustomSprites/
├─ BetterCustomSprites.sln
├─ Directory.Build.props      # 引用 Elin / BepInEx 程序集
├─ global.json                # SDK 版本
├─ LICENSE
├─ README.md
└─ src/
   ├─ BetterCustomSprites.csproj
   ├─ packages.lock.json
   ├─ AcsMod.cs               # BepInEx 入口、版本、Harmony.PatchAll
   ├─ package/
   │  └─ package.xml          # Elin Package 元数据
   ├─ API/
   │  ├─ AcsApi.cs            # AcsClip 解析 + AcsController 选片
   │  ├─ AcsStateResolver.cs  # 状态 / 前缀 / combat·greet 标记
   │  ├─ AcsSuffixes.cs       # suffixes 查询与 Resolve
   │  └─ StaticCombatMap.cs  # Skin/bcs_static_map.ini [combat]
   └─ Patches/
      ├─ RenderFramePatch.cs  # NPC：CardActor 帧 → ACS clip
      ├─ PccRenderPatch.cs    # PC：CharaActorPCC 上 ACS 覆盖
      ├─ AcsSpritePresenter.cs# 缩放约定、底边对齐、bottom-pivot
      ├─ SkinLoadPatches.cs   # ListSkins / Reload / ACS 后缀规范化
      ├─ StaticCombatMapApply.cs # apply map -> _bcs_combat
      ├─ PcSkinSupport.cs     # PC 绑 skin、创角/菜单换肤、命令
      ├─ StatePatches.cs      # combat 粘滞 + greet 触发 + revive
      ├─ ActEmote.cs          # PC 表情按钮（一次性 ACS）
      └─ EmoteActPlanPatch.cs # 悬停自己时注入表情按钮
```

本地目录（**不提交**，见 `.gitignore`）：

- `.tools/` — 脚本与临时工具
- `.research/`、`.tmp_*` — 反编译与研究缓存

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

输出（由 csproj `OutputPath` 决定）：

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
---

## 许可证

MIT — 原作者 DK（Elin.Plugins / AnimatedCustomSprites），本衍生作品同样 MIT。

详见 [LICENSE](./LICENSE)。
