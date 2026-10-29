using System;
using System.Collections.Generic;
using System.Text;
using AyaHitmarker.Assets;
using AyaHitmarker.Game;
using TMPro;
using UnityEngine;

namespace AyaHitmarker.Ui;

/// <summary>
/// 击杀卡片，按战地 6 的击杀确认重做（两段实录逐帧拆解，见 README §9）。
///
/// <b>它不是「一杀一张」</b>：窗口内连续击杀并到同一张卡上 —— 骷髅并排、分数累计、标签取并集、
/// 名字换成最新的受害者；窗口过了才整体退场（<see cref="KillCardModel"/>）。
///
/// 版式（脊线 = 锚点，骷髅 / 名字 / 标签 / 详情在脊线左边<b>右对齐</b>，分数从脊线往右长）：
/// <code>
///                              ┌─────────┐
///                              │ 双杀    │▌≡☠☠≡          ← 成就横幅（只有触发成就时才有）
///                              └─────────┘
///   ┌──────┐                         ☠ ☠   225              ← 主行：骷髅 + 累计得分
///   │ 军衔 │                    ──────────                   ← 连杀进度条
///   │ 徽章 │                USEC  Nikita                     ← 阵营 + 名字
///   └──────┘         [150m] [狙击] [击杀] [爆头]             ← 标签（描边小方框）
///                              胸部 · M4A1                   ← 详情（暗色小字）
/// </code>
/// 军衔徽章贴在整张卡片内容的<b>最左边</b>（内容变宽它就往左让）。
///
/// <b>动效</b>（都是实录里量出来的，曲线在 <see cref="GlitchMath"/>）：
/// <list type="bullet">
///   <item>入场：每个元素先以一块<b>实心色块</b>闪出，再在 ~0.12s 内显影；分数数字乱跳着滚到终值；</item>
///   <item>退场：<b>错峰闪烁熄灭</b> —— 骷髅 + 名字先走，标签 / 横幅 / 详情随后，分数最后硬切；</item>
///   <item>「故障特效」关掉后退化成普通淡入淡出。</item>
/// </list>
///
/// <para>
/// <b>0.6 拆分：</b>横幅 / 骷髅行 / 标签行 / 军衔徽章已经拆成独立视图
/// （<see cref="KillCardBannerView"/> / <see cref="KillCardSkullRowView"/> /
/// <see cref="KillCardTagRowView"/> / <see cref="KillCardBadgeView"/>）；
/// 这里只留「模型 → 每杀准备一次 → 布局 → 逐帧编排」和进度条 / 分数 / 名字 / 详情。
/// 共用的动画参数走 <see cref="KillCardAnim"/>，配色常量走 <see cref="KillCardPalette"/>。
/// </para>
///
/// 对齐 AGENTS.md §3.5：位置 / 尺寸 / 字号只在变化时写；淡入淡出一律走 <c>CanvasGroup.alpha</c>
/// （<see cref="UiTextRow.SetFade"/>，不触发 TMP 重建网格），所以每帧都在变的故障闪烁没有代价。
/// </summary>
internal sealed class KillFeedLayer
{
    // 进度条 / 分数 / 名字 / 详情的错峰衰减（其余元素在各自视图里）
    private const float DelayBar = 0.04f;
    private const float DelayScore = 0f;
    private const float DelayName = 0.04f;
    private const float DelayDetails = 0.12f;

    private const int SeedBar = 14;       // = 骷髅 seed + 3
    private const int SeedScore = 37;
    private const int SeedName = 23;
    private const int SeedDetails = 79;

    private const float BadgePlatePad = 7f;

    private readonly KillCardModel _model = new KillCardModel();
    private readonly RingEffect _ring = new RingEffect();
    private readonly StringBuilder _sb = new StringBuilder(160);

    private readonly KillCardSkullRowView _skullRow = new KillCardSkullRowView();
    private readonly KillCardTagRowView _tagRow = new KillCardTagRowView();
    private readonly KillCardBannerView _banner = new KillCardBannerView();
    private readonly KillCardBadgeView _badge = new KillCardBadgeView();

    private RectTransform? _parent;

    // ---- 卡片里没有拆出去的元素 ----
    private UiImageRow? _barBack;
    private UiImageRow? _barFill;
    private UiTextRow? _score;
    private UiImageRow? _scoreFlash;
    private UiTextRow? _name;
    private UiImageRow? _nameFlash;
    private UiTextRow? _details;

    // ---- 每杀量一次 / 拼一次的展示数据（逐帧绘制只读这些，不再拼串）----
    private Color _factionColor = Color.white;
    private string _nameRich = string.Empty;
    private float _nameWidth;
    private string _detailsText = string.Empty;
    private float _detailsWidth;
    private float _scoreWidth;

    // ---- 逐帧状态 ----
    private int _shownScore;
    private float _streakTarget;
    private float _barShown = float.NaN;
    private float _now;
    private float _dt;
    private bool _glitch;
    private int _loggedVictimSerial = -1;
    private KillCardAnim _anim;
    private Vector2 _ringCenter;
    private bool _ringCenterValid;

    private static readonly Dictionary<int, string> ScoreCache = new Dictionary<int, string>(128);

    // ------------------------------------------------------------------ 构建

    internal void Attach(RectTransform parent)
    {
        _parent = parent;
        _ring.Attach(parent);

        // 创建顺序 = 绘制顺序（先建的在下面）。色块（Flash）必须建在它所覆盖的内容之后。
        _barBack = UiImageRow.Create("AyaHitmarker.KillBarBack", parent);
        _barFill = UiImageRow.Create("AyaHitmarker.KillBarFill", parent);
        _barFill.Apply(Vector2.zero, new Vector2(1f, 1f), UiTheme.WhiteSprite, Color.clear, 1f, new Vector2(0f, 0.5f));

        _badge.Attach(parent);
        _banner.Attach(parent);

        _score = UiTextRow.Create("AyaHitmarker.KillScore", parent, TextAlignmentOptions.Left);
        _scoreFlash = UiImageRow.Create("AyaHitmarker.KillScoreFlash", parent);

        _name = UiTextRow.Create("AyaHitmarker.KillName", parent, TextAlignmentOptions.Right);
        _nameFlash = UiImageRow.Create("AyaHitmarker.KillNameFlash", parent);

        _tagRow.Attach(parent);

        _details = UiTextRow.Create("AyaHitmarker.KillDetails", parent, TextAlignmentOptions.Right);

        HideCard();
    }

    // ------------------------------------------------------------------ 新增击杀

    /// <param name="rec">击杀快照。</param>
    /// <param name="analysis">由 <see cref="KillStreakTracker"/> 算好的成就/连杀结果。</param>
    internal void Show(in KillRecord rec, in KillAnalysis analysis)
    {
        if (_parent == null) return;

        var cfg = Plugin.Cfg;
        float now = UiClock.Now;

        var ribbon = cfg.ShowAchievement.Value ? analysis.Ribbon : KillAchievement.None;
        var input = new CardKillInput(
            rec.Headshot,
            rec.Melee,
            rec.Explosive,
            rec.SniperDamage,
            rec.Distance,
            analysis.Experience ?? 0,
            ribbon,
            analysis.MultiCount,
            analysis.Experience.HasValue);

        float exitDuration = Mathf.Max(0f, cfg.CardExitDuration.Value);
        float hold = Mathf.Max(0.3f, cfg.KillFeedDuration.Value - exitDuration);

        bool fresh = _model.AddKill(now, input, _shownScore, hold, exitDuration, cfg.KillFeedMaxSkulls.Value);
        if (fresh) ResetViewState();

        SetStreakProgress(analysis.StreakProgress);

        PrepareVictim(cfg, rec);
        _tagRow.Prepare(cfg, _model);
        _banner.Prepare(cfg, _model);
        PrepareScore(cfg);
        _skullRow.Sync(cfg, _model);

        // 环形特效：爆头必定触发；史诗级成就（多杀 / 复仇 / 首领）用爆炸环，冲击感更强
        bool epic = analysis.Ribbon.Tier == KillAchievementTier.Epic || analysis.MultiCount >= 4;
        bool trigger = rec.Headshot || epic;
        KillType type = epic ? KillType.Explosion : KillType.Headshot;

        // 只在需要时触发：Trigger(enabled:false) 会把上一个还在扩散的环直接掐掉（爆头后 0.2s 内再来一杀普通击杀就会）
        if (cfg.EnableRingEffect.Value && cfg.RingHeadshotEnabled.Value && trigger)
        {
            _ring.Trigger(now, true, type);
        }

        if (cfg.DebugEnabled.Value)
        {
            Plugin.Logger.LogInfo(
                $"[kill] 卡片: {(fresh ? "新卡" : "并入")} kills={_model.KillCount} score={_model.TotalScore} " +
                $"skulls={_model.Icons.Count} tags={_tagRow.Count} banner='{_model.Banner.Title}' " +
                $"宽度 name={_nameWidth:0} tags={_tagRow.Width:0} details={_detailsWidth:0} banner={_banner.TotalWidth:0} " +
                $"badge={(_badge.HasBadge ? "Y" : "N")} anchor={Anchor()}");
        }
    }

    /// <summary>
    /// 启动时把卡片要用的程序化图标全部烘好（骷髅 / 2~5 杀徽记 / 标签框）。
    /// 烘焙是主线程同步的，5 杀徽记要十几毫秒 —— 留到第一次连杀时再烘就是在最需要流畅的那一刻掉帧。
    /// </summary>
    internal void Warmup()
    {
        var cfg = Plugin.Cfg;
        ProceduralIcons.TagBox();
        _skullRow.Warmup(cfg);
        _banner.Warmup(cfg);
    }

    /// <summary>新开一张卡：丢掉上一张卡的视图状态（徽章位置、显示分数、骷髅视图、横幅缓存）。</summary>
    private void ResetViewState()
    {
        _shownScore = 0;
        _badge.ResetViewState();
        _banner.InvalidateSizeCache();
        _skullRow.Reset();
    }

    // ------------------------------------------------------------------ 每杀只做一次的准备（量宽度 / 拼文本）

    private void PrepareVictim(AyaConfig cfg, in KillRecord rec)
    {
        if (_name == null || _details == null) return;

        // ---- 阵营色 ----
        _factionColor = KillCardPalette.FactionColor(cfg, rec.BotType);

        // ---- 军衔徽章（视图自己算图 / 等级小标宽度）----
        _badge.Prepare(cfg, rec, _factionColor);

        // ---- 名字行：「阵营  名字」一行富文本 ----
        string name = string.IsNullOrWhiteSpace(rec.VictimName)
            ? "Unknown"
            : NameTranslit.ForDisplay(rec.VictimName, cfg.NameTransliterate.Value);
        string label = cfg.ShowBossName.Value && rec.VictimIsBoss
            ? BotTypeMapping.DisplayName(rec.Role, rec.BotType)
            : rec.BotType;

        _sb.Clear();
        if (cfg.ShowFaction.Value && !string.IsNullOrWhiteSpace(label)
            && !string.Equals(label, name, StringComparison.OrdinalIgnoreCase))
        {
            _sb.Append("<size=78%><color=#").Append(Hex(_factionColor)).Append('>').Append(Escape(label)).Append("</color></size>  ");
        }

        // 没有军衔徽章的 PMC（图标文件缺失）：把等级放回名字前面，别让信息凭空消失
        if (!_badge.HasBadge && rec.IsPmc && rec.VictimLevel > 0)
        {
            _sb.Append("<size=78%><color=#9AA3AD>Lv.").Append(rec.VictimLevel).Append("</color></size>  ");
        }
        _sb.Append(Escape(name));
        _nameRich = _sb.ToString();
        _nameWidth = cfg.ShowPlayerName.Value
            ? _name.Measure(_nameRich, cfg.FontSizePlayerName.Value, FontStyles.Bold) + 4f
            : 0f;

        // ---- 详情行：「胸部 · M4A1」（距离在标签里，不重复）----
        _detailsText = cfg.ShowKillDetails.Value ? ComposeDetails(rec) : string.Empty;
        _detailsWidth = string.IsNullOrEmpty(_detailsText)
            ? 0f
            : _details.Measure(_detailsText, cfg.FontSizeKillDetails.Value, FontStyles.Normal) + 2f;
    }

    /// <summary>「胸部 · M4A1」。爆头已经在标签里了，所以爆头时不再写部位。空字段自动跳过。</summary>
    private string ComposeDetails(in KillRecord rec)
    {
        _sb.Clear();

        if (!rec.Headshot && !string.IsNullOrWhiteSpace(rec.BodyPart)) AppendDetail(Escape(rec.BodyPart));

        if (!string.IsNullOrWhiteSpace(rec.Weapon) && rec.Weapon != "?") AppendDetail(Escape(rec.Weapon));

        return _sb.ToString();
    }

    private void AppendDetail(string part)
    {
        if (_sb.Length > 0) _sb.Append(" · ");
        _sb.Append(part);
    }

    private void PrepareScore(AyaConfig cfg)
    {
        if (_score == null) return;
        _scoreWidth = cfg.ShowScore.Value
            ? _score.Measure(ScoreString(_model.TotalScore), cfg.FontSizeScore.Value, FontStyles.Bold) + 10f
            : 0f;
    }

    // ------------------------------------------------------------------ 每帧

    internal void Tick()
    {
        var cfg = Plugin.Cfg;

        if (!cfg.KillFeedEnabled.Value)
        {
            if (_model.Active || _ring.IsActive) Reset();
            return;
        }

        float now = UiClock.Now;

        if (_model.Tick(now))
        {
            HideCard();
            ClearViews();
            if (cfg.DebugEnabled.Value) Plugin.Logger.LogInfo("[kill] 卡片已结束并清空");
        }

        if (!_model.Active)
        {
            _ring.Tick(Anchor(), now);
            return;
        }

        _now = now;
        _dt = UiClock.Delta;
        _glitch = cfg.CardGlitchEnabled.Value;
        _anim = new KillCardAnim(_now, _dt, _glitch);

        RefreshIconsIfConfigChanged(cfg);
        DrawCard(cfg);

        _ring.Tick(_ringCenterValid ? _ringCenter : Anchor(), now);
    }

    private void RefreshIconsIfConfigChanged(AyaConfig cfg)
    {
        if (!_skullRow.RefreshSpritesIfChanged(cfg)) return;

        // 横幅的徽记宽度也依赖 UI 缩放，重新量一次
        _banner.InvalidateSizeCache();
        _banner.Prepare(cfg, _model);
    }

    private KillCardLayoutInput BuildLayoutInput(AyaConfig cfg)
    {
        bool hasBanner = cfg.ShowAchievement.Value && _model.HasBanner && _banner.TotalWidth > 0f;
        bool hasTags = cfg.ShowTags.Value && _tagRow.Count > 0;
        bool hasDetails = cfg.ShowKillDetails.Value && !string.IsNullOrEmpty(_detailsText);

        return new KillCardLayoutInput
        {
            SkullSize = cfg.SkullSize.Value,
            IconGap = cfg.SkullSpacing.Value,
            RowGap = cfg.KillFeedLineSpacing.Value,
            BarHeight = cfg.StreakBarHeight.Value,
            BarGap = cfg.StreakBarGap.Value,
            BannerHeight = cfg.AchievementHeight.Value,
            NameHeight = KillCardLayoutMath.RowHeightFor(cfg.FontSizePlayerName.Value),
            TagHeight = KillCardTagRowView.TagHeight(cfg),
            DetailsHeight = KillCardLayoutMath.RowHeightFor(cfg.FontSizeKillDetails.Value),
            BadgeSize = cfg.FactionIconSize.Value + BadgePlatePad * 2f,
            BadgeGap = 10f,
            HasBanner = hasBanner,
            HasBar = cfg.ShowStreakBar.Value,
            HasName = cfg.ShowPlayerName.Value,
            HasTags = hasTags,
            HasDetails = hasDetails,
            HasBadge = cfg.ShowFactionIcon.Value && _badge.HasBadge,
            SkullCount = cfg.ShowSkull.Value ? _skullRow.Count : 0,
            BannerWidth = _banner.TotalWidth,
            NameWidth = _nameWidth,
            TagsWidth = _tagRow.Width,
            DetailsWidth = _detailsWidth,
            BannerOverhang = Mathf.Max(0f, cfg.ScoreHorizontalOffset.Value),
        };
    }

    private void DrawCard(AyaConfig cfg)
    {
        float exitAge = _model.ExitAge(_now);
        float exitDur = _model.ExitDuration;

        var input = BuildLayoutInput(cfg);
        var layout = KillCardLayoutMath.Compute(input);
        Vector2 anchor = Anchor();

        // 退场到头了（Tick 里 Model.Tick 会清掉，这里只是兜底，避免最后一帧全 0 的残影）
        if (exitAge >= exitDur && exitDur > 0f) { HideCard(); return; }

        _ringCenterValid = false;

        DrawBar(cfg, anchor, layout, exitAge, exitDur);
        _skullRow.Draw(cfg, _anim, anchor, layout, exitAge, exitDur, out _ringCenter, out _ringCenterValid);
        DrawScore(cfg, anchor, layout, exitAge, exitDur);
        DrawName(cfg, anchor, layout, exitAge, exitDur);
        _tagRow.Draw(cfg, _anim, exitAge, exitDur, anchor, layout);
        DrawDetails(cfg, anchor, layout, exitAge, exitDur);
        _banner.Draw(cfg, _anim, _model, anchor, layout, exitAge, exitDur);
        _badge.Draw(cfg, _anim, _model, anchor, layout, exitAge, exitDur);
        LogScreenPositions(cfg, anchor, layout);
    }

    // ------------------------------------------------------------------ 进度条

    private void DrawBar(AyaConfig cfg, Vector2 anchor, in KillCardLayout layout, float exitAge, float exitDur)
    {
        if (_barBack == null || _barFill == null) return;

        if (!cfg.ShowStreakBar.Value)
        {
            _barBack.Hide();
            _barFill.Hide();
            _barShown = float.NaN;
            return;
        }

        _anim.Fades(_model.StartTime, DelayBar, SeedBar, GlitchMath.RevealTiming.Quick, 1, exitAge, exitDur,
            out float content, out _);
        if (content <= 0.003f)
        {
            _barBack.Hide();
            _barFill.Hide();
            return;
        }

        // 进度条至少和骷髅行一样宽：连杀多了骷髅排得很长，一条 120px 的短线挂在下面很突兀
        float skullRow = cfg.ShowSkull.Value && _skullRow.Count > 0
            ? Mathf.Max(8f, cfg.SkullSize.Value) + (_skullRow.Count - 1) * layout.SkullPitch
            : 0f;
        float width = Mathf.Max(20f, Mathf.Max(cfg.StreakBarWidth.Value, skullRow));
        float height = Mathf.Max(1f, cfg.StreakBarHeight.Value);
        float left = anchor.x - width;
        float y = anchor.y + layout.BarY;

        // 目标进度平滑收敛，避免连杀时条子「跳」一下。1.6 = 每秒能走完 1.6 份进度。
        const float barSweepPerSecond = 1.6f;
        float target = _streakTarget;
        if (float.IsNaN(_barShown)) _barShown = target;
        else _barShown = Mathf.MoveTowards(_barShown, target, _dt * barSweepPerSecond);

        _barBack.Apply(
            new Vector2(left, y), new Vector2(width, height), UiTheme.WhiteSprite,
            UiTheme.Parse(cfg.ColorStreakBarBack.Value, new Color(0f, 0f, 0f, 0.5f)), 1f, new Vector2(0f, 0.5f));
        _barBack.SetFade(content);

        float filled = Mathf.Clamp01(_barShown) * width;
        _barFill.Apply(
            new Vector2(left, y), new Vector2(Mathf.Max(0f, filled), height), UiTheme.WhiteSprite,
            UiTheme.Parse(cfg.ColorStreakBar.Value, new Color(1f, 0.48f, 0.09f)), 1f, new Vector2(0f, 0.5f));
        _barFill.SetFade(content);
    }

    // ------------------------------------------------------------------ 得分

    private void DrawScore(AyaConfig cfg, Vector2 anchor, in KillCardLayout layout, float exitAge, float exitDur)
    {
        if (_score == null || _scoreFlash == null) return;

        if (!cfg.ShowScore.Value || !_model.ExperienceComplete || _model.TotalScore <= 0)
        {
            _score.Hide();
            _scoreFlash.Hide();
            return;
        }

        // 入场显影只在卡片的第一杀做；之后每一杀只做「数字滚动 + 弹出」，分数一直在场
        _anim.Fades(_model.StartTime, DelayScore, SeedScore, GlitchMath.RevealTiming.Quick, 2, exitAge, exitDur,
            out float content, out float block);

        if (content <= 0.003f && block <= 0.003f)
        {
            _score.Hide();
            _scoreFlash.Hide();
            return;
        }

        float countDur = Mathf.Max(0f, cfg.ScoreCountDuration.Value);
        int shown = GlitchMath.CountValue(
            _now - _model.ScoreChangedAt, countDur, _model.PreviousScore, _model.TotalScore, SeedScore + _model.KillCount * 5, _glitch);
        _shownScore = shown;

        float popDur = Mathf.Max(0.01f, cfg.ScorePopDuration.Value);
        float popMax = Mathf.Max(1f, cfg.ScorePopScale.Value);
        float pop = UiAnim.OutBack(UiAnim.Clamp01((_now - _model.ScoreChangedAt) / popDur));
        float scale = popMax + (1f - popMax) * pop;

        float rowH = KillCardLayoutMath.RowHeightFor(cfg.FontSizeScore.Value);
        float left = anchor.x + cfg.ScoreHorizontalOffset.Value;
        float y = anchor.y + layout.MainY;

        _score.SetOutline(KillCardPalette.OutlineFor(cfg, cfg.FontSizeScore.Value), KillCardPalette.TextOutlineColor);
        _score.Apply(
            new Vector2(left, y),
            new Vector2(Mathf.Max(40f, _scoreWidth + 20f), rowH),
            ScoreString(shown),
            cfg.FontSizeScore.Value,
            UiTheme.Parse(cfg.ColorScore.Value, Color.white),
            FontStyles.Bold,
            TextAlignmentOptions.Left,
            scale);
        _score.SetFade(content);

        if (block > 0.003f)
        {
            float w = _scoreWidth + 4f;
            _scoreFlash.Apply(new Vector2(left + w * 0.5f - 2f, y), new Vector2(w, rowH * 0.8f), UiTheme.WhiteSprite, KillCardPalette.IceWhite, scale);
            _scoreFlash.SetFade(block);
        }
        else
        {
            _scoreFlash.Hide();
        }
    }

    // ------------------------------------------------------------------ 名字

    private void DrawName(AyaConfig cfg, Vector2 anchor, in KillCardLayout layout, float exitAge, float exitDur)
    {
        if (_name == null || _nameFlash == null) return;

        if (!cfg.ShowPlayerName.Value)
        {
            _name.Hide();
            _nameFlash.Hide();
            return;
        }

        _anim.Fades(_model.VictimBorn, DelayName, SeedName + _model.VictimSerial * 7, GlitchMath.RevealTiming.Standard, 0,
            exitAge, exitDur, out float content, out float block);

        if (content <= 0.003f && block <= 0.003f)
        {
            _name.Hide();
            _nameFlash.Hide();
            return;
        }

        float rowH = KillCardLayoutMath.RowHeightFor(cfg.FontSizePlayerName.Value);
        float y = anchor.y + layout.NameY;
        Color nameColor = UiTheme.Parse(cfg.ColorPlayerName.Value, new Color(1f, 0.35f, 0.3f));

        _name.SetOutline(KillCardPalette.OutlineFor(cfg, cfg.FontSizePlayerName.Value), KillCardPalette.TextOutlineColor);
        _name.Apply(
            new Vector2(anchor.x, y),
            new Vector2(Mathf.Max(40f, _nameWidth + 8f), rowH),
            _nameRich,
            cfg.FontSizePlayerName.Value,
            nameColor,
            FontStyles.Bold,
            TextAlignmentOptions.Right);
        _name.SetFade(content);

        if (block > 0.003f)
        {
            float w = _nameWidth + 4f;
            _nameFlash.Apply(new Vector2(anchor.x - w * 0.5f, y), new Vector2(w, rowH * 0.62f), UiTheme.WhiteSprite, nameColor);
            _nameFlash.SetFade(block);
        }
        else
        {
            _nameFlash.Hide();
        }
    }

    // ------------------------------------------------------------------ 详情

    private void DrawDetails(AyaConfig cfg, Vector2 anchor, in KillCardLayout layout, float exitAge, float exitDur)
    {
        if (_details == null) return;

        if (!cfg.ShowKillDetails.Value || string.IsNullOrEmpty(_detailsText))
        {
            _details.Hide();
            return;
        }

        _anim.Fades(_model.VictimBorn, DelayDetails, SeedDetails + _model.VictimSerial * 3, GlitchMath.RevealTiming.Quick, 1,
            exitAge, exitDur, out float content, out _);

        if (content <= 0.003f)
        {
            _details.Hide();
            return;
        }

        float rowH = KillCardLayoutMath.RowHeightFor(cfg.FontSizeKillDetails.Value);
        _details.SetOutline(KillCardPalette.OutlineFor(cfg, cfg.FontSizeKillDetails.Value), KillCardPalette.TextOutlineColor);
        _details.Apply(
            new Vector2(anchor.x, anchor.y + layout.DetailsY),
            new Vector2(Mathf.Max(40f, _detailsWidth + 8f), rowH),
            _detailsText,
            cfg.FontSizeKillDetails.Value,
            UiTheme.Parse(cfg.ColorKillDetails.Value, new Color(0.73f, 0.75f, 0.78f)),
            FontStyles.Normal,
            TextAlignmentOptions.Right);
        _details.SetFade(content);
    }

    // ------------------------------------------------------------------ 诊断

    /// <summary>
    /// 把关键元素的**实际屏幕坐标**写进日志（每杀一次，卡片出现 0.5 秒后）。
    /// ScreenSpaceOverlay 下 <c>RectTransform.position</c> 就是屏幕像素坐标，但**原点在左下角**，
    /// 和截图的左上角原点差一个 Y 翻转，别搞反。
    /// </summary>
    private void LogScreenPositions(AyaConfig cfg, Vector2 anchor, in KillCardLayout layout)
    {
        if (!cfg.DebugEnabled.Value) return;
        if (_loggedVictimSerial == _model.VictimSerial) return;
        if (_now - _model.VictimBorn < 0.5f) return;
        if (_name == null || _score == null) return;

        _loggedVictimSerial = _model.VictimSerial;
        Plugin.Logger.LogInfo(
            $"[kill] 屏幕坐标(左下原点): 名字={_name.Rt.position} 分数={_score.Rt.position} " +
            $"徽章={(_badge.Rt != null ? _badge.Rt!.position.ToString() : "-")} " +
            $"layout: left={layout.LeftEdge:0} top={layout.Top:0} bottom={layout.Bottom:0} " +
            $"screen={Screen.width}x{Screen.height} canvasRect={Plugin.CanvasRectSize}");
    }

    // ------------------------------------------------------------------ 外部接口

    /// <summary>
    /// 锚点（canvas 局部坐标）。
    /// 旧项目 <c>anchorX = sw/2 - hOffset</c>、<c>anchorY = sh/2 + vOffset</c> 都是**屏幕**坐标（Y 向下为正），
    /// canvas 里原点在屏幕中心且 Y 向上，所以垂直分量要取反。
    /// </summary>
    internal static Vector2 Anchor() => new Vector2(
        -Plugin.Cfg.KillFeedHorizontalOffset.Value,
        -Plugin.Cfg.KillFeedVerticalOffset.Value);

    /// <summary>由 <c>AyaBehaviour</c> 在每次击杀后写入：距离下一档连杀称号的进度 0..1。</summary>
    internal void SetStreakProgress(float progress) => _streakTarget = Mathf.Clamp01(progress);

    internal void Reset()
    {
        _model.Reset();
        ClearViews();
        _streakTarget = 0f;
        _ring.Reset();
        HideCard();
    }

    private void ClearViews()
    {
        _skullRow.Reset();
        _tagRow.Clear();
        _shownScore = 0;
        _barShown = float.NaN;
        _banner.Reset();
        _badge.ResetViewState();
        _nameRich = string.Empty;
        _detailsText = string.Empty;
        _tagRow.HideAll();
        _ringCenterValid = false;
    }

    private void HideCard()
    {
        _barBack?.Hide();
        _barFill?.Hide();
        _badge.HideAll();
        _banner.HideAll();
        _score?.Hide();
        _scoreFlash?.Hide();
        _name?.Hide();
        _nameFlash?.Hide();
        _details?.Hide();
        _tagRow.HideAll();
        _skullRow.HideAll();
    }

    // ------------------------------------------------------------------ 文本工具

    private static string ScoreString(int score)
    {
        if (ScoreCache.TryGetValue(score, out var cached)) return cached;
        string s = score.ToString();
        if (ScoreCache.Count > 512) ScoreCache.Clear();
        ScoreCache[score] = s;
        return s;
    }

    private static string Hex(Color c)
    {
        int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
        int g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
        int b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
        return $"{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>TMP 富文本以 &lt;&gt; 为标记，玩家昵称/武器名里出现就会吞标签，必须转义。</summary>
    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.IndexOf('<') < 0 && s.IndexOf('>') < 0) return s;
        return s.Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
