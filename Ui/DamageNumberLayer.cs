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
/// 在旧行为之上做的改动（战地 6 的做法）：
/// <list type="bullet">
///   <item><b>深色描边</b>：旧默认是「白字 + 白色 25% 描边」，等于没有描边，亮背景上看不清；</item>
///   <item><b>爆头填充色</b>单独配置（旧项目爆头只改描边）；</item>
///   <item><b>击杀那一下变红并重新弹出</b>（<see cref="PromoteToKill"/>），战地里最后一击的数字就是红的。</item>
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
        internal float PopStart;
        internal float Life;
        internal float Width;
        internal float Damage;
        internal bool Headshot;
        internal bool Kill;
        internal float LastAlpha = -1f;
        internal float LastScale = float.NaN;
        internal float LastWidth = float.NaN;
        internal float LastHeight = float.NaN;
        internal Color LastColor = new Color(-1f, -1f, -1f, -1f);
        internal bool Logged;
    }

    private readonly List<Entry> _live = new List<Entry>(16);
    private readonly List<Entry> _pool = new List<Entry>(16);
    private static readonly Dictionary<int, string> TextCache = new Dictionary<int, string>(256);

    private RectTransform? _parent;
    private Vector2 _anchor;

    /// <summary>
    /// 击杀确认之后的这段时间里新出现的数字也算「最后一击」（命中事件可能比击杀事件晚几帧到）。
    /// 窗口要窄 —— 理由见 <c>HitmarkerLayer.KillStickWindow</c>。
    /// </summary>
    private const float KillWindow = 0.06f;
    private float _killWindowUntil = -1f;

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

        float now = UiClock.Now;
        e.Damage = damage;
        e.Headshot = headshot;
        e.Kill = false;
        e.Start = now;
        e.PopStart = now;
        e.Life = Mathf.Max(0.05f, Plugin.Cfg.DamageTextLifetime.Value);
        e.LastAlpha = -1f;
        e.LastScale = float.NaN;
        e.LastColor = new Color(-1f, -1f, -1f, -1f);
        e.Logged = false;

        // 击杀事件先到的情况：这个数字就是最后一击
        if (now <= _killWindowUntil)
        {
            e.Kill = true;
            e.Life = Mathf.Max(e.Life, 1.1f);
        }

        e.Text.text = text;
        e.Text.fontSize = Mathf.Max(6f, Plugin.Cfg.DamageTextSize.Value);

        // 描边：uGUI/TMP 下描边走 SDF，几乎零成本，不像旧项目那样需要 8 次额外 GUI.Label。
        var cfg = Plugin.Cfg;
        e.Text.outlineWidth = Mathf.Clamp(cfg.DamageTextOutlineWidth.Value, 0f, 1f);
        Color outline = headshot
            ? UiTheme.Parse(cfg.DamageTextHeadshotOutlineColor.Value, new Color(0f, 0f, 0f, 0.8f))
            : UiTheme.Parse(cfg.DamageTextOutlineColor.Value, new Color(0f, 0f, 0f, 0.8f));
        e.Text.outlineColor = outline;   // 隐式转 Color32，alpha 在这里体现

        // 量一次真实宽度，用于同一行内的排列
        e.Text.ForceMeshUpdate();
        e.Width = e.Text.preferredWidth > 1f
            ? e.Text.preferredWidth
            : text.Length * e.Text.fontSize * 0.6f;

        e.Go.SetActive(true);
        _live.Insert(0, e);   // 最新在最前，和旧项目一致
    }

    /// <summary>
    /// 击杀确认：最新那个数字变成击杀色并重新弹出，同时多留一会儿 ——
    /// 这是「把对手打死的那一下」，值得多看一眼。
    /// </summary>
    internal void PromoteToKill()
    {
        float now = UiClock.Now;
        _killWindowUntil = now + KillWindow;

        // 只升级「刚刚」出现的那个；更早的数字是上一枪的，不该跟着变红
        if (_live.Count == 0 || now - _live[0].Start > KillWindow) return;

        var e = _live[0];
        e.Kill = true;
        e.PopStart = now;
        e.Start = now;
        e.Life = Mathf.Max(e.Life, 1.1f);
        e.LastAlpha = -1f;
        e.LastScale = float.NaN;
        e.LastColor = new Color(-1f, -1f, -1f, -1f);
    }

    internal void Tick()
    {
        if (_live.Count == 0) return;

        var cfg = Plugin.Cfg;
        bool enabled = cfg.DamageTextEnabled.Value;
        float now = UiClock.Now;

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
        // 数字和命中图标共用同一个中心偏移，否则两者会差出偏移那么多像素
        Vector2 offset = HitmarkerLayer.CenterOffset(cfg);
        float startX = _anchor.x + offset.x + Mathf.Max(1f, cfg.HitBaseSize.Value) * 0.5f + padding;
        float baseY = _anchor.y + offset.y;

        // 弹出：用 localScale 而不是 sizeDelta，避免触发 TMP 重排（AGENTS.md §3.5）
        float popScale = Mathf.Max(1f, cfg.DamageTextPopScale.Value);
        float popDuration = Mathf.Max(0.01f, cfg.DamageTextPopDuration.Value);

        Color fill = UiTheme.Parse(cfg.DamageTextColor.Value, Color.white);
        Color headshotFill = UiTheme.Parse(cfg.DamageTextHeadshotColor.Value, fill);
        Color killFill = UiTheme.Parse(cfg.DamageTextKillColor.Value, new Color(1f, 0.29f, 0.24f));

        float x = startX;
        for (int i = 0; i < _live.Count; i++)
        {
            var e = _live[i];
            if (e.Rt == null || e.Text == null) continue;

            float alpha = Mathf.Clamp01(1f - (now - e.Start) / e.Life);
            // 击杀数字前 40% 保持全亮，别一出来就开始淡
            if (e.Kill) alpha = Mathf.Clamp01(1f - Mathf.Max(0f, (now - e.Start) / e.Life - 0.4f) / 0.6f);

            // NaN 做哨兵：Mathf.Abs(NaN - x) > eps 恒为 false，用 Abs 比较会让第一次永远写不进去 ——
            // sizeDelta 停在 TMP 默认的 200×50，左对齐的文字就从矩形左缘（= 命中图标左边）开始画了（实测）。
            if (float.IsNaN(e.LastWidth) || float.IsNaN(e.LastHeight)
                || Mathf.Abs(e.LastWidth - e.Width) > 0.01f || Mathf.Abs(e.LastHeight - lineHeight) > 0.01f)
            {
                e.LastWidth = e.Width;
                e.LastHeight = lineHeight;
                e.Rt.sizeDelta = new Vector2(e.Width, lineHeight);
            }
            e.Rt.anchoredPosition = new Vector2(x + e.Width * 0.5f, baseY);

            if (cfg.DebugEnabled.Value && !e.Logged)
            {
                e.Logged = true;
                Plugin.Logger.LogInfo(
                    $"[dmg] anchor={_anchor} startX={startX:0.#} x={x:0.#} width={e.Width:0.#} " +
                    $"anchored={e.Rt.anchoredPosition} screen={e.Rt.position} pivot={e.Rt.pivot} " +
                    $"size={e.Rt.sizeDelta} scale={e.Rt.localScale} align={e.Text.alignment}");
            }

            // 击杀的数字弹得更用力
            float entryPop = e.Kill ? Mathf.Max(popScale, 1.45f) : popScale;
            if (entryPop > 1.0001f)
            {
                float popT = UiAnim.OutBack(UiAnim.Clamp01((now - e.PopStart) / popDuration));
                float scale = entryPop + (1f - entryPop) * popT;
                if (float.IsNaN(e.LastScale) || Mathf.Abs(e.LastScale - scale) > 0.002f)
                {
                    e.LastScale = scale;
                    e.Rt.localScale = new Vector3(scale, scale, 1f);
                }
            }
            else if (float.IsNaN(e.LastScale))
            {
                e.LastScale = 1f;
                e.Rt.localScale = Vector3.one;
            }

            Color baseColor = e.Kill ? killFill : (e.Headshot ? headshotFill : fill);
            Color color = UiTheme.WithAlpha(baseColor, alpha * baseColor.a);
            if (e.LastAlpha < 0f
                || Mathf.Abs(alpha - e.LastAlpha) > 0.01f
                || Mathf.Abs(color.r - e.LastColor.r) > 0.004f
                || Mathf.Abs(color.g - e.LastColor.g) > 0.004f
                || Mathf.Abs(color.b - e.LastColor.b) > 0.004f)
            {
                e.LastAlpha = alpha;
                e.LastColor = color;
                e.Text.color = color;
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
            e.LastWidth = float.NaN;
            e.LastHeight = float.NaN;
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
