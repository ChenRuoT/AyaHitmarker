# 0.7.1 真实战局验收

打开 `0. 调试 → 启用诊断日志`，保留每局完整 `BepInEx/LogOutput.log`。
日志行尾 `@t=` 为本代插件加载后的单调秒数；热重载会重新从 0 开始。
至少做三局经验对账；一局可同时覆盖下面其他项目。每项记录“观察到 / 未遇到 / 不通过”，未遇到不能算通过。

## 击杀经验

```powershell
Select-String -Path BepInEx/LogOutput.log -Pattern '\[xp\]|\[recon-kill\]|\[recon-xp\]|\[summary\]'
```

- 首杀后有 `[xp] rules=backend`，记下基础经验、爆头倍率、combo 表；应与本服规则一致。
- 每次我方击杀有 `[xp] ... base= head= combo= total=`；与 `[recon-kill]` 的目标、阵营、模板值相配。
- `templateXp=0` 应保留为 0；负数才走游戏 bot 默认值。AI PMC 走 PMC 基础值。
- 从 `[summary]` 读取 `killXp=`；`missingXp` 必须为 0 才能拿来声称整局对账成功。
- 将逐杀 `total` 求和，与结算后的 `[recon-xp] ... ExpKill=` 比较，目标只允许独立取整误差。
  不与 `totalSessionExp` 或最终升级到账经验比较；此处整局倍率固定 ×1，不含伤害、拾取、治疗等经验。
- 若没有结算 `ExpKill`，记为“缺结算基准”；不能拿计算结果证明自己正确。
- 遇到 `experience unavailable`，保存目标和原因；卡片隐藏经验，小结显示“不可用”。
  `MarkOfUnknown` 特殊 SCAV 减益目前主动隐藏，尚未宣称支持。
- 对账表记录：局号、逐杀总和、summary.killXp、missingXp、ExpKill、差值、异常原因。
  收集完整输入后再添加真实事件 fixture，现有数学样例不算实战证据。

## 护甲与爆头

```powershell
Select-String -Path BepInEx/LogOutput.log -Pattern '护甲判定: Blocked|护甲判定: Penetrated|part=-1'
```

- 真实挡下观察到蓝色反馈，对应 `Blocked`；击穿对应 `Penetrated`。没碰到挡下就保留待测。
- 子弹命中的 `part=-1` 应为 0 次；确认头部命中对应爆头反馈。颈部按游戏归胸部。

## 小结

- 撤离局：`[summary] ... 阵亡=0 ... → 排队等主菜单`；回到可操作主菜单后才显示。
- 藏身处：`无活动（藏身处 / 空局），忽略`，不弹空小结。
- 中英文分别确认“击杀经验 / Kill XP”；任何缺失输入不能显示部分数值当成总数。

## 音效

```powershell
Select-String -Path BepInEx/LogOutput.log -Pattern '本局静音播放|失效重载完成|silentPlays=|数据失效'
```

- 每局结束有 `[audio] 本局静音播放 N 次，失效重载 M 次，最长恢复 X ms`。
- M 按恢复成功的 clip 计数，X 从首次观察到失效到该 clip 可用，使用真实单调时间。
  `pending` 表示局末仍未恢复；不存在“恢复成功”不代表恢复耗时为 0。
- 区分 `clipMissing`（可能吞音）、`disabled`（主动关闭）、`zeroVolume`（主动静音）、`otherFailures`。
  N=0 仅证明没有被插件跳过的播放请求；不证明输出设备实际可听，也不覆盖两次检查之间未被观察的失效。
- 手动切到 `Flat2D` 听命中与击杀，再还原原设置；记录是否可听。

## 主菜单冒烟（单独证据）

`tools/verify/0.7.1-smoke.txt`：固定模拟击杀覆盖中英文经验显示，`xp` 探针单独读取真实后端规则，
`audio metricsbegin/end` 临时开启度量窗口验证失效恢复。它不产生真实击杀或实战经验对账证据。
冒烟完成需有 `0.7.1 smoke done`、4 张截图、后端探针可读、无插件异常。

## 0.7.1 发布前记录

- 三局击杀经验与结算 `ExpKill` 对账：120 / 55 / 499，差值均为 0，详见 `recon-0.7.1-raid01/02/03.md`。
- 三局静音播放请求均为 0；用户确认三局命中和击杀音效听感正常。
- `Flat2D` 用户明确未测，保留为已知未覆盖模式；不标记为听感通过。
- 其他未遇到 / 未完成的分支仍按各局记录和版本说明保留边界，不因发布而改记为通过。
