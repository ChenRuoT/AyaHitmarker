using System.Collections.Generic;
using AyaHitmarker.Game;
using UnityEngine;

namespace AyaHitmarker.Ui;

/// <summary>
/// 0.6 e 受击方向指示：屏幕中央一块**透明椭圆盘**（长轴水平，不画底盘），
/// 每次挨打时在攻击者所在方位亮起一簇粒子。
///
/// 造型参考二次函数突起：粒子在抛物线包络内，**底部密、顶部疏**，伤害越高突起越高；
/// 颜色分三档：护甲吸收的钝伤 = 白色、普通伤害 = 黄色、重伤 = 红色。
/// 角度约定见 <see cref="HitDirectionMath"/>（0 = 正前 / 椭圆上端，+π/2 = 右侧）。
///
/// 只画附加的 Image 粒子，不做自定义 Graphic；粒子行走 <see cref="UiImageRow"/> 脏检查 + CanvasGroup 淡出。
/// </summary>
internal sealed class HitDirectionLayer
{
    private sealed class Particle
    {
        internal UiImageRow? Row;
        internal float BaseX;
        internal float BaseY;
        internal float OutX;
        internal float OutY;
        internal float Height;
        internal float Size;
        internal Color Color;
        internal float Born;
        internal float Duration;
        internal bool Used;
    }

    private readonly List<Particle> _active = new List<Particle>(64);
    private readonly List<Particle> _pool = new List<Particle>(64);
    private readonly List<HitDirectionParticle> _scratch = new List<HitDirectionParticle>(64);

    private RectTransform? _parent;
    private Sprite? _dot;

    internal void Attach(RectTransform parent)
    {
        _parent = parent;
        _dot = ResolveDot();
    }

    /// <summary>启动时把柔光粒子贴图烘好（复用 <see cref="Assets.IconBank.GetGlow"/> 的白精灵辉光）。</summary>
    internal void Warmup() => _dot ??= ResolveDot();

    private static Sprite? ResolveDot()
    {
        // 柔光圆点（0.6 e）：IconShapes.SoftDot 程序化烘焙，随缓存一起在卸载时销毁
        return Assets.ProceduralIcons.SoftDot(9f);
    }

    /// <summary>真实事件入口：受害者是我，把世界向量换算成相机方位角后生成粒子。</summary>
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
        Spawn(angle, damage, hit.ArmorOnly, heavy, NextSeed());
    }

    /// <summary>调试 / 验收入口：直接给定方位角（弧度）与伤害，跳过相机换算。</summary>
    internal void ShowDebug(float angle, float damage, bool armorOnly)
    {
        bool heavy = !armorOnly && damage >= Mathf.Max(1f, Plugin.Cfg.HitDirectionHeavyDamage.Value);
        Spawn(angle, damage, armorOnly, heavy, NextSeed());
    }

    private static uint NextSeed()
    {
        uint seed = (uint)UnityEngine.Random.Range(1, int.MaxValue);
        return seed == 0u ? 1u : seed;
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

    private void Spawn(float angle, float damage, bool armorOnly, bool heavy, uint seed)
    {
        var cfg = Plugin.Cfg;
        if (!cfg.HitDirectionEnabled.Value || _parent == null) return;

        _dot ??= ResolveDot();
        if (_dot == null) return;

        Vector2 radii = UiTheme.ParseOffset(cfg.HitDirectionEllipse.Value, new Vector2(320f, 150f));
        float ui = Mathf.Clamp(cfg.UiScale.Value, 0.3f, 3f);
        float rx = Mathf.Max(20f, radii.x * ui);
        float ry = Mathf.Max(10f, radii.y * ui);

        // 伤害 → 突起强度（0.25~1）：越高突起越大、角宽度越大、粒子越多
        float norm = Mathf.Clamp01(damage / 70f);
        float intensity = Mathf.Lerp(0.25f, 1f, norm);
        float amplitude = (18f + 80f * intensity) * ui;
        float sigma = 0.48f + 0.35f * intensity;
        int count = armorOnly
            ? Mathf.RoundToInt(12f + 16f * intensity)
            : Mathf.RoundToInt(16f + 34f * intensity);
        count = Mathf.Clamp(count, 8, 48);

        Color color = UiTheme.Parse(armorOnly ? cfg.HitDirectionColorAbsorb.Value
            : heavy ? cfg.HitDirectionColorHeavy.Value
            : cfg.HitDirectionColorNormal.Value,
            armorOnly ? Color.white : heavy ? new Color(1f, 0.23f, 0.19f) : new Color(1f, 0.82f, 0.29f));

        HitDirectionMath.BuildParticles(angle, sigma, amplitude, rx, ry, count, seed, _scratch);
        if (_scratch.Count == 0) return;

        float now = UiClock.Now;
        float duration = Mathf.Max(0.1f, cfg.HitDirectionDuration.Value);
        float sizeBase = 12f * ui;

        for (int i = 0; i < _scratch.Count; i++)
        {
            HitDirectionParticle p = _scratch[i];
            Particle? particle = Rent();
            if (particle == null) break;

            particle.BaseX = p.BaseX;
            particle.BaseY = p.BaseY;
            particle.OutX = p.OutX;
            particle.OutY = p.OutY;
            particle.Height = p.Height;
            particle.Size = sizeBase * p.SizeRatio;
            particle.Color = color;
            particle.Born = now;
            // 每颗粒子的时长略有差异，避免整簇同时消失
            particle.Duration = duration * Mathf.Lerp(0.72f, 1.05f, (i * 37 % 100) / 100f);
            particle.Used = true;

            _active.Add(particle);
        }

        if (cfg.DebugEnabled.Value && _active.Count > 0)
        {
            Plugin.Logger.LogInfo(
                $"[hitdir] 角度={angle * Mathf.Rad2Deg:0.#}° dmg={damage:0.#} armor={armorOnly} heavy={heavy} " +
                $"粒子={_scratch.Count} 突起={amplitude:0.#} rx={rx:0.#} ry={ry:0.#}");
        }
    }

    internal void Tick()
    {
        if (_active.Count == 0) return;

        float now = UiClock.Now;
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            Particle p = _active[i];
            float t = (now - p.Born) / p.Duration;
            if (t >= 1f)
            {
                p.Row?.Hide();
                Recycle(p);
                _active.RemoveAt(i);
                continue;
            }

            if (t < 0f) t = 0f;
            float ease = 1f - (1f - t) * (1f - t);   // OutQuad：先快后慢地向外撑开
            float radial = p.Height * ease;
            Vector2 pos = new Vector2(p.BaseX + p.OutX * radial, p.BaseY + p.OutY * radial);

            // 尺寸只写一次（出生时的 p.Size），这里只改 localScale / 位置 / CanvasGroup 透明度
            float scale = 0.6f + 0.55f * ease;
            float alpha = (1f - t) * (1f - t);

            var row = p.Row;
            if (row == null) continue;

            row.Apply(pos, new Vector2(p.Size, p.Size), _dot, p.Color, scale);
            row.SetFade(alpha);
        }
    }

    internal void Reset()
    {
        for (int i = 0; i < _active.Count; i++)
        {
            _active[i].Row?.Hide();
            Recycle(_active[i]);
        }
        _active.Clear();
    }

    private Particle? Rent()
    {
        if (_pool.Count > 0)
        {
            int last = _pool.Count - 1;
            Particle p = _pool[last];
            _pool.RemoveAt(last);
            p.Used = true;
            return p;
        }

        if (_parent == null) return null;
        var row = UiImageRow.Create("AyaHitmarker.HitDirectionParticle", _parent);
        return new Particle { Row = row };
    }

    private void Recycle(Particle p)
    {
        p.Used = false;
        if (_pool.Count < 96) _pool.Add(p);
    }
}
