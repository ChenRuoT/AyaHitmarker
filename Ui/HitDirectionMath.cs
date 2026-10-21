using System;
using System.Collections.Generic;

namespace AyaHitmarker.Ui;

/// <summary>
/// 受击方向指示的**纯逻辑**几何 / 粒子分布（不引用 UnityEngine，可离线测试）。
///
/// 约定（和战地风格的「椭圆盘」一致）：
/// <list type="bullet">
///   <item>角度 <c>0</c> = 正前方，显示在椭圆**上端**；<c>+π/2</c> = 右侧；<c>±π</c> = 背后（下端）；</item>
///   <item>椭圆长轴在水平方向：<c>x = sin(angle) * radiusX</c>、<c>y = cos(angle) * radiusY</c>；</item>
///   <item>突起是二次函数包络：<c>h(t) = amplitude * (1 - (t/σ)²)</c>，<c>|t| &gt; σ</c> 时没有粒子；</item>
///   <item>粒子在突起底部密、顶部疏：高度系数 <c>u = rnd²</c>，越靠近底部（u 小）粒子越多，向外位移 = <c>h * u</c>。</item>
/// </list>
/// </summary>
internal readonly struct HitDirectionParticle
{
    /// <summary>粒子在椭圆上的基准位置（相对屏幕中心，画布单位）。</summary>
    internal readonly float BaseX;
    internal readonly float BaseY;

    /// <summary>椭圆外法线方向（单位向量）。</summary>
    internal readonly float OutX;
    internal readonly float OutY;

    /// <summary>该粒子的向外突起高度（画布单位）。</summary>
    internal readonly float Height;

    /// <summary>尺寸系数（0.55~1.45），底部粒子略大。</summary>
    internal readonly float SizeRatio;

    internal HitDirectionParticle(float baseX, float baseY, float outX, float outY, float height, float sizeRatio)
    {
        BaseX = baseX;
        BaseY = baseY;
        OutX = outX;
        OutY = outY;
        Height = height;
        SizeRatio = sizeRatio;
    }
}

internal static class HitDirectionMath
{
    /// <summary>角度换算失败（方向向量退化 / 相机朝向退化）时返回 false。</summary>
    internal static bool TryAngle(float dirX, float dirZ,
        float forwardX, float forwardZ, float rightX, float rightZ, out float angle)
    {
        angle = 0f;
        float fx = forwardX, fz = forwardZ;
        float flen = MathF.Sqrt(fx * fx + fz * fz);
        if (flen < 1e-4f) return false;
        fx /= flen;
        fz /= flen;

        float rx = rightX, rz = rightZ;
        float rlen = MathF.Sqrt(rx * rx + rz * rz);
        if (rlen < 1e-4f) return false;
        rx /= rlen;
        rz /= rlen;

        float vlen = MathF.Sqrt(dirX * dirX + dirZ * dirZ);
        if (vlen < 1e-4f) return false;

        float forwardDot = (dirX * fx + dirZ * fz) / vlen;
        float rightDot = (dirX * rx + dirZ * rz) / vlen;
        angle = MathF.Atan2(rightDot, forwardDot);
        return true;
    }

    /// <summary>二次函数包络：t 在 ±sigma 内为 1..0，之外为 0。</summary>
    internal static float Envelope(float t, float sigma)
    {
        if (sigma <= 1e-4f) return 0f;
        float n = t / sigma;
        if (n <= -1f || n >= 1f) return 0f;
        return 1f - n * n;
    }

    /// <summary>
    /// 生成一组突起粒子。同一次命中的结果由 <paramref name="seed"/> 确定，便于离线测试 / 复现截图。
    /// </summary>
    /// <param name="angle">中心方位角（0 = 正前，+ = 右）。</param>
    /// <param name="sigma">突起角宽度（弧度，半宽）。</param>
    /// <param name="amplitude">最大突起高度（画布单位）。</param>
    /// <param name="radiusX">椭圆水平半径。</param>
    /// <param name="radiusY">椭圆垂直半径。</param>
    /// <param name="count">粒子数量（0~128；越界会被钳制）。</param>
    /// <param name="seed">随机种子。</param>
    internal static void BuildParticles(float angle, float sigma, float amplitude, float radiusX, float radiusY,
        int count, uint seed, List<HitDirectionParticle> into)
    {
        if (into == null) return;
        into.Clear();
        count = Math.Clamp(count, 0, 128);
        if (count == 0 || sigma <= 1e-4f || amplitude <= 0f || radiusX <= 0f || radiusY <= 0f) return;

        uint state = seed == 0 ? 0x9E3779B9u : seed;
        for (int i = 0; i < count; i++)
        {
            float r1 = Next01(ref state);
            float r2 = Next01(ref state);

            // 底部密、顶部疏：u = rnd² ；同一高度下横向偏移按 sqrt(1-u) 收窄（正好落在抛物线内）
            float u = r1 * r1;
            float spread = sigma * MathF.Sqrt(MathF.Max(0f, 1f - u));
            float t = (r2 * 2f - 1f) * spread;

            float theta = angle + t;
            float sin = MathF.Sin(theta);
            float cos = MathF.Cos(theta);

            float baseX = sin * radiusX;
            float baseY = cos * radiusY;

            // 椭圆外法线：梯度 (x/rx², y/ry²) 归一化
            float nx = sin / radiusX;
            float ny = cos / radiusY;
            float nlen = MathF.Sqrt(nx * nx + ny * ny);
            if (nlen < 1e-5f) continue;
            nx /= nlen;
            ny /= nlen;

            float height = amplitude * Envelope(t, sigma) * u;
            float sizeRatio = 0.55f + 0.9f * (1f - u);
            into.Add(new HitDirectionParticle(baseX, baseY, nx, ny, height, sizeRatio));
        }
    }

    /// <summary>确定性伪随机（不依赖 UnityEngine.Random，离线测试可复现）。</summary>
    private static float Next01(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state & 0x00FFFFFF) / 16777215f;
    }
}
