# 路线图

> 写于 2026-10-29，基线 **0.7.1**（2026-10-29）。这是计划，不是承诺。
>
> **这是唯一的路线图，只写还没做的事。** 一项做完：在 [开发记录 §13](DEVELOPMENT.md#changelog) 加一行变更记录、写进 `docs/releases/<版本>.md`，
> 然后**从这里删掉**，不在这里留「✅ 已完成」。已发布版本的进度、证据和实测结论都在 [开发与实测记录](DEVELOPMENT.md)（§4 事件源 / §6 配置 / §9 卡片设计 / §10 已知限制 / §13 变更记录）
> 与 `docs/recon-*.md`，不要在这里重复。
>
> 所有条目都必须遵守 [AGENTS.md](../AGENTS.md) §3 的约束（零补丁、两个事件源、单一注入 MonoBehaviour、主线程边界、uGUI 写入纪律）。

## 现状

0.7.1：命中标记、伤害数字（两种排布）、战地 6 式累加击杀卡片、成就、助攻、受击方向、战局小结、12 套外观预设（含高对比 / 色盲友好）、
中英文界面（含设置面板）、昵称音译、音效、配置自动升级、全自动 CI 发布。1239 项离线测试，DebugHarness 可脚本化逐帧截图，ScriptEngine.IL2CPP 支持热重载迭代。

0.7 的真实战局验收暴露了几件决定后续排期的事实：

- **DebugHarness 截图并不完全确定**：0.6.1 基线第一次跑的 `card_after` 与之后两次不一致（同脚本同 DLL，像素差 1.2 万），要做截图回归必须先查清。

## 已发布版本索引

| 版本 | 日期 | 一句话 | 详情 |
|---|---|---|---|
| 0.7.1 | 2026-10-29 | 游戏规则击杀经验 / 单调日志时间戳 / 音效恢复度量，三局经验对账零差值 | [releases/0.7.1.md](releases/0.7.1.md) |
| 0.7.0 | 2026-10-26 | 预设 / 色盲配色 / 英文界面 / 昵称音译 / 战局小结 / 命中级爆头修复 | [releases/0.7.0.md](releases/0.7.0.md) |
| 0.6.1 | 2026-10-25 | 修护甲命中几乎不可见（合并「板阶段 + 身体」事件，区分挡下 / 击穿） | [releases/0.6.1.md](releases/0.6.1.md) |
| 0.6.0 | 2026-10-25 | 受击方向 / 护甲命中区分 / 累加伤害数字 / 助攻提示 / 连杀音效 | [releases/0.6.0.md](releases/0.6.0.md) |
| 0.5.3 | 2026-10-19 | 修连续战局第 2 局起命中反馈消失（每局重绑 `OnBeingHit`，主玩家判战局边界） | [releases/0.5.3.md](releases/0.5.3.md) |
| 0.5.2 | 2026-10-19 | 音效空间模式默认 3D、热重载迭代、字体与设置面板实机验收、分类顺序修复 | [releases/0.5.2.md](releases/0.5.2.md) |
| 0.5.1 | 2026-10-12 | 修战局中音效数据清空后持续无声 | [releases/0.5.1.md](releases/0.5.1.md) |
| 0.5.0 | 2026-10-12 | 设置面板分类与控件、程序化徽记、完整资源安装 ZIP、CI 自动发布 | [releases/0.5.0.md](releases/0.5.0.md) |

完整变更记录（含 0.1–0.4）见 [开发记录 §13](DEVELOPMENT.md#changelog)。

## 探查局（0.6 之前做过一次，结论）

用打开诊断日志的版本进真实战局，回答了四个「可能做不了」的问题（探针 `Game/ReconProbe.cs`，操作说明 [recon-0.6.md](recon-0.6.md)，原始证据 [recon-findings-2026-10-19.md](recon-findings-2026-10-19.md)）：

| # | 问题 | 结论 |
|---|---|---|
| S1 | `OnBeingHit` 里能否稳定拿到受害者 `ProfileId` | ✅ 稳定，且与击杀侧一致（第 2 局 9 个受害者逐一核对）→ 累加伤害数字 / 助攻按目标归组 |
| S2 | 我方被打时 `OnBeingHit` 是否触发，`Direction` / `MasterOrigin` 是否可用 | ✅ 触发且方向可用 → 受击方向指示 |
| S3 | 真实击杀经验能否读到 | ⚠️ **只能部分读**：`Kills` / `HeadShots` / `HitCount` / `CauseBodyDamage` 等战局中实时更新；**`ExpKill` / `totalSessionExp` 战局中恒为 0，死亡结算后才出现**（一局 `ExpKill=115`、`totalSessionExp=231`）；`Exp` / `ExpKillBase` / `ExpKillBodyPartBonus` / `ExpKillStreakBonus` 三局全程为 0 → **不能「读」，只能按规则自己算**（0.7.1） |
| S4 | 游戏当前语言从哪读 | ✅ `EFT.LocalizationManager.Instance.Culture`，取值 `ch` / `en`（启动初期先是 `en`、约 1 秒后变 `ch`），见 [recon-s4-language.md](recon-s4-language.md) |

---

## 0.7.x — 后续覆盖

代码、配置文案与本地验证清单已转入 [版本说明](releases/0.7.1.md)。只保留未完成工作：

- 将三局已归档的真实经验输入固化为离线 fixture；正数 bot 模板、bot 爆头、同组击杀等未覆盖分支继续保留明确边界。
- 核实 `MarkOfUnknown` 特殊 SCAV 减益的取整并补实现；当前遇到该分支隐藏经验并告警。
- 用真实战局日志核对护甲挡下、爆头读取、小结撤离 / 藏身处路径、音效静音原因与恢复窗口，手听 `Flat2D`（用户确认三局当前模式听感正常，Flat2D 明确未测）。

---

## 0.8.0 — 兼容与工程（游戏一更新就有防线）

### A. 版本守卫

- `Game/GameBuild.cs`（纯逻辑）：解析 exe 的 `ProductVersion`（本机是 `1.2.0.0-47965-a0aa6211`）→ `(版本, build, hash)`，宽容处理缺字段 / 空串。
- `Plugin.Load` 里读 `FileVersionInfo.GetVersionInfo(BepInEx.Paths.ExecutablePath).ProductVersion`（托管，不碰 Unity；失败只记日志）。
- `Version.props` 新增 `VerifiedGameBuild=47965`，`GenerateReleaseVersion` target 同时生成 `ReleaseVersion.VerifiedGameBuild`。
- build 不等于已验证值 → 启动打一条醒目的 `[version] 游戏 build=… 未在此版本验证（已验证 47965）…`；**只警告，不禁用功能**。
- **CI 一致性校验**：`tools/release/verify-package.ps1`（或 `Get-Version.ps1`）断言 `buildrefs.lock.json` 的 `gameBuild` == `Version.props` 的 `VerifiedGameBuild`，防止「升了依赖忘了改守卫」。
- 测试：`GameBuildFacts`（含异常格式）。

### B. 游戏更新流程 + 冒烟

- `docs/RELEASING.md` 新增「游戏更新流程」：重新生成 interop（启动一次游戏；游戏更新 → interop 重生成 → 类型 / 字段签名可能变 → 要重编译）→ 打包并更新私有 BuildRefs 仓库
  （`buildrefs.lock.json` 的 `archive / gameBuild / commit / sha256` 四项一起改）→ 改 `Directory.Build.props` 的 `GameDir` 默认值（建议用本地 props 覆盖，别把绝对路径提交）与 `Version.props` 的 `VerifiedGameBuild` →
  `dotnet build`（编译错误指向被改名的字段）→ 冒烟 → 发布。写明哪些**无法自动化**（真实战局的事件源、音效）。
- `tools/verify/smoke.txt`（≤ 20 张）：命中 / 爆头 / 挡下 / 击穿 / 受击 / 击杀 1..3 / 复仇 / 助攻 / 小结 / `lang en|zh` / `preset Default` / `audio status` / `glyphs`。
- `tools/verify/smoke-check.ps1`：跑完读 `LogOutput.log`，断言「`smoke done` 出现、无 AyaHitmarker 的 Error / Exception、`canvas ready font=ok`、`缺字形 0 个`」，退出码给人 / CI 看；
  已知的 `0xC0000005`（`quit` 时 ScreenCapture 队列崩溃）在脚本完成日志存在时视为成功。

### C. 补离线测试

- 抽 `Game/ConfigMigration.cs`（纯逻辑）：`(当前序列化值, 旧默认, 新默认, since, from)` → `Reset / AlreadyNew / Kept / Skip`，把 `AyaConfig.SameValue`（浮点容差、颜色不分大小写）一并搬进去；`ApplyConfigMigration` 只剩「遍历名单 + 调用 + 写回」。
  表驱动测试：版本门控、旧默认被重置、用户调过的保留、幂等。
- 抽 `Ui/PromoteWindow.cs`（纯逻辑小结构）：`HitmarkerLayer.KillStickWindow` 与 `DamageNumberLayer.KillWindow` 共用的 0.06s「事后升级成击杀」窗口；测试窗口内 / 外 / 跨帧 / 边界。
- 把 `PresetApplier.SameSerialized` 挪到 `Game/Presets.cs`（纯逻辑）并补测试。
- 目标：+ 40~60 项断言；`tests/.../*.csproj` 与 AGENTS §2 清单同步。

### D. 截图回归（先确定性，后回归）

1. **非确定性排查**：同一脚本（`0.6-cards.txt`）冷启动 5 次、同一 DLL，逐次像素比对，复现并定位 `card_after` 那类差异（怀疑方向：脚本从 `waitmenu` 的自动时钟切到手动时钟的瞬间、首次加载后异步音频 / 字体重建占帧、与绝对时间相关的闪烁种子）。修到「同 DLL 同脚本逐像素一致」。
2. 把手写的比对固化为 `tools/verify/pixdiff.py`（OpenCV，`python -P`）+ `tools/verify/regress.ps1`（依次跑指定脚本到指定目录，再与基线目录比对，输出差异图与汇总；基线放仓库外 `D:\shots\baseline\<版本>`，不入库）。
3. 用法写进 AGENTS §5，并在 RELEASING.md 的发布前检查里列为「改了 UI 后可选」。

### E. 性能指标

- `[diag]` 增加：每 20 秒窗口内的托管分配量（`GC.GetAllocatedBytesForCurrentThread` 差值 ÷ 帧数）、`Update` 平均 / 最大耗时（`Stopwatch`，**仅 `DebugEnabled` 时**）。
- 先量后改：每帧路径目前没发现明显热点（分配几乎都在出生时的 `Show`），只有 `KillFeedLayer.ScoreString` 在分数滚动动画期间每帧 `ToString()`。有数据再决定是否优化。
- 在战局里 / 主菜单各记一份基线数字进开发记录 §11。

**验证**：离线测试（预计 1185+ → 1250+）；Release `-warnaserror`；`smoke.txt` + `smoke-check.ps1` 在当前 build 上全绿；`regress.ps1` 对 0.7.1 基线 0 差异；一局真实战局看 `[diag]` 的分配与耗时。

---

## 0.9.0 — 功能（候选，开工前再逐项确认，不在此锁定）

| 候选 | 价值 | 成本 / 风险 | 设计要点 |
|---|---|---|---|
| **自定义预设（保存当前外观）** | 自己调好的受击方向 / 卡片外观能一键保存、分享、切换；0.7 的预设机制天然延伸 | 中 | `Presets` 从「全是代码」扩展为「内置 + 文件」：`BepInEx/config/AyaHitmarker/presets/*.json`（只存与默认不同的键）；`保存当前外观` 动作写出并并入并集；选择器的取值改为启动时动态生成；校验沿用 `PresetFacts` 的类型 / 范围 / 受保护键规则；英文文案补齐 |
| **战局历史** | 每局一行 JSON 追加到 `raids.jsonl`，可累计看击杀 / 爆头 / 最远 / 伤害 / 经验趋势 | 小~中 | 复用 `RaidStats.Snapshot`（0.7.1 起含击杀经验）；文件写入放主线程小块、失败只告警；可选在小结里加「历史最佳」行 |
| **更多语言（俄语优先）** | EFT 社区主力语言之一 | 中（约 280 条文案 + `Loc.Pick` 改 N 语言重载，缺省回退英文） | 先做屏幕文字，面板文案分批；`ConfigTextFacts` 的对账模式复用 |
| **判定正确性清理**（0.6 遗留） | 环境 / 流血死亡不再记成助攻、阵亡后不弹助攻、受击无攻击者位置时不画错方向、助攻卡与高卡片不重叠 | 小，每条都有离线测试入口 | 多数在 `AssistTracker` / `HitDirectionLayer`；需要先从真实日志收集样本 |

## 1.0 的标准

- 0.6 / 0.7 / 0.7.1 的功能都经过真实战局验收（0.7.1 的验收清单就是为了把这件事压到一局）。
- 有英文版 ✅（已完成）；有版本守卫、有游戏更新流程文档（0.8）。

**条件性调研（不排期）**：联机。本机没有任何联机模组，联网也没查到可用结论；一旦出现类似 Fika 的联机模组，`IsYourPlayer` 对其他玩家的判定、助攻 / 复仇 / 击杀经验的归属都要重新确认。

## 明确不做

- 不为了「用上框架」加 Harmony 补丁。真到非加不可，按 AGENTS.md §3.0 先确认目标方法体独立。
- 不把 `Player.ApplyShot` / `Player.ApplyDamageInfo` 加回来（[开发记录 §4](DEVELOPMENT.md#events)）。
- 不加第二个托管注入类型，不写自定义 `Graphic` / `MonoBehaviour`（AGENTS §3.3）。
- 「肢体打黑」标签暂不做：要在主线程读健康状态，收益抵不过风险。
- 不碰 BattlEye / `ConsistencyInfo` / `FilesChecker`（AGENTS §3.8）。
- 不改连杀判定的默认值（2026-10-29 用户决定：只在文档里说明，见[开发记录 §9.3](DEVELOPMENT.md#achievements)）；不做「不死亡连杀」的规则重写。
- 没有联机模组环境就不写联机相关逻辑。

## 风险与未知

- **经验特殊分支仍有未覆盖项**：MarkOfUnknown 减益遇到时隐藏经验；正数 bot 模板、bot 爆头和同组击杀仍需真实样本。
- **截图非确定性的根因未知**（0.8 D）：可能是脚本时序而非插件问题；查不出就把回归阈值放宽并只比对结构区域。
- **音效继续观察**：三局请求级静音均为 0、用户确认听感正常；未来场景 / 设备变化仍需保留恢复度量，不外推 Flat2D 已通过。
- **版本守卫只警告**：游戏更新后字段改名会在编译期（升级 BuildRefs 后）而不是运行期暴露。
- 每个版本的真实战局验收都需要打一局：0.7.1 的清单就是为了把这件事压到一局。
