using System.Collections.Generic;
using AyaHitmarker.Game;
using TMPro;
using UnityEngine;

namespace AyaHitmarker.Ui;

/// <summary>
/// 0.7.0 战局小结：回到主菜单后，在屏幕上显示几秒的一张统计小卡。
///
/// 版式：深色半透明底 + 左侧浅钢色强调条；标题一行，下面是「左标签 / 右数值」的统计行，
/// 有成就时再加一道分隔线和最多 4 行「档次色条 + 成就名 / ×次数」。
/// 整块面板是一个带 <c>CanvasGroup</c> 的容器（和 <c>HitDirectionLayer</c> 的簇同一个写法），
/// 淡入淡出只写一次 <c>CanvasGroup.alpha</c>，子行都不带 CanvasGroup；所有位置 / 尺寸 / 文字在 <see cref="Show"/> 里写一次，
/// 之后逐帧只有 <see cref="Tick"/> 里那一次透明度写入（AGENTS.md §3.5）。
///
/// 什么时候显示由 <see cref="SummaryScheduler"/> 决定，本类只管画。
/// 行对象按需创建、之后复用：战局小结一局最多弹一次，不必在启动时预建几十个 TMP 文本。
/// </summary>
internal sealed class RaidSummaryLayer
{
    private const int MaxAchievementRows = 4;
    private const float FadeIn = 0.35f;
    private const float FadeOut = 0.6f;

    private RectTransform? _parent;
    private UiImageRow? _panel;
    private UiImageRow? _accent;
    private UiImageRow? _titleLine;
    private UiTextRow? _title;

    private readonly List<UiTextRow> _labels = new List<UiTextRow>(16);
    private readonly List<UiTextRow> _values = new List<UiTextRow>(16);
    private readonly List<UiImageRow> _bars = new List<UiImageRow>(MaxAchievementRows + 1);

    private bool _active;
    private float _born;
    private float _duration;

    internal void Attach(RectTransform parent)
    {
        _parent = parent;
        _panel = UiImageRow.Create("AyaHitmarker.RaidSummary", parent);
        _panel.Hide();
        _accent = UiImageRow.Create("AyaHitmarker.RaidSummaryAccent", _panel.Rt, withGroup: false);
        _titleLine = UiImageRow.Create("AyaHitmarker.RaidSummaryTitleLine", _panel.Rt, withGroup: false);
        _title = UiTextRow.Create("AyaHitmarker.RaidSummaryTitle", _panel.Rt, TextAlignmentOptions.Left, withGroup: false);
        HideAll();
    }

    /// <summary>没有需要预热的资源（纯白精灵 + TMP 都已就绪）。</summary>
    internal void Warmup() { }

    /// <summary>当前是否正在显示（调试 / 测试用）。</summary>
    internal bool IsActive => _active;

    /// <summary>显示一份小结，重新计时。</summary>
    internal void Show(RaidSummary summary)
    {
        var cfg = Plugin.Cfg;
        if (_panel == null || _accent == null || _titleLine == null || _title == null || _parent == null) return;

        var rows = RaidStats.Rows(summary, MaxAchievementRows);

        int font = Mathf.Clamp(cfg.RaidSummaryFontSize.Value, 10, 48);
        int titleFont = font + 4;
        float rowH = font * 1.55f;
        float pad = font * 0.9f;
        float accentW = 4f;
        float width = font * 16f;
        float titleH = titleFont * 1.7f;
        float gap = font * 0.35f;
        float dividerH = rowH * 0.55f;

        // ---- 先量总高度 ----
        float contentH = 0f;
        foreach (var r in rows) contentH += r.Kind == SummaryRowKind.Divider ? dividerH : rowH;
        float height = pad + titleH + gap + contentH + pad;

        Color textDim = UiTheme.Parse(cfg.ColorKillDetails.Value, new Color(0.83f, 0.85f, 0.88f));
        Color textBright = KillCardPalette.IceWhite;
        Color steel = KillCardPalette.BannerFill;

        Vector2 center = new Vector2(cfg.RaidSummaryOffsetX.Value, cfg.RaidSummaryOffsetY.Value);
        _panel.Apply(center, new Vector2(width, height), UiTheme.WhiteSprite, new Color(0.055f, 0.075f, 0.095f, 0.86f));

        // 以下全部是面板局部坐标：原点 = 面板中心，Y 向上
        float left = -width * 0.5f;
        float right = width * 0.5f;
        float top = height * 0.5f;

        _accent.Apply(new Vector2(left + accentW * 0.5f, 0f), new Vector2(accentW, height), UiTheme.WhiteSprite, steel);

        float y = top - pad - titleH * 0.5f;
        _title.Apply(new Vector2(left + accentW + pad, y), new Vector2(width - accentW - pad * 2f, titleH),
            RaidStats.Title(), titleFont, steel, FontStyles.Bold, TextAlignmentOptions.Left);
        float lineY = top - pad - titleH + 1f;
        // 内容区在强调条右边，所以水平中心右移半个强调条宽度
        _titleLine.Apply(new Vector2(accentW * 0.5f, lineY), new Vector2(width - accentW - pad * 2f, 1.5f), UiTheme.WhiteSprite,
            new Color(steel.r, steel.g, steel.b, 0.35f));

        float cursor = top - pad - titleH - gap;
        int labelIndex = 0;
        int barIndex = 0;
        int achievementIndex = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];

            if (row.Kind == SummaryRowKind.Divider)
            {
                var bar = RentBar(barIndex++);
                bar?.Apply(new Vector2(accentW * 0.5f, cursor - dividerH * 0.5f),
                    new Vector2(width - accentW - pad * 2f, 1f), UiTheme.WhiteSprite, new Color(1f, 1f, 1f, 0.18f));
                cursor -= dividerH;
                continue;
            }

            float cy = cursor - rowH * 0.5f;
            bool achievement = row.Kind == SummaryRowKind.Achievement;
            float labelX = left + accentW + pad + (achievement ? font * 0.8f : 0f);

            var label = RentText(_labels, labelIndex, TextAlignmentOptions.Left, "Label");
            var value = RentText(_values, labelIndex, TextAlignmentOptions.Right, "Value");
            labelIndex++;

            label?.Apply(new Vector2(labelX, cy), new Vector2(width * 0.62f, rowH),
                row.Label, font, achievement ? textBright : textDim, achievement ? FontStyles.Bold : FontStyles.Normal,
                TextAlignmentOptions.Left);
            value?.Apply(new Vector2(right - pad, cy), new Vector2(width * 0.34f, rowH),
                row.Value, font, achievement ? steel : textBright, FontStyles.Bold, TextAlignmentOptions.Right);

            if (achievement)
            {
                // 档次色条：与击杀卡片横幅的强调条同一套四档颜色（成就行与 summary.Achievements 一一对应）
                var tier = achievementIndex < summary.Achievements.Length
                    ? summary.Achievements[achievementIndex].Tier
                    : KillAchievementTier.Normal;
                achievementIndex++;
                var bar = RentBar(barIndex++);
                bar?.Apply(new Vector2(left + accentW + pad + font * 0.25f, cy), new Vector2(3f, rowH * 0.6f),
                    UiTheme.WhiteSprite, TierColor(cfg, tier));
            }

            cursor -= rowH;
        }

        // 多出来的行（上一次显示留下的）收起来
        for (int i = labelIndex; i < _labels.Count; i++) { _labels[i].Hide(); _values[i].Hide(); }
        for (int i = barIndex; i < _bars.Count; i++) _bars[i].Hide();

        _duration = Mathf.Max(1f, cfg.RaidSummaryDuration.Value);
        _born = UiClock.Now;
        _active = true;
        _panel.SetFade(0f);

        if (cfg.DebugEnabled.Value)
        {
            Plugin.Logger.LogInfo(
                $"[summary] 显示: kills={summary.Kills} 爆头击杀={summary.HeadshotKills} 行数={rows.Count} " +
                $"面板={width:0}x{height:0} 位置=({center.x:0},{center.y:0}) 时长={_duration:0.#}s");
        }
    }

    private static Color TierColor(AyaConfig cfg, KillAchievementTier tier) => tier switch
    {
        KillAchievementTier.Epic => UiTheme.Parse(cfg.ColorAchievementEpic.Value, new Color(0.64f, 0.14f, 0.20f)),
        KillAchievementTier.Great => UiTheme.Parse(cfg.ColorAchievementGreat.Value, new Color(0.76f, 0.41f, 0.10f)),
        KillAchievementTier.Good => UiTheme.Parse(cfg.ColorAchievementGood.Value, new Color(0.18f, 0.44f, 0.71f)),
        _ => UiTheme.Parse(cfg.ColorAchievementNormal.Value, new Color(0.27f, 0.31f, 0.36f)),
    };

    private UiTextRow? RentText(List<UiTextRow> pool, int index, TextAlignmentOptions align, string kind)
    {
        if (_panel == null) return null;
        while (pool.Count <= index)
        {
            pool.Add(UiTextRow.Create("AyaHitmarker.RaidSummary" + kind + pool.Count, _panel.Rt, align, withGroup: false));
        }
        return pool[index];
    }

    private UiImageRow? RentBar(int index)
    {
        if (_panel == null) return null;
        while (_bars.Count <= index)
        {
            _bars.Add(UiImageRow.Create("AyaHitmarker.RaidSummaryBar" + _bars.Count, _panel.Rt, withGroup: false));
        }
        return _bars[index];
    }

    internal void Tick()
    {
        if (!_active) return;
        if (_panel == null)
        {
            _active = false;
            return;
        }

        float age = UiClock.Now - _born;
        if (age < 0f || age >= _duration)
        {
            HideAll();
            return;
        }

        float alpha = Mathf.Min(Mathf.Clamp01(age / FadeIn), Mathf.Clamp01((_duration - age) / FadeOut));
        _panel.SetFade(alpha);
    }

    internal void Reset() => HideAll();

    private void HideAll()
    {
        _active = false;
        _panel?.Hide();
        _accent?.Hide();
        _titleLine?.Hide();
        _title?.Hide();
        for (int i = 0; i < _labels.Count; i++) { _labels[i].Hide(); _values[i].Hide(); }
        for (int i = 0; i < _bars.Count; i++) _bars[i].Hide();
    }
}
