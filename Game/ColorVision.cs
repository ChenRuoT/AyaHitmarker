using System;
using System.Collections.Generic;
using System.Globalization;

namespace AyaHitmarker.Game;

/// <summary>色觉类型。<c>Normal</c> 以外的三种是完全缺失（二色视），最坏情形。</summary>
internal enum ColorVisionMode
{
    Normal = 0,

    /// <summary>红色盲（缺 L 视锥）。</summary>
    Protanopia = 1,

    /// <summary>绿色盲（缺 M 视锥）。</summary>
    Deuteranopia = 2,

    /// <summary>蓝色盲（缺 S 视锥）。</summary>
    Tritanopia = 3,
}

/// <summary>
/// 色觉缺陷模拟 + 感知色差（0.7.0，给“高对比 / 色盲友好”预设的离线对比度测试用）。
///
/// 做法：sRGB → 线性 RGB → 乘 Machado 等人 2009 年的三种缺陷矩阵（严重度 1.0）→ 回到 sRGB → CIELAB（D65）→ ΔE76。
/// 这是一个**粗算**：ΔE76 不如 CIEDE2000 贴近感知，且游戏里的颜色还会叠在各种背景上。
/// 所以它只用来回答“两种颜色在缺陷视觉下会不会几乎一样”，阈值要留余量，别当成精确的可读性证明。
/// 纯托管、不引用 Unity，所以能进离线测试。
/// </summary>
internal static class ColorVision
{
    // Machado, Oliveira, Fernandes (2009), "A Physiologically-based Model for Simulation of Color Vision Deficiency"，
    // 严重度 1.0，作用在**线性** RGB 上。每行之和 = 1（白色 / 灰色保持不变）。
    private static readonly double[][] Protan =
    {
        new[] {  0.152286,  1.052583, -0.204868 },
        new[] {  0.114503,  0.786281,  0.099216 },
        new[] { -0.003882, -0.048116,  1.051998 },
    };

    private static readonly double[][] Deutan =
    {
        new[] {  0.367322,  0.860646, -0.227968 },
        new[] {  0.280085,  0.672501,  0.047413 },
        new[] { -0.011820,  0.042940,  0.968881 },
    };

    private static readonly double[][] Tritan =
    {
        new[] {  1.255528, -0.076749, -0.178779 },
        new[] { -0.078411,  0.930809,  0.147602 },
        new[] {  0.004733,  0.691367,  0.303900 },
    };

    internal readonly struct Rgb
    {
        /// <summary>sRGB，0..1。</summary>
        internal readonly double R, G, B;

        internal Rgb(double r, double g, double b)
        {
            R = r;
            G = g;
            B = b;
        }
    }

    /// <summary>"#RRGGBB" / "#RRGGBBAA" / "RRGGBB"（Alpha 忽略）→ sRGB。和 <c>UiTheme.Parse</c> 接受同一种格式。</summary>
    internal static bool TryParseHex(string? hex, out Rgb rgb)
    {
        rgb = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;

        string s = hex!.Trim();
        if (s.Length > 0 && s[0] == '#') s = s.Substring(1);
        if (s.Length != 6 && s.Length != 8) return false;
        if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v)) return false;

        uint rgb24 = s.Length == 6 ? v : v >> 8;   // 8 位时低 8 位是 Alpha
        rgb = new Rgb(((rgb24 >> 16) & 0xFF) / 255.0, ((rgb24 >> 8) & 0xFF) / 255.0, (rgb24 & 0xFF) / 255.0);
        return true;
    }

    private static double ToLinear(double c)
        => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double ToSrgb(double c)
    {
        c = Math.Clamp(c, 0.0, 1.0);
        return c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055;
    }

    /// <summary>按色觉类型变换一个 sRGB 颜色。<c>Normal</c> 原样返回。</summary>
    internal static Rgb Simulate(Rgb c, ColorVisionMode mode)
    {
        if (mode == ColorVisionMode.Normal) return c;

        double[][] m = mode switch
        {
            ColorVisionMode.Protanopia => Protan,
            ColorVisionMode.Deuteranopia => Deutan,
            _ => Tritan,
        };

        double r = ToLinear(c.R), g = ToLinear(c.G), b = ToLinear(c.B);
        double r2 = m[0][0] * r + m[0][1] * g + m[0][2] * b;
        double g2 = m[1][0] * r + m[1][1] * g + m[1][2] * b;
        double b2 = m[2][0] * r + m[2][1] * g + m[2][2] * b;
        return new Rgb(ToSrgb(r2), ToSrgb(g2), ToSrgb(b2));
    }

    /// <summary>sRGB → CIELAB（D65）。</summary>
    internal static (double L, double A, double B) ToLab(Rgb c)
    {
        double r = ToLinear(c.R), g = ToLinear(c.G), b = ToLinear(c.B);

        double x = (0.4124564 * r + 0.3575761 * g + 0.1804375 * b) / 0.95047;
        double y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b;
        double z = (0.0193339 * r + 0.1191920 * g + 0.9503041 * b) / 1.08883;

        static double F(double t) => t > 216.0 / 24389.0 ? Math.Cbrt(t) : (24389.0 / 27.0 * t + 16.0) / 116.0;

        double fx = F(x), fy = F(y), fz = F(z);
        return (116.0 * fy - 16.0, 500.0 * (fx - fy), 200.0 * (fy - fz));
    }

    /// <summary>两种颜色在指定色觉下的 ΔE76。≈2.3 是肉眼刚能分辨，&gt;25 是一眼就能分开。</summary>
    internal static double DeltaE(Rgb a, Rgb b, ColorVisionMode mode)
    {
        var la = ToLab(Simulate(a, mode));
        var lb = ToLab(Simulate(b, mode));
        double dl = la.L - lb.L, da = la.A - lb.A, db = la.B - lb.B;
        return Math.Sqrt(dl * dl + da * da + db * db);
    }

    /// <summary>一组颜色里最难区分的那一对的 ΔE。少于两种颜色返回正无穷（没有“一对”可比）。</summary>
    internal static double MinPairwiseDeltaE(IReadOnlyList<Rgb> colors, ColorVisionMode mode)
    {
        double min = double.PositiveInfinity;
        for (int i = 0; i < colors.Count; i++)
        {
            for (int j = i + 1; j < colors.Count; j++)
            {
                min = Math.Min(min, DeltaE(colors[i], colors[j], mode));
            }
        }
        return min;
    }
}
