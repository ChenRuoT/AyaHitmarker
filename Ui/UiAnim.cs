using System;

namespace AyaHitmarker.Ui;

/// <summary>
/// 缓动函数。全部是纯数学，**不引用 UnityEngine**（用 <see cref="MathF"/> 而不是 <c>Mathf</c>），
/// 所以能被 <c>tests/AyaHitmarker.LogicTests</c> 离线校验。
///
/// 约定：<c>t</c> 是 0..1 的归一化进度，返回 0..1（<see cref="OutBack"/> 会略微超过 1，
/// 这正是「弹一下」的手感来源）。
/// </summary>
internal static class UiAnim
{
    internal static float Clamp01(float t) => t < 0f ? 0f : (t > 1f ? 1f : t);

    /// <summary>二次方缓出：比三次方更「线性」，用于数字滚动。</summary>
    internal static float OutQuad(float t)
    {
        t = Clamp01(t);
        return 1f - (1f - t) * (1f - t);
    }

    /// <summary>三次方缓出：起步快、尾巴长。用于位置/宽度收敛。</summary>
    internal static float OutCubic(float t)
    {
        t = Clamp01(t);
        float inv = 1f - t;
        return 1f - inv * inv * inv;
    }

    /// <summary>五次方缓出：比 <see cref="OutCubic"/> 更「一下子到位」，用于透明度。</summary>
    internal static float OutQuint(float t)
    {
        t = Clamp01(t);
        float inv = 1f - t;
        return 1f - inv * inv * inv * inv * inv;
    }

    /// <summary>回弹缓出：冲过终点再收回来。</summary>
    internal static float OutBack(float t, float overshoot = 1.70158f)
    {
        t = Clamp01(t);
        float s = t - 1f;
        return 1f + (overshoot + 1f) * s * s * s + overshoot * s * s;
    }

    /// <summary>
    /// 生命周期透明度：前 <paramref name="fadeIn"/> 秒淡入，中间保持，最后 <paramref name="fadeOut"/> 秒淡出。
    /// </summary>
    internal static float LifeAlpha(float elapsed, float total, float fadeIn, float fadeOut)
    {
        if (elapsed <= 0f) return 0f;
        if (elapsed >= total) return 0f;

        float a = fadeIn <= 0f ? 1f : OutQuint(Clamp01(elapsed / fadeIn));

        float fadeStart = total - fadeOut;
        if (fadeOut > 0f && elapsed > fadeStart)
        {
            float k = Clamp01((elapsed - fadeStart) / fadeOut);
            a *= 1f - k * k;   // 二次淡出，尾部更干净
        }
        return Clamp01(a);
    }

    /// <summary>
    /// 分档错开（stagger）：第 <paramref name="index"/> 个元素的进度，
    /// 每个元素晚 <paramref name="step"/> 秒开始、<paramref name="duration"/> 秒内完成。
    /// </summary>
    internal static float Stagger(float elapsed, int index, float step, float duration)
        => Clamp01((elapsed - index * step) / (duration <= 0f ? 0.0001f : duration));

    /// <summary>带一点点随机感的抖动（纯函数，不需要 Random，避免影响可复现性）。</summary>
    internal static float Wobble(float time, float seed, float frequency, float amplitude)
        => MathF.Sin((time * frequency) + seed) * amplitude;
}
