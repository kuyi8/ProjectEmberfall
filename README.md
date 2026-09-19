# Project Emberfall（暂定名）

Unity **2022.3.62f3** 制作的 1–2 人合作**第三人称动作 RPG** 求职 Demo（Windows x64，键鼠 / Xbox 手柄）。

本仓库只包含**打开并运行工程所需的文件**：`Assets/`、`Packages/`、`ProjectSettings/`。
设计文档、验证记录、构建产物与试玩录像不在仓库内（构建产物通过 GitHub Release 分发）。

## 运行方式

1. 用 Unity Hub 添加本文件夹，以 **Unity 2022.3.62f3** 打开（首次导入需要一些时间）
2. 打开场景：
   - `Assets/_Game/Scenes/00_Bootstrap.unity` —— 应用入口（进入主菜单）
   - `Assets/_Game/Scenes/10_EmberValley.unity` —— 单人主线纵切（战斗、三处封印、Boss、结算）
   - `Assets/_Game/Scenes/90_CombatGym.unity` / `91_NetworkGym.unity` —— 战斗与联机测试场景
3. 按 Play

出 Windows 包：`File → Build Settings → Windows x64 → Build`。

## 操作（默认键鼠）

| 操作 | 按键 |
| --- | --- |
| 移动 / 镜头 | `WASD` / 鼠标 |
| 交互、处决 | `E` |
| 轻击（三连） | 鼠标左键 |
| 蓄力重击 | 按住鼠标右键 |
| 投掷飞刀 | `F` |
| 防御 / 精准防御 | `Q` |
| 闪避 | `Space` |
| 冲刺 | `Shift` |
| 治疗药 | `R` |
| 锁定目标 | 鼠标中键 |
| 操作说明 / 输入显示 / 暂停 | `F1` / `F2` / `Esc` |

## 技术要点

- **模块化架构**：8 个运行时程序集（Core / Application / Gameplay / AI / Quests / UI / Networking / Infrastructure），`Core` 不依赖 UnityEngine
- **确定性战斗**：状态机、敌人 Brain、破防与资源模型写在纯 C# 领域层，Unity 侧只做适配与表现
- **服务端权威联机**：Netcode for GameObjects（Listen Server）+ Unity Transport，AI / 伤害 / 任务 / 交互由服务器裁决
- **数据驱动与热更新**：JSON 内容 + 受限 xLua 门面，内容包使用 RSA-2048 / SHA-256 签名，支持暂存与回滚
- **自动化测试**：EditMode / PlayMode 测试与命令行门禁脚本

## 第三方素材

来自 KayKit（角色、地牢）、Quaternius（角色、动画）、Universal Animation Library 等公开素材包，各自目录内保留原始 `LICENSE.txt`。其余美术为项目内自建。

## 当前状态（诚实的进度说明）

单人主线纵切可完整跑通（接任务 → 三段封印 → 圣所 → Boss → 结算），双人联机路线已闭合到结算；目前仍在**扩充内容与打磨手感**（遭遇数量、选择与支线偏少，正在逐切片补齐）。
