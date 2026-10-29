<div align="center">

# AyaHitmarker

**为塔科夫带来清晰、即时的命中与击杀反馈。**

参考 Battlefield 6 的累加击杀卡片，适配 SPTushonka / EFT IL2CPP。

[![Release](https://img.shields.io/github/v/release/ChenRuoT/AyaHitmarker?style=flat&color=0891b2)](https://github.com/ChenRuoT/AyaHitmarker/releases/latest)
[![CI](https://github.com/ChenRuoT/AyaHitmarker/actions/workflows/ci.yml/badge.svg)](https://github.com/ChenRuoT/AyaHitmarker/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-64748b?style=flat)](LICENSE.txt)

<p>
  <img src="docs/assets/tag-hit.svg" width="112" height="28" alt="命中与护甲">
  <img src="docs/assets/tag-card.svg" width="122" height="28" alt="累加击杀卡片">
  <img src="docs/assets/tag-xp.svg" width="122" height="28" alt="游戏规则经验">
  <img src="docs/assets/tag-presets.svg" width="122" height="28" alt="12 套外观预设">
  <img src="docs/assets/tag-language.svg" width="122" height="28" alt="中文 / English">
</p>

**[下载安装](https://github.com/ChenRuoT/AyaHitmarker/releases/latest)** · [版本说明](docs/releases/0.7.1.md) · [问题反馈](https://github.com/ChenRuoT/AyaHitmarker/issues) · [路线图](docs/ROADMAP.md)

</div>

## 功能

| 反馈 | 内容 |
|---|---|
| 命中与伤害 | 普通 / 爆头 / 护甲反馈，伤害数字支持逐条显示或按目标累加 |
| 击杀卡片 | 窗口内击杀并卡、累计经验、多杀与成就横幅、军衔徽章、故障动效 |
| 战局信息 | 受击方向点阵、助攻提示、返回主菜单后的战局小结 |
| 声音 | 命中 / 爆头命中 / 击杀 / 爆头击杀四类音效，支持自定义文件与可选连杀音效 |
| 外观与语言 | 12 套预设、高对比与色盲友好配色、中英文界面和设置面板、可选昵称音译 |

**0.7.1**：击杀经验改按实际后端规则计算，新增日志时间戳与音效恢复度量。三局经验对账差值均为 0，实战命中与击杀音效听感正常；详见[版本说明](docs/releases/0.7.1.md)。

## 下载与安装

**适用环境**：SPTushonka 5.x · EFT **1.2.0.0 / build 47965** · BepInEx 6 **IL2CPP / CoreCLR 10**。
运行时由游戏整合包提供，玩家无需安装 .NET SDK。旧 SPT 4.x / Mono 请使用 [SimpleHitMarker](https://github.com/ChenRuoT/SimpleHitMarker)。

1. 在 [最新 Release](https://github.com/ChenRuoT/AyaHitmarker/releases/latest) 下载 `AyaHitmarker-v版本号-EFT-1.2.0.0.zip`。
2. 关闭游戏，将 ZIP 内的 **BepInEx** 文件夹解压到 **EscapeFromTarkov.exe 所在目录**，更新时允许覆盖。
3. 通过 SPT 启动器进入游戏。基础图标、16 个军衔徽章和四类音效已随包提供。

> GitHub 的 **Source code** 是源码。安装请下载带版本号的 ZIP。

<details>
<summary>检查安装目录 / 更新与卸载</summary>

```text
游戏根目录/
├─ EscapeFromTarkov.exe
└─ BepInEx/plugins/AyaHitmarker/
   ├─ AyaHitmarker.dll
   ├─ Icons/
   ├─ Sounds/
   └─ 安装说明.txt
```

更新时保留 cfg，程序会自动迁移仍为旧默认值的设置；无需删除配置。
卸载时删除 `BepInEx/plugins/AyaHitmarker/` 即可。

</details>

## 常用设置

在游戏内 **ConfigurationManager** 调整设置，细项需开启 **Advanced / 高级设置**。
也可在首次运行后关闭游戏，编辑 `BepInEx/config/com.aya.ayahitmarker.cfg`。

| 想调整什么 | 面板位置 |
|---|---|
| 预设、语言、UI 缩放 | `00. 常用与整体界面` |
| 命中标记、受击方向 | `10. 命中提示` / `11. 受击方向` |
| 战局小结、伤害数字 | `12. 战局小结` / `20. 伤害数字` |
| 卡片内容、布局、配色、动画 | `30–33. 击杀卡片` |
| 音量与自定义音效 | `60. 音效` |
| 测试快捷键、音效空间模式、诊断日志 | `90. 调试与排障`（高级） |

<details>
<summary>12 套预设与生效方式</summary>

`Default` 战地 6 默认 · `Legacy` 经典 · `Minimal` 极简 · `HighContrast` 高对比

`Tactical` · `Operator` · `Bloodbath` · `Headhunter` · `Slick` · `NightOps` · `Inferno` · `Plague`

选中预设后自动套用，选择器回到 `None`。预设会覆盖其涉及的外观设置，位置偏移、音效、快捷键和语言保留；套用前自动备份 cfg，保留最近 5 份。
卡片样式在下次击杀时完整更新，自定义音效路径在下一局开始时重载。图标辉光仅对 `CustomFiles` 生效。

</details>

## 经验口径与兼容边界

- **显示击杀经验**：基础值 + 爆头奖励 + 本局累计击杀 combo 奖励，整局倍率按 **×1**。不含伤害、拾取、探索等经验，因此不等于结算总经验或最终到账值。
- **输入缺失时隐藏数值**：小结标记“不可用”。`MarkOfUnknown` 特殊 SCAV 减益的取整尚未核实，该分支同样隐藏经验并告警。
- **音效默认 `FollowCamera3D`**：三局实战听感正常；`Flat2D` 尚未实测。联机模组兼容性也尚未验证。
- **成就采用限时连杀**：默认相邻两杀间隔不超过 10 秒；2.5 秒内优先显示多杀横幅。[完整触发条件](docs/DEVELOPMENT.md#achievements)。

[经验公式与源码证据](docs/recon-kill-experience-47965.md) · [三局对账](docs/recon-0.7.1-raid03.md) · [完整限制](docs/DEVELOPMENT.md#limitations)

## 常见问题

<details>
<summary>没有反馈、文字缺失或中文变成方块？</summary>

先核对安装目录，避免在 `plugins/` 下多套一层版本名目录。
**字体名称保持留空**，使用游戏自带字体及中文回退链；当前版本无法将系统字体转成 TMP 字体。
仍异常时，附上日志中的 `[ui] canvas ready` 和中文字形检查记录。

</details>

<details>
<summary>没有声音？</summary>

检查音效开关、总音量和分类音量，确认 `Sounds/` 文件齐全。空间模式建议保持默认 `FollowCamera3D`；首次加载可能稍有延迟。
持续无声时，请附上 `[audio]` 相关日志。

</details>

<details>
<summary>语言没切换，或战局小结没出现？</summary>

语言默认跟随游戏，设置面板需重新打开才更新文案。
小结在返回主菜单、菜单可操作后显示；无活动的局（如只进出藏身处）不显示。延迟、位置和开关可在 `12. 战局小结` 调整。

</details>

<details>
<summary>如何测试反馈或报告问题？</summary>

开启测试快捷键后，可在主菜单按 `F8` / `F9` / `F10` / `P` 模拟命中、击杀、阵亡和受击方向。
这能检查界面，真实事件与听感仍需进战局验证。

[提交问题](https://github.com/ChenRuoT/AyaHitmarker/issues)时请附游戏版本、插件版本，以及 `BepInEx/LogOutput.log` 中与 AyaHitmarker 相关的记录；提交前移除个人或认证信息。

</details>

## 开发与文档

构建需要 .NET 10 SDK 及游戏 interop 依赖；游戏路径用 `Directory.Build.local.props` 覆盖。

```powershell
dotnet build -p:DeployToGame=false
dotnet run --project tests/AyaHitmarker.LogicTests
```

1239 项离线检查覆盖纯逻辑契约；渲染、动画、音效与设置面板仍需游戏验证。

| 文档 | 内容 |
|---|---|
| [开发与实测记录](docs/DEVELOPMENT.md) | 架构、配置、卡片设计、调试工具、历史证据与完整变更记录 |
| [构建与发布](docs/RELEASING.md) | 编译依赖、安装包校验与 CI 发布 |
| [真实战局验收](docs/field-checklist.md) | 当前实测证据与待覆盖项目 |
| [路线图](docs/ROADMAP.md) | 后续计划 |

## 致谢与许可

本项目延续 [SimpleHitMarker](https://github.com/ChenRuoT/SimpleHitMarker)，视觉参考 Battlefield 6。
感谢 kmyuhkyuk、Amands2Mello、qianyelinghua，以及 SPT / SPTushonka、BepInEx、Il2CppInterop 和 TextMeshPro 的开发者。
代码采用 [MIT 许可](LICENSE.txt)；随包资源的来源与权利说明见[第三方说明](THIRD-PARTY-NOTICES.md)。
