# 探查局第一轮实测（2026-10-19，同一进程三个连续战局）

> 原始日志：`BepInEx/LogOutput.log`（8235 行，单个游戏进程）。行号均指该文件。
> 探针代码：[`Game/ReconProbe.cs`](../Game/ReconProbe.cs)；本轮开始前的部署是 `3bdcd1d` / `ca26c88`。

## 0. 结论速览

| 问题 | 结论 | 证据 |
|---|---|---|
| S2 我方被打时 `OnBeingHit` 是否触发 | ✅ **触发，且 `Direction` / `MasterOrigin` / `HitNormal` 可用** | 6872–6876 连续 5 条 `[recon-hit] victimLocal=True`（被 HANAVI 打：护甲 ×3、身体 ×1、护甲 ×1），字段完整；随后 6877 阵亡 |
| S3 真实击杀经验能否读到 | ✅ **部分能**：`Kills` / `HeadShots` / `HitCount` / `CauseBodyDamage` / `CauseArmorDamage` 战局中实时更新；`ExpKill` 只在**死亡结算后**才出现（115），战局中读不到实时经验 | 7428 / 7975 击杀后计数立即变；7439 阵亡后 `ExpKill=115`、`totalSessionExp=231` |
| S1 命中侧 `ProfileId` | ⚠️ **被下面的 bug 挡住，待第二轮**：击杀侧可稳定拿到（`[recon-kill]` 里两个 bot 的 ProfileId 不同且完整），但玩家自己的命中事件从第 2 局起收不到 | 7422 / 7970 击杀；0 条 `attackerLocal=True` 的 `[recon-hit]` |
| S4 当前语言 API | ✅ 离线查清（见 [recon-s4-language.md](recon-s4-language.md)） | `EFT.LocalizationManager.Instance.Culture` |

## 1. 新发现的严重 bug：连续战局的第 2 局起命中事件丢失

**现象**：第 1 局命中反馈正常；第 2、3 局玩家命中完全收不到 `OnBeingHit`（命中标记 / 命中音效整局消失），但击杀卡片、击杀音效正常。用户在第 3 局观察到的「命中标记和音效消失」就是它。

**证据链**：

- 第 1 局：玩家被 HANAVI 打 → 6872–6876 共 5 条 `[recon-hit] victimLocal=True`，随后 6877 `[recon-kill]` 阵亡。
- 第 2 局：7422 玩家击杀（`killerLocal=True`）、7437 被 Provocator 击杀 —— **全程 0 条 `[recon-hit]`**，连「被打死前那一枪」都没有事件。
- 第 3 局：`SessionCounters` 明确显示玩家打出 15+ 次命中（7967/7969/7975/8088：`HitCount 6→14→15→22→24`、`CauseBodyDamage +15058/+20356`、`CauseArmorDamage +19478`），击杀 7970，却仍然 **0 条 `attackerLocal=True` 的 `[recon-hit]`**。
- 对比：`Player.OnPlayerDeadStatic`（静态事件）三局都正常，说明不是插件整体失效，而是 `GlobalEventDispatcher.OnBeingHit` 的订阅在第 1 局后丢了。

**战局边界也从来没被侦测到**：

- 全日志只有 1 条 `[events] raid started (new dispatcher)`（301 行，启动时），没有任何 `raid ended`。
- `[diag]` 的 `raidKills` 一路累计到 4（含 2 次 F9 测试击杀 + 2 次真实击杀），跨局不归零 → 连杀 / 成就 / 得分在三局之间串了。
- 音频不再随开局重载，靠 0.5.1 的数据完整性检查懒恢复（6832 / 6896 / 7404 / 7456 / 7947 / 8106 每次都自动重载成功；击杀音效在 7426 / 7974 / 7984 确实播放过）。
- 结论：**`GlobalEventDispatcher` 在本版本里不是「每个 raid 一个新实例」**（旧结论来自更早版本）；插件只在启动订阅一次，之后换局时游戏清掉/换掉了 `OnBeingHit` 的委托列表，插件再没重绑。

## 2. 修复（已实现，待第二轮实测验证）

`Game/GameEvents.cs`：

1. **战局边界改用主玩家判定**：`GameWorld.MainPlayer.Profile` 出现 = 进图，消失 = 回菜单/结算；
   `RaidStarted` / `RaidEnded` 改为由这个状态机触发（同时修正 streak / UI / 开局音频重载）。
2. **每局开局强制重绑 `OnBeingHit`**：`-=` 再 `+=`（幂等），覆盖「游戏换局时清掉旧订阅」的情况；
   dispatcher 实例变化时也重绑。
3. 新增日志（始终 Info，不依赖诊断开关）：
   - `[events] OnBeingHit 已绑定（raid started / dispatcher changed，dispatcher=0x…，inRaid=…）`
   - `[events] raid started (main player)` / `[events] raid ended (main player gone)`
   - `[events] 本局第一发 OnBeingHit（victim=…，inRaid=…）`
   - `[diag]` 增加 `raidState=inRaid=… dispatcher=0x…`

> 如果第二轮仍然没有 `[events] 本局第一发 OnBeingHit`，说明游戏实际派发的 dispatcher 与
> `Singleton<GlobalEventDispatcher>.Instance` 不是同一个对象，需要另找入口（不排除只能加补丁/改数据源）。

## 3. 其他发现（对本轮结论的补充）

- **S2 的 `BodyPartColliderType` 全是 `None`**：护甲/身体的区分只能靠 `DidArmorDamage` / `DidBodyDamage`。
  0.6 e 做受击方向不需要 collider，`Direction` / `MasterOrigin` 已经够用。
- **经验结算时机**：击杀后 `Kills` 等计数立刻变，但 `ExpKill` / `totalSessionExp` 在战局中保持 0，
  直到死亡结算才出现（`ExpKill=115`、`totalSessionExp=231`）。所以「实时显示游戏真值经验」在战局中做不到；
  可以用于战局结束小结，或等后续再验证撤离结算的时机。
- `Exp` / `ExpKillBase` / `ExpKillBodyPartBonus` / `ExpKillStreakBonus` 三局全程为 0 —— 客户端会话计数器里只有 `ExpKill` 有值。
- 8105 行 `SessionEndUIScene` 卸载与 8091 行 Unity `NullReferenceException` 是游戏侧日志（没有插件栈），与本 bug 无关。
- 音频：`[audio] play … isPlaying=True` 说明击杀音效确实在播；用户感到的「音效消失」只是命中音效缺失（命中事件没来）。

## 4. 下一轮验证清单（约 2–3 局、10 分钟）

1. 每局开局出现 `[events] raid started (main player)` + `[events] OnBeingHit 已绑定（raid started…）`。
2. 每局至少打中一次：出现 `[events] 本局第一发 OnBeingHit`，命中标记 / 音效恢复。
3. 第 2、3 局同样成立（这是本轮 bug 的判据）。
4. `[diag]` 的 `raidState=`：菜单 `inRaid=False`、战局 `inRaid=True`；dispatcher 指针跨局是否变？
5. 新局 `[diag] raidKills` 从 0 开始（streak 每局归零）。
6. S1 补测：打中同一个 bot 多次 → `[recon-hit] … victim=bot(ProfileId)` 的 id 是否稳定、不同 bot 是否不同。

## 5. 第二轮验证：修复通过（2026-10-19 晚，同一进程三连局）

用户按上面的清单又打了三局，`LogOutput.log`（9357 行）确认修复：

- **逐局重绑**：`[events] OnBeingHit 已绑定（raid started…）` 出现 3 次（6217 / 7726 / 9123），
  dispatcher 指针三局都是 `0x26DFE7244C0` —— 直接证实「实例跨局不变」的根因判断。
- **战局边界**：`raid started (main player)` / `raid ended (main player gone)` 各 3 次（6219/6682、7728/8611、9125/9205），
  `[diag]` 里 `raidState=inRaid=True/False` 切换正常；新局 `raidKills` 从 0 开始。
- **命中事件恢复**：第 2 局 99 条 `[recon-hit]`（修复前同场景 0 条）、第 3 局 6 条；
  `[events] 本局第一发 OnBeingHit` 在第 2、3 局都出现（第 1 局没有交火）。
- 无插件告警 / 错误，`droppedHits=0`；命中音效 64 次、击杀音效 9 次（3 普通 + 6 爆头）与击杀数一致。
- 0.5.3 发布说明见 [releases/0.5.3.md](releases/0.5.3.md)；S1（命中侧 ProfileId 稳定性）现在可以正常补测。

