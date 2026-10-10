# SPT / EFT IL2CPP 客户端 Mod 开发路线

**针对：** `D:\EFTdev\Client.1.2.0.0.47965`（EFT 1.2.0.0，build 47965）
**日期：** 2026-10-10
**你的旧流程：** 反编译 `Assembly-CSharp.dll` → 用 BepInEx 注入（不走 Harmony）

---

## 0. 结论速览

1. **你的旧方法整体失效，不是"要换个写法"，而是那条路已经不存在了。** 客户端里没有 `Assembly-CSharp.dll`，没有 `EscapeFromTarkov_Data/Managed/`。整个 `Managed` 目录消失，取而代之的是 `GameAssembly.dll`（105 MB）+ `global-metadata.dat`（27 MB）。
2. **`BepInEx/interop/Assembly-CSharp.dll` 会出现，但它不是游戏代码。** 它是 Il2CppInterop 生成的**代理程序集**，每次启动按 `GameAssembly.dll` 重新生成，方法体只是转发存根。能拿它编译和看类型结构，改它没有任何效果。
3. **Patching 现在必须走"原生 hook"。** Harmony 在你的场景里已经不是 IL 补丁库，而是原生 detour 安装器。SPTushonka 自己的 `ModulePatch` 从 4.x 的 6 种补丁点砍到只剩 **Prefix / Postfix / Finalizer** —— Transpiler、ILManipulator、ReversePatch 全部移除，因为反编译不出真实的 IL。
4. **你手上这套东西的精确源码已定位：** `SP-Tushonka/modules` @ 分支 `5.0x-dev`，commit `e82cb6abcf3888aed9cb7f2e0f394345e62d37ab`（从 DLL 的 `ProductVersion` 反查得到）。这是你最重要的参考资料。
5. **`sp-tarkov` 已整体归档**（2026-10-02），项目改名 **SP-Tushonka**，wiki 迁到 `wiki.sp-tushonka.com`。**旧 wiki 的《Client Modding Quick Guide》是过期的**（还在教 dnSpy 打开 `Managed/Assembly-CSharp.dll`、教装 Mono 版 BIE5 的 UnityExplorer），不要照它做。
6. **第一步动作：先把游戏启动到主菜单再退出**，否则 `BepInEx/interop/` 永远不会生成，你连编译都做不了。

---

## 1. 客户端实测清单

| 项目 | 实测值 |
|---|---|
| 游戏 | Escape From Tarkov 1.2.0.0 (build 47965) |
| 脚本后端 | IL2CPP |
| `GameAssembly.dll` | 105,324,368 bytes |
| `global-metadata.dat` | 27,922,268 bytes @ `EscapeFromTarkov_Data/il2cpp_data/Metadata/` |
| `EscapeFromTarkov_Data/Managed/` | **不存在** |
| Unity 版本 | 2022.3.43（`BepInEx/unity-libs/2022.3.43.zip`） |
| 注入方式 | Unity Doorstop：`winhttp.dll` + `doorstop_config.ini` |
| Doorstop target | `BepInEx\core\BepInEx.Unity.IL2CPP.dll` |
| 托管运行时 | CoreCLR：`coreclr_path = dotnet\coreclr.dll`，`corlib_dir = dotnet` |
| BepInEx 版本 | 6（IL2CPP 分支） |
| `BepInEx/interop/` | **不存在 → 游戏从未启动过** |
| 客户端插件 | `BepInEx/plugins/sptushonka/SPTushonka.*.dll` |
| 插件版本 | `5.0.0+e82cb6abcf3888aed9cb7f2e0f394345e62d37ab` |
| Prepatcher | `BepInEx/patchers/sptushonka-prepatch.dll` |
| 服务端 | `SPT_Runtime/SPT.Server.dll` = `5.0.0-BLEEDINGEDGEMODS+06dfd3b.20261009`，.NET 10 |
| 服务端 mod 目录 | `SPT_Runtime/user/mods`（**尚不存在**，服务端也没跑过） |

### BepInEx/core 里已经给你的工具（不用另找）

```
0Harmony.dll                     ← HarmonyX（IL2CPP 支持）
Il2CppInterop.Runtime.dll        ← 运行时互操作核心
Il2CppInterop.Generator.dll      ← 首次启动时生成 interop 代理程序集
Il2CppInterop.HarmonySupport.dll ← 让 Harmony 能 patch IL2CPP 方法
Cpp2IL.Core.dll / LibCpp2IL.dll  ← IL2CPP 元数据解析（可当库用）
MonoMod.Core.dll                 ← 原生 detour 引擎
MonoMod.Iced.dll                 ← x64 反汇编/编码
MonoMod.RuntimeDetour.dll        ← 托管侧 detour API
dobby.dll                        ← 原生 inline hook 库
AsmResolver.*                    ← PE / 程序集读写
```

`BepInEx.cfg` 里相关设置（当前值）：

```ini
[IL2CPP]
UpdateInteropAssemblies = true
IL2CPPInteropAssembliesPath = {BepInEx}
GlobalMetadataPath = {GameDataPath}/il2cpp_data/Metadata/global-metadata.dat
ScanMethodRefs = true            ; 用 xref 标 CallerCount，可判断死方法
DumpDummyAssemblies = false      ; 想拿 Cpp2IL 的 dummy 程序集就改 true
PreloadIL2CPPInteropAssemblies = true

[Detours]
DetourProviderType = Default     ; 可选 Dobby / Funchook —— 说明 BepInEx 自带原生 hook Provider
```

---

## 2. 生态现状：SPT → SP-Tushonka

- `sp-tarkov` 组织下**所有仓库均已 archived**，最后提交 2026-10-02。
- 新组织：**`SP-Tushonka`**（github.com/SP-Tushonka），活跃。
- 新 wiki：`wiki.sp-tushonka.com`，源码仓库 `SP-Tushonka/wiki`（分支 `main`，Markdown 可直读）。
- 分支代号与客户端版本的关系：

| `SP-Tushonka/modules` 分支 | 对应 SPT | 脚本后端 | 备注 |
|---|---|---|---|
| `3.11.x-dev` | SPT 3.11 | Mono | |
| `4.0.x-dev` | SPT 4.0 | Mono | |
| `master` | SPT 4.1 | Mono | BepInEx 5，`EscapeFromTarkov_Data/Managed` 仍存在 |
| **`5.0x-dev`** | **SPT 5.0** | **IL2CPP** | **← 你的客户端就是这条分支** |

**注意版本错位：** `5.0x-dev` 的 README 文字还写着 "make Escape From Tarkov **1.1** run"，而你的客户端是 **1.2.0.0.47965**。以 commit hash 为准（已匹配 `e82cb6a`），README 文字滞后。拉代码前用 `git log` / tag 核对一下，别直接吃 HEAD。

---

## 3. 旧方法为什么逐条失效

### 原理

```
旧（Mono）： C# → IL 程序集 → 塞进游戏 → 运行时 JIT 编译执行
                    ↑ 你能改这里，改完就是改行为

新（IL2CPP）：C# → IL → il2cpp.exe → C++ → MSVC → GameAssembly.dll
                              ↑ 中间产物在构建期就丢了，不进包
```

运行时执行的机器码只在 `GameAssembly.dll` 里。IL 已经不存在了。

### 逐条对照

| 旧做法 | IL2CPP 下的结果 |
|---|---|
| dnSpy 打开 `Managed/Assembly-CSharp.dll`，改方法体存盘 | **没有这个文件。** 目录都不存在 |
| dnSpy 导出项目读游戏源码 | 结构能读（看 interop 或 dump.cs），**方法体是空的/存根**，读不到逻辑 |
| Mono.Cecil 改 IL | 没有可改的目标 |
| BepInEx 5 preloader patcher 改游戏程序集 | BepInEx 6 的 patcher 只能改**托管侧**（你自己和别人的插件），**改不到游戏逻辑** |
| Harmony Transpiler 改指令序列 | Interop 程序集里的"IL"跟真实原生代码不对应，**SPTushonka 已直接移除该补丁点** |
| Harmony ReversePatch 取原方法 | 同上，已移除；改用 `Il2CppBaseCall`（见 §6） |
| 直接在自己代码里 `new` 游戏类型、调游戏方法 | **仍然可以**，这是唯一没变的，通过 interop 代理 |

**最硬的证据：**

```csharp
// SPT 4.1 (Mono)  —  SPT.Reflection/Patching/ModulePatch.cs
_prefixList / _postfixList / _transpilerList / _finalizerList / _ilmanipulatorList / _reverseList
throw new PatchException("At least one of the patch methods must be specified")  // 6 选 1

// SPT 5.0 (IL2CPP) —  SPTushonka.Reflection/Patching/ModulePatch.cs
_prefixList / _postfixList / _finalizerList
// 只剩 3 个。Transpiler / ILManipulator / ReversePatch 被删掉了。
```

---

## 4. 新工具链（旧 → 新对照）

| 需求 | 旧工具 | 新工具 |
|---|---|---|
| 看游戏代码结构 | dnSpy / ILSpy 打开 `Assembly-CSharp.dll` | **Il2CppDumper** 或 **Cpp2IL** 对 `GameAssembly.dll` + `global-metadata.dat` 出 `dump.cs` / DummyDll |
| 看方法体逻辑 | dnSpy 直接读 IL | **IDA / Ghidra** 读 `GameAssembly.dll` 原生代码（用 dump 出的 method RVA 定位） |
| 编译时引用 | `Managed/*.dll` | **`BepInEx/interop/*.dll`**（首次启动后生成） |
| 运行时探索对象 | UnityExplorer（Mono 版） | **UnityExplorer 的 BepInEx 6 / IL2CPP 版**（别装 Mono 版） |
| 改游戏行为 | 改 IL / preloader patcher | **原生 hook**：Harmony(MonoMod detour) / MonoMod.RuntimeDetour / Dobby / MinHook |
| 调试 | dnSpy 附加进程 | Visual Studio / Rider 附加，**托管调试**（BepInEx 6 支持）+ 原生调试器分开用 |

### 生成 `dump.cs`（替代"反编译 Assembly-CSharp"）

用 **Il2CppDumper**（Perfare）或 **Cpp2IL**，输入：

```
GameAssembly.dll                                  (游戏根目录)
EscapeFromTarkov_Data/il2cpp_data/Metadata/global-metadata.dat
```

产物：
- `dump.cs` —— 全部类型/方法/字段/签名 + 每个方法的 **RVA**
- `DummyDll/` —— 只含签名和空方法体的程序集（可以直接当引用）
- `script.json` + `il2cpp.h` —— 给 IDA 用的符号脚本
- `stringliteral.json`

> 元数据未加密。BepInEx 能直接读 `global-metadata.dat` 说明它是明文的，所以两个工具都能跑。
>
> 另一个更省事的办法：把 `BepInEx.cfg` 里 `DumpDummyAssemblies` 改成 `true`，BepInEx 复用自带的 `Cpp2IL.Core` 把 dummy 程序集存到 `BepInEx/dummy/`。

---

## 5. 落地路线

### Step 0 — 先跑一次游戏（不做这步后面全部免谈）

1. 启动 SPT Launcher / 或直接跑 `EscapeFromTarkov.exe`，进到主菜单然后退出。
2. 退出后确认生成：
   ```
   BepInEx/interop/                    ← Il2CppInterop 代理程序集（几十个 dll）
   BepInEx/LogOutput.log
   SPT_Runtime/user/                   ← 服务端首次运行会建
   ```
3. 顺手改 `BepInEx/config/BepInEx.cfg` 打开调试输出：
   ```ini
   [Logging.Console]
   Enabled = true
   LogLevels = Fatal, Error, Warning, Message, Info, Debug

   [Harmony.Logger]
   LogChannels = Warn, Error, Debug      ; 需要看 Harmony 内部时才加 IL（很吵）
   ```
4. 记住：**`UpdateInteropAssemblies = true` 意味着游戏更新后 interop 会自动重生成 → 你引用到的类型签名可能变 → mod 需要跟着重编译。**

### Step 1 — 建立"读代码"的能力

阅读优先级（从最准到最深）：

1. **`BepInEx/interop/*.dll`** —— 类型层次、签名、字段名最准确，直接用 dnSpy/ILSpy 打开看结构。
2. **`dump.cs`** —— 交叉验证，重点看每个方法的 RVA（后面 hook 用得上）。
3. **IDA/Ghidra + `script.json`** —— 只有必须看懂方法体逻辑时才上。IL2CPP 的原生代码可读性很差（全是 `il2cpp_*` 调用和 `MethodInfo*` 传参），但比瞎猜强。

EFT 的类名在 4.x 时代靠跑一次游戏 deobfuscate（BepInEx 的 `UnhollowerDeobfuscationRegex` 现在为空，即不做重命名）。**所以你会看到大量原名**，这是好消息。

### Step 2 — 搭开发工程

**照抄官方做法最省事**（`SP-Tushonka/modules` 的 `Directory.Build.props`）：从游戏安装目录**直接引用**，不 redistribut。

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>   <!-- 注意：BepInEx 6 走 CoreCLR，插件是 net10.0 -->
  <GameDir>D:/EFTdev/Client.1.2.0.0.47965</GameDir>
  <BepInExCore>$(GameDir)/BepInEx/core</BepInExCore>
  <Interop>$(GameDir)/BepInEx/interop</Interop>
</PropertyGroup>

<ItemGroup>
  <Reference Include="BepInEx.Core"><HintPath>$(BepInExCore)/BepInEx.Core.dll</HintPath><Private>false</Private></Reference>
  <Reference Include="BepInEx.Unity.IL2CPP"><HintPath>$(BepInExCore)/BepInEx.Unity.IL2CPP.dll</HintPath><Private>false</Private></Reference>
  <Reference Include="0Harmony"><HintPath>$(BepInExCore)/0Harmony.dll</HintPath><Private>false</Private></Reference>
  <Reference Include="Il2CppInterop.Runtime"><HintPath>$(BepInExCore)/Il2CppInterop.Runtime.dll</HintPath><Private>false</Private></Reference>
  <Reference Include="Assembly-CSharp"><HintPath>$(Interop)/Assembly-CSharp.dll</HintPath><Private>false</Private></Reference>
  <Reference Include="Assembly-CSharp-firstpass"><HintPath>$(Interop)/Assembly-CSharp-firstpass.dll</HintPath><Private>false</Private></Reference>
  <Reference Include="Il2Cppmscorlib"><HintPath>$(Interop)/Il2Cppmscorlib.dll</HintPath><Private>false</Private></Reference>
  <Reference Include="Il2CppSystem"><HintPath>$(Interop)/Il2CppSystem.dll</HintPath><Private>false</Private></Reference>
  <Reference Include="Il2CppSystem.Core"><HintPath>$(Interop)/Il2CppSystem.Core.dll</HintPath><Private>false</Private></Reference>
  <Reference Include="UnityEngine.CoreModule"><HintPath>$(Interop)/UnityEngine.CoreModule.dll</HintPath><Private>false</Private></Reference>
  <!-- 需要什么再加：UnityEngine.UI / Sirenix.Serialization / Vuplex.WebView / Diz.Resources
       / Diz.Binding / Comfort / CoreServices / FilesChecker / GlobalEvents / bsG.console.core
       / DissonanceVoip / DependencyManager / Unity.ScriptableBuildPipeline -->
</ItemGroup>
```

**部署路径：** 插件放 `BepInEx/plugins/sptushonka/`，**不要**散在 `BepInEx/plugins/` 根目录（这是 5.0 的约定，官方 README 明确写了）。

**工程骨架可以直接抄的对象：** `SP-Tushonka/modules` 的 `5.0x-dev` 分支。克隆下来（注意它 270 MB，含大量二进制资源），把 `GameDir` 指向你自己的目录，`dotnet build` 就能出全套 dll。这是你免费得到的、**与你客户端逐字节对应**的参考实现。

### Step 3 — 写第一个补丁

用 `SPTushonka.Reflection.Patching.ModulePatch`（`BepInEx/plugins/sptushonka/SPTushonka.Reflection.dll` 已经在你的客户端里，直接引用）。这是官方自己用的框架，源码见 §8。

真实例子（`SPTushonka.Custom/Patches/VersionLabelPatch.cs` 节选）：

```csharp
public class VersionLabelPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
        => AccessTools.Method(typeof(EftVersion), nameof(EftVersion.Create));

    [PatchPostfix]
    public static void PatchPostfix(EftVersion __result)
    {
        __result.Major = "my mod";
    }
}
```

要点：
- `GetTargetMethod()` 返回 `MethodBase`（IL2CPP 下通常传 interop 代理类型的 `MethodInfo`）。
- 用 `AccessTools.Method(...)` 找方法，**不要**用字符串 refletion 拼名字。
- 补丁点只有 `[PatchPrefix]` / `[PatchPostfix]` / `[PatchFinalizer]`。
- 参数注入风格和 Harmony 一致（`__result`、`__instance`、按名注入）。
- 用 `PatchManager(BasePlugin plugin)` 注册，或 `autoPatch: true` 自动扫描。

### Step 4 — "往游戏里加自己的代码"的替代路径

你原来靠改 `Assembly-CSharp.dll` 加代码/改实现。IL2CPP 下的对应设施在：

```
SPTushonka.Reflection/Il2Cpp/
├── Il2CppInjection.cs     ← 把托管类/方法注入进 IL2CPP 运行时（§6 的关键）
├── Il2CppBaseCall.cs      ← 非虚调用游戏方法（替代 ReversePatch）
├── Il2CppConversions.cs   ← 托管 ↔ IL2CPP 类型转换、委托、数组
├── Il2CppAssets.cs        ← 资源加载
├── Il2CppMethods.cs       ← 方法解析工具
├── Il2CppTaskExtensions.cs← Task ↔ Il2CppSystem.Threading.Tasks
└── MainThread.cs          ← 切主线程（Unity API 只能在主线程调）
```

**先读 `Il2CppInjection.cs` 和 `Il2CppBaseCall.cs`**，这两个文件直接对应你原来的需求。

`Il2CppBaseCall` 的用法（官方注释说得很清楚）：

```csharp
// 你要调"游戏自己的实现"，但那个方法在你的子类里被 override 了。
// 从外部直接调 interop 包装器会走虚分发 → 又回到你的 override → 无限递归。
// C# 的 base.Method() 只能在派生类内部用，所以用这个构造一个非虚调用委托。
var call = methodInfo.CreateBaseCall<Func<MyType, Arg1, Ret>>();
var result = call(instance, arg1);
// 构造有开销，缓存起来，别在热路径每次重建。
```

### Step 5 — Prepatcher 的语义变了

`BepInEx/patchers/sptushonka-prepatch.dll` 里能看到这些符号：

```
SPTushonka.PrePatch.Loader / Loader.X64     ← 原生加载器
SPTushonkaPrePatcher                        ← IPreloaderPatcher
FieldDefaultValueHook
TypeDefIndexHook
InteropCorrections
WhenInteropType
Il2CppProbe
```

也就是说，5.0 的 "PrePatch" 不再是"游戏加载前改游戏程序集"，而是**在 interop 生成/加载阶段做元数据层面的修正**（字段默认值、TypeDef 索引、interop 偏差校正）。你自己写 mod 一般不需要碰这一层，但要知道它的存在——如果你遇到"某个类型的 interop 代理签名不对/字段读出来是默认值"，问题的解法可能在这里，而不是你的补丁代码。

---

## 6. 关于"我不用 Harmony"

先说明一件事：**Harmony 在这里已经不是 IL 补丁库了。** BepInEx 6 IL2CPP 通过 `Il2CppInterop.HarmonySupport` + `MonoMod.Core` 把 patch 实现成了**原生 detour** —— 直接替换 `GameAssembly.dll` 里那个方法的原生入口地址。所以它做的是"装 hook + 帮你把参数从 IL2CPP 侧 marshal 到托管侧"。你排斥 Harmony 的原始理由（"改 IL 不干净/不想依赖"）在 IL2CPP 下基本不成立了。

**如果仍要绕开 Harmony**，从近到远三条路：

### 路线 A：MonoMod.RuntimeDetour（推荐，BepInEx 自带）

`BepInEx/core/` 里已经有 `MonoMod.Core.dll` / `MonoMod.RuntimeDetour.dll` / `MonoMod.Iced.dll`。BepInEx 内部用的就是 `IL2CPPDetourMethodPatcher` 这套。你要做的是：拿到目标 `MethodInfo` → 拿到它的原生函数指针 → 用 MonoMod 的 detour API 替换。

```csharp
// 思路示意，API 细节以 MonoMod.Core 当前版本为准
var method = AccessTools.Method(typeof(SomeGameType), "SomeMethod");
var handle = new DetourConfig().CreateDetour(method);  // IL2CPP 运行时下会解析到原生入口
```
优点：托管侧写 C#，BepInEx 已经把这套跑通了，兼容性有保证。
缺点：你要自己处理参数/返回值 marshal（Harmony 免费帮你做的那部分），签名一复杂就很痛苦。

### 路线 B：裸原生 hook（MinHook / Dobby）

```
il2cpp_domain_get()
  → il2cpp_domain_get_assemblies() / il2cpp_assembly_get_image()
  → il2cpp_class_from_name(image, "Namespace", "TypeName")
  → il2cpp_class_get_method_from_name(klass, "MethodName", argc)
  → MethodInfo* → *(MethodInfo + offset) 或 il2cpp_method_get_pointer 拿原生地址
  → 装 MinHook / Dobby inline hook
```

配合：用 Il2CppDumper 的 `dump.cs` 里的 RVA 直接算地址（`模块基址 + RVA`），比名字查找更快更稳。Dobby 已经在 `BepInEx/core/dobby.dll`，`BepInEx.cfg` 也能把 detour provider 切成 Dobby。

优点：完全不依赖托管侧，最贴近"我在做原生逆向"的手感；性能最好。
缺点：签名/marshal/GC 保活全部自己负责，写错了就是崩溃；跨 SPT 版本维护成本高。

### 路线 C：不改行为，只加东西

如果 mod 本质是"加载我自己的资源/UI/数据"，可以完全不做 hook，只在插件 `Load()` 里注册自己的 MonoBehaviour、订阅游戏事件、通过 interop 读游戏状态。这条路最省事，但能做的事最少。

### 我的建议

把约束从"**不用 Harmony**"放宽成"**不用 Harmony 的 IL 级特性（Transpiler/ILManipulator）**"。这两件事在 IL2CPP 下是分开的：

- 用 `ModulePatch` + Prefix/Postfix → 行为等价于你原来做的大部分事情，且和 SPTushonka 生态一致，出问题有人能帮你。
- 需要极致性能或要 hook 原生函数（不是游戏方法）时，再下沉到路线 A/B。

**理由：** 5.0 的官方全套、社区示例、`SPTushonka.Reflection` 都是 Harmony。逆着来意味着所有既有代码示例对你无效，遇到问题只能自己 debug。**而收益基本为零**——Harmony 该做的"装 detour"你也得做，只是换成你自己做。

---

## 7. 踩坑清单

### 类型系统是两套

- 游戏类型 → interop 代理类，继承 `Il2CppObjectBase`，`IntPtr` 包着原生指针。
- BCL 是 **`Il2Cppmscorlib.dll` / `Il2CppSystem.dll` / `Il2CppSystem.Core.dll`** —— 这些是 IL2CPP 自己的 BCL 影子，**不是你编译时的 `System.Collections.Generic.List<T>`**。
- 传参要在两套之间显式转换（`Il2CppConversions`）。看到 `Il2CppSystem.Collections.Generic.List<T>` 不要愣，它就是游戏侧的 List。

### 泛型

- AOT：只有**编译期用过的**泛型实例被生成。
- 高概率踩的形态：`List<你自己定义的结构体>`、只在反射里出现的泛型、泛型虚方法、值类型泛型。
- 好消息：SPT 5.0 的 interop 已经为游戏内部用到的组合生成了代理，你新增的组合才可能出问题。

### Marshal 开销

字符串、数组、委托在托管 ↔ IL2CPP 之间每次都要转换。**别在每帧/高频路径做**，缓存转换结果。

### 线程

Unity API 只能在主线程调。用 `MainThread.cs` 切回去，别自己在后台线程里碰游戏对象。

### 生命周期 / GC

interop 代理对象持有原生指针。原生侧对象被销毁后你还握着代理 → use-after-free 崩溃。注意缓存的引用何时失效（场景切换、对象销毁）。

### 版本更新

- 游戏更新 → interop 重生成 → 类型/字段签名可能变 → mod 要重编译。
- 建议：`GameDir` 用环境变量或本地 props 覆写，别把绝对路径提交进 git（官方就是这么做的，`Directory.Build.props` 里留默认值但可被 `-p:GameDir=` 覆盖）。

### 别碰这些

`BattlEye` 相关（`EscapeFromTarkov_BE.exe`）、`ConsistencyInfo`、`FilesChecker`。SPT 自己就在 patch 这些区域（`SPTushonka.Core/Patches/` 里的 BattlEye / Consistency / SSL / UnityWebRequest 补丁），你插手会跟 SPT 打架。注意这是**离线版**，`EscapeFromTarkov_BE.exe` 是原版残留，正常跑的是 `EscapeFromTarkov.exe`。

---

## 8. 权威参考（按重要性排序）

| 资源 | 地址 | 说明 |
|---|---|---|
| **`SP-Tushonka/modules` @ `5.0x-dev`** | github.com/SP-Tushonka/modules/tree/5.0x-dev | **最重要。** 与你客户端逐字节对应（commit `e82cb6a`）的参考实现 |
| `SPTushonka.Reflection` | `5.0x-dev/SPTushonka.Reflection/` | 补丁框架 + IL2CPP 工具集，读 `Il2Cpp/` 和 `Patching/` |
| `SPTushonka.Custom/Patches/` | 同仓库 | **真实补丁范例**：BundleManifest / EasyBundlePath / ExpansionsBrowser / RedirectClientImageRequests / SaveRegistryLocally / SetPreRaidSettingsScreenDefaults / VersionLabel |
| `Directory.Build.props` | 仓库根目录 | 工程配置的权威模板（GameDir、interop 引用清单、net10.0） |
| `SP-Tushonka/wiki` | github.com/SP-Tushonka/wiki（分支 `main`） | wiki 源码，Markdown 直读，比网页好抓 |
| BepInEx IL2CPP 文档 | docs.bepinex.dev | 装/配/写插件 |
| MonoMod.Core | github.com/MonoMod/MonoMod | 不依赖 Harmony 时的 detour 路线 |
| Il2CppDumper | github.com/Perfare/Il2CppDumper | 出 `dump.cs` / DummyDll / RVA |
| Cpp2IL | github.com/SamboyCoding/Cpp2IL | 同上，BepInEx 内部就在用它 |
| UnityExplorer | github.com/sinai-dev/UnityExplorer | 运行时探索。**选 BepInEx 6 / IL2CPP 的包** |
| SPT Discord | discord.sp-tushonka.com | `#mods-development`，有问题在这里问最有效 |

### 明确不要照做的

- ❌ `wiki.sp-tushonka.com/en/modding/tutorials/Client_Modding_Quick_Guide` —— 2026-08-08 写的，**Mono 时代内容**（教 dnSpy 开 `Managed/Assembly-CSharp.dll`、教装 **Mono 版** BIE5 UnityExplorer）。它页面顶上还写着 "applies to any SPT version"，是错的。
- ❌ `SP-Tushonka/modules` 的 `master` 分支（还是 4.1 Mono 时代）
- ❌ `sp-tarkov/*` 全部仓库（已归档）
- ❌ `Jehree/SPTClientModExamples` —— 模板仍是 Mono 时代（引用 `Managed/` 的 Assembly-CSharp）。可以借鉴代码风格和工程骨架，引用方式必须换。

---

## 9. 下一步（建议顺序）

1. **跑一次游戏**，确认 `BepInEx/interop/` 生成。这一步不做，下面全卡住。
2. **克隆 `SP-Tushonka/modules`（`5.0x-dev`，或用 tag/commit 定位到 `e82cb6a`）**，把 `GameDir` 改成 `D:/EFTdev/Client.1.2.0.0.47965`，`dotnet build` 跑通。**这一步能跑通，就说明你的工具链完全正确**——这是最有价值的里程碑。
3. **跑 Il2CppDumper**，产出 `dump.cs`，建立自己的代码索引。顺便把 `BepInEx.cfg` 的 `DumpDummyAssemblies` 打开拿 dummy 程序集。
4. **照 `SPTushonka.Custom/Patches/` 里最小的那个补丁**，写一个只改个字符串/日志的 Hello World patch，确认能加载、能命中、能看日志。
5. **读 `Il2CppInjection.cs` + `Il2CppBaseCall.cs`**，把这两个吃透 —— 这是你原来"直接改游戏代码"能力的两个替代品。
6. 然后才回来做你真正的 mod。届时把具体需求（要改哪个行为、拦住哪个方法）告诉我，我可以帮你定位目标方法并写出第一个真实补丁。
