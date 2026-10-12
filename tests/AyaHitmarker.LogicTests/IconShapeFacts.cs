using System;
using AyaHitmarker.Assets;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// 程序化图标的像素级契约：尺寸、对称性、关键位置的颜色 / 透明度。
/// 好不好看用 <c>--render-icons</c> 渲染成 PNG 肉眼看；这里只挡「五官跑偏 / 描边消失 / 爆头裂口没切出来 / 左右不对称」。
/// </summary>
internal static class IconShapeFacts
{
    private static (int r, int g, int b, int a) Px(IconBitmap bmp, int x, int y)
    {
        int k = (y * bmp.Width + x) * 4;
        return (bmp.Rgba[k], bmp.Rgba[k + 1], bmp.Rgba[k + 2], bmp.Rgba[k + 3]);
    }

    /// <summary>骷髅局部坐标 → 像素（y 从下往上）。</summary>
    private static (int x, int y) SkullPx(IconBitmap bmp, int heightPx, float outlinePx, float lx, float ly)
    {
        int pad = (int)MathF.Ceiling(outlinePx) + 2;
        return ((int)MathF.Round(bmp.Width * 0.5f + lx * heightPx), (int)MathF.Round(pad + ly * heightPx));
    }

    internal static void Run()
    {
        Assert.Suite("Icon: 骷髅");
        {
            const int H = 88;
            const float outline = 2.6f;
            IconBitmap n = IconShapes.Skull(H, false, outline);
            IconBitmap h = IconShapes.Skull(H, true, outline);

            int pad = (int)Math.Ceiling(outline) + 2;
            Assert.Equal(H + pad * 2, n.Height, "高度 = 本体高 + 两侧描边留白");
            Assert.Equal((int)Math.Round(H * IconShapes.SkullAspect) + pad * 2, n.Width, "宽度 = 本体宽 + 两侧描边留白");
            Assert.Equal(n.Width, h.Width, "爆头版与普通版尺寸一致");
            Assert.Equal(n.Width * n.Height * 4, n.Rgba.Length, "RGBA 缓冲区大小正确");

            Assert.Equal(0, Px(n, 0, 0).a, "四个角是全透明的");
            Assert.Equal(0, Px(n, n.Width - 1, n.Height - 1).a, "右上角也是全透明");

            var (fx, fy) = SkullPx(n, H, outline, 0f, 0.75f);
            var forehead = Px(n, fx, fy);
            Assert.True(forehead.a >= 250 && forehead.r >= 250 && forehead.g >= 250 && forehead.b >= 250, "额头是不透明的白色");

            var (ex, ey) = SkullPx(n, H, outline, -0.168f, 0.525f);
            var eye = Px(n, ex, ey);
            Assert.True(eye.a >= 240, "眼窝在本体里面，是不透明的");
            Assert.True(eye.r < 60 && eye.g < 60 && eye.b < 60, "眼窝是深色（战地里的眼睛是黑的，不是镂空）");

            // 外描边：本体边缘外 1.3px 处应是半透明的黑
            var (ox, oy) = SkullPx(n, H, outline, -(0.455f + 1.3f / H), 0.60f);
            var ring = Px(n, ox, oy);
            Assert.True(ring.a > 100, "本体边缘外有描边（不透明度 > 100）");
            Assert.True(ring.r < 40 && ring.g < 40 && ring.b < 40, "描边是黑色，着色相乘后仍是黑");

            // 爆头裂口：颅顶正中，普通版是实心的，爆头版被切开
            var (tx, ty) = SkullPx(n, H, outline, 0f, 0.97f);
            Assert.True(Px(n, tx, ty).a >= 240, "普通骷髅颅顶正中是实心的");
            Assert.True(Px(h, tx, ty).a < 60, "爆头骷髅颅顶正中被 V 形裂口切开");

            // 左右对称（逐行比较，允许 3/255 的数值误差）
            bool symmetric = true;
            for (int y = 0; y < n.Height && symmetric; y++)
            {
                for (int x = 0; x < n.Width; x++)
                {
                    if (Math.Abs(Px(n, x, y).a - Px(n, n.Width - 1 - x, y).a) > 3) { symmetric = false; break; }
                }
            }
            Assert.True(symmetric, "普通骷髅左右对称");

            // 无描边：位图仍然合法，边缘留白最小
            IconBitmap bare = IconShapes.Skull(64, false, 0f);
            Assert.True(bare.Width > 0 && bare.Height > 0, "outline=0 也能生成");
            bool anyOpaque = false;
            for (int i = 3; i < bare.Rgba.Length; i += 4) if (bare.Rgba[i] > 200) { anyOpaque = true; break; }
            Assert.True(anyOpaque, "outline=0 时本体仍然存在");
        }

        Assert.Suite("Icon: 命中标记");
        {
            const int S = 256;
            float outline = 1.8f / (71f * 0.5f);   // 71px 显示、1.8px 描边
            IconBitmap m = IconShapes.HitMarker(S, outline);

            Assert.Equal(S, m.Width, "宽度 = 请求的边长");
            Assert.Equal(S, m.Height, "高度 = 请求的边长");

            bool sym = true;
            for (int y = 0; y < S && sym; y += 3)
            {
                for (int x = 0; x < S; x += 3)
                {
                    int a = Px(m, x, y).a;
                    if (Math.Abs(a - Px(m, S - 1 - x, y).a) > 2 || Math.Abs(a - Px(m, x, S - 1 - y).a) > 2) { sym = false; break; }
                }
            }
            Assert.True(sym, "四象限严格对称（四条虚线一模一样）");

            Assert.True(Px(m, S / 2, S / 2).a < 5, "中心是空的（准星位置不被遮挡）");

            // 核心：对角线上半径 0.65 处是不透明白
            float half = S * 0.5f;
            float d = 0.65f / MathF.Sqrt(2f);
            int cx = (int)MathF.Round(half + d * half);
            int cy = (int)MathF.Round(half + d * half);
            var core = Px(m, cx, cy);
            Assert.True(core.a >= 250 && core.r >= 250, "虚线核心是不透明的白色");

            // 描边：沿垂直方向偏出 (半厚 0.033 + 半描边)，应该是深色
            float off = 0.037f + outline * 0.5f;
            float k = 0.70710678f;
            int ox = (int)MathF.Round(half + (d + off * k) * half);
            int oy = (int)MathF.Round(half + (d - off * k) * half);
            var edge = Px(m, ox, oy);
            Assert.True(edge.a > 150, "核心旁边有不透明度较高的描边");
            Assert.True(edge.r < 60 && edge.g < 60 && edge.b < 60, "描边是深色");

            // 粗线版（击杀标记）：同一个垂直偏移（半厚 0.037 < 0.045 < 粗版半厚 0.052）上，
            // 普通版已经是描边（深色），粗版还在白色核心里
            float perp = 0.045f * 0.70710678f;
            int px = (int)MathF.Round(half + (d + perp) * half);
            int py = (int)MathF.Round(half + (d - perp) * half);
            IconBitmap bold = IconShapes.HitMarker(S, outline, bold: true);
            Assert.True(Px(m, px, py).r < 80, "普通核心的半厚之外 0.045 处已经是深色描边");
            Assert.True(Px(bold, px, py).r > 200 && Px(bold, px, py).a > 240, "粗线版的核心更宽：同一位置仍是不透明白");

            // 没有描边时同一个位置只剩辉光（白、较淡）
            IconBitmap bare = IconShapes.HitMarker(S, 0f);
            var glowOnly = Px(bare, ox, oy);
            Assert.True(glowOnly.r > 200 || glowOnly.a < 120, "无描边时该处只有淡白色辉光");

            // 位图不会画到边缘外（最外一圈应当是透明的，否则缩放时会被裁出硬边）
            bool borderClear = true;
            for (int i = 0; i < S; i++)
            {
                if (Px(m, i, 0).a > 8 || Px(m, i, S - 1).a > 8 || Px(m, 0, i).a > 8 || Px(m, S - 1, i).a > 8) { borderClear = false; break; }
            }
            Assert.True(borderClear, "最外一圈像素是透明的（辉光没有被裁成硬边）");
        }

        Assert.Suite("Icon: 多杀徽记");
        {
            int prevW = 0;
            bool widthNonDecreasing = true;
            int height = -1;
            bool sameHeight = true;
            for (int count = 1; count <= 5; count++)
            {
                IconBitmap e = IconShapes.Emblem(count, 72);
                if (e.Width < prevW) widthNonDecreasing = false;
                prevW = e.Width;
                if (height < 0) height = e.Height; else if (e.Height != height) sameHeight = false;
                Assert.Equal(e.Width * e.Height * 4, e.Rgba.Length, $"徽记 x{count} 缓冲区大小正确");
            }
            Assert.True(widthNonDecreasing, "击杀数越多徽记越宽（或相等）");
            Assert.True(sameHeight, "各档徽记高度一致（横幅里可以统一摆放）");
            Assert.Greater(IconShapes.Emblem(5, 72).Width, IconShapes.Emblem(2, 72).Width, "5 杀比 2 杀宽");

            // 速度线：最长那道（中间）在骷髅簇左侧一段距离处是不透明的
            IconBitmap two = IconShapes.Emblem(2, 72);
            float s = 72f;
            int pad = 3;
            float cx = two.Width * 0.5f;
            float cy = pad + s * 0.60f;
            int bx = (int)MathF.Round(cx + (-(0.93f + 0.07f + 0.30f)) * s);
            int by = (int)MathF.Round(cy);
            Assert.True(Px(two, bx, by).a > 200, "骷髅簇左侧有速度线");
            int rx = (int)MathF.Round(cx + (0.93f + 0.07f + 0.30f) * s);
            Assert.True(Px(two, rx, by).a > 200, "骷髅簇右侧有速度线（左右对称）");

            // 4 / 5 杀冒头的骷髅（连同它的分隔描边）在大尺寸下也不能被位图上沿裁掉 ——
            // 画布高度只给 1.45 倍时，UI 缩放 ≥ 2 的烘焙尺寸（128px 以上）顶部会被裁出一道平边
            bool headroom = true;
            foreach (int skullPx in new[] { 72, 128, 192 })
            {
                foreach (int n in new[] { 4, 5 })
                {
                    IconBitmap eb = IconShapes.Emblem(n, skullPx);
                    int topRow = eb.Height - 1;
                    for (int x = 0; x < eb.Width; x++)
                    {
                        if (Px(eb, x, topRow).a > 8) headroom = false;
                    }
                }
            }
            Assert.True(headroom, "4 / 5 杀徽记在 72 / 128 / 192px 下最上面一行都是透明的（冒头的骷髅没被裁）");
            Assert.Near(0.175, IconShapes.EmblemCenterShift, 1e-6, "位图中心比主骷髅中心高 0.175 个骷髅高（UI 据此把位图抬回去对齐横幅）");

            // 4 杀有一个冒头的骷髅：中轴线上方（骷髅簇顶部之上）应有不透明像素
            IconBitmap four = IconShapes.Emblem(4, 72);
            int tx = (int)MathF.Round(four.Width * 0.5f);
            int ty = (int)MathF.Round(3 + 72f * 0.60f + 0.50f * 72f + 0.25f * 72f);
            Assert.True(Px(four, tx, ty).a > 200, "4 杀时顶部多一个冒出来的骷髅");
            IconBitmap twoSame = IconShapes.Emblem(2, 72);
            Assert.True(Px(twoSame, twoSame.Width / 2, ty).a < 20, "而 2 杀同一位置是空的");
        }

        Assert.Suite("Icon: 标签框九宫格");
        {
            IconBitmap b = IconShapes.TagBox(2);
            Assert.Equal(8, b.Width, "边长 = 2 × (border + 2)");
            Assert.Equal(8, b.Height, "正方形");

            var corner = Px(b, 0, 0);
            Assert.True(corner.r == 255 && corner.a > 200, "最外一圈是不透明的白色描边");
            var inner = Px(b, 3, 3);
            Assert.True(inner.r == 0 && inner.a > 100 && inner.a < 200, "里面是半透明黑色填充");
            Assert.Equal(255, Px(b, 1, 4).r, "描边宽 2 像素：第 2 列仍是描边");
            Assert.Equal(0, Px(b, 2, 4).r, "第 3 列已是填充");

            Assert.Equal(6, IconShapes.TagBox(99).Width / 2 - 2, "描边宽度被钳到 6 以内");
            Assert.Equal(1, IconShapes.TagBox(0).Width / 2 - 2, "描边宽度至少 1 像素");
        }
    }
}
