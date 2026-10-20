# 路线图

> 写于 2026-10-15，基线 0.5.1；2026-10-19 发布 0.5.2 / 0.5.3 并更新进度。这是计划，不是承诺；每完成一项就在这里勾掉或改写，结论以 README 的实测记录为准。
> 所有条目都必须遵守 [AGENTS.md](../AGENTS.md) §3 的约束（零补丁、两个事件源、单一注入 MonoBehaviour、主线程边界、uGUI 写入纪律）。

## 现状

0.5.3 已发布（2026-10-19）：在 0.5.2 基础上修掉「同一进程第 2 局起命中反馈消失」的严重 bug，
并改用主玩家判定战局边界。命中标记、伤害数字、战地 6 式累加击杀卡片、成就、音效、设置面板、配置自动升级、全自动 CI 发布。
315 项离线测试，DebugHarness 可脚本化逐帧截图，ScriptEngine.IL2CPP 支持热重载迭代。GitHub 上尚无 issue。

**规划的出发点**：两个事件源里还有大量没用上的数据。`DamageInfo` 自带 `Direction` / `MasterOrigin` / `HitNormal` /
`Penetrated` / `BlockedBy`，`DidArmorDamage` 已在 `HitRecord` 里；`OnBeingHit` 的第二个参数就是受害者 `Player`。
所以下面大部分新功能**不需要任何补丁**，不会碰到 §3.0 的方法体共享问题。

---

## 0.5.2 — 收尾与首装体验（2026-10-19 已发布）

> 发布 **v0.5.2**（2026-10-19）。发布前补完的两项收尾（卸载清理健壮性）见本节末尾。

| # | 事项 | 状态 | 结果与证据 |
|---|---|---|---|
| 1 | **默认设置下中文显示** | ✅ 已验证，**前提不成立，无需改默认字体** | 默认字体（游戏自带 Bender，回退链含中文）对屏上全部 114 个中文字符 **缺字形 0 个**，暗底 / 白底 18 张截图无方块。原计划「挂 YaHei 做回退」既不需要也做不到：系统字体名 → `CreateFontAsset` 对 YaHei / Arial / SimHei **全部返回 null**，`new Font(path)` 直接抛异常。实际改动：默认流程不再探测 Arial / Segoe UI（注定失败还漏对象）、失败时销毁 `Font`、启动写一行字形覆盖日志、新增 `glyphs` 指令与 `tools/verify/font-check.txt`；README FAQ / cfg 说明里「填 Microsoft YaHei」的错误建议已更正。详见 README §6「字体」 |
| 2 | **2D 音效实机确认** | ✅ 已按「2D 不可靠」处理：默认改成 `FollowCamera3D`；`Flat2D` 本身是否出声仍待真实战局验证（可选） | 已知事实：本机 cfg 是 `空间模式 = FollowCamera3D`，所以 0.5.1「用户真实战局实测确认」是在 **3D 模式**下做的；默认的 `Flat2D` 至今没有任何战局内证据。主菜单里测不了：Meta XR Audio 在战局里接管空间音频，且菜单音乐会污染任何输出电平的读数。自动化也不合适：开一局会改动存档。**已做（你选了方案 b）**：默认改成唯一确认过的 `FollowCamera3D`，`(SoundSpatialMode, Flat2D)` 进了 `ApplyConfigMigration` 名单（`CurrentConfigVersion` 2 → 3），旧版 cfg 迁移与全新安装都已进游戏验证。想彻底关掉这条，只需某次真实战局里把空间模式切到 `Flat2D` 听一次（有没有声、什么声） |
| 3 | **文档对齐** | ✅ | 测试数 306 → 315（README §0 / §2、tests README；AGENTS.md 本来就是 315，README §8 / §13 里的 306 是当时的历史记录，保留）；`UiTheme.Font` 注释改为实际流程；README §10 的 `VictimIsLocalPlayer` 说法更正（「我被打」在 `OnGlobalBeingHit` 里就被过滤了，要做被击提示得先放开过滤并验证 S2）；README §6 补「cfg 分组 ↔ 面板分类」对照表，并更正分组名 `4. 音效` → `4. 音频` |
| 4 | **设置面板实机验收** | ✅ 通过，并修了一个真 bug | 用遥控模式 + 键鼠输入工具逐项验收（枚举 / 颜色 / 偏移 / 滑块 / 快捷键编辑器 / Reset / 持久化，结果见 README §14）。**发现并修复**：分类顺序不是设计的 `00 → 90`，而是绑定顺序（`90` 最前、`00` 最后）——ConfigurationManager 不看 `Order`；改成在 `AyaConfig` 构造函数最前面按期望顺序先绑每个分类的代表条目 |
| 5 | Issue 模板 | ✅（未在 GitHub 上渲染过） | `.github/ISSUE_TEMPLATE/bug_report.yml`（插件版本 / 游戏版本 / 哪里不正常 / 现象 / 复现 / 日志 / 脱敏确认）+ `config.yml`（旧版 SPT 4.x 指向 SimpleHitMarker）。YAML 表单要推送后才能看到实际效果 |
| 6 | 工作区清理 | ✅ | 已做：删掉空的 `y/`；`tools/verify/evidence/` 加进 `.gitignore`（README §8 本来就写明「不随源码发布」）；`.gitignore` 里本地论坛素材的两条保留；0.5.2 的全部改动已随 `e82da96` 等提交，`git status` 干净。AGENTS.md 是你有意 `.gitignore` 的本地入口，不算「脏」 |

### 0.5.2 期间新增的工具与发现（不在原计划里）

- **热重载迭代**（ScriptEngine.IL2CPP）已在真实游戏里验证：14 代连续重载 0 error、多代后截图与第 1 代逐字节相同、画布不叠、Texture2D / Sprite / TMP 字体 / Font / Material 逐代增量实测为 0（修复前每代 +2 / 0 / 0 / +2 / +5）。详见 README §11。
- **遥控模式**：`watch` 指令 + `tools/verify/send-input.ps1` + `remote.txt`，用于需要「先发输入再看画面」的交互式验证。
- **✅ 已完成（0.5.2 发布前）**：`AyaBehaviour.OnDestroy` 的清理改为每项独立 `try`（`Cleanup(label, action)`），任一步失败只告警、不拖累画布 / 图标 / 字体 / 音频宿主的回收；并在 `OnDestroy` 末尾补一次幂等的 `GameEvents.Shutdown()`，兜住旧代次同一帧还有一次 `Update` 时 `Poll()` 重新订阅 `OnBeingHit` 的情况（`Shutdown` 自带 `_staticSubscribed` / `_lastDispatcher` 判空，可重复调用）。

---

## 0.5.3 — 连续战局命中反馈修复（2026-10-19 已发布）

> 发布 **v0.5.3**。探查局第一轮实测暴露的严重 bug：同一进程第 2 局起命中标记 / 命中音效整局消失（击杀反馈正常）。

| 事项 | 结果与证据 |
|---|---|
| 根因 | `GlobalEventDispatcher` 实例在 EFT 1.2.0.0 里**跨局不变**（三连局日志里指针始终 `0x26DFE7244C0`）；游戏换局时会清掉 `OnBeingHit` 的订阅，而旧版只在启动订阅一次 → 第 2 局起收不到命中事件。旧「每局一个新实例」的判据实测推翻（README §4 / §10） |
| 修复 | 战局边界改用 `GameWorld.MainPlayer.Profile` 出现/消失判定；**每局开局强制重绑 `OnBeingHit`**（先 `-=` 再 `+=`，幂等）。连带修正 streak / 每局击杀 / 得分 / 成就跨局累计、开局不重载音效、不清空 UI |
| 验证 | 第二轮同一进程三连局：3 次 `raid started/ended`、逐局 `OnBeingHit 已绑定（raid started）`、第 2/3 局都有 `本局第一发 OnBeingHit`；第 2 局 99 条命中事件（此前是 0），无插件告警；315 项离线测试通过 |

---

## 探查局（0.6 之前做一次）

做一个打开诊断日志的版本，进**一局真实战局**，一次回答下面四个问题。每一项都可能得出「做不了」的结论，所以先查清再排期。

| # | 问题 | 怎么查 | 决定什么 |
|---|---|---|---|
| S1 | `OnBeingHit` 里能否稳定拿到受害者 `ProfileId`？ | handler 里已经在读 `victim` 的昵称，多打一个 `ProfileId` | 0.6 的 b / c（「是不是同一个目标」） |
| S2 | 我方被打时 `OnBeingHit` 是否触发？`Direction` / `MasterOrigin` 是否可用？ | 在 `IsYourPlayer` 过滤**之前**对「受害者是自己」的事件打日志 | 0.6 的 e |
| S3 | **真实击杀经验能否读到** | `dump.cs` 里有 `CounterTag.ExpKill` / `ExpKillBase` / `ExpKillBodyPartBonus` / `ExpKillStreakBonus`。主线程在击杀前后读本局 `SessionCounters` 的差值，看客户端是否在战局中就更新 | 能读到就把「自编得分公式」换成游戏真值（README §10 第一条已知限制） |
| S4 | 游戏当前语言从哪个 API 读 | 查 `dump.cs` 里的本地化 / 设置类型 | 0.7 英文版的「自动」档 |

注意：探查代码只在事件线程上**读值并转字符串**（§3.4），`SessionCounters` 的读取放主线程。

> **准备状态（2026-10-19）**：诊断探针已就位（[`Game/ReconProbe.cs`](../Game/ReconProbe.cs)，只在 cfg 的
> `0. 调试 → 启用调试日志` 打开时输出）——`[recon-hit]`（S1/S2 原始事件：`victimLocal` / `ProfileId` /
> `Direction` / `MasterOrigin` / `HitNormal`）、`[recon-kill]`（击杀双方 ProfileId）、
> `[recon-xp]`（主线程 1Hz 读 `SessionCounters` + `TotalSessionExperience`）、`[diag]` 增加 `reconHits` / `reconKills`。
> **S4 已离线查清**（见 [recon-s4-language.md](recon-s4-language.md)）：没有语言枚举，读
> `EFT.LocalizationManager.Instance.Culture`（interop 已把 internal 生成为 public），`ch` → 中文、其余 → English，
> 只剩真实环境确认一次取值格式。用户操作步骤见 [recon-0.6.md](recon-0.6.md)，**打一局真实战局**即可收集 S1–S3。

> **第一轮实测结果（2026-10-19，同一进程三连局）**：S2 ✅ 触发且方向可用；S3 ✅ 部分可读
> （击杀计数实时、`ExpKill` 要等结算）；S1 ⚠️ 被上方 0.5.3 修的订阅 bug 挡住。详见
> [recon-findings-2026-10-19.md](recon-findings-2026-10-19.md)。
>
> **第二轮实测（0.5.3 修复后，同一进程三连局）**：3 次 `raid started/ended`、每局开局 `OnBeingHit 已绑定`
> （dispatcher 指针三局都是 `0x26DFE7244C0`，证实根因）、第 2/3 局都有 `本局第一发 OnBeingHit`；
> 第 2 局收到 **99 条命中事件**（修复前是 0）。
>
> **✅ S1–S4 全部回答完毕**：S1 命中侧 `ProfileId` 稳定且与击杀侧一致（第 2 局 9 个受害者逐一核对）；
> S2 挨打事件 + 方向可用；S3 击杀计数实时、`ExpKill` 要等结算；S4 读 `LocalizationManager.Instance.Culture`。
> **下一步按原计划进入 0.6：先拆 `KillFeedLayer`，再做 a / d，然后 b / c。**


---

## 0.6 — 命中反馈加深（零补丁）

**动卡片之前先拆 `Ui/KillFeedLayer.cs`**（1482 行）：横幅 / 骷髅行 / 标签行 / 军衔徽章拆成各自的视图类，否则 c 要往这个文件里继续堆。

> **进度（2026-10-20）**：**a ✅ / b ✅ / d ✅ 已实现**（0.6.0 开发中，离线测试 315 → 454 项；
> 盾形图标用 `--render-icons` 验过 `armor_markers.png`；b 保留了「一行向右」作为默认样式，新样式是设置项）。
> **c（助攻）和 e（受击方向）未做**；`KillFeedLayer` 拆分仍未开始（做 c 之前必须先拆）。
> 待进游戏验收：护甲盾 / 累加数字 / 连杀音效（音效文件可先不放，验证回退普通击杀）。

| # | 功能 | 价值 | 成本 / 风险 |
|---|---|---|---|
| a ✅ | **护甲命中区分**：`DidBodyDamage == 0 && DidArmorDamage > 0` 时换成蓝灰色盾形标记，伤害数字也换色 | 塔科夫特有、信息量大：知道自己是不是一直在打防弹板。旧项目也没做 | 已完成：`IconShapes.ArmorMarker`（白芯 + 黑描边 + 辉光，像素契约测试 24 条）、命中标记第三形态、护甲配色、配置开关 |
| b ✅ | **战地 6 式累加伤害数字**：同一目标的伤害在同一位置滚动累计（`32 → 67 → 99`），击杀时变红 | README §9.2 记为未做 | 已完成：`Game/DamageAccum.cs` 纯逻辑（115 条断言）+ 视图垂直堆叠；受害者 `ProfileId` 冻结进 `HitRecord`；设置项 `排布方式 = 一行向右 / 累加`，默认保留旧样式 |
| c | **助攻**：我打过的目标在 N 秒内被别人打死，卡片上出一行「助攻 +X」 | bot 混战、将来联机都有用 | 中。`GameEvents.OnPlayerDead` 现在非我方击杀直接返回，需要放开并记账；和 b 共用受害者 ID。**开工前先拆 KillFeedLayer** |
| d ✅ | **多杀 / 连杀音效**：可选 `double_kill` / `triple_kill` / `multi_kill` 文件 | 现在只有 4 个槽（`AudioBank.SoundKind`）；连杀数据已经算好，只交给了卡片，没交给声音 | 已完成：`SoundKind` 扩到 7 槽、可选文件缺失静默回退普通击杀音效、配置开关 + 三个自定义路径 |
| e | **受击方向指示**（默认关） | 新旧两个项目都没有 | 中。依赖 S2；要放开 `OnGlobalBeingHit` 的过滤。塔科夫原版刻意不提供这类提示，所以默认关闭 |

**下一步**：拆 `KillFeedLayer` → 0.6 c → 0.6 e → 0.6.0 进游戏验收 + 发版。

---

## 0.7 — 个性化与国际化

- **预设系统**（README §10 记为缺失）：旧项目 8 套（tactical / operator / bloodbath / headhunter / slick / nightops / inferno / plague），
  再加 战地 6 默认 / 旧版 SimpleHitMarker / 极简 / 高对比（色盲友好）。一套预设 = 一组「(键, 值)」，套用时写进对应 `ConfigEntry`。
  离线测试断言「每个预设里的键都真实存在、值在范围内」。
- **英文版**：**只翻译显示层** —— ConfigurationManager 的 `DispName` / 说明文字，以及屏幕上的横幅、标签、击杀方式。
  **cfg 的键名不改**（改键名 = 新键 + 旧键成孤儿，见 AGENTS.md §1）。`语言 = 自动 / 中文 / English`，自动档依赖 S4。
  横幅文案在纯逻辑的 `Game/KillStreak.cs` 里，可以离线测。
- **高对比 / 色盲配色**：现在普通与爆头主要靠红白区分。
- **战局小结**：战局结束时快照本局击杀数、爆头、最远一杀、最高连杀、拿到的成就、总伤害，回到菜单后显示几秒。
  统计聚合写成纯逻辑可测；显示仍走现有画布。
- **昵称音译**（可选）：移植旧项目 `Utils/LocalizedHelper.cs` 的 `Transliterate`，处理西里尔字母的 bot 名字。

---

## 0.8 → 1.0 — 兼容与工程

- **版本守卫**：启动时读游戏 build 号，不是 47965 就记一条「未在此版本验证」的警告。
- **游戏更新流程**：重生成 interop → 更新 BuildRefs（`tools/release/buildrefs.lock.json` 三个字段一起改）→ 编译 →
  跑新写的 `tools/verify/smoke.txt` 冒烟脚本（命中 / 击杀 / 阵亡 / `audio status` / 截图）。写进 [RELEASING.md](RELEASING.md)。
- **扩大离线测试**：配置升级逻辑（`ApplyConfigMigration` 现在只能进游戏验）、命中标记「升级成击杀」的 0.06s 窗口状态机；
  ~~0.6 的累加模型~~ ✅ 已完成（`Game/DamageAccum.cs`，测试总数 315 → 454）。
- **截图回归**：用 OpenCV 把 DebugHarness 截图和基线图对比（本地跑，需要游戏）。
- **性能指标**：`[diag]` 增加每帧 GC 分配量（`GC.GetAllocatedBytesForCurrentThread`）与 `Update` 耗时。
- ~~热重载适配~~ ✅ 已完成并实测（见 0.5.2）：`Plugin.Unload()` 销毁 `AyaBehaviour`，`OnDestroy` 拆画布 / 音频宿主 / 图标与字体缓存 / TMP 材质实例；
  `Plugin.PluginDirectory` 取 `Assembly.Location` 在引擎下可用（引擎垫了该值），所以**不要改它的来源**；DLL 不能同时部署到 `plugins/` 与 `scripts/`，由 csproj 的两个互斥 target 保证。
- **1.0 的标准**：0.6 / 0.7 的功能都经过真实战局验收，有英文版，有版本守卫，有游戏更新流程文档。

**待调研**：联机。SPTushonka 5.x 是否有类似 Fika 的联机模组？有的话 `IsYourPlayer` 对其他玩家的判定、助攻 / 复仇的归属都要重新确认。

---

## 明确不做

- 不为了「用上框架」加 Harmony 补丁。真到非加不可，按 AGENTS.md §3.0 先确认目标方法体独立。
- 不把 `Player.ApplyShot` / `Player.ApplyDamageInfo` 加回来（README §4）。
- 不加第二个托管注入类型，不写自定义 `Graphic` / `MonoBehaviour`（§3.3）。
- 「肢体打黑」标签暂不做：要在主线程读健康状态，收益抵不过风险。
- 不碰 BattlEye / `ConsistencyInfo` / `FilesChecker`（§3.7）。

## 建议顺序

0.5.2（字体回退 + 2D 音效确认 + 文档对齐）→ 探查局 → 拆 `KillFeedLayer` → 0.6 a、d → 0.6 b、c → 0.7 → 0.8 / 1.0。
