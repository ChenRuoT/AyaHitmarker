# AyaHitmarker.LogicTests

离线测试：**只编译纯逻辑源文件**，不加载 Unity、不加载游戏程序集，`dotnet run` 就能跑。

```powershell
cd D:\EFTdev\dev\AyaHitmarker
dotnet run --project tests/AyaHitmarker.LogicTests
# 全部通过 → ALL PASS (1185 checks)，退出码 0
# 有失败   → 逐条打印 expected/actual，退出码 1

# 把程序化图标渲染成 PNG 联系表，肉眼检查（不用进游戏）
dotnet run --project tests/AyaHitmarker.LogicTests -- --render-icons D:\somewhere\icons
# → skulls.png / emblems.png / hitmarkers.png / tagbox.png（每张都合成在 深色 / 灰 / 白 / 雪地 / 草地 五种背景上）

# 受击方向点阵的离线预览（和游戏同一套排布 / 时间线 / 点形状，1 画布单位 = 1 像素）
dotnet run --project tests/AyaHitmarker.LogicTests -- --render-hitdir D:\somewhere\hitdir
# → hitdir_directions.png / hitdir_backgrounds.png / hitdir_timeline.png / hitdir_zoom4x.png
```

## 为什么能脱离游戏跑

被 `<Compile Include>` 进来的文件刻意不引用 `UnityEngine` / `EFT`：

| 文件 | 覆盖的东西 |
|---|---|
| `Game/KillStreak.cs` | 连杀 / 成就判定引擎 + 得分公式 |
| `Game/NameKey.cs` | 昵称归一化（复仇者判定靠它把「同一个人」对上） |
| `Game/BodyPartNames.cs` | 命中部位 → 显示名（中 / 英） |
| `Game/HeadshotRules.cs` | 命中级爆头判定：被打中的部位 / 碰撞体编号 / `DamageInfo` 自带字段的优先级（数值取自 dump.cs） |
| `Game/Loc.cs` | 界面语言：设置 + 游戏语言 → 实际语言、`Pick` |
| `Game/NameTranslit.cs` | 西里尔昵称 → 拉丁字母（仅显示） |
| `Game/ColorVision.cs` | 色觉缺陷模拟（Machado 2009）+ CIELAB ΔE76，给高对比预设的对比度断言用 |
| `Game/RaidStats.cs` | 战局小结：累计口径 / 快照 / 文案行 / `SummaryScheduler` 显示时机状态机 |
| `Game/Presets.cs` | 预设目录（12 套）：`PresetFacts` 用静态解析逐键对账 |
| `Game/ConfigTextEn.cs` | 设置面板英文文案：`ConfigTextFacts` 对账每个键都有英文、没有 CJK、交叉引用有效 |
| `Ui/KillCardLayout.cs` | 击杀卡片的行位置数学（行序、行间距、缺席的行不占位、内容左缘、军衔徽章位置） |
| `Ui/KillCardModel.cs` | 卡片**累加模型**：窗口内连续击杀并到同一张卡（骷髅 / 分数 / 标签并集 / 横幅），退场与重新开卡的时间线 |
| `Ui/GlitchMath.cs` | 战地式「故障」动效曲线：色块显影、数字滚动、闪烁熄灭、退场分组错峰 |
| `Ui/UiAnim.cs` | 缓动函数 |
| `Ui/HitDirectionMath.cs` | 受击方向：方位角换算、收尖的二次轮廓、蜂窝点阵排布（峰尖精确指向 / 不重叠 / 纵横渐变）、伤害映射、时间线 |
| `Game/DamageAccum.cs` | 累加伤害数字模型：同目标并槽、击杀变红并从击杀时刻重新计时、透明度时间线 |
| `Assets/IconShapes.cs` | 程序化图标的 SDF 栅格化：骷髅（含爆头裂口）、多杀徽记、命中标记（含描边 / 粗线版）、标签框九宫格 |

`ConfigSourceModel.cs`（测试工程自己的）静态解析 `AyaConfig.cs` / `ConfigPanel.cs`：键名、类型、字面量默认值、范围、面板分类。
`AyaConfig` 依赖 BepInEx 不能离线引用，预设 / 英文面板这类「和全部配置键对账」的契约只能走这条路。

`Ui/UiRow.cs`、`Ui/KillFeedLayer.cs`、`Assets/ProceduralIcons.cs` 之类碰 `UnityEngine`/`EFT` 的文件**不能**放进来
（`ProceduralIcons` 只是把 `IconShapes` 的位图包成 Texture2D，形状本身都在可测的 `IconShapes` 里）。

## 这个测试在测什么（以及不测什么）

**测**：可判定的行为契约 ——
哪次击杀应该出哪条缎带、得分怎么加、连杀进度条到哪一档、卡片各行顺序与间距不变量、
窗口内第二杀是并卡还是新卡、退场期间补杀会不会取消退场、闪烁曲线的起止与硬切、
图标五官 / 描边 / 爆头裂口 / 对称性的像素级位置。

**不测**：视觉好不好看、动画手感、uGUI 实际渲染结果。
那些只能在游戏里看 —— 见根目录 `AGENTS.md` §5 的 **DebugHarness**（环境变量驱动的调试脚本：
手动步进 UI 时钟 + 引擎自截图，一帧一帧验证动画，不需要按键也不需要抢窗口焦点）。

## 加测试

1. 新的可测逻辑写成一个**不引用 Unity/EFT** 的类（纯 `float`/`int`/`string`）。
2. 在 `AyaHitmarker.LogicTests.csproj` 的 `<ItemGroup>` 里加上 `<Compile Include>`。
3. 在 `Program.cs` 里调用你的 `XxxFacts.Run()`，用 `Assert.Suite(...)` 分组。

> ⚠️ 根目录的 `AyaHitmarker.csproj` 必须保持 `<Compile Remove="tests/**" />`。
> 否则插件的默认 glob 会把这里的 `Program.cs` 一起编进去（重复 `Main` + 找不到 Unity 类型）。
> 这和 `ref/` 的处理是同一个原因，见根目录 `AGENTS.md` §3.2。
