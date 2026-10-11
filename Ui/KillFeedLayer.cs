using System;
using System.Collections.Generic;
using System.Text;
using AyaHitmarker.Game;
using TMPro;
using UnityEngine;

namespace AyaHitmarker.Ui;

/// <summary>
/// 击杀播报卡片。在旧项目那套「阵营 / 骷髅 / 名字 / 详情」的基础上，按战地 6 的击杀反馈补上
/// **得分弹窗**、**成就缎带**（复仇者 / 狂战士 / 双杀 …）与**连杀进度条**，并给所有元素加动效。
///
/// 布局（锚点在屏幕中心左侧 <c>KillFeedHorizontalOffset</c> 像素、垂直 <c>KillFeedVerticalOffset</c> 处）：
/// <code>
///                        ▏复仇者 · 142m ▕          ← 成就缎带（只有触发成就时才出现）
///                    [☠] [等级] 阵营 [军衔徽章]      ← 阵营行（右对齐于锚点上方）
///              ☠ ☠ ☠  +150                          ← 骷髅行（向左生长）+ 得分弹窗（锚点右侧）
///         ────────────────────────                  ← 连杀进度条
///                        玩家名字                    ← 右对齐于锚点下方
///              头部 · 142m · M4A1 · 枪械             ← 再下一行
/// </code>
///
/// 动效（对齐 AGENTS.md §3.5 的「每帧写入纪律」）：
/// <list type="bullet">
///   <item>入场：各行按 <c>CardRowStagger</c> 依次从右侧 <c>CardEnterDistance</c> 像素滑入，
///     透明度走五次方缓出；</item>
///   <item>骷髅：新骷髅用 <c>SkullPopScale</c> 回弹弹出，连杀时已有骷髅整体左移（<c>SkullPushAnimationSpeed</c> 平滑）；</item>
///   <item>得分：从 0 滚到最终值（<c>ScoreCountDuration</c>），并向上漂移 <c>ScoreRiseDistance</c>；</item>
///   <item>成就缎带：从左往右「擦除式」展开（<c>OutBack</c> 带过冲），停留期间轻微呼吸，最后淡出；</item>
///   <item>退场：显示时长结束前 <c>CardExitDuration</c> 秒整体淡出并轻微上移。</item>
/// </list>
///
/// 所有位置/尺寸/颜色写入都经过 <see cref="UiTextRow"/> / <see cref="UiImageRow"/> 的脏检查，
/// 停止变化后就不再产生 uGUI 重建。
/// </summary>
internal sealed class KillFeedLayer
{
    /// <summary>各行在入场动画里的顺序（越小越先出现）。</summary>
    private const int RowAchievement = 0;
    private const int RowScore = 1;
    private const int RowFaction = 2;
    private const int RowName = 3;
    private const int RowDetails = 4;

    private sealed class SkullEntry
    {
        internal UiImageRow Icon = null!;
        internal UiImageRow Glow = null!;
        internal Sprite? Sprite;
        internal float Start;
        internal float TargetOffset;
        internal float CurrentOffset;
    }

    private readonly List<SkullEntry> _skulls = new List<SkullEntry>(8);
    private readonly List<SkullEntry> _pool = new List<SkullEntry>(8);
    private readonly StringBuilder _sb = new StringBuilder(160);
    private readonly RingEffect _ring = new RingEffect();

    private RectTransform? _parent;

    private UiTextRow? _factionText;
    private UiImageRow? _factionIcon;
    private UiTextRow? _nameText;
    private UiTextRow? _detailsText;
    private UiTextRow? _scoreText;
    private UiImageRow? _scoreIcon;
    private UiImageRow? _achievementBg;
    private UiTextRow? _achievementText;
    private UiImageRow? _streakBarBack;
    private UiImageRow? _streakBarFill;

    private KillRecord _latest;
    private KillAnalysis _analysis;
    private float _latestStart;

    /// <summary>
    /// 卡片文字的存活时长。不一定等于 <c>KillFeedDuration</c>：
    /// 成就缎带自己的「出现 + 停留 + 淡出」可能更长，这时取两者较大值，
    /// 否则会出现「缎带还在展开、整个卡片已经淡完了」的视觉断裂。
    /// </summary>
    private float _textDuration = 6f;

    /// <summary>
    /// 文本层是否处于显示状态。
    ///
    /// **这里是修复的 bug**：旧实现用 <c>_factionGo.activeSelf || _nameGo.activeSelf</c> 反推，
    /// 而「让它们 active」这件事本身发生在 <c>DrawText</c> 里 —— 于是第一次击杀时判断恒为 false、
    /// <c>DrawText</c> 永远不被调用，整个击杀播报的文字（含军衔图标）从来没画出来过。
    /// 现在用一个显式状态位，不再从 GameObject 状态反推。
    /// </summary>
    private bool _textVisible;

    /// <summary>得分文字缓存：伤害/得分都是小整数，重复度极高，避免每帧 ToString 分配。</summary>
    private static readonly Dictionary<int, string> ScoreCache = new Dictionary<int, string>(128);

    // ------------------------------------------------------------------ 构建

    internal void Attach(RectTransform parent)
    {
        _parent = parent;
        _ring.Attach(parent);

        _factionText = UiTextRow.Create("AyaHitmarker.KillFaction", parent, TextAlignmentOptions.Right);
        _factionIcon = UiImageRow.Create("AyaHitmarker.KillFactionIcon", parent);
        _nameText = UiTextRow.Create("AyaHitmarker.KillName", parent, TextAlignmentOptions.Right);
        _detailsText = UiTextRow.Create("AyaHitmarker.KillDetails", parent, TextAlignmentOptions.Right);

        _scoreIcon = UiImageRow.Create("AyaHitmarker.KillScoreIcon", parent);
        _scoreText = UiTextRow.Create("AyaHitmarker.KillScore", parent, TextAlignmentOptions.Left);

        _achievementBg = UiImageRow.Create("AyaHitmarker.KillAchievementBg", parent);
        _achievementText = UiTextRow.Create("AyaHitmarker.KillAchievementText", parent, TextAlignmentOptions.Center);

        _streakBarBack = UiImageRow.Create("AyaHitmarker.KillStreakBarBack", parent);
        _streakBarFill = UiImageRow.Create("AyaHitmarker.KillStreakBarFill", parent);
        // 填充条从左往右长：pivot 钉在左边
        _streakBarFill.Apply(Vector2.zero, new Vector2(1f, 1f), UiTheme.WhiteSprite, Color.clear, 1f, new Vector2(0f, 0.5f));

        HideCard();
    }

    // ------------------------------------------------------------------ 新增击杀

    /// <param name="rec">击杀快照。</param>
    /// <param name="analysis">由 <see cref="KillStreakTracker"/> 算好的成就/连杀结果。</param>
    internal void Show(in KillRecord rec, in KillAnalysis analysis)
    {
        if (_parent == null) return;

        var cfg = Plugin.Cfg;
        float now = Time.unscaledTime;

        _latest = rec;
        _analysis = analysis;
        _latestStart = now;
        _textVisible = true;
        _textDuration = ComputeTextDuration(cfg);
        _layoutLoggedFrame = -1;

        if (cfg.ShowSkull.Value)
        {
            float skullDisplay = Mathf.Max(0.1f, cfg.SkullDisplayDuration.Value);
            if (analysis.Streak > 1) PushPreviousSkulls(now, skullDisplay, cfg);

            var entry = Rent();
            if (entry != null)
            {
                entry.Start = now;
                entry.TargetOffset = 0f;
                entry.CurrentOffset = 0f;
                ApplySkullSprite(entry, rec.Headshot);
                _skulls.Add(entry);
            }
        }

        SetStreakProgress(analysis.StreakProgress);

        // 环形特效：爆头必定触发；史诗级成就（多杀/复仇/首领）用爆炸环，冲击感更强
        bool epic = analysis.Ribbon.Tier == KillAchievementTier.Epic || analysis.MultiCount >= 4;
        bool trigger = rec.Headshot || epic;
        KillType type = epic ? KillType.Explosion : KillType.Headshot;
        _ring.Trigger(
            now,
            cfg.EnableRingEffect.Value && cfg.RingHeadshotEnabled.Value && trigger,
            type);

        PrepareCaseTexts(cfg);
        LogLayout(cfg);

        TrimToLimit(cfg.KillFeedMaxSkulls.Value);
    }

    private void PushPreviousSkulls(float now, float skullDisplayDuration, AyaConfig cfg)
    {
        float shift = Mathf.Max(0f, cfg.SkullSize.Value + cfg.SkullSpacing.Value);
        for (int i = 0; i < _skulls.Count; i++)
        {
            var s = _skulls[i];
            if (now - s.Start < skullDisplayDuration) s.TargetOffset += shift;
        }
    }

    private void ApplySkullSprite(SkullEntry entry, bool headshot)
    {
        var cfg = Plugin.Cfg;
        float skullSize = Mathf.Max(8f, cfg.SkullSize.Value);

        var icon = Assets.IconBank.SkullFor(headshot);
        entry.Sprite = icon;
        if (entry.Icon.Img != null) entry.Icon.Img.preserveAspect = true;

        // ---- 辉光（烘成一张纹理，1 次绘制代替旧版的 17 次叠加） ----
        Sprite? glowSprite = null;
        float glowPad = 1f;
        if (cfg.EnableIconGlow.Value && icon != null)
        {
            int spread = Mathf.Max(1, Mathf.RoundToInt(cfg.IconGlowSize.Value));
            glowSprite = Assets.IconBank.GetGlow(icon, spread);
            if (glowSprite != null) glowPad = Assets.IconBank.GlowPadRatio(icon, spread);
        }

        entry.Icon.Apply(Vector2.zero, new Vector2(skullSize, skullSize), icon, Color.clear);
        entry.Glow.Apply(Vector2.zero, new Vector2(skullSize * glowPad, skullSize * glowPad), glowSprite, Color.clear);
    }

    private void LogLayout(AyaConfig cfg)
    {
        if (!cfg.DebugEnabled.Value) return;

        var anchor = Anchor();
        Plugin.Logger.LogInfo(
            $"[kill] 卡片布局: anchor=({anchor.x:0.#},{anchor.y:0.#}) " +
            $"ribbon={_achievementWidth:0.#} " +
            $"canvas={(Plugin.CanvasRectSize.x):0.#}x{(Plugin.CanvasRectSize.y):0.#} " +
            $"宽度={_achievementComposed}");
    }

    /// <summary>
    /// 把一行的**实际屏幕坐标**写进日志。
    ///
    /// <c>anchoredPosition</c> 只是局部坐标；落在屏幕哪个像素还叠着「画布原点 + canvas scaleFactor」
    /// 两层换算，光看局部坐标算不出来。ScreenSpaceOverlay 下 <c>RectTransform.position</c>
    /// 就是屏幕像素坐标，但**原点在左下角** —— 和截图的左上角原点差一个 Y 翻转，别搞反。
    /// 击杀后 0.5 秒才量：入场动画没走完时行还没被写入位置，量到的会是上一次的值。
    /// </summary>
    private void LogRowScreenPosition(AyaConfig cfg, float elapsed)
    {
        if (!cfg.DebugEnabled.Value || _layoutLoggedFrame == Time.frameCount) return;
        if (elapsed < 0.5f) return;
        if (_factionText == null || _nameText == null || _detailsText == null) return;

        _layoutLoggedFrame = Time.frameCount;
        Plugin.Logger.LogInfo(
            $"[kill] 屏幕坐标(左下原点): 阵营={_factionText.Rt.position} 名字={_nameText.Rt.position} " +
            $"详情={_detailsText.Rt.position} screen={Screen.width}x{Screen.height} canvasRect={Plugin.CanvasRectSize}");
    }

    private int _layoutLoggedFrame = -1;

    /// <summary>
    /// 把这一次击杀要显示的所有文本**一次性**拼好：阵营行、名字、详情行、成就缎带。
    /// 缎带还要实测一次宽度（背景要贴合文字），走 <c>ForceMeshUpdate</c> + <c>preferredWidth</c>，
    /// 和伤害数字同一个做法。
    /// </summary>
    private void PrepareCaseTexts(AyaConfig cfg)
    {
        _nameComposed = string.IsNullOrWhiteSpace(_latest.VictimName) ? "Unknown" : Escape(_latest.VictimName);

        string botType = _latest.BotType;
        string factionLabel = cfg.ShowBossName.Value && _latest.VictimIsBoss
            ? BotTypeMapping.DisplayName(_latest.Role, botType)
            : botType;
        _factionComposed = ComposeFaction(cfg, factionLabel);

        _rankSprite = cfg.ShowFactionIcon.Value && _latest.IsPmc
            ? Assets.IconBank.GetRankIcon(_latest.VictimLevel)
            : null;

        _detailsComposed = ComposeDetails(cfg);

        if (_achievementText == null || _achievementBg == null) return;

        if (!cfg.ShowAchievement.Value || !_analysis.Ribbon.HasTitle)
        {
            _achievementWidth = 0f;
            _achievementComposed = string.Empty;
            return;
        }

        var ribbon = _analysis.Ribbon;
        string text = string.IsNullOrWhiteSpace(ribbon.Detail)
            ? ribbon.Title
            : ribbon.Title + " · " + ribbon.Detail;

        int fontSize = cfg.FontSizeAchievement.Value;
        string rich = Escape(text);          // TMP 富文本转义，拼串一次就好
        _achievementText.Text.fontSize = fontSize;
        _achievementText.Text.fontStyle = FontStyles.Bold;
        _achievementText.Text.text = rich;
        _achievementText.Text.ForceMeshUpdate();

        float measured = _achievementText.Text.preferredWidth;
        if (!(measured > 1f)) measured = text.Length * fontSize * 0.6f;

        _achievementWidth = measured + Mathf.Max(0f, cfg.AchievementPadding.Value) * 2f;

        // 缎带文字每杀只拼一次。DrawAchievement 每帧都会调用 Apply，
        // 在那儿拼串就是每帧一次 GC 分配（AGENTS.md §3.5 明确禁止）。
        _achievementComposed = rich;
    }

    private float _achievementWidth;
    private string _achievementComposed = string.Empty;

    // 每杀只拼一次的展示文本。DrawXxx 每帧都会调 Apply，如果在里面拼串，
    // 整张卡片存活的 6 秒里就是每秒几十次 GC 分配（AGENTS.md §3.5）。
    // 代价：改配色 / 首领名称开关要等**下一次击杀**才生效。
    private string _factionComposed = string.Empty;
    private string _nameComposed = string.Empty;
    private string _detailsComposed = string.Empty;
    private Sprite? _rankSprite;

    /// <summary>文字层的实际存活时长（见 <see cref="_textDuration"/>）。</summary>
    private float ComputeTextDuration(AyaConfig cfg)
    {
        float duration = Mathf.Max(0.5f, cfg.KillFeedDuration.Value);
        if (!cfg.ShowAchievement.Value || !_analysis.Ribbon.HasTitle) return duration;

        float achievementTotal = Mathf.Max(0.01f, cfg.AchievementEnterDuration.Value)
                                 + Mathf.Max(0f, cfg.AchievementHoldDuration.Value)
                                 + Mathf.Max(0.01f, cfg.AchievementFadeDuration.Value);
        return Mathf.Max(duration, achievementTotal);
    }

    // ------------------------------------------------------------------ 每帧

    internal void Tick()
    {
        var cfg = Plugin.Cfg;

        if (!cfg.KillFeedEnabled.Value)
        {
            if (_textVisible || _skulls.Count > 0 || _ring.IsActive) Reset();
            return;
        }

        float now = Time.unscaledTime;
        float dt = Time.unscaledDeltaTime;
        float skullDisplay = Mathf.Max(0.1f, cfg.SkullDisplayDuration.Value);

        // ---- 骷髅过期回收 ----
        for (int i = _skulls.Count - 1; i >= 0; i--)
        {
            var s = _skulls[i];
            if (now - s.Start > skullDisplay)
            {
                Recycle(s);
                _skulls.RemoveAt(i);
            }
        }

        // 「已经没有任何东西要画」的早退。
        //
        // **必须在把 _textVisible 置 false 之前判断。** 旧写法把这一行放在置 false 之后，
        // 于是「文字刚好过期」的那一帧：_textVisible 刚被置 false、骷髅也早已回收（骷髅只活 2 秒、
        // 文字活 6 秒）、环形特效也结束了 → 直接 return，下面的 HideCard() **永远不会执行**。
        // 结果：卡片停在淡出末帧的那个很低的不透明度上，再也不消失（实测 bug）。
        // 放在这里，过期那一帧 _textVisible 仍为 true → 走到 else 分支 → HideCard() 正常收尾。
        if (!_textVisible && _skulls.Count == 0 && !_ring.IsActive) return;

        bool wasVisible = _textVisible;
        if (wasVisible && now - _latestStart > _textDuration) _textVisible = false;

        UpdateSkulls(now, dt, cfg);

        if (_textVisible)
        {
            DrawCard(cfg, dt);
        }
        else
        {
            HideCard();
            if (wasVisible && cfg.DebugEnabled.Value)
            {
                // 把「行是不是真的被关掉了」也打出来 —— 这个 bug 的验收点就是它，
                // 只打「已隐藏」无法区分「HideCard 跑了但没生效」和「根本没跑」。
                // GameObject 非 active 就绝对不会渲染，不留任何低透明度的残影。
                Plugin.Logger.LogInfo(
                    $"[kill] 卡片已隐藏（存活 {now - _latestStart:0.##}s, " +
                    $"阵营行 active={_factionText?.Go.activeSelf}, 名字行 active={_nameText?.Go.activeSelf}）");
            }
        }

        // 环形特效每帧只驱动一次：优先挂在最新那个骷髅的中心，没有骷髅（关了显示）时退到锚点
        _ring.Tick(_ringCenterValid ? _ringCenter : Anchor(), now);
    }

    /// <summary>最新骷髅的中心（<see cref="_ringCenterValid"/> 为 false 时无意义）。</summary>
    private Vector2 _ringCenter;
    private bool _ringCenterValid;

    private void UpdateSkulls(float now, float dt, AyaConfig cfg)
    {
        float skullSize = Mathf.Max(8f, cfg.SkullSize.Value);
        float skullDisplay = Mathf.Max(0.1f, cfg.SkullDisplayDuration.Value);
        float skullFade = Mathf.Max(0.01f, cfg.SkullFadeDuration.Value);
        float pushSpeed = Mathf.Max(0.01f, cfg.SkullPushAnimationSpeed.Value);
        float popScale = Mathf.Max(1f, cfg.SkullPopScale.Value);
        float popDuration = Mathf.Max(0.01f, cfg.SkullPopDuration.Value);
        Vector2 anchor = Anchor();

        _ringCenterValid = false;

        for (int i = 0; i < _skulls.Count; i++)
        {
            var s = _skulls[i];

            if (Mathf.Abs(s.CurrentOffset - s.TargetOffset) > 0.1f)
            {
                s.CurrentOffset = Mathf.Lerp(s.CurrentOffset, s.TargetOffset, dt * pushSpeed);
            }
            else
            {
                s.CurrentOffset = s.TargetOffset;
            }

            float elapsed = now - s.Start;
            float alpha = 1f;
            if (elapsed >= skullDisplay - skullFade)
            {
                float fadeProgress = (elapsed - (skullDisplay - skullFade)) / skullFade;
                alpha = Mathf.Clamp01(1f - fadeProgress);
            }
            if (elapsed >= skullDisplay) alpha = 0f;

            // 旧项目：skullX = anchorX - skullSize - offset，中心 = 左边 + size/2
            var center = new Vector2(anchor.x - skullSize * 0.5f - s.CurrentOffset, anchor.y);

            float pop = UiAnim.OutBack(UiAnim.Clamp01(elapsed / popDuration));
            float scale = popScale + (1f - popScale) * pop;

            s.Icon.Apply(center, new Vector2(skullSize, skullSize), s.Sprite, new Color(1f, 1f, 1f, alpha), scale);

            var glowColor = UiTheme.WithAlpha(
                UiTheme.Parse(cfg.IconGlowColor.Value, Color.white),
                Mathf.Clamp01(cfg.IconGlowIntensity.Value) * alpha);
            s.Glow.SetPosition(center);
            s.Glow.ApplyAlpha(glowColor);

            // 列表最后一个就是最新加入的（Show 里 Add 到尾部）
            if (i == _skulls.Count - 1 && alpha > 0.01f)
            {
                _ringCenter = center;
                _ringCenterValid = true;
            }
        }
    }

    private void DrawCard(AyaConfig cfg, float dt)
    {
        float now = Time.unscaledTime;
        float elapsed = now - _latestStart;
        float duration = _textDuration;

        float enterDuration = Mathf.Max(0f, cfg.CardEnterDuration.Value);
        float stagger = Mathf.Max(0f, cfg.CardRowStagger.Value);
        float exitDuration = Mathf.Max(0f, cfg.CardExitDuration.Value);

        // ---- 退场：整体淡出 + 轻微上移（不改 scale，避免和骷髅/缎带的缩放动画打架）----
        float exitAlpha = 1f;
        if (exitDuration > 0f && elapsed > duration - exitDuration)
        {
            float k = UiAnim.Clamp01((elapsed - (duration - exitDuration)) / exitDuration);
            exitAlpha = 1f - k * k;
        }
        if (exitAlpha <= 0.001f) { HideCard(); return; }

        float exitDrift = (1f - exitAlpha) * 12f;

        float blockWidth = Mathf.Max(100f, cfg.KillFeedBlockWidth.Value);
        Vector2 anchor = Anchor();

        // 行位置全部交给纯数学层算，这样行序 / 间距可以离线验证（tests/AyaHitmarker.LogicTests）
        var layout = KillCardLayoutMath.Compute(new KillCardLayoutInput(
            cfg.SkullSize.Value,
            cfg.KillFeedLineSpacing.Value,
            cfg.AchievementHeight.Value,
            cfg.AchievementVerticalOffset.Value,
            cfg.ShowStreakBar.Value ? cfg.StreakBarHeight.Value : 0f,
            cfg.StreakBarGap.Value,
            cfg.FontSizeFaction.Value,
            cfg.FontSizePlayerName.Value,
            cfg.FontSizeKillDetails.Value,
            cfg.FontSizeScore.Value,
            cfg.FontSizeAchievement.Value));

        float lineHeight = layout.LineHeight;
        float skullCenterY = anchor.y + layout.SkullCenterY + exitDrift;
        float factionY = anchor.y + layout.FactionY + exitDrift;
        float nameY = anchor.y + layout.NameY + exitDrift;
        float detailsY = anchor.y + layout.DetailsY + exitDrift;
        float achievementHeight = Mathf.Max(8f, cfg.AchievementHeight.Value);
        float achievementY = anchor.y + layout.AchievementY + exitDrift;

        float enterDistance = Mathf.Max(0f, cfg.CardEnterDistance.Value);
        float Off(int row)
        {
            float t = UiAnim.OutCubic(UiAnim.Stagger(elapsed, row, stagger, enterDuration));
            return enterDistance * (1f - t);
        }
        float Alpha(int row)
        {
            float t = UiAnim.OutCubic(UiAnim.Stagger(elapsed, row, stagger, enterDuration));
            return Mathf.Clamp01(t) * exitAlpha;
        }

        DrawAchievement(cfg, anchor, achievementY, achievementHeight, elapsed, Off(RowAchievement), Alpha(RowAchievement));
        DrawScore(cfg, anchor, skullCenterY, lineHeight, elapsed, Off(RowScore), Alpha(RowScore));
        DrawFaction(cfg, anchor, factionY, lineHeight, blockWidth, Off(RowFaction), Alpha(RowFaction));
        DrawName(cfg, anchor, nameY, lineHeight, blockWidth, Off(RowName), Alpha(RowName));
        DrawDetails(cfg, anchor, detailsY, lineHeight, blockWidth, Off(RowDetails), Alpha(RowDetails));
        DrawStreakBar(cfg, anchor, layout.StreakBarY + exitDrift, dt, exitAlpha);
        LogRowScreenPosition(cfg, elapsed);
    }

    // ------------------------------------------------------------------ 行

    private void DrawFaction(AyaConfig cfg, Vector2 anchor, float y, float lineHeight, float blockWidth, float dx, float alpha)
    {
        if (_factionText == null || _factionIcon == null) return;

        if (!cfg.ShowFaction.Value || alpha <= 0.002f)
        {
            _factionText.Hide();
            _factionIcon.Hide();
            return;
        }

        Sprite? rankSprite = _rankSprite;
        bool showIcon = rankSprite != null;
        float iconSize = Mathf.Max(0f, cfg.FactionIconSize.Value);
        float rightEdge = anchor.x - (showIcon ? iconSize + 6f : 0f);

        _factionText.Apply(
            new Vector2(rightEdge + dx, y),
            new Vector2(blockWidth, lineHeight),
            _factionComposed,
            cfg.FontSizeFaction.Value,
            new Color(1f, 1f, 1f, alpha),
            FontStyles.Bold,
            TextAlignmentOptions.Right);

        if (showIcon)
        {
            _factionIcon.Apply(
                new Vector2(anchor.x - iconSize * 0.5f + dx, y + cfg.FactionIconVerticalOffset.Value),
                new Vector2(iconSize, iconSize),
                rankSprite,
                new Color(1f, 1f, 1f, alpha));
            if (_factionIcon.Img != null) _factionIcon.Img.preserveAspect = true;
        }
        else
        {
            _factionIcon.Hide();
        }
    }

    private void DrawName(AyaConfig cfg, Vector2 anchor, float y, float lineHeight, float blockWidth, float dx, float alpha)
    {
        if (_nameText == null) return;

        if (!cfg.ShowPlayerName.Value || alpha <= 0.002f)
        {
            _nameText.Hide();
            return;
        }

        // TMP 的 outlineWidth 是 0..1 归一化宽度，旧项目那边是 0.5..10 像素
        float outline = Mathf.Clamp(cfg.NameOutlineThickness.Value / 8f, 0f, 1f);
        if (!Mathf.Approximately(_nameText.Text.outlineWidth, outline)) _nameText.Text.outlineWidth = outline;

        _nameText.Apply(
            new Vector2(anchor.x + dx, y),
            new Vector2(blockWidth, lineHeight),
            _nameComposed,
            cfg.FontSizePlayerName.Value,
            UiTheme.WithAlpha(UiTheme.Parse(cfg.ColorPlayerName.Value, new Color(1f, 0.3f, 0.3f)), alpha),
            FontStyles.Bold,
            TextAlignmentOptions.Right);
    }

    private void DrawDetails(AyaConfig cfg, Vector2 anchor, float y, float lineHeight, float blockWidth, float dx, float alpha)
    {
        if (_detailsText == null) return;

        if (!cfg.ShowKillDetails.Value || alpha <= 0.002f || string.IsNullOrEmpty(_detailsComposed))
        {
            _detailsText.Hide();
            return;
        }

        _detailsText.Apply(
            new Vector2(anchor.x + dx, y),
            new Vector2(blockWidth, lineHeight),
            _detailsComposed,
            cfg.FontSizeKillDetails.Value,
            UiTheme.WithAlpha(UiTheme.Parse(cfg.ColorKillDetails.Value, new Color(0.8f, 0.8f, 0.8f)), alpha),
            FontStyles.Normal,
            TextAlignmentOptions.Right);
    }

    /// <summary>战地式的得分弹窗：骷髅图标 + 从 0 滚上去的「+100」。</summary>
    private void DrawScore(AyaConfig cfg, Vector2 anchor, float skullCenterY, float lineHeight, float elapsed, float dx, float alpha)
    {
        if (_scoreText == null || _scoreIcon == null) return;

        if (!cfg.ShowScore.Value || !_analysis.HasScore || alpha <= 0.002f)
        {
            _scoreText.Hide();
            _scoreIcon.Hide();
            return;
        }

        // 计数动画 + 上浮
        float countDuration = Mathf.Max(0f, cfg.ScoreCountDuration.Value);
        float countT = countDuration <= 0f ? 1f : UiAnim.OutCubic(UiAnim.Clamp01(elapsed / countDuration));
        int shown = Mathf.RoundToInt(_analysis.Score * countT);

        float lifetime = Mathf.Max(0.01f, cfg.KillFeedDuration.Value);
        float riseT = UiAnim.OutQuint(UiAnim.Clamp01(elapsed / lifetime));
        float y = skullCenterY + cfg.ScoreRiseDistance.Value * riseT;

        float pop = UiAnim.OutBack(UiAnim.Clamp01(elapsed / Mathf.Max(0.01f, cfg.ScorePopDuration.Value)));
        float scale = cfg.ScorePopScale.Value + (1f - cfg.ScorePopScale.Value) * pop;

        float iconSize = Mathf.Max(0f, cfg.ScoreIconSize.Value);
        float textX = anchor.x + cfg.ScoreHorizontalOffset.Value
                      + (iconSize > 0f ? iconSize * 0.5f : 0f);
        float textWidth = Mathf.Max(40f, cfg.ScoreTextWidth.Value);

        // 图标贴在文字左边，两者一起缩放（锚点取文字左缘）
        if (iconSize > 0f && Assets.IconBank.Skull != null)
        {
            _scoreIcon.Apply(
                new Vector2(textX - iconSize * 0.5f + dx, y),
                new Vector2(iconSize, iconSize),
                Assets.IconBank.Skull,
                UiTheme.WithAlpha(UiTheme.Parse(cfg.ColorScore.Value, new Color(1f, 0.88f, 0.54f)), alpha),
                scale);
        }
        else
        {
            _scoreIcon.Hide();
        }

        _scoreText.Apply(
            new Vector2(textX + dx, y),
            new Vector2(textWidth, lineHeight),
            ScoreString(shown),
            cfg.FontSizeScore.Value,
            UiTheme.WithAlpha(UiTheme.Parse(cfg.ColorScore.Value, new Color(1f, 0.88f, 0.54f)), alpha),
            FontStyles.Bold,
            TextAlignmentOptions.Left,
            scale);
    }

    private static string ScoreString(int score)
    {
        if (ScoreCache.TryGetValue(score, out var cached)) return cached;
        string s = "+" + score;
        if (ScoreCache.Count > 512) ScoreCache.Clear();
        ScoreCache[score] = s;
        return s;
    }

    /// <summary>成就缎带：擦除式展开 + 过冲 + 停留呼吸 + 淡出。pivot 在左边，所以 scaleX = 展开程度。</summary>
    private void DrawAchievement(AyaConfig cfg, Vector2 anchor, float y, float height, float elapsed, float dx, float alpha)
    {
        if (_achievementBg == null || _achievementText == null) return;

        float enter = Mathf.Max(0.01f, cfg.AchievementEnterDuration.Value);
        float hold = Mathf.Max(0f, cfg.AchievementHoldDuration.Value);
        float fade = Mathf.Max(0.01f, cfg.AchievementFadeDuration.Value);
        float total = enter + hold + fade;

        bool wanted = cfg.ShowAchievement.Value
                      && _analysis.Ribbon.HasTitle
                      && _achievementWidth > 0f
                      && elapsed <= total;

        if (!wanted)
        {
            _achievementBg.Hide();
            _achievementText.Hide();
            return;
        }

        float openT = UiAnim.OutBack(UiAnim.Clamp01(elapsed / enter));
        float pulse = elapsed > enter
            ? UiAnim.Wobble(elapsed, 0.7f, 9f, Mathf.Max(0f, cfg.AchievementPulseScale.Value))
            : 0f;

        float bgAlpha = UiAnim.LifeAlpha(elapsed, total, enter * 0.5f, fade) * alpha;
        if (bgAlpha <= 0.002f)
        {
            _achievementBg.Hide();
            _achievementText.Hide();
            return;
        }

        float left = anchor.x - _achievementWidth + dx;

        _achievementBg.Apply(
            new Vector2(left, y),
            new Vector2(_achievementWidth, height),
            UiTheme.WhiteSprite,
            UiTheme.WithAlpha(RibbonColor(cfg), bgAlpha),
            1f,
            new Vector2(0f, 0.5f));
        _achievementBg.SetScaleX(Mathf.Max(0f, openT + pulse));

        _achievementText.Apply(
            new Vector2(left + _achievementWidth * 0.5f, y),
            new Vector2(_achievementWidth, height),
            _achievementComposed,
            cfg.FontSizeAchievement.Value,
            UiTheme.WithAlpha(UiTheme.Parse(cfg.ColorAchievementText.Value, Color.white), bgAlpha),
            FontStyles.Bold,
            TextAlignmentOptions.Center);
    }

    private Color RibbonColor(AyaConfig cfg) => _analysis.Ribbon.Tier switch
    {
        KillAchievementTier.Epic => UiTheme.Parse(cfg.ColorAchievementEpic.Value, new Color(0.64f, 0.14f, 0.2f)),
        KillAchievementTier.Great => UiTheme.Parse(cfg.ColorAchievementGreat.Value, new Color(0.76f, 0.41f, 0.1f)),
        KillAchievementTier.Good => UiTheme.Parse(cfg.ColorAchievementGood.Value, new Color(0.18f, 0.44f, 0.71f)),
        _ => UiTheme.Parse(cfg.ColorAchievementNormal.Value, new Color(0.27f, 0.31f, 0.36f)),
    };

    private void DrawStreakBar(AyaConfig cfg, Vector2 anchor, float yOffset, float dt, float exitAlpha)
    {
        if (_streakBarBack == null || _streakBarFill == null) return;

        // 进度条只在「至少有一个骷髅在屏幕上」时才画 —— 连杀数用骷髅个数表达，进度用条子表达
        bool wanted = cfg.ShowStreakBar.Value && _skulls.Count > 0 && exitAlpha > 0.002f;
        if (!wanted)
        {
            _streakBarBack.Hide();
            _streakBarFill.Hide();
            _barShown = float.NaN;
            return;
        }

        float width = Mathf.Max(20f, cfg.StreakBarWidth.Value);
        float height = Mathf.Max(1f, cfg.StreakBarHeight.Value);
        // pivot 钉在左边，所以 left 就是进度条左缘（右缘对齐到锚点）
        float left = anchor.x - width;
        float y = anchor.y + yOffset;

        // 目标进度平滑收敛，避免连杀时条子「跳」一下。
        // 1.6 = 每秒能走完 1.6 份进度，也就是满格一次约 0.6 秒。
        const float barSweepPerSecond = 1.6f;
        float target = _streakTarget;
        if (float.IsNaN(_barShown)) _barShown = target;
        else _barShown = Mathf.MoveTowards(_barShown, target, dt * barSweepPerSecond);

        _streakBarBack.Apply(
            new Vector2(left, y),
            new Vector2(width, height),
            UiTheme.WhiteSprite,
            UiTheme.WithAlpha(UiTheme.Parse(cfg.ColorStreakBarBack.Value, new Color(0f, 0f, 0f, 0.6f)), exitAlpha),
            1f,
            new Vector2(0f, 0.5f));

        float filled = Mathf.Clamp01(_barShown) * width;
        _streakBarFill.Apply(
            new Vector2(left, y),
            new Vector2(Mathf.Max(0f, filled), height),
            UiTheme.WhiteSprite,
            UiTheme.WithAlpha(UiTheme.Parse(cfg.ColorStreakBar.Value, new Color(1f, 0.48f, 0.09f)), exitAlpha),
            1f,
            new Vector2(0f, 0.5f));
    }

    private float _streakTarget;
    private float _barShown = float.NaN;

    /// <summary>由 <c>AyaBehaviour</c> 在每次击杀后写入：距离下一档连杀称号的进度 0..1。</summary>
    internal void SetStreakProgress(float progress) => _streakTarget = Mathf.Clamp01(progress);

    // ------------------------------------------------------------------ 文本组装

    /// <summary>
    /// 锚点（canvas 局部坐标）。
    /// 旧项目 <c>anchorX = sw/2 - hOffset</c>、<c>anchorY = sh/2 + vOffset</c> 都是**屏幕**坐标（Y 向下为正），
    /// canvas 里原点在屏幕中心且 Y 向上，所以垂直分量要取反。
    /// </summary>
    internal static Vector2 Anchor() => new Vector2(
        -Plugin.Cfg.KillFeedHorizontalOffset.Value,
        -Plugin.Cfg.KillFeedVerticalOffset.Value);

    private string ComposeFaction(AyaConfig cfg, string label)
    {
        _sb.Clear();

        Color factionColor = UiTheme.Parse(cfg.ColorFaction.Value, Color.white);
        Color botColor = _latest.BotType switch
        {
            "USEC" => UiTheme.Parse(cfg.ColorUSEC.Value, factionColor),
            "BEAR" => UiTheme.Parse(cfg.ColorBEAR.Value, factionColor),
            "SCAV" => UiTheme.Parse(cfg.ColorSCAV.Value, factionColor),
            "BOSS" => UiTheme.Parse(cfg.ColorBOSS.Value, factionColor),
            "Follower" => UiTheme.Parse(cfg.ColorFollower.Value, factionColor),
            "Raider" => UiTheme.Parse(cfg.ColorRAIDER.Value, factionColor),
            "Rouges" => UiTheme.Parse(cfg.ColorRouges.Value, factionColor),
            "Sectant" => UiTheme.Parse(cfg.ColorSectant.Value, factionColor),
            _ => factionColor,
        };

        if (_latest.IsPmc && _latest.VictimLevel > 0)
        {
            _sb.Append("<color=#").Append(Hex(factionColor)).Append(">[")
               .Append(_latest.VictimLevel).Append("] </color>");
        }

        _sb.Append("<color=#").Append(Hex(botColor)).Append('>')
           .Append(Escape(label)).Append("</color>");

        return _sb.ToString();
    }

    /// <summary>「头部 · 142m · 枪械 · M4A1」——空的字段自动跳过，不会留出「· ·」。</summary>
    private string ComposeDetails(AyaConfig cfg)
    {
        _sb.Clear();

        if (!cfg.ShowKillDetails.Value) return string.Empty;

        if (_latest.Headshot) AppendDetail("爆头");
        else if (!string.IsNullOrWhiteSpace(_latest.BodyPart)) AppendDetail("部位:" + Escape(_latest.BodyPart));

        if (_latest.Distance > 0.5f) AppendDetail("距离:" + _latest.Distance.ToString("0") + "m");

        if (cfg.ShowKillMethod.Value && _latest.MethodIsNotable && !string.IsNullOrWhiteSpace(_latest.Method))
        {
            AppendDetail(Escape(_latest.Method));
        }

        if (!string.IsNullOrWhiteSpace(_latest.Weapon) && _latest.Weapon != "?")
        {
            AppendDetail(Escape(_latest.Weapon));
        }

        return _sb.ToString();
    }

    private void AppendDetail(string part)
    {
        if (_sb.Length > 0) _sb.Append(" · ");
        _sb.Append(part);
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

    // ------------------------------------------------------------------ 其他

    private void HideCard()
    {
        _factionText?.Hide();
        _factionIcon?.Hide();
        _nameText?.Hide();
        _detailsText?.Hide();
        _scoreText?.Hide();
        _scoreIcon?.Hide();
        _achievementBg?.Hide();
        _achievementText?.Hide();
        _streakBarBack?.Hide();
        _streakBarFill?.Hide();
    }

    private void TrimToLimit(int max)
    {
        int limit = Mathf.Clamp(max, 1, 24);
        while (_skulls.Count > limit)
        {
            var s = _skulls[0];
            Recycle(s);
            _skulls.RemoveAt(0);
        }
    }

    private void Recycle(SkullEntry s)
    {
        s.Icon.Hide();
        s.Glow.Hide();
        if (_pool.Count < 24) _pool.Add(s);
    }

    private SkullEntry? Rent()
    {
        if (_pool.Count > 0)
        {
            var pooled = _pool[_pool.Count - 1];
            _pool.RemoveAt(_pool.Count - 1);
            if (pooled.Icon.Img != null) pooled.Icon.Img.color = Color.clear;
            return pooled;
        }

        if (_parent == null) return null;

        return new SkullEntry
        {
            // 辉光在底层，骷髅在上层
            Glow = UiImageRow.Create("AyaHitmarker.SkullGlow", _parent),
            Icon = UiImageRow.Create("AyaHitmarker.Skull", _parent),
        };
    }

    internal void Reset()
    {
        for (int i = 0; i < _skulls.Count; i++)
        {
            Recycle(_skulls[i]);
        }
        _skulls.Clear();
        _textVisible = false;
        _achievementWidth = 0f;
        _achievementComposed = string.Empty;
        _factionComposed = string.Empty;
        _nameComposed = string.Empty;
        _detailsComposed = string.Empty;
        _rankSprite = null;
        _barShown = float.NaN;
        _latest = default;
        _analysis = KillAnalysis.Empty;
        HideCard();
        _ring.Reset();
    }
}
