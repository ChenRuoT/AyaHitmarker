# 击杀经验公式调查（build 47965）

2026-10-29。目标是呈现游戏规则下的击杀经验。按用户确认的口径，撤离状态与整局倍率取 ×1。
本文记录本机二进制的静态证据；运行时与显示已接线，真实战局对账待完成。

## 证据来源与复查

- `ref/Il2CppDumperOutput/dump.cs` 只提供签名、字段布局与 RVA，**空方法体不提供公式证据**。
- 公式来自本机 `D:/EFTdev/Client.1.2.0.0.47965/GameAssembly.dll` 的只读反汇编。
- 当前数据库：`SPT_Runtime/SPT_Data/database/globals.json` 的 `config.exp.kill`。
- `tools/verify/xp-native.py` 用 dump 的 RVA 和 PE section 映射读取原生代码；依赖可临时安装到已忽略的 `artifacts/xp-research/deps`：

```text
python -m pip install --target artifacts/xp-research/deps capstone==5.0.9 pefile==2024.8.26
python tools/verify/xp-native.py 1B5CE80:2700 38E2B00:49
python tools/verify/xp-native.py 406FAC0:DF0
```

脚本输出的符号名只是 dump 的地址注解；共享方法体可能对应多个名字，应同时核对字段偏移与调用上下文。
不分发游戏 DLL、数据库或完整反汇编产物。

## 已确认的算术

`EFT.BaseStatisticsManager.OnEnemyKill` RVA `0x1B5CE80` 接收 `int killExp`。
寄存器/栈追踪与 `PredefinedCounters` 的布局可以对应出以下步骤：

1. 基础经验 `B` 经过阵营分支解析后，保存在 `r15d`。
2. `0x1B5E3E4` 读取 `PredefinedCounters.Kills`（静态字段偏移 `0x78`）。
   这个计数在 `0x1B5D678` 已经加过 1，使用的是**包括本次在内的本局累计击杀数**。
3. `0x1B5E403..0x1B5E414`：`headBonus = trunc(float(B) * headMultiplier)`。
4. `0x1B5E41D` 调用 `GetKillingBonusPercent(kills)`；
   `0x1B5E422..0x1B5E43D`：`comboBonus = trunc((float(percent) / 100f) * float(B))`。
5. `0x1B5E479`、`0x1B5E4B5`、`0x1B5E4F1` 分别写入三个独立计数器：
   `ExpKillBase`（静态偏移 0）、`ExpKillBodyPartBonus`（8）、`ExpKillStreakBonus`（16）。
6. `EndStatisticsSession` 在 `0x4070396` 构造 `CounterTag.ExpKill`（77 / `0x4D`），
   `0x4070404` 用 `GetAllInt` 汇总该分类，`0x4070430` 写入最终 `ExpKill`。

因此本口径为：

```text
killXP = B + trunc(B * headMultiplier) + trunc((comboPercent / 100f) * B)
```

两个奖励独立向零截断，不能先相加再取整；保持单精度运算顺序。
头部倍率在非爆头时为 0。当前数据库中 PMC 为 1.2、Savage 为 1.1；
这些数是**额外奖励的倍率**，不应减 1，也不应把 `B * 1.1` 当最终总数。

例：默认普通 bot 的 B=55 时，身体首杀 55，爆头首杀 `55 + trunc(60.5) = 115`；
第二次爆头为 `55 + 60 + 11 = 126`。
这与旧探查记录的结算 `ExpKill=115` 数值相容，但旧记录没有冻结模板与完整逐杀输入，
**不能把它宣称为已匹配的真实事件 fixture**。

## Combo 与远程、伤害

`GetKillingBonusPercent` RVA `0x38E2B00` 的下标是 `clamp(killed - 1, 0, Combo.Length - 1)`。
当前表为 `[0,20,30,40,50,60,70,80,90,100]`，第 10 杀起封顶。
这不是插件的多杀窗口或连杀重置窗口，不能复用它们的序号。

`longShotDistance` 在 `0x1B5D3CB` 用于判断并增加 `LongShots` 计数，
没有进入上述三个经验分项的算术。远程成就不应继续给自编经验奖励。
复仇、多杀称号、首领称号也不能直接转成额外经验。

`ExpDamage` 是单独的计数器（`PredefinedCounters` 静态偏移 `0x30`），
`pmcExpOnDamageAllHealth` / `botExpOnDamageAllHealth` 不能凭字段名加进击杀经验。

## 基础经验分支与例外

- `ProfileSettings.Experience` 的字段在 `dump.cs:297703`，与玩家累计升级经验 `Profile.Info.Experience` 不同。
  已追到调用方 `Player.OnBeenKilledByAggressor`（RVA `0x4178D00`）：
  `0x41792B2..0x41792CF` 依次读 Profile.Info（`0x48`）、Info.Settings（`0x90`）、
  Settings.Experience（`0x18`）；`0x417943F` 取出这个值，`0x41794AE` 放进 OnEnemyKill 的 killExp 参数槽。
  因此模板值的来源已由调用方确认。
- Savage 分支在 `0x1B5DD46` 取 `killExp`；若为负，在 `0x1B5DD72` 改用
  `GlobalExpConfig.Kill.VictimBotLevelExp`（当前 55）。**0 是有效值，不能当缺失。**
- PMC 的 USEC / BEAR 分支最终在 `0x1B5DF52` 取 `VictimLevelExp`（当前 120），
  不是 `victim.Level * 120`，也不是用 bot 模板奖励覆盖所有阵营。
- `pmcBEAR`（51）和 `pmcUSEC`（52）在入口改写计算 side 为 2 / 1
  （`0x1B5D127..0x1B5D14F`；枚举见 `dump.cs:297571`），所以这两种 AI PMC 也走 120 的分支。
- `0x1B5D7D0..0x1B5D83F` 比较非空 GroupId；同组在 `0x1B5E061` 跳过经验写入。
  不能把友军击杀当成普通有经验击杀。
- Savage 分支还有 `HasMarkOfUnknown` / `ScavKillExpPenalty` 路径
  （`0x1B5DD8C..0x1B5DDEF`），会先修改基础经验。
  这不是撤离倍率，本次“整局 ×1”的决定不意味着可以忽略它。

## 客户端可读取的配置路径

`HealthStatisticsManager.GlobalExpConfig` 是 protected 的 `GlobalConfiguration.ExperienceSettings`，
证明客户端有这份运行时配置。后端 session 也暴露 `BackEndConfig`，其 `Config.Experience.Kill`
包含全部乘数和 combo 数组。接线应在主线程读取并冻结这份配置，不把本机数据库绝对路径写进产品，
也不直接把今天的 globals 常量作为所有服务端的权威值。

事件侧现已在 `[recon-kill]` 补充 `side / role / templateXp / group` 的值快照，便于下一局收集缺失输入。
读取仍在事件回调内完成，不长期持有玩家代理；配置与计算接线在主线程进行。

## 本次实现与剩余工作

`Game/KillExperience.cs` 实现了已证实的算术与 combo 下标，离线断言覆盖独立取整、
额外爆头奖励、整局 combo 封顶及零经验。这些是原生规则样例测试，不是实机对账。

接线结果：事件侧冻结阵营 / 模板经验 / 同组关系 / AI 标志；主线程从
`Singleton<GlobalConfiguration>.Instance.Experience.Kill`（原生统计管理器的入口；不可用时再查包含 inactive 的 TarkovApplication 后端会话）
读取原生配置作为同步回退；本机菜单实测这些入口为空，因此已增加 SPT 托管 Common 的 RequestHandler.GetJsonAsync("/client/globals") 异步读取实际后端规则。JSON 解析只处理纯数据，结果在 Update 发布；每局重新读取，15 秒超时、失败 30 秒后重试，卸载 / 换局取消旧请求的结果等待。不新增 SPT Reflection / Harmony 引用，不读取或打印认证信息，不采用固定经验常量作为真实路径回退。真实经验作为独立字段传给卡片与小结，成就规则保持独立。
缺少输入时隐藏经验；含缺失项的并卡不显示部分总数，小结显示“不可用”。旧计分配置已删除。

还需完成：

1. 完成特殊减益的精确门控与取整函数核实：当前可见门控还涉及 role 和 isAI，不能仅按“杀了 SCAV”套减益。
2. 收集带完整输入的逐杀真实样本，按三局结算 `ExpKill` 对账；整局倍率始终按 ×1 口径单列解释。

A 已接线但未完成实战对账，不能宣称已通过真实战局验收或已发布。
