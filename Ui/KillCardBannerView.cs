using AyaHitmarker.Assets;
using AyaHitmarker.Game;
using TMPro;
using UnityEngine;

namespace AyaHitmarker.Ui;

/// <summary>
/// 击杀卡片顶部的**成就横幅**（战地 6 的浅钢色带 + 左侧强调条 + 可选的多杀骷髅徽记）。
///
/// 只在触发成就时出现；标题量宽决定底色宽度，多杀时右侧再留出徽记的位置。
/// 框 / 字 / 徽记共用同一个退场不透明度（否则会出现「只剩一根灰条、字已经灭了」）。
///
/// 从 <see cref="KillFeedLayer"/> 拆出（0.6 卡片拆分），行为保持不变。
/// </summary>
internal sealed class KillCardBannerView
{
    private const float EmblemGap = 6f;
    private const float AccentWidth = 4f;
    private const int MaxBannerDetailChars = 7;

    private const float DelayBanner = 0.10f;
    private const float DelayEmblem = 0.14f;
    private const int SeedBanner = 53;

    private static readonly Color BannerFill = new Color(0.851f, 0.878f, 0.910f, 0.97f);

    private UiImageRow? _box;
    private UiImageRow? _accent;
    private UiImageRow? _flash;
    private UiTextRow? _text;
    private UiImageRow? _emblem;

    private int _serialSeen = -1;
    private string _string = string.Empty;
    private float _boxWidth;
    private int _emblemCount;
    private Color _accentColor = Color.gray;

    private float _fpEmblemCount = -1;
    private float _fpEmblemSize = -1f;
    private float _fpEmblemUi = -1f;
    private BakedIcon? _emblemBaked;

    /// <summary>横幅整体宽度（含徽记与间隔）；0 = 当前没有横幅。<see cref="KillFeedLayer"/> 布局用。</summary>
    internal float TotalWidth { get; private set; }

    internal void Attach(RectTransform parent)
    {
        // 创建顺序 = 绘制顺序：框 → 强调条 → 字 → 徽记 → 色块闪出
        _box = UiImageRow.Create("AyaHitmarker.KillBannerBox", parent);
        _accent = UiImageRow.Create("AyaHitmarker.KillBannerAccent", parent);
        _text = UiTextRow.Create("AyaHitmarker.KillBannerText", parent, TextAlignmentOptions.Center);
        _emblem = UiImageRow.Create("AyaHitmarker.KillEmblem", parent);
        _flash = UiImageRow.Create("AyaHitmarker.KillBannerFlash", parent);
    }

    /// <summary>启动时把 2~5 杀徽记烘好（主线程同步烘焙，留到第一次连杀会掉帧）。</summary>
    internal void Warmup(AyaConfig cfg)
    {
        float emblem = EmblemSkullPx(cfg);
        for (int n = 2; n <= 5; n++) ProceduralIcons.Emblem(n, emblem);
    }

    /// <summary>每杀一次：量标题宽度、决定强调色 / 多杀徽记数。同一条横幅不重复量。</summary>
    internal void Prepare(AyaConfig cfg, KillCardModel model)
    {
        if (_text == null) return;

        if (!model.HasBanner)
        {
            _serialSeen = model.BannerSerial;
            TotalWidth = 0f;
            _emblemCount = 0;
            return;
        }

        // 同一条横幅不用重新量
        bool sameBanner = _serialSeen == model.BannerSerial;
        _serialSeen = model.BannerSerial;
        if (sameBanner && TotalWidth > 0f) return;

        var ribbon = model.Banner;
        bool multi = ribbon.Kind == KillAchievementKind.MultiKill && model.BannerMulti >= 2;

        // 横幅只带**短**详情（「87m」「连杀 x5」）。多杀的「间隔 2.5 秒内第 N 杀」徽记里的骷髅数已经说明了；
        // 「第 4 杀后 8 秒内再杀」这类解释性的长句放进去会把横幅撑得比整张卡还宽，BF6 的横幅也只有一个短标题。
        bool showDetail = !multi
                          && !string.IsNullOrWhiteSpace(ribbon.Detail)
                          && ribbon.Detail.Length <= MaxBannerDetailChars;
        string text = showDetail ? ribbon.Title + " · " + ribbon.Detail : ribbon.Title;
        _string = Escape(text);

        int fontSize = cfg.FontSizeAchievement.Value;
        _text.SetSpacing(3f);
        float textWidth = _text.Measure(_string, fontSize, FontStyles.Bold);
        _boxWidth = Mathf.Round(textWidth + Mathf.Max(0f, cfg.AchievementPadding.Value) * 2f + AccentWidth);

        _emblemCount = multi ? Mathf.Clamp(model.BannerMulti, 2, 5) : 0;
        _accentColor = ribbon.Tier switch
        {
            KillAchievementTier.Epic => UiTheme.Parse(cfg.ColorAchievementEpic.Value, new Color(0.88f, 0.23f, 0.31f)),
            KillAchievementTier.Great => UiTheme.Parse(cfg.ColorAchievementGreat.Value, new Color(0.88f, 0.5f, 0.12f)),
            KillAchievementTier.Good => UiTheme.Parse(cfg.ColorAchievementGood.Value, new Color(0.18f, 0.48f, 0.84f)),
            _ => UiTheme.Parse(cfg.ColorAchievementNormal.Value, new Color(0.42f, 0.49f, 0.55f)),
        };

        TotalWidth = _boxWidth + (_emblemCount > 0 ? EmblemGap + EmblemWidth(cfg) : 0f);
    }

    /// <summary>尺寸 / 图标样式变了：下一次 <see cref="Prepare"/> 强制重新量（徽记宽度依赖它们）。</summary>
    internal void InvalidateSizeCache() => _serialSeen = -1;

    /// <summary>卡片清空：把缓存与可见性一起复位（布局据此认为没有横幅）。</summary>
    internal void Reset()
    {
        _serialSeen = -1;
        TotalWidth = 0f;
        _emblemCount = 0;
        _string = string.Empty;
        _boxWidth = 0f;
        HideAll();
    }

    internal void Draw(AyaConfig cfg, in KillCardAnim anim, KillCardModel model, Vector2 anchor,
        in KillCardLayout layout, float exitAge, float exitDur)
    {
        if (_box == null || _accent == null || _text == null || _flash == null || _emblem == null) return;

        bool wanted = cfg.ShowAchievement.Value && model.HasBanner && TotalWidth > 0f;
        if (!wanted)
        {
            HideAll();
            return;
        }

        int seed = SeedBanner + model.BannerSerial * 5;

        // 框 / 字 / 徽记共用同一个退场不透明度，闪烁才同步
        float exit = anim.Exit(1, seed, exitAge, exitDur);

        anim.Reveal(model.BannerBorn, DelayBanner, seed, GlitchMath.RevealTiming.Standard,
            out float boxContent, out float boxBlock);
        boxContent *= exit;
        boxBlock *= exit;

        if (boxContent <= 0.003f && boxBlock <= 0.003f)
        {
            HideAll();
            return;
        }

        float h = Mathf.Max(8f, cfg.AchievementHeight.Value);
        float y = anchor.y + layout.BannerY;

        float emblemW = 0f;
        float emblemH = 0f;
        BakedIcon? emblem = _emblemCount >= 2 ? ResolveEmblem(cfg) : null;
        if (emblem.HasValue)
        {
            emblemH = EmblemHeight(cfg, emblem.Value);
            emblemW = emblemH * (emblem.Value.Sprite.rect.width / Mathf.Max(1f, emblem.Value.Sprite.rect.height));
        }

        float rightEdge = anchor.x + Mathf.Max(0f, cfg.ScoreHorizontalOffset.Value);
        float boxRight = rightEdge - (emblemW > 0f ? emblemW + EmblemGap : 0f);
        float boxCenterX = Mathf.Round(boxRight - _boxWidth * 0.5f);
        float boxLeft = boxCenterX - _boxWidth * 0.5f;

        _box.Apply(new Vector2(boxCenterX, y), new Vector2(_boxWidth, h), UiTheme.WhiteSprite, BannerFill);
        _box.SetFade(boxContent);

        _accent.Apply(new Vector2(boxLeft + AccentWidth * 0.5f, y), new Vector2(AccentWidth, h), UiTheme.WhiteSprite, _accentColor);
        _accent.SetFade(boxContent);

        // 文字比底晚一点点显影，更有「先出框、再出字」的层次
        anim.Reveal(model.BannerBorn, DelayBanner + 0.04f, seed + 1, GlitchMath.RevealTiming.Quick,
            out float textContent, out _);
        _text.SetSpacing(3f);
        _text.Apply(
            new Vector2(boxCenterX + AccentWidth * 0.5f, y),
            new Vector2(_boxWidth, h),
            _string,
            cfg.FontSizeAchievement.Value,
            UiTheme.Parse(cfg.ColorAchievementText.Value, new Color(0.18f, 0.24f, 0.31f)),
            FontStyles.Bold,
            TextAlignmentOptions.Center);
        _text.SetFade(textContent * exit);

        if (boxBlock > 0.003f)
        {
            _flash.Apply(new Vector2(boxCenterX, y), new Vector2(_boxWidth, h), UiTheme.WhiteSprite, BannerFill);
            _flash.SetFade(boxBlock);
        }
        else
        {
            _flash.Hide();
        }

        if (emblem.HasValue)
        {
            anim.Reveal(model.BannerBorn, DelayEmblem, seed + 2, GlitchMath.RevealTiming.Standard,
                out float emblemContent, out _);
            emblemContent *= exit;
            if (emblemContent > 0.003f)
            {
                // 位图里主骷髅的中心比位图中心低（上面留了冒头骷髅的位置），抬回来让它和横幅竖直居中
                Vector2 center = new Vector2(rightEdge - emblemW * 0.5f, y + EmblemSkullPx(cfg) * IconShapes.EmblemCenterShift);
                _emblem.Apply(center, new Vector2(emblemW, emblemH), emblem.Value.Sprite, KillCardPalette.IceWhite);
                _emblem.SetFade(emblemContent);
            }
            else
            {
                _emblem.Hide();
            }
        }
        else
        {
            _emblem.Hide();
        }
    }

    internal void HideAll()
    {
        _box?.Hide();
        _accent?.Hide();
        _text?.Hide();
        _flash?.Hide();
        _emblem?.Hide();
    }

    private float EmblemWidth(AyaConfig cfg)
    {
        var baked = ResolveEmblem(cfg);
        if (!baked.HasValue) return 0f;
        var sp = baked.Value.Sprite;
        float h = EmblemHeight(cfg, baked.Value);
        return h * (sp.rect.width / Mathf.Max(1f, sp.rect.height));
    }

    /// <summary>徽记里主骷髅的显示高度：和横幅等高（BF6 实录里徽记的骷髅就是和横幅一样高）。</summary>
    private static float EmblemSkullPx(AyaConfig cfg) => Mathf.Max(8f, cfg.AchievementHeight.Value) * 0.92f;

    /// <summary>徽记位图的显示高度（含冒头骷髅的留白，按 <see cref="BakedIcon.PadRatio"/> 放大）。</summary>
    private static float EmblemHeight(AyaConfig cfg, in BakedIcon baked)
        => EmblemSkullPx(cfg) * baked.PadRatio;

    private BakedIcon? ResolveEmblem(AyaConfig cfg)
    {
        float skullPx = EmblemSkullPx(cfg);
        if (_emblemBaked.HasValue && _fpEmblemCount == _emblemCount && Mathf.Abs(_fpEmblemSize - skullPx) < 0.01f
            && Mathf.Abs(_fpEmblemUi - cfg.UiScale.Value) < 0.0001f)
        {
            return _emblemBaked;
        }

        _fpEmblemCount = _emblemCount;
        _fpEmblemSize = skullPx;
        _fpEmblemUi = cfg.UiScale.Value;
        _emblemBaked = _emblemCount >= 2 ? ProceduralIcons.Emblem(_emblemCount, skullPx) : null;
        return _emblemBaked;
    }

    /// <summary>TMP 富文本以 &lt;&gt; 为标记，玩家昵称 / 武器名里出现就会吞标签，必须转义。</summary>
    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.IndexOf('<') < 0 && s.IndexOf('>') < 0) return s;
        return s.Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
