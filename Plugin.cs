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
    public const string PluginVersion = "0.3.0";

    internal static Plugin Instance { get; private set; } = null!;
    internal static ManualLogSource Logger { get; private set; } = null!;
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

    public override void Load()
    {
        Instance = this;
        Logger = base.Log;
        Cfg = new AyaConfig(Config);

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
        AddComponent<Behaviour.AyaBehaviour>();

        Logger.LogInfo($"{PluginName} :: loaded (0 patches)");
    }

    public override bool Unload()
    {
        try { Game.GameEvents.Shutdown(); }
        catch (Exception ex) { Logger.LogWarning($"shutdown: {ex.Message}"); }

        return true;
    }
}
