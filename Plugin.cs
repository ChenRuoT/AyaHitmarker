using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using SPTushonka.Reflection.Patching;

namespace AyaHitmarker;

/// <summary>
/// BepInEx 6 (IL2CPP) 入口。
///
/// 注意：<see cref="BasePlugin"/> **不是** MonoBehaviour（实测基类是 <c>System.Object</c>），
/// 所以 Unity 生命周期要另起组件，用 <c>AddComponent&lt;T&gt;()</c> 注入。
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BasePlugin
{
    public const string PluginGuid = "com.aya.ayahitmarker";
    public const string PluginName = "AyaHitmarker";
    public const string PluginVersion = "0.2.0";

    internal static Plugin Instance { get; private set; } = null!;
    internal static ManualLogSource Logger { get; private set; } = null!;
    internal static AyaConfig Cfg { get; private set; } = null!;

    internal static PatchManager Patches { get; private set; } = null!;

    /// <summary>插件所在目录（BepInEx/plugins/AyaHitmarker）。</summary>
    internal static string PluginDirectory { get; private set; } = string.Empty;

    /// <summary>可选的自定义音效目录：BepInEx/plugins/AyaHitmarker/Sounds/。</summary>
    internal static string SoundsDirectory { get; private set; } = string.Empty;

    /// <summary>图标目录：BepInEx/plugins/AyaHitmarker/Icons/。</summary>
    internal static string IconsDirectory { get; private set; } = string.Empty;

    public override void Load()
    {
        Instance = this;
        Logger = base.Log;
        Cfg = new AyaConfig(Config);

        try
        {
            PluginDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            SoundsDirectory = System.IO.Path.Combine(PluginDirectory, "Sounds");
            IconsDirectory = System.IO.Path.Combine(PluginDirectory, "Icons");
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"无法定位插件目录: {ex.Message}");
        }

        Logger.LogInfo($"{PluginName} {PluginVersion} :: target EFT 1.2.0.0 (IL2CPP) / BepInEx 6 / net10.0");

        // SPTushonka 官方补丁框架。autoPatch=true 会扫描本程序集里所有 ModulePatch 子类。
        Patches = new PatchManager(this, true);
        Patches.EnablePatches();

        AddComponent<Behaviour.AyaBehaviour>();

        Logger.LogInfo($"{PluginName} :: loaded");
    }

    public override bool Unload()
    {
        try { Game.GameEvents.Shutdown(); }
        catch (Exception ex) { Logger.LogWarning($"shutdown: {ex.Message}"); }

        try { Patches?.DisablePatches(); }
        catch (Exception ex) { Logger.LogWarning($"unpatch: {ex.Message}"); }

        return true;
    }
}
