using System;
using System.Collections.Generic;
using AyaHitmarker.Game;
using TMPro;
using UnityEngine;

namespace AyaHitmarker.Ui;

/// <summary>
/// 伤害数字。两种排布样式，由 `2. 伤害数字 → 排布方式` 切换（0.6 b）：
///
/// <para><b>一行向右（LineRight，默认）</b>：逐行移植旧项目 <c>DamageIndicatorUI.DrawDamageNumbers</c> ——</para>
/// <list type="bullet">
///   <item>新命中插到列表**最前**，因此最新的数字紧贴命中图标右侧；</item>
///   <item>所有还活着的数字排成**一行**向右延伸，每个的 X = 上一个 X + 上一个宽度 + 间距；</item>
///   <item>没有上浮/飘移 —— 只有各自独立的 <c>1 - t</c> 线性淡出；</item>
///   <item>伤害 &lt; 0.5 的不画（护甲吸收完时不显示毫无意义的"0"）。</item>
/// </list>
///
/// <para><b>累加（Accumulate，战地 6 样式）</b>：同一目标在窗口内只显示一个滚动累计的数字
/// （<c>32 → 67 → 99</c>），击杀时变红；多个目标按垂直堆叠，最新在最上。
/// 合并/过期判定在纯逻辑 <see cref="DamageAccumModel"/> 里（可离线测），这里只做 uGUI 呈现。</para>
///
/// 通用改动（战地 6 的做法）：深色描边；爆头填充色单独配置；击杀那一下变红并重新弹出；
/// 打中护甲未穿透时用「护甲命中颜色」。
///
/// 宽度用 <c>TMP_Text.ForceMeshUpdate()</c> + <c>preferredWidth</c> 实测，而不是按字数估算，
/// 这样行内间距在任意字体下都对齐。所有写入都遵守 AGENTS.md §3.5（sizeDelta / fontSize 只在变化时写、
/// 动画只写 localScale、透明度脏检查）。
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
        internal bool ArmorOnly;
        internal bool Kill;
        internal string TargetKey = string.Empty;
        internal int LastTextKey = int.MinValue;
        internal float LastFontSize = -1f;
        internal int LastOutlineKey = int.MinValue;
        internal bool Used;
        internal float LastAlpha = -1f;
        internal float LastScale = float.NaN;
        internal float LastWidth = float.NaN;
        internal float LastHeight = float.NaN;
        internal Color LastColor = new Color(-1f, -1f, -1f, -1f);
        internal bool Logged;
    }

    private readonly List<Entry> _live = new List<Entry>(16);
    private readonly List<Entry> _pool = new List<Entry>(16);

    /// <summary>累加样式的视图条目；按 <see cref="Entry.TargetKey"/> 与模型槽一一对应。</summary>
    private readonly List<Entry> _accumEntries = new List<Entry>(8);

    private DamageAccumModel? _accumModel;
    private DamageNumberLayout? _mode;
    private Vector2 _accumAnchor;

    private static readonly Dictionary<int, string> TextCache = new Dictionary<int, string>(256);

    private RectTransform? _parent;
    private Vector2 _anchor;

    /// <summary>
    /// 击杀确认之后的这段时间里新出现的数字也算「最后一击」（命中事件可能比击杀事件晚几帧到）。
    /// 窗口要窄 —— 理由见 <c>HitmarkerLayer.KillStickWindow</c>。
    /// </summary>
    private const float KillWindow = 0.06f;
    private float _killWindowUntil = -1f;

    /// <summary>击杀事件先于命中数据到达时（模型里还没槽），记下 key 等窄窗口内补结算。</summary>
    private string? _pendingKillKey;
    private float _pendingKillUntil = -1f;

    internal void Attach(RectTransform parent) => _parent = parent;

    /// <summary><paramref name="canvasAnchor"/> 是画布局部坐标（原点 = 屏幕中心）。</summary>
    /// <param name="armorOnly">本次命中只有护甲伤害（0.6 a：用护甲配色）。</param>
    /// <param name="targetKey">目标标识（0.6 b 累加用）；空串时退回「一行向右」。</param>
    internal void Show(Vector2 canvasAnchor, float damage, bool headshot, bool armorOnly, string targetKey)
    {
        _anchor = canvasAnchor;

        if (Plugin.Cfg.DamageTextLayout.Value == DamageNumberLayout.Accumulate)
        {
            // 归组 key 理论上不会为空（ProfileId / 昵称兜底）；真为空时用一个固定 key，避免退回到不被绘制的行样式
            ShowAccumulated(canvasAnchor, damage, headshot, armorOnly, string.IsNullOrEmpty(targetKey) ? "unknown" : targetKey);
            return;
        }

        ShowLine(damage, headshot, armorOnly);
    }

    private void ShowLine(float damage, bool headshot, bool armorOnly)
    {
        var e = Rent();
        if (e?.Text == null || e.Rt == null || e.Go == null) return;

        string text = FormatDamage(damage);
        e.Damage = damage;
        e.Headshot = headshot;
        e.ArmorOnly = armorOnly;
        e.Kill = false;
        e.TargetKey = string.Empty;
        e.Start = UiClock.Now;
        e.PopStart = e.Start;
        e.Life = Mathf.Max(0.05f, Plugin.Cfg.DamageTextLifetime.Value);
        e.LastAlpha = -1f;
        e.LastScale = float.NaN;
        e.LastColor = new Color(-1f, -1f, -1f, -1f);
        e.Logged = false;

        // 击杀事件先到的情况：这个数字就是最后一击
        if (e.Start <= _killWindowUntil)
        {
            e.Kill = true;
            e.Life = Mathf.Max(e.Life, 1.1f);
        }

        e.Text.text = text;

        // 描边：uGUI/TMP 下描边走 SDF，几乎零成本，不像旧项目那样需要 8 次额外 GUI.Label。
        var cfg = Plugin.Cfg;
        e.Text.fontSize = Mathf.Max(6f, cfg.DamageTextSize.Value);
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

    private void ShowAccumulated(Vector2 canvasAnchor, float damage, bool headshot, bool armorOnly, string targetKey)
    {
        var cfg = Plugin.Cfg;
        var model = EnsureModel(cfg);
        float now = UiClock.Now;
        _accumAnchor = canvasAnchor;

        model.Add(new DamageAccumHit(targetKey, damage, headshot, armorOnly, now));

        // 击杀事件比这一击的命中数据早到时：模型里可能还没有槽，PromoteKill 会失败。
        // 这里用和一行样式一样的窄窗口补一次。
        if (_pendingKillKey != null && now <= _pendingKillUntil
            && string.Equals(_pendingKillKey, targetKey, StringComparison.Ordinal))
        {
            model.PromoteKill(targetKey, now);
            _pendingKillKey = null;
        }
    }

    /// <summary>
    /// 击杀确认：一行样式把最新那个数字变成击杀色并重新弹出；累加样式按目标 key 标红。
    /// </summary>
    internal void PromoteToKill(string targetKey)
    {
        float now = UiClock.Now;

        if (Plugin.Cfg.DamageTextLayout.Value == DamageNumberLayout.Accumulate)
        {
            _killWindowUntil = now + KillWindow;
            var model = EnsureModel(Plugin.Cfg);
            if (!model.PromoteKill(targetKey, now) && !string.IsNullOrEmpty(targetKey))
            {
                // 命中数据可能晚一两帧到，记下来等 ShowAccumulated 里补
                _pendingKillKey = targetKey;
                _pendingKillUntil = now + KillWindow;
            }
            return;
        }

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
        var cfg = Plugin.Cfg;
        var layout = cfg.DamageTextLayout.Value;

        // 运行中在设置页切换样式：清掉另一套视图，避免旧数字停在屏幕上
        if (_mode != layout)
        {
            _mode = layout;
            ClearAll();
        }

        if (layout == DamageNumberLayout.Accumulate)
        {
            if (_accumModel != null) TickAccumulated(cfg);
            return;
        }

        // 累加槽在切回一行样式时已经由 ClearAll 清掉了
        TickLine(cfg);
    }

    private void TickLine(AyaConfig cfg)
    {
        if (_live.Count == 0) return;

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
                Recycle(e);
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
        Color armorFill = UiTheme.Parse(cfg.DamageTextArmorColor.Value, new Color(0.62f, 0.78f, 0.92f));

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
                    $"size={e.Rt.sizeDelta} scale={e.Rt.localScale} align={e.Text.alignment} armor={e.ArmorOnly}");
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

            Color baseColor = e.Kill ? killFill
                : e.ArmorOnly ? armorFill
                : e.Headshot ? headshotFill
                : fill;
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

    /// <summary>
    /// 累加样式：模型槽 ↔ 视图条目按 TargetKey 对账，垂直堆叠（最新在最上），
    /// 最后再调 <see cref="DamageAccumModel.Tick"/> 清理过期槽（它同时会清 Changed ——
    /// 所以本帧的显影/弹出必须在它之前消费掉）。
    /// </summary>
    private void TickAccumulated(AyaConfig cfg)
    {
        var model = _accumModel;
        if (model == null) return;

        float now = UiClock.Now;
        bool enabled = cfg.DamageTextEnabled.Value;

        float lineHeight = Mathf.Max(8f, cfg.DamageTextSize.Value) * 1.4f;
        float padding = Mathf.Max(0f, cfg.DamageTextPadding.Value);
        Vector2 offset = HitmarkerLayer.CenterOffset(cfg);
        float startX = _accumAnchor.x + offset.x + Mathf.Max(1f, cfg.HitBaseSize.Value) * 0.5f + padding;
        float baseY = _accumAnchor.y + offset.y;
        float rowGap = Mathf.Max(1f, lineHeight * 0.12f);

        float popScale = Mathf.Max(1f, cfg.DamageTextPopScale.Value);
        float popDuration = Mathf.Max(0.01f, cfg.DamageTextPopDuration.Value);
        float fontSize = Mathf.Max(6f, cfg.DamageTextSize.Value);
        float outlineWidth = Mathf.Clamp(cfg.DamageTextOutlineWidth.Value, 0f, 1f);

        Color fill = UiTheme.Parse(cfg.DamageTextColor.Value, Color.white);
        Color headshotFill = UiTheme.Parse(cfg.DamageTextHeadshotColor.Value, fill);
        Color killFill = UiTheme.Parse(cfg.DamageTextKillColor.Value, new Color(1f, 0.29f, 0.24f));
        Color armorFill = UiTheme.Parse(cfg.DamageTextArmorColor.Value, new Color(0.62f, 0.78f, 0.92f));
        Color normalOutline = UiTheme.Parse(cfg.DamageTextOutlineColor.Value, new Color(0f, 0f, 0f, 0.8f));
        Color headshotOutline = UiTheme.Parse(cfg.DamageTextHeadshotOutlineColor.Value, new Color(0f, 0f, 0f, 0.8f));

        var slots = model.Slots;

        // 先给条目打 Used=false；下面按槽对账
        for (int i = 0; i < _accumEntries.Count; i++) _accumEntries[i].Used = false;

        int visible = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            DamageAccumSlot slot = slots[i];
            if (!enabled || slot.Damage < 0.5f) continue;

            var e = FindAccumEntry(slot.TargetKey);
            if (e == null)
            {
                e = Rent();
                if (e == null) continue;
                e.TargetKey = slot.TargetKey;
                e.LastTextKey = int.MinValue;
                e.LastFontSize = -1f;
                e.LastOutlineKey = int.MinValue;
                e.PopStart = now;
                e.Start = now;
                _accumEntries.Add(e);
            }

            var text = e.Text;
            var rt = e.Rt;
            var go = e.Go;
            if (text == null || rt == null || go == null) continue;
            if (!go.activeSelf) go.SetActive(true);

            e.Used = true;
            e.Damage = slot.Damage;
            e.Headshot = slot.Headshot;
            e.ArmorOnly = slot.ArmorOnly;
            e.Kill = slot.Kill;

            int textKey = Mathf.Max(0, Mathf.RoundToInt(slot.Damage));
            bool textChanged = textKey != e.LastTextKey;
            if (textChanged)
            {
                e.LastTextKey = textKey;
                text.text = FormatDamageByKey(textKey);
            }

            // 数字变过一次就弹一次（击杀也要重新弹）
            if (textChanged || slot.Changed)
            {
                e.PopStart = now;
                e.Start = now;
            }

            if (Mathf.Abs(e.LastFontSize - fontSize) > 0.01f)
            {
                e.LastFontSize = fontSize;
                text.fontSize = fontSize;
            }

            int outlineKey = slot.Headshot ? 1 : 0;
            if (e.LastOutlineKey != outlineKey)
            {
                e.LastOutlineKey = outlineKey;
                text.outlineWidth = outlineWidth;
                text.outlineColor = slot.Headshot ? headshotOutline : normalOutline;
            }

            if (textChanged || float.IsNaN(e.LastWidth))
            {
                text.ForceMeshUpdate();
                e.Width = text.preferredWidth > 1f
                    ? text.preferredWidth
                    : text.text.Length * fontSize * 0.6f;
            }

            float y = baseY - visible * (lineHeight + rowGap);
            visible++;

            if (float.IsNaN(e.LastWidth) || float.IsNaN(e.LastHeight)
                || Mathf.Abs(e.LastWidth - e.Width) > 0.01f || Mathf.Abs(e.LastHeight - lineHeight) > 0.01f)
            {
                e.LastWidth = e.Width;
                e.LastHeight = lineHeight;
                rt.sizeDelta = new Vector2(e.Width, lineHeight);
            }
            rt.anchoredPosition = new Vector2(startX + e.Width * 0.5f, y);

            // 击杀的数字弹得更用力
            float entryPop = slot.Kill ? Mathf.Max(popScale, 1.45f) : popScale;
            if (entryPop > 1.0001f)
            {
                float popT = UiAnim.OutBack(UiAnim.Clamp01((now - e.PopStart) / popDuration));
                float scale = entryPop + (1f - entryPop) * popT;
                if (float.IsNaN(e.LastScale) || Mathf.Abs(e.LastScale - scale) > 0.002f)
                {
                    e.LastScale = scale;
                    rt.localScale = new Vector3(scale, scale, 1f);
                }
            }
            else if (float.IsNaN(e.LastScale))
            {
                e.LastScale = 1f;
                rt.localScale = Vector3.one;
            }

            float alpha = DamageAccumModel.Alpha(slot, now, model.Window, model.Lifetime);
            Color baseColor = slot.Kill ? killFill
                : slot.ArmorOnly ? armorFill
                : slot.Headshot ? headshotFill
                : fill;
            Color color = UiTheme.WithAlpha(baseColor, alpha * baseColor.a);
            if (e.LastAlpha < 0f
                || Mathf.Abs(alpha - e.LastAlpha) > 0.01f
                || Mathf.Abs(color.r - e.LastColor.r) > 0.004f
                || Mathf.Abs(color.g - e.LastColor.g) > 0.004f
                || Mathf.Abs(color.b - e.LastColor.b) > 0.004f)
            {
                e.LastAlpha = alpha;
                e.LastColor = color;
                text.color = color;
            }

            if (cfg.DebugEnabled.Value && !e.Logged)
            {
                e.Logged = true;
                Plugin.Logger.LogInfo(
                    $"[dmg] 累加 key={slot.TargetKey} total={slot.Damage:0.#} hits={slot.HitCount} " +
                    $"kill={slot.Kill} head={slot.Headshot} armor={slot.ArmorOnly} " +
                    $"anchored={rt.anchoredPosition} size={rt.sizeDelta}");
            }
        }

        // 没对上槽的条目 → 隐藏回收（槽被 Tick 清掉后就会走到这里）
        for (int i = _accumEntries.Count - 1; i >= 0; i--)
        {
            var e = _accumEntries[i];
            if (e.Used) continue;
            e.Go?.SetActive(false);
            _accumEntries.RemoveAt(i);
            Recycle(e);
        }

        // 最后再清理过期槽 / 清 Changed（下一帧的 Changed 才能被完整消费）
        model.Tick(now);
    }

    private Entry? FindAccumEntry(string key)
    {
        for (int i = 0; i < _accumEntries.Count; i++)
        {
            if (!_accumEntries[i].Used && string.Equals(_accumEntries[i].TargetKey, key, StringComparison.Ordinal))
                return _accumEntries[i];
        }
        return null;
    }

    private DamageAccumModel EnsureModel(AyaConfig cfg)
    {
        float window = Mathf.Max(0.05f, cfg.DamageAccumulateWindow.Value);
        float lifetime = Mathf.Max(0.05f, cfg.DamageAccumulateLifetime.Value);

        if (_accumModel == null
            || Mathf.Abs(_accumModel.Window - window) > 1e-4f
            || Mathf.Abs(_accumModel.Lifetime - lifetime) > 1e-4f)
        {
            _accumModel = new DamageAccumModel(window, lifetime);
        }
        return _accumModel;
    }

    internal void Reset()
    {
        ClearAll();
        _killWindowUntil = -1f;
        _pendingKillKey = null;
        _pendingKillUntil = -1f;
        _mode = null;
    }

    private void ClearAll()
    {
        for (int i = 0; i < _live.Count; i++)
        {
            _live[i].Go?.SetActive(false);
            Recycle(_live[i]);
        }
        _live.Clear();

        for (int i = 0; i < _accumEntries.Count; i++)
        {
            _accumEntries[i].Go?.SetActive(false);
            Recycle(_accumEntries[i]);
        }
        _accumEntries.Clear();

        _accumModel?.Reset();
        _accumModel = null;
        _pendingKillKey = null;
    }

    private static string FormatDamage(float damage)
        => FormatDamageByKey(Mathf.Max(0, Mathf.RoundToInt(damage)));

    private static string FormatDamageByKey(int key)
    {
        if (TextCache.TryGetValue(key, out var cached)) return cached;
        string text = key.ToString();
        if (TextCache.Count > 2048) TextCache.Clear();
        TextCache[key] = text;
        return text;
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
            e.LastScale = float.NaN;
            e.LastAlpha = -1f;
            e.LastColor = new Color(-1f, -1f, -1f, -1f);
            e.LastTextKey = int.MinValue;
            e.LastFontSize = -1f;
            e.LastOutlineKey = int.MinValue;
            e.Logged = false;
            e.TargetKey = string.Empty;
            e.Used = false;
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

    private void Recycle(Entry e)
    {
        if (_pool.Count >= 64) return;
        e.TargetKey = string.Empty;
        e.Used = false;
        _pool.Add(e);
    }
}
