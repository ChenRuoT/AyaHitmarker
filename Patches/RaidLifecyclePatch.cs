using System;
using System.Reflection;
using EFT;
using HarmonyLib;
using SPTushonka.Reflection.Patching;

namespace AyaHitmarker.Patches;

/// <summary>
/// 战局生命周期补丁。
///
/// 为什么需要它：<c>GlobalEventDispatcher</c> 实例消失（<see cref="Game.GameEvents.Poll"/> 轮询）能覆盖
/// 「战局结束」，但它只在主线程 Update 里被观察到，而且没法区分「正常撤离」和「直接崩掉」。
/// 直接补 <c>GameWorld.OnDestroy</c> 能在战局销毁的**当帧**清干净 UI，
/// 免得撤离后还挂着上一局的伤害数字和击杀播报。
///
/// 这是本插件**唯一**的补丁。命中 / 击杀全部靠事件订阅，没有任何 detour 在热路径上。
///
/// 用 SPTushonka 官方的 <see cref="ModulePatch"/>（Prefix / Postfix / Finalizer 三种补丁点）。
/// IL2CPP 下 Transpiler / ILManipulator / ReversePatch 已被官方移除——反编译不出真实 IL。
/// </summary>
internal sealed class RaidLifecyclePatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        var method = AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnDestroy));
        if (method == null) Logger.LogError("[patch] 找不到 EFT.GameWorld.OnDestroy");
        return method!;
    }

    [PatchPostfix]
    private static void Postfix()
    {
        try
        {
            Game.GameEvents.RaidEndedFromPatch();
        }
        catch (Exception ex)
        {
            Logger.LogError($"[patch] RaidLifecyclePatch: {ex}");
        }
    }
}
