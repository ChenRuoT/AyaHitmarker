# S4 调研：游戏当前语言从哪个 API 读

结论（已证实）：EFT 1.2.0.0 没有 `ELanguage` / `ELocale` 枚举，界面语言是 **string locale id**（"ch"/"en"/…）。
权威存储是 `EFT.Settings.Game.GameSettingsGroup.Language`（`GameSetting<string>`）；运行时最省事的读法是
`EFT.LocalizationManager.Instance.Culture`（interop 把 internal 成员生成成了 public，可直接调用）。

## 1. 存储类型 / 字段（已证实）
| 证据 | 位置 |
|---|---|
| `public sealed class GameSettingsGroup : SettingsGroup<GameSettingsGroup>` | dump.cs:354144 |
| `public readonly GameSetting<string> Language; // 0x18` | dump.cs:354149 |
| `Bsg.GameSettings.GameSetting<T>`；`public T Value { get; set; }` | 1492890 / 1492904 |
| `EFT.Settings.SettingsManager`；`public readonly SettingsWithController<GameSettingsGroup,...> Game; // 0x10` | 351811 / 351817 |
| `SettingsManager.SettingsWithProvider<T>`；`public readonly T Settings; // 0x0` | 351649 / 351652 |
| `EFT.LocalizationManager`（plain class，非 MonoBehaviour） | 336933 |
| `internal static LocalizationManager Instance` / `internal string Culture` / `public static string DefaultLanguage` | 336960 / 336963 / 336961 |
| 语言常量 `RUSSIAN="ru"` `ENGLISH="en"` `FRENCH="fr"` `GERMAN="ge"` | 336937-336940 |
| 变更通知 `public Action AddLocaleUpdateListener(Action)` | 336999 |

没有枚举：grep `\bELanguage\b` / `\bELocale\b` 零命中；近似物只有 `Helpers.Languages {Any,En,Ru}`（136876，文本解析用）
和 TMP 的 `LanguageDirection`（975489，与游戏语言无关）。

## 2. 从插件怎么读（按优先级）
**A. 推荐：`EFT.LocalizationManager.Instance?.Culture`（string）**
- 类型 public（336933）；`Instance`/`Culture` 在 dump 里是 internal，但 interop 元数据实测为
  **public static `get_Instance` / public `get_Culture`**（Assembly-CSharp.dll），C# 可直接写，
  **不需要反射，也不需要 `Comfort.Common.Singleton<T>`**。
- 它表示"本地化实际在用的 locale"，最贴近"游戏当前语言"（语义见 §5，属推测）。
- `AddLocaleUpdateListener`（336999）可订阅语言切换，但跨 il2cpp delegate 有坑，非必需。

**B. 设置值：`ItemUiContext.Instance.SettingsManager.Game.Settings.Language.Value`**
- `EFT.UI.ItemUiContext : UIInputNode`（550766）；`public static ItemUiContext Instance`（550940）；
  私有属性 `SettingsManager`（550979，getter 551153）→ interop 里是 public。
- 链路：`SettingsManager.Game`（351817）→ `.Settings`（351652）→ `Language`（354149）→ `GameSetting<string>.Value`（1492904）。
- `SettingsWithProvider<T>` 是嵌套类型，interop 元数据确认 **NestedPublic**；需 csproj 增加 interop 引用
  `BSG.GameSettings.dll`（`$(InteropDir)/BSG.GameSettings.dll`，`ref/` 已有 §3.2 排除规则）。
- 风险：`ItemUiContext` 是 UI 节点，生命周期里可能为 null；getter 实现看不到（无 IL），不作唯一路径。

**C. `SettingsScreen.SettingsScreenController.OriginalSettings`**（591317/591328，public）——仅设置界面存在时可用。
**没有静态入口**：`SettingsManager` 全部字段见 351814-351823，无 `_instance`；`il2cpp.h` 的
`Comfort_Common_Singleton_*` 只有 GlobalEventDispatcher / BotSettingsController / IAssetsManager /
PropagationVolumesManager，**没有 `Singleton<SettingsManager>`**。不要写 `Singleton<SettingsManager>.Instance`。

## 3. 线程 / 主线程边界
- `LocalizationManager` 不是 MonoBehaviour，`get_Culture` 只是托管字段 getter，不碰 Unity API →
  事件线程读也基本安全（推测：x64 引用赋值原子，不会 use-after-free）。但按 AGENTS §3.4 纪律，
  建议**主线程读一次并缓存**，成本可忽略。
- `ItemUiContext` 是 Unity 侧 UI 输入节点 → **必须主线程**。不要长期缓存 `SettingsManager` / `ItemUiContext`
  （场景切换后是假 null）；读到的 string 值本身安全。
- 时机：`Plugin.Load` / 主菜单早期 `Instance` 可能为 null（它在 TarkovApplication 的 LoadAvailableLanguages /
  语言选择流程里初始化，314436-314544）→ 惰性重试，别在 Awake 硬读。

## 4. 取值映射 / 兜底（ch ↔ 中文，已证实数据）
- SPT 语言表 `SPT_Data/database/locales/languages.json`：`"ch":"Chinese"`、`"en":"English"`、`"ru":"Русский"`…；
  `menu/ch.json`、`global/ch.json` 实际存在。
- 本机 `%APPDATA%\Battlestate Games\Escape from Tarkov\Settings\Game.ini` 第 3 行即 `"Language": "ch"`；
  该 JSON 的键与 354149-354178 的 `GameSettingsGroup` 字段一一对应（`SettingsManager.SettingsFolderPath` 见 351815）。

`自动` 档建议：
```
s = (Culture ?? "").Trim().ToLowerInvariant()
中文   : s == "ch" || s.StartsWith("zh") || s.Contains("chinese")
English: 其余（en / ru / fr / ge / …）
未知或异常/空: 中文（保持 0.6 的现有行为）
```
不要把 `DefaultLanguage`（336961）当当前值：名字暗示是默认常量，语义未证实。

## 5. 间接途径与不确定项
- **Game.ini 旁路（最稳，内容已证实）**：`%APPDATA%\Battlestate Games\Escape from Tarkov\Settings\Game.ini` 的
  `Language` 键；纯文件读、可后台线程、不依赖 interop。缺点：游戏内改语言可能要到保存/重启才同步（推测）。
- **反面候选**：`UnityEngine.Application.systemLanguage`（1148638 / enum 1149006）是**操作系统语言**，不能用。
- `EFT.UI.LocalizedText`（531612）只存 `localizationKey`，没有当前语言；`_locales`（336949）是已加载的表，
  没有当前标志；TMP 字体缓存也没有当前语言字段。
- 别调 `SettingsManager.InstantiateSingleton()`（351881，会新建并重新读盘）/ `ClearSettings()`（351884）。
- **未证实**：`Culture` 是 locale id（"ch"）还是 .NET culture 名（"zh-CN"/"en-US"）。`_currentApplicationCulture`
  （336951）与 `Culture`（336957）是两个字段，后者更可能是 locale id，但要进游戏确认。验证方案：主菜单/战局各打一行日志，输出 `Culture` + `Language.Value` + Game.ini 值，三者对上即可定稿 0.7。

本次实际排查关键词：`Language`、`LocalizationManager`、`GameSettingsGroup`、`SettingsManager`、
`SettingsWithProvider`、`ELanguage`、`ELocale`、`CurrentLanguage`、`LanguageDirection`、`SystemLanguage`、
`languages.json`、`Game.ini`。
