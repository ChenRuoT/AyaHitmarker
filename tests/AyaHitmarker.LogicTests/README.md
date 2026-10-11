# AyaHitmarker.LogicTests

离线测试：**只编译纯逻辑源文件**，不加载 Unity、不加载游戏程序集，`dotnet run` 就能跑。

```powershell
cd D:\EFTdev\dev\AyaHitmarker
dotnet run --project tests/AyaHitmarker.LogicTests
# 全部通过 → ALL PASS (90 checks)，退出码 0
# 有失败   → 逐条打印 expected/actual，退出码 1
```

## 为什么能脱离游戏跑

被 `<Compile Include>` 进来的文件刻意不引用 `UnityEngine` / `EFT`：

| 文件 | 覆盖的东西 |
|---|---|
| `Game/KillStreak.cs` | 连杀 / 成就判定引擎 + 得分公式 |
| `Game/NameKey.cs` | 昵称归一化（复仇者判定靠它把「同一个人」对上） |
| `Ui/KillCardLayout.cs` | 击杀卡片的行位置数学（行序、行间距、进度条占位） |

`Game/DamageMethod.cs`、`Ui/UiRow.cs` 之类碰 `UnityEngine`/`EFT` 的文件**不能**放进来。

## 这个测试在测什么（以及不测什么）

**测**：可判定的行为契约 ——
哪次击杀应该出哪条缎带（`复仇者 / 双杀 / 狂战士 / 掠夺者 / 枪手 / 神射手 …`）、得分怎么加、
连杀进度条到哪一档、卡片各行顺序与间距不变量。

**不测**：视觉好不好看、动画手感、uGUI 实际渲染结果。
那些只能在游戏里看 —— 用 `F8` / `F9` / `F10` 测试快捷键（见根目录 `AGENTS.md` §5）。

## 加测试

1. 新的可测逻辑写成一个**不引用 Unity/EFT** 的类（纯 `float`/`int`/`string`）。
2. 在 `AyaHitmarker.LogicTests.csproj` 的 `<ItemGroup>` 里加上 `<Compile Include>`。
3. 在 `Program.cs` 里调用你的 `XxxFacts.Run()`，用 `Assert.Suite(...)` 分组。

> ⚠️ 根目录的 `AyaHitmarker.csproj` 必须保持 `<Compile Remove="tests/**" />`。
> 否则插件的默认 glob 会把这里的 `Program.cs` 一起编进去（重复 `Main` + 找不到 Unity 类型）。
> 这和 `ref/` 的处理是同一个原因，见根目录 `AGENTS.md` §3.2。
