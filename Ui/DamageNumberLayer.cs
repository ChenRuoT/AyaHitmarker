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
/// 通用改动（战地 6 的做法）：深色描边；爆头填充色单独配置；击杀那一下变红并重新弹出。
///
/// <para><b>护甲伤害（0.6.1）</b>：一次命中带护甲耐久伤害时，它是**另起一行**画在身体伤害**上方**的（护甲配色 + 右侧小盾标），
/// 身体伤害照常显示。被挡下且钝伤为 0 的命中没有身体数字，护甲数字就占身体数字的位置。
/// 取值模式 = Total 时回到旧行为：一个数字显示身体 + 护甲，被挡下时整个数字用护甲样式。</para>
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
        /// <summary>这行数字用护甲配色 + 右侧小盾标（护甲行，或旧「总和」模式下被挡下的命中）。</summary>
        internal bool ArmorStyle;
        /// <summary>护甲耐久伤害那一行（累加样式下与身体行分属两个条目）。</summary>
        internal bool IsArmorLine;
        /// <summary>一行向右样式：叠在紧邻的前一个条目（同一次命中的身体数字）上方，不占行内横向空间。</summary>
        internal bool Above;
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

        // 护甲命中：伤害数字右侧的小盾标（0.6 a 调整；盾不再替换命中标记）
        internal UiImageRow? Shield;
        internal Sprite? LastShieldSprite;
        internal float LastShieldSize = float.NaN;
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

    /// <summary>护甲小盾标与数字之间的间距。</summary>
    private const float ShieldGap = 4f;

    /// <summary>小盾标的显示尺寸：跟着伤害字号缩放（21px 字号 → 约 18px），比命中标记小得多。</summary>
    private static float ShieldSizeFor(float fontSize) => Mathf.Clamp(fontSize * 0.85f, 12f, 26f);

    private static float ShieldOutlineFor(float size) => Mathf.Clamp(size * 0.06f, 0.4f, 1.0f);

    /// <summary>启动时把护甲小盾标烘好（主线程同步烘焙，别留到第一次护甲命中）。</summary>
    internal void Warmup()
    {
        float size = ShieldSizeFor(Mathf.Max(6f, Plugin.Cfg.DamageTextSize.Value));
        Assets.ProceduralIcons.ArmorMarker(size, ShieldOutlineFor(size));
    }

    /// <summary><paramref name="canvasAnchor"/> 是画布局部坐标（原点 = 屏幕中心）。</summary>
    /// <param name="damage">主数字（身体伤害；&lt; 0.5 不画）。</param>
    /// <param name="armorDamage">护甲耐久伤害，另起一行画在主数字上方（0.6.1）；&lt; 0.5 不画。</param>
    /// <param name="armorStyle">主数字也用护甲样式（旧「总和」模式下被挡下的命中）。</param>
    /// <param name="targetKey">目标标识（0.6 b 累加用）；空串时退回「一行向右」。</param>
    internal void Show(Vector2 canvasAnchor, float damage, float armorDamage, bool headshot, bool armorStyle, string targetKey)
    {
        _anchor = canvasAnchor;

        if (Plugin.Cfg.DamageTextLayout.Value == DamageNumberLayout.Accumulate)
        {
            // 归组 key 理论上不会为空（ProfileId / 昵称兜底）；真为空时用一个固定 key，避免退回到不被绘制的行样式
            ShowAccumulated(canvasAnchor, damage, armorDamage, headshot, armorStyle, string.IsNullOrEmpty(targetKey) ? "unknown" : targetKey);
            return;
        }

        // 先插护甲行、再插身体行：列表最前 = 最新，所以身体行在前、护甲行紧随其后（布局时叠在它上方）
        bool hasBody = damage >= 0.5f;
        if (armorDamage >= 0.5f) ShowLine(armorDamage, false, armorLine: true, above: hasBody);
        if (hasBody) ShowLine(damage, headshot, armorLine: false, above: false, armorStyle: armorStyle);
    }

    private void ShowLine(float damage, bool headshot, bool armorLine, bool above, bool armorStyle = false)
    {
        var e = Rent();
        if (e?.Text == null || e.Rt == null || e.Go == null) return;

        string text = FormatDamage(damage);
        e.Damage = damage;
        e.Headshot = headshot;
        e.IsArmorLine = armorLine;
        e.Above = above;
        e.ArmorStyle = armorLine || armorStyle;
        e.Kill = false;
        e.TargetKey = string.Empty;
        e.Start = UiClock.Now;
        e.PopStart = e.Start;
        e.Life = Mathf.Max(0.05f, Plugin.Cfg.DamageTextLifetime.Value);
        e.LastAlpha = -1f;
        e.LastScale = float.NaN;
        e.LastColor = new Color(-1f, -1f, -1f, -1f);
        e.Logged = false;

        // 击杀事件先到的情况：这个数字就是最后一击（护甲行不变红）
        if (!armorLine && e.Start <= _killWindowUntil)
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

    private void ShowAccumulated(Vector2 canvasAnchor, float damage, float armorDamage, bool headshot, bool armorStyle, string targetKey)
    {
        var cfg = Plugin.Cfg;
        var model = EnsureModel(cfg);
        float now = UiClock.Now;
        _accumAnchor = canvasAnchor;

        model.Add(new DamageAccumHit(targetKey, damage, armorDamage, headshot, armorStyle, now));

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
                HideShield(e);
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
        float prevLeft = startX;   // 上一个非叠放条目的左缘：护甲行叠在它上方
        float shieldBase = ShieldSizeFor(cfg.DamageTextSize.Value);
        for (int i = 0; i < _live.Count; i++)
        {
            var e = _live[i];
            if (e.Rt == null || e.Text == null) continue;

            float shieldSize = e.ArmorStyle ? shieldBase : 0f;
            float shieldGap = e.ArmorStyle ? ShieldGap : 0f;

            // 护甲行叠在紧邻的前一个条目（同一次命中的身体数字）上方：同一个左缘、抬高一行、不占横向空间
            bool above = e.Above && i > 0 && !_live[i - 1].Above;
            float left = above ? prevLeft : x;
            float y = above ? baseY + lineHeight : baseY;

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
            e.Rt.anchoredPosition = new Vector2(left + e.Width * 0.5f, y);

            if (cfg.DebugEnabled.Value && !e.Logged)
            {
                e.Logged = true;
                Plugin.Logger.LogInfo(
                    $"[dmg] anchor={_anchor} startX={startX:0.#} x={left:0.#} width={e.Width:0.#} " +
                    $"anchored={e.Rt.anchoredPosition} screen={e.Rt.position} pivot={e.Rt.pivot} " +
                    $"size={e.Rt.sizeDelta} scale={e.Rt.localScale} align={e.Text.alignment} armorLine={e.IsArmorLine} above={above}");
            }

            // 击杀的数字弹得更用力
            float entryScale = 1f;
            float entryPop = e.Kill ? Mathf.Max(popScale, 1.45f) : popScale;
            if (entryPop > 1.0001f)
            {
                float popT = UiAnim.OutBack(UiAnim.Clamp01((now - e.PopStart) / popDuration));
                entryScale = entryPop + (1f - entryPop) * popT;
                if (float.IsNaN(e.LastScale) || Mathf.Abs(e.LastScale - entryScale) > 0.002f)
                {
                    e.LastScale = entryScale;
                    e.Rt.localScale = new Vector3(entryScale, entryScale, 1f);
                }
            }
            else if (float.IsNaN(e.LastScale))
            {
                e.LastScale = 1f;
                e.Rt.localScale = Vector3.one;
            }

            Color baseColor = e.Kill ? killFill
                : e.ArmorStyle ? armorFill
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

            // 护甲行：小盾标贴在数字右侧，不遮命中标记也不遮下一个数字
            if (e.ArmorStyle) DrawShield(e, left, e.Width, y, shieldSize, armorFill, alpha, entryScale);
            else HideShield(e);

            // 叠放的护甲行不占横向空间（它的宽度不该把后面的数字推开）
            // —— 但这一列的宽度要取身体数字与护甲行（数字 + 盾标）里更宽的那个，否则连发时相邻两列的护甲行会叠在一起
            if (!above)
            {
                prevLeft = left;
                float footprint = e.Width + shieldGap + shieldSize;
                if (i + 1 < _live.Count && _live[i + 1].Above)
                    footprint = Mathf.Max(footprint, _live[i + 1].Width + ShieldGap + shieldBase);
                x += footprint + spacing;
            }
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

        var style = new AccumStyle
        {
            Cfg = cfg,
            Now = now,
            Model = model,
            StartX = startX,
            LineHeight = lineHeight,
            PopScale = popScale,
            PopDuration = popDuration,
            FontSize = fontSize,
            OutlineWidth = outlineWidth,
            Fill = fill,
            HeadshotFill = headshotFill,
            KillFill = killFill,
            ArmorFill = armorFill,
            NormalOutline = normalOutline,
            HeadshotOutline = headshotOutline,
        };

        // 行布局（0.6.1）：一个槽最多两行 —— 护甲耐久行在上、身体行在下。
        // 最新槽的身体行固定在 baseY（和命中图标对齐），护甲行往上长；更旧的槽依次往下排，
        // 带护甲行的槽要多留一行。只有护甲行（被挡下且钝伤为 0）时，它占身体行的位置。
        float rowH = lineHeight + rowGap;
        float nextRowY = baseY;
        bool first = true;
        for (int i = 0; i < slots.Count; i++)
        {
            DamageAccumSlot slot = slots[i];
            bool hasBody = enabled && slot.Damage >= 0.5f;
            bool hasArmor = enabled && slot.ArmorDamage >= 0.5f;
            if (!hasBody && !hasArmor) continue;

            float bodyY, armorY;
            if (hasBody && hasArmor)
            {
                if (first) { bodyY = nextRowY; armorY = bodyY + rowH; }
                else { armorY = nextRowY; bodyY = armorY - rowH; }
                nextRowY = bodyY - rowH;
            }
            else
            {
                bodyY = armorY = nextRowY;
                nextRowY -= rowH;
            }
            first = false;

            if (hasBody) DrawAccumLine(in style, slot, false, slot.Damage, bodyY);
            if (hasArmor) DrawAccumLine(in style, slot, true, slot.ArmorDamage, armorY);
        }

        // 没对上槽的条目 → 隐藏回收（槽被 Tick 清掉后就会走到这里）
        for (int i = _accumEntries.Count - 1; i >= 0; i--)
        {
            var e = _accumEntries[i];
            if (e.Used) continue;
            e.Go?.SetActive(false);
            HideShield(e);
            _accumEntries.RemoveAt(i);
            Recycle(e);
        }

        // 最后再清理过期槽 / 清 Changed（下一帧的 Changed 才能被完整消费）
        model.Tick(now);
    }

    /// <summary>累加样式一帧内共用的参数，避免 DrawAccumLine 的参数列表失控。</summary>
    private struct AccumStyle
    {
        internal AyaConfig Cfg;
        internal float Now;
        internal DamageAccumModel Model;
        internal float StartX;
        internal float LineHeight;
        internal float PopScale;
        internal float PopDuration;
        internal float FontSize;
        internal float OutlineWidth;
        internal Color Fill, HeadshotFill, KillFill, ArmorFill, NormalOutline, HeadshotOutline;
    }

    /// <summary>画槽里的一行（身体行或护甲行）：按 (key, 行种类) 找条目、没有就租一个，写入走脏检查。</summary>
    private void DrawAccumLine(in AccumStyle st, DamageAccumSlot slot, bool armorLine, float value, float y)
    {
        var e = FindAccumEntry(slot.TargetKey, armorLine);
        if (e == null)
        {
            e = Rent();
            if (e == null) return;
            e.TargetKey = slot.TargetKey;
            e.IsArmorLine = armorLine;
            e.LastTextKey = int.MinValue;
            e.LastFontSize = -1f;
            e.LastOutlineKey = int.MinValue;
            e.PopStart = st.Now;
            e.Start = st.Now;
            _accumEntries.Add(e);
        }

        var text = e.Text;
        var rt = e.Rt;
        var go = e.Go;
        if (text == null || rt == null || go == null) return;
        if (!go.activeSelf) go.SetActive(true);

        // 护甲行永远是护甲样式（带盾标，不跟着击杀变红 / 爆头变色）；身体行只有旧「总和」模式才可能是护甲样式
        bool kill = !armorLine && slot.Kill;
        bool headshot = !armorLine && slot.Headshot;
        bool armorStyle = armorLine || slot.ArmorStyle;

        e.Used = true;
        e.Damage = value;
        e.Headshot = headshot;
        e.ArmorStyle = armorStyle;
        e.Kill = kill;

        int textKey = Mathf.Max(0, Mathf.RoundToInt(value));
        bool textChanged = textKey != e.LastTextKey;
        if (textChanged)
        {
            e.LastTextKey = textKey;
            text.text = FormatDamageByKey(textKey);
        }

        // 数字变过一次就弹一次（击杀也要重新弹）
        if (textChanged || slot.Changed)
        {
            e.PopStart = st.Now;
            e.Start = st.Now;
        }

        if (Mathf.Abs(e.LastFontSize - st.FontSize) > 0.01f)
        {
            e.LastFontSize = st.FontSize;
            text.fontSize = st.FontSize;
        }

        int outlineKey = headshot ? 1 : 0;
        if (e.LastOutlineKey != outlineKey)
        {
            e.LastOutlineKey = outlineKey;
            text.outlineWidth = st.OutlineWidth;
            text.outlineColor = headshot ? st.HeadshotOutline : st.NormalOutline;
        }

        if (textChanged || float.IsNaN(e.LastWidth))
        {
            text.ForceMeshUpdate();
            e.Width = text.preferredWidth > 1f
                ? text.preferredWidth
                : text.text.Length * st.FontSize * 0.6f;
        }

        if (float.IsNaN(e.LastWidth) || float.IsNaN(e.LastHeight)
            || Mathf.Abs(e.LastWidth - e.Width) > 0.01f || Mathf.Abs(e.LastHeight - st.LineHeight) > 0.01f)
        {
            e.LastWidth = e.Width;
            e.LastHeight = st.LineHeight;
            rt.sizeDelta = new Vector2(e.Width, st.LineHeight);
        }
        rt.anchoredPosition = new Vector2(st.StartX + e.Width * 0.5f, y);

        // 击杀的数字弹得更用力
        float entryPop = kill ? Mathf.Max(st.PopScale, 1.45f) : st.PopScale;
        if (entryPop > 1.0001f)
        {
            float popT = UiAnim.OutBack(UiAnim.Clamp01((st.Now - e.PopStart) / st.PopDuration));
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

        float alpha = DamageAccumModel.Alpha(slot, st.Now, st.Model.Window, st.Model.Lifetime);
        Color baseColor = kill ? st.KillFill
            : armorStyle ? st.ArmorFill
            : headshot ? st.HeadshotFill
            : st.Fill;
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

        // 护甲样式：小盾标贴在数字右侧
        if (armorStyle)
        {
            float scaleNow = float.IsNaN(e.LastScale) ? 1f : e.LastScale;
            DrawShield(e, st.StartX, e.Width, y, ShieldSizeFor(st.FontSize), st.ArmorFill, alpha, scaleNow);
        }
        else
        {
            HideShield(e);
        }

        if (st.Cfg.DebugEnabled.Value && !e.Logged)
        {
            e.Logged = true;
            string lineName = armorLine ? "armor" : "body";
            Plugin.Logger.LogInfo(
                $"[dmg] 累加 key={slot.TargetKey} line={lineName} value={value:0.#} hits={slot.HitCount} " +
                $"kill={slot.Kill} head={slot.Headshot} anchored={rt.anchoredPosition} size={rt.sizeDelta}");
        }
    }

    private Entry? FindAccumEntry(string key, bool armorLine)
    {
        for (int i = 0; i < _accumEntries.Count; i++)
        {
            var c = _accumEntries[i];
            if (!c.Used && c.IsArmorLine == armorLine && string.Equals(c.TargetKey, key, StringComparison.Ordinal))
                return c;
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
            HideShield(_live[i]);
            Recycle(_live[i]);
        }
        _live.Clear();

        for (int i = 0; i < _accumEntries.Count; i++)
        {
            _accumEntries[i].Go?.SetActive(false);
            HideShield(_accumEntries[i]);
            Recycle(_accumEntries[i]);
        }
        _accumEntries.Clear();

        _accumModel?.Reset();
        _accumModel = null;
        _pendingKillKey = null;
    }

    private static string FormatDamage(float damage)
        => FormatDamageByKey(Mathf.Max(0, Mathf.RoundToInt(damage)));

    /// <summary>取（并按尺寸缓存）小盾标精灵；尺寸随字号变，变了一次才重取。</summary>
    private Sprite? ResolveShield(Entry e, float size)
    {
        var cached = e.LastShieldSprite;
        if (cached != null && Mathf.Abs(e.LastShieldSize - size) < 0.01f) return cached;

        var baked = Assets.ProceduralIcons.ArmorMarker(size, ShieldOutlineFor(size));
        e.LastShieldSize = size;
        e.LastShieldSprite = baked.HasValue ? baked.Value.Sprite : null;
        return e.LastShieldSprite;
    }

    /// <summary>把盾标画在数字右侧。<paramref name="left"/> = 数字矩形左缘（数字是左对齐的）。</summary>
    private void DrawShield(Entry e, float left, float numberWidth, float y, float size, Color tint, float alpha, float scale)
    {
        if (e.Shield == null) return;
        if (alpha <= 0.01f)
        {
            e.Shield.Hide();
            return;
        }

        var sprite = ResolveShield(e, size);
        if (sprite == null)
        {
            e.Shield.Hide();
            return;
        }

        Vector2 center = new Vector2(left + numberWidth + ShieldGap + size * 0.5f, y);
        e.Shield.Apply(center, new Vector2(size, size), sprite, UiTheme.WithAlpha(tint, alpha * tint.a), scale);
    }

    private static void HideShield(Entry e) => e.Shield?.Hide();

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
            e.LastShieldSprite = null;
            e.LastShieldSize = float.NaN;
            e.Logged = false;
            e.TargetKey = string.Empty;
            e.IsArmorLine = false;
            e.Above = false;
            e.ArmorStyle = false;
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
        // 盾标建在数字之后（更靠上），但它在数字右侧、不会遮挡文字
        e.Shield = UiImageRow.Create("AyaHitmarker.DamageShield", _parent);
        go.SetActive(false);
        return e;
    }

    private void Recycle(Entry e)
    {
        if (_pool.Count >= 64) return;
        HideShield(e);
        e.TargetKey = string.Empty;
        e.Used = false;
        _pool.Add(e);
    }
}
