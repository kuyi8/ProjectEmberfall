# Unity 免费美术资源包（已下载归档）

> 根目录：`UnityFreeAssets/`（约 1.5 GB，zip 均保留原件，解压目录可直接使用）
> 全部为 **CC0 免授权费、可商用**（Quaternius / KayKit / Kenney）；各包内自带 License 文件，随包保留即可。
> 风格：Quaternius 低多边形为主（角色/武器/环境/道具），KayKit CC0 为角色与地牢备选，Kenney 负责 UI 与音效。
> `_tools/` 内存放下载脚本（fetch.js / dl.js / manifest.txt / repos.js），以后加资源可复用。

## 目录内容

### 01_Warden_Boss —— Warden Boss 角色
- `Knight Character by @Quaternius/` — 重甲骑士 `KnightCharacter.fbx`（Humanoid 可自动识别，带基础动画）+ 换装件：Sword / ShortSword / Katana / Club / Helmet1-3 / ShoulderPads（FBX + Blend + OBJ）
- 二阶段：复制材质开 URP Lit Emission 即可；盾牌/符文剑见 04

### 02_Warden_Animations —— 动画库（Boss + 玩家共用）
- `UAL2_Standard/` — **Universal Animation Library 2**：130+ 条通用 Humanoid 动画，全部装在 `Unity/UAL2_Standard.fbx` 一个文件里（含 `Female Mannequin/Unity/Mannequin_F.fbx` 参考骨架）
  - 导入方法：fbx 拖入项目 → 选中后在 Model 面板提取/分配动画片段（Try 选择 Extract Clips，动画会像普通 Clip 一样使用）
- `UAL1_Standard/` — UAL 第一代（`Unity/AnimationLibrary_Unity_Standard.fbx`），保留作替补
- ⚠️ Root 位移：导入后逐个 Clip 检查 Root/Hips 曲线，In-Place 设置

### 03_Character_Kit —— 统一角色套件
- `RPG Characters - Nov 2020/` — Quaternius RPG 角色包（含法师/骑士类，60 文件）
- `Animated Human by @Quaternius/` — 动画人形基础体
- `Animated Monster Pack by @Quaternius/` — 动画怪物包（近战怪）
- `KayKit_Adventurers/` — KayKit 冒险者 4 角色（Barbarian/Druid/Engineer/Knight 类，`Assets/Characters/fbx`），含武器配件（斧/弩/匕首）
- `KayKit_Skeletons/` — KayKit 骷髅 4 个（Warrior/Rogue/Mage/Minion）—— **近战怪首选**
- 注意：KayKit 与 Quaternius 是不同低模风格，全套角色统一用一家

### 04_Weapons —— 中世纪武器
- `MedievalPack/` — 剑/盾/战斧/弓弩系（Blend + OBJ；需转 FBX 或直接导入 OBJ）
- `RPG Pack/` — **FBX 可直接用**：Shield / Staff / IceStaff / Dagger / Book / Scroll / 宝箱等（剑+法杖+盾一步到位）
- ⚠️ 握把 Pivot 需自查对齐；剑鞘无现成，用书卷/条状道具改或自制

### 05_Player_Animations —— （共用 02 的 UAL + Mixamo，空目录）

### 06_VFX —— （需 Unity Asset Store 手动下载，见文末清单）

### 07_Environment_Ruins —— 遗迹/地下圣所
- `Updated_Modular_Dungeon/` — Quaternius 模块化石质地牢（地面/墙/柱/拱门/楼梯/门，主用）
- `Medieval_Village_MegaKit/` — 中世纪村庄 MegaKit（木桥/栏杆/市政结构件，936 文件）
- `KayKit_Dungeon_Remastered/` — KayKit 地牢（200+ 构件，**备选，勿与 Quaternius 混搭**

### 08_HUD_UI —— （Kenney CC0）
- `kenney_ui-pack.zip` / `kenney_pixel-ui-pack.zip` / `kenney_fantasy-ui-borders.zip`
- `_Kenney_UI_解包内容/` — 已解包的 PNG（9-Slice/字体/精灵等）
- 键鼠/手柄按键图标：Kenney 官网 Game Icons 系（需另下，见手动清单）

### 09_Nature_Props —— 地表/植被/道具
- `Stylized_Nature_MegaKit/`（454 文件）、`Nature model pack/`（低模树灌木）、`Ultimate Nature Pack - Jun 2019/`（602 文件）— 植被三件套
- `Fantasy_Props_MegaKit/`（491 文件）— 宝箱/药瓶/书本/篝火道具
- `Survival Pack - Sept 2020/`（214 文件）— 帐篷/篝火/露营
- `Ultimate RPG Items Pack - Aug 2019/`（433 文件）— 药水/卷轴/背包等
- `Kenney_NatureKit/`（3618 文件）— 石/地形/植被贴图（CC0）
- 烧痕/符文 Decal：自制贴图 + URP Decal Projector

### 10_Audio —— （Kenney CC0）
- `ImpactSounds/`（133 文件）— 金属/钝器/命中/破碎
- `RPGAudio/`（55 文件）— 法术/战斗/环境类
- `InterfaceSounds/`（103 文件）— UI 点击/提示

---

## 需要手动操作的清单（需账号登录，无法脚本化）

### 1. Unity Asset Store —— 全部战斗 VFX（重点）
登录 https://assetstore.unity.com → 搜下列名称 → **Add to My Assets**（确认价格为免费）→ 在 Unity 编辑器 **Window > Package Manager > My Assets** 下载导入：
| 资产 | 用途 |
|---|---|
| Slash Effects FREE | 斩击刀光（含符文斩） |
| Magic Effects FREE | 治疗/弹道粒子 |
| Magic Mandala VFX (FREE) | 地面法阵（地符/转阶段地板） |
| Easy Shockwaves VFX - URP | 冲击波（盾击/完美格挡） |
| Free Fire VFX - URP | 爆炸/火焰 |
| Human Melee Animations FREE | （可选）剑击手感动画 |
| Free Low Poly Dungeon Pack | （可选）另一个地牢包 |

### 2. Mixamo —— 盾击/举盾受击/冲锋/四向受击等专项动画（重点缺口）
https://www.mixamo.com 登录 Adobe 账号 → 搜 shield / block / attack / hurt / roll / death → **Download 选 FBX for Unity** → 拖入项目自动 Humanoid。
UAL2 中没有的盾牌相关动作靠它补。

### 3. 可选补充
- **Sonniss GDC 2024 音效包**（27GB，免费商用）：https://sonniss.com/gameaudiogdc 手动下载，一次性补齐脚步/环境循环/乐句；不想要大包就用已下的 Kenney + Freesound（需账号）散补。
- **Kenney 键鼠/手柄按键图标**：https://kenney.nl 搜「Game Icons / Input Prompts」（CC0，浏览器直接下，无账号）。

### 4. 需要自制/改动的（免费库无现成）
- 盾牌破碎碎片（用 MedievalPack 盾拆 2-4 块）
- 剑鞘（简单几何体 + 皮带扣）
- 剑刃拖尾（Trail Renderer + 自写 shader）、死亡溶解（Shader Graph）
- 符文 Decal / 烧痕贴图

## 使用提醒
1. Unity 导入优先 `.fbx`（03/04 中 OBJ/Blend 只能用 Blender 中转或直接导 OBJ）。
2. 武器 Pivot：统一在 Blender 里对齐握把中心后再导出。
3. UAL2 动画 fbx：导入后按「提取片段」拆成独立 Clip 再用。
4. URP 14：粒子包下载后跑一次《可将内置管线升级为 URP》自动转换。
