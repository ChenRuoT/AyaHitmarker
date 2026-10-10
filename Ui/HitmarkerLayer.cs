using UnityEngine;
using UnityEngine.UI;

namespace AyaHitmarker.Ui;

/// <summary>
/// 命中提示：一张 <c>Icons/hit.png</c>，命中瞬间以 <c>HitMarkerAnimationScale</c> 的比例出现，
/// 在 <c>HitDuration</c> 内放大到 1 并线性淡出（旧项目 <c>DamageIndicatorUI.DrawHitMarkerIcon</c> 的逻辑）。
///
/// uGUI 比起 IMGUI 的好处在这里很直接：位置/尺寸/透明度每帧写就是了，
/// 不需要 <c>GUIUtility.RotateAroundPivot</c>，也没有每帧 8 次 OnGUI 事件的开销。
/// </summary>
internal sealed class HitmarkerLayer
{
    private GameObject? _go;
    private Image? _img;
    private RectTransform? _rt;

    private float _start = -999f;
    private bool _shown;
    private Vector2 _anchor;

    internal void Attach(RectTransform parent)
    {
        _go = new GameObject("AyaHitmarker.Hit");
        _img = _go.AddComponent<Image>();
        _img.raycastTarget = false;
        _img.preserveAspect = true;
        _img.color = Color.clear;

        _rt = UiTheme.Rect(_go);
        if (_rt != null)
        {
            _rt.SetParent(parent, false);
            _rt.anchorMin = new Vector2(0.5f, 0.5f);
            _rt.anchorMax = new Vector2(0.5f, 0.5f);
            _rt.pivot = new Vector2(0.5f, 0.5f);
        }

        _go.SetActive(false);
    }

    /// <summary><paramref name="canvasAnchor"/> 是画布局部坐标（原点 = 屏幕中心）。</summary>
    internal void Show(Vector2 canvasAnchor)
    {
        _anchor = canvasAnchor;
        _start = Time.unscaledTime;
        _shown = true;
    }

    internal void Tick()
    {
        if (!_shown || _rt == null || _img == null || _go == null) return;

        var cfg = Plugin.Cfg;
        var sprite = Assets.IconBank.Hit;

        if (!cfg.HitMarkerEnabled.Value || sprite == null)
        {
            Hide();
            return;
        }

        float duration = Mathf.Max(0.02f, cfg.HitDuration.Value);
        float t = (Time.unscaledTime - _start) / duration;
        if (t < 0f || t >= 1f)
        {
            Hide();
            return;
        }

        if (!_go.activeSelf) _go.SetActive(true);

        float alpha = 1f - t;
        float scale = Mathf.Lerp(cfg.HitMarkerAnimationScale.Value, 1f, t);
        float size = Mathf.Max(1f, cfg.HitBaseSize.Value) * scale;

        if (_img.sprite != sprite) _img.sprite = sprite;
        _img.color = UiTheme.WithAlpha(UiTheme.Parse(cfg.HitMarkerColor.Value, Color.white), alpha);

        _rt.sizeDelta = new Vector2(size, size);
        _rt.anchoredPosition = _anchor + UiTheme.ParseOffset(cfg.HitMarkerCenterOffset.Value, Vector2.zero);
    }

    internal void Reset()
    {
        _start = -999f;
        Hide();
    }

    private void Hide()
    {
        if (!_shown) return;
        _shown = false;
        if (_img != null) _img.color = Color.clear;
        if (_go != null && _go.activeSelf) _go.SetActive(false);
    }
}
