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
/// 对齐 AGENTS.md §3.5：位置 / 尺寸 / 字号只在变化时写；淡入淡出一律走 <c>CanvasGroup.alpha</c>
/// （<see cref="UiTextRow.SetFade"/>，不触发 TMP 重建网格），所以每帧都在变的故障闪烁没有代价。
/// </summary>
internal sealed class KillFeedLayer
{
    private const int MaxTags = 6;
    private const float TagGap = 6f;
    private const float TagPadX = 9f;
    private const float EmblemGap = 6f;
    private const float AccentWidth = 4f;
    private const float BadgeChipHeight = 17f;
    private const float BadgePlatePad = 7f;
    private const int MaxBannerDetailChars = 7;

    // 错峰入场延迟（秒）。顺序取自 BF6 实录：标签最先，分数同时，骷髅 / 名字稍后，横幅最后。
    private const float DelayTag = 0f;
    private const float DelayScore = 0f;
    private const float DelaySkull = 0.04f;
    private const float DelayName = 0.04f;
    private const float DelayBadge = 0.03f;
    private const float DelayBanner = 0.10f;
    private const float DelayEmblem = 0.14f;
    private const float DelayDetails = 0.12f;

    // 故障随机种子：每类元素一个，保证它们不会同帧同样地掉线
    private const int SeedSkull = 11;
    private const int SeedName = 23;
    private const int SeedScore = 37;
    private const int SeedTag = 41;
    private const int SeedBanner = 53;
    private const int SeedBadge = 67;
    private const int SeedDetails = 79;

    private static readonly Color IceWhite = new Color(0.91f, 0.94f, 0.96f, 1f);
    private static readonly Color Coral = new Color(1f, 0.416f, 0.369f, 1f);
    private static readonly Color BannerFill = new Color(0.851f, 0.878f, 0.910f, 0.97f);
    private static readonly Color TagWhite = new Color(0.95f, 0.96f, 0.98f, 1f);
    private static readonly Color TagCoral = new Color(1f, 0.478f, 0.431f, 1f);
    private static readonly Color TagAmber = new Color(1f, 0.784f, 0.341f, 1f);
    private static readonly Color TagSteel = new Color(0.686f, 0.769f, 0.839f, 1f);
    private static readonly Color32 TextOutlineColor = new Color32(0, 0, 0, 215);

    private sealed class SkullView
    {
        internal UiImageRow Glow = null!;
        internal UiImageRow Icon = null!;
        internal UiImageRow Flash = null!;
        internal int Serial;
        internal float Born;
        internal bool Headshot;
        internal float X = float.NaN;
        internal Sprite? Sprite;
        internal float PadRatio = 1f;
        internal Sprite? GlowSprite;
        internal float GlowPad = 1f;
    }

    private struct TagView
    {
        internal string Text;
        internal float Width;
        internal Color Border;
        internal float Born;
        internal int Seed;
    }

    private readonly KillCardModel _model = new KillCardModel();
    private readonly RingEffect _ring = new RingEffect();
    private readonly StringBuilder _sb = new StringBuilder(160);
    private readonly List<SkullView> _skulls = new List<SkullView>(8);
    private readonly List<SkullView> _skullPool = new List<SkullView>(8);
    private readonly List<SkullView> _skullScratch = new List<SkullView>(8);
    private readonly TagView[] _tags = new TagView[MaxTags];
    private int _tagCount;

    private RectTransform? _parent;

    // ---- 元素 ----
    private UiImageRow? _barBack;
    private UiImageRow? _barFill;
    private UiImageRow? _badgePlate;
    private UiImageRow? _badge;
    private UiImageRow? _badgeFlash;
    private UiImageRow? _badgeChip;
    private UiTextRow? _badgeLevel;
    private UiImageRow? _bannerBox;
    private UiImageRow? _bannerAccent;
    private UiImageRow? _bannerFlash;
    private UiTextRow? _bannerText;
    private UiImageRow? _emblem;
    private UiTextRow? _score;
    private UiImageRow? _scoreFlash;
    private UiTextRow? _name;
    private UiImageRow? _nameFlash;
    private readonly UiTagRow?[] _tagRows = new UiTagRow?[MaxTags];
    private readonly UiImageRow?[] _tagFlash = new UiImageRow?[MaxTags];
    private UiTextRow? _details;

    // ---- 每杀量一次 / 拼一次的展示数据（逐帧绘制只读这些，不再拼串）----
    private Sprite? _rankSprite;
    private Color _factionColor = Color.white;
    private string _nameRich = string.Empty;
    private float _nameWidth;
    private string _detailsText = string.Empty;
    private float _detailsWidth;
    private string _levelText = string.Empty;
    private float _levelWidth;
    private float _scoreWidth;
    private float _tagsWidth;

    private int _bannerSerialSeen = -1;
    private string _bannerString = string.Empty;
    private float _bannerBoxWidth;
    private int _bannerEmblemCount;
    private Color _bannerAccentColor = Color.gray;
    private float _bannerTotalWidth;

    // ---- 逐帧状态 ----
    private int _shownScore;
    private float _badgeX = float.NaN;
    private float _streakTarget;
    private float _barShown = float.NaN;
    private float _now;
    private float _dt;
    private bool _glitch;
    private int _loggedVictimSerial = -1;

    // 图标指纹：骷髅大小 / UI 缩放 / 图标样式任何一个变了，所有骷髅的精灵都要重取
    private float _fpSkullSize = -1f;
    private float _fpUiScale = -1f;
    private IconStyle _fpStyle = (IconStyle)(-1);
    private int _fpEmblemCount = -1;
    private float _fpEmblemSize = -1f;
    private float _fpEmblemUi = -1f;
    private BakedIcon? _emblemBaked;

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

        _badgePlate = UiImageRow.Create("AyaHitmarker.KillBadgePlate", parent);
        _badgePlate.SetSliced(true);
        _badge = UiImageRow.Create("AyaHitmarker.KillBadge", parent);
        _badgeChip = UiImageRow.Create("AyaHitmarker.KillBadgeChip", parent);
        _badgeLevel = UiTextRow.Create("AyaHitmarker.KillBadgeLevel", parent, TextAlignmentOptions.Center);
        _badgeFlash = UiImageRow.Create("AyaHitmarker.KillBadgeFlash", parent);

        _bannerBox = UiImageRow.Create("AyaHitmarker.KillBannerBox", parent);
        _bannerAccent = UiImageRow.Create("AyaHitmarker.KillBannerAccent", parent);
        _bannerText = UiTextRow.Create("AyaHitmarker.KillBannerText", parent, TextAlignmentOptions.Center);
        _emblem = UiImageRow.Create("AyaHitmarker.KillEmblem", parent);
        _bannerFlash = UiImageRow.Create("AyaHitmarker.KillBannerFlash", parent);

        _score = UiTextRow.Create("AyaHitmarker.KillScore", parent, TextAlignmentOptions.Left);
        _scoreFlash = UiImageRow.Create("AyaHitmarker.KillScoreFlash", parent);

        _name = UiTextRow.Create("AyaHitmarker.KillName", parent, TextAlignmentOptions.Right);
        _nameFlash = UiImageRow.Create("AyaHitmarker.KillNameFlash", parent);

        for (int i = 0; i < MaxTags; i++)
        {
            _tagRows[i] = UiTagRow.Create("AyaHitmarker.KillTag" + i, parent);
            _tagFlash[i] = UiImageRow.Create("AyaHitmarker.KillTagFlash" + i, parent);
        }

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
            analysis.HasScore ? analysis.Score : 0,
            ribbon,
            analysis.MultiCount);

        float exitDuration = Mathf.Max(0f, cfg.CardExitDuration.Value);
        float hold = Mathf.Max(0.3f, cfg.KillFeedDuration.Value - exitDuration);

        bool fresh = _model.AddKill(now, input, _shownScore, hold, exitDuration, cfg.KillFeedMaxSkulls.Value);
        if (fresh) ResetViewState();

        SetStreakProgress(analysis.StreakProgress);

        PrepareVictim(cfg, rec);
        PrepareTags(cfg);
        PrepareBanner(cfg);
        PrepareScore(cfg);
        SyncSkulls();

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
                $"skulls={_model.Icons.Count} tags={_tagCount} banner='{_model.Banner.Title}' " +
                $"宽度 name={_nameWidth:0} tags={_tagsWidth:0} details={_detailsWidth:0} banner={_bannerTotalWidth:0} " +
                $"badge={(_rankSprite != null ? "Y" : "N")} anchor={Anchor()}");
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

        if (cfg.IconStyleMode.Value == IconStyle.Procedural)
        {
            float size = Mathf.Max(8f, cfg.SkullSize.Value);
            ProceduralIcons.Skull(false, size);
            ProceduralIcons.Skull(true, size);
        }

        float emblem = EmblemSkullPx(cfg);
        for (int n = 2; n <= 5; n++) ProceduralIcons.Emblem(n, emblem);
    }

    /// <summary>新开一张卡：丢掉上一张卡的视图状态（徽章位置、显示分数、骷髅视图）。</summary>
    private void ResetViewState()
    {
        _shownScore = 0;
        _badgeX = float.NaN;
        _bannerSerialSeen = -1;
        for (int i = 0; i < _skulls.Count; i++) RecycleSkull(_skulls[i]);
        _skulls.Clear();
    }

    // ------------------------------------------------------------------ 每杀只做一次的准备（量宽度 / 拼文本）

    private void PrepareVictim(AyaConfig cfg, in KillRecord rec)
    {
        if (_name == null || _details == null || _badgeLevel == null) return;

        // ---- 阵营色 ----
        Color factionBase = UiTheme.Parse(cfg.ColorFaction.Value, Color.white);
        _factionColor = rec.BotType switch
        {
            "USEC" => UiTheme.Parse(cfg.ColorUSEC.Value, factionBase),
            "BEAR" => UiTheme.Parse(cfg.ColorBEAR.Value, factionBase),
            "SCAV" => UiTheme.Parse(cfg.ColorSCAV.Value, factionBase),
            "BOSS" => UiTheme.Parse(cfg.ColorBOSS.Value, factionBase),
            "Follower" => UiTheme.Parse(cfg.ColorFollower.Value, factionBase),
            "Raider" => UiTheme.Parse(cfg.ColorRAIDER.Value, factionBase),
            "Rouges" => UiTheme.Parse(cfg.ColorRouges.Value, factionBase),
            "Sectant" => UiTheme.Parse(cfg.ColorSectant.Value, factionBase),
            _ => factionBase,
        };

        // ---- 军衔徽章（只有 PMC 有）----
        _rankSprite = cfg.ShowFactionIcon.Value && rec.IsPmc ? IconBank.GetRankIcon(rec.VictimLevel) : null;
        _levelText = rec.IsPmc && rec.VictimLevel > 0 ? rec.VictimLevel.ToString() : string.Empty;
        _levelWidth = string.IsNullOrEmpty(_levelText) ? 0f : _badgeLevel.Measure(_levelText, 13, FontStyles.Bold);

        // ---- 名字行：「阵营  名字」一行富文本 ----
        string name = string.IsNullOrWhiteSpace(rec.VictimName) ? "Unknown" : rec.VictimName;
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
        if (_rankSprite == null && rec.IsPmc && rec.VictimLevel > 0)
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

    private static string TagLabel(CardTagKind kind) => kind switch
    {
        CardTagKind.Headshot => "爆头",
        CardTagKind.Melee => "近战",
        CardTagKind.Explosive => "爆炸",
        CardTagKind.Sniper => "狙击",
        _ => "击杀",
    };

    private static Color TagColor(CardTagKind kind) => kind switch
    {
        CardTagKind.Headshot => TagCoral,
        CardTagKind.Melee => TagAmber,
        CardTagKind.Explosive => TagAmber,
        CardTagKind.Sniper => TagAmber,
        _ => TagWhite,
    };

    /// <summary>
    /// 标签行：距离（最左，次要信息）→ 方式（近战 / 爆炸 / 狙击）→ 击杀类（击杀 / 爆头）。
    /// 击杀类标签靠脊线，和 BF6 一样「最新的在最右」。每个标签的宽度实测一次。
    /// </summary>
    private void PrepareTags(AyaConfig cfg)
    {
        _tagCount = 0;
        _tagsWidth = 0f;
        if (!cfg.ShowTags.Value) return;

        int fontSize = cfg.FontSizeTag.Value;

        // 距离
        float minDist = Mathf.Max(0f, cfg.TagMinDistance.Value);
        float dist = _model.LastDistance;
        if (dist > 0.5f && dist >= minDist)
        {
            AddTagView(DistanceString(dist), TagSteel, _model.VictimBorn, fontSize, 0);
        }

        // 方式标签在前，击杀类在后（脊线侧）
        var tags = _model.Tags;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < tags.Count && _tagCount < MaxTags; i++)
            {
                bool isKind = tags[i].Kind == CardTagKind.Kill || tags[i].Kind == CardTagKind.Headshot;
                if ((pass == 0) == isKind) continue;
                if (!cfg.ShowKillMethod.Value && !isKind) continue;
                AddTagView(TagLabel(tags[i].Kind), TagColor(tags[i].Kind), tags[i].Born, fontSize, i + 1);
            }
        }

        for (int i = 0; i < _tagCount; i++) _tagsWidth += _tags[i].Width;
        if (_tagCount > 1) _tagsWidth += (_tagCount - 1) * TagGap;
    }

    private void AddTagView(string text, Color border, float born, int fontSize, int seedOffset)
    {
        if (_tagCount >= MaxTags) return;
        var row = _tagRows[_tagCount];
        if (row == null) return;

        float width = Mathf.Round(row.Label.Measure(text, fontSize, FontStyles.Bold) + TagPadX * 2f);
        _tags[_tagCount] = new TagView
        {
            Text = text,
            Width = width,
            Border = border,
            Born = born,
            Seed = SeedTag + seedOffset * 7,
        };
        _tagCount++;
    }

    private static readonly Dictionary<int, string> DistanceCache = new Dictionary<int, string>(64);

    private static string DistanceString(float meters)
    {
        int m = Mathf.Max(0, Mathf.RoundToInt(meters));
        if (DistanceCache.TryGetValue(m, out var s)) return s;
        s = m + "m";
        if (DistanceCache.Count > 512) DistanceCache.Clear();
        DistanceCache[m] = s;
        return s;
    }

    /// <summary>横幅：标题量宽 → 决定浅色底的宽度；多杀时右边再留出骷髅徽记的位置。</summary>
    private void PrepareBanner(AyaConfig cfg)
    {
        if (_bannerText == null) return;

        if (!_model.HasBanner)
        {
            _bannerSerialSeen = _model.BannerSerial;
            _bannerTotalWidth = 0f;
            _bannerEmblemCount = 0;
            return;
        }

        // 同一条横幅不用重新量
        bool sameBanner = _bannerSerialSeen == _model.BannerSerial;
        _bannerSerialSeen = _model.BannerSerial;
        if (sameBanner && _bannerTotalWidth > 0f) return;

        var ribbon = _model.Banner;
        bool multi = ribbon.Kind == KillAchievementKind.MultiKill && _model.BannerMulti >= 2;

        // 横幅只带**短**详情（「87m」「连杀 x5」）。多杀的「间隔 2.5 秒内第 N 杀」徽记里的骷髅数已经说明了；
        // 「第 4 杀后 8 秒内再杀」这类解释性的长句放进去会把横幅撑得比整张卡还宽，BF6 的横幅也只有一个短标题。
        bool showDetail = !multi
                          && !string.IsNullOrWhiteSpace(ribbon.Detail)
                          && ribbon.Detail.Length <= MaxBannerDetailChars;
        string text = showDetail ? ribbon.Title + " · " + ribbon.Detail : ribbon.Title;
        _bannerString = Escape(text);

        int fontSize = cfg.FontSizeAchievement.Value;
        _bannerText.SetSpacing(3f);
        float textWidth = _bannerText.Measure(_bannerString, fontSize, FontStyles.Bold);
        _bannerBoxWidth = Mathf.Round(textWidth + Mathf.Max(0f, cfg.AchievementPadding.Value) * 2f + AccentWidth);

        _bannerEmblemCount = multi ? Mathf.Clamp(_model.BannerMulti, 2, 5) : 0;
        _bannerAccentColor = ribbon.Tier switch
        {
            KillAchievementTier.Epic => UiTheme.Parse(cfg.ColorAchievementEpic.Value, new Color(0.88f, 0.23f, 0.31f)),
            KillAchievementTier.Great => UiTheme.Parse(cfg.ColorAchievementGreat.Value, new Color(0.88f, 0.5f, 0.12f)),
            KillAchievementTier.Good => UiTheme.Parse(cfg.ColorAchievementGood.Value, new Color(0.18f, 0.48f, 0.84f)),
            _ => UiTheme.Parse(cfg.ColorAchievementNormal.Value, new Color(0.42f, 0.49f, 0.55f)),
        };

        _bannerTotalWidth = _bannerBoxWidth + (_bannerEmblemCount > 0 ? EmblemGap + EmblemWidth(cfg) : 0f);
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
        if (_emblemBaked.HasValue && _fpEmblemCount == _bannerEmblemCount && Mathf.Abs(_fpEmblemSize - skullPx) < 0.01f
            && Mathf.Abs(_fpEmblemUi - cfg.UiScale.Value) < 0.0001f)
        {
            return _emblemBaked;
        }

        _fpEmblemCount = _bannerEmblemCount;
        _fpEmblemSize = skullPx;
        _fpEmblemUi = cfg.UiScale.Value;
        _emblemBaked = _bannerEmblemCount >= 2 ? ProceduralIcons.Emblem(_bannerEmblemCount, skullPx) : null;
        return _emblemBaked;
    }

    private void PrepareScore(AyaConfig cfg)
    {
        if (_score == null) return;
        _scoreWidth = cfg.ShowScore.Value
            ? _score.Measure(ScoreString(_model.TotalScore), cfg.FontSizeScore.Value, FontStyles.Bold) + 10f
            : 0f;
    }

    // ------------------------------------------------------------------ 骷髅视图与模型对账

    private void SyncSkulls()
    {
        var icons = _model.Icons;

        // 先把模型里已经没有的骷髅（被上限挤掉的最老的）还回池子，再去借新的 ——
        // 反过来的话，上限 24 时会同时存在 25 个视图，池子装不下第 25 个，它的三个 GameObject 就成了孤儿
        for (int i = _skulls.Count - 1; i >= 0; i--)
        {
            bool alive = false;
            for (int n = 0; n < icons.Count; n++)
            {
                if (icons[n].Serial != _skulls[i].Serial) continue;
                alive = true;
                break;
            }

            if (alive) continue;
            RecycleSkull(_skulls[i]);
            _skulls.RemoveAt(i);
        }

        _skullScratch.Clear();
        _skullScratch.AddRange(_skulls);
        _skulls.Clear();

        for (int n = 0; n < icons.Count; n++)
        {
            var icon = icons[n];
            SkullView? view = null;
            for (int i = 0; i < _skullScratch.Count; i++)
            {
                if (_skullScratch[i].Serial != icon.Serial) continue;
                view = _skullScratch[i];
                _skullScratch.RemoveAt(i);
                break;
            }

            if (view == null)
            {
                view = RentSkull();
                if (view == null) continue;
                view.Serial = icon.Serial;
                view.Born = icon.Born;
                view.Headshot = icon.Headshot;
                view.X = float.NaN;
                ResolveSkullSprite(view);
            }
            _skulls.Add(view);
        }

        for (int i = 0; i < _skullScratch.Count; i++) RecycleSkull(_skullScratch[i]);
        _skullScratch.Clear();
    }

    private void ResolveSkullSprite(SkullView v)
    {
        var cfg = Plugin.Cfg;
        float size = Mathf.Max(8f, cfg.SkullSize.Value);

        if (cfg.IconStyleMode.Value == IconStyle.CustomFiles)
        {
            v.Sprite = IconBank.SkullFor(v.Headshot);
            v.PadRatio = 1f;
        }
        else
        {
            var baked = ProceduralIcons.Skull(v.Headshot, size);
            if (baked.HasValue)
            {
                v.Sprite = baked.Value.Sprite;
                v.PadRatio = baked.Value.PadRatio;
            }
            else
            {
                v.Sprite = IconBank.SkullFor(v.Headshot);
                v.PadRatio = 1f;
            }
        }

        // 辉光（烘成一张纹理，1 次绘制代替旧版的 17 次叠加）。
        // 程序化骷髅自带深色描边，再叠一圈白色辉光会在深色背景上糊成一个灰色光盘 —— 只给旧版 PNG 图标用。
        Sprite? glowSprite = null;
        float glowPad = 1f;
        if (cfg.EnableIconGlow.Value && v.Sprite != null && cfg.IconStyleMode.Value == IconStyle.CustomFiles)
        {
            int spread = Mathf.Max(1, Mathf.RoundToInt(cfg.IconGlowSize.Value));
            glowSprite = IconBank.GetGlow(v.Sprite, spread);
            if (glowSprite != null) glowPad = IconBank.GlowPadRatio(v.Sprite, spread);
        }
        v.Glow.Img.preserveAspect = true;
        v.Icon.Img.preserveAspect = true;
        v.GlowSprite = glowSprite;
        v.GlowPad = glowPad;
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

        RefreshIconsIfConfigChanged(cfg);
        DrawCard(cfg);

        _ring.Tick(_ringCenterValid ? _ringCenter : Anchor(), now);
    }

    private Vector2 _ringCenter;
    private bool _ringCenterValid;

    private void RefreshIconsIfConfigChanged(AyaConfig cfg)
    {
        float size = cfg.SkullSize.Value;
        float ui = cfg.UiScale.Value;
        var style = cfg.IconStyleMode.Value;
        if (Mathf.Abs(size - _fpSkullSize) < 0.01f && Mathf.Abs(ui - _fpUiScale) < 0.0001f && style == _fpStyle) return;

        _fpSkullSize = size;
        _fpUiScale = ui;
        _fpStyle = style;
        for (int i = 0; i < _skulls.Count; i++) ResolveSkullSprite(_skulls[i]);

        // 横幅的徽记宽度也依赖尺寸
        _bannerSerialSeen = -1;
        PrepareBanner(cfg);
    }

    private KillCardLayoutInput BuildLayoutInput(AyaConfig cfg)
    {
        bool hasBanner = cfg.ShowAchievement.Value && _model.HasBanner && _bannerTotalWidth > 0f;
        bool hasTags = cfg.ShowTags.Value && _tagCount > 0;
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
            TagHeight = TagHeight(cfg),
            DetailsHeight = KillCardLayoutMath.RowHeightFor(cfg.FontSizeKillDetails.Value),
            BadgeSize = cfg.FactionIconSize.Value + BadgePlatePad * 2f,
            BadgeGap = 10f,
            HasBanner = hasBanner,
            HasBar = cfg.ShowStreakBar.Value,
            HasName = cfg.ShowPlayerName.Value,
            HasTags = hasTags,
            HasDetails = hasDetails,
            HasBadge = cfg.ShowFactionIcon.Value && _rankSprite != null,
            SkullCount = cfg.ShowSkull.Value ? _skulls.Count : 0,
            BannerWidth = _bannerTotalWidth,
            NameWidth = _nameWidth,
            TagsWidth = _tagsWidth,
            DetailsWidth = _detailsWidth,
            BannerOverhang = Mathf.Max(0f, cfg.ScoreHorizontalOffset.Value),
        };
    }

    /// <summary>
    /// 文字描边宽度随字号缩放。TMP 的描边宽度是归一化的，同一个数值落在 15px 的小字上会把笔画吃掉一大半
    /// （实测 0.22 的名字 / 详情发暗发细），落在 34px 的分数上才刚好。以 24px 为基准线性缩放，并限幅。
    /// </summary>
    private static float OutlineFor(AyaConfig cfg, int fontSize)
        => Mathf.Clamp01(cfg.CardTextOutline.Value * Mathf.Clamp(fontSize / 24f, 0.7f, 1.3f));

    private static float TagHeight(AyaConfig cfg) => Mathf.Round(Mathf.Max(8f, cfg.FontSizeTag.Value * 1.55f));

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
        DrawSkulls(cfg, anchor, layout, exitAge, exitDur);
        DrawScore(cfg, anchor, layout, exitAge, exitDur);
        DrawName(cfg, anchor, layout, exitAge, exitDur);
        DrawTags(cfg, anchor, layout, exitAge, exitDur);
        DrawDetails(cfg, anchor, layout, exitAge, exitDur);
        DrawBanner(cfg, anchor, layout, exitAge, exitDur);
        DrawBadge(cfg, anchor, layout, exitAge, exitDur);
        LogScreenPositions(cfg, anchor, layout);
    }

    /// <summary>只算入场显影（不含退场）。</summary>
    private void RevealOf(float born, float delay, int seed, in GlitchMath.RevealTiming timing, out float content, out float block)
        => GlitchMath.Reveal(_now - born - delay, seed, timing, _glitch, out content, out block);

    /// <summary>一个梯队当前的退场不透明度。同一个元素的各个部件（框 / 字 / 徽记）必须共用它，闪烁才会同步。</summary>
    private float ExitOf(int group, int seed, float exitAge, float exitDur)
        => GlitchMath.ExitAlpha(group, exitAge, exitDur, seed, _glitch);

    /// <summary>元素当前的「内容不透明度 / 色块不透明度」，已经乘上了它所在梯队的退场不透明度。</summary>
    private void Fades(float born, float delay, int seed, in GlitchMath.RevealTiming timing, int group, float exitAge, float exitDur,
        out float content, out float block)
    {
        GlitchMath.Reveal(_now - born - delay, seed, timing, _glitch, out content, out block);
        float exit = GlitchMath.ExitAlpha(group, exitAge, exitDur, seed, _glitch);
        content *= exit;
        block *= exit;
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

        Fades(_model.StartTime, DelaySkull, SeedSkull + 3, GlitchMath.RevealTiming.Quick, 1, exitAge, exitDur, out float content, out _);
        if (content <= 0.003f)
        {
            _barBack.Hide();
            _barFill.Hide();
            return;
        }

        // 进度条至少和骷髅行一样宽：连杀多了骷髅排得很长，一条 120px 的短线挂在下面很突兀
        float skullRow = cfg.ShowSkull.Value && _skulls.Count > 0
            ? Mathf.Max(8f, cfg.SkullSize.Value) + (_skulls.Count - 1) * layout.SkullPitch
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

    // ------------------------------------------------------------------ 骷髅

    private void DrawSkulls(AyaConfig cfg, Vector2 anchor, in KillCardLayout layout, float exitAge, float exitDur)
    {
        if (!cfg.ShowSkull.Value)
        {
            for (int i = 0; i < _skulls.Count; i++) HideSkull(_skulls[i]);
            return;
        }

        float size = Mathf.Max(8f, cfg.SkullSize.Value);
        float popScale = Mathf.Max(1f, cfg.SkullPopScale.Value);
        float popDuration = Mathf.Max(0.01f, cfg.SkullPopDuration.Value);
        float pushSpeed = Mathf.Max(0.01f, cfg.SkullPushAnimationSpeed.Value);
        int count = _skulls.Count;
        bool procedural = cfg.IconStyleMode.Value == IconStyle.Procedural;

        for (int i = 0; i < count; i++)
        {
            var v = _skulls[i];
            int slot = count - 1 - i;   // 0 = 最新，紧贴脊线
            float targetX = anchor.x + layout.SkullCenterX(slot);

            if (float.IsNaN(v.X)) v.X = targetX;
            else if (Mathf.Abs(v.X - targetX) > 0.1f) v.X = Mathf.Lerp(v.X, targetX, Mathf.Clamp01(_dt * pushSpeed));
            else v.X = targetX;

            Fades(v.Born, DelaySkull, SeedSkull + v.Serial * 13, GlitchMath.RevealTiming.Standard, 0, exitAge, exitDur,
                out float content, out float block);

            if (content <= 0.003f && block <= 0.003f)
            {
                HideSkull(v);
                continue;
            }

            float age = _now - v.Born;
            float pop = UiAnim.OutBack(UiAnim.Clamp01(age / popDuration));
            float scale = popScale + (1f - popScale) * pop;

            Vector2 center = new Vector2(v.X, anchor.y + layout.MainY);
            Color tint = procedural
                ? (v.Headshot ? Coral : IceWhite)
                : Color.white;

            float boxH = size * v.PadRatio;

            // 辉光在底层
            if (v.GlowSprite != null && content > 0.003f)
            {
                var glowColor = UiTheme.WithAlpha(
                    UiTheme.Parse(cfg.IconGlowColor.Value, Color.white),
                    Mathf.Clamp01(cfg.IconGlowIntensity.Value));
                v.Glow.Apply(center, new Vector2(boxH * v.GlowPad, boxH * v.GlowPad), v.GlowSprite, glowColor, scale);
                v.Glow.SetFade(content);
            }
            else
            {
                v.Glow.Hide();
            }

            if (content > 0.003f && v.Sprite != null)
            {
                v.Icon.Apply(center, new Vector2(boxH, boxH), v.Sprite, tint, scale);
                v.Icon.SetFade(content);
            }
            else
            {
                v.Icon.Hide();
            }

            if (block > 0.003f)
            {
                v.Flash.Apply(center, new Vector2(size * 0.92f, size), UiTheme.WhiteSprite, UiTheme.WithAlpha(procedural ? tint : IceWhite, 1f), scale);
                v.Flash.SetFade(block);
            }
            else
            {
                v.Flash.Hide();
            }

            // 最新的骷髅就是环形特效的中心
            if (slot == 0)
            {
                _ringCenter = center;
                _ringCenterValid = true;
            }
        }
    }

    // ------------------------------------------------------------------ 得分

    private void DrawScore(AyaConfig cfg, Vector2 anchor, in KillCardLayout layout, float exitAge, float exitDur)
    {
        if (_score == null || _scoreFlash == null) return;

        if (!cfg.ShowScore.Value || _model.TotalScore <= 0)
        {
            _score.Hide();
            _scoreFlash.Hide();
            return;
        }

        // 入场显影只在卡片的第一杀做；之后每一杀只做「数字滚动 + 弹出」，分数一直在场
        Fades(_model.StartTime, DelayScore, SeedScore, GlitchMath.RevealTiming.Quick, 2, exitAge, exitDur,
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

        _score.SetOutline(OutlineFor(cfg, cfg.FontSizeScore.Value), TextOutlineColor);
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
            _scoreFlash.Apply(new Vector2(left + w * 0.5f - 2f, y), new Vector2(w, rowH * 0.8f), UiTheme.WhiteSprite, IceWhite, scale);
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

        Fades(_model.VictimBorn, DelayName, SeedName + _model.VictimSerial * 7, GlitchMath.RevealTiming.Standard, 0, exitAge, exitDur,
            out float content, out float block);

        if (content <= 0.003f && block <= 0.003f)
        {
            _name.Hide();
            _nameFlash.Hide();
            return;
        }

        float rowH = KillCardLayoutMath.RowHeightFor(cfg.FontSizePlayerName.Value);
        float y = anchor.y + layout.NameY;
        Color nameColor = UiTheme.Parse(cfg.ColorPlayerName.Value, new Color(1f, 0.35f, 0.3f));

        _name.SetOutline(OutlineFor(cfg, cfg.FontSizePlayerName.Value), TextOutlineColor);
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

    // ------------------------------------------------------------------ 标签

    private void DrawTags(AyaConfig cfg, Vector2 anchor, in KillCardLayout layout, float exitAge, float exitDur)
    {
        var box = ProceduralIcons.TagBox();

        if (!cfg.ShowTags.Value || _tagCount == 0 || box == null)
        {
            for (int i = 0; i < MaxTags; i++)
            {
                _tagRows[i]?.Hide();
                _tagFlash[i]?.Hide();
            }
            return;
        }

        int fontSize = cfg.FontSizeTag.Value;
        float height = TagHeight(cfg);
        float y = Mathf.Round(anchor.y + layout.TagY);

        // 从脊线往左排：最后一个标签的右缘贴脊线
        float right = anchor.x;
        for (int j = _tagCount - 1; j >= 0; j--)
        {
            var tag = _tags[j];
            var row = _tagRows[j];
            var flash = _tagFlash[j];
            if (row == null || flash == null) continue;

            float center = Mathf.Round(right - tag.Width * 0.5f);
            right -= tag.Width + TagGap;

            Fades(tag.Born, DelayTag, tag.Seed, GlitchMath.RevealTiming.Quick, 1, exitAge, exitDur, out float content, out float block);

            if (content <= 0.003f && block <= 0.003f)
            {
                row.Hide();
                flash.Hide();
                continue;
            }

            row.Apply(new Vector2(center, y), tag.Width, height, box, tag.Text, fontSize, tag.Border, tag.Border);
            row.SetFade(content);

            if (block > 0.003f)
            {
                flash.Apply(new Vector2(center, y), new Vector2(tag.Width, height), UiTheme.WhiteSprite, tag.Border);
                flash.SetFade(block);
            }
            else
            {
                flash.Hide();
            }
        }

        for (int j = _tagCount; j < MaxTags; j++)
        {
            _tagRows[j]?.Hide();
            _tagFlash[j]?.Hide();
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

        Fades(_model.VictimBorn, DelayDetails, SeedDetails + _model.VictimSerial * 3, GlitchMath.RevealTiming.Quick, 1, exitAge, exitDur,
            out float content, out _);

        if (content <= 0.003f)
        {
            _details.Hide();
            return;
        }

        float rowH = KillCardLayoutMath.RowHeightFor(cfg.FontSizeKillDetails.Value);
        _details.SetOutline(OutlineFor(cfg, cfg.FontSizeKillDetails.Value), TextOutlineColor);
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

    // ------------------------------------------------------------------ 横幅

    private void DrawBanner(AyaConfig cfg, Vector2 anchor, in KillCardLayout layout, float exitAge, float exitDur)
    {
        if (_bannerBox == null || _bannerAccent == null || _bannerText == null || _bannerFlash == null || _emblem == null) return;

        bool wanted = cfg.ShowAchievement.Value && _model.HasBanner && _bannerTotalWidth > 0f;
        if (!wanted)
        {
            HideBanner();
            return;
        }

        int seed = SeedBanner + _model.BannerSerial * 5;

        // 框 / 字 / 徽记共用同一个退场不透明度，闪烁才同步 —— 否则会出现「只剩一根灰条、字已经灭了」
        float exit = ExitOf(1, seed, exitAge, exitDur);

        RevealOf(_model.BannerBorn, DelayBanner, seed, GlitchMath.RevealTiming.Standard, out float boxContent, out float boxBlock);
        boxContent *= exit;
        boxBlock *= exit;

        if (boxContent <= 0.003f && boxBlock <= 0.003f)
        {
            HideBanner();
            return;
        }

        float h = Mathf.Max(8f, cfg.AchievementHeight.Value);
        float y = anchor.y + layout.BannerY;

        float emblemW = 0f;
        float emblemH = 0f;
        BakedIcon? emblem = _bannerEmblemCount >= 2 ? ResolveEmblem(cfg) : null;
        if (emblem.HasValue)
        {
            emblemH = EmblemHeight(cfg, emblem.Value);
            emblemW = emblemH * (emblem.Value.Sprite.rect.width / Mathf.Max(1f, emblem.Value.Sprite.rect.height));
        }

        float rightEdge = anchor.x + Mathf.Max(0f, cfg.ScoreHorizontalOffset.Value);
        float boxRight = rightEdge - (emblemW > 0f ? emblemW + EmblemGap : 0f);
        float boxCenterX = Mathf.Round(boxRight - _bannerBoxWidth * 0.5f);
        float boxLeft = boxCenterX - _bannerBoxWidth * 0.5f;

        _bannerBox.Apply(new Vector2(boxCenterX, y), new Vector2(_bannerBoxWidth, h), UiTheme.WhiteSprite, BannerFill);
        _bannerBox.SetFade(boxContent);

        _bannerAccent.Apply(new Vector2(boxLeft + AccentWidth * 0.5f, y), new Vector2(AccentWidth, h), UiTheme.WhiteSprite, _bannerAccentColor);
        _bannerAccent.SetFade(boxContent);

        // 文字比底晚一点点显影，更有「先出框、再出字」的层次
        RevealOf(_model.BannerBorn, DelayBanner + 0.04f, seed + 1, GlitchMath.RevealTiming.Quick, out float textContent, out _);
        _bannerText.SetSpacing(3f);
        _bannerText.Apply(
            new Vector2(boxCenterX + AccentWidth * 0.5f, y),
            new Vector2(_bannerBoxWidth, h),
            _bannerString,
            cfg.FontSizeAchievement.Value,
            UiTheme.Parse(cfg.ColorAchievementText.Value, new Color(0.18f, 0.24f, 0.31f)),
            FontStyles.Bold,
            TextAlignmentOptions.Center);
        _bannerText.SetFade(textContent * exit);

        if (boxBlock > 0.003f)
        {
            _bannerFlash.Apply(new Vector2(boxCenterX, y), new Vector2(_bannerBoxWidth, h), UiTheme.WhiteSprite, BannerFill);
            _bannerFlash.SetFade(boxBlock);
        }
        else
        {
            _bannerFlash.Hide();
        }

        if (emblem.HasValue)
        {
            RevealOf(_model.BannerBorn, DelayEmblem, seed + 2, GlitchMath.RevealTiming.Standard, out float emblemContent, out _);
            emblemContent *= exit;
            if (emblemContent > 0.003f)
            {
                // 位图里主骷髅的中心比位图中心低（上面留了冒头骷髅的位置），抬回来让它和横幅竖直居中
                Vector2 center = new Vector2(rightEdge - emblemW * 0.5f, y + EmblemSkullPx(cfg) * IconShapes.EmblemCenterShift);
                _emblem.Apply(center, new Vector2(emblemW, emblemH), emblem.Value.Sprite, IceWhite);
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

    private void HideBanner()
    {
        _bannerBox?.Hide();
        _bannerAccent?.Hide();
        _bannerText?.Hide();
        _bannerFlash?.Hide();
        _emblem?.Hide();
    }

    // ------------------------------------------------------------------ 军衔徽章

    private void DrawBadge(AyaConfig cfg, Vector2 anchor, in KillCardLayout layout, float exitAge, float exitDur)
    {
        if (_badgePlate == null || _badge == null || _badgeFlash == null || _badgeChip == null || _badgeLevel == null) return;

        var plateSprite = ProceduralIcons.TagBox();
        if (!cfg.ShowFactionIcon.Value || _rankSprite == null || plateSprite == null)
        {
            HideBadge();
            return;
        }

        int seed = SeedBadge + _model.VictimSerial * 11;
        RevealOf(_model.VictimBorn, DelayBadge, seed, GlitchMath.RevealTiming.Standard, out float content, out float block);
        float exit = ExitOf(1, seed, exitAge, exitDur);
        content *= exit;
        block *= exit;

        if (content <= 0.003f && block <= 0.003f)
        {
            HideBadge();
            return;
        }

        float icon = Mathf.Max(8f, cfg.FactionIconSize.Value);
        float plate = icon + BadgePlatePad * 2f;

        // 内容变宽 / 变窄时徽章平滑地挪到新位置（和骷髅的推挤同一个速度）
        float targetX = anchor.x + layout.BadgeX;
        if (float.IsNaN(_badgeX)) _badgeX = targetX;
        else if (Mathf.Abs(_badgeX - targetX) > 0.1f) _badgeX = Mathf.Lerp(_badgeX, targetX, Mathf.Clamp01(_dt * Mathf.Max(0.01f, cfg.SkullPushAnimationSpeed.Value)));
        else _badgeX = targetX;

        float y = anchor.y + layout.BadgeY + cfg.FactionIconVerticalOffset.Value;
        Vector2 center = new Vector2(Mathf.Round(_badgeX), Mathf.Round(y));

        float age = _now - (_model.VictimBorn + DelayBadge);
        float pop = UiAnim.OutBack(UiAnim.Clamp01(age / 0.16f));
        float scale = 1.18f + (1f - 1.18f) * pop;

        if (content > 0.003f)
        {
            // 底板：战地标签同款的描边方框，描边取阵营色（USEC 蓝 / BEAR 橙 …），底是半透明黑 ——
            // 军衔图本身是米灰色的浅浮雕，直接放在浅色背景上几乎看不见，垫一块深色底就醒目了
            _badgePlate.Apply(center, new Vector2(plate, plate), plateSprite, _factionColor, scale);
            _badgePlate.SetFade(content);

            _badge.Img.preserveAspect = true;
            _badge.Apply(center, new Vector2(icon, icon), _rankSprite, Color.white, scale);
            _badge.SetFade(content);

            // 等级小标：压在底板右下角
            if (cfg.ShowLevelChip.Value && !string.IsNullOrEmpty(_levelText))
            {
                float chipW = Mathf.Round(_levelWidth + 12f);
                float chipH = BadgeChipHeight;
                Vector2 chipCenter = center + new Vector2(plate * 0.5f - chipW * 0.5f + 3f, -plate * 0.5f + chipH * 0.5f - 2f) * scale;
                _badgeChip.SetSliced(true);
                _badgeChip.Apply(chipCenter, new Vector2(chipW, chipH), plateSprite, _factionColor, scale);
                _badgeChip.SetFade(content);
                _badgeLevel.Apply(chipCenter, new Vector2(chipW, chipH), _levelText, 13, Color.white, FontStyles.Bold, TextAlignmentOptions.Center, scale);
                _badgeLevel.SetFade(content);
            }
            else
            {
                _badgeChip.Hide();
                _badgeLevel.Hide();
            }
        }
        else
        {
            _badgePlate.Hide();
            _badge.Hide();
            _badgeChip.Hide();
            _badgeLevel.Hide();
        }

        if (block > 0.003f)
        {
            _badgeFlash.Apply(center, new Vector2(plate * 0.9f, plate * 0.9f), UiTheme.WhiteSprite, _factionColor, scale);
            _badgeFlash.SetFade(block * 0.45f);
        }
        else
        {
            _badgeFlash.Hide();
        }
    }

    private void HideBadge()
    {
        _badgePlate?.Hide();
        _badge?.Hide();
        _badgeFlash?.Hide();
        _badgeChip?.Hide();
        _badgeLevel?.Hide();
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
            $"徽章={(_badge != null ? _badge.Rt.position.ToString() : "-")} " +
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
        for (int i = 0; i < _skulls.Count; i++) RecycleSkull(_skulls[i]);
        _skulls.Clear();
        _shownScore = 0;
        _badgeX = float.NaN;
        _barShown = float.NaN;
        _bannerSerialSeen = -1;
        _bannerTotalWidth = 0f;
        _bannerEmblemCount = 0;
        _nameRich = string.Empty;
        _detailsText = string.Empty;
        _rankSprite = null;
        _tagCount = 0;
        _ringCenterValid = false;
    }

    private void HideCard()
    {
        _barBack?.Hide();
        _barFill?.Hide();
        HideBadge();
        HideBanner();
        _score?.Hide();
        _scoreFlash?.Hide();
        _name?.Hide();
        _nameFlash?.Hide();
        _details?.Hide();
        for (int i = 0; i < MaxTags; i++)
        {
            _tagRows[i]?.Hide();
            _tagFlash[i]?.Hide();
        }
        for (int i = 0; i < _skulls.Count; i++) HideSkull(_skulls[i]);
    }

    // ------------------------------------------------------------------ 骷髅池

    private static void HideSkull(SkullView v)
    {
        v.Icon.Hide();
        v.Glow.Hide();
        v.Flash.Hide();
    }

    private void RecycleSkull(SkullView v)
    {
        HideSkull(v);
        if (_skullPool.Count < 24) _skullPool.Add(v);
    }

    private SkullView? RentSkull()
    {
        if (_skullPool.Count > 0)
        {
            var pooled = _skullPool[_skullPool.Count - 1];
            _skullPool.RemoveAt(_skullPool.Count - 1);
            return pooled;
        }

        if (_parent == null) return null;

        return new SkullView
        {
            // 辉光在最底层，骷髅在中间，色块在最上
            Glow = UiImageRow.Create("AyaHitmarker.SkullGlow", _parent),
            Icon = UiImageRow.Create("AyaHitmarker.Skull", _parent),
            Flash = UiImageRow.Create("AyaHitmarker.SkullFlash", _parent),
        };
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
