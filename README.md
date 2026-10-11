# AyaHitmarker

SPTushonka / Escape From Tarkov **1.2.0.0 (build 47965)** 的命中反馈插件：**命中提示 + 伤害数字 + 击杀卡片 + 音效**。
命中/伤害/骷髅动效按旧 Mono 项目 [`SimpleHitMarker`](file:///E:/EFTDev/Client.0.16.9.5.40743/Dev/SimpleHitMarker) 的逻辑逐行移植；
击杀卡片在其基础上按 **战地 6** 的击杀反馈重做：得分弹窗 + 成就缎带（复仇者 / 狂战士 / 枪手 / 掠夺者 …）+ 连杀进度条。

> 目标是替代旧 Mono 版 [`SimpleHitMarker`](file:///E:/EFTDev/Client.0.16.9.5.40743/Dev/SimpleHitMarker)（SPT 4.1，Mono）。
> 迁移到 IL2CPP 后旧流程整体失效，本文档记录的是**实测过**的结论。
>
> AI / 新同事请先读 [`AGENTS.md`](AGENTS.md)。

---

## 0. 30 秒上手

```powershell
cd D:\EFTdev\dev\AyaHitmarker
dotnet build                                   # 0 warning / 0 error，通过后自动部署
dotnet run --project tests/AyaHitmarker.LogicTests   # 离线逻辑测试（90 项）
```

进游戏以后按 `F8` / `F9` / `F10` 就能看到各种击杀反馈（无需真的打架）：
`F9` 每次轮换一种地形（PMC 爆头远程 → SCAV 近战 → 首领爆炸 → 连杀 …），连按几次就能看到双杀 / 狂战士 / 掠夺者。

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
# ALL PASS (90 checks) → 退出码 0；有失败逐条打印 expected/actual → 退出码 1
```

测的是**可判定的行为契约**：哪次击杀该出哪条缎带、得分怎么加、连杀进度到哪一档、卡片行序与间距。
测不了「好不好看」—— 那只能进图看。详见 [`tests/AyaHitmarker.LogicTests/README.md`](tests/AyaHitmarker.LogicTests/README.md)。

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

**击杀卡片的元素与动效**（详见 §9）：阵营+军衔徽章 / 骷髅行 / 连杀进度条 / 名字 / 详情，
外加战地风格的**得分弹窗**与**成就缎带**。每行按 `CardRowStagger` 依次滑入，
动画一律写 `localScale`（不触发 TMP 重排）与 `anchoredPosition`，尺寸/字号只在出生时写一次。

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
| `0. 诊断` | 诊断日志开关、`F8` 测试命中 / `F9` 测试击杀（轮换地形）/ `F10` 模拟阵亡 |
| `1. 命中提示` | 开关、基础大小、显示时长、动画缩放、**弹出比例/时长**、中心偏移、着色、**爆头着色与尺寸倍率**、**显示在命中点**（默认关） |
| `2. 伤害数字` | 开关、**取值**（`BodyOnly` 旧行为 / `Total` 肉体+护甲）、字号、颜色、描边颜色与宽度（爆头单独配色）、**弹出比例/时长**、距图标间距、多段间距、存活时长 |
| `3. 击杀信息` | 开关、锚点水平/垂直偏移、内容宽度、行间距、显示时长；**卡片动效**（入场时长/位移/行错开/淡出时长、骷髅弹出）；**得分弹窗**（开关/字号/图标/偏移/上浮/计数时长）；**成就缎带**（开关/字号/高度/留白/偏移/出现·停留·淡出、四档底色、脉冲）；**连杀进度条**（开关/宽度/高度/间距/配色）；骷髅大小·间距·停留·淡出·推挤速度·上限；连杀重置窗口；阵营/名字/详情/方式/首领名称各字段开关与字号；8 种阵营配色；玩家名描边 |
| `3b. 击杀特效` | 环形特效开关与爆头/暴击/爆炸三套半径·粗细·颜色、延迟、时长；图标辉光开关/颜色/强度/扩散 |
| `3c. 击杀成就` | 复仇/多杀/枪手/掠夺者四个判定窗口、远程与神射手距离、精锐等级阈值；得分基础值与五项加成（爆头/远程/复仇/多杀/首领） |
| `4. 音效` | 开关、总音量、四类音量、四个自定义文件路径（留空自动找 `Sounds/`） |
| `5. 界面` | UI 缩放、画布层级、**字体名称**（默认空 = 游戏自带字体；要显示中文就填 `Microsoft YaHei`） |

> **默认值不是代码默认值，而是旧 Mono 项目那份已经调过的实机配置**（`com.shiunaya.simplehitmarker.cfg`）。
> 所以 `水平中心偏移 = 107`、`骷髅头大小 = 40`、`基础大小 = 71`、`显示时长 = 0.868` 这些数字看着眼熟是故意的。
>
> BepInEx 的配置文件会把**首次运行时的默认值写死在磁盘上**，改代码默认值不会影响已存在的 cfg。
> 要拿到新默认值，删掉 `BepInEx/config/com.aya.ayahitmarker.cfg` 重启即可。

### 0.2.x → 0.3.0 的配置迁移

不删 cfg 也能正常跑（BepInEx 会把新键按新默认值补上），但会有几个**孤儿键**不会再被读取：
`显示经验值` / `经验值字号` / `经验值颜色` / `经验值文字宽度` / `经验值水平偏移`（已被
`显示得分` / `得分字号` / `得分颜色` / `得分文字宽度` / `得分水平偏移` 取代）
和 `进度条垂直偏移`（取代为 `进度条距骷髅间距`）。
想要一份干净的配置就删掉 cfg 让它重建；但注意删掉会**一并丢掉你调过的数值**。

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

## 8. ⚠️ 音效为什么这么难：三个坑，且第一个判断错了两次

排查花了五轮。按**结论可信度**排序：

| # | 事实 | 证据 | 修法 |
|---|---|---|---|
| 1 | **主菜单场景里创建的 AudioClip 会随场景切换被销毁** | 加载日志说 `ok`，进 raid 后 `clip 不可用 [Hit=missing ... destroyed ...]`；播放失败触发兜底重载**在 raid 内**重建后立刻正常 | **每个 raid 开始时清掉重载**（`OnRaidStarted`） |
| 2 | **`UnityWebRequest.Dispose()` 会连 handler 的 clip 一起销毁** | 更早一版 `finally { request.Dispose(); }` → 加载正常、播放时假 null | 不 dispose；请求一直持有（`_keepAliveRequests`） |
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
默认值仍按旧项目设成 `Flat2D`；如果 2D 真的无声，就保持 `FollowCamera3D`
（`SoundPlayer.Tick` 每帧把声源挪到 `Camera.main`）。

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

### 音量项的坑

`ConfigDescription(desc, null, new object[]{0f,1f})` 里的 tags **只给 ConfigurationManager 画滑条用，
不做钳制**。曾有人把音量填成 `80`，于是 `vol = 80 × 80 = 6400`，
全靠代码里 `Mathf.Clamp(vol, 0f, 1f)` 才没变成噪音。
音量项现在改用 `new AcceptableValueRange<float>(0f, 1f)`，这才是真正会钳制的写法。

## 9. 击杀反馈视觉设计（对齐战地 6）

**参考**：B 站实战视频 `BV1JWs6zxEno`（24 分钟 / 119 杀）。480p 逐帧看下来，BF6 的击杀反馈是一张
半透明深色卡片，从上到下叠着：① 武器/击杀类型小图标行，② `☠ +100` 得分，③ 一条白字彩色缎带
（`AVENGER` 这类成就名），④ 方框标签（`击杀` / `爆头`）。缎带是整张卡片里最抢眼的东西。

BF6 的成就官方叫 **Accolades（荣誉）**：`先锋 Vanguard` / `枪手 Gunner`（4 秒内用不同武器再杀）/`掠夺者 Marauder`（第 4 杀后 8 秒内再杀）/`侦察专家` / `破碎机` 等，各自给 XP 并累计成荣誉狗牌。
（来源：EA 官方 progression 文章 zh-hans 页；`复仇者 / 宿敌 / 救世主` 这类是社区惯用译名，非官方字符串。）

本插件的卡片把「击杀确认」拆成六行，每一行都有一个开关：

| 行 | 内容 | 动效 |
|---|---|---|
| 成就缎带 | `复仇者 · 142m`（只在触发成就时出现） | 从左往右 `OutBack` 擦除式展开（会过冲一下），停留时宽度轻微呼吸，末尾淡出 |
| 得分弹窗 | `☠ +150` | 从 0 滚到最终值，弹出时带过冲，存活期间向上漂移 26px |
| 阵营行 | `[42] USEC` + PMC 军衔徽章 | 从右侧滑入 |
| 骷髅行 | `☠ ☠ ☠`，连杀时向左推 | 新骷髅按 `SkullPopScale` 弹出，旧骷髅 `Lerp` 推位 |
| 连杀进度条 | 细条，显示到下一档称号的进度 | 宽度平滑收敛（不会因为连杀“跳”一下） |
| 名字 / 详情 | `名字` / `头部 · 142m · 狙击 · M700` | 从右侧滑入，尾部整体淡出 |

### 成就名与触发条件

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

### 得分公式

`基础 100` + `爆头 +50` + `距离≥远程判定 +50` + `复仇 +25` + `多杀 +25 × (连杀段数-1)` + `首领 +200`。
全是配置项。**这不是游戏的权威经验值**（客户端读不到），只是把“这一杀有多难/多解气”量化的反馈。

### 为什么这些判断能测

判定引擎（`Game/KillStreak.cs`）、昵称归一化（`Game/NameKey.cs`）和卡片行位置数学（`Ui/KillCardLayout.cs`）
全部**不引用 Unity/EFT**，所以 `tests/AyaHitmarker.LogicTests` 能直接编译它们并断言“哪一杀出哪条缎带”。
改判定时先看 `KillStreakFacts.cs` —— 那里是这套规则的可执行规格。

---

## 10. 已知限制

- **得分不是权威经验值**：击杀事件读不到「这一击给了多少经验」（真实值走 SkillManager / 战局结算）。
  现在显示的是自己的计分公式（基础 100 + 爆头/远程/复仇/多杀/首领加成），**不是**游戏里的 XP。
  想要真值就得另找数据源。
- **没有 Preset 系统**：旧项目的 8 套 Preset（`StyleCatalog` / `KillIconPreset`）没有移植，
  环形/辉光/缎带参数改由配置直接提供。想恢复预设时只要把这些字段做成一组配置档即可，事件层不用动。
- **军衔图标需要自己放文件**：`Icons/RankIcons/Rank{1,5,...,75}.png`（66×66 最佳）。
  启动时就会探测一次并打日志（`[icons] PMC 军衔图标已加载 N/16` 或 `没有找到任何 PMC 军衔图标`），
  所以不会再出现“分不清是没装图标还是没碰到 PMC”的情况。缺具体档位时会**向下借用最近的可用档位**；
  换文件需要重启（只探一次）。
- **判定阈值以主线程时间为准**：多杀 / 连杀 / 复仇的时间戳在 `FeedbackBus.Drain` 时打，
  事件线程不做 `Time` 读取（不保证主线程，也不值得去碰 Unity）。
- **战局结束只有一路检测**：轮询 `GlobalEventDispatcher` 实例变化（限频 4Hz）。
  所以从战局销毁到 UI 清空最多有 0.25 秒延迟 —— 实际感知不到（那 0.25 秒里画面已经切走了）。
- 自己被打**只做复仇记账、不出提示**。`HitRecord.VictimIsLocalPlayer` 已经有了，想加提示很容易。

## 11. 排查

见 [`AGENTS.md` §5 排查入口](AGENTS.md#5-排查入口) —— 那里列了所有日志关键字及其含义。

最常见的几种：

- `[ui] canvas ready ... font=MISSING` → 字体没解析成功，文本不显示。改 `5. 界面 → 字体名称`。
- `[icons] 没有找到任何 PMC 军衔图标` → 图标文件没放对地方（应为 `Icons/RankIcons/Rank40.png` 这种）。
- 本局完全没有命中提示 → 看有没有 `[events] 无法解析 GlobalEventDispatcher.Instance`。
- 卡片位置/大小看着不对 → 先看 `[ui] canvas geometry` 和 `[kill] 屏幕坐标(左下原点)` 两行（需开诊断日志）。

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

`F8` 命中 / `F9` 击杀 / `F10` 模拟阵亡（需 `0. 诊断 → 启用测试快捷键`）：

- `F9` **不是固定造一条**，而是按脚本表轮换地形：`精锐猎杀 → 枪手 → 猎手 → 双杀 → 复仇者 → 掠夺者 → …`，
  连按几下就能把缎带、得分、进度条、军衔徽章全看一遍。
- `F10` 会把「凶手」设成**下一发 F9 的目标**，所以 `F10` 再 `F9` 必定出「复仇者」。
- 这些快捷键在主菜单就能用（画布在插件加载时就建好了），不用进图。

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
