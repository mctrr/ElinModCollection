# Elin Mods

我（[mctrr](https://github.com/mctrr)）为 [Elin](https://store.steampowered.com/app/2135150/Elin/) 做的 BepInEx / Harmony mod 合集。

全部代码以 **MIT** 协议开放，见根目录 [LICENSE](LICENSE)。

## 目录

| 文件夹 | Mod | 一句话说明 | Package ID |
|---|---|---|---|
| [`BetterCustomSprites/`](BetterCustomSprites) | **Better Custom Sprites**（BCS） | 状态感知动画 + 动态换肤：`_acs_*` 状态片、idle 整条底图、PC/NPC 双路径渲染 | `mctrr.bettercustomsprites` |
| [`NpcLabor/`](NpcLabor) | **NPC 帮工**（NPC Labor） | 居民协同制作、加工机外包、区域 / 地牢派遣、城镇店铺雇工；内置 CN/EN/JP 文本 | `mctrr.npclabor` |
| [`ElonaBeLike/`](ElonaBeLike) | **Elona Be Like** | 野外行动整合包：右键小地图派队友守住 / 巡逻、战略经验、队伍共享视野、踢树结算 | `elonabelike.elonabelike` |
| [`Wetness/`](Wetness) | **湿润度**（Wetness） | 仿 Elona 的「湿润度」生存状态，与饥饿并列的一整套七档机制 | `mctrr.wetness` |
| [`Warfog/`](Warfog) | **战争迷雾**（Warfog） | 给北提里斯大陆地图盖上战争迷雾 | `mctrr.warfog` |
| [`ElinPeek/`](ElinPeek) | **窥视**（Peek Around Corners） | 受 CDDA 启发：在拐角后窥视敌人而不被发现 | `deux.peek` |

每个文件夹里都包含：C# 源码、`.csproj`、`package/`（`package.xml` + 本地化表 + 该 mod 用到的贴图）。各目录**互不依赖**，可以单独取出使用。

## 构建

每个 mod 是独立的 .NET Framework 4.7.2 工程，引用游戏本体的 DLL。把 `ElinGamePath` 指到你的 Elin 安装目录即可：

```bash
dotnet build BetterCustomSprites/src/BetterCustomSprites.csproj -c Release \
  -p:ElinGamePath='C:\Program Files (x86)\Steam\steamapps\common\Elin'
```

产物会直接输出到 `<Elin>\Package\Mod_<名字>\`，进游戏即可加载。

> BCS 的工程在 `src/` 子目录里，另外几个 mod 的 `.csproj` 就在各自文件夹根上。

## 关于

- 上游与致谢：**Better Custom Sprites** 衍生自 [Elin.Plugins / Animated Custom Sprites](https://github.com/gottyduke/Elin.Plugins/tree/master/AnimatedCustomSprites)（作者 DK，MIT），版权声明保留在 [LICENSE](LICENSE) 中。
- 本仓库不含游戏本体文件、不含反编译的游戏源码。
