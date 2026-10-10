using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace AyaHitmarker.Ui;

/// <summary>
/// 伤害数字。逐行移植旧项目 <c>DamageIndicatorUI.DrawDamageNumbers</c> 的布局逻辑：
///
/// <list type="bullet">
///   <item>新命中插到列表**最前**，因此最新的数字紧贴命中图标右侧；</item>
///   <item>所有还活着的数字排成**一行**向右延伸，每个的 X = 上一个 X + 上一个宽度 + 间距；</item>
///   <item>没有上浮/飘移 —— 只有各自独立的 <c>1 - t</c> 线性淡出；</item>
///   <item>伤害 &lt; 0.5 的不画（护甲吸收完时不显示毫无意义的"0"）。</item>
/// </list>
///
/// 宽度用 <c>TMP_Text.ForceMeshUpdate()</c> + <c>preferredWidth</c> 实测，而不是按字数估算，
/// 这样行内间距在任意字体下都对齐。
/// </summary>
internal sealed class DamageNumberLayer
{
    private sealed class Entry
    {
        internal GameObject? Go;
        internal TextMeshProUGUI? Text;
        internal RectTransform? Rt;
        internal float Start;
        internal float Life;
        internal float Width;
        internal float Damage;
        internal bool Headshot;
        internal float LastAlpha = -1f;
    }

    private readonly List<Entry> _live = new List<Entry>(16);
    private readonly List<Entry> _pool = new List<Entry>(16);
    private static readonly Dictionary<int, string> TextCache = new Dictionary<int, string>(256);

    private RectTransform? _parent;
    private Vector2 _anchor;

    internal void Attach(RectTransform parent) => _parent = parent;

    /// <summary><paramref name="canvasAnchor"/> 是画布局部坐标（原点 = 屏幕中心）。</summary>
    internal void Show(Vector2 canvasAnchor, float damage, bool headshot)
    {
        _anchor = canvasAnchor;

        var e = Rent();
        if (e?.Text == null || e.Rt == null || e.Go == null) return;

        int key = Mathf.Max(0, Mathf.RoundToInt(damage));
        string text = key.ToString();
        if (TextCache.TryGetValue(key, out var cached)) text = cached;
        else
        {
            if (TextCache.Count > 2048) TextCache.Clear();
            TextCache[key] = text;
        }

        e.Damage = damage;
        e.Headshot = headshot;
        e.Start = Time.unscaledTime;
        e.Life = Mathf.Max(0.05f, Plugin.Cfg.DamageTextLifetime.Value);
        e.LastAlpha = -1f;

        e.Text.text = text;
        e.Text.fontSize = Mathf.Max(6f, Plugin.Cfg.DamageTextSize.Value);
        // 量一次真实宽度，用于同一行内的排列
        e.Text.ForceMeshUpdate();
        e.Width = e.Text.preferredWidth > 1f
            ? e.Text.preferredWidth
            : text.Length * e.Text.fontSize * 0.6f;

        e.Go.SetActive(true);
        _live.Insert(0, e);   // 最新在最前，和旧项目一致
    }

    internal void Tick()
    {
        if (_live.Count == 0) return;

        var cfg = Plugin.Cfg;
        bool enabled = cfg.DamageTextEnabled.Value;
        float now = Time.unscaledTime;

        // 手写倒序 for，不用 RemoveAll(lambda)（后者每帧分配闭包）
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            var e = _live[i];
            float age = now - e.Start;

            if (!enabled || age < 0f || age >= e.Life || e.Damage < 0.5f)
            {
                e.Go?.SetActive(false);
                _live.RemoveAt(i);
                if (_pool.Count < 64) _pool.Add(e);
            }
        }

        if (_live.Count == 0) return;

        // ---- 行内布局：从命中图标右侧开始向右排列 ----
        float lineHeight = Mathf.Max(8f, cfg.DamageTextSize.Value) * 1.4f;
        float padding = Mathf.Max(0f, cfg.DamageTextPadding.Value);
        float spacing = Mathf.Clamp(cfg.DamageMultiTextPadding.Value, 0f, 200f);
        float startX = _anchor.x + Mathf.Max(1f, cfg.HitBaseSize.Value) * 0.5f + padding;

        float x = startX;
        for (int i = 0; i < _live.Count; i++)
        {
            var e = _live[i];
            if (e.Rt == null || e.Text == null) continue;

            float alpha = Mathf.Clamp01(1f - (now - e.Start) / e.Life);

            e.Rt.sizeDelta = new Vector2(e.Width, lineHeight);
            e.Rt.anchoredPosition = new Vector2(x + e.Width * 0.5f, _anchor.y);

            if (e.LastAlpha < 0f || Mathf.Abs(alpha - e.LastAlpha) > 0.01f)
            {
                e.LastAlpha = alpha;
                Color color = e.Headshot
                    ? UiTheme.Parse(cfg.DamageTextHeadshotColor.Value, new Color(1f, 0.79f, 0.30f))
                    : UiTheme.Parse(cfg.DamageTextColor.Value, Color.white);
                e.Text.color = UiTheme.WithAlpha(color, alpha);
            }

            x += e.Width + spacing;
        }
    }

    internal void Reset()
    {
        for (int i = 0; i < _live.Count; i++)
        {
            var e = _live[i];
            e.Go?.SetActive(false);
            if (_pool.Count < 64) _pool.Add(e);
        }
        _live.Clear();
    }

    private Entry? Rent()
    {
        Entry e;
        if (_pool.Count > 0)
        {
            e = _pool[_pool.Count - 1];
            _pool.RemoveAt(_pool.Count - 1);
            return e;
        }

        if (_parent == null) return null;

        e = new Entry();
        var text = UiTheme.CreateTextObject("AyaHitmarker.Damage", _parent, TextAlignmentOptions.Left);
        text.fontStyle = FontStyles.Bold;
        text.outlineWidth = 0.15f;
        UiTheme.EnableTextOutline(text);

        var go = text.gameObject;
        var rt = UiTheme.Rect(go);

        e.Go = go;
        e.Text = text;
        e.Rt = rt;
        go.SetActive(false);
        return e;
    }
}
