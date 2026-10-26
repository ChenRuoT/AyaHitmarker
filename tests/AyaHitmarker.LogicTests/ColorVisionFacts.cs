using System.Collections.Generic;
using AyaHitmarker.Game;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// 色觉缺陷模拟 + 感知色差（0.7.0）。这里锁死的是**公式本身**；
/// “高对比预设够不够清楚”的断言在 <c>PresetFacts</c> 里，用的就是这个文件的函数。
///
/// 参考值：用独立的 Python 实现（同一组 Machado 2009 矩阵 + CIELAB D65）算出，
/// 与 0.7.0 调研时对 0.6.1 默认配色的粗算结果一致（白 vs #FF4D4D = 88 / 56 / 58 / 99 …）。
/// </summary>
internal static class ColorVisionFacts
{
    private const double Tol = 0.3;

    private static ColorVision.Rgb Hex(string hex)
    {
        Assert.True(ColorVision.TryParseHex(hex, out var rgb), "测试数据本身是合法颜色: " + hex);
        return rgb;
    }

    private static double De(string a, string b, ColorVisionMode mode) => ColorVision.DeltaE(Hex(a), Hex(b), mode);

    internal static void Run()
    {
        Assert.Suite("色觉: 颜色字符串解析");
        {
            Assert.True(ColorVision.TryParseHex("#FF0000", out var r), "#RRGGBB");
            Assert.Near(1.0, r.R, 1e-9, "红通道 1");
            Assert.Near(0.0, r.G, 1e-9, "绿通道 0");

            Assert.True(ColorVision.TryParseHex("#00FF0080", out var g), "#RRGGBBAA 解析成功");
            Assert.Near(1.0, g.G, 1e-9, "带 Alpha 时颜色取前 6 位");
            Assert.Near(0.0, g.R, 1e-9, "Alpha 不会混进红通道");

            Assert.True(ColorVision.TryParseHex("0000FF", out var b), "不带 # 也行（与 UiTheme.Parse 一致）");
            Assert.Near(1.0, b.B, 1e-9, "蓝通道 1");

            Assert.False(ColorVision.TryParseHex("#GG0000", out _), "非十六进制");
            Assert.False(ColorVision.TryParseHex("#FFF", out _), "3 位简写不支持");
            Assert.False(ColorVision.TryParseHex("#FF00", out _), "长度不对");
            Assert.False(ColorVision.TryParseHex("", out _), "空串");
            Assert.False(ColorVision.TryParseHex(null, out _), "null");
        }

        Assert.Suite("色觉: 模拟矩阵保持中性色");
        {
            // 矩阵每行之和 = 1：白 / 灰在任何缺陷视觉下都不变色
            foreach (ColorVisionMode mode in new[] { ColorVisionMode.Protanopia, ColorVisionMode.Deuteranopia, ColorVisionMode.Tritanopia })
            {
                var white = ColorVision.Simulate(new ColorVision.Rgb(1, 1, 1), mode);
                Assert.Near(1.0, white.R, 2e-3, mode + ": 白的 R");
                Assert.Near(1.0, white.G, 2e-3, mode + ": 白的 G");
                Assert.Near(1.0, white.B, 2e-3, mode + ": 白的 B");

                var gray = ColorVision.Simulate(new ColorVision.Rgb(0.5, 0.5, 0.5), mode);
                Assert.Near(0.5, gray.R, 2e-3, mode + ": 灰的 R");
                Assert.Near(0.5, gray.G, 2e-3, mode + ": 灰的 G");
                Assert.Near(0.5, gray.B, 2e-3, mode + ": 灰的 B");
            }

            var same = ColorVision.Simulate(Hex("#FF4D4D"), ColorVisionMode.Normal);
            Assert.Near(Hex("#FF4D4D").G, same.G, 1e-12, "Normal 原样返回");

            // 红色盲看纯红：偏暗的黄褐色（独立实现算得 0.427, 0.373, 0.0）
            var red = ColorVision.Simulate(new ColorVision.Rgb(1, 0, 0), ColorVisionMode.Protanopia);
            Assert.Near(0.427, red.R, 2e-3, "红色盲看纯红: R");
            Assert.Near(0.373, red.G, 2e-3, "红色盲看纯红: G");
            Assert.Near(0.0, red.B, 2e-3, "红色盲看纯红: B");
        }

        Assert.Suite("色觉: 色差 ΔE76");
        {
            Assert.Near(0.0, De("#33CC99", "#33CC99", ColorVisionMode.Normal), 1e-9, "同色 ΔE = 0");
            Assert.Near(100.0, De("#000000", "#FFFFFF", ColorVisionMode.Normal), 0.01, "黑白 ΔE = 100（只差 L）");
            Assert.Near(100.0, De("#000000", "#FFFFFF", ColorVisionMode.Deuteranopia), 0.05, "黑白在缺陷视觉下依然 100");

            // 经典的红绿：常人一眼分开，绿色盲/红色盲大幅缩水
            Assert.Near(170.6, De("#FF0000", "#00FF00", ColorVisionMode.Normal), Tol, "红 vs 绿：常人");
            Assert.Near(65.9, De("#FF0000", "#00FF00", ColorVisionMode.Protanopia), Tol, "红 vs 绿：红色盲");
            Assert.Near(27.8, De("#FF0000", "#00FF00", ColorVisionMode.Deuteranopia), Tol, "红 vs 绿：绿色盲");
            Assert.Near(153.2, De("#FF0000", "#00FF00", ColorVisionMode.Tritanopia), Tol, "红 vs 绿：蓝色盲几乎不受影响");
            Assert.True(De("#FF0000", "#00FF00", ColorVisionMode.Deuteranopia) < De("#FF0000", "#00FF00", ColorVisionMode.Normal) * 0.25,
                "绿色盲下红绿的差距缩到常人的 1/4 以内");

            // 对称
            Assert.Near(De("#FF4D4D", "#FFD24A", ColorVisionMode.Deuteranopia),
                De("#FFD24A", "#FF4D4D", ColorVisionMode.Deuteranopia), 1e-9, "ΔE 对称");
        }

        Assert.Suite("色觉: 0.6.1 默认配色的已知弱点（预设要修的问题）");
        {
            // 命中标记：普通白 vs 爆头 #FF4D4D —— 亮度差撑着，红绿色盲下仍有 56~58
            Assert.Near(87.8, De("#FFFFFF", "#FF4D4D", ColorVisionMode.Normal), Tol, "白 vs 爆头标记：常人");
            Assert.Near(56.4, De("#FFFFFF", "#FF4D4D", ColorVisionMode.Protanopia), Tol, "白 vs 爆头标记：红色盲");
            Assert.Near(58.3, De("#FFFFFF", "#FF4D4D", ColorVisionMode.Deuteranopia), Tol, "白 vs 爆头标记：绿色盲");

            // 爆头标记 vs 击杀标记：几乎同色，任何人都难分（这对要靠形状 / 大小 / 时长区分）
            Assert.Near(8.1, De("#FF4D4D", "#FF4A3D", ColorVisionMode.Normal), Tol, "爆头标记 vs 击杀标记：常人");
            Assert.Near(3.5, De("#FF4D4D", "#FF4A3D", ColorVisionMode.Tritanopia), Tol, "爆头标记 vs 击杀标记：蓝色盲");

            // 伤害数字：爆头 #FF8A7A vs 击杀 #FF4A3D —— 最弱的一对
            Assert.Near(33.1, De("#FF8A7A", "#FF4A3D", ColorVisionMode.Normal), Tol, "伤害数字 爆头 vs 击杀：常人");
            Assert.Near(20.6, De("#FF8A7A", "#FF4A3D", ColorVisionMode.Protanopia), Tol, "伤害数字 爆头 vs 击杀：红色盲");

            // 受击方向：黄 #FFD24A vs 红 #FF3B30 —— 绿色盲下只有 27
            Assert.Near(77.1, De("#FFD24A", "#FF3B30", ColorVisionMode.Normal), Tol, "受击方向 黄 vs 红：常人");
            Assert.Near(27.3, De("#FFD24A", "#FF3B30", ColorVisionMode.Deuteranopia), Tol, "受击方向 黄 vs 红：绿色盲");
        }

        Assert.Suite("色觉: 一组颜色里最难分的一对");
        {
            var colors = new List<ColorVision.Rgb> { Hex("#FFFFFF"), Hex("#FF4D4D"), Hex("#FF4A3D") };
            Assert.Near(8.1, ColorVision.MinPairwiseDeltaE(colors, ColorVisionMode.Normal), Tol, "三色里最难分的是两个红");
            Assert.Near(3.5, ColorVision.MinPairwiseDeltaE(colors, ColorVisionMode.Tritanopia), Tol, "蓝色盲下更糟");

            Assert.True(double.IsPositiveInfinity(ColorVision.MinPairwiseDeltaE(new List<ColorVision.Rgb> { Hex("#FFFFFF") }, ColorVisionMode.Normal)),
                "只有一种颜色：没有“一对”，返回正无穷");
            Assert.True(double.IsPositiveInfinity(ColorVision.MinPairwiseDeltaE(new List<ColorVision.Rgb>(), ColorVisionMode.Normal)),
                "空集合同理");
        }
    }
}
