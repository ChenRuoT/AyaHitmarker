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
- **零补丁，因此不依赖 `SPTushonka.Reflection`。** 命中和击杀全是事件订阅；
  战局结束靠轮询 `GlobalEventDispatcher` 实例变化检测。为什么连框架都不引：见 §7。
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
| `1. 命中提示` | 开关、基础大小、显示时长、动画缩放、中心偏移、着色、**显示在命中点**（默认关） |
| `2. 伤害数字` | 开关、**取值**（`BodyOnly` 旧行为 / `Total` 肉体+护甲）、字号、颜色、描边颜色与宽度（爆头单独配色）、距图标间距、多段间距、存活时长 |
| `3. 击杀信息` | 开关、锚点水平/垂直偏移、内容宽度、行间距、显示时长、骷髅大小/间距/停留/淡出、连杀窗口、推挤动画速度、最多骷髅数、阵营/名字/详情/经验各字段开关与字号、8 种阵营配色、玩家名描边 |
| `3b. 击杀特效` | 环形特效开关与爆头/暴击/爆炸三套半径·粗细·颜色、延迟、时长；图标辉光开关/颜色/强度/扩散 |
| `4. 音效` | 开关、总音量、四类音量、四个自定义文件路径（留空自动找 `Sounds/`） |
| `5. 界面` | UI 缩放、画布层级、**字体名称**（默认 `Microsoft YaHei`，同时覆盖拉丁/西里尔/中文） |

> **默认值不是代码默认值，而是旧 Mono 项目那份已经调过的实机配置**（`com.shiunaya.simplehitmarker.cfg`）。
> 所以 `水平中心偏移 = 107`、`骷髅头大小 = 40`、`基础大小 = 71`、`显示时长 = 0.868` 这些数字看着眼熟是故意的。
>
> BepInEx 的配置文件会把**首次运行时的默认值写死在磁盘上**，改代码默认值不会影响已存在的 cfg。
> 要拿到新默认值，删掉 `BepInEx/config/com.aya.ayahitmarker.cfg` 重启即可。

音效文件名约定（放 `BepInEx/plugins/AyaHitmarker/Sounds/`）：
`hit.ogg` / `headshot_hit.ogg` / `kill.ogg` / `headshot_kill.ogg`（`.wav` / `.mp3` 同样支持，也支持 `hit_headshot` / `kill_headshot` 这类写法）。

PMC 军衔图标（可选）：`BepInEx/plugins/AyaHitmarker/Icons/RankIcons/Rank{1,5,...,75}.png`，没有就不显示。

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
战局边界由 `GlobalEventDispatcher` 实例变化（`GameEvents.Poll()`，限频 4Hz）精确覆盖。

顺带避开的第二个坑：`new PatchManager(this, autoPatch: true)` 在「本程序集里一个 `ModulePatch` 都没有」时
会抛 `PatchException: Could not find any patches defined in the assembly during auto patching`，
**整个插件加载失败**（第二版就是这么挂的）。所以要么真的有补丁，要么别构造它。

将来确实需要补丁时：加回引用 → 写 `ModulePatch` 子类 → `new PatchManager(this, true)` →
**先启动一次并搜 `shares its native body`**。

## 8. ⚠️ 音效：`UnityWebRequest.Dispose()` 会把 AudioClip 一起销毁

症状极其反直觉：

```
[audio] Hit <- hit.ogg            ← 加载阶段一切正常
[audio] 加载完成 hit=True ...      ← 四个 clip 全 True
[debug] 测试命中                   ← 播放被调用
（然后什么都没有 —— 没有报错、没有声音）
```

原因：`DownloadHandlerAudioClip` **持有**它解出来的 `AudioClip`。加载完顺手
`request.Dispose()` 会连 handler 一起释放，那个 clip 的原生对象随之销毁。
之后 `AudioBank.Get()` 拿到的是一个 **Unity 假 null** —— 托管包装还在、`!= null` 在 C# 意义上成立，
但 Unity 重载的 `== null` 返回 true，于是 `Play()` 静默 return 了。

旧 Mono 项目从不 dispose 请求，所以没踩到；是我自己加了个 `finally { request.Dispose(); }`。

**修法**（`Assets/AudioBank.cs`）：
1. 首选**克隆**：`clip.GetData()` → `AudioClip.Create()` → `SetData()`，得到一份自己拥有的副本，
   之后才能安全地 dispose 请求。
2. 克隆失败（例如流式压缩取不出样本）就把请求塞进 `_keepAliveRequests` **一直持有**，
   靠 handler 的存活保住 clip。
3. `SoundPlayer.Play` 的**每一条提前返回都会 `WarnOnce`**，同类原因每种音效只告警一次。
   静默 return 是这次排查成本这么高的直接原因。

## 9. ⚠️ 音效：不要把 AudioSource 配置得"太聪明"

这个版本的 EFT 用 **Meta XR Audio** 接管空间音频（启动日志里能看到）：

```
[Message: Unity] Applying Acoustic Propagation Settings: [acoustic model = AcousticRayTracing], [diffraction = True]
[Message: Unity] Meta XR Audio Native Interface initialized with Unity plugin
[Message: Unity] Setting spatial voice limit: 196
[Message: Unity] No Meta XR Audio Room found, setting default room
```

于是"多设几个绕过开关让它更稳"的想法是**反效果**的。Unity 文档里
`AudioSource.bypassEffects` 会一并绕过 **AudioListener** 上的效果 —— 而那正是 Meta XR Audio
把声音渲染出来的环节。一次排查中 AudioSource 被设成了：

```csharp
bypassEffects = true; bypassListenerEffects = true; bypassReverbZones = true;
```

结果是：clip 真实（`loadState=Loaded`、长度正确）、`AudioListener.volume=1`、`pause=False`、
`isPlaying=True`，**但完全没有声音，且没有任何 Unity 告警**。

**结论：音频源保持旧 Mono 项目的三行即可**（`playOnAwake=false` / `spatialBlend=0` / `volume=1`）。
那些绕过开关现在都是默认关闭的排障项（`4. 音频` 分组）。

如果 2D 仍然无声，`4. 音频 → 空间模式` 可以切成「3D 跟随相机」——走游戏自己的声场管线。
（3D 模式下 `Tick()` 每帧把声源挪到 `Camera.main` 的位置。）

## 10. 已知限制

- **经验值是估算**：击杀事件读不到「这一击给了多少经验」（真实值走 SkillManager / 战局结算）。
  公式沿用旧项目：100 基础 + 爆头 50。旧项目默认就显示它，这里也默认显示。
- **没有 Preset 系统**：旧项目的 8 套 Preset（`StyleCatalog` / `KillIconPreset`）没有移植，
  环形/辉光的参数改由配置直接提供。想恢复预设时只要把这些字段做成一组配置档即可，事件层不用动。
- **PMC 军衔图标从未真正存在过**：旧项目代码里会去读 `RankIcons/RankN.png`，但仓库里没有这些文件。
  本仓库补上了加载器，把文件丢进 `Icons/RankIcons/` 就会生效。
- **战局结束只有一路检测**：轮询 `GlobalEventDispatcher` 实例变化（限频 4Hz）。
  所以从战局销毁到 UI 清空最多有 0.25 秒延迟 —— 实际感知不到（那 0.25 秒里画面已经切走了）。
- 自己被打**不提示**（只提示「我打中了别人」）。事件里带 `VictimIsLocalPlayer`，想加很容易。

## 11. 排查

见 [`AGENTS.md` §5 排查入口](AGENTS.md#5-排查入口) —— 那里列了所有日志关键字及其含义。

最常见的两种情况：

- `[ui] canvas ready ... font=MISSING` → 字体没解析成功，文本不显示。改 `5. 界面 → 字体名称`。
- 本局完全没有命中提示 → 看有没有 `[events] 无法解析 GlobalEventDispatcher.Instance`。

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
| 0.2.5 | 音频源配置退回旧项目三行 —— 投机加的 `bypassEffects/bypassListenerEffects/bypassReverbZones` 会绕过 AudioListener 效果，而 Meta XR Audio 正靠它出声；这些改为默认关闭的排障项，并新增「空间模式」2D/3D 切换。加载日志补 PCM 峰值、listener 数量、驱动信息 |
| 0.2.4 | 修音效无声：`DownloadHandlerAudioClip` 的 clip 被 `request.Dispose()` 一起销毁。改为「能克隆就克隆，否则持有请求保活」；`SoundPlayer` 每条提前返回都告警 |
| 0.2.3 | 默认参数按旧项目实机 cfg 校准；命中点投影加边界校验（越界退回屏幕中心）；测试快捷键不再被「调试日志」开关连坐；AudioSource 绕过 listener 暂停/音效链；伤害数字补描边颜色 |
| 0.2.2 | 去掉 `PatchManager` 与 `SPTushonka.Reflection` / `0Harmony` 依赖 —— `autoPatch:true` + 零补丁会抛异常让插件加载失败。初始化改为分步 try/catch |
| 0.2.1 | 删掉 `GameWorld.OnDestroy` 补丁 —— IL2CPP 方法体共享导致 detour 命中 3360 个方法，游戏无法启动 |
| 0.2.0 | IL2CPP 迁移完成：事件源换成 `GlobalEventDispatcher.OnBeingHit` + `Player.OnPlayerDeadStatic`；uGUI + TMP；Icons/Sounds 资源加载；环形/辉光特效 |
| 0.1.0 | 探针版：验证 interop 引用链路 + 事件源判决 |
