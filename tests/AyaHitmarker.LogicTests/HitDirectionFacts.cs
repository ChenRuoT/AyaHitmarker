using System;
using System.Collections.Generic;
using AyaHitmarker.Ui;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// 0.6e 受击方向指示的纯逻辑：方位角换算、二次函数包络、点阵排布（规则蜂窝、峰尖精确指向、纵横渐变）、
/// 伤害映射与时间线。好不好看用 <c>--render-hitdir</c> 渲染成 PNG 肉眼看。
/// </summary>
internal static class HitDirectionFacts
{
    private const float Eps = 1e-4f;

    internal static void Run()
    {
        Assert.Suite("受击方向：方位角与包络");

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

        Lattice();
        ShapeAndTimeline();
    }

    private static float ThetaOf(in HitDirectionParticle p, float rx, float ry) => MathF.Atan2(p.BaseX / rx, p.BaseY / ry);

    private static HitDirectionParticle Tallest(List<HitDirectionParticle> list)
    {
        HitDirectionParticle tip = list[0];
        foreach (var p in list) if (p.Height > tip.Height) tip = p;
        return tip;
    }

    /// <summary>0.6 e 视觉重做：规则蜂窝点阵，确定性，峰尖精确指向攻击者。</summary>
    private static void Lattice()
    {
        Assert.Suite("受击方向：点阵排布");

        var particles = new List<HitDirectionParticle>();
        const float rx = 320f, ry = 150f, halfW = 60f, amp = 70f, pitch = 6.5f;
        const float angle0 = 0.3f;
        float usedPitch = HitDirectionMath.BuildLattice(angle0, halfW, amp, rx, ry, pitch, 256, particles);
        Assert.Near(pitch, usedPitch, 1e-6, "没超上限时用传入的间距");
        Assert.Equal(HitDirectionMath.CountLattice(halfW, amp, pitch), particles.Count, "CountLattice 与实际生成的点数一致");
        Assert.Greater(particles.Count, 100, "点阵有上百个点（旧版随机散布最多 48 个）");

        // 局部平直坐标系：中心列的基准点在椭圆上，其余列的基准点都在过它的切线上，外法线全部相同（不呈扇形散开）
        HitDirectionParticle first = particles[0];
        float centerEllipse = (first.BaseX * first.BaseX) / (rx * rx) + (first.BaseY * first.BaseY) / (ry * ry);
        Assert.Near(1f, centerEllipse, 1e-3, "中心列的基准点落在椭圆上");
        Assert.Near(angle0, ThetaOf(first, rx, ry), 1e-4, "中心列的基准点就是攻击者方位");

        bool allOnTangent = true, parallel = true, heightOk = true, alphaOk = true, sizeOk = true, unitNormal = true;
        int leftCount = 0, rightCount = 0;
        foreach (var p in particles)
        {
            float along = (p.BaseX - first.BaseX) * first.OutX + (p.BaseY - first.BaseY) * first.OutY;
            if (MathF.Abs(along) > 1e-3f) allOnTangent = false;
            if (MathF.Abs(p.OutX - first.OutX) > 1e-5f || MathF.Abs(p.OutY - first.OutY) > 1e-5f) parallel = false;
            if (p.Height < -Eps || p.Height > amp + Eps) heightOk = false;
            if (p.Alpha <= 0f || p.Alpha > 1f + Eps) alphaOk = false;
            if (p.SizeRatio < 1f - HitDirectionMath.SizeShrink - Eps || p.SizeRatio > 1f + Eps) sizeOk = false;
            float nlen = MathF.Sqrt(p.OutX * p.OutX + p.OutY * p.OutY);
            if (MathF.Abs(nlen - 1f) > 2e-3f) unitNormal = false;

            float theta = ThetaOf(p, rx, ry);
            if (theta > angle0 + 1e-4f) rightCount++;
            else if (theta < angle0 - 1e-4f) leftCount++;
        }
        Assert.True(allOnTangent, "其余列的基准点都在中心点的切线上");
        Assert.True(parallel, "所有列共用同一条外法线（平行排列，不呈扇形）");
        Assert.True(heightOk, "突起高度在 0..amplitude");
        Assert.True(alphaOk, "透明度在 (0,1]");
        Assert.True(sizeOk, "尺寸系数在 [1 - SizeShrink, 1]");
        Assert.True(unitNormal, "外法线是单位向量");
        Assert.Equal(leftCount, rightCount, "左右两侧点数相同（以方位角对称）");

        // 方向精确：最高的点就在中心列上，基准方位角 = 传入的 angle
        HitDirectionParticle tip = Tallest(particles);
        Assert.Near(angle0, ThetaOf(tip, rx, ry), 1e-4, "峰尖（最高点）正好在攻击者方位上");
        Assert.Near(MathF.Floor(amp / pitch) * pitch, tip.Height, 1e-3, "中心列从基线按行距一直叠到包络顶");

        // 紧密且规则：任意两点不重叠（蜂窝最近邻 = 行距），也不稀疏
        float minDist = float.MaxValue;
        for (int a = 0; a < particles.Count; a++)
        {
            for (int b = a + 1; b < particles.Count; b++)
            {
                float dx = particles[a].X - particles[b].X, dy = particles[a].Y - particles[b].Y;
                minDist = MathF.Min(minDist, MathF.Sqrt(dx * dx + dy * dy));
            }
        }
        Assert.True(minDist >= pitch * 0.85f, $"点与点不重叠（最近 {minDist:0.00} ≥ 0.85 × 行距）");
        Assert.True(minDist <= pitch * 1.02f, $"排得紧（最近 {minDist:0.00} ≈ 行距）");

        // 每个点的最近邻都在 1.1 × 行距以内 —— 没有孤立的散点
        bool noStray = true;
        for (int a = 0; a < particles.Count && noStray; a++)
        {
            float best = float.MaxValue;
            for (int b = 0; b < particles.Count; b++)
            {
                if (a == b) continue;
                float dx = particles[a].X - particles[b].X, dy = particles[a].Y - particles[b].Y;
                best = MathF.Min(best, MathF.Sqrt(dx * dx + dy * dy));
            }
            if (best > pitch * 1.1f) noStray = false;
        }
        Assert.True(noStray, "没有孤立散点：每个点的最近邻都在 1.1 × 行距以内");

        // 相邻列的基准点沿弧长相距 列距（生成顺序：中心列，然后 -1/+1、-2/+2 …）
        var bases = new List<(float x, float y)>();
        foreach (var p in particles)
        {
            if (bases.Count == 0 || MathF.Abs(bases[^1].x - p.BaseX) > 1e-4f || MathF.Abs(bases[^1].y - p.BaseY) > 1e-4f)
                bases.Add((p.BaseX, p.BaseY));
        }
        float d1 = MathF.Sqrt(MathF.Pow(bases[1].x - bases[0].x, 2) + MathF.Pow(bases[1].y - bases[0].y, 2));
        float d2 = MathF.Sqrt(MathF.Pow(bases[2].x - bases[0].x, 2) + MathF.Pow(bases[2].y - bases[0].y, 2));
        Assert.Near(pitch * 0.8660254f, d1, 1e-3, "左侧第一列与中心列的距离 = 列距（√3/2 × 行距）");
        Assert.Near(pitch * 0.8660254f, d2, 1e-3, "右侧第一列与中心列的距离 = 列距");

        // 中心列：越往外越淡、越小
        var center = particles.FindAll(p => MathF.Abs(p.BaseX - tip.BaseX) < 1e-4f && MathF.Abs(p.BaseY - tip.BaseY) < 1e-4f);
        bool fadesOut = true, shrinks = true;
        for (int k = 1; k < center.Count; k++)
        {
            if (center[k].Height <= center[k - 1].Height || center[k].Alpha >= center[k - 1].Alpha) fadesOut = false;
            if (center[k].SizeRatio >= center[k - 1].SizeRatio) shrinks = false;
        }
        Assert.Greater(center.Count, 8, "中心列有很多行");
        Assert.True(fadesOut, "中心列从基线到峰尖透明度递减（纵向渐变）");
        Assert.True(shrinks, "中心列从基线到峰尖尺寸递减");
        Assert.Near(1f, center[0].Alpha, 1e-5, "中心列基线全亮");
        Assert.Near(1f - HitDirectionMath.RadialFade * center[^1].Rise, center[^1].Alpha, 1e-5, "峰尖透明度 = 1 - RadialFade × Rise");

        // 横向：同一侧的每一列的最底点，离中心越远越淡；最外侧的尾巴已经很淡
        var rightBase = new List<HitDirectionParticle>();
        foreach (var p in particles)
        {
            if (ThetaOf(p, rx, ry) < angle0 - 1e-4f) continue;
            if (p.Height > pitch * 0.75f) continue;
            rightBase.Add(p);
        }
        rightBase.Sort((a, b) => ThetaOf(a, rx, ry).CompareTo(ThetaOf(b, rx, ry)));
        bool lateralFade = true;
        for (int k = 1; k < rightBase.Count; k++)
        {
            // 奇数列外移半行，纵向衰减会让它比相邻偶数列略淡；只比较去掉纵向因子的横向部分
            float l0 = rightBase[k - 1].Alpha / (1f - HitDirectionMath.RadialFade * rightBase[k - 1].Rise);
            float l1 = rightBase[k].Alpha / (1f - HitDirectionMath.RadialFade * rightBase[k].Rise);
            if (l1 > l0 + 1e-5f) { lateralFade = false; break; }
        }
        Assert.True(lateralFade, "离中心越远越淡（横向渐变）");
        Assert.True(rightBase[^1].Alpha < 0.3f, "最外侧的尾巴已经很淡");

        // 确定性：同参数两次结果完全一致（不再有随机种子）
        var again = new List<HitDirectionParticle>();
        HitDirectionMath.BuildLattice(angle0, halfW, amp, rx, ry, pitch, 256, again);
        bool same = again.Count == particles.Count;
        for (int k = 0; same && k < again.Count; k++)
            same = again[k].X == particles[k].X && again[k].Y == particles[k].Y && again[k].Alpha == particles[k].Alpha;
        Assert.True(same, "同参数两次生成逐点一致（确定性，无随机）");

        // 数量上限：放大间距而不是截掉一侧；峰尖仍在方位上
        var capped = new List<HitDirectionParticle>();
        float bigger = HitDirectionMath.BuildLattice(angle0, halfW, amp, rx, ry, pitch, 60, capped);
        Assert.True(capped.Count <= 60, "粒子数不超过上限");
        Assert.Greater(bigger, pitch, "超上限时自动放大间距");
        Assert.Greater(capped.Count, 30, "放大间距后仍有足够的点");
        Assert.Near(angle0, ThetaOf(Tallest(capped), rx, ry), 1e-4, "被限量时峰尖仍在攻击者方位上");

        HitDirectionMath.BuildLattice(0f, halfW, amp, rx, ry, pitch, 100000, again);
        Assert.True(again.Count <= HitDirectionMath.MaxParticles, "上限钳制到 MaxParticles");

        // 退化参数
        HitDirectionMath.BuildLattice(0f, halfW, amp, rx, ry, pitch, 0, again);
        Assert.Equal(0, again.Count, "maxCount = 0 不生成粒子");
        HitDirectionMath.BuildLattice(0f, 0f, amp, rx, ry, pitch, 50, again);
        Assert.Equal(0, again.Count, "halfWidth = 0 不生成粒子");
        HitDirectionMath.BuildLattice(0f, halfW, 0f, rx, ry, pitch, 50, again);
        Assert.Equal(0, again.Count, "amplitude = 0 不生成粒子");
        HitDirectionMath.BuildLattice(0f, halfW, amp, rx, ry, 0f, 50, again);
        Assert.Equal(0, again.Count, "pitch = 0 不生成粒子");

        // 背后 / 侧面也成立（椭圆在这些位置的曲率不同）
        foreach (float a in new[] { MathF.PI, MathF.PI / 2f, -MathF.PI / 2f, 2.4f })
        {
            HitDirectionMath.BuildLattice(a, halfW, amp, rx, ry, pitch, 256, again);
            float diff = MathF.Abs(MathF.IEEERemainder(ThetaOf(Tallest(again), rx, ry) - a, 2f * MathF.PI));
            Assert.True(diff < 1e-3f, $"方位 {a:0.00} rad：峰尖在攻击者方位上（差 {diff:0.0000}）");
        }
    }

    private static void ShapeAndTimeline()
    {
        Assert.Suite("受击方向：伤害映射与时间线");
        const float rx = 320f, ry = 150f;

        HitDirectionMath.ShapeFor(10f, false, out float w10, out float a10);
        HitDirectionMath.ShapeFor(60f, false, out float w60, out float a60);
        HitDirectionMath.ShapeFor(70f, false, out float w70, out float a70);
        HitDirectionMath.ShapeFor(500f, false, out float w500, out float a500);
        HitDirectionMath.ShapeFor(60f, true, out float w60a, out float a60a);
        Assert.Greater(a60, a10, "伤害越高突起越高");
        Assert.Greater(w60, w10, "伤害越高突起越宽");
        Assert.Near(a70, a500, 1e-5, "70 以上不再增长（高度）");
        Assert.Near(w70, w500, 1e-5, "70 以上不再增长（宽度）");
        Assert.Near(w60, w60a, 1e-5, "护甲吸收：宽度一样");
        Assert.Greater(a60, a60a, "护甲吸收：更矮");
        Assert.True(w500 < 80f, "满伤害的半宽也窄于 80（集中指向）");
        Assert.Greater(a500, w500, "满伤害时突起比半宽还高（又窄又突出）");

        // 轮廓：中心 1、两端 0、对称、单调；顶端收尖（斜率不为 0，不是圆顶）
        Assert.Near(1f, HitDirectionMath.Profile(0f), 1e-6, "轮廓中心 = 1");
        Assert.Near(0f, HitDirectionMath.Profile(1f), 1e-6, "轮廓边界 = 0");
        Assert.Near(HitDirectionMath.Profile(0.3f), HitDirectionMath.Profile(-0.3f), 1e-6, "轮廓左右对称");
        Assert.True(HitDirectionMath.Profile(0.6f) < HitDirectionMath.Profile(0.3f), "轮廓向两侧单调下降");
        Assert.True(1f - HitDirectionMath.Profile(0.05f) > 0.04f, "顶端收尖：离中心 5% 就降 4% 以上（圆顶只降 0.25%）");

        var list = new List<HitDirectionParticle>();
        HitDirectionMath.BuildLattice(0f, w500, a500, rx, ry, HitDirectionMath.LatticePitch, HitDirectionMath.BurstMaxCount, list);
        Assert.True(list.Count <= HitDirectionMath.BurstMaxCount, "满伤害的点数不超过每簇上限");
        Assert.Greater(list.Count, 60, "满伤害的点阵约 75 个点（2026-10-25 目检定的出厂外观）");
        HitDirectionMath.BuildLattice(0f, w10, a10, rx, ry, HitDirectionMath.LatticePitch, HitDirectionMath.BurstMaxCount, list);
        Assert.Greater(list.Count, 15, "低伤害也有二十来个点（不是零星几颗）");

        // ---- 时间线 ----
        Assert.Near(0f, HitDirectionMath.VisibleRise(0f), 1e-6, "出生第一帧只有基线（Rise = 0）可见");
        Assert.Near(1f, HitDirectionMath.VisibleRise(HitDirectionMath.RevealPortion), 1e-5, "点亮阶段结束时长满到峰尖");
        Assert.Near(1f, HitDirectionMath.VisibleRise(HitDirectionMath.RetractStart), 1e-5, "收回之前保持全部可见");
        Assert.True(HitDirectionMath.VisibleRise(0.8f) < HitDirectionMath.VisibleRise(0.6f), "收回阶段从峰尖往基线缩");
        Assert.True(HitDirectionMath.VisibleRise(1f) < 0f, "结束时全部隐藏");
        bool riseMonotone = true;
        for (float t = 0f; t < HitDirectionMath.RevealPortion; t += 0.01f)
            if (HitDirectionMath.VisibleRise(t + 0.01f) < HitDirectionMath.VisibleRise(t) - 1e-6f) riseMonotone = false;
        Assert.True(riseMonotone, "点亮阶段单调增长");
        Assert.Near(1f, HitDirectionMath.BurstFade(0.3f), 1e-6, "收回之前整簇全亮");
        Assert.True(HitDirectionMath.BurstFade(0.7f) < 1f && HitDirectionMath.BurstFade(0.7f) > 0f, "收回阶段淡出中");
        Assert.Near(0f, HitDirectionMath.BurstFade(1f), 1e-6, "结束时透明");
        Assert.Near(0f, HitDirectionMath.Drift(0f), 1e-6, "漂移从 0 开始");
        Assert.Near(HitDirectionMath.DriftDistance, HitDirectionMath.Drift(1f), 1e-5, "漂移终点 = DriftDistance");

        HitDirectionMath.Normal(0f, rx, ry, out float n0x, out float n0y);
        Assert.Near(0f, n0x, 1e-5, "正前方的峰尖法线朝上（x = 0）");
        Assert.Near(1f, n0y, 1e-5, "正前方的峰尖法线朝上（y = 1）");
        HitDirectionMath.Normal(MathF.PI / 2f, rx, ry, out float n1x, out float n1y);
        Assert.Near(1f, n1x, 1e-5, "右侧的峰尖法线朝右");
        Assert.Near(0f, n1y, 1e-4, "右侧的峰尖法线没有竖直分量");

        StyleOverrides();
    }

    /// <summary>2026-10-25：点阵外观全部做成设置项 —— 样式参数逐项生效，离谱值被钳住，出厂样式与之前的常量一致。</summary>
    private static void StyleOverrides()
    {
        Assert.Suite("受击方向：可调样式（设置项）");
        const float rx = 320f, ry = 150f;
        var def = HitDirectionStyle.Default;

        // 出厂样式 = 无样式重载（设置里什么都不改，外观和视觉重做时一样）
        foreach (float dmg in new[] { 0f, 20f, 45f, 70f, 200f })
        {
            HitDirectionMath.ShapeFor(dmg, false, out float w0, out float a0);
            HitDirectionMath.ShapeFor(dmg, false, def, out float w1, out float a1);
            Assert.Near(w0, w1, 1e-4, $"出厂样式的半宽与默认一致（伤害 {dmg}）");
            Assert.Near(a0, a1, 1e-4, $"出厂样式的高度与默认一致（伤害 {dmg}）");
        }
        HitDirectionMath.ShapeFor(0f, false, out float wZero, out float aZero);
        Assert.Near(25.15f, wZero, 1e-4, "出厂：0 伤害半宽 25.15（目检定值）");
        Assert.Near(55.02f * 0.73f, ShapeHeight(500f, true, def), 1e-4, "出厂：满强度护甲吸收高度 = 55.02 × 0.73");

        var a = new List<HitDirectionParticle>();
        var b = new List<HitDirectionParticle>();
        HitDirectionMath.BuildLattice(0.2f, 60f, 70f, rx, ry, 6.5f, 256, a);
        HitDirectionMath.BuildLattice(0.2f, 60f, 70f, rx, ry, 6.5f, 256, def, b);
        bool same = a.Count == b.Count;
        for (int i = 0; same && i < a.Count; i++) same = a[i].X == b[i].X && a[i].Alpha == b[i].Alpha && a[i].SizeRatio == b[i].SizeRatio;
        Assert.True(same, "出厂样式的点阵与无样式重载逐点一致");

        // 半宽 / 高度区间：伤害 0 → Low，FullDamage → High，中间线性
        var custom = def;
        custom.WidthLow = 20f; custom.WidthHigh = 40f;
        custom.HeightLow = 10f; custom.HeightHigh = 30f;
        custom.FullDamage = 100f; custom.ArmorHeightScale = 0.5f;
        HitDirectionMath.ShapeFor(0f, false, custom, out float wl, out float al);
        HitDirectionMath.ShapeFor(50f, false, custom, out float wm, out float am);
        HitDirectionMath.ShapeFor(100f, false, custom, out float wh, out float ah);
        Assert.Near(20f, wl, 1e-4, "半宽(低伤害) 生效");
        Assert.Near(30f, wm, 1e-4, "半宽在 0..满强度伤害 之间线性插值");
        Assert.Near(40f, wh, 1e-4, "半宽(满强度) 生效");
        Assert.Near(10f, al, 1e-4, "高度(低伤害) 生效");
        Assert.Near(20f, am, 1e-4, "高度线性插值");
        Assert.Near(30f, ah, 1e-4, "高度(满强度) 生效");
        Assert.Near(15f, ShapeHeight(100f, true, custom), 1e-4, "护甲吸收高度比例 生效");

        var weird = def;
        weird.WidthLow = -50f; weird.HeightLow = -10f; weird.FullDamage = 0f; weird.ArmorHeightScale = -1f;
        HitDirectionMath.ShapeFor(10f, true, weird, out float ww, out float aw);
        Assert.True(ww >= 1f && aw >= 1f, "负数 / 0 的设置值不会算出非正的尺寸");

        // 纵向渐变 = 0：中心列整列全亮；尺寸收缩 = 0：所有点一样大
        var flat = def;
        flat.RadialFade = 0f; flat.SizeShrink = 0f;
        HitDirectionMath.BuildLattice(0f, 60f, 70f, rx, ry, 6.5f, 256, flat, a);
        bool centerOpaque = true, sameSize = true;
        foreach (var p in a)
        {
            if (MathF.Abs(p.BaseX - a[0].BaseX) < 1e-4f && MathF.Abs(p.BaseY - a[0].BaseY) < 1e-4f && MathF.Abs(p.Alpha - 1f) > 1e-5f) centerOpaque = false;
            if (MathF.Abs(p.SizeRatio - 1f) > 1e-5f) sameSize = false;
        }
        Assert.True(centerOpaque, "峰尖变淡程度 = 0：中心列从基线到峰尖都全亮");
        Assert.True(sameSize, "峰尖缩小比例 = 0：所有点一样大");

        var hard = def;
        hard.RadialFade = 5f; hard.SizeShrink = 5f;
        HitDirectionMath.BuildLattice(0f, 60f, 70f, rx, ry, 6.5f, 256, hard, a);
        bool clamped = true;
        foreach (var p in a) if (p.Alpha < -1e-5f || p.SizeRatio < 0.05f - 1e-5f) clamped = false;
        Assert.True(clamped, "越界的渐变 / 缩小比例被钳住（透明度不为负、点不会缩没）");

        // 尾巴 = 1：没有超出半宽的点；尾巴更长 → 点更多、更宽
        var noTail = def;
        noTail.TailRatio = 1f;
        HitDirectionMath.BuildLattice(0f, 60f, 70f, rx, ry, 6.5f, 256, noTail, a);
        float maxAbsX = 0f;
        foreach (var p in a) maxAbsX = MathF.Max(maxAbsX, MathF.Abs(p.BaseX - a[0].BaseX));
        Assert.True(maxAbsX <= 60f + 1e-3f, $"尾巴长度 = 1：所有列都在半宽以内（最远 {maxAbsX:0.0}）");
        Assert.Equal(HitDirectionMath.CountLattice(60f, 70f, 6.5f, 1f), a.Count, "CountLattice 的尾巴参数与生成一致");
        var longTail = def;
        longTail.TailRatio = 3f;
        HitDirectionMath.BuildLattice(0f, 60f, 70f, rx, ry, 6.5f, 256, longTail, b);
        Assert.Greater(b.Count, a.Count, "尾巴越长点越多");

        // 时间线参数
        Assert.Near(1f, HitDirectionMath.VisibleRise(0.5f, 0.5f, 0.8f), 1e-5, "长出时间占比 0.5：t = 0.5 时长满");
        Assert.True(HitDirectionMath.VisibleRise(0.25f, 0.5f, 0.8f) < 1f, "长出时间占比 0.5：t = 0.25 时还没长满");
        Assert.Near(1f, HitDirectionMath.BurstFade(0.7f, 0.8f), 1e-6, "开始收回时间点 0.8：t = 0.7 仍全亮");
        Assert.True(HitDirectionMath.BurstFade(0.9f, 0.8f) < 1f, "开始收回时间点 0.8：t = 0.9 已在淡出");
        Assert.True(HitDirectionMath.VisibleRise(0.995f, 0.16f, 5f) <= 1f, "越界的收回时间点被钳到 0.99，不会除零");
        Assert.True(HitDirectionMath.BurstFade(0.995f, 5f) < 1f, "收回时间点被钳住后最后 1% 仍然淡出");
        Assert.Near(1f, HitDirectionMath.VisibleRise(0.01f, 0f, 0.45f), 1e-6, "长出时间占比 = 0（被钳到 0.001）：出生后的下一帧就长满");
        Assert.Near(20f, HitDirectionMath.Drift(1f, 20f), 1e-5, "外漂距离 生效");
        Assert.Near(0f, HitDirectionMath.Drift(1f, -5f), 1e-6, "负的外漂距离按 0");
    }

    private static float ShapeHeight(float damage, bool armorOnly, in HitDirectionStyle style)
    {
        HitDirectionMath.ShapeFor(damage, armorOnly, style, out _, out float amplitude);
        return amplitude;
    }
}
