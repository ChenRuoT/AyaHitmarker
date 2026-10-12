using System;

namespace AyaHitmarker.Assets;

/// <summary>
/// RGBA8 位图，**直通 alpha**（非预乘），第 0 行在**最下面**（Unity 贴图的约定）。
/// 纯数据，不引用 UnityEngine —— 由 <c>IconBank</c> 负责把它变成 Texture2D / Sprite。
/// </summary>
internal sealed class IconBitmap
{
    internal readonly int Width;
    internal readonly int Height;
    internal readonly byte[] Rgba;

    internal IconBitmap(int width, int height, byte[] rgba)
    {
        Width = width;
        Height = height;
        Rgba = rgba;
    }
}

/// <summary>
/// 程序化图标：骷髅 / 多杀徽记 / 命中标记 / 标签框。**纯数学，不引用 UnityEngine**，
/// 所以可以在 <c>tests/AyaHitmarker.LogicTests</c> 里离线渲染成 PNG 直接看（<c>dotnet run -- --render-icons 目录</c>）。
///
/// 为什么不用现成的 PNG：旧项目留下的骷髅只有 33×38 像素，放大到 40~48px 明显发糊，
/// 命中图标只有 ~3px 粗的白线、没有任何深色描边，白色背景（雪地 / 天空）上会整个消失。
/// 这里全部用<b>有向距离场（SDF）</b>画：任何尺寸都有干净的抗锯齿边缘，描边宽度是精确的像素数。
///
/// 配色约定：位图里只有「白 / 黑」两种颜色（黑 = 描边与五官），运行时用 <c>Image.color</c> 相乘着色 ——
/// 白的部分随色变，黑的部分永远是黑，所以同一张图既能当白骷髅也能当红色爆头骷髅。
/// </summary>
internal static class IconShapes
{
    // ------------------------------------------------------------------ 合成器

    /// <summary>预乘 alpha 的浮点画布，按「over」算子一层层叠。</summary>
    private sealed class Canvas
    {
        internal readonly int W;
        internal readonly int H;
        private readonly float[] _p;

        internal Canvas(int w, int h)
        {
            W = w;
            H = h;
            _p = new float[w * h * 4];
        }

        /// <summary>把覆盖率 <paramref name="cov"/> 以纯色叠到画布上。</summary>
        internal void Over(float[] cov, float r, float g, float b, float opacity)
        {
            int n = W * H;
            for (int i = 0; i < n; i++)
            {
                float sa = cov[i] * opacity;
                if (sa <= 0f) continue;
                int k = i * 4;
                float inv = 1f - sa;
                _p[k] = r * sa + _p[k] * inv;
                _p[k + 1] = g * sa + _p[k + 1] * inv;
                _p[k + 2] = b * sa + _p[k + 2] * inv;
                _p[k + 3] = sa + _p[k + 3] * inv;
            }
        }

        /// <summary>
        /// 直接把「像素空间的有向距离」合成到画布上（1 像素线性抗锯齿），不分配中间数组，
        /// 并且只算 <c>[x0,x1) × [y0,y1)</c> 这块包围盒 —— 多杀徽记里每个骷髅 / 每道速度线只占画布很小一块，
        /// 全画布逐个栅格化会慢一个数量级（首次出现 5 连杀徽记要同步烘焙 ~80ms，会卡好几帧）。
        /// </summary>
        internal void DrawSdf(Func<float, float, float> sdfPixels, float r, float g, float b, float opacity,
            int x0 = 0, int y0 = 0, int x1 = int.MaxValue, int y1 = int.MaxValue)
        {
            x0 = Math.Max(0, x0);
            y0 = Math.Max(0, y0);
            x1 = Math.Min(W, x1);
            y1 = Math.Min(H, y1);

            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    float cov = 0.5f - sdfPixels(x + 0.5f, y + 0.5f);
                    if (cov <= 0f) continue;
                    float sa = (cov > 1f ? 1f : cov) * opacity;
                    int k = (y * W + x) * 4;
                    float inv = 1f - sa;
                    _p[k] = r * sa + _p[k] * inv;
                    _p[k + 1] = g * sa + _p[k + 1] * inv;
                    _p[k + 2] = b * sa + _p[k + 2] * inv;
                    _p[k + 3] = sa + _p[k + 3] * inv;
                }
            }
        }

        /// <param name="bleedWhite">
        /// 全透明像素的 RGB 填白而不是填黑。mipmap 缩小时 RGB 会按直通 alpha 平均，
        /// 白色辉光 / 白色线条外面的「黑色透明像素」会把边缘拉脏；填白就不会。
        /// （骷髅外面本来就有黑色描边，保持默认的黑。）
        /// </param>
        internal IconBitmap ToBitmap(bool bleedWhite = false)
        {
            var bytes = new byte[W * H * 4];
            for (int i = 0; i < W * H; i++)
            {
                int k = i * 4;
                float a = _p[k + 3];
                if (a <= 0.0001f)
                {
                    if (bleedWhite)
                    {
                        bytes[k] = 255;
                        bytes[k + 1] = 255;
                        bytes[k + 2] = 255;
                    }
                    continue;
                }
                bytes[k] = To8(_p[k] / a);
                bytes[k + 1] = To8(_p[k + 1] / a);
                bytes[k + 2] = To8(_p[k + 2] / a);
                bytes[k + 3] = To8(a);
            }
            return new IconBitmap(W, H, bytes);
        }

        private static byte To8(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
    }

    /// <summary>把「像素空间的有向距离」栅格化成 0..1 的覆盖率（1 像素宽的线性抗锯齿）。</summary>
    private static float[] Raster(int w, int h, Func<float, float, float> sdfPixels)
    {
        var cov = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float d = sdfPixels(x + 0.5f, y + 0.5f);
                cov[y * w + x] = Math.Clamp(0.5f - d, 0f, 1f);
            }
        }
        return cov;
    }

    // ------------------------------------------------------------------ SDF 图元（局部坐标，Y 向上）

    private static float SdEllipse(float x, float y, float cx, float cy, float rx, float ry)
    {
        // IQ 的近似：边界附近足够准，用来做抗锯齿
        float px = x - cx;
        float py = y - cy;
        float k0 = MathF.Sqrt((px / rx) * (px / rx) + (py / ry) * (py / ry));
        float k1 = MathF.Sqrt((px / (rx * rx)) * (px / (rx * rx)) + (py / (ry * ry)) * (py / (ry * ry)));
        return k1 < 1e-6f ? -Math.Min(rx, ry) : k0 * (k0 - 1f) / k1;
    }

    private static float SdRoundBox(float x, float y, float cx, float cy, float hx, float hy, float r)
    {
        float qx = MathF.Abs(x - cx) - (hx - r);
        float qy = MathF.Abs(y - cy) - (hy - r);
        float ox = Math.Max(qx, 0f);
        float oy = Math.Max(qy, 0f);
        return MathF.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0f) - r;
    }

    private static float SdSegment(float x, float y, float ax, float ay, float bx, float by)
    {
        float pax = x - ax, pay = y - ay;
        float bax = bx - ax, bay = by - ay;
        float h = Math.Clamp((pax * bax + pay * bay) / (bax * bax + bay * bay), 0f, 1f);
        float dx = pax - bax * h, dy = pay - bay * h;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static float SdTriangle(float x, float y, float ax, float ay, float bx, float by, float cx, float cy)
    {
        float d = Math.Min(
            Math.Min(SdSegment(x, y, ax, ay, bx, by), SdSegment(x, y, bx, by, cx, cy)),
            SdSegment(x, y, cx, cy, ax, ay));

        // 内外判定：三条边的叉积同号即在内部
        float s1 = (bx - ax) * (y - ay) - (by - ay) * (x - ax);
        float s2 = (cx - bx) * (y - by) - (cy - by) * (x - bx);
        float s3 = (ax - cx) * (y - cy) - (ay - cy) * (x - cx);
        bool inside = (s1 >= 0f && s2 >= 0f && s3 >= 0f) || (s1 <= 0f && s2 <= 0f && s3 <= 0f);
        return inside ? -d : d;
    }

    private static float SMin(float a, float b, float k)
    {
        float h = Math.Max(k - MathF.Abs(a - b), 0f) / k;
        return Math.Min(a, b) - h * h * k * 0.25f;
    }

    // ------------------------------------------------------------------ 骷髅

    /// <summary>骷髅宽高比（宽 / 高）。</summary>
    internal const float SkullAspect = 0.92f;

    /// <summary>骷髅位图四周为描边留的像素。烘焙器据此算「位图高 / 本体高」，把图标按本体高度摆放。</summary>
    internal static int SkullPad(float outlinePx) => (int)MathF.Ceiling(Math.Max(0f, outlinePx)) + 2;

    /// <summary>
    /// 骷髅轮廓的有向距离（局部单位：高 = 1，宽 = <see cref="SkullAspect"/>，原点在下巴正中）。
    /// 爆头版在颅顶多一道 V 形裂口 —— 战地 6 的爆头图标就是这个样子。
    /// </summary>
    private static float SkullBody(float lx, float ly, bool headshot)
    {
        // 颅骨：偏宽的椭圆；下颌：圆角方块；用平滑并集把颧骨过渡出来
        float cranium = SdEllipse(lx, ly, 0f, 0.60f, 0.455f, 0.40f);
        float jaw = SdRoundBox(lx, ly, 0f, 0.255f, 0.245f, 0.245f, 0.085f);
        float d = SMin(cranium, jaw, 0.12f);

        if (headshot)
        {
            // 颅顶 V 形裂口：顶点朝下的三角形，从颅顶外面切进去
            float notch = SdTriangle(lx, ly, 0f, 0.80f, -0.085f, 1.06f, 0.085f, 1.06f);
            d = Math.Max(d, -notch);
        }
        return d;
    }

    /// <summary>五官（眼窝 / 鼻孔 / 牙缝）的有向距离，负数 = 在五官里面。</summary>
    private static float SkullDetails(float lx, float ly)
    {
        float eyeL = SdEllipse(lx, ly, -0.168f, 0.525f, 0.108f, 0.122f);
        float eyeR = SdEllipse(lx, ly, 0.168f, 0.525f, 0.108f, 0.122f);
        float nose = SdTriangle(lx, ly, 0f, 0.405f, -0.052f, 0.318f, 0.052f, 0.318f) - 0.012f;

        float d = Math.Min(Math.Min(eyeL, eyeR), nose);

        // 牙缝：三道细竖槽
        for (int i = -1; i <= 1; i++)
        {
            float slit = SdRoundBox(lx, ly, i * 0.088f, 0.095f, 0.014f, 0.062f, 0.010f);
            d = Math.Min(d, slit);
        }
        return d;
    }

    /// <summary>
    /// 一个骷髅。位图尺寸 = 骷髅本体 + 描边留白。颜色：本体白、五官黑、外描边黑（半透明）。
    /// </summary>
    /// <param name="heightPx">骷髅本体的像素高度。</param>
    /// <param name="outlinePx">深色描边的像素宽度（0 = 无描边）。</param>
    internal static IconBitmap Skull(int heightPx, bool headshot, float outlinePx)
    {
        heightPx = Math.Max(16, heightPx);
        outlinePx = Math.Max(0f, outlinePx);
        int pad = SkullPad(outlinePx);
        int w = (int)MathF.Round(heightPx * SkullAspect) + pad * 2;
        int h = heightPx + pad * 2;
        float s = heightPx;

        void ToLocal(float x, float y, out float lx, out float ly)
        {
            lx = (x - w * 0.5f) / s;
            ly = (y - pad) / s;
        }

        var canvas = new Canvas(w, h);

        if (outlinePx > 0.01f)
        {
            float[] outline = Raster(w, h, (x, y) =>
            {
                ToLocal(x, y, out float lx, out float ly);
                return SkullBody(lx, ly, headshot) * s - outlinePx;
            });
            canvas.Over(outline, 0f, 0f, 0f, 0.62f);
        }

        float[] body = Raster(w, h, (x, y) =>
        {
            ToLocal(x, y, out float lx, out float ly);
            return SkullBody(lx, ly, headshot) * s;
        });
        canvas.Over(body, 1f, 1f, 1f, 1f);

        float[] details = Raster(w, h, (x, y) =>
        {
            ToLocal(x, y, out float lx, out float ly);
            return SkullDetails(lx, ly) * s;
        });
        // 五官只画在本体里面（牙缝会贴着下巴边缘，不能把描边外面也涂黑）
        for (int i = 0; i < details.Length; i++) details[i] = Math.Min(details[i], body[i]);
        canvas.Over(details, 0f, 0f, 0f, 0.9f);

        return canvas.ToBitmap();
    }

    // ------------------------------------------------------------------ 多杀徽记

    /// <summary>徽记里的一个骷髅：中心 / 缩放 / 是否在前景（前景的会描一圈深色边把它和身后的分开）。</summary>
    private readonly struct EmblemSkull
    {
        internal readonly float X;
        internal readonly float Y;
        internal readonly float Scale;

        internal EmblemSkull(float x, float y, float scale)
        {
            X = x;
            Y = y;
            Scale = scale;
        }
    }

    /// <summary>
    /// 按击杀数排骷髅，**从后往前**返回（最后一个在最前面）。
    /// 2 个并排；3 个是「一前两后」；4 个再在后面冒出一个头；5 个以上两侧再各加一个。
    /// 坐标：局部单位，骷髅高 = 1，原点在徽记水平正中、主骷髅的中心高度。
    /// </summary>
    private static EmblemSkull[] EmblemLayout(int count)
    {
        count = Math.Clamp(count, 1, 5);
        switch (count)
        {
            case 1:
                return new[] { new EmblemSkull(0f, 0f, 1f) };
            case 2:
                return new[] { new EmblemSkull(-0.47f, 0f, 1f), new EmblemSkull(0.47f, 0f, 1f) };
            case 3:
                return new[]
                {
                    new EmblemSkull(-0.62f, 0.03f, 0.88f),
                    new EmblemSkull(0.62f, 0.03f, 0.88f),
                    new EmblemSkull(0f, -0.04f, 1f),
                };
            case 4:
                return new[]
                {
                    new EmblemSkull(0f, 0.50f, 0.80f),
                    new EmblemSkull(-0.62f, 0.05f, 0.88f),
                    new EmblemSkull(0.62f, 0.05f, 0.88f),
                    new EmblemSkull(0f, -0.06f, 1f),
                };
            default:
                return new[]
                {
                    new EmblemSkull(-1.12f, 0.02f, 0.78f),
                    new EmblemSkull(1.12f, 0.02f, 0.78f),
                    new EmblemSkull(0f, 0.50f, 0.80f),
                    new EmblemSkull(-0.62f, 0.05f, 0.88f),
                    new EmblemSkull(0.62f, 0.05f, 0.88f),
                    new EmblemSkull(0f, -0.06f, 1f),
                };
        }
    }

    /// <summary>徽记位图高度 = 主骷髅高度 × 这个系数（为 4 个以上时冒出头的那个骷髅和它的分隔描边留位置）。</summary>
    internal const float EmblemHeightFactor = 1.55f;

    /// <summary>主骷髅中心在位图里的高度 = 主骷髅高度 × 这个系数（从位图底部算）。</summary>
    internal const float EmblemMainCenter = 0.60f;

    /// <summary>
    /// 位图中心比主骷髅中心高多少个骷髅高（上面留的头部空间比下面多）。
    /// 把位图竖直居中在横幅上时，要把位图再抬这么多，主骷髅才和横幅居中。
    /// </summary>
    internal const float EmblemCenterShift = EmblemHeightFactor * 0.5f - EmblemMainCenter;

    /// <summary>两侧速度线的相对长度（占骷髅高度的比例），上下短、中间长，合起来像一只眼睛。</summary>
    private static readonly float[] EmblemBarLengths = { 0.38f, 0.58f, 0.70f, 0.58f, 0.38f };

    /// <summary>
    /// 多杀徽记：中间一簇骷髅，两侧各 5 道长短不一的横线（战地 6 的「双杀 / 三杀 / 四杀」横幅右侧那个图标）。
    /// 返回的位图高度 = <paramref name="skullHeightPx"/> 的 <see cref="EmblemHeightFactor"/> 倍（给 4 个以上时冒出头的骷髅留位置）。
    /// 全白 + 黑色分隔描边，运行时用 <c>Image.color</c> 着色。
    /// </summary>
    internal static IconBitmap Emblem(int killCount, int skullHeightPx)
    {
        skullHeightPx = Math.Max(16, skullHeightPx);
        var skulls = EmblemLayout(killCount);

        float s = skullHeightPx;
        float barThick = 0.095f;
        float barPitch = 0.25f;
        float barGap = 0.07f;

        // 先量出骷髅簇的水平范围，决定画布宽度
        float clusterHalf = 0f;
        for (int i = 0; i < skulls.Length; i++)
        {
            clusterHalf = Math.Max(clusterHalf, MathF.Abs(skulls[i].X) + SkullAspect * 0.5f * skulls[i].Scale);
        }

        float barMax = EmblemBarLengths[2];
        float halfWidth = clusterHalf + barGap + barMax + 0.04f;

        int pad = 3;
        int w = (int)MathF.Ceiling(halfWidth * 2f * s) + pad * 2;
        int h = (int)MathF.Ceiling(s * EmblemHeightFactor) + pad * 2;
        float cx = w * 0.5f;
        float cy = pad + s * EmblemMainCenter;   // 主骷髅中心的像素高度（下面留 0.6H，上面留 0.95H 给冒头的骷髅）

        var canvas = new Canvas(w, h);

        // ---- 两侧速度线 ----
        for (int side = -1; side <= 1; side += 2)
        {
            for (int i = 0; i < EmblemBarLengths.Length; i++)
            {
                float len = EmblemBarLengths[i];
                float by = (2 - i) * barPitch;                  // 局部 Y：-0.5 .. +0.5
                float inner = clusterHalf + barGap;             // 靠近骷髅那一端
                float outer = inner + len;
                float x0 = Math.Min(inner, outer) * side;
                float x1 = Math.Max(inner, outer) * side;
                float midX = (x0 + x1) * 0.5f;
                float halfLen = MathF.Abs(x1 - x0) * 0.5f;
                int bx0 = (int)MathF.Floor(cx + (midX - halfLen) * s) - 2;
                int bx1 = (int)MathF.Ceiling(cx + (midX + halfLen) * s) + 2;
                int by0 = (int)MathF.Floor(cy + (by - barThick * 0.5f) * s) - 2;
                int by1 = (int)MathF.Ceiling(cy + (by + barThick * 0.5f) * s) + 2;
                canvas.DrawSdf((x, y) =>
                {
                    float lx = (x - cx) / s;
                    float ly = (y - cy) / s;
                    return SdRoundBox(lx, ly, midX, by, halfLen, barThick * 0.5f, barThick * 0.5f) * s;
                }, 1f, 1f, 1f, 1f, bx0, by0, bx1, by1);
            }
        }

        // ---- 骷髅簇（从后往前画）----
        for (int n = 0; n < skulls.Length; n++)
        {
            var sk = skulls[n];
            float scale = sk.Scale;

            // 分隔描边：把身后的骷髅压住一圈深色
            float ring = 0.02f * s + 1.1f;

            // 这个骷髅占画布的哪一块（含分隔描边和 2px 抗锯齿余量）
            float pcx = cx + sk.X * s;
            float pcy = cy + sk.Y * s;
            float halfW = SkullAspect * 0.5f * scale * s + ring + 2f;
            float halfH = 0.5f * scale * s + ring + 2f;
            int sx0 = (int)MathF.Floor(pcx - halfW);
            int sx1 = (int)MathF.Ceiling(pcx + halfW);
            int sy0 = (int)MathF.Floor(pcy - halfH);
            int sy1 = (int)MathF.Ceiling(pcy + halfH);

            // 局部（徽记坐标）→ 骷髅自己的坐标：骷髅原点在下巴正中，高 1 → 中心在 0.5
            float BodySdf(float x, float y)
            {
                float ex = (x - cx) / s - sk.X;
                float ey = (y - cy) / s - sk.Y;
                return SkullBody(ex / scale, ey / scale + 0.5f, false) * s * scale;
            }

            canvas.DrawSdf((x, y) => BodySdf(x, y) - ring, 0f, 0f, 0f, 0.85f, sx0, sy0, sx1, sy1);
            canvas.DrawSdf(BodySdf, 1f, 1f, 1f, 1f, sx0, sy0, sx1, sy1);

            // 五官只画在本体里面（取两个距离场的交集 = max）
            canvas.DrawSdf((x, y) =>
            {
                float ex = (x - cx) / s - sk.X;
                float ey = (y - cy) / s - sk.Y;
                float details = SkullDetails(ex / scale, ey / scale + 0.5f) * s * scale;
                return Math.Max(details, BodySdf(x, y));
            }, 0f, 0f, 0f, 0.9f, sx0, sy0, sx1, sy1);
        }

        return canvas.ToBitmap(bleedWhite: true);
    }

    // ------------------------------------------------------------------ 命中标记

    /// <summary>
    /// 命中标记：四条斜向虚线组成的 ✕，中间留空。几何量自旧项目的 <c>hit.png</c>（128px 贴图里每条线
    /// 从中心 32px 画到 63px、核心粗约 3px），这里用 SDF 重建，并加上<b>深色描边</b>和柔和的外辉光 ——
    /// 白底上不会再消失。
    /// </summary>
    /// <param name="sizePx">位图边长。</param>
    /// <param name="outlineLocal">描边宽度，单位是「半边长 = 1」的局部单位（= 描边像素 / (显示尺寸 / 2)）。</param>
    /// <param name="bold">
    /// 粗线版（击杀标记用）：核心更粗。着色后红色要压得过黑色描边才看得出是红的，
    /// 普通命中那种 ~2px 的细核心配 1.5px 描边，染成红色会发黑。
    /// </param>
    internal static IconBitmap HitMarker(int sizePx, float outlineLocal, bool bold = false)
    {
        sizePx = Math.Max(32, sizePx);
        outlineLocal = Math.Clamp(outlineLocal, 0f, 0.2f);

        // 几何（半边长 = 1）：为描边与辉光留出余量，所以比旧图（0.50 ~ 0.98）略收一点
        const float rIn = 0.46f;
        float rOut = bold ? 0.90f : 0.88f;
        float thickness = bold ? 0.104f : 0.074f;       // 核心粗（旧图约 0.047）
        float half = sizePx * 0.5f;

        int w = sizePx, h = sizePx;
        float inv = 1f / half;

        float DashSdf(float x, float y)
        {
            float lx = (x - half) * inv;
            float ly = (y - half) * inv;
            // 四象限对称：折到第一象限，只算一条线
            float ax = MathF.Abs(lx);
            float ay = MathF.Abs(ly);
            const float k = 0.70710678f;
            return SdSegment(ax, ay, rIn * k, rIn * k, rOut * k, rOut * k) - thickness * 0.5f;
        }

        var canvas = new Canvas(w, h);

        // 辉光：核心外面一圈衰减的白光
        var glow = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float d = DashSdf(x + 0.5f, y + 0.5f) - outlineLocal;
                if (d <= 0f) continue;
                glow[y * w + x] = 0.38f * MathF.Exp(-d / 0.045f);
            }
        }
        canvas.Over(glow, 1f, 1f, 1f, 1f);

        if (outlineLocal > 0.0005f)
        {
            float[] outline = Raster(w, h, (x, y) => (DashSdf(x, y) - outlineLocal) * half);
            canvas.Over(outline, 0f, 0f, 0f, 0.78f);
        }

        float[] core = Raster(w, h, (x, y) => DashSdf(x, y) * half);
        canvas.Over(core, 1f, 1f, 1f, 1f);

        return canvas.ToBitmap(bleedWhite: true);
    }

    // ------------------------------------------------------------------ 标签框（9 宫格）

    /// <summary>
    /// 标签框的九宫格贴图：<paramref name="border"/> 像素宽的白色描边 + 半透明黑色填充，直角。
    /// 运行时 <c>Image.type = Sliced</c>，边框宽度不随框的大小缩放。
    /// 位图边长 = 2 × (border + 2)，四个角各留 border 像素不被拉伸。
    /// </summary>
    internal static IconBitmap TagBox(int border)
    {
        border = Math.Clamp(border, 1, 6);
        int size = (border + 2) * 2;
        var rgba = new byte[size * size * 4];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool edge = x < border || y < border || x >= size - border || y >= size - border;
                int k = (y * size + x) * 4;
                if (edge)
                {
                    rgba[k] = 255;
                    rgba[k + 1] = 255;
                    rgba[k + 2] = 255;
                    rgba[k + 3] = 235;
                }
                else
                {
                    rgba[k] = 0;
                    rgba[k + 1] = 0;
                    rgba[k + 2] = 0;
                    rgba[k + 3] = 140;
                }
            }
        }
        return new IconBitmap(size, size, rgba);
    }
}
