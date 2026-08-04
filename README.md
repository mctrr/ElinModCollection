# Better Custom Sprites

[Elin](https://store.steampowered.com/app/2135150/Elin/) 的 **Animated Custom Sprites** 增强版：状态感知动画、主角（PCC）动态 Skin、问候与移动等扩展。

> 基于 [gottyduke/Elin.Plugins](https://github.com/gottyduke/Elin.Plugins) 中的 AnimatedCustomSprites，MIT 协议。  
> 仓库：https://github.com/mctrr/BetterCustomSprites

**请卸载原版 Animated Custom Sprites**，否则会冲突。

当前版本：`2.3.0`（`mctrr.bettercustomsprites`）

---

## 功能概览

| 能力 | 说明 |
|------|------|
| 状态动画 | combat / move(PC) / greet / wet / drunk / confused / blind / idle |
| 前缀 | `snow`、`married`、`acs_override` 等与状态组合 |
| 主角 Skin | PCC 可使用 Package/Skin 动态贴图；创角与居民菜单可打开原版换肤 UI |
| NPC ACS | 非 PCC 角色走原版 `CardActor` 帧替换管线 |

---

## 命名格式

```
_acs_{前缀}.{状态}#{帧间隔ms}_{起始帧}-{结束帧}
```

- **前缀**（可选）：外观修饰（雪地、已婚、手动 override 等）
- **状态**：`idle` 为默认，**可不写**；非默认状态需写出
- **帧**：`#66_0-5` → 66ms/帧，第 0～5 帧循环

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
| 1 | `combat` | 战斗 / 主动窗口 | PC：攻击或**装备主手**粘滞 5 回合（卸下不播）；可按主手拆 `combat.sword` 等 |
| 2 | `move` | **仅 PC** 移动中 | 资源名 `_acs_move` |
| 3 | `greet` | **仅 NPC** 见面问候 | 播完一轮回 idle |
| 4 | `wet` | 潮湿 / 曾在水中 | |
| 5 | `drunk` | 醉酒 | |
| 6 | `confused` | 混乱 | |
| 7 | `blind` | 失明 | |
| 8 | `idle` | 默认 | 文件名可省略状态 |

异常状态只在非战斗时生效；战斗中固定 `combat`。

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

### greet

- NPC 触发 `TalkTopic("fov")`（看见玩家气泡）后延迟约 0.2s 进入 greet
- 播完一轮自动清除；无 greet 资源则回退 idle

---

## 主角动态 Skin

- Skin 文件放在游戏 / Mod 的 **Skin** 目录（与原版一致）
- 支持 `基础名_acs_idle#…` 等形式；加载时会合并进基础 skin 的 suffixes
- **创建角色**面板底部有「更换贴图」入口（原版 `LayerEditSkin`）
- **居民列表**右键菜单对 PC 也可换肤
- 控制台（ReflexCLI）提供 `ACS.SetSkin` 等命令（见 `PcSkinSupport`）

拟态等特殊情况会跳过 ACS 覆盖，避免与 PCC 冲突。


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
      └─ StatePatches.cs      # combat 粘滞 + greet 触发
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

3. **卸载原版 ACS** 后启动游戏  

---

## 许可证

MIT — 原作者 DK（Elin.Plugins / AnimatedCustomSprites），本衍生作品同样 MIT。

详见 [LICENSE](./LICENSE)。
