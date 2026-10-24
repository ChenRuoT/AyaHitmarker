using System;
using System.Collections.Generic;

namespace AyaHitmarker.Ui;

/// <summary>
/// 受击方向指示的一颗粒子（点阵里的一个点）。
///
/// 约定（和战地风格的「椭圆盘」一致）：
/// <list type="bullet">
///   <item>角度 <c>0</c> = 正前方，显示在椭圆**上端**；<c>+π/2</c> = 右侧；<c>±π</c> = 背后（下端）；</item>
///   <item>椭圆长轴在水平方向：<c>x = sin(angle) * radiusX</c>、<c>y = cos(angle) * radiusY</c>；</item>
///   <item>粒子最终位置 = 基准点 + 外法线 × <see cref="Height"/>。</item>
/// </list>
/// </summary>
internal readonly struct HitDirectionParticle
{
    /// <summary>粒子所在列在椭圆上的基准位置（相对屏幕中心，画布单位）。</summary>
    internal readonly float BaseX;
    internal readonly float BaseY;

    /// <summary>椭圆外法线方向（单位向量）。</summary>
    internal readonly float OutX;
    internal readonly float OutY;

    /// <summary>该粒子沿外法线的偏移（画布单位）：第几行 × 行距。</summary>
    internal readonly float Height;

    /// <summary>尺寸系数（1 = 基线；越靠外越小，最小 <c>1 - SizeShrink</c>）。</summary>
    internal readonly float SizeRatio;

    /// <summary>设计透明度（0~1）：横向衰减 × 纵向衰减。</summary>
    internal readonly float Alpha;

    /// <summary>高度占突起幅度的比例（0 = 基线，1 = 峰尖）；视图按它从内向外依次点亮。</summary>
    internal readonly float Rise;

    internal HitDirectionParticle(float baseX, float baseY, float outX, float outY, float height,
        float sizeRatio, float alpha, float rise)
    {
        BaseX = baseX;
        BaseY = baseY;
        OutX = outX;
        OutY = outY;
        Height = height;
        SizeRatio = sizeRatio;
        Alpha = alpha;
        Rise = rise;
    }

    internal float X => BaseX + OutX * Height;
    internal float Y => BaseY + OutY * Height;
}

/// <summary>
/// 受击方向点阵的可调样式（设置页「11. 受击方向」逐项对应）。<see cref="Default"/> = 视觉重做时定下的出厂外观；
/// 所有字段在使用处钳制，设置里填出离谱值也不会算出负数 / 死循环。
/// </summary>
internal struct HitDirectionStyle
{
    /// <summary>基线尾巴的半宽 = halfWidth × 这个比例（1 = 没有尾巴）。</summary>
    internal float TailRatio;

    /// <summary>纵向渐变：峰尖那一行的透明度 = 1 - RadialFade。</summary>
    internal float RadialFade;

    /// <summary>纵向尺寸：峰尖那一行的尺寸系数 = 1 - SizeShrink。</summary>
    internal float SizeShrink;

    /// <summary>突起半宽：低伤害 → 满强度（画布单位）。</summary>
    internal float WidthLow, WidthHigh;

    /// <summary>突起高度：低伤害 → 满强度（画布单位）。</summary>
    internal float HeightLow, HeightHigh;

    /// <summary>伤害达到它就按满强度画。</summary>
    internal float FullDamage;

    /// <summary>护甲吸收的钝伤：高度 × 这个比例。</summary>
    internal float ArmorHeightScale;

    /// <summary>时间线：点亮阶段占比 / 收回开始的时间点（都是 0..1 的总时长比例）/ 外漂距离（画布单位）。</summary>
    internal float RevealPortion, RetractStart, DriftDistance;

    internal static HitDirectionStyle Default => new HitDirectionStyle
    {
        TailRatio = HitDirectionMath.TailRatio,
        RadialFade = HitDirectionMath.RadialFade,
        SizeShrink = HitDirectionMath.SizeShrink,
        // 2026-10-25 用户在游戏里目检调定的值（滑条值四舍五入到两位小数）：
        // 比视觉重做初版（半宽 46~76 / 高度 42~90 / 满强度 70 / 护甲 0.8 / 尾巴 1.5）更小更窄，少挡视野
        WidthLow = 25.15f,
        WidthHigh = 53.77f,
        HeightLow = 22.41f,
        HeightHigh = 55.02f,
        FullDamage = 57.37f,
        ArmorHeightScale = 0.73f,
        RevealPortion = HitDirectionMath.RevealPortion,
        RetractStart = HitDirectionMath.RetractStart,
        DriftDistance = HitDirectionMath.DriftDistance,
    };
}

/// <summary>受击方向指示的**纯逻辑**几何 / 点阵排布（不引用 UnityEngine，可离线测试）。</summary>
internal static class HitDirectionMath
{
    /// <summary>粒子总数上限（视图对象池按这个开）。</summary>
    internal const int MaxParticles = 256;

    /// <summary>基线尾巴的半宽 = halfWidth × 这个比例（<see cref="HitDirectionStyle.TailRatio"/> 的出厂值）。</summary>
    internal const float TailRatio = 1.08f;

    /// <summary>纵向渐变的出厂值：峰尖那一行的透明度 = 1 - RadialFade。</summary>
    internal const float RadialFade = 0.43f;

    /// <summary>纵向尺寸的出厂值：峰尖那一行的尺寸系数 = 1 - SizeShrink。</summary>
    internal const float SizeShrink = 0.29f;

    /// <summary>蜂窝排列：列距 = 行距 × √3/2。</summary>
    private const float HexColumn = 0.8660254f;

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

    /// <summary>横向透明度：中心 1，到 tailWidth 处按二次曲线衰减到 0。</summary>
    internal static float LateralFade(float absS, float tailWidth)
    {
        if (tailWidth <= 1e-4f) return 0f;
        float n = absS / tailWidth;
        if (n >= 1f) return 0f;
        return 1f - n * n;
    }

    /// <summary>
    /// 在攻击者方位上生成一块**规则排列**的粒子点阵（确定性，没有随机）。
    ///
    /// 排布：在方位点的切线上等距排出若干「列」（奇数列外移半行 = 蜂窝排列，最紧密），
    /// 每列沿外法线等距叠「行」，行数由收尖的二次包络 <c>h(s) = amplitude · Profile(s / halfWidth)</c> 决定 ——
    /// 中心列最高、两侧逐列变矮，峰尖就是方向。包络外（<c>halfWidth ~ halfWidth·TailRatio</c>）每列只剩一个点，
    /// 连成一条锯齿基线，透明度向两端衰减到 0，作为横向渐变的尾巴。
    ///
    /// 渐变：透明度 = 横向衰减（离中心越远越淡）× 纵向衰减（越靠外越淡），尺寸也随高度略缩；
    /// 中心列不做横向衰减，保证峰尖那一列是整簇里最亮、最高的。
    /// </summary>
    /// <param name="angle">中心方位角（0 = 正前，+ = 右）。</param>
    /// <param name="halfWidth">突起的半宽（沿切线，画布单位）。</param>
    /// <param name="amplitude">中心列的最大突起高度（画布单位）。</param>
    /// <param name="radiusX">椭圆水平半径。</param>
    /// <param name="radiusY">椭圆垂直半径。</param>
    /// <param name="pitch">点间距（画布单位）。点数超过 <paramref name="maxCount"/> 时自动放大间距。</param>
    /// <param name="maxCount">粒子数上限（钳制到 0~<see cref="MaxParticles"/>）。</param>
    /// <returns>实际使用的点间距（可能比传入的大）。</returns>
    internal static float BuildLattice(float angle, float halfWidth, float amplitude, float radiusX, float radiusY,
        float pitch, int maxCount, List<HitDirectionParticle> into)
        => BuildLattice(angle, halfWidth, amplitude, radiusX, radiusY, pitch, maxCount, HitDirectionStyle.Default, into);

    /// <summary>同上，渐变 / 尾巴按 <paramref name="style"/>（<c>TailRatio</c> / <c>RadialFade</c> / <c>SizeShrink</c>）。</summary>
    internal static float BuildLattice(float angle, float halfWidth, float amplitude, float radiusX, float radiusY,
        float pitch, int maxCount, in HitDirectionStyle style, List<HitDirectionParticle> into)
    {
        if (into == null) return pitch;
        into.Clear();
        maxCount = Math.Clamp(maxCount, 0, MaxParticles);
        if (maxCount == 0 || halfWidth <= 1e-3f || amplitude <= 0f || radiusX <= 0f || radiusY <= 0f || pitch <= 1e-3f)
            return pitch;

        float tailRatio = ClampTail(style.TailRatio);
        float radialFade = Math.Clamp(style.RadialFade, 0f, 1f);
        float sizeShrink = Math.Clamp(style.SizeShrink, 0f, 0.95f);
        float tailWidth = halfWidth * tailRatio;

        // 先按数量上限定间距（只数个数，不生成）：点数 ∝ 1/pitch²，超了就放大 8% 再数
        for (int guard = 0; guard < 48 && CountLattice(halfWidth, amplitude, pitch, tailRatio) > maxCount; guard++)
            pitch *= 1.08f;

        float colPitch = pitch * HexColumn;
        int half = (int)MathF.Floor(tailWidth / colPitch);

        // 局部平直坐标系：原点 = 椭圆上的方位点，n = 该点外法线（峰尖方向），t = 切线（指向角度增大的一侧）。
        // 点阵不跟着椭圆弯曲 —— 椭圆两端曲率半径只有 ry²/rx（默认 70），贴着它排的列会呈扇形散开、外圈稀疏；
        // 椭圆本身不画，所以用平直点阵既规整又不会被看出「没贴着椭圆」。
        float cx = MathF.Sin(angle) * radiusX;
        float cy = MathF.Cos(angle) * radiusY;
        Normal(angle, radiusX, radiusY, out float nx, out float ny);
        float tx = ny, ty = -nx;

        // 中心列先出，再向两侧成对展开（-1/+1、-2/+2 …）：万一被上限截断也是对称地从外侧丢
        AddColumn(into, 0, 0f, cx, cy, nx, ny, halfWidth, tailWidth, amplitude, pitch, maxCount, radialFade, sizeShrink);
        for (int k = 1; k <= half; k++)
        {
            float s = k * colPitch;
            AddColumn(into, -k, -s, cx - tx * s, cy - ty * s, nx, ny, halfWidth, tailWidth, amplitude, pitch, maxCount, radialFade, sizeShrink);
            AddColumn(into, k, s, cx + tx * s, cy + ty * s, nx, ny, halfWidth, tailWidth, amplitude, pitch, maxCount, radialFade, sizeShrink);
        }
        return pitch;
    }

    private static void AddColumn(List<HitDirectionParticle> into, int k, float s, float baseX, float baseY, float nx, float ny,
        float halfWidth, float tailWidth, float amplitude, float pitch, int maxCount, float radialFade, float sizeShrink)
    {
        float lateral = k == 0 ? 1f : LateralFade(MathF.Abs(s), tailWidth);
        if (lateral <= 0.02f) return;

        float h = ColumnHeight(k, s, halfWidth, amplitude, pitch);
        float offset = (k & 1) != 0 ? pitch * 0.5f : 0f;
        int rows = RowsFor(h, offset, pitch);
        for (int j = 0; j < rows; j++)
        {
            if (into.Count >= maxCount) return;
            float r = offset + j * pitch;
            float rise = Math.Clamp(r / amplitude, 0f, 1f);
            float alpha = lateral * (1f - radialFade * rise);
            float size = 1f - sizeShrink * rise;
            into.Add(new HitDirectionParticle(baseX, baseY, nx, ny, r, size, alpha, rise));
        }
    }

    /// <summary>
    /// 一列的包络高度。除中心列外都压到「中心列最高点 - 半行」以下：
    /// 奇数列外移半行后可能比中心列还高一点，峰尖就会变成两个并排的点 —— 压住它，峰尖永远是中心列那一个点。
    /// </summary>
    private static float ColumnHeight(int k, float s, float halfWidth, float amplitude, float pitch)
    {
        float h = amplitude * Profile(s / halfWidth);
        if (k == 0) return h;
        float centerTop = MathF.Floor(amplitude / pitch + 1e-3f) * pitch;
        return MathF.Min(h, centerTop - pitch * 0.5f - 1e-3f);
    }

    /// <summary>
    /// 突起轮廓 <c>(1 - n²)(1 - |n|)</c>（n = s / halfWidth，|n| ≥ 1 为 0）：二次包络再乘一个线性收尖 ——
    /// 腰身饱满、顶端收成一个尖，峰尖比纯 <c>1 - n²</c> 的圆顶更能精确地指向攻击者。
    /// </summary>
    internal static float Profile(float n)
    {
        float a = MathF.Abs(n);
        if (a >= 1f) return 0f;
        return (1f - a * a) * (1f - a);
    }

    /// <summary>尾巴比例钳到 1..4：1 = 没有尾巴（基线只到突起边缘）。</summary>
    private static float ClampTail(float tailRatio) => Math.Clamp(tailRatio, 1f, 4f);

    /// <summary>一列的点数：从 offset 起每隔 pitch 一个，直到包络高度；至少一个（基线）。</summary>
    private static int RowsFor(float h, float offset, float pitch)
        => (int)MathF.Floor((MathF.Max(h, offset) - offset) / pitch + 1e-3f) + 1;

    /// <summary>只数点阵个数（定间距用），规则与 <see cref="BuildLattice"/> 一致（与椭圆无关）。</summary>
    internal static int CountLattice(float halfWidth, float amplitude, float pitch)
        => CountLattice(halfWidth, amplitude, pitch, TailRatio);

    /// <summary>同上，尾巴长度按 <paramref name="tailRatio"/>。</summary>
    internal static int CountLattice(float halfWidth, float amplitude, float pitch, float tailRatio)
    {
        if (halfWidth <= 1e-3f || amplitude <= 0f || pitch <= 1e-3f) return 0;
        float tailWidth = halfWidth * ClampTail(tailRatio);
        float colPitch = pitch * HexColumn;
        int half = (int)MathF.Floor(tailWidth / colPitch);
        int total = 0;
        for (int k = -half; k <= half; k++)
        {
            float s = k * colPitch;
            if (k != 0 && LateralFade(MathF.Abs(s), tailWidth) <= 0.02f) continue;
            float offset = (k & 1) != 0 ? pitch * 0.5f : 0f;
            total += RowsFor(ColumnHeight(k, s, halfWidth, amplitude, pitch), offset, pitch);
        }
        return total;
    }

    // ------------------------------------------------------------------ 伤害 → 形状

    /// <summary>点阵的行距 / 列距基准（画布单位）。</summary>
    internal const float LatticePitch = 6.5f;

    /// <summary>单个点的显示边长（画布单位，含暗描边；白芯直径约为它的 0.62 倍）。</summary>
    internal const float DotDisplaySize = 7f;

    /// <summary>每簇点数上限（满伤害的形状约 200 个点）。</summary>
    internal const int BurstMaxCount = 220;

    /// <summary>
    /// 伤害 → 突起形状（出厂样式 <see cref="HitDirectionStyle.Default"/>）：伤害 0~57 线性对应
    /// 半宽 25~54、高度 22~55（护甲吸收的钝伤再乘 0.73）—— 窄而尖，峰尖指向攻击者。
    /// </summary>
    internal static void ShapeFor(float damage, bool armorOnly, out float halfWidth, out float amplitude)
        => ShapeFor(damage, armorOnly, HitDirectionStyle.Default, out halfWidth, out amplitude);

    /// <summary>
    /// 同上，按 <paramref name="style"/>：伤害在 <c>0 .. FullDamage</c> 之间线性插值
    /// <c>WidthLow → WidthHigh</c> / <c>HeightLow → HeightHigh</c>；护甲吸收再乘 <c>ArmorHeightScale</c>。
    /// </summary>
    internal static void ShapeFor(float damage, bool armorOnly, in HitDirectionStyle style, out float halfWidth, out float amplitude)
    {
        float norm = Math.Clamp(damage / MathF.Max(1f, style.FullDamage), 0f, 1f);
        halfWidth = MathF.Max(1f, style.WidthLow + (style.WidthHigh - style.WidthLow) * norm);
        amplitude = MathF.Max(1f, style.HeightLow + (style.HeightHigh - style.HeightLow) * norm);
        if (armorOnly) amplitude = MathF.Max(1f, amplitude * Math.Clamp(style.ArmorHeightScale, 0.05f, 3f));
    }

    // ------------------------------------------------------------------ 时间线（t = 已过时间 / 持续时长，0..1）

    /// <summary>点亮阶段占总时长的比例：这段时间里点阵从基线一行行长到峰尖。</summary>
    internal const float RevealPortion = 0.16f;

    /// <summary>收回阶段从这里开始：峰尖一行行往基线缩回，同时整簇淡出。</summary>
    internal const float RetractStart = 0.45f;

    /// <summary>整簇向外漂移的最大距离（画布单位）。</summary>
    internal const float DriftDistance = 8f;

    /// <summary>
    /// 本帧可见的最大 <see cref="HitDirectionParticle.Rise"/>：<c>Rise ≤ 它</c> 的点才显示。
    /// 先 OutCubic 地从 0 长到 1（基线那一行 Rise = 0，出生第一帧就亮），停住，再 InQuad 地缩回 0。
    /// </summary>
    internal static float VisibleRise(float t) => VisibleRise(t, RevealPortion, RetractStart);

    /// <summary>同上，点亮占比 / 收回起点可调（钳到 0.001..1 / 0..0.99）。</summary>
    internal static float VisibleRise(float t, float revealPortion, float retractStart)
    {
        if (t >= 1f) return -1f;
        if (t < 0f) t = 0f;
        revealPortion = Math.Clamp(revealPortion, 0.001f, 1f);
        retractStart = Math.Clamp(retractStart, 0f, 0.99f);
        float grow = UiAnim.OutCubic(Math.Clamp(t / revealPortion, 0f, 1f));
        if (t <= retractStart) return grow;
        float u = (t - retractStart) / (1f - retractStart);
        return MathF.Min(grow, 1f - u * u);
    }

    /// <summary>整簇的透明度（CanvasGroup）：收回开始前全亮，之后二次曲线淡到 0。</summary>
    internal static float BurstFade(float t) => BurstFade(t, RetractStart);

    /// <summary>同上，收回起点可调（钳到 0..0.99）。</summary>
    internal static float BurstFade(float t, float retractStart)
    {
        if (t >= 1f) return 0f;
        retractStart = Math.Clamp(retractStart, 0f, 0.99f);
        if (t <= retractStart) return 1f;
        float u = 1f - (t - retractStart) / (1f - retractStart);
        return u * u;
    }

    /// <summary>整簇沿峰尖法线向外漂移的距离（OutQuad，0 → <see cref="DriftDistance"/>）。</summary>
    internal static float Drift(float t) => Drift(t, DriftDistance);

    /// <summary>同上，漂移距离可调（负数按 0）。</summary>
    internal static float Drift(float t, float distance)
    {
        t = Math.Clamp(t, 0f, 1f);
        return MathF.Max(0f, distance) * (1f - (1f - t) * (1f - t));
    }

    /// <summary>椭圆上方位角 <paramref name="angle"/> 处的外法线（峰尖方向，单位向量）。</summary>
    internal static void Normal(float angle, float radiusX, float radiusY, out float nx, out float ny)
    {
        nx = MathF.Sin(angle) / MathF.Max(1e-4f, radiusX);
        ny = MathF.Cos(angle) / MathF.Max(1e-4f, radiusY);
        float len = MathF.Sqrt(nx * nx + ny * ny);
        if (len < 1e-6f) { nx = 0f; ny = 1f; return; }
        nx /= len;
        ny /= len;
    }
}
