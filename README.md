# AyaHitmarker

[![CI](https://github.com/ChenRuoT/AyaHitmarker/actions/workflows/ci.yml/badge.svg)](https://github.com/ChenRuoT/AyaHitmarker/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/ChenRuoT/AyaHitmarker)](https://github.com/ChenRuoT/AyaHitmarker/releases/latest)

为 **SPTushonka / Escape From Tarkov 1.2.0.0（build 47965，IL2CPP）** 提供命中标记、伤害数字、击杀卡片和音效。
击杀反馈参考 Battlefield 6：窗口内连杀累加到同一张卡片，支持得分滚动、多杀横幅、成就、军衔徽章和故障动效。

## 下载与安装（玩家）

1. 打开 [最新 Release](https://github.com/ChenRuoT/AyaHitmarker/releases/latest)，下载 **AyaHitmarker-v版本号-EFT-1.2.0.0.zip**。GitHub 的 Source code 是源码，不是安装包。
2. 关闭游戏，把 ZIP 中的 **BepInEx** 文件夹解压到 **EscapeFromTarkov.exe 所在目录**。更新时允许覆盖同名文件。
3. 通过 SPT 启动器启动游戏。图标、16 个军衔徽章和四种音效已包含，无需额外下载资源。

解压后应当是：

```text
游戏根目录/
├─ EscapeFromTarkov.exe
└─ BepInEx/plugins/AyaHitmarker/
   ├─ AyaHitmarker.dll
   ├─ Icons/                 # 基础图标 + RankIcons/ 的 16 个军衔徽章
   ├─ Sounds/                # 命中、爆头命中、击杀、爆头击杀
   └─ 安装说明.txt
```

**兼容环境：SPTushonka 5.x、EFT build 47965、BepInEx 6 IL2CPP / CoreCLR 10。**
BepInEx 与运行时由游戏整合包提供；玩家无需安装 .NET SDK。
旧 **SPT 4.x / Mono** 请使用 [SimpleHitMarker](https://github.com/ChenRuoT/SimpleHitMarker)。

使用游戏内 **ConfigurationManager** 调整开关、位置、颜色、动画和音量；细项在 **Advanced / 高级设置** 中。
首次运行后也可关闭游戏，编辑 `BepInEx/config/com.aya.ayahitmarker.cfg`。
更新保留 cfg，程序会自动迁移仍为旧默认值的样式项；卸载只需删除 `BepInEx/plugins/AyaHitmarker/`。

## 常见问题

- 没有文字或中文显示成方块：默认用的是游戏自带字体，它的回退链已包含中文，**字体名称请保持留空**。在当前游戏版本上系统字体（包括 `Microsoft YaHei`）无法转成 TMP 字体，填了也会自动退回游戏字体。仍有问题时，请把日志里 `[ui] 字体 … 中文字形` 那一行连同日志一起反馈。
- 没声音：检查音效开关和音量；空间模式默认是 `FollowCamera3D`（唯一在真实战局里确认过出声的模式），可以切到 `Flat2D` 对比。首次加载音效可能稍有延迟。
- 安装后没有反馈：确认 DLL 的位置与上图一致，目录不能多套一层版本名。
- `F8 / F9 / F10` 是测试命中、击杀和阵亡的快捷键，主菜单也可使用；可以在设置中关闭。
- 报告问题时请附游戏版本、插件版本和 `BepInEx/LogOutput.log` 中与 AyaHitmarker 相关的日志。提交前移除日志中的个人或认证信息。

[问题反馈](https://github.com/ChenRuoT/AyaHitmarker/issues) · [发布维护流程](docs/RELEASING.md) · [版本说明](docs/releases/0.5.3.md) · [路线图](docs/ROADMAP.md)

## 致谢与许可

本项目是 [ChenRuoT/SimpleHitMarker](https://github.com/ChenRuoT/SimpleHitMarker) 的 IL2CPP 后继项目。
沿用旧项目致谢，感谢 [kmyuhkyuk](https://github.com/kmyuhkyuk)、[Amands2Mello](https://github.com/Amands2Mello)
和 [qianyelinghua](https://github.com/qianyelinghua) 的 EFT 模组带来的启发。
视觉设计参考 Battlefield 6；感谢 SPT / SPTushonka、BepInEx、Il2CppInterop 和 TextMeshPro 的开发者。
项目代码采用 [MIT](LICENSE.txt)，随包资源来源见 [资源与第三方说明](THIRD-PARTY-NOTICES.md)。

---

## 开发记录与实测结论

SPTushonka / Escape From Tarkov **1.2.0.0 (build 47965)** 的命中反馈插件：**命中提示 + 伤害数字 + 击杀卡片 + 音效**。
命中 / 伤害数字的排布与时序按旧 Mono 项目 [`SimpleHitMarker`](https://github.com/ChenRuoT/SimpleHitMarker) 的逻辑移植；
**0.4.0 起击杀卡片与命中标记按 战地 6 实录重做**：
一张**累加式**击杀卡（窗口内连杀并到同一张卡上）+ 色块显影 / 数字滚动 / 错峰闪烁熄灭的「故障」动效 +
多杀横幅与骷髅徽记 + 左侧放大的军衔徽章；命中标记加了深色描边（白底上不再消失）和红色的击杀扩散版。

> 目标是替代旧 Mono 版 [`SimpleHitMarker`](https://github.com/ChenRuoT/SimpleHitMarker)（SPT 4.1，Mono）。
> 迁移到 IL2CPP 后旧流程整体失效，本文档记录的是**实测过**的结论。
>
> 维护者请先读本地工作入口 AGENTS.md；发布维护见 [docs/RELEASING.md](docs/RELEASING.md)。

---

## 0. 开发者构建入口

```powershell
cd D:\EFTdev\dev\AyaHitmarker
dotnet build                                   # 0 warning / 0 error，通过后自动部署
dotnet run --project tests/AyaHitmarker.LogicTests   # 离线逻辑测试（456 项）
dotnet run --project tests/AyaHitmarker.LogicTests -- --render-icons D:\icons   # 把程序化图标渲染成 PNG 肉眼看
```

进游戏以后按 `F8` / `F9` / `F10` 就能看到各种击杀反馈（无需真的打架）：
`F9` 每次轮换一种地形（PMC 爆头远程 → SCAV 近战 → 首领爆炸 → 连杀 …），连按几次就能看到
**双杀 / 三杀 / 四杀 / 五杀的累加卡片**、各档军衔徽章和红色的击杀标记。

---

## 1. 环境

| 项 | 实测值 |
|---|---|
| 游戏 | `D:\EFTdev\Client.1.2.0.0.47965` |
| 脚本后端 | IL2CPP（`GameAssembly.dll` 105 MB + v31.1 metadata 27 MB） |
| BepInEx | `6.0.0-be.4`（IL2CPP 分支，Unity Doorstop `winhttp.dll`） |
| 托管运行时 | CoreCLR **10.0.12** → 插件 TFM 必须 **`net10.0`** |
| Unity | 2022.3.43f2 |
| 本机 SDK | `dotnet 10.0.400` |
| 补丁框架 | `SPTushonka.Reflection` 的 `ModulePatch`（`BepInEx/plugins/sptushonka/`） |
| UI | uGUI + TextMeshPro |

## 2. 构建 / 部署

```powershell
cd D:\EFTdev\dev\AyaHitmarker
dotnet build
```

构建通过后自动把 `AyaHitmarker.dll` 拷到 `BepInEx\plugins\AyaHitmarker\`。

- 游戏路径默认 `D:/EFTdev/Client.1.2.0.0.47965`，在 `Directory.Build.props` 的 `GameDir`。
- 覆盖方式：`Directory.Build.local.props`（已 gitignore）或 `dotnet build -p:GameDir="E:/OtherEFT"`。
- 只编译不部署：`dotnet build -p:DeployToGame=false`。

### 离线测试（不需要游戏）

```powershell
dotnet run --project tests/AyaHitmarker.LogicTests
# ALL PASS (456 checks) → 退出码 0；有失败逐条打印 expected/actual → 退出码 1
```

测的是**可判定的行为契约**：哪次击杀该出哪条缎带、得分怎么加、连杀进度到哪一档、卡片行序与间距、
窗口内第二杀是并卡还是新卡、闪烁曲线的起止与硬切、程序化图标的像素级位置。
测不了「好不好看」—— 图标用 `--render-icons` 渲染成 PNG 看，动画只能进图看（见 §11 的 DebugHarness）。
详见 [`tests/AyaHitmarker.LogicTests/README.md`](tests/AyaHitmarker.LogicTests/README.md)。

> 根目录 `AyaHitmarker.csproj` 里有 `<Compile Remove="tests/**" />`。
> 删掉它，插件的默认 glob 就会把测试的 `Program.cs` 也编进去（重复 `Main`）。

## 3. 架构

```
事件线程                                    主线程（AyaBehaviour.Update）
─────────                                   ──────────────────────────────
GlobalEventDispatcher.OnBeingHit ─┐
Player.OnPlayerDeadStatic ────────┤         FeedbackBus.Drain
   （击杀 / 我方阵亡各一路）      ├──► 冻结成 HitRecord / KillRecord ──► HitmarkerLayer      (Icons/hit.png)
                                  │    / LocalDeathRecord               DamageNumberLayer
                                  │    （只传值类型和字符串，          │  KillStreakTracker   → KillAnalysis
                                  │      不碰任何 Unity API）           └► KillFeedLayer      (击杀卡片)
                                                                          SoundPlayer        (Sounds/*.ogg)
```

> **击杀播报的文字层曾经完全画不出来。** 旧实现用一个「读 GameObject.activeSelf」的办法判断
> 文字层是否可见，而「把 GameObject 设为 active」这件事本身发生在绘制函数里 ——
> 首次击杀时判断恒为 false，绘制函数永远不会被调用，于是名字 / 阵营 / 详情 / 军衔徽章全部是空的
> （只有骷髅能出来）。现在改成一个显式状态位 `_textVisible`，不再从 GameObject 状态反推。

**击杀卡片的元素与动效**（详见 §9）：成就横幅（+ 多杀骷髅徽记）/ 骷髅行 + 累计得分 / 连杀进度条 /
阵营 + 名字 / 标签（击杀 · 爆头 · 近战 · 距离）/ 详情，左侧是放大的军衔徽章。
卡片本身是**累加模型**（`Ui/KillCardModel.cs`，纯逻辑可离线测），动效曲线在 `Ui/GlitchMath.cs`（纯数学），
布局在 `Ui/KillCardLayout.cs`（纯数学），`Ui/KillFeedLayer.cs` 只负责把模型 → 布局 → 逐帧编排画到 uGUI 上
（0.6 起横幅 / 骷髅行 / 标签行 / 军衔徽章已拆成 `KillCardBannerView` / `KillCardSkullRowView` /
`KillCardTagRowView` / `KillCardBadgeView`，共用动画参数 `KillCardAnim` 与配色 `KillCardPalette`）。
动画写 `localScale`（不触发 TMP 重排）与 `anchoredPosition`；**淡入淡出走 `CanvasGroup.alpha`**（同样不触发重建），
所以每帧都在变的故障闪烁没有代价；尺寸 / 字号只在出生时写一次。
所有 UI 动画读 `UiClock`（正常游戏里就是 `Time.unscaledTime`），调试脚本借它手动步进，逐帧截图。

**资源加载**：
- `Icons/` 的 PNG 用 `ImageConversion.LoadImage` 同步读（启动时一次）。
- 环形特效纹理与辉光纹理**运行时烘**（`Assets/IconBank.cs`）：环形按 `厚度/半径` 分桶缓存；
  辉光把旧项目「8+8+1 次偏移叠加」的数学在 CPU 上烘成一张带外边距的纹理 —— 少 16 次绘制，视觉等价。
- `Sounds/` 的 ogg/wav/mp3 走 `UnityWebRequestMultimedia` **协程异步**解码
  （`BepInEx` 的 `WrapToIl2Cpp()` 桥接托管迭代器），主线程全程不阻塞。
  **没有程序化合成音**，也没有 `Thread.Sleep` 轮询。

- **只有一个注入的托管类型**（`Behaviour/AyaBehaviour.cs`）。`BasePlugin` 不是 MonoBehaviour，
  所以由 `Plugin.Load()` 用 `AddComponent<T>()` 注入。其余 UI 全是 interop 自带组件。
- **零补丁，因此不依赖 `SPTushonka.Reflection`。** 命中和击杀全是事件订阅；
  战局边界靠轮询主玩家（`GameWorld.MainPlayer.Profile`）出现/消失检测；`OnBeingHit` 每局开局重绑
  （实测 `GlobalEventDispatcher` 实例跨局不变，换局时旧订阅会被游戏清掉）。为什么连框架都不引：见 §7。
- 音效从 `Sounds/` 异步加载（见上），播放路径里**绝不触发加载**，没加载完就静默不响。

## 4. 实测结论：命中 / 击杀到底该挂哪儿

一局战斗各事件源的触发统计（2026-10-10）：

| 事件源 | 次数 | 判决 |
|---|---|---|
| `GlobalEventDispatcher.OnBeingHit` | 61 | ✅ **采用**。每命中恰好 1 条，`DidBodyDamage` / `DidArmorDamage` 已算好，`aggressorIsLocal` 正确 |
| `Player.OnPlayerDeadStatic` | 11 | ✅ **采用**。与 `GlobalEventDispatcher.OnKill` 同刻同数，但多带 `DamageInfo` + `EBodyPart` |
| `GlobalEventDispatcher.OnKill` | 11 | ⚪ 弃用（信息更少，会和上面重复计数） |
| `Player.ApplyShot` | 104 | ❌ 一发子弹触发多条；19/104 条 `dmg=0`（鞭尸）；时序还在死亡事件之后 |
| `Player.ApplyDamageInfo` | 15 | ❌ 只在**护甲板**路径触发（全部 `collider=RibcageUp` + `armorDmg>0`），且 postfix 里 `DidBodyDamage` 恒为 0 |

### 鞭尸（射击尸体）为什么会出现、怎么解决

打尸体时上述事件**照常触发**，只是 `DidBodyDamage == 0 && DidArmorDamage == 0`：

```
[probe:GlobalBeingHit] victim=Заур Молния aggressorIsLocal=True bodyDmg=0 armorDmg=0
```

过滤条件就是 `DidBodyDamage + DidArmorDamage > 0.01f`
（正是旧 Mono 项目 `DamagePatch.cs` 里那句注释的等价物）。一局 61 条里有 6 条属于这种。

### 为什么 `ApplyDamageInfo` 的 `DidBodyDamage` 是 0

`EFT.Ballistics.DamageInfo` 在 IL2CPP 侧是 struct，但在 **interop 侧被生成成类**
（基类 `Il2CppSystem.ValueType`，字段通过 `NativeFieldOffset_*` 访问）。
Harmony 把按值传的结构体 marshal 成托管对象时，postfix 拿到的是**调用前的副本**，
所以方法体内写入的 `DidBodyDamage` 在 postfix 里看不到（`DidArmorDamage` 因为是在更早的
`ProceedLocalAbsorbedDamage` 里写的，所以能看到）。

## 5. 5.0 (IL2CPP) vs 4.1 (Mono) 差异清单

| 4.1 Mono | 5.0 IL2CPP |
|---|---|
| `Managed/Assembly-CSharp.dll` 可直接反编译 | 无此文件；用 `BepInEx/interop/*.dll` + `dump.cs` |
| Harmony IL 补丁 / Transpiler / ReversePatch | 只有 Prefix / Postfix / Finalizer；ReversePatch → `Il2CppBaseCall` |
| `Player.Init` 后订阅每个玩家的 `BeingHitAction` | **`Player.Init` 已不存在**；改用全局事件（见 §4） |
| `DamageInfo` 是普通 struct | interop 里是类，可能为 `null` |
| `using SPT.Reflection.Patching` | `using SPTushonka.Reflection.Patching` |
| BCL 就是普通 `System.*` | 游戏侧是 `Il2CppSystem.*` / `Il2Cppmscorlib` 影子程序集 |
| IMGUI (`OnGUI`) | 本项目改用 uGUI + TMP |

## 6. 配置

`BepInEx/config/com.aya.ayahitmarker.cfg`（或游戏内 ConfigurationManager）：

| 分组 | 内容 |
|---|---|
| `0. 调试` | 诊断日志开关、`F8` 测试命中（**按住 Shift = 模拟只打中护甲**）/ `F9` 测试击杀（轮换 8 种地形）/ `F10` 模拟阵亡、配置版本（内部用，勿改） |
| `1. 命中提示` | 开关、基础大小、显示时长、动画缩放、弹出比例/时长、中心偏移、着色、爆头着色与尺寸倍率、显示在命中点（默认关）；**0.4.0 新增**：描边宽度（像素）、淡出时向外扩散比例、击杀红色扩散标记（开关 / 颜色 / 扩散比例 / 时长）；**0.6 新增**：护甲命中标记（开关 / 颜色）—— 只打中防弹板时换成蓝灰盾形 |
| `2. 伤害数字` | 开关、取值（`BodyOnly` 旧行为 / `Total` 肉体+护甲）、字号、颜色、描边颜色与宽度（**默认改成深色**）、弹出比例/时长、距图标间距、多段间距、存活时长；**新增**：爆头填充色、击杀（最后一击）颜色；**0.6 新增**：排布方式（`一行向右` 旧样式 / `累加` 战地 6 样式）、累加窗口、累加淡出时长、护甲命中颜色 |
| `3. 击杀信息` | 开关、锚点水平/垂直偏移（**水平默认 107→190**）、行间距、**卡片停留时长**；**故障特效开关**、卡片淡出时长、卡片文字描边；骷髅大小·间距·推挤速度·上限·弹出；**得分**（字号/颜色/水平偏移/滚动时长/弹出）；**成就横幅**（字号/高度/留白/偏移/文字色/四档强调色）；**连杀进度条**；**标签**（开关/字号/距离标签最小距离）；阵营 / 军衔徽章（**默认 40→72**）/ 徽章等级小标 / 名字 / 详情 / 方式 / 首领名称各字段开关与字号；8 种阵营配色 |
| `3b. 击杀特效` | 环形特效开关与爆头/暴击/爆炸三套半径·粗细·颜色、延迟、时长；图标辉光开关/颜色/强度/扩散（**辉光只对「自定义文件」图标样式生效**，程序化骷髅自带描边） |
| `3c. 击杀成就` | 复仇/多杀/枪手/掠夺者四个判定窗口、远程与神射手距离、精锐等级阈值；得分基础值与五项加成（爆头/远程/复仇/多杀/首领） |
| `4. 音频` | 开关、总音量、四类音量、四个自定义文件路径（留空自动找 `Sounds/`）、空间模式（`FollowCamera3D` 默认 / `Flat2D`）及两个排障项；**0.6 新增**：连杀音效开关与三个可选文件路径（`double_kill` / `triple_kill` / `multi_kill`，文件不存在时回退普通击杀音效） |
| `5. 界面` | UI 缩放、画布层级、**字体名称**（默认空 = 游戏自带字体，已含中文，**保持留空**；系统字体在本游戏版本上建不出 TMP 字体，见下文「字体」）、**图标样式**（`Procedural` 程序化绘制 / `CustomFiles` 沿用 `Icons/*.png` 按原样显示） |

> **命中提示 / 伤害数字的默认值仍然是旧 Mono 项目那份已经调过的实机配置**（`com.shiunaya.simplehitmarker.cfg`）：
> `基础大小 = 71`、`显示时长 = 0.868`、`中心偏移 = 0,5` 这些数字看着眼熟是故意的。
> **击杀卡片的默认值从 0.4.0 起不再对齐旧配置**（整张卡按战地 6 重做了，骷髅 44 / 字号 34·20·15 / 徽章 72 / 水平偏移 190 …）。
>
> BepInEx 的配置文件会把**首次运行时的默认值写死在磁盘上**，所以光改代码默认值，老用户永远拿不到新值。
> 0.4.0 起有**一次性配置升级**（见下），不用再手动删 cfg。

### cfg 分组 ↔ 设置面板分类

上表是 cfg 文件里的 section（键名固定，**不能改名**：改名 = 新键、旧键变孤儿）。ConfigurationManager 里另有一套按使用顺序排的分类
（§14，只是显示元数据，不改 cfg 的 section / 键 / 默认值），对应关系：

| cfg 分组 | 面板分类 |
|---|---|
| `0. 调试` | 90 调试与排障 |
| `1. 命中提示` | 10 命中提示 |
| `2. 伤害数字` | 20 伤害数字 |
| `3. 击杀信息` | 30 击杀卡片·内容 / 31 ·布局 / 32 ·配色 / 33 ·动画（`连杀时间窗口(秒)` 一项在 50） |
| `3b. 击杀特效` | 40 击杀图标特效 |
| `3c. 击杀成就` | 50 成就与计分规则 |
| `4. 音频` | 60 音效（`空间模式` 和两个排障项在 90） |
| `5. 界面` | 00 常用与整体界面 |

共 151 项配置，默认显示 47 项，其余 104 项需要在 ConfigurationManager 里开启 **Advanced / 高级设置**。

### 0.5.2：配置版本 2 → 3（自动）

音效 `空间模式` 的默认值从 `Flat2D` 改为 `FollowCamera3D`（原因见 §8 (b)）。升级时**只把值仍等于 `Flat2D` 的重置成新默认**；
本来就是 `FollowCamera3D` 的（比如作者本机）不变。局限：无法区分「从没动过」和「特意选了 Flat2D」，后者会被重置一次，
升级后版本号变成 3，不会再重置，想用 2D 再改回去即可。日志里是同一行 `[config] 配置从 v2 升级到 v3：…`。

### 0.3.x → 0.4.0 的配置升级（自动）

启动时发现 `0. 调试 → 配置版本` 落后，就把 33 个「默认值变了」的样式项（击杀卡片的尺寸 / 字号 / 配色 / 水平偏移，
伤害数字的描边）**换成新默认 —— 但只换那些值还等于 0.3.0 旧默认的**；你自己调过的值（≠ 旧默认）原样保留，
日志会写 `[config] 配置从 v0 升级到 v2：x 个仍是旧默认值的样式项已换成新默认，y 个被你改过的保持原样`。
垂直偏移、命中图标中心偏移、各种开关、音效相关的键不在名单里，一律不碰。

0.3.0 里**已经不存在**的动效键（`卡片入场位移` / `卡片入场时长` / `卡片行错开` / `骷髅头显示时长` / `骷髅头淡出时长` /
`得分图标大小` / `得分文字宽度` / `得分上浮距离` / `成就出现·停留·淡出时长` / `成就脉冲幅度` / `内容宽度` / `阵营文字字号` /
`玩家名字描边厚度`）会作为孤儿键被清出 cfg 文件；`显示时长(秒)` 改名为 `卡片停留时长(秒)`（默认 6 → 4.2）。

想退回旧外观：`5. 界面 → 图标样式 = CustomFiles` 只影响骷髅 / 命中标记用 PNG 还是程序化；
卡片版式本身没有「旧版」开关，旧布局在 git 历史里（0.3.0 = `6d6a7b5`）。

音效文件名约定（放 `BepInEx/plugins/AyaHitmarker/Sounds/`）：
`hit.ogg` / `headshot_hit.ogg` / `kill.ogg` / `headshot_kill.ogg`（`.wav` / `.mp3` 同样支持，也支持 `hit_headshot` / `kill_headshot` 这类写法）。

PMC 军衔图标（可选）：`BepInEx/plugins/AyaHitmarker/Icons/RankIcons/Rank{1,5,...,75}.png`，没有就不显示。

### 字体（实测，2026-10-19）

默认（`字体名称` 留空）用游戏自带的 TMP 字体（`Jovanny Lemonad - Bender Normal SDF`），它的回退链里带中文。
进图实测（`tools/verify/font-check.txt`）：

| 实验 | 结果 |
|---|---|
| 屏上全部中文字符（`Game/*.cs` 字符串字面量，114 个）逐个 `HasCharacter(c, searchFallbacks, tryAdd)` | **缺字形 0 个**；暗底 / 白底下 8 种击杀场景 + 5 连杀 + 复仇共 18 张截图，横幅 / 标签 / 部位 / 击杀方式（连同西里尔字母的名字）均无方块 |
| 系统字体名 → `Font.CreateDynamicFontFromOSFont` → `TMP_FontAsset.CreateFontAsset(font)`（Microsoft YaHei / Arial / SimHei） | **全部返回 null**，Unity 报 `Unable to load font face … Include Font Data` |
| 字体文件 → `new Font(path)`（`msyh.ttc` / `arial.ttf` / `Deng.ttf` / `NotoSansSC-VF.ttf`） | **构造即抛 `ArgumentNullException`** |

结论：这个游戏版本里系统字体建不出 TMP 字体资源，`字体名称` 填什么都会退回游戏字体；
所以**不需要、也做不到**「挂 YaHei 做中文回退」。0.5.2 没有改默认字体，只做了这几件事：

- 默认流程不再探测 Arial / Segoe UI（注定失败，而且每次失败都漏一个 `Font` + 它的材质与贴图，热重载下逐代累积）；
- 用户显式填了字体名且失败时，销毁创建出来的 `Font`（含材质与贴图）；
- 启动时在日志里写一行 `[ui] 字体 '…' 中文字形检查通过` / `缺少中文字形 […]`，用户反馈「方块」时一眼可判；
- 调试脚本新增 `glyphs` 指令；`tools/verify/onscreen_chars.py` 从源码提取屏上字符集（`--update` 直接刷新 `font-check.txt`）。

## 7. ⚠️ 不要补 `GameWorld.OnDestroy`（方法体共享）

第一版曾用 `ModulePatch` 补 `EFT.GameWorld.OnDestroy` 来在战局销毁时清 UI。**这会让游戏起不来。**

```
[Warning:Il2CppInterop] virtual void EFT.GameWorld::OnDestroy() shares its native body with 3360 other methods
and the detour runs for all of them, the body belongs to TOD_Sky.UpdateAmbient, TOD_Sky.UpdateReflection,
ExampleSceneController.Start, PaintingTarget.Awake, SnatcherPair.SetWeight.
Patch a method with its own body instead
```

**IL2CPP 会把方法体完全相同的方法合并到同一个原生地址。** `EFT.GameWorld.OnDestroy` 是个空 stub，
它的地址被 **3360 个**方法共用。Harmony 的 detour 装在这个地址上，就等于给那 3360 个方法全部插了桩：

| 症状 | 原因 |
|---|---|
| `[events] raid ended` 刷了 **24603** 行 | postfix 被 3360 个无关方法在启动期高频调用 |
| `InvalidOperationException: Handle is not initialized` 刷屏 | trampoline 试图把无关类型的 `this` 包成 `EFT.GameWorld` 代理，对象池里没有 |
| `NullReferenceException` 刷屏 | 同上 |
| 游戏卡死在启动 | 上面三条的叠加 |

**排查口诀：补丁装上后，先在 `LogOutput.log` 里搜 `shares its native body`。** 有这行就说明目标方法
不可补，必须换目标或者改用轮询/事件。

**当前结论：这个插件不需要任何补丁，所以也不引用 `SPTushonka.Reflection` / `0Harmony`。**
战局边界由主玩家出现/消失判定（`GameEvents.Poll()`，限频 4Hz）；`OnBeingHit` 每局开局重绑
（`GlobalEventDispatcher` 实例在 EFT 1.2.0.0 里跨局不变，旧「实例变化 = 换局」的判据已实测推翻）。

顺带避开的第二个坑：`new PatchManager(this, autoPatch: true)` 在「本程序集里一个 `ModulePatch` 都没有」时
会抛 `PatchException: Could not find any patches defined in the assembly during auto patching`，
**整个插件加载失败**（第二版就是这么挂的）。所以要么真的有补丁，要么别构造它。

将来确实需要补丁时：加回引用 → 写 `ModulePatch` 子类 → `new PatchManager(this, true)` →
**先启动一次并搜 `shares its native body`**。

## 8. ⚠️ 音效为什么这么难：三个坑，且第一个判断错了两次

排查花了五轮。按**结论可信度**排序：

| # | 事实 | 证据 | 修法 |
|---|---|---|---|
| 1 | **主菜单场景里创建的 AudioClip 会随场景切换被销毁** | 加载日志说 `ok`，进 raid 后 `clip 不可用 [Hit=missing ... destroyed ...]`；播放失败触发兜底重载**在 raid 内**重建后立刻正常 | **每个 raid 开始时清掉重载**（`OnRaidStarted`） |
| 2 | **`UnityWebRequest.Dispose()` 会连 handler 的 clip 一起销毁** | 更早一版 `finally { request.Dispose(); }` → 加载正常、播放时假 null | 不 dispose；请求逐槽持有（`_keepAliveRequests`） |
| 3 | **`bypassEffects` 会绕过 AudioListener 效果链** | Unity 文档；而本版本 EFT 用 Meta XR Audio 靠那条链出声 | 去掉，退回旧项目那三行配置 |

### 被证伪的两个判断（留着别再走一遍）

**（a）`AudioClip.GetData()` 在这个 comb 下恒为全 0。** 我曾以为原因是
`DownloadHandlerAudioClip.compressed`（Unity 的 `GetAudioClip()` 内部写死为 `true`），
于是显式设成 `false` 并"克隆一份自己拥有的 PCM"。
**实测无效**：`readOk=True` 但 `peak` 始终是 `0`，改 `compressed` 也不变。

结论：**不要试图克隆。** 直接用 handler 给的那个 clip（旧 Mono 项目就是这么做的），
代价是必须一直持有 `UnityWebRequest`。`GetData` 的峰值现在只作为诊断信息保留。

**（b）「2D 一定行」没有在本版本验证过。** 唯一确认能出声的配置是
`空间模式 = FollowCamera3D`。2D 那几次尝试都撞在"clip 已死"上，所以无法判断 2D 本身行不行。
0.5.1 之前默认值按旧项目设成 `Flat2D`，但 0.5.1「用户真实战局实测确认修复」用的是本机 cfg 里的 `FollowCamera3D`，
**`Flat2D` 至今没有任何战局内证据**（主菜单里也测不了：Meta XR Audio 在战局里接管空间音频，菜单音乐还会污染任何输出电平读数）。
**0.5.2 起默认改成 `FollowCamera3D`**（`SoundPlayer.Tick` 每帧把声源挪到 `Camera.main`），配置版本 2 → 3：升级时只把仍等于 `Flat2D` 的重置成新默认。
想试 2D 的人自己切回去，没声音就改回来。

现在的加载流程（`Assets/AudioBank.cs`）：

```
GetAudioClip(url, type) → handler.compressed=false / streamAudio=false（保险，非必需）
  → SendWebRequest / 轮询 isDone
  → GetContent(request)              // 直接用它的 clip
  → 量一次 GetData 峰值（仅诊断，实测恒 0）
  → 持有 request 保活（_keepAliveRequests）
每个 raid 开始 → Invalidate() + 重新加载
```

日志会打 `dataPeak` / `readOk`。**这两个值哪怕看着不对也不影响出声** ——
它们只是诊断；能出声靠的是"用 handler 的 clip + 持有请求 + 进图重载"。

### 加载中断后永久失声（2026-10-12）

日志只加载了 `Hit`，随后出现 `raid started`，四槽全部 `missing`，且没有第二次「开始加载」。
原因是 Unity `StopCoroutine` 不保证释放托管迭代器：`LoadAll` 的 `finally` 没运行，
`_loading` 永远为 true，新加载立即退出。

修复：停止 Unity 调度后显式 `Dispose` 托管迭代器；`LoadAll` 同时释放当前嵌套的
`LoadOne`，中断时释放未完成的请求，成功加载的请求继续保活。进图/路径变更严格按
「停止并释放旧协程 → 清缓存 → 启动新协程」执行。
另加主线程 4Hz 存活检查：场景切换晚于 dispatcher 创建时，也能自动发现被销毁的
clip 并提前重载，不必牺牲第一次命中来触发恢复。

回归脚本：`tools/verify/audio-reload.txt` 在首帧连续中断加载，然后播放四类音效，
再完成一次重载并重复播放。使用 `run_game.ps1` 启动；`audio status` 应显示
`loading=False` 且四槽 `ok`。该脚本验证加载和播放调用；是否实际出声仍需听音或录音确认。

最终版本实机回归结果（2026-10-12）：首帧连续中断后成功重载；菜单场景销毁 clip 后
由存活检查提前恢复；两次状态检查均 `loading=False`、四槽 `ok`；四类音效两轮共
8 次播放均 `isPlaying=True`，无「未播放」告警，脚本正常退出（code=0）。
构建 0 警告 / 0 错误；既有离线逻辑测试 306 项通过（不覆盖 Unity 音频）。

### 战局中 Loaded 音效变成零采样空壳（2026-10-12）

最新战局日志中，正常菜单测试有 `len=0.583s samples=25754 ch=2 freq=44100 isPlaying=True`，
真实击杀时却出现 `len=0s samples=0 ch=0 freq=0 loadState=Loaded isPlaying=False`。
诊断仍报四槽 `ok`：原检查只识别 Unity 假 null，漏掉了非 null 但音频元数据已清空的对象。
这说明失败发生在音频资源数据层；具体是哪次游戏资源清理导致清空，仅凭这份日志无法确定。

修复：

- handler 的 clip 设置 `HideFlags.HideAndDontSave`，防止未使用资源清理；不克隆 PCM。
- 加载完成、主线程 4Hz 健康检查、每次播放前均检查 Loaded、正采样数/声道/频率/有限正长度。
  非 null 的空壳也会标记失效，记录实际元数据，并由主线程协程异步重载。
- 请求逐槽持有，槽位替换或缓存失效时释放旧请求/clip，避免兜底重载积累资源。
- 播放路径不再调用 `LoadAudioData`；坏数据只请求异步恢复。

回归：`tools/verify/audio-integrity.txt` 覆盖首帧中断、`Resources.UnloadUnusedAssets` + GC，
以及 `audio unload`（主动卸载数据、保留对象）后的自动恢复。离线测试新增元数据契约用例，
直接覆盖日志中的 Loaded 零采样组合，不能证明 Unity 实际出声。

实机结果：主动卸载成功复现四槽非 null、Loaded、零采样的同一故障，4Hz 健康检查自动识别并重载，
3 秒后的检查四槽均恢复 ok。资源清理后与恢复后的两轮四类音效共 8 次脚本播放均
`isPlaying=True`，无未播放/加载异常，游戏正常退出（code=0）。构建 0 警告/0 错误，
315 项离线测试通过。本地日志证据保存在 `tools/verify/evidence/audio-before.log` / `audio-after.log`（不随源码发布）。
脚本验证在主菜单完成，未录音。随后用户完成真实战局实测，确认音效丢失已修复（2026-10-12）。

### 音量项的坑

`ConfigDescription(desc, null, new object[]{0f,1f})` 里的 tags **只给 ConfigurationManager 画滑条用，
不做钳制**。曾有人把音量填成 `80`，于是 `vol = 80 × 80 = 6400`，
全靠代码里 `Mathf.Clamp(vol, 0f, 1f)` 才没变成噪音。
音量项现在改用 `new AcceptableValueRange<float>(0f, 1f)`，这才是真正会钳制的写法。

## 9. 击杀反馈视觉设计（对齐战地 6）

**参考**：两段 BF6 实录 —— `ref/击杀演示.mp4`（1398×858 / 30fps，一个爆头 + 一个普通击杀）和
`ref/连杀演示.mp4`（1310×778 / 30fps，双杀 → 三杀 → 四杀）。
0.3.0 时只有一段 480p 的 B 站视频；0.4.0 拿到了清晰原片，用 OpenCV 逐帧（33ms 一帧）抽出来看，
下面 §9.1 的数字是**量出来的**，不是印象。

### 9.1 BF6 的击杀确认到底长什么样（实录逐帧）

**版式** —— 所有东西在一条竖直「脊线」左边**右对齐**，只有分数从脊线往右长：

| 元素 | 实录里的样子 |
|---|---|
| 主行 | 骷髅图标 + 白色分数（`100`，Bender 一类方正字体、0 带斜杠）。**普通击杀 = 白骷髅；爆头 = 珊瑚红骷髅，颅顶有一道 V 形裂口** |
| 名字 | 骷髅下面一行，珊瑚红小字（不是纯红，纯红在暗背景上不够亮） |
| 标签 | 名字下面的**描边小方框**：深色半透明底 + 白色细描边 + 白色大写字 —— `KILL` / `HEADSHOT` |
| 多杀横幅 | 主行上方：**浅钢色实心方块 + 深色字**（`DOUBLE KILL`），右边是骷髅徽记 —— N 个骷髅一簇，两侧各 5 道长短不一的横线（上下短、中间长，合起来像一只眼睛）。2 杀并排、3 杀「一前两后」（前面那个描一圈深色边压住后面的）、4 杀后面再冒出一个头 |

**累加**（连杀演示第 4 杀）：卡片**不是一杀一张**。窗口内再杀一个，会**并到同一张卡上**：
红骷髅（上一杀的爆头）+ 白骷髅（这一杀）并排，分数 `100 → 200`，标签 `[HEADSHOT][KILL]` 并排，
名字换成最新的受害者；横幅 TRIPLE → QUAD 时旧的闪灭、新的显影。窗口过了才整体退场。

**入场**（单杀演示；0 = 第一帧出现东西）：

| 时刻 | 发生什么 |
|---|---|
| 0.00s | **标签最先出现**（整个框，没有过渡）；分数开始乱跳（`14` → `72` …）；名字位置先扫过一条红色横向拖影 |
| 0.07 ~ 0.17s | 骷髅 / 名字 / 分数各自先以**实心色块**闪出（骷髅 = 图标颜色的方块，名字 = 一条红带，分数 = 一块白底），数字在块里乱码 |
| 0.17 ~ 0.23s | 色块褪去，真正的图标 / 文字显影（期间内容随机掉几帧） |
| 0.27s | 稳定 |

**退场**（同一段；0 = 第一次熄灭）—— 不是平滑淡出，而是**错峰闪烁熄灭**：

| 时刻 | 发生什么 |
|---|---|
| 0.00 ~ 0.23s | 骷髅 + 名字：先整体灭一帧，回到约 70% 亮度，再一路闪着变暗 |
| 0.20 ~ 0.50s | 标签：同样闪着变暗 |
| 0.50s | **分数最后硬切**（前一帧还是满亮） |

整张卡约 3.4 秒（入场 0.27 + 停留 2.7 + 退场 0.5）。

**命中标记**：普通命中是白色细 ✕（四条虚线，中间留空，没有描边）；**击杀那一下变成红色的、更长更粗的 ✕，并向外扩散**
（约 0.27s 内从半径 ~60px 扩到 ~100px）。伤害数字不是「一发一个」，而是**累加**（`32 → 67 → 99`，同一位置滚动），
击杀那一下变红（`163`）。

### 9.2 本插件的实现

```
                             ┌─────────┐
                             │▌双杀    │ ≡☠☠≡        ← 成就横幅（只有触发成就时才有；多杀时右边带骷髅徽记）
                             └─────────┘
  ┌────────┐                       ☠ ☠   425           ← 主行：骷髅（并排累加）+ 累计得分
  │  军衔  │                  ────────────             ← 连杀进度条
  │  徽章  │                USEC  Sniper               ← 阵营 + 名字
  └────────┘          [268m] [狙击] [爆头]              ← 标签（描边小方框）
                                  M700                 ← 详情（暗色小字）
        ↑ 最左边，随内容变宽往左让            ↑ 脊线（锚点）：右对齐边
```

| 元素 | 实现 |
|---|---|
| 累加卡片 | `Ui/KillCardModel.cs`：窗口 = 最后一杀 + `卡片停留时长`（默认 4.2s，含 0.5s 退场）；窗口内再杀 → 骷髅并排（上限 8）、分数累计（数字从**屏幕上正显示的数**滚起，不是从 0）、标签取并集（爆头击杀只给「爆头」不再加「击杀」）、名字换最新、横幅只在有新成就时替换；退场期间补杀会取消退场 |
| 骷髅 | 程序化 SDF 绘制（`Assets/IconShapes.cs`）：白骷髅 / 珊瑚红爆头骷髅（颅顶 V 形裂口），自带深色描边，眼窝是深色而不是镂空。旧 `skull.png` 只有 33×38 像素，放大到 44px 明显发糊 |
| 分数 | 白字，Bender；`GlitchMath.CountValue` 做「乱跳着滚到终值」，噪声随进度收敛 |
| 标签 | 九宫格描边框（`ProceduralIcons.TagBox`）。顺序（从左到右）：**距离**（≥ `距离标签最小距离`，默认 20m）→ 方式（近战 / 爆炸 / 狙击，琥珀色）→ 击杀 / 爆头（脊线侧；爆头是珊瑚色）。BF6 只有 `KILL` / `HEADSHOT`，距离和方式是本插件额外的信息 |
| 成就横幅 | 浅钢色底 + 深色字 + 左缘 4px **档次强调色**（普通灰 / 优秀蓝 / 精英橙 / 史诗红）；只带短详情（距离 / 连杀数，≤ 7 个字符），解释性长句不显示；双杀 ~ 五杀右边带骷髅徽记（`IconShapes.Emblem`，骷髅数 = 连杀段数） |
| 军衔徽章 | **在卡片最左边**，72px（旧默认 40px，浅色浮雕图在 40px 下只剩一团灰），垫一块深色底板（和标签同款描边框，描边取阵营色 USEC 蓝 / BEAR 橙），右下角压着等级小标。内容变宽徽章平滑往左让；没有军衔图的阵营（SCAV / 首领）不显示这块，阵营文字仍在名字前面 |
| 连杀进度条 | 保留（BF6 没有，是本插件的功能）：细条，宽度 ≥ 骷髅行宽 |
| 入场 | 每个元素按 `标签 0 → 分数 0 → 徽章 0.03 → 骷髅·名字 0.04 → 横幅 0.10 → 详情 0.12 → 徽记 0.14` 的延迟，先**实心色块**闪出 0.10s（骷髅 = 骷髅色，名字 = 名字色，徽章 = 阵营色，横幅 = 浅钢色），再 0.12s 显影，期间内容随机掉帧 |
| 退场 | `GlitchMath.ExitAlpha`：第一梯队（骷髅 + 名字）占前 46%，第二梯队（标签 / 横幅 / 徽章 / 详情）40% ~ 100%，分数一直亮到最后一刻硬切。闪烁按 30Hz 量化 + 整数哈希（不用 `Random`），同一时刻永远同一结果，截图可复现 |
| 关掉故障特效 | `3. 击杀信息 → 故障特效 = false`：退化成普通淡入淡出（对闪烁敏感的话请关掉） |

**命中标记**（`Ui/HitmarkerLayer.cs`）：

- **深色描边**：旧 `hit.png` 只有 ~3px 粗的白线，**在纯白背景上整个消失**（实测，见 `IconShapes` 渲染预览第一列）。
  现在四条虚线用 SDF 重建（几何量自旧图：中心 32px 到 63px、核心粗 3px），外面加 `描边宽度`（默认 1.5px）的深色描边和柔光；
  描边烘在贴图里是黑色，`Image.color` 相乘着色只会改白色部分，所以红色标记的描边照样是黑的。
- **击杀红色扩散版**：击杀事件到达时（`PromoteToKill`）标记升级成红色**粗线版**（核心更粗，否则红色会被黑描边压得发黑），
  从 0.85 倍弹出、向外扩散到 `击杀标记扩散比例`（默认 1.6），前 35% 保持全亮再淡出。
  命中事件与击杀事件谁先到不保证 —— 击杀之后 0.06s（几帧）内才到的命中不会把标记降级回普通版；
  窗口刻意很窄：同一颗子弹的两个事件在同一次 `ApplyShot` 里发出、基本同帧出队，窗口大了，击杀后紧接着扫射下一个目标的前几发会被误判成击杀。
- 普通命中淡出时缓缓向外撑开（`淡出时向外扩散比例`，默认 1.12），其余沿用旧项目的弹出 / 缩放 / 爆头变色。
- **`中心偏移(X,Y)` 的 Y 符号修正**：配置沿用旧项目（IMGUI）的屏幕坐标语义，**Y 向下为正**（配置说明写的是「负 Y 向上」），
  画布是 Y 向上；旧版没取反，默认 `0,5` 本该是往下 5px，实际却往上抬了 5px。现在取反，伤害数字读同一个偏移、和图标保持对齐。

**伤害数字**（`Ui/DamageNumberLayer.cs`）：两种排布样式，设置页 `2. 伤害数字 → 排布方式` 切换，**两种都保留**：

- `一行向右`（默认，旧项目样式）：每次命中在图标右侧多加一个数字，所有还活着的数字排成一行向右延伸；描边默认从「白 25%」（白字配白描边等于没有）改成深色；爆头填充色单独配置；击杀那一下的数字变红并重新弹出（同样不依赖到达顺序）。
- `累加`（0.6 b，战地 6 样式）：同一目标在窗口内连续命中只显示**一个滚动累计**的数字（`32 → 67 → 99`），
  多个目标垂直堆叠（最新在最上），击杀时变红。合并/过期模型是纯逻辑 `Game/DamageAccum.cs`（离线测试覆盖），
  目标身份用探查局 S1 确认稳定的受害者 `ProfileId`；窗口 / 淡出时长 / 护甲颜色都在设置页可调。

**0.6 a 护甲命中**：`DidBodyDamage == 0 && DidArmorDamage > 0` 的命中（打中防弹板）把命中标记换成蓝灰色**盾形**，
伤害数字用「护甲命中颜色」；盾形在 `Assets/IconShapes.cs` 里程序化绘制（离线 `--render-icons` 可看）。可整体关闭。

**和 BF6 不一样的地方（有意的）**：BF6 的卡片很小、放在准星左下；本插件默认锚点在准星左边 190px、与准星同高，
因为旧项目就是这么摆的（用户调过位置，只是 107 → 190 把分数挪出了命中标记的范围）。
卡片上多出来的东西 —— 军衔徽章 / 阵营 / 距离与方式标签 / 详情 / 连杀进度条 —— 都是 Tarkov 里有用的信息，BF6 没有。

BF6 的成就官方叫 **Accolades（荣誉）**：`先锋 Vanguard` / `枪手 Gunner`（4 秒内用不同武器再杀）/`掠夺者 Marauder`（第 4 杀后 8 秒内再杀）/`侦察专家` / `破碎机` 等，各自给 XP 并累计成荣誉狗牌。
（来源：EA 官方 progression 文章 zh-hans 页；`复仇者 / 宿敌 / 救世主` 这类是社区惯用译名，非官方字符串。）

### 9.3 成就名与触发条件

优先级：**复仇 &gt; 多杀 &gt; 掠夺者 &gt; 连杀天梯 &gt; 枪手 &gt; 首领 &gt; 神射手 &gt; 远程 &gt; 近战/爆破 &gt; 精锐 &gt; 先锋 &gt; 爆头**（一次击杀只出一条缎带）。

| 缎带 | 触发 | 依据 |
|---|---|---|
| 复仇者 | 在`复仇判定窗口`（默认 120s）内，击杀刚刚打死过我的玩家 | 我方阵亡也走 `Player.OnPlayerDeadStatic`，只是不产生 UI |
| 双杀 / 三杀 / 四杀 / 五杀 / 多杀 xN | 相邻击杀间隔 ≤ `多杀判定窗口`（2.5s） | 真实击杀事件 |
| 猎手 / 狂战士 / 势不可挡 / 主宰 / 无双 / 战神 | 连续击杀数到 3 / 5 / 7 / 10 / 15 / 20（间隔 ≤ `连杀时间窗口`） | 同上 |
| 掠夺者 | 第 4 杀后 `掠夺者判定窗口`（8s）内再杀一个（踩到天梯新档时让位） | BF6 官方荣誉 |
| 枪手 | `枪手判定窗口`（4s）内用**不同武器**再杀一个 | BF6 官方荣誉 |
| 首领猎杀者 | 击杀 `WildSpawnType` 属于 BOSS 类 | `BotTypeMapping` |
| 神射手 | 距离 ≥ `神射手判定距离`（200m）且爆头 | `Vector3.Distance` |
| 远程击杀 | 距离 ≥ `远程判定距离`（100m），或狙击类伤害 + 100m | `DamageInfo.DamageType` |
| 冷兵器 / 爆破专家 | `DamageType` 含 `Melee/Blunt` / `Explosion` 等 | 同上 |
| 精锐猎杀 | 击杀等级 ≥ `精锐等级阈值`（40）的 PMC | `Profile.Info.Level` |
| 先锋 | 本局第一杀 | 本局计数 |
| 爆头 | 命中部位为头部（最低优先级，兵底缎带） | `EBodyPart.Head` |

### 9.4 得分公式

`基础 100` + `爆头 +50` + `距离≥远程判定 +50` + `复仇 +25` + `多杀 +25 × (连杀段数-1)` + `首领 +200`。
全是配置项。**这不是游戏的权威经验值**（客户端读不到），只是把“这一杀有多难/多解气”量化的反馈。

### 9.5 为什么这些判断能测

判定引擎（`Game/KillStreak.cs`）、昵称归一化（`Game/NameKey.cs`）、卡片累加模型（`Ui/KillCardModel.cs`）、
卡片行位置数学（`Ui/KillCardLayout.cs`）、故障动效曲线（`Ui/GlitchMath.cs`）和程序化图标的栅格化（`Assets/IconShapes.cs`）
全部**不引用 Unity/EFT**，所以 `tests/AyaHitmarker.LogicTests` 能直接编译它们并断言“哪一杀出哪条缎带 / 并卡还是新卡 / 闪烁什么时候硬切”。
改判定时先看 `KillStreakFacts.cs`，改卡片行为先看 `KillCardModelFacts.cs` —— 那里是这些规则的可执行规格。

---

## 10. 已知限制

- **得分不是权威经验值**：击杀事件读不到「这一击给了多少经验」（真实值走 SkillManager / 战局结算）。
  现在显示的是自己的计分公式（基础 100 + 爆头/远程/复仇/多杀/首领加成），**不是**游戏里的 XP。
  想要真值就得另找数据源。
- **没有 Preset 系统**：旧项目的 8 套 Preset（`StyleCatalog` / `KillIconPreset`）没有移植，
  环形/辉光/缎带参数改由配置直接提供。想恢复预设时只要把这些字段做成一组配置档即可，事件层不用动。
- **发布 ZIP 已包含军衔图标，源码运行时需放文件**：`Icons/RankIcons/Rank{1,5,...,75}.png`（66×66 最佳）。
  启动时就会探测一次并打日志（`[icons] PMC 军衔图标已加载 N/16` 或 `没有找到任何 PMC 军衔图标`），
  所以不会再出现“分不清是没装图标还是没碰到 PMC”的情况。缺具体档位时会**向下借用最近的可用档位**；
  换文件需要重启（只探一次）。
- **判定阈值以主线程时间为准**：多杀 / 连杀 / 复仇的时间戳在 `FeedbackBus.Drain` 时打，
  事件线程不做 `Time` 读取（不保证主线程，也不值得去碰 Unity）。
- **战局边界以主玩家为准**：轮询 `GameWorld.MainPlayer.Profile` 出现/消失（限频 4Hz），
  所以从战局销毁到 UI 清空最多有 0.25 秒延迟 —— 实际感知不到（那 0.25 秒里画面已经切走了）。
  **不能用 `GlobalEventDispatcher` 实例判局**：实测（2026-10-19 三连局）它在整个进程里指针不变，
  而且游戏换局时会清掉 `OnBeingHit` 订阅 —— 这正是 0.5.2 及以前「第 2 局起命中反馈消失」的原因；
  0.5.3 起每局开局强制重绑（幂等）。
- 自己被打**只做复仇记账、不出提示**。`GameEvents.OnGlobalBeingHit` 在建 `HitRecord` 之前就按 `aggressor.IsYourPlayer` 过滤掉了「我被打」的事件，所以 `HitRecord.VictimIsLocalPlayer` 实际只会在「自己打到自己」时为 true（`AyaBehaviour` 里直接跳过）。要做被击提示得先放开这个过滤；探查局 S2 已确认 `OnBeingHit` 在受害者是本机玩家时确实触发、`Direction` / `MasterOrigin` 可用（见 `docs/recon-findings-2026-10-19.md`），0.6 e 可以直接做。
- **伤害数字有两种样式，默认仍是旧项目的「一行向右」**：0.6 b 起设置页 `2. 伤害数字 → 排布方式` 可切到
  `累加`（战地 6 样式：同一目标窗口内滚动累计、击杀变红）。累加样式按目标 `ProfileId` 归组，
  所以**同名的两个 bot 不会被合并**；窗口 / 淡出时长可调。两种样式都保留，随时切回。
- **程序化骷髅是 SDF 近似，不是 BF6 的原图**：形状按实录里的轮廓（圆颅 + 窄下颌 + 爆头颅顶 V 形裂口）画的，
  放大看偏「卡通」。想用自己的图：`5. 界面 → 图标样式 = CustomFiles`（骷髅 / 命中标记用 `Icons/*.png` 原样显示，不加描边）。
- **卡片配置改了要等下一次击杀才生效**：文字 / 标签的宽度每杀量一次（`ForceMeshUpdate`），逐帧只读缓存。
- **击杀卡片只认「主线程时钟」**：多杀 / 连杀窗口读 `UiClock`（= `Time.unscaledTime`），暂停菜单里时间照走。

## 11. 排查

排查日志位于游戏 BepInEx/LogOutput.log，本节列出常见问题；本地 AGENTS.md §5 提供完整维护入口。

最常见的几种：

- `[ui] canvas ready ... font=MISSING` → 连游戏自带 TMP 字体都取不到，文本不显示（极少见；`字体名称` 填系统字体帮不上忙，见 §6「字体」）。请带日志反馈。
- `[ui] 字体 '…' 缺少中文字形 […]` → 对应文字会渲染成方块；`tools/verify/font-check.txt` 可以复现并逐字检查。
- `[icons] 没有找到任何 PMC 军衔图标` → 图标文件没放对地方（应为 `Icons/RankIcons/Rank40.png` 这种）。
- 本局完全没有命中提示 → 看有没有 `[events] 无法解析 GlobalEventDispatcher.Instance`。
- 卡片位置/大小看着不对 → 先看 `[ui] canvas geometry` 和 `[kill] 屏幕坐标(左下原点)` 两行（需开诊断日志）。
- 伤害数字跑到命中图标**左边** → 不是设计：看 `[dmg] ... size=` 那行，`size` 应该是 `(数字宽, 行高)`，
  如果是 `(200, 50)`（TMP 默认值）说明 `sizeDelta` 没写进去（NaN 哨兵比较的坑，见 AGENTS.md §3.5）。
- 配置升级没生效 → 日志里搜 `[config] 配置从`；没有这行说明 `配置版本` 已经是最新。

### ⚠️ 截图验证的坑：DPI 虚拟化

用脚本截图验证 UI 位置时，**非 DPI-aware 进程拿到的图不是你以为的那张**：

- 本机游戏 `Screen.width/height` = **2560×1600**，但 PowerShell 的 `Screen.Bounds` 报 **1707×1067**（150% 缩放）。
- 不调 `SetProcessDPIAware()` 时，`CopyFromScreen` 得到的是**屏幕左上角 1:1 的一块 1707×1067**，
  不是整屏缩放图。拿它去对坐标会把「完全正确」的布局误判成偏到右下。

正确做法（验证脚本里已经用了）：

```powershell
Add-Type 'using System.Runtime.InteropServices; public static class D { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }'
[D]::SetProcessDPIAware() | Out-Null   # 必须在读 Screen.Bounds / 截图之前
```

先调它，再截图得到的就是 2560×1600 的整屏 1:1 图。

### 游戏内自测快捷键

`F8` 命中 / `F9` 击杀 / `F10` 模拟阵亡（需 `0. 调试 → 启用测试快捷键`）：

- `F9` **不是固定造一条**，而是按 `Behaviour/TestKills.cs` 的脚本表轮换 8 种地形：
  PMC 胸部远程 → PMC 爆头狙击 → 神射手 → SCAV 近战 → 首领爆炸 → PMC-bot 右臂 → 低等级 PMC 近距离爆头 → 满级 PMC + 长名字。
  连按几下就能把横幅、得分累加、标签、进度条、各档军衔徽章、红色击杀标记全看一遍
  （**连按得快**：间隔小于 2.5s 才是连杀，卡片会累加；慢一点就是一张新卡）。
- `F10` 会把「凶手」设成**下一发 F9 的目标**，所以 `F10` 再 `F9` 必定出「复仇者」。
- 这些快捷键在主菜单就能用（画布在插件加载时就建好了），不用进图。

### DebugHarness：脚本化的进图验证（逐帧截图，不用按键）

按键 + 截图的老办法有两个毛病：要抢窗口焦点，而且拍不到「动画走到第几帧」。
`Behaviour/DebugHarness.cs` 解决这两件事 —— **只有设置了环境变量 `AYA_DEBUG_SCRIPT` 才会创建**，正常游戏零开销：

```powershell
# 需要：SPT 服务端已在监听 6969、游戏当前没在运行
tools\verify\run_game.ps1 -Script tools\verify\kill-card.txt -ShotsDir D:\shots\v1
python -P tools\verify\imgsheet.py D:\shots\v1 a_enter D:\shots\a_enter.png 480 690 1300 950 3 520   # 一段动画拼成联系表
```

脚本一行一条指令（`#` 注释）：`clock manual`（UI 时钟手动步进）、`step 0.0333`、`kill N`（造脚本表第 N 条击杀，
走真实的判定 / 卡片 / 命中标记升级）、`hit [head]`、`hit armor [N]`（只打护甲，验盾形标记）、`death N`、
`bg dark|mid|white|snow|grass|#RRGGBB|off`（画布后面垫纯色，验证各种背景下的可读性）、`shot name`、
`seq name 14 0.0333`（连拍 14 张，每张之间 step 一帧）、`scale 0.1`（自动时钟慢放）、`wait 2`、
`waitmenu 90`（等主菜单场景加载完；热重载后同一进程自动跳过）、`glyphs 击杀爆头`（逐字检查当前字体有没有字形）、
`watch`（遥控模式，见下）、`reset`、`log 文字`、`quit`。

> **单次脚本截图数要 ≤ 20 张。** 实测超过 20 张后 `quit` 有概率以 `0xC0000005` 退出：
> `ScreenCapture` 的写入队列在 `Application.Quit()` 时尚未清空，与插件逻辑无关（同一份脚本 20 张干净退出、25 张必崩；
> 在 `quit` 前 `wait 3` 也没用）。验收脚本已按段拆开：`0.6-hitmarkers.txt`（19 张）/
> `0.6-cards.txt`（16 张）/ `0.6b-accum.txt`（15 张）。
截图用 `ScreenCapture.CaptureScreenshot`（引擎在帧尾写文件）：**不依赖窗口焦点，也不会被 DPI 虚拟化坑**，
手动时钟下动画时刻完全由脚本决定，不受掉帧 / 截图卡顿影响。游戏在 `quit` 时自己退出。

### 遥控模式 + 输入工具：需要「先发输入、再看画面」的交互式验证

设置面板这类要点鼠标 / 按键的界面，脚本没法事先排好时序。`watch` 指令让 harness 在脚本跑完后**不退出**，
持续监听 `<shots>/cmd.txt`：外部往里写指令（读后即删），harness 就追加执行。`tools/verify/send-input.ps1` 负责给游戏窗口发键鼠输入：

```powershell
tools\verify\run_game.ps1 -Script tools\verify\remote.txt -ShotsDir D:\shots\remote -TimeoutSec 3600   # 另开终端
tools\verify\send-input.ps1 -KeyDelayMs 80 focus wait:1.5 key:F12 wait:2.5                               # 打开 ConfigurationManager
"shot a" | Set-Content D:\shots\remote\cmd.txt -Encoding ascii                                          # 截一张图
"quit"   | Set-Content D:\shots\remote\cmd.txt -Encoding ascii                                          # 结束
```

`send-input.ps1` 的动作：`focus` / `key:F12` / `move:x,y` / `click:x,y` / `dclick` / `rclick` / `drag:x1,y1,x2,y2` / `wheel:x,y,n` / `text:abc` / `wait:秒`，
坐标用截图的画面坐标（2560×1600 下左上角 0,0），脚本按游戏窗口的客户区自动换算。踩过的坑：

- **游戏必须在前台**，否则 Unity 不处理（或吞掉）输入：每次都先 `focus`。注意进程的 `MainWindowHandle` 抓到的是 **BepInEx 控制台**，
  工具里按类名 `UnityWndClass` 找真正的游戏窗口。
- `focus` 内部按了一下 **Alt**（绕过前台锁）。如果此时界面正等着捕获按键（比如快捷键编辑器），Alt 会被当成用户按的键。
- 游戏在后台 / 低帧率时按键会丢字符：`-KeyDelayMs 80`~`120`，并在输入之后多留 2 秒再截图。
- 加载画面期间输入全部被吞，`waitmenu` 在 `MenuUIScene` 场景出现时就结束了（比真正进菜单早）：进菜单后再等几十秒。
- 脚本文件含中文必须带 **UTF-8 BOM**，否则 Windows PowerShell 5.1 把中文当 ANSI 读、直接解析失败。

> 0.4.0 的所有视觉调整都是这么验证的：先在 `--render-icons` 里看图标，再用 DebugHarness 逐帧看动画与各种背景。
> 这条流程抓出过两处肉眼难发现的缺陷（NaN 哨兵比较导致 `sizeDelta` / `localScale` 写不进去，见 AGENTS.md §3.5）。

### 热重载迭代（ScriptEngine.IL2CPP）：改代码不重启游戏

适合 UI 布局 / 动画 / 文案 / 配置默认值的快速迭代：游戏只启动一次，之后每次构建约 1 秒后由引擎自动装载新 DLL。
依赖独立仓库 `D:\EFTdev\dev\ScriptEngine.IL2CPP`（开发机上装在 `BepInEx/plugins/ScriptEngine.IL2CPP/`，**不随本插件发布**）；
它只扫描 `BepInEx/scripts/`，所以切换模式由构建目标完成：

```powershell
dotnet build -p:HotReload=true     # 第一次切模式：游戏必须已退出（要把 plugins/ 里被锁的 DLL 改名为 *.se-disabled）
tools\verify\run_game.ps1 -Script tools\verify\hot-reload.txt -ShotsDir D:\shots\hot -TimeoutSec 7200   # 另开终端：启动一次，脚本不 quit
dotnet build -p:HotReload=true     # 之后每改一次代码；游戏开着也能构建（scripts/ 里的 DLL 是字节流加载，不锁文件）
tools\verify\reload-scripts.ps1    # 可选：强制重载；-Command status / unload
dotnet build                       # 收尾：必须先退出游戏。恢复 plugins/ 部署，并撤掉 scripts/ 下的 DLL / PDB
```

- 日志确认：`Reload (file) done: generation N, … 0 error(s)` → `[behaviour] Awake` → `[ui] canvas ready`。
- `hot-reload.txt` 以 `waitmenu 90` 开头：本进程第一次运行时等主菜单场景（`MenuUIScene`）加载完，之后每代重载直接跳过；
  `shot` / `seq` 每次重载都会从头重放并**覆盖同名截图**。`AYA_DEBUG_SHOTS` 不要指到 `BepInEx/scripts/` 里面（引擎监视那个目录）。
- `reload-scripts.ps1` 在 Windows PowerShell 5.1 和 pwsh 7 下都能跑（文件必须带 UTF-8 BOM，否则 5.1 把中文当 ANSI 读会解析失败）。
- 注入过类型的代次，Il2CppInterop 无法回收程序集，日志会打 `Generation N: its assemblies stay in memory (~218 KB)`，属正常，只是字节残留。
- 卸载路径：`Plugin.Unload()` 销毁 `AyaBehaviour` → `OnDestroy` 拆画布（含所有 TMP 文本的材质实例）、音频宿主、图标 / 精灵 / 字体缓存。
  **新增任何 `DontDestroyOnLoad` 对象或静态缓存，必须在这里一并清掉。**
- 不验证事件源与真实战局行为，冷启动验收仍然要跑一遍普通模式。

**实测（2026-10-19）**：冷启动 + 连续 14 代重载 0 error；多代之后同一场景的截图与第 1 代**逐字节相同**；
`AyaHitmarker.Overlay` 画布始终只有 1 份；用 `Resources.FindObjectsOfTypeAll` 数对象，Texture2D / Sprite / TMP 字体 / Font / Material 的逐代增量都是 0。
修复前每代固定漏 `+2 Texture2D / +2 Font / +5 Material`：来源是默认流程里注定失败的 Arial / Segoe UI 系统字体尝试（没销毁创建出的 Font 及其材质贴图），
以及 3 个从未激活过的 TMP 文本（不走 `OnDestroy`，它们的 `(Instance)` 材质没人清）。

## 12. 参考

| 资源 | 地址 |
|---|---|
| `SP-Tushonka/modules` @ `5.0x-dev` | 与客户端逐字节对应的官方参考实现 |
| `SPTushonka.Reflection` | `Patching/`（ModulePatch）+ `Il2Cpp/`（Injection / BaseCall / MainThread） |
| `ref/Il2CppDumperOutput/dump.cs` | 全类型/字段/方法 + RVA |
| 旧 Mono 版 | `E:\EFTDev\Client.0.16.9.5.40743\Dev\SimpleHitMarker` |

## 13. 变更记录

| 版本 | 内容 |
|---|---|
| 0.6.0（开发中） | **0.6 a 护甲命中区分**：只打中护甲（`DidBodyDamage == 0`）时命中标记换成程序化**盾形**（蓝灰着色，可关），伤害数字用「护甲命中颜色」。**0.6 b 累加伤害数字**：新增 `排布方式 = 累加`（战地 6 样式，同一目标窗口内滚动累计、击杀变红、多目标垂直堆叠），**保留**原有「一行向右」作为默认样式；目标身份用探查局 S1 确认的 `ProfileId`，纯逻辑 `Game/DamageAccum.cs` + 离线测试覆盖。**0.6 d 连杀音效**：可选的 `Sounds/double_kill` / `triple_kill` / `multi_kill`，文件不存在时回退普通击杀音效；设置页新增开关与自定义路径。`F8` 测试命中支持按住 Shift 模拟护甲命中，harness 新增 `hit armor [N]`。离线测试 315 → 456 项。**卡片拆分**：`Ui/KillFeedLayer.cs` 1482 → 694 行，横幅 / 骷髅行 / 标签行 / 军衔徽章拆成 `KillCardBannerView` / `KillCardSkullRowView` / `KillCardTagRowView` / `KillCardBadgeView`，共用 `KillCardAnim` / `KillCardPalette`（纯提取，版式与动效保持不变）。**修了一次加载失败**：0.6 新增的 11 个配置键漏了 `ConfigPanel.For` 的面板分类映射，游戏加载插件时直接抛 `ArgumentException` 整个失败；现已补齐，并新增离线测试 `ConfigPresentationFacts` 静态对账（以后新键漏映射会在 `dotnet run` 阶段就报出来）。**实机已用 DebugHarness 验收**：主菜单 53 张截图（普通 ✕ / 爆头 ✕ / 蓝灰盾 / 护甲配色、卡片入场·稳定·退场、2~4 杀累加卡片）；`排布方式 = 累加` 另跑 15 张（单目标 76→152→294→370 滚动累计、击杀变红、多目标垂直堆叠）。验收脚本已按 ≤20 张/次拆成 `tools/verify/0.6-hitmarkers.txt` / `0.6-cards.txt` / `0.6b-accum.txt`（超过 20 张时 `quit` 会触发 Unity `ScreenCapture` 队列的 `0xC0000005` 退出，与插件逻辑无关）。 |
| 0.5.3 | **修连续战局命中反馈消失**：同一进程第 2 局起命中标记 / 命中音效整局不出现（击杀正常）。实测 `GlobalEventDispatcher` 实例跨局不变、换局时 `OnBeingHit` 订阅被游戏清掉，旧版只在启动订阅一次；现在**每局开局强制重绑**（幂等），三连局实测 99 条命中事件全部收到。**战局边界改用主玩家判定**（`GameWorld.MainPlayer.Profile` 出现/消失），连带修正连杀 / 每局击杀 / 得分 / 成就跨局累计与开局不重载音效、不清空 UI。新增 `[events] raid started (main player)` / `OnBeingHit 已绑定` / `本局第一发 OnBeingHit` 与 `[diag] raidState=` 诊断日志。配置版本不变（3）。 |
| 0.5.2 | **音效空间模式默认 `Flat2D` → `FollowCamera3D`**（配置版本 2 → 3，只重置仍是 `Flat2D` 的；`FollowCamera3D` 是唯一在战局里确认过出声的模式）；**热重载迭代**（`-p:HotReload=true`、`reload-scripts.ps1`、`waitmenu`）及完整的卸载路径（画布 / 音频宿主 / 图标与字体缓存 / TMP 材质实例，逐代泄漏实测为 0）；**字体**：停止探测注定失败的 Arial / Segoe UI，修正「填 Microsoft YaHei」的误导建议，启动时写字形覆盖日志，新增 `glyphs` 指令与 `font-check.txt`；**设置面板分类顺序修复**（`00` 原来排在最后、`90` 排在最前：ConfigurationManager 按首次绑定顺序排，不看 `Order`）；设置面板实机验收（枚举 / 颜色 / 偏移 / 滑块 / 快捷键 / Reset / 持久化）；
遥控模式 `watch` + `send-input.ps1`；Issue 模板；文档对齐（测试数、cfg 分组名 `4. 音频`、`VictimIsLocalPlayer`）；**卸载清理健壮性**：`OnDestroy` 各清理步骤彼此独立，任一步失败不拖累其余，并在末尾补一次幂等的 `GameEvents.Shutdown()`（兜住旧代次同帧 Update 重新订阅 `OnBeingHit`）。 |
| 0.5.1 | 修复战局中音效数据清空后持续无声：完整性检查与异步恢复、资源保活及旧请求回收；315 项离线测试与实机脚本通过，用户真实战局实测确认修复。 |
| 0.5.0 | 设置面板分类与控件改进、程序化徽记调整；加入完整资源的安装 ZIP、统一版本号与自动 CI / tag / Release 流程。离线验证不能替代设置面板实机验收。 |
| 0.4.0 | **击杀卡片与命中标记按 战地 6 实录重做**（两段原片逐帧拆解，见 §9）：**累加式卡片**（窗口内连杀并到同一张卡：骷髅并排 / 分数累计并从屏幕上的数字滚起 / 标签取并集 / 横幅与名字换最新）；**故障动效**（入场：标签最先出现、元素先以实心色块闪出再显影、分数数字乱跳着滚到终值；退场：骷髅+名字先闪灭、标签随后、分数最后硬切；可在配置里关掉）；**多杀横幅**（浅钢色底 + 深色字 + 档次强调色，双杀~五杀右边带骷髅徽记）；**标签**（击杀 / 爆头 / 近战 / 爆炸 / 狙击 / 距离的描边小方框）；**军衔徽章挪到卡片最左边并放大**（40 → 72px + 阵营色描边底板 + 等级小标）；**程序化图标**（骷髅含爆头 V 形裂口 / 多杀徽记 / 命中标记 / 标签框，SDF 绘制，任意尺寸边缘干净）；**命中标记深色描边**（旧图在白色背景上整个消失）+ **击杀红色扩散标记** + 淡出时向外扩散；伤害数字深色描边、爆头 / 击杀配色；卡片默认水平偏移 107 → 190（分数不再压命中标记）；**一次性配置升级**（只重置值仍等于旧默认的样式项，调过的保留，旧版独有的键清出 cfg）；**DebugHarness**（环境变量驱动的脚本化进图验证，手动步进 UI 时钟 + 引擎自截图）；**修了旧版两个缺陷**：`UiTextRow/UiImageRow` 的缩放在 `Hide()` 之后永远写不进去（NaN 哨兵比较，骷髅 / 得分 / 徽章的弹出动画其实从来没生效过）；命中图标 `中心偏移` 的 Y 符号与配置说明相反（默认 `0,5` 往上抬了 5px 而不是往下），伤害数字也改为跟随同一偏移；离线测试 113 → 306 项 |
| 0.3.0 | 击杀卡片按战地 6 重做：**修掉「击杀播报文字永远不显示」的 bug**（可见性判断循环依赖，军衔徽章/名字/阵营/详情全跟着一起出来）；新增**得分弹窗**（计数 + 上浮 + 弹出）、**成就缎带**（复仇者 / 双杀 / 狂战士 / 掠夺者 / 枪手 / 神射手… 12 种，带擦除展开与呼吸）、**连杀进度条**；卡片逐行错开滑入、骷髅回弹弹出；命中提示爆头配色/尺寸倍率/弹出；伤害数字弹出；军衔徽章改为启动时探测 + 档位向下借用 + 日志可见，**徽章默认尺寸 86→40、垂直偏移 -30→0**（86 是没显示过时的值，真渲染会盖到骷髅行）；行位置锁定到纯数学层（可离线测）+ 新增 `tests/` 离线测试（90 项）；捕获每帧字符串分配；新增 `[ui] canvas geometry` / `[kill] 屏幕坐标` 诊断行；`ExperienceTextWidth` / `ColorExperience` 等配置项改名为 `得分*` |
| 0.2.7 | 音效在**进图时重载** —— 主菜单场景创建的 clip 会随场景切换被销毁，这是"加载正常但播放无效"的最终原因；音量项改用 `AcceptableValueRange` 真正钳制；兜底路径不再当告警 |
| 0.2.6 | **`DownloadHandlerAudioClip.compressed` 必须显式设 false** —— Unity 的 `GetAudioClip()` 内部写死为 true，数据保持压缩态使 `GetData()` 全为 0，克隆出来的副本必然静音。同时补上 clip 被销毁后的自愈重载 |
| 0.2.5 | 音频源配置退回旧项目三行 —— 投机加的 `bypassEffects/bypassListenerEffects/bypassReverbZones` 会绕过 AudioListener 效果，而 Meta XR Audio 正靠它出声；这些改为默认关闭的排障项，并新增「空间模式」2D/3D 切换。加载日志补 PCM 峰值、listener 数量、驱动信息 |
| 0.2.4 | 修音效无声：`DownloadHandlerAudioClip` 的 clip 被 `request.Dispose()` 一起销毁。改为「能克隆就克隆，否则持有请求保活」；`SoundPlayer` 每条提前返回都告警 |
| 0.2.3 | 默认参数按旧项目实机 cfg 校准；命中点投影加边界校验（越界退回屏幕中心）；测试快捷键不再被「调试日志」开关连坐；AudioSource 绕过 listener 暂停/音效链；伤害数字补描边颜色 |
| 0.2.2 | 去掉 `PatchManager` 与 `SPTushonka.Reflection` / `0Harmony` 依赖 —— `autoPatch:true` + 零补丁会抛异常让插件加载失败。初始化改为分步 try/catch |
| 0.2.1 | 删掉 `GameWorld.OnDestroy` 补丁 —— IL2CPP 方法体共享导致 detour 命中 3360 个方法，游戏无法启动 |
| 0.2.0 | IL2CPP 迁移完成：事件源换成 `GlobalEventDispatcher.OnBeingHit` + `Player.OnPlayerDeadStatic`；uGUI + TMP；Icons/Sounds 资源加载；环形/辉光特效 |
| 0.1.0 | 探针版：验证 interop 引用链路 + 事件源判决 |


## 14. 设置面板

ConfigurationManager 的分类按使用顺序排列：00 常用与整体界面、10 命中提示、20 伤害数字、
30–33 击杀卡片（内容 / 布局 / 配色 / 动画）、40 击杀图标特效、50 成就与计分规则、
60 音效、90 调试与排障。常用开关、位置、大小、停留时间和主要颜色默认显示；
细部布局、动画曲线参数、成就阈值、计分公式、自定义音效路径和排障开关需要启用
ConfigurationManager 的 **Advanced / 高级设置**。配置版本是内部状态，不在面板显示。

- 布尔值使用勾选框；伤害取值、图标来源、音效空间模式使用枚举下拉框。
- 有范围的数值使用管理器内置滑块及数字输入；音量显示百分比，其余数值保留实际单位。
  范围现在使用 BepInEx 的 AcceptableValueRange，手填超界值会被钳制。
- 颜色保留原来的字符串存储，面板提供 HEX 输入、色块预览、R/G/B/A 滑块（0–255）。
  HEX 支持 #RRGGBB 与 #RRGGBBAA。未完成或非法输入不覆盖已保存的有效颜色。
- 命中中心偏移使用独立 X / Y 数字输入，X 向右、Y 向下；cfg 仍存储 X,Y。
- 字体名、自定义音效路径保留文本输入；快捷键使用管理器原生按键编辑器。
- 悬停说明注明用途、单位、依赖开关与生效时机。卡片样式在下一次击杀时完整更新，
  音效路径在下一局开始时重载。

分类调整通过 Category / Order / IsAdvanced 等显示元数据实现（ConfigPanel.cs），
cfg 的 section、key、类型保持原有定义，无需删 cfg（0.5.2 起配置版本为 3，唯一变化是音效空间模式的默认值，见 §6）。
因此旧文档中的“5. 界面”等是 cfg 分类，面板应到上面的新分类寻找。
元数据采用 [ConfigurationManager 官方约定](https://github.com/BepInEx/BepInEx.ConfigurationManager/blob/master/ConfigurationManagerAttributes.cs)，不增加管理器 DLL 的硬依赖。

验证边界：离线逻辑测试不加载 Unity，不能验证管理器实际排版，面板只能进游戏看。

### 实机验收（2026-10-19，ConfigurationManager 19.0.0）

用 DebugHarness 遥控模式（`remote.txt`，见 §11）+ `send-input.ps1` 发真实键鼠输入、逐步截图，并直接读 cfg 文件确认落盘：

| 项目 | 结果 |
|---|---|
| F12 打开面板、插件以 `AyaHitmarker 0.5.1` 列出 | 通过 |
| 默认视图只显示 47 项；勾选 Advanced 后高级项（黄色标签）出现，包括整个 `90. 调试与排障` | 通过 |
| 枚举下拉（`取值`：`Body Only` / `Total`） | 通过：选择后 cfg 立即变成 `取值 = Total`，Reset 回 `BodyOnly` |
| 颜色控件：HEX 输入框 + 色块 + R/G/B/A 滑块 | 通过：输入未完成（5 位）时提示「未保存：需 6/8 位 HEX」，**cfg 与色块不变**；补成 6/8 位后保存并同步色块；`#12345678` 完整落盘 |
| 命中中心偏移 X / Y 双输入框 | 通过：`12` → cfg `12,5`；输入 `1x` 时停在上一个有效值 `1,5` |
| 范围滑块（拖动 + 数字框） | 通过：`基础大小` 拖到 357.3 并落盘，Reset 回 71 |
| 原生快捷键编辑器（`Press any key combination` / `Clear` / `Reset`） | 通过：捕获按键并落盘，Reset 回 `F8` |
| 值持久化 | 通过：每次修改 cfg 文件立即更新；测试结束后 cfg 与测试前逐项一致 |
| **分类顺序** | **未通过 → 已修复**（见下） |

**分类顺序的坑**：原来的实现靠分类名的数字前缀（`00` … `90`）和 `Order` 来排，**ConfigurationManager 并不按这个排**——
分类的先后 = 各分类的条目在 ConfigFile 里**首次绑定**的先后（实测改 `Order` 没有任何效果）。
`AyaConfig` 里 `0. 调试` 最先绑定、`5. 界面` 最后绑定，于是面板上 `90. 调试与排障` 排最前、`00. 常用与整体界面` 排最后
（默认视图里 UI 缩放 / 图标样式沉在最底下）。0.5.0 / 0.5.1 都带着这个问题发布。
修法：构造函数最前面按期望顺序先绑每个分类的一个代表条目（`UiScale` → `HitMarkerEnabled` → … → `DebugEnabled`），
其余条目照旧在各自小节里绑定；这些条目的 section / 键 / 默认值不变，已有 cfg 的值与小节顺序实测不受影响。
修复后实测：默认视图 `00 → 10 → 20 → 30 → 31 → 40 → 60`，高级视图 `00 → … → 40 → 50 → 60 → 90`。
**新增分类 / 条目时，记得让每个分类的第一个绑定仍然按这个顺序**（`AyaConfig` 构造函数开头的「分类顺序锚点」）。

仍存在的小瑕疵（管理器或布局造成，不影响功能）：枚举 `FollowCamera3D` 被管理器显示成 `Follow Camera3 D`；
颜色输入未完成时的「未保存：需 6/8 位 HEX」提示在窄栏里被挤成三行。
