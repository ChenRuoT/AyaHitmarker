using System;
using System.Collections.Generic;
using System.IO;
using AyaHitmarker.Assets;
using AyaHitmarker.Ui;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// <c>dotnet run --project tests/AyaHitmarker.LogicTests -- --render-hitdir &lt;目录&gt;</c>
/// 把受击方向点阵按游戏里的参数（<see cref="HitDirectionMath.ShapeFor"/> / <see cref="HitDirectionMath.BuildLattice"/> /
/// 时间线 / <see cref="IconShapes.LatticeDotCoverage"/> 的点形状）渲染成 PNG，1 画布单位 = 1 像素（UI 缩放 1）。
/// 只用来调视觉，不进游戏；最终效果以 DebugHarness 截图为准（<c>tools/verify/0.6e-hitdir.txt</c>）。
/// </summary>
internal static class HitDirectionPreview
{
    private const float RadiusX = 320f;
    private const float RadiusY = 150f;

    private static readonly (float r, float g, float b) Yellow = (1f, 0.824f, 0.290f);   // #FFD24A
    private static readonly (float r, float g, float b) Red = (1f, 0.231f, 0.188f);      // #FF3B30
    private static readonly (float r, float g, float b) White = (1f, 1f, 1f);

    private sealed class Frame
    {
        internal readonly int W, H;
        internal readonly float[] Rgb;

        internal Frame(int w, int h, (byte r, byte g, byte b) bg)
        {
            W = w;
            H = h;
            Rgb = new float[w * h * 3];
            for (int i = 0; i < w * h; i++)
            {
                Rgb[i * 3] = bg.r / 255f;
                Rgb[i * 3 + 1] = bg.g / 255f;
                Rgb[i * 3 + 2] = bg.b / 255f;
            }
        }

        private void Over(int x, int y, float r, float g, float b, float a)
        {
            if (a <= 0f || x < 0 || y < 0 || x >= W || y >= H) return;
            int k = (y * W + x) * 3;
            Rgb[k] = r * a + Rgb[k] * (1f - a);
            Rgb[k + 1] = g * a + Rgb[k + 1] * (1f - a);
            Rgb[k + 2] = b * a + Rgb[k + 2] * (1f - a);
        }

        /// <summary>画一个点阵粒子（4×4 超采样）。cx/cy 是图像像素坐标（y 向下）。</summary>
        internal void Dot(float cx, float cy, float size, (float r, float g, float b) tint, float alpha, float zoom)
        {
            float radius = size * 0.5f * zoom;
            int x0 = (int)MathF.Floor(cx - radius), x1 = (int)MathF.Ceiling(cx + radius);
            int y0 = (int)MathF.Floor(cy - radius), y1 = (int)MathF.Ceiling(cy + radius);
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float core = 0f, rim = 0f;
                    for (int sy = 0; sy < 4; sy++)
                    {
                        for (int sx = 0; sx < 4; sx++)
                        {
                            float px = x + (sx + 0.5f) / 4f - cx;
                            float py = y + (sy + 0.5f) / 4f - cy;
                            var (c, k) = IconShapes.LatticeDotCoverage(MathF.Sqrt(px * px + py * py) / radius);
                            core += c;
                            rim += k;
                        }
                    }
                    core /= 16f;
                    rim /= 16f;
                    // 和位图一致：先暗描边、再白芯（乘 tint），整体再乘透明度
                    Over(x, y, 0f, 0f, 0f, rim * (1f - core) * alpha);
                    Over(x, y, tint.r, tint.g, tint.b, core * alpha);
                }
            }
        }

        internal void Blit(Frame src, int ox, int oy)
        {
            for (int y = 0; y < src.H; y++)
            {
                for (int x = 0; x < src.W; x++)
                {
                    int dx = ox + x, dy = oy + y;
                    if (dx < 0 || dy < 0 || dx >= W || dy >= H) continue;
                    int s = (y * src.W + x) * 3, d = (dy * W + dx) * 3;
                    Rgb[d] = src.Rgb[s];
                    Rgb[d + 1] = src.Rgb[s + 1];
                    Rgb[d + 2] = src.Rgb[s + 2];
                }
            }
        }

        internal void Save(string path)
        {
            var bytes = new byte[W * H * 3];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)Math.Clamp((int)MathF.Round(Rgb[i] * 255f), 0, 255);
            PngWriter.Write(path, W, H, bytes);
        }
    }

    private readonly struct Shot
    {
        internal readonly string Name;
        internal readonly float Degrees, Damage;
        internal readonly bool Armor;

        internal Shot(string name, float degrees, float damage, bool armor = false)
        {
            Name = name;
            Degrees = degrees;
            Damage = damage;
            Armor = armor;
        }

        internal (float r, float g, float b) Tint
            => Armor ? White : Damage >= 35f ? Red : Yellow;
    }

    /// <summary>
    /// 在 <paramref name="frame"/> 上画一簇（中心在 (cx,cy)，画布 y 向上），时间进度 <paramref name="t"/>。
    /// 视图的规则：<c>Rise ≤ VisibleRise(t)</c> 才显示，整簇乘 BurstFade 并沿峰尖法线漂移。
    /// </summary>
    private static int DrawBurst(Frame frame, float cx, float cy, Shot shot, float t, float zoom = 1f)
    {
        var list = new List<HitDirectionParticle>(256);
        float angle = shot.Degrees * MathF.PI / 180f;
        HitDirectionMath.ShapeFor(shot.Damage, shot.Armor, out float halfWidth, out float amplitude);
        HitDirectionMath.BuildLattice(angle, halfWidth, amplitude, RadiusX, RadiusY,
            HitDirectionMath.LatticePitch, HitDirectionMath.BurstMaxCount, list);

        HitDirectionMath.Normal(angle, RadiusX, RadiusY, out float nx, out float ny);
        float drift = HitDirectionMath.Drift(t);
        float visible = HitDirectionMath.VisibleRise(t);
        float fade = HitDirectionMath.BurstFade(t);

        foreach (var p in list)
        {
            if (p.Rise > visible + 1e-4f) continue;
            float x = p.X + nx * drift;
            float y = p.Y + ny * drift;
            frame.Dot(cx + x * zoom, cy - y * zoom, HitDirectionMath.DotDisplaySize * p.SizeRatio, shot.Tint, p.Alpha * fade, zoom);
        }
        return list.Count;
    }

    internal static void Render(string dir)
    {
        Directory.CreateDirectory(dir);
        (byte, byte, byte) dark = (21, 24, 28);

        // 1) 四个方位 + 三种颜色（暗底），和 0.6e-hitdir.txt 的场景一一对应；t = 0.2（点亮完成、尚未收回）
        var shots = new[]
        {
            new Shot("front 20", 0f, 20f),
            new Shot("right 120", 90f, 120f),
            new Shot("back 30 armor", 180f, 30f, true),
            new Shot("left 25", 270f, 25f),
            new Shot("front 60", 0f, 60f),
            new Shot("45 deg 20", 45f, 20f),
        };
        const int cw = 960, ch = 560;
        var sheet = new Frame(cw * 3, ch * 2, dark);
        for (int i = 0; i < shots.Length; i++)
        {
            var f = new Frame(cw, ch, dark);
            int n = DrawBurst(f, cw / 2f, ch / 2f, shots[i], 0.2f);
            sheet.Blit(f, (i % 3) * cw, (i / 3) * ch);
            Console.WriteLine($"  {shots[i].Name,-14} particles={n}");
        }
        sheet.Save(Path.Combine(dir, "hitdir_directions.png"));

        // 2) 同一簇在五种背景上（重伤红 + 护甲白）
        var bgs = new (string name, (byte, byte, byte) rgb)[]
        {
            ("dark", dark), ("mid", (128, 128, 128)), ("white", (255, 255, 255)),
            ("snow", (214, 224, 234)), ("grass", (70, 96, 52)),
        };
        const int bw = 520, bh = 300;
        var bgSheet = new Frame(bw * bgs.Length, bh * 2, dark);
        for (int i = 0; i < bgs.Length; i++)
        {
            var a = new Frame(bw, bh, bgs[i].rgb);
            DrawBurst(a, bw / 2f, bh / 2f + 190f, new Shot("front 60", 0f, 60f), 0.2f);
            bgSheet.Blit(a, i * bw, 0);
            var b = new Frame(bw, bh, bgs[i].rgb);
            DrawBurst(b, bw / 2f, bh / 2f + 190f, new Shot("front 30 armor", 0f, 30f, true), 0.2f);
            bgSheet.Blit(b, i * bw, bh);
        }
        bgSheet.Save(Path.Combine(dir, "hitdir_backgrounds.png"));

        // 3) 时间线：点亮 → 停住 → 收回 + 淡出（持续时长 0.9 秒时的帧）
        float[] ts = { 0.02f, 0.05f, 0.09f, 0.16f, 0.4f, 0.6f, 0.75f, 0.9f };
        const int aw = 420, ah = 260;
        var anim = new Frame(aw * 4, ah * 2, dark);
        for (int i = 0; i < ts.Length; i++)
        {
            var f = new Frame(aw, ah, dark);
            DrawBurst(f, aw / 2f, ah / 2f + 190f, new Shot("front 60", 0f, 60f), ts[i]);
            anim.Blit(f, (i % 4) * aw, (i / 4) * ah);
        }
        anim.Save(Path.Combine(dir, "hitdir_timeline.png"));

        // 4) 放大 4 倍看点阵排布
        const float zoom = 4f;
        var zf = new Frame(1000, 520, dark);
        DrawBurst(zf, 500f, 520f + 150f * zoom - 40f, new Shot("front 60", 0f, 60f), 0.2f, zoom);
        zf.Save(Path.Combine(dir, "hitdir_zoom4x.png"));

        Console.WriteLine("rendered hit-direction previews to " + Path.GetFullPath(dir));
    }
}
