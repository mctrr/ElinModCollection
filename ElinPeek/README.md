# ElinPeek — 窥视（Peek Around Corners）

受 CDDA 启发的 Elin Mod：PC 可以在拐角处窥视敌人，而不会被发现。
（CDDA-inspired peek mod: look around corners without being seen. /
CDDA 風の覗き見 Mod：角の先を身を隠したまま観察できます。）

## 功能
- 点选 PC 时，动作栏多出一个「窥视 / Peek / 覗き見」按钮（全动作菜单里也有）。
- 点击后世界冻结，把鼠标移向拐角方向即可“探出头”观察墙后的敌人/地形。
- 视野跟随鼠标实时换方向；鼠标回到 PC 身上 = 恢复正常视野。
- 右键 / Esc / 左键 退出窥视，恢复世界运行。
- 窥视不消耗回合、不移动角色，敌人无法发现你（墙挡视线 + 世界冻结）。
- 多语言：中文（默认）/ English / 日本語，按游戏语言自动切换。

## 安装 / 构建
- 需要 .NET SDK 与 `ElinGamePath` 环境变量（指向 Elin 游戏根目录）。
- `dotnet build` 自动输出到 `<ElinGamePath>\Package\Mod_ElinPeek\`。
- 游戏内 Mod 列表启用即可（需 BepInEx）。

## 文件
- `PeekManager.cs` 状态机 + 拐角原点算法
- `ActPeek.cs` 动作按钮
- `PeekText.cs` CN/EN/JP 多语言词典
- `Patches.cs` Harmony 补丁（Fov.Perform / AM_BaseGameMode.OnUpdateInput / AM_Adv._OnUpdateInput / Scene.OnUpdate / ActPlan.Update）

详见 `RESEARCH.md`（研究结论、机制、待验证项、扩展方向）。
