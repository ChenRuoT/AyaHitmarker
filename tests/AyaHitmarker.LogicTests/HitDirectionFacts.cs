using System;
using System.Collections.Generic;
using AyaHitmarker.Ui;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// 0.6e 受击方向指示的纯逻辑：方位角换算、二次函数包络、粒子分布（底部密顶部疏、落在椭圆上）。
/// </summary>
internal static class HitDirectionFacts
{
    private const float Eps = 1e-4f;

    internal static void Run()
    {
        Assert.Suite("受击方向：方位角与粒子");

        // ---- 方位角：0 = 正前，+ = 右，π = 背后，-π/2 = 左 ----
        float angle;
        Assert.True(HitDirectionMath.TryAngle(0f, 1f, 0f, 1f, 1f, 0f, out angle), "正前方的方向向量可以换算");
        Assert.Near(0f, angle, 1e-4, "正前方 = 0 弧度");

        Assert.True(HitDirectionMath.TryAngle(1f, 0f, 0f, 1f, 1f, 0f, out angle), "右侧方向可换算");
        Assert.Near(MathF.PI / 2f, angle, 1e-4, "右侧 = +π/2");

        Assert.True(HitDirectionMath.TryAngle(-1f, 0f, 0f, 1f, 1f, 0f, out angle), "左侧方向可换算");
        Assert.Near(-MathF.PI / 2f, angle, 1e-4, "左侧 = -π/2");

        Assert.True(HitDirectionMath.TryAngle(0f, -1f, 0f, 1f, 1f, 0f, out angle), "背后方向可换算");
        Assert.Near(MathF.PI, MathF.Abs(angle), 1e-4, "背后 = ±π（取绝对值）");

        Assert.False(HitDirectionMath.TryAngle(0f, 0f, 0f, 1f, 1f, 0f, out _), "零方向向量无法换算");
        Assert.False(HitDirectionMath.TryAngle(0f, 1f, 0f, 0f, 1f, 0f, out _), "相机朝向退化为零时无法换算");
        Assert.False(HitDirectionMath.TryAngle(0f, 1f, 0f, 1f, 0f, 0f, out _), "相机右向退化为零时无法换算");

        // ---- 二次函数包络 ----
        Assert.Near(1f, HitDirectionMath.Envelope(0f, 0.5f), 1e-4, "包络中心 = 1");
        Assert.Near(0.75f, HitDirectionMath.Envelope(0.25f, 0.5f), 1e-4, "半宽处 = 0.75（二次曲线）");
        Assert.Near(0f, HitDirectionMath.Envelope(0.5f, 0.5f), 1e-4, "边界处 = 0");
        Assert.Near(0f, HitDirectionMath.Envelope(0.51f, 0.5f), 1e-4, "边界外 = 0");
        Assert.Near(0.75f, HitDirectionMath.Envelope(-0.25f, 0.5f), 1e-4, "左右对称");
        Assert.Near(0f, HitDirectionMath.Envelope(0.1f, 0f), 1e-4, "σ = 0 时没有突起");

        // ---- 粒子分布 ----
        var particles = new List<HitDirectionParticle>();
        const float rx = 320f, ry = 150f, sigma = 0.6f, amp = 40f;
        HitDirectionMath.BuildParticles(0.3f, sigma, amp, rx, ry, 200, 12345u, particles);
        Assert.Greater(particles.Count, 100, "正常参数应生成大部分粒子");

        bool allOnEllipse = true, allInsideAmplitude = true, allOutward = true, sizeInRange = true;
        float sumTheta = 0f;
        int low = 0, high = 0;
        foreach (var p in particles)
        {
            float ellipse = (p.BaseX * p.BaseX) / (rx * rx) + (p.BaseY * p.BaseY) / (ry * ry);
            if (MathF.Abs(ellipse - 1f) > 1e-2f) allOnEllipse = false;

            if (p.Height < -Eps || p.Height > amp + Eps) allInsideAmplitude = false;
            if (p.Height < amp * 0.1f) low++;
            if (p.Height > amp * 0.75f) high++;

            float nlen = MathF.Sqrt(p.OutX * p.OutX + p.OutY * p.OutY);
            if (MathF.Abs(nlen - 1f) > 2e-3f) allOutward = false;
            if (p.SizeRatio < 0.5f || p.SizeRatio > 1.5f) sizeInRange = false;

            // 基准点对应的方位角（0 = 正前方 → y 正、x 0）
            sumTheta += MathF.Atan2(p.BaseX / rx, p.BaseY / ry);
        }

        Assert.True(allOnEllipse, "所有粒子的基准点都落在椭圆上");
        Assert.True(allInsideAmplitude, "突起高度不超过 amplitude");
        Assert.True(allOutward, "外法线是单位向量");
        Assert.True(sizeInRange, "尺寸系数在 0.5~1.5");
        Assert.Greater(low, high, "突起底部（高度 <10%）的粒子明显多于顶部（>75%）—— 底部密、顶部疏");

        float meanTheta = sumTheta / particles.Count;
        Assert.Near(0.3f, meanTheta, 0.05, "粒子群中心方位角 ≈ 传入的 angle");

        // 确定性 / 种子差异
        var again = new List<HitDirectionParticle>();
        HitDirectionMath.BuildParticles(0.3f, sigma, amp, rx, ry, 200, 12345u, again);
        Assert.Equal(particles.Count, again.Count, "同一 seed 的粒子数一致");
        bool same = again.Count == particles.Count;
        for (int i = 0; same && i < again.Count; i++)
        {
            same = MathF.Abs(again[i].BaseX - particles[i].BaseX) < 1e-6f
                && MathF.Abs(again[i].Height - particles[i].Height) < 1e-6f;
        }
        Assert.True(same, "同一 seed 的粒子分布完全一致（可复现）");

        var different = new List<HitDirectionParticle>();
        HitDirectionMath.BuildParticles(0.3f, sigma, amp, rx, ry, 200, 999u, different);
        bool anyDifferent = false;
        for (int i = 0; i < Math.Min(particles.Count, different.Count); i++)
        {
            if (MathF.Abs(different[i].BaseX - particles[i].BaseX) > 1e-4f) { anyDifferent = true; break; }
        }
        Assert.True(anyDifferent, "不同 seed 的粒子分布不同");

        // 退化参数
        HitDirectionMath.BuildParticles(0f, sigma, amp, rx, ry, 0, 1u, again);
        Assert.Equal(0, again.Count, "count = 0 不生成粒子");
        HitDirectionMath.BuildParticles(0f, 0f, amp, rx, ry, 50, 1u, again);
        Assert.Equal(0, again.Count, "σ = 0 不生成粒子");
        HitDirectionMath.BuildParticles(0f, sigma, 0f, rx, ry, 50, 1u, again);
        Assert.Equal(0, again.Count, "amplitude = 0 不生成粒子");

        // 数量上限
        HitDirectionMath.BuildParticles(0f, sigma, amp, rx, ry, 1000, 1u, again);
        Assert.True(again.Count <= 128, "粒子数量上限 128");
    }
}
