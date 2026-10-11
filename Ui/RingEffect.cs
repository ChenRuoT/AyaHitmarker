using UnityEngine;
using UnityEngine.UI;

namespace AyaHitmarker.Ui;

internal enum KillType
{
    Normal = 0,
    Headshot = 1,
    Explosion = 2,
    Crit = 3,
}

/// <summary>
/// 击杀图标的环形特效。逐行移植旧项目 <c>IconRingEffect</c>（本身又移植自 gd656killicon）。
///
/// 旧版用 <c>GL.TRIANGLE_STRIP</c> + <c>GL.LoadPixelMatrix()</c> 在 OnGUI 里画圆环；
/// uGUI 里没有等价的即时绘制入口，所以改成：向 <c>Assets.IconBank</c> 要一张
/// 「外沿在半径 1.0、内沿在 1-thickness/radius」的环形纹理（按比例分桶缓存），
/// 再用一个 <see cref="Image"/> 缩放到位。
///
/// 曲线与原版逐项一致：半径 ease-out cubic 从 <c>10/(10+32)</c> 比例涨到满，
/// 厚度随 <c>(1-t)</c> 收细，透明度 <c>(1-t)²</c>。
/// </summary>
internal sealed class RingEffect
{
    private GameObject?[] _go = new GameObject?[2];
    private Image?[] _img = new Image?[2];
    private RectTransform?[] _rt = new RectTransform?[2];
    private bool _attached;

    private KillType _type = KillType.Normal;
    private float _start = -1f;

    /// <summary>特效是否还在生命周期内（用于决定是否还要每帧驱动）。</summary>
    internal bool IsActive => _start > 0f;

    internal void Attach(RectTransform parent)
    {
        if (_attached) return;
        _attached = true;

        for (int i = 0; i < 2; i++)
        {
            var go = new GameObject($"AyaHitmarker.Ring{i}");
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.color = Color.clear;

            var rt = UiTheme.Rect(go);
            if (rt != null)
            {
                rt.SetParent(parent, false);
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
            }

            _go[i] = go;
            _img[i] = img;
            _rt[i] = rt;
            go.SetActive(false);
        }
    }

    internal void Trigger(float now, bool enabled, KillType type)
    {
        _type = type;

        bool supported = type == KillType.Headshot || type == KillType.Explosion || type == KillType.Crit;
        if (!enabled || !supported)
        {
            _start = -1f;
            HideAll();
            return;
        }

        _start = now + Plugin.Cfg.RingEffectDelay.Value;
    }

    internal void Reset()
    {
        _start = -1f;
        HideAll();
    }

    /// <summary><paramref name="center"/> 是画布局部坐标。</summary>
    internal void Tick(Vector2 center, float now)
    {
        if (_start <= 0f)
        {
            HideAll();
            return;
        }

        var cfg = Plugin.Cfg;
        float duration = Mathf.Max(0.01f, cfg.RingEffectDuration.Value);
        float secondDelay = Mathf.Max(0f, cfg.RingExplosionSecondRingDelay.Value);
        float elapsed = now - _start;

        float maxDuration = _type == KillType.Explosion ? duration + secondDelay : duration;
        if (elapsed < 0f || elapsed > maxDuration)
        {
            HideAll();
            if (elapsed > maxDuration) _start = -1f;
            return;
        }

        float t = Mathf.Clamp01(elapsed / duration);
        float eased = 1f - Mathf.Pow(1f - t, 3f);
        float alpha = 1f - t;
        alpha *= alpha;

        switch (_type)
        {
            case KillType.Headshot:
                DrawRing(0, center,
                    cfg.RingHeadshotRadius.Value, cfg.RingHeadshotThickness.Value,
                    UiTheme.Parse(cfg.RingHeadshotColor.Value, new Color(0.831f, 0.722f, 0f)),
                    eased, t, alpha);
                Hide(1);
                break;

            case KillType.Explosion:
                DrawRing(0, center,
                    cfg.RingHeadshotRadius.Value, cfg.RingHeadshotThickness.Value,
                    UiTheme.Parse(cfg.RingHeadshotColor.Value, new Color(0.831f, 0.722f, 0f)),
                    eased, t, alpha);

                float elapsed2 = elapsed - secondDelay;
                if (elapsed2 < 0f || elapsed2 > duration)
                {
                    Hide(1);
                    break;
                }

                float t2 = Mathf.Clamp01(elapsed2 / duration);
                float eased2 = 1f - Mathf.Pow(1f - t2, 3f);
                float alpha2 = 1f - t2;
                alpha2 *= alpha2;

                DrawRing(1, center,
                    cfg.RingExplosionRadius.Value, cfg.RingExplosionThickness.Value,
                    UiTheme.Parse(cfg.RingExplosionColor.Value, new Color(0.969f, 0.498f, 0f)),
                    eased2, t2, alpha2);
                break;

            case KillType.Crit:
                DrawRing(0, center,
                    cfg.RingCritRadius.Value, cfg.RingCritThickness.Value,
                    UiTheme.Parse(cfg.RingCritColor.Value, new Color(0.612f, 0.8f, 0.396f)),
                    eased, t, alpha);
                Hide(1);
                break;

            default:
                HideAll();
                break;
        }
    }

    private void DrawRing(int slot, Vector2 center, float maxRadius, float baseThickness, Color color, float eased, float t, float alpha)
    {
        var rt = _rt[slot];
        var img = _img[slot];
        var go = _go[slot];
        if (rt == null || img == null || go == null) return;

        if (alpha <= 0.001f || maxRadius <= 0.01f || baseThickness <= 0.01f)
        {
            Hide(slot);
            return;
        }

        // 与旧版一致：半径从 BASE/(BASE+GROWTH) 比例涨到满
        const float baseRatio = 10f / (10f + 32f);
        float minRadius = maxRadius * baseRatio;
        float radius = minRadius + (maxRadius - minRadius) * eased;

        float thickness = baseThickness * (1f - t);
        if (thickness <= 0.02f)
        {
            Hide(slot);
            return;
        }

        var sprite = Assets.IconBank.GetRing(thickness, radius);
        if (sprite == null)
        {
            Hide(slot);
            return;
        }

        // 纹理的外沿正好在 <paramref>radius + thickness/2</paramref> 处
        float outer = radius + thickness * 0.5f;
        float size = outer * 2f;

        if (!go.activeSelf) go.SetActive(true);
        if (img.sprite != sprite) img.sprite = sprite;
        img.color = UiTheme.WithAlpha(color, alpha);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = center;
    }

    private void Hide(int slot)
    {
        var go = _go[slot];
        var img = _img[slot];
        if (img != null) img.color = Color.clear;
        if (go != null && go.activeSelf) go.SetActive(false);
    }

    private void HideAll()
    {
        Hide(0);
        Hide(1);
    }
}
