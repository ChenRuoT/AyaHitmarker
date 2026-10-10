# AyaHitmarker

SPTushonka / Escape From Tarkov **1.2.0.0 (build 47965)** 的命中反馈插件：**命中提示 + 伤害数字 + 击杀播报 + 音效**。
渲染与动效按旧 Mono 项目 [`SimpleHitMarker`](file:///E:/EFTDev/Client.0.16.9.5.40743/Dev/SimpleHitMarker) 的逻辑逐行移植。

> 目标是替代旧 Mono 版 [`SimpleHitMarker`](file:///E:/EFTDev/Client.0.16.9.5.40743/Dev/SimpleHitMarker)（SPT 4.1，Mono）。
> 迁移到 IL2CPP 后旧流程整体失效，本文档记录的是**实测过**的结论。
>
> AI / 新同事请先读 [`AGENTS.md`](AGENTS.md)。

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

## 3. 架构

```
事件线程                                    主线程（AyaBehaviour.Update）
─────────                                   ──────────────────────────────
GlobalEventDispatcher.OnBeingHit ─┐
Player.OnPlayerDeadStatic ────────┤         FeedbackBus.Drain
                                  ├──► 冻结成 HitRecord / KillRecord ──► HitmarkerLayer   (Icons/hit.png)
                                  │    （只传值类型和字符串，             DamageNumberLayer
                                  │      不碰任何 Unity API）              KillFeedLayer     (Icons/skull*.png
GameWorld.OnDestroy (ModulePatch) ┘                                          + 环形/辉光特效)
                                                                           SoundPlayer       (Sounds/*.ogg)
```

**资源加载**：
- `Icons/` 的 PNG 用 `ImageConversion.LoadImage` 同步读（启动时一次）。
- 环形特效纹理与辉光纹理**运行时烘**（`Assets/IconBank.cs`）：环形按 `厚度/半径` 分桶缓存；
  辉光把旧项目「8+8+1 次偏移叠加」的数学在 CPU 上烘成一张带外边距的纹理 —— 少 16 次绘制，视觉等价。
- `Sounds/` 的 ogg/wav/mp3 走 `UnityWebRequestMultimedia` **协程异步**解码
  （`BepInEx` 的 `WrapToIl2Cpp()` 桥接托管迭代器），主线程全程不阻塞。
  **没有程序化合成音**，也没有 `Thread.Sleep` 轮询。

- **只有一个注入的托管类型**（`Behaviour/AyaBehaviour.cs`）。`BasePlugin` 不是 MonoBehaviour，
  所以由 `Plugin.Load()` 用 `AddComponent<T>()` 注入。其余 UI 全是 interop 自带组件。
- **只有一个 Harmony 补丁**（`GameWorld.OnDestroy`，用来在撤离/战局销毁时清 UI）。
  命中与击杀**没有任何 detour 挂在热路径上**，全是事件订阅。
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
| `0. 诊断` | 诊断日志开关、`F8` 测试命中 / `F9` 测试击杀 |
| `1. 命中提示` | 开关、图标基础尺寸、显示时长、出现时缩放、屏幕中心偏移、着色、**显示在命中点** |
| `2. 伤害数字` | 开关、**取值**（`Total` 肉体+护甲 / `BodyOnly` 旧行为）、字号、颜色、距图标间距、数字之间间距、存活时长 |
| `3. 击杀播报` | 开关、锚点水平/垂直偏移、文本块宽度、行间距、文字停留、骷髅尺寸/间距/停留/淡出、连杀窗口、推动动画速度、最多骷髅数、阵营/名字/详情/经验各字段开关与字号、8 种阵营配色、玩家名描边 |
| `3. 击杀播报 · 特效` | 环形特效开关与爆头/暴击/爆炸三套半径·粗细·颜色、延迟、时长；图标辉光开关/颜色/强度/扩散 |
| `4. 音效` | 开关、总音量、四类音量、四个自定义文件路径（留空自动找 `Sounds/`） |
| `5. 界面` | UI 缩放、画布层级、**字体名称**（默认 `Microsoft YaHei`，同时覆盖拉丁/西里尔/中文） |

音效文件名约定（放 `BepInEx/plugins/AyaHitmarker/Sounds/`）：
`hit.ogg` / `headshot_hit.ogg` / `kill.ogg` / `headshot_kill.ogg`（`.wav` / `.mp3` 同样支持，也支持 `hit_headshot` / `kill_headshot` 这类写法）。

PMC 军衔图标（可选）：`BepInEx/plugins/AyaHitmarker/Icons/RankIcons/Rank{1,5,...,75}.png`，没有就不显示。

## 7. 已知限制

- **经验值是估算**：击杀事件读不到「这一击给了多少经验」（真实值走 SkillManager / 战局结算）。
  默认关闭显示（`3. 击杀播报 → 显示经验估算`）。
- **没有 Preset 系统**：旧项目的 8 套 Preset（`StyleCatalog` / `KillIconPreset`）没有移植，
  环形/辉光的参数改由配置直接提供。想恢复预设时只要把这些字段做成一组配置档即可，事件层不用动。
- **PMC 军衔图标从未真正存在过**：旧项目代码里会去读 `RankIcons/RankN.png`，但仓库里没有这些文件。
  本仓库补上了加载器，把文件丢进 `Icons/RankIcons/` 就会生效。
- **战局结束的检测有两路**：轮询 `GlobalEventDispatcher` 实例 + `GameWorld.OnDestroy` 补丁。
  若某次更新后补丁目标失效，轮询仍能兜住（只是晚一帧）。
- 自己被打**不提示**（只提示「我打中了别人」）。事件里带 `VictimIsLocalPlayer`，想加很容易。

## 8. 排查

见 [`AGENTS.md` §5 排查入口](AGENTS.md#5-排查入口) —— 那里列了所有日志关键字及其含义。

最常见的两种情况：

- `[ui] canvas ready ... font=MISSING` → 字体没解析成功，文本不显示。改 `5. 界面 → 字体名称`。
- 本局完全没有命中提示 → 看有没有 `[events] 无法解析 GlobalEventDispatcher.Instance`。

## 9. 参考

| 资源 | 地址 |
|---|---|
| `SP-Tushonka/modules` @ `5.0x-dev` | 与客户端逐字节对应的官方参考实现 |
| `SPTushonka.Reflection` | `Patching/`（ModulePatch）+ `Il2Cpp/`（Injection / BaseCall / MainThread） |
| `ref/Il2CppDumperOutput/dump.cs` | 全类型/字段/方法 + RVA |
| 旧 Mono 版 | `E:\EFTDev\Client.0.16.9.5.40743\Dev\SimpleHitMarker` |
