using System;
using System.Collections.Generic;
using System.IO;
using AyaHitmarker.Assets;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// <c>dotnet run --project tests/AyaHitmarker.LogicTests -- --render-icons &lt;目录&gt;</c>
/// 把程序化图标渲染成 PNG 联系表。图标好不好看没法写断言，但不必为此进游戏 ——
/// 这里按游戏里的做法（2 倍烘焙 + 缩小显示 + <c>Image.color</c> 相乘着色）合成到几种典型背景上。
/// </summary>
internal static class IconPreview
{
    private static readonly (string name, byte r, byte g, byte b)[] Backgrounds =
    {
        ("dark", 20, 24, 28),
        ("mid", 128, 128, 128),
        ("white", 255, 255, 255),
        ("snow", 214, 224, 234),
        ("grass", 70, 96, 52),
    };

    private sealed class Sheet
    {
        internal readonly int W;
        internal readonly int H;
        internal readonly byte[] Rgb;

        internal Sheet(int w, int h)
        {
            W = w;
            H = h;
            Rgb = new byte[w * h * 3];
        }

        internal void FillRect(int x0, int y0, int w, int h, byte r, byte g, byte b)
        {
            for (int y = y0; y < y0 + h && y < H; y++)
            {
                for (int x = x0; x < x0 + w && x < W; x++)
                {
                    if (x < 0 || y < 0) continue;
                    int k = (y * W + x) * 3;
                    Rgb[k] = r;
                    Rgb[k + 1] = g;
                    Rgb[k + 2] = b;
                }
            }
        }

        /// <summary>把位图（tint 相乘着色）以指定显示尺寸叠到 (x0,y0) 左上角。</summary>
        internal void Blit(IconBitmap bmp, int x0, int y0, float displayScale, (float r, float g, float b) tint, float opacity = 1f)
        {
            int dw = Math.Max(1, (int)MathF.Round(bmp.Width * displayScale));
            int dh = Math.Max(1, (int)MathF.Round(bmp.Height * displayScale));
            float inv = 1f / displayScale;

            for (int dy = 0; dy < dh; dy++)
            {
                for (int dx = 0; dx < dw; dx++)
                {
                    // 面积平均（等价于 GPU 的 mipmap 缩小），在预乘空间里做
                    float sx0 = dx * inv, sx1 = (dx + 1) * inv;
                    float sy0 = dy * inv, sy1 = (dy + 1) * inv;
                    float pr = 0, pg = 0, pb = 0, pa = 0, wsum = 0;

                    for (int sy = (int)sy0; sy < Math.Min(bmp.Height, (int)MathF.Ceiling(sy1)); sy++)
                    {
                        float wy = Math.Min(sy + 1, sy1) - Math.Max(sy, sy0);
                        if (wy <= 0) continue;
                        int srcRow = bmp.Height - 1 - sy;   // 位图第 0 行在最下面，画布从上往下
                        for (int sx = (int)sx0; sx < Math.Min(bmp.Width, (int)MathF.Ceiling(sx1)); sx++)
                        {
                            float wx = Math.Min(sx + 1, sx1) - Math.Max(sx, sx0);
                            if (wx <= 0) continue;
                            float wgt = wx * wy;
                            int k = (srcRow * bmp.Width + sx) * 4;
                            float a = bmp.Rgba[k + 3] / 255f;
                            pr += bmp.Rgba[k] / 255f * a * wgt;
                            pg += bmp.Rgba[k + 1] / 255f * a * wgt;
                            pb += bmp.Rgba[k + 2] / 255f * a * wgt;
                            pa += a * wgt;
                            wsum += wgt;
                        }
                    }

                    if (wsum <= 0 || pa <= 0) continue;
                    float a2 = pa / wsum * opacity;
                    float r = pr / pa * tint.r;
                    float g = pg / pa * tint.g;
                    float b = pb / pa * tint.b;

                    int px = x0 + dx, py = y0 + dy;
                    if (px < 0 || py < 0 || px >= W || py >= H) continue;
                    int o = (py * W + px) * 3;
                    Rgb[o] = (byte)Math.Clamp((int)MathF.Round((r * a2 + Rgb[o] / 255f * (1 - a2)) * 255f), 0, 255);
                    Rgb[o + 1] = (byte)Math.Clamp((int)MathF.Round((g * a2 + Rgb[o + 1] / 255f * (1 - a2)) * 255f), 0, 255);
                    Rgb[o + 2] = (byte)Math.Clamp((int)MathF.Round((b * a2 + Rgb[o + 2] / 255f * (1 - a2)) * 255f), 0, 255);
                }
            }
        }

        internal void Save(string path) => PngWriter.Write(path, W, H, Rgb);
    }

    private static (float r, float g, float b) Hex(int rgb)
        => (((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

    internal static void Render(string dir)
    {
        Directory.CreateDirectory(dir);
        RenderSkulls(Path.Combine(dir, "skulls.png"));
        RenderEmblems(Path.Combine(dir, "emblems.png"));
        RenderHitMarkers(Path.Combine(dir, "hitmarkers.png"));
        RenderArmorMarkers(Path.Combine(dir, "armor_markers.png"));
        RenderTagBox(Path.Combine(dir, "tagbox.png"));
        Console.WriteLine("rendered icon sheets to " + Path.GetFullPath(dir));
        Bench();
    }

    /// <summary>
    /// 典型尺寸下各图标的烘焙耗时（毫秒）。游戏里是主线程同步烘焙，首次出现时会卡一帧，
    /// 所以 <c>ProceduralIcons.Warmup</c> 在载入阶段就把它们烘好 —— 这里用来确认「值不值得预热」。
    /// </summary>
    private static void Bench()
    {
        void Time(string name, Action action)
        {
            action();   // 预热 JIT
            var sw = System.Diagnostics.Stopwatch.StartNew();
            const int n = 5;
            for (int i = 0; i < n; i++) action();
            sw.Stop();
            Console.WriteLine($"  bake {name,-22} {sw.Elapsed.TotalMilliseconds / n,7:0.0} ms");
        }

        Console.WriteLine("bake timings (avg of 5):");
        Time("skull 88px", () => IconShapes.Skull(88, false, 4.4f));
        Time("skull headshot 88px", () => IconShapes.Skull(88, true, 4.4f));
        Time("emblem x2 @72", () => IconShapes.Emblem(2, 72));
        Time("emblem x5 @72", () => IconShapes.Emblem(5, 72));
        Time("hit marker 144px", () => IconShapes.HitMarker(144, 0.04f));
        Time("hit marker bold 144px", () => IconShapes.HitMarker(144, 0.04f, true));
        Time("armor marker 142px", () => IconShapes.ArmorMarker(142, 1.8f / (71f * 0.5f)));
        Time("tag box", () => IconShapes.TagBox(2));
    }

    private static void RenderSkulls(string path)
    {
        // 行 = 背景；列 = [白 44px][爆头红 44px][白 32px][爆头红 32px][白 176px][爆头红 176px]
        var white = Hex(0xFFFFFF);
        var coral = Hex(0xFF6A5E);
        var ice = Hex(0xDCE4EC);
        int cell = 210;
        var sheet = new Sheet(cell * 6, cell * Backgrounds.Length);

        for (int row = 0; row < Backgrounds.Length; row++)
        {
            var bg = Backgrounds[row];
            sheet.FillRect(0, row * cell, sheet.W, cell, bg.r, bg.g, bg.b);

            // 44px 显示（2 倍烘焙）
            var n44 = IconShapes.Skull(88, false, 2.6f);
            var h44 = IconShapes.Skull(88, true, 2.6f);
            sheet.Blit(n44, 0 * cell + 70, row * cell + 70, 0.5f, ice);
            sheet.Blit(h44, 1 * cell + 70, row * cell + 70, 0.5f, coral);

            var n32 = IconShapes.Skull(64, false, 2.0f);
            var h32 = IconShapes.Skull(64, true, 2.0f);
            sheet.Blit(n32, 2 * cell + 70, row * cell + 70, 0.5f, ice);
            sheet.Blit(h32, 3 * cell + 70, row * cell + 70, 0.5f, coral);

            // 大图（1:1）
            var nBig = IconShapes.Skull(176, false, 4f);
            var hBig = IconShapes.Skull(176, true, 4f);
            sheet.Blit(nBig, 4 * cell + 10, row * cell + 5, 1f, white);
            sheet.Blit(hBig, 5 * cell + 10, row * cell + 5, 1f, coral);
        }
        sheet.Save(path);
    }

    private static void RenderEmblems(string path)
    {
        var ice = Hex(0xE6EDF4);
        int cellW = 380, cellH = 150;
        var sheet = new Sheet(cellW * 3, cellH * 2 * 2);

        int[] counts = { 2, 3, 4, 5, 1, 3 };
        for (int bgRow = 0; bgRow < 2; bgRow++)
        {
            var bg = Backgrounds[bgRow == 0 ? 0 : 1];
            sheet.FillRect(0, bgRow * cellH * 2, sheet.W, cellH * 2, bg.r, bg.g, bg.b);
            for (int i = 0; i < counts.Length; i++)
            {
                int col = i % 3, rr = i / 3;
                // 横幅里骷髅高 36px → 2 倍烘焙 72px，缩小 0.5 显示
                var e = IconShapes.Emblem(counts[i], 72);
                sheet.Blit(e, col * cellW + 20, bgRow * cellH * 2 + rr * cellH + 30, 0.5f, ice);
            }
        }
        sheet.Save(path);
    }

    private static void RenderHitMarkers(string path)
    {
        int cell = 170;
        // 列 = 描边 [0, 1.2, 1.8, 2.6]（屏幕像素，显示尺寸 71）× 白；再加 红色 + 大尺寸
        float[] outlinePx = { 0f, 1.2f, 1.8f, 2.6f };
        var sheet = new Sheet(cell * 6, cell * Backgrounds.Length);
        var white = Hex(0xFFFFFF);
        var red = Hex(0xFF4A3D);

        for (int row = 0; row < Backgrounds.Length; row++)
        {
            var bg = Backgrounds[row];
            sheet.FillRect(0, row * cell, sheet.W, cell, bg.r, bg.g, bg.b);

            for (int c = 0; c < outlinePx.Length; c++)
            {
                const int display = 71;
                float local = outlinePx[c] / (display * 0.5f);
                var bmp = IconShapes.HitMarker(256, local);
                float scale = display / 256f;
                sheet.Blit(bmp, c * cell + (cell - display) / 2, row * cell + (cell - display) / 2, scale, white);
            }

            // 击杀红色版：显示尺寸 71 * 1.5，描边 1.8px
            {
                const int display = 110;
                var bmp = IconShapes.HitMarker(256, 1.8f / (71 * 0.5f));
                sheet.Blit(bmp, 4 * cell + (cell - display) / 2, row * cell + (cell - display) / 2, display / 256f, red);
            }

            // 1:1 大图
            {
                var bmp = IconShapes.HitMarker(256, 1.8f / (71 * 0.5f));
                sheet.Blit(bmp, 5 * cell + 0, row * cell + 0, 0.66f, white);
            }
        }
        sheet.Save(path);
    }

    private static void RenderArmorMarkers(string path)
    {
        int cell = 170;
        // 列 = 描边 [0, 1.2, 1.8, 2.6]（屏幕像素，显示 71）× 白；再加蓝灰着色 + 1:1 大图
        float[] outlinePx = { 0f, 1.2f, 1.8f, 2.6f };
        var sheet = new Sheet(cell * 6, cell * Backgrounds.Length);
        var white = Hex(0xFFFFFF);
        // 护甲命中预期的蓝灰色（运行时由调用方 Image.color 相乘着色，这里只模拟）
        var steel = Hex(0x9FB8D4);

        for (int row = 0; row < Backgrounds.Length; row++)
        {
            var bg = Backgrounds[row];
            sheet.FillRect(0, row * cell, sheet.W, cell, bg.r, bg.g, bg.b);

            for (int c = 0; c < outlinePx.Length; c++)
            {
                const int display = 71;
                float local = outlinePx[c] / (display * 0.5f);
                var bmp = IconShapes.ArmorMarker(256, local);
                float scale = display / 256f;
                sheet.Blit(bmp, c * cell + (cell - display) / 2, row * cell + (cell - display) / 2, scale, white);
            }

            // 蓝灰着色版：显示尺寸 71，描边 1.8px
            {
                const int display = 71;
                var bmp = IconShapes.ArmorMarker(256, 1.8f / (display * 0.5f));
                sheet.Blit(bmp, 4 * cell + (cell - display) / 2, row * cell + (cell - display) / 2, display / 256f, steel);
            }

            // 1:1 大图
            {
                var bmp = IconShapes.ArmorMarker(256, 1.8f / (71f * 0.5f));
                sheet.Blit(bmp, 5 * cell + 0, row * cell + 0, 0.66f, white);
            }
        }
        sheet.Save(path);
    }

    private static void RenderTagBox(string path)
    {
        var bmp = IconShapes.TagBox(2);
        var sheet = new Sheet(300, 120);
        sheet.FillRect(0, 0, 300, 120, 60, 70, 60);
        sheet.Blit(bmp, 20, 20, 3f, Hex(0xFFFFFF));
        sheet.Save(path);
    }
}
