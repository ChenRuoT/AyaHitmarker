# 探查局（0.6 之前）操作说明

> 背景与判定目标见 [ROADMAP.md](ROADMAP.md) 的「探查局」一节。探针代码是
> [`Game/ReconProbe.cs`](../Game/ReconProbe.cs)，**只在 cfg 的 `0. 调试 → 启用调试日志 = true` 时输出**，
> 不参与玩法判定、不产生 UI / 音效。

## 一、准备（已由开发侧完成）

1. `dotnet build` 已把带探针的 DLL 部署到游戏目录（普通构建，不是热重载）。
2. 打开 `BepInEx/config/com.aya.ayahitmarker.cfg`，确认：

   ```ini
   [0. 调试]
   启用调试日志 = true
   ```

   （也可以在游戏内 ConfigurationManager 勾选 Advanced 后的 `90. 调试与排障` 里打开。
   当前机器的 cfg 里它已经是 `true`，通常不用改。）
3. 正常启动游戏（SPT 启动器）。

确认探针生效：进主菜单后 `LogOutput.log` 里应能看到 `[diag]` 行，并且带
`reconHits=.. reconKills=..` 两个字段。

## 二、你需要打的一局（真实战局，10 分钟内）

进一局真实的离线 raid（普通玩法，**不需要打赢**），按顺序做四件事：

1. **S2 挨打**：故意让一个 scav / bot 打中你 1–2 次（站原地别还手），别立刻死。
   目的是看「受害者是我」时 `OnBeingHit` 到底有没有触发。
2. **S1 打甲**：找一个 PMC / raider（有护甲的 bot），先打一枪胸口（打中防弹板），
   再补 2 枪身体或头；然后对**第二个** bot 再命中至少 1 次。
   目的是看同一目标多次命中的 `ProfileId` 是否稳定、不同目标是否不同。
3. **S3 击杀**：击杀 2–3 个 bot，最好包含 1 次爆头。击杀后原地等 3–5 秒再看下一件。
4. **（可选）** 捡一件地上的物品、用一次药，顺便看 `ExpLooting` / `ExpHeal` 是否也更新。

然后正常撤离或阵亡，退出游戏。

## 三、把日志发我

- `BepInEx/LogOutput.log` 整份，或只保留含这些前缀的行：`[recon-`、`[diag]`、`[events]`、`[hit]`、`[kill]`。
- 方便的话说一下大致时间线（比如「第 3 分钟挨了两枪，第 5 分钟打死两个 bot」），对照日志更快。
- 日志可能含昵称 / ProfileId；不想给的话把 `victim=` / `profile=` 后面的值打码，保留「相同/不同」关系即可。

## 四、我会从日志里看什么

| 问题 | 证据（日志关键字） | 通过标准 → 决定 |
|---|---|---|
| S1 受害者 ProfileId | `[recon-hit] ... victim=名字(ProfileId)`、`[recon-kill]` | id 非 `Unknown`，同一 bot 多次命中的 id 相同、不同 bot 不同 → 0.6 b/c 可做 |
| S2 我方被打 | 挨打时是否出现 `victimLocal=True` 的 `[recon-hit]`；`dir=/origin=/normal=` 是否有效 | 有事件且方向可用 → 0.6 e 可做；没有事件 → 受击提示要换数据源 |
| S3 真实经验 | `[recon-xp]` 的 `totalSessionExp=`、`ExpKill=`、`ExpKillBase=`、`ExpKillBodyPartBonus=`、`ExpKillStreakBonus=`、`Kills=` 在击杀后是否变大 | 变大 → 把自编得分换成游戏真值 |
| S4 当前语言 | 离线已初步定位 `EFT.LocalizationManager.Instance.Culture`（interop 公开的字符串） | 真实环境确认一次即可 → 0.7「自动」档 |

补充：`[diag]` 里的 `reconHits` / `reconKills` 是**回调总次数**（含被过滤的）。
如果挨打时这些数字在涨、却没有 `victimLocal=True` 的 `[recon-hit]`，说明事件到了但归属判定没通过，
和「根本没有事件」是两回事 —— 两种情况 0.6 e 的做法不同。

## 五、注意

- 诊断日志量大，**一局足够**，打完就关掉开关。
- 探针只读：不改存档、不写游戏数据、不需要改任何配置默认值。
- 如果出现 `[recon-xp] 读取 SessionCounters 失败`，把那一行原样带上 —— S3 需要换 API 路径再试。
