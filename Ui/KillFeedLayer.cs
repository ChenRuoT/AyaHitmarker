using System;
using System.Collections.Generic;
using System.Text;
using AyaHitmarker.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AyaHitmarker.Ui;

/// <summary>
/// 击杀播报。逐行移植旧项目 <c>KillFeedUI</c> 的布局与动效。
///
/// 布局（锚点在屏幕中心左侧 <c>KillFeedHorizontalOffset</c> 像素处，垂直 <c>KillFeedVerticalOffset</c>）：
/// <code>
///                    [阵营图标][等级] USEC      ← 阵营行（右对齐于锚点上方）
///                                                      [经验]      ← 锚点右侧
///                            💀 💀 💀                ← 骷髅行，垂直居中于锚点，向**左**生长
///                       玩家名字                    ← 右对齐于锚点下方
///                   部位 · 距离 · 武器               ← 再下一行
/// </code>
///
/// 动效：
/// <list type="bullet">
///   <item>新的击杀在 <c>StreakWindow</c> 内连上时算连杀，已有的骷髅整体左移
///     <c>SkullSize + SkullSpacing</c>，用 <c>Lerp(dt * SkullPushAnimationSpeed)</c> 平滑过去；</item>
///   <item>骷髅只在 <c>SkullDisplayDuration</c> 内显示，最后 <c>SkullFadeDuration</c> 淡出；</item>
///   <item>文字只画**最新**那一条，在 <c>KillFeedDuration</c> 内线性淡出（旧版就是这样，不是每杀一行）；</item>
///   <item>爆头击杀在最新骷髅上放环形特效。</item>
/// </list>
/// </summary>
internal sealed class KillFeedLayer
{
    private sealed class SkullEntry
    {
        internal GameObject? Go;
        internal Image? Icon;
        internal Image? Glow;
        internal RectTransform? Rt;
        internal RectTransform? GlowRt;
        internal Vector2 GlowBaseSize;
        internal float Start;
        internal float Streak;
        internal bool Headshot;
        internal float TargetOffset;
        internal float CurrentOffset;
        internal float LastAlpha = -1f;
    }

    private readonly List<SkullEntry> _skulls = new List<SkullEntry>(8);
    private readonly List<SkullEntry> _pool = new List<SkullEntry>(8);
    private readonly StringBuilder _sb = new StringBuilder(128);

    private readonly RingEffect _ring = new RingEffect();

    private RectTransform? _parent;

    // 文本对象（全套只有一份，旧项目也只画最新一条）
    private GameObject? _factionGo;
    private TextMeshProUGUI? _factionText;
    private RectTransform? _factionRt;
    private GameObject? _factionIconGo;
    private Image? _factionIcon;
    private RectTransform? _factionIconRt;

    private GameObject? _nameGo;
    private TextMeshProUGUI? _nameText;
    private RectTransform? _nameRt;
    private GameObject? _detailsGo;
    private TextMeshProUGUI? _detailsText;
    private RectTransform? _detailsRt;
    private GameObject? _expGo;
    private TextMeshProUGUI? _expText;
    private RectTransform? _expRt;

    private KillRecord _latest;
    private float _latestStart;
    private bool _hasLatest;

    // ------------------------------------------------------------------ 构建

    internal void Attach(RectTransform parent)
    {
        _parent = parent;
        _ring.Attach(parent);

        _factionText = UiTheme.CreateTextObject("AyaHitmarker.KillFaction", parent, TextAlignmentOptions.Right);
        _factionGo = _factionText.gameObject;
        _factionRt = UiTheme.Rect(_factionGo);

        _factionIconGo = UiTheme.CreateContainer("AyaHitmarker.KillFactionIcon", parent);
        _factionIcon = _factionIconGo.GetComponent<Image>();
        _factionIconRt = UiTheme.Rect(_factionIconGo);
        _factionIconGo.SetActive(false);

        _nameText = UiTheme.CreateTextObject("AyaHitmarker.KillName", parent, TextAlignmentOptions.Right);
        _nameGo = _nameText.gameObject;
        _nameRt = UiTheme.Rect(_nameGo);

        _detailsText = UiTheme.CreateTextObject("AyaHitmarker.KillDetails", parent, TextAlignmentOptions.Right);
        _detailsGo = _detailsText.gameObject;
        _detailsRt = UiTheme.Rect(_detailsGo);

        _expText = UiTheme.CreateTextObject("AyaHitmarker.KillExp", parent, TextAlignmentOptions.Left);
        _expGo = _expText.gameObject;
        _expRt = UiTheme.Rect(_expGo);

        SetTextVisible(false);
    }

    // ------------------------------------------------------------------ 新增击杀

    internal void Show(in KillRecord rec)
    {
        if (_parent == null) return;

        var cfg = Plugin.Cfg;
        float now = Time.unscaledTime;
        float skullDisplayDuration = Mathf.Max(0.1f, cfg.SkullDisplayDuration.Value);

        float streak = CalculateStreak(now, cfg.StreakWindow.Value);
        if (streak > 1f) PushPreviousSkulls(now, skullDisplayDuration, cfg);

        var entry = Rent();
        if (entry?.Go == null || entry.Rt == null) return;

        entry.Start = now;
        entry.Streak = streak;
        entry.Headshot = rec.Headshot;
        entry.TargetOffset = 0f;
        entry.CurrentOffset = 0f;
        entry.LastAlpha = -1f;

        ApplySkullSprite(entry, rec.Headshot);
        entry.Go.SetActive(true);
        _skulls.Add(entry);

        // 只保留最新那条的文字
        _latest = rec;
        _latestStart = now;
        _hasLatest = true;
        SetTextVisible(true);

        // 环形特效（旧项目只在爆头时触发；爆炸/暴击的通道保留）
        bool ringEnabled = cfg.EnableRingEffect.Value && cfg.RingHeadshotEnabled.Value;
        _ring.Trigger(now, ringEnabled && rec.Headshot, rec.Headshot ? KillType.Headshot : KillType.Normal);

        TrimToLimit(cfg.KillFeedMaxSkulls.Value);
    }

    private float CalculateStreak(float now, float window)
    {
        float streak = 1f;
        for (int i = 0; i < _skulls.Count; i++)
        {
            if (now - _skulls[i].Start <= window)
            {
                streak = Mathf.Max(streak, _skulls[i].Streak + 1f);
            }
        }
        return streak;
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
        if (entry.Icon != null && entry.Icon.sprite != icon) entry.Icon.sprite = icon;
        if (entry.Rt != null) entry.Rt.sizeDelta = new Vector2(skullSize, skullSize);
        if (entry.Icon != null)
        {
            entry.Icon.color = Color.white;
            entry.Icon.preserveAspect = true;
        }

        // ---- 辉光（烘成一张纹理，1 次绘制代替旧版的 17 次叠加） ----
        if (entry.Glow == null || entry.GlowRt == null || entry.Icon == null) return;

        if (!cfg.EnableIconGlow.Value || icon == null)
        {
            entry.Glow.color = Color.clear;
            return;
        }

        int spread = Mathf.Max(1, Mathf.RoundToInt(cfg.IconGlowSize.Value));
        var glowSprite = Assets.IconBank.GetGlow(icon, spread);
        if (glowSprite == null)
        {
            entry.Glow.color = Color.clear;
            return;
        }

        float padRatio = Assets.IconBank.GlowPadRatio(icon, spread);
        entry.Glow.sprite = glowSprite;
        entry.GlowRt.sizeDelta = new Vector2(skullSize * padRatio, skullSize * padRatio);
        entry.GlowBaseSize = entry.GlowRt.sizeDelta;
        entry.Glow.color = UiTheme.WithAlpha(
            UiTheme.Parse(cfg.IconGlowColor.Value, Color.white),
            Mathf.Clamp01(cfg.IconGlowIntensity.Value));
    }

    // ------------------------------------------------------------------ 每帧

    internal void Tick()
    {
        var cfg = Plugin.Cfg;

        if (!cfg.KillFeedEnabled.Value)
        {
            if (_skulls.Count > 0 || _hasLatest) Reset();
            return;
        }

        float now = Time.unscaledTime;
        float killFeedDuration = Mathf.Max(0.5f, cfg.KillFeedDuration.Value);
        float skullDisplayDuration = Mathf.Max(0.1f, cfg.SkullDisplayDuration.Value);
        float skullFadeDuration = Mathf.Max(0.01f, cfg.SkullFadeDuration.Value);
        float removeAfter = Mathf.Max(killFeedDuration, skullDisplayDuration);

        // 过期回收
        for (int i = _skulls.Count - 1; i >= 0; i--)
        {
            var s = _skulls[i];
            if (now - s.Start > removeAfter)
            {
                s.Go?.SetActive(false);
                HideGlow(s);
                _skulls.RemoveAt(i);
                if (_pool.Count < 24) _pool.Add(s);
            }
        }

        if (now - _latestStart > killFeedDuration) SetTextVisible(false);
        if (_skulls.Count == 0 && !IsTextVisible) _hasLatest = false;

        // ---- 骷髅位置动画 ----
        float speed = Mathf.Max(0.01f, cfg.SkullPushAnimationSpeed.Value);
        float dt = Time.unscaledDeltaTime;
        float skullSize = Mathf.Max(8f, cfg.SkullSize.Value);
        Vector2 anchor = Anchor();

        SkullEntry? latest = null;
        for (int i = 0; i < _skulls.Count; i++) latest = _skulls[i];

        for (int i = 0; i < _skulls.Count; i++)
        {
            var s = _skulls[i];
            if (s.Rt == null || s.Icon == null || s.Go == null) continue;

            if (Mathf.Abs(s.CurrentOffset - s.TargetOffset) > 0.1f)
            {
                s.CurrentOffset = Mathf.Lerp(s.CurrentOffset, s.TargetOffset, dt * speed);
            }
            else
            {
                s.CurrentOffset = s.TargetOffset;
            }

            float elapsed = now - s.Start;
            float alpha = 1f;
            if (elapsed >= skullDisplayDuration - skullFadeDuration)
            {
                float fadeProgress = (elapsed - (skullDisplayDuration - skullFadeDuration)) / skullFadeDuration;
                alpha = Mathf.Clamp01(1f - fadeProgress);
            }
            if (elapsed >= skullDisplayDuration) alpha = 0f;

            // 旧项目：skullX = anchorX - skullSize - offset，中心 = 左边 + size/2
            var center = new Vector2(anchor.x - skullSize * 0.5f - s.CurrentOffset, anchor.y);
            s.Rt.anchoredPosition = center;

            if (s.LastAlpha < 0f || Mathf.Abs(alpha - s.LastAlpha) > 0.005f)
            {
                s.LastAlpha = alpha;
                s.Icon.color = new Color(1f, 1f, 1f, alpha);
                if (s.Glow != null)
                {
                    var glowColor = UiTheme.Parse(cfg.IconGlowColor.Value, Color.white);
                    s.Glow.color = UiTheme.WithAlpha(glowColor, Mathf.Clamp01(cfg.IconGlowIntensity.Value) * alpha);
                }
            }

            // 辉光纹理带外边距，和骷髅同心；尺寸在 ApplySkullSprite 里算好
            if (s.Glow != null && s.GlowRt != null)
            {
                s.GlowRt.anchoredPosition = center;
            }

            // 环形特效只挂在最新那个骷髅上
            if (latest != null && ReferenceEquals(s, latest) && alpha > 0.01f)
            {
                _ring.Tick(center, now);
            }
        }

        if (latest == null || !IsTextVisible) return;
        DrawText(cfg, now, anchor, killFeedDuration);
    }

    // ------------------------------------------------------------------ 文本

    private void DrawText(AyaConfig cfg, float now, Vector2 anchor, float killFeedDuration)
    {
        float elapsed = now - _latestStart;
        float textAlpha = Mathf.Clamp01(1f - elapsed / killFeedDuration);

        float skullSize = Mathf.Max(8f, cfg.SkullSize.Value);
        float lineSpacing = cfg.KillFeedLineSpacing.Value;
        int fontFaction = cfg.FontSizeFaction.Value;
        int fontName = cfg.FontSizePlayerName.Value;
        int fontDetails = cfg.FontSizeKillDetails.Value;
        float fontExp = cfg.FontSizeExperience.Value;

        float textHeightsMax = Mathf.Max(fontFaction, Mathf.Max(fontName, fontDetails)) * 1.2f;
        float lineHeight = Mathf.Clamp(Mathf.Max(textHeightsMax, skullSize * 0.5f), 14f, 200f);

        float blockWidth = Mathf.Max(100f, cfg.KillFeedBlockWidth.Value);

        // 全部用 canvas 局部坐标（原点 = 屏幕中心，Y 向上为正）。
        // 注意：旧项目是 IMGUI 的屏幕坐标（Y 向下为正），Anchor() 里已经做了 Y 取反，
        // 这里的 delta 一律"正数 = 在锚点上方"，比旧代码里那串易错的正负号清楚。
        float factionY = anchor.y + skullSize * 0.5f + lineSpacing + lineHeight * 0.5f;
        float nameY = anchor.y - (skullSize * 0.5f + lineSpacing + lineHeight * 0.5f);
        float detailsY = anchor.y - (skullSize * 0.5f + lineSpacing + lineHeight + lineSpacing + lineHeight * 0.5f);

        // ---- 阵营行（锚点上方，右对齐） ----
        if (cfg.ShowFaction.Value && _factionText != null && _factionRt != null && _factionGo != null)
        {
            if (cfg.ShowFactionIcon.Value && _factionIcon != null)
            {
                string botType = BotTypeMapping.GetBotType(_latest.Role);
                var rankSprite = BotTypeMapping.IsPmc(botType) ? Assets.IconBank.GetRankIcon(_latest.VictimLevel) : null;
                if (_factionIcon.sprite != rankSprite) _factionIcon.sprite = rankSprite;
            }

            bool showIcon = cfg.ShowFactionIcon.Value && _factionIconGo != null
                            && _factionIcon != null && _factionIcon.sprite != null;
            float iconSize = Mathf.Max(0f, cfg.FactionIconSize.Value);
            float rightEdge = anchor.x - (showIcon ? iconSize + 6f : 0f);

            _factionGo.SetActive(true);
            _factionText.fontSize = fontFaction;
            _factionText.fontStyle = FontStyles.Bold;
            _factionText.alignment = TextAlignmentOptions.Right;
            _factionText.text = ComposeFaction(cfg, _latest);
            _factionText.color = new Color(1f, 1f, 1f, textAlpha);

            _factionRt.sizeDelta = new Vector2(blockWidth, lineHeight);
            _factionRt.anchoredPosition = new Vector2(rightEdge, factionY);

            if (showIcon)
            {
                _factionIconGo!.SetActive(true);
                _factionIcon!.color = new Color(1f, 1f, 1f, textAlpha);
                _factionIconRt!.sizeDelta = new Vector2(iconSize, iconSize);
                _factionIconRt.anchoredPosition = new Vector2(
                    anchor.x - iconSize * 0.5f,
                    factionY + cfg.FactionIconVerticalOffset.Value);
            }
            else
            {
                _factionIconGo?.SetActive(false);
            }
        }
        else
        {
            _factionGo?.SetActive(false);
            _factionIconGo?.SetActive(false);
        }

        // ---- 经验值（锚点右侧） ----
        if (cfg.ShowExperience.Value && _expText != null && _expRt != null && _expGo != null)
        {
            _expGo.SetActive(true);
            _expText.fontSize = fontExp;
            _expText.fontStyle = FontStyles.Bold;
            _expText.alignment = TextAlignmentOptions.Left;
            _expText.text = _latest.Experience.ToString();
            _expText.color = UiTheme.WithAlpha(UiTheme.Parse(cfg.ColorExperience.Value, new Color(1f, 1f, 0.8f)), textAlpha);
            _expRt.sizeDelta = new Vector2(Mathf.Max(40f, cfg.ExperienceTextWidth.Value), lineHeight);
            _expRt.anchoredPosition = new Vector2(
                anchor.x + cfg.KillFeedExperienceHorizontalOffset.Value,
                anchor.y);
        }
        else
        {
            _expGo?.SetActive(false);
        }

        // ---- 玩家名（锚点下方，右对齐） ----
        if (cfg.ShowPlayerName.Value && _nameText != null && _nameRt != null && _nameGo != null)
        {
            _nameGo.SetActive(true);
            _nameText.fontSize = fontName;
            _nameText.fontStyle = FontStyles.Bold;
            _nameText.alignment = TextAlignmentOptions.Right;
            _nameText.text = string.IsNullOrWhiteSpace(_latest.VictimName) ? "Unknown" : _latest.VictimName;
            _nameText.color = UiTheme.WithAlpha(UiTheme.Parse(cfg.ColorPlayerName.Value, new Color(1f, 0.3f, 0.3f)), textAlpha);

            // TMP 的 outlineWidth 是 0..1 归一化宽度，旧项目那边是 0.5..10 像素
            float outline = Mathf.Clamp(cfg.NameOutlineThickness.Value / 8f, 0f, 1f);
            if (!Mathf.Approximately(_nameText.outlineWidth, outline)) _nameText.outlineWidth = outline;

            _nameRt.sizeDelta = new Vector2(blockWidth, lineHeight);
            _nameRt.anchoredPosition = new Vector2(anchor.x, nameY);
        }
        else
        {
            _nameGo?.SetActive(false);
        }

        // ---- 详情行 ----
        if (cfg.ShowKillDetails.Value && _detailsText != null && _detailsRt != null && _detailsGo != null)
        {
            _detailsGo.SetActive(true);
            _detailsText.fontSize = fontDetails;
            _detailsText.fontStyle = FontStyles.Normal;
            _detailsText.alignment = TextAlignmentOptions.Right;
            _detailsText.text = ComposeDetails(_latest);
            _detailsText.color = UiTheme.WithAlpha(UiTheme.Parse(cfg.ColorKillDetails.Value, new Color(0.8f, 0.8f, 0.8f)), textAlpha);
            _detailsRt.sizeDelta = new Vector2(blockWidth, lineHeight);
            _detailsRt.anchoredPosition = new Vector2(anchor.x, detailsY);
        }
        else
        {
            _detailsGo?.SetActive(false);
        }
    }

    /// <summary>
    /// 锚点（canvas 局部坐标）。
    /// 旧项目 <c>anchorX = sw/2 - hOffset</c>、<c>anchorY = sh/2 + vOffset</c> 都是**屏幕**坐标（Y 向下为正），
    /// canvas 里原点在屏幕中心且 Y 向上，所以垂直分量要取反。
    /// </summary>
    private static Vector2 Anchor() => new Vector2(
        -Plugin.Cfg.KillFeedHorizontalOffset.Value,
        -Plugin.Cfg.KillFeedVerticalOffset.Value);

    private string ComposeFaction(AyaConfig cfg, in KillRecord rec)
    {
        _sb.Clear();

        string botType = BotTypeMapping.GetBotType(rec.Role);
        var factionColor = UiTheme.Parse(cfg.ColorFaction.Value, Color.white);
        Color botColor = botType switch
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

        if (BotTypeMapping.IsPmc(botType) && rec.VictimLevel > 0)
        {
            _sb.Append("<color=#").Append(Hex(factionColor)).Append(">[")
               .Append(rec.VictimLevel).Append("] </color>");
        }

        _sb.Append("<color=#").Append(Hex(botColor)).Append('>')
           .Append(Escape(botType)).Append("</color>");

        return _sb.ToString();
    }

    private string ComposeDetails(in KillRecord rec)
    {
        var cfg = Plugin.Cfg;
        _sb.Clear();

        if (cfg.ShowKillDetails.Value)
        {
            if (rec.Headshot) _sb.Append("爆头");
            else if (!string.IsNullOrWhiteSpace(rec.BodyPart)) _sb.Append("部位:").Append(Escape(rec.BodyPart));

            if (rec.Distance > 0f)
            {
                if (_sb.Length > 0) _sb.Append(" · ");
                _sb.Append("距离:").Append(rec.Distance.ToString("0")).Append('m');
            }

            if (!string.IsNullOrWhiteSpace(rec.Weapon))
            {
                if (_sb.Length > 0) _sb.Append(" · ");
                _sb.Append("武器:").Append(Escape(rec.Weapon));
            }
        }

        return _sb.ToString();
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
        if (s.IndexOf('<') < 0 && s.IndexOf('>') < 0) return s;
        return s.Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private bool IsTextVisible => _factionGo != null && (_factionGo.activeSelf || (_nameGo?.activeSelf ?? false));

    private void SetTextVisible(bool visible)
    {
        if (!visible)
        {
            _factionGo?.SetActive(false);
            _nameGo?.SetActive(false);
            _detailsGo?.SetActive(false);
            _expGo?.SetActive(false);
            _factionIconGo?.SetActive(false);
        }
    }

    // ------------------------------------------------------------------ 其他

    private void TrimToLimit(int max)
    {
        int limit = Mathf.Clamp(max, 1, 24);
        while (_skulls.Count > limit)
        {
            var s = _skulls[0];
            s.Go?.SetActive(false);
            HideGlow(s);
            _skulls.RemoveAt(0);
            if (_pool.Count < 24) _pool.Add(s);
        }
    }

    private static void HideGlow(SkullEntry s)
    {
        if (s.Glow != null) s.Glow.color = Color.clear;
    }

    private SkullEntry? Rent()
    {
        SkullEntry e;
        if (_pool.Count > 0)
        {
            e = _pool[_pool.Count - 1];
            _pool.RemoveAt(_pool.Count - 1);
            return e;
        }

        if (_parent == null) return null;

        e = new SkullEntry();

        // 辉光在底层，骷髅在上层
        var glowGo = UiTheme.CreateContainer("AyaHitmarker.SkullGlow", _parent);
        e.Glow = glowGo.GetComponent<Image>();
        e.GlowRt = UiTheme.Rect(glowGo);
        e.Glow!.raycastTarget = false;

        var go = UiTheme.CreateContainer("AyaHitmarker.Skull", _parent);
        e.Icon = go.GetComponent<Image>();
        e.Rt = UiTheme.Rect(go);
        e.Icon!.raycastTarget = false;
        e.Icon.preserveAspect = true;

        e.Go = go;
        go.SetActive(false);
        e.Glow.color = Color.clear;
        return e;
    }

    internal void Reset()
    {
        for (int i = 0; i < _skulls.Count; i++)
        {
            var s = _skulls[i];
            s.Go?.SetActive(false);
            HideGlow(s);
            if (_pool.Count < 24) _pool.Add(s);
        }
        _skulls.Clear();
        _hasLatest = false;
        SetTextVisible(false);
        _ring.Reset();
    }
}
