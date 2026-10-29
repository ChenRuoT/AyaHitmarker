using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace AyaHitmarker;

/// <summary>
/// BepInEx 6 (IL2CPP) 入口。
///
/// 注意：<see cref="BasePlugin"/> **不是** MonoBehaviour（实测基类是 <c>System.Object</c>），
/// 所以 Unity 生命周期要另起组件，用 <c>AddComponent&lt;T&gt;()</c> 注入。
///
/// <para>
/// **本插件零补丁，因此不依赖 <c>SPTushonka.Reflection</c>。**
/// 命中和击杀都是 IL2CPP 事件订阅，战局边界靠轮询 <c>GlobalEventDispatcher</c> 实例变化。
/// 曾试图用 <c>ModulePatch</c> 补 <c>EFT.GameWorld.OnDestroy</c>，结果因为 IL2CPP 的「方法体共享」
/// 让 detour 命中 3360 个无关方法，游戏直接起不来 —— 详见 README §7。
/// </para>
///
/// <para>
/// 如果将来确实需要补丁：加回 <c>SPTushonka.Reflection</c> 引用 → 写一个 <c>ModulePatch</c> 子类 →
/// 用 <c>new PatchManager(this, true)</c>。**但必须先启动一次并在日志里搜 <c>shares its native body</c>**。
/// 注意 <c>PatchManager(this, true)</c> 在「本程序集里一个 ModulePatch 都没有」时会抛
/// <c>PatchException</c> 让插件加载失败，所以要么真的有补丁，要么就别构造它。
/// </para>
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "com.aya.ayahitmarker";
    public const string PluginName = "AyaHitmarker";
    public const string PluginVersion = ReleaseVersion.Value;

    internal static Plugin Instance { get; private set; } = null!;
    internal static DiagnosticLogger Logger { get; private set; } = null!;
    internal static AyaConfig Cfg { get; private set; } = null!;

    /// <summary>插件所在目录（BepInEx/plugins/AyaHitmarker）。</summary>
    internal static string PluginDirectory { get; private set; } = string.Empty;

    /// <summary>图标目录：BepInEx/plugins/AyaHitmarker/Icons/。</summary>
    internal static string IconsDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// 画布根 RectTransform 的 rect 尺寸（画布单位）。
    /// 只用来诊断“局部坐标 → 屏幕坐标”的换算（见 KillFeedLayer.LogRowScreenPosition），
    /// 业务代码不应该读它。
    /// </summary>
    internal static Vector2 CanvasRectSize { get; private set; }

    /// <summary>由 <c>OverlayRoot</c> 在创建画布时写入。</summary>
    internal static void SetCanvasRectSize(Vector2 size) => CanvasRectSize = size;

    /// <summary>音效目录：BepInEx/plugins/AyaHitmarker/Sounds/。</summary>
    internal static string SoundsDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// 唯一注入的 MonoBehaviour。保留引用，<see cref="Unload"/> 时显式销毁（热重载必需）：
    /// 它的 OnDestroy 负责拆画布 / 音频宿主 / 静态图标缓存。
    /// </summary>
    private Behaviour.AyaBehaviour? _behaviour;

    public override void Load()
    {
        Instance = this;
        Logger = new DiagnosticLogger(base.Log);
        Cfg = new AyaConfig(Config);

        // 界面语言：先按设置里的强制值生效（自动档要等游戏的 LocalizationManager 就绪，AyaBehaviour 约 1Hz 重新解析）。
        // 设置面板的文字也要在这里先应用一次，否则强制 English 的用户第一次打开面板看到的还是中文。
        Game.Loc.Current = Game.Loc.Resolve(Cfg.Language.Value, null);
        ConfigPanel.ApplyLanguage(Game.Loc.Current);

        try
        {
            PluginDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            IconsDirectory = System.IO.Path.Combine(PluginDirectory, "Icons");
            SoundsDirectory = System.IO.Path.Combine(PluginDirectory, "Sounds");
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"无法定位插件目录: {ex.Message}");
        }

        Logger.LogInfo($"{PluginName} {PluginVersion} :: target EFT 1.2.0.0 (IL2CPP) / BepInEx 6 / net10.0");
        Logger.LogInfo($"{PluginName} :: plugin dir = {PluginDirectory}");

        // 零补丁 —— 不构造 PatchManager（详见类注释）。
        _behaviour = AddComponent<Behaviour.AyaBehaviour>();

        Logger.LogInfo($"{PluginName} :: loaded (0 patches)");
    }

    public override bool Unload()
    {
        try { Game.GameEvents.Shutdown(); }
        catch (Exception ex) { Logger.LogWarning($"shutdown: {ex.Message}"); }

        // 热重载 / 正常卸载：销毁唯一注入的组件，触发 AyaBehaviour.OnDestroy 拆画布、音频宿主和静态缓存。
        // 引擎的 LeftoverSweeper 也会兜底销毁注入类型实例，但不能依赖它 —— 普通 BepInEx 卸载路径没有它。
        try
        {
            if (_behaviour != null)
            {
                UnityEngine.Object.Destroy(_behaviour);
                _behaviour = null;
                Logger.LogInfo("[behaviour] 已请求销毁 AyaBehaviour（画布 / 音效宿主由 OnDestroy 清理）");
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"销毁 AyaBehaviour 失败: {ex.Message}");
        }

        return true;
    }
}
