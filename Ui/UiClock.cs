using UnityEngine;

namespace AyaHitmarker.Ui;

/// <summary>
/// 所有 UI 动画共用的时钟。**正常游戏里它就是 <c>Time.unscaledTime</c>**（缩放恒为 1）。
///
/// 为什么不直接用 <c>Time.unscaledTime</c>：要验证动画「在第几帧长什么样」，必须能控制时间。
/// 这里提供两个只给调试脚本用的开关（见 <c>Behaviour/DebugHarness.cs</c>）：
/// <list type="bullet">
///   <item><b>慢放</b>（<see cref="Scale"/>）：整个 UI 时间乘一个系数，肉眼能看清 0.27 秒的入场动画；</item>
///   <item><b>手动步进</b>（<see cref="SetManual"/> + <see cref="Step"/>）：时钟不再自己走，
///     调用方一帧一帧地推进，于是任何一个动画时刻都能精确截图，不受掉帧 / 截图卡顿影响。</item>
/// </list>
/// 击杀判定（多杀 / 连杀窗口）也读这个时钟，所以脚本可以精确控制「两杀间隔多少秒」。
/// </summary>
internal static class UiClock
{
    // 用 double 累加：float 累加 `_now += dt` 在连续运行十几小时后，帧间隔会小于 float 的半个 ulp，
    // 时间就停住了（Unity 自己的 Time.unscaledTime 内部也是 double 累加再转 float，不会停）。
    private static double _now;
    private static float _delta;
    private static bool _manual;

    /// <summary>当前 UI 时间（秒）。</summary>
    internal static float Now => (float)_now;

    /// <summary>上一帧到这一帧的 UI 时间增量（秒）。</summary>
    internal static float Delta => _delta;

    /// <summary>慢放系数（自动模式下有效）。1 = 正常。</summary>
    internal static float Scale { get; set; } = 1f;

    internal static bool Manual => _manual;

    /// <summary>每帧最开始调用一次。</summary>
    internal static void Advance()
    {
        if (_manual) return;   // 手动模式：时间只由 Step 推进
        _delta = Time.unscaledDeltaTime * Scale;
        _now += _delta;
    }

    /// <summary>一帧的 UI 更新结束之后调用：手动模式下把增量清零，没有 Step 的帧里时间就是静止的。</summary>
    internal static void EndFrame()
    {
        if (_manual) _delta = 0f;
    }

    internal static void SetManual(bool manual)
    {
        _manual = manual;
        _delta = 0f;
    }

    /// <summary>手动模式下推进时间；下一帧的 UI 更新会读到这个增量。</summary>
    internal static void Step(float seconds)
    {
        if (seconds <= 0f) return;
        _now += seconds;
        _delta = seconds;
    }
}
