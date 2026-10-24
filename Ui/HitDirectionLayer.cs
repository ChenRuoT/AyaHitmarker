using System.Collections.Generic;
using AyaHitmarker.Game;
using UnityEngine;

namespace AyaHitmarker.Ui;

/// <summary>
/// 0.6 e 受击方向指示：屏幕中央一块**透明椭圆盘**（长轴水平，不画底盘），
/// 每次挨打时在攻击者所在方位亮起一簇**规则排列的点阵**（视觉重做后不再随机散布）。
///
/// 造型：蜂窝点阵组成一个收尖的二次突起，峰尖精确落在攻击者方位上；点小而密，
/// 透明度从基线到峰尖、从中心到两侧渐变（排布全部在 <see cref="HitDirectionMath.BuildLattice"/>，可离线测 / 预览）。
/// 颜色分三档：护甲吸收的钝伤 = 白色、普通伤害 = 黄色、重伤 = 红色。
/// 角度约定见 <see cref="HitDirectionMath"/>（0 = 正前 / 椭圆上端，+π/2 = 右侧）。
///
/// 每一簇是一个「簇容器」（自带 CanvasGroup，整簇淡出只写一次 alpha）+ 若干子点：
/// 点的位置 / 尺寸 / 颜色只在第一次点亮时写一次，之后每帧只按时间线开关可见性；
/// 整簇向外漂移写的是容器的位置（每簇每帧一次），不动子点。只用 interop 自带的 Image，不做自定义 Graphic。
/// </summary>
internal sealed class HitDirectionLayer
{
    /// <summary>
    /// 同时存在的簇数的硬上限（设置项「同时最多簇数」钳到 1..这个值）：
    /// 持续挨打时最旧的一簇提前回收（新簇总会盖在它上面），池子也按它封顶 —— 对象数有上限。
    /// </summary>
    private const int MaxBurstsCap = 12;

    private sealed class Dot
    {
        internal UiImageRow? Row;
        internal Vector2 Pos;
        internal float Size;
        internal Color Color;
        internal float Rise;
        internal bool Applied;
        internal bool Visible;
    }

    private sealed class Burst
    {
        internal UiImageRow? Container;
        internal readonly List<Dot> Dots = new List<Dot>(HitDirectionMath.BurstMaxCount);
        internal int Count;
        internal Vector2 Normal;
        internal float Born;
        internal float Duration;

        // 出生时从设置里冻结下来的样式：设置页里改动只影响之后的新簇，不会让正在播放的簇跳变
        internal Sprite? Sprite;
        internal float Opacity;
        internal float RevealPortion;
        internal float RetractStart;
        internal float DriftDistance;
    }

    private readonly List<Burst> _active = new List<Burst>(MaxBurstsCap);
    private readonly List<Burst> _pool = new List<Burst>(MaxBurstsCap);
    private readonly List<HitDirectionParticle> _scratch = new List<HitDirectionParticle>(HitDirectionMath.MaxParticles);

    private RectTransform? _parent;
    private Sprite? _dot;

    internal void Attach(RectTransform parent)
    {
        _parent = parent;
        _dot = ResolveDot();
    }

    /// <summary>启动时把点阵粒子贴图烘好。</summary>
    internal void Warmup() => _dot ??= ResolveDot();

    private static Sprite? ResolveDot()
    {
        // 点阵粒子（0.6 e）：IconShapes.LatticeDot 程序化烘焙（点大小 / 描边按设置），随缓存一起在卸载时销毁
        var cfg = Plugin.Cfg;
        return Assets.ProceduralIcons.LatticeDot(cfg.HitDirectionDotSize.Value, cfg.HitDirectionRimAlpha.Value);
    }

    /// <summary>当前设置 → 纯逻辑样式（每次生成一簇时读一次，设置页里的改动下一簇就生效）。</summary>
    private static HitDirectionStyle StyleFromConfig(AyaConfig cfg) => new HitDirectionStyle
    {
        TailRatio = cfg.HitDirectionTailRatio.Value,
        RadialFade = cfg.HitDirectionRadialFade.Value,
        SizeShrink = cfg.HitDirectionSizeShrink.Value,
        WidthLow = cfg.HitDirectionWidthLow.Value,
        WidthHigh = cfg.HitDirectionWidthHigh.Value,
        HeightLow = cfg.HitDirectionHeightLow.Value,
        HeightHigh = cfg.HitDirectionHeightHigh.Value,
        FullDamage = cfg.HitDirectionFullDamage.Value,
        ArmorHeightScale = cfg.HitDirectionArmorHeightScale.Value,
        RevealPortion = cfg.HitDirectionRevealPortion.Value,
        RetractStart = cfg.HitDirectionRetractStart.Value,
        DriftDistance = cfg.HitDirectionDrift.Value,
    };

    /// <summary>真实事件入口：受害者是我，把世界向量换算成相机方位角后生成点阵。</summary>
    internal void Show(in HitRecord hit)
    {
        if (!Plugin.Cfg.HitDirectionEnabled.Value) return;

        Vector3 incoming = hit.IncomingDirection;
        incoming.y = 0f;
        if (incoming.sqrMagnitude < 1e-4f) return;

        if (!TryGetCameraBasis(out Vector3 forward, out Vector3 right)) return;
        if (!HitDirectionMath.TryAngle(incoming.x, incoming.z, forward.x, forward.z, right.x, right.z, out float angle)) return;

        float damage = hit.ArmorOnly ? hit.ArmorDamage : hit.BodyDamage;
        bool heavy = !hit.ArmorOnly && damage >= Mathf.Max(1f, Plugin.Cfg.HitDirectionHeavyDamage.Value);
        Spawn(angle, damage, hit.ArmorOnly, heavy);
    }

    /// <summary>调试 / 验收入口：直接给定方位角（弧度）与伤害，跳过相机换算。</summary>
    internal void ShowDebug(float angle, float damage, bool armorOnly)
    {
        bool heavy = !armorOnly && damage >= Mathf.Max(1f, Plugin.Cfg.HitDirectionHeavyDamage.Value);
        Spawn(angle, damage, armorOnly, heavy);
    }

    private static bool TryGetCameraBasis(out Vector3 forward, out Vector3 right)
    {
        forward = Vector3.forward;
        right = Vector3.right;
        var cam = Camera.main;
        if (cam == null) return false;

        var t = cam.transform;
        forward = t.forward;
        forward.y = 0f;
        right = t.right;
        right.y = 0f;
        return forward.sqrMagnitude > 1e-4f && right.sqrMagnitude > 1e-4f;
    }

    private void Spawn(float angle, float damage, bool armorOnly, bool heavy)
    {
        var cfg = Plugin.Cfg;
        if (!cfg.HitDirectionEnabled.Value || _parent == null) return;

        // 每簇都重新取一次：点大小 / 描边改了会落到另一个缓存桶（同参数是一次字典查找）
        _dot = ResolveDot();
        if (_dot == null) return;

        // 画布单位：UI 缩放已经由 CanvasScaler.scaleFactor 统一处理，这里不再乘（0.6 e 初版乘了两次）
        Vector2 radii = UiTheme.ParseOffset(cfg.HitDirectionEllipse.Value, new Vector2(320f, 150f));
        float rx = Mathf.Max(20f, radii.x);
        float ry = Mathf.Max(10f, radii.y);

        HitDirectionStyle style = StyleFromConfig(cfg);
        float dotSize = Mathf.Clamp(cfg.HitDirectionDotSize.Value, 1f, 64f);
        float spacing = Mathf.Clamp(cfg.HitDirectionDotSpacing.Value, 1f, 64f);
        int maxDots = Mathf.Clamp(cfg.HitDirectionMaxDots.Value, 1, HitDirectionMath.MaxParticles);

        HitDirectionMath.ShapeFor(damage, armorOnly, style, out float halfWidth, out float amplitude);
        float usedSpacing = HitDirectionMath.BuildLattice(angle, halfWidth, amplitude, rx, ry, spacing, maxDots, style, _scratch);
        if (_scratch.Count == 0) return;

        Color color = UiTheme.Parse(armorOnly ? cfg.HitDirectionColorAbsorb.Value
            : heavy ? cfg.HitDirectionColorHeavy.Value
            : cfg.HitDirectionColorNormal.Value,
            armorOnly ? Color.white : heavy ? new Color(1f, 0.23f, 0.19f) : new Color(1f, 0.82f, 0.29f));

        Burst? burst = RentBurst(Mathf.Clamp(cfg.HitDirectionMaxBursts.Value, 1, MaxBurstsCap));
        if (burst == null) return;

        HitDirectionMath.Normal(angle, rx, ry, out float nx, out float ny);
        burst.Normal = new Vector2(nx, ny);
        burst.Born = UiClock.Now;
        burst.Duration = Mathf.Max(0.1f, cfg.HitDirectionDuration.Value);
        burst.Count = 0;
        burst.Sprite = _dot;
        burst.Opacity = Mathf.Clamp01(cfg.HitDirectionOpacity.Value);
        burst.RevealPortion = style.RevealPortion;
        burst.RetractStart = style.RetractStart;
        burst.DriftDistance = style.DriftDistance;

        for (int i = 0; i < _scratch.Count; i++)
        {
            Dot? dot = RentDot(burst, i);
            if (dot == null) break;

            HitDirectionParticle p = _scratch[i];
            dot.Pos = new Vector2(p.X, p.Y);
            dot.Size = dotSize * p.SizeRatio;
            dot.Color = UiTheme.WithAlpha(color, color.a * p.Alpha);
            dot.Rise = p.Rise;
            dot.Applied = false;
            dot.Visible = false;
            burst.Count++;
        }

        _active.Add(burst);
        Draw(burst, UiClock.Now);   // 出生这一帧就把基线画出来

        if (cfg.DebugEnabled.Value)
        {
            Plugin.Logger.LogInfo(
                $"[hitdir] 角度={angle * Mathf.Rad2Deg:0.#}° dmg={damage:0.#} armor={armorOnly} heavy={heavy} " +
                $"粒子={burst.Count} 半宽={halfWidth:0.#} 突起={amplitude:0.#} 间距={usedSpacing:0.##} 点={dotSize:0.#} " +
                $"rx={rx:0.#} ry={ry:0.#} 簇={_active.Count}");
        }
    }

    internal void Tick()
    {
        if (_active.Count == 0) return;

        float now = UiClock.Now;
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            Burst b = _active[i];
            if (!Draw(b, now))
            {
                Release(b);
                _active.RemoveAt(i);
            }
        }
    }

    /// <summary>按时间线画一簇；返回 false 表示这簇已经结束。</summary>
    private bool Draw(Burst b, float now)
    {
        float t = (now - b.Born) / b.Duration;
        if (t >= 1f) return false;
        if (t < 0f) t = 0f;

        var container = b.Container;
        if (container == null) return false;

        float drift = HitDirectionMath.Drift(t, b.DriftDistance);
        container.Apply(b.Normal * drift, Vector2.one, null, Color.clear);
        container.SetFade(HitDirectionMath.BurstFade(t, b.RetractStart) * b.Opacity);

        float visibleRise = HitDirectionMath.VisibleRise(t, b.RevealPortion, b.RetractStart) + 1e-4f;
        for (int i = 0; i < b.Count; i++)
        {
            Dot d = b.Dots[i];
            var row = d.Row;
            if (row == null) continue;

            bool want = d.Rise <= visibleRise;
            if (want == d.Visible) continue;
            d.Visible = want;

            if (!want)
            {
                row.SetActive(false);
                continue;
            }

            if (!d.Applied)
            {
                // 位置 / 尺寸 / 颜色只写这一次（§3.5）；之后只开关可见性
                row.Apply(d.Pos, new Vector2(d.Size, d.Size), b.Sprite, d.Color);
                d.Applied = true;
            }
            else
            {
                row.SetActive(true);
            }
        }
        return true;
    }

    internal void Reset()
    {
        for (int i = 0; i < _active.Count; i++) Release(_active[i]);
        _active.Clear();
    }

    private Burst? RentBurst(int maxBursts)
    {
        // 簇数到上限：提前回收最旧的一簇（它已经快淡完了，新簇也会盖在它上面）。
        // 用 while：设置里把上限调小后，多出来的旧簇在下一次受击时一次收掉
        while (_active.Count >= maxBursts && _active.Count > 0)
        {
            Release(_active[0]);
            _active.RemoveAt(0);
        }

        Burst burst;
        if (_pool.Count > 0)
        {
            int last = _pool.Count - 1;
            burst = _pool[last];
            _pool.RemoveAt(last);
        }
        else
        {
            if (_parent == null) return null;
            var container = UiImageRow.Create("AyaHitmarker.HitDirectionBurst", _parent);
            container.Img.enabled = false;   // 只要它的 RectTransform + CanvasGroup，不画东西
            burst = new Burst { Container = container };
        }

        // 新簇画在最上面
        burst.Container?.Rt.SetAsLastSibling();
        return burst;
    }

    private Dot? RentDot(Burst burst, int index)
    {
        if (index < burst.Dots.Count) return burst.Dots[index];
        if (index >= HitDirectionMath.MaxParticles || burst.Container == null) return null;

        var row = UiImageRow.Create("AyaHitmarker.HitDirectionDot", burst.Container.Rt, withGroup: false);
        var dot = new Dot { Row = row };
        burst.Dots.Add(dot);
        return dot;
    }

    /// <summary>
    /// 把一簇收回池子：容器隐藏（子点随之不渲染），子点各自隐藏并失效脏检查缓存。
    /// 池子与活跃簇各不超过 <see cref="MaxBurstsCap"/>，子点按簇复用 —— 持续挨打也不会无限创建对象。
    /// </summary>
    private void Release(Burst b)
    {
        for (int i = 0; i < b.Dots.Count; i++)
        {
            Dot d = b.Dots[i];
            d.Row?.Hide();
            d.Applied = false;
            d.Visible = false;
        }
        b.Count = 0;
        b.Container?.Hide();
        if (_pool.Count < MaxBurstsCap) _pool.Add(b);
        else if (b.Container != null) Object.Destroy(b.Container.Go);
    }
}
