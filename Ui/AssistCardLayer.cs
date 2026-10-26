using System;
using System.Text;
using AyaHitmarker.Game;
using TMPro;
using UnityEngine;

namespace AyaHitmarker.Ui;

/// <summary>
/// 0.6 c 助攻提示：击杀卡片风格的**简化竖排**小卡。
///
/// 只显示两行：
/// <list type="bullet">
///   <item>上方：成就横幅样式的「助攻 +伤害量」（浅钢色底 + 阵营色强调条 + 深色粗体字）；</item>
///   <item>下方：「阵营  受害者名字」，右对齐、和击杀卡片同一套字体 / 颜色 / 描边。</item>
/// </list>
/// 出现在击杀卡片下方（固定下移，避免和卡片重叠），淡入淡出，不占击杀卡片的累加模型。
/// </summary>
internal sealed class AssistCardLayer
{
    private UiImageRow? _box;
    private UiImageRow? _accent;
    private UiTextRow? _banner;
    private UiTextRow? _name;

    private readonly StringBuilder _sb = new StringBuilder(64);

    private AssistRecord _record;
    private string _bannerString = string.Empty;
    private string _nameRich = string.Empty;
    private float _bannerWidth;
    private float _nameWidth;
    private float _born;
    private bool _active;

    internal void Attach(RectTransform parent)
    {
        _box = UiImageRow.Create("AyaHitmarker.AssistBox", parent);
        _accent = UiImageRow.Create("AyaHitmarker.AssistAccent", parent);
        _banner = UiTextRow.Create("AyaHitmarker.AssistBanner", parent, TextAlignmentOptions.Center);
        _name = UiTextRow.Create("AyaHitmarker.AssistName", parent, TextAlignmentOptions.Right);
        HideAll();
    }

    /// <summary>没有需要预热的资源（纯白精灵 + TMP 都已就绪）。</summary>
    internal void Warmup() { }

    /// <summary>收到一次助攻：记录并重新计时（同时多条助攻时显示最新一条）。</summary>
    internal void Show(in AssistRecord record)
    {
        if (!Plugin.Cfg.AssistEnabled.Value) return;
        _record = record;
        _born = UiClock.Now;
        _active = true;
        Prepare(Plugin.Cfg);
    }

    private void Prepare(AyaConfig cfg)
    {
        if (_banner == null || _name == null) return;

        int bannerFont = BannerFont(cfg);
        int damage = Mathf.Max(0, Mathf.RoundToInt(_record.Damage));
        _bannerString = Loc.Pick("助攻 +", "Assist +") + damage;
        _bannerWidth = _banner.Measure(_bannerString, bannerFont, FontStyles.Bold);

        string name = string.IsNullOrWhiteSpace(_record.VictimName)
            ? "Unknown"
            : NameTranslit.ForDisplay(_record.VictimName, cfg.NameTransliterate.Value);
        string label = cfg.ShowBossName.Value && _record.VictimIsBoss
            ? BotTypeMapping.DisplayName(_record.Role, _record.BotType)
            : _record.BotType;

        _sb.Clear();
        if (cfg.ShowFaction.Value && !string.IsNullOrWhiteSpace(label)
            && !string.Equals(label, name, StringComparison.OrdinalIgnoreCase))
        {
            _sb.Append("<size=78%><color=#").Append(Hex(KillCardPalette.FactionColor(cfg, _record.BotType)))
                .Append('>').Append(Escape(label)).Append("</color></size>  ");
        }
        _sb.Append(Escape(name));
        _nameRich = _sb.ToString();
        _nameWidth = _name.Measure(_nameRich, cfg.FontSizePlayerName.Value, FontStyles.Bold) + 4f;
    }

    internal void Tick()
    {
        if (!_active) return;
        if (_box == null || _accent == null || _banner == null || _name == null)
        {
            _active = false;
            return;
        }

        var cfg = Plugin.Cfg;
        float duration = Mathf.Max(0.3f, cfg.AssistNoticeDuration.Value);
        float age = UiClock.Now - _born;
        if (age < 0f || age >= duration)
        {
            HideAll();
            return;
        }

        // 0.12s 淡入、最后 0.38s 淡出
        float fadeIn = Mathf.Clamp01(age / 0.12f);
        float fadeOut = Mathf.Clamp01((duration - age) / 0.38f);
        float alpha = Mathf.Min(fadeIn, fadeOut);

        float bannerH = Mathf.Max(16f, cfg.AchievementHeight.Value * 0.78f);
        int bannerFont = BannerFont(cfg);
        float padding = Mathf.Max(4f, cfg.AchievementPadding.Value * 0.8f);
        const float accentW = 4f;

        float boxW = Mathf.Max(40f, _bannerWidth + padding * 2f + accentW);
        Vector2 anchor = KillFeedLayer.Anchor();

        // 固定出现在击杀卡片下方（卡片底 ≈ anchor.y - 112），避免和累加卡片重叠
        float top = anchor.y - 150f;
        float boxCenterX = Mathf.Round(anchor.x - boxW * 0.5f);
        float boxLeft = boxCenterX - boxW * 0.5f;

        _box.Apply(new Vector2(boxCenterX, top), new Vector2(boxW, bannerH), UiTheme.WhiteSprite, KillCardPalette.BannerFill);
        _box.SetFade(alpha);

        _accent.Apply(new Vector2(boxLeft + accentW * 0.5f, top), new Vector2(accentW, bannerH),
            UiTheme.WhiteSprite, KillCardPalette.FactionColor(cfg, _record.BotType));
        _accent.SetFade(alpha);

        _banner.Apply(
            new Vector2(boxCenterX + accentW * 0.5f, top),
            new Vector2(boxW, bannerH),
            _bannerString,
            bannerFont,
            UiTheme.Parse(cfg.ColorAchievementText.Value, new Color(0.18f, 0.24f, 0.31f)),
            FontStyles.Bold,
            TextAlignmentOptions.Center);
        _banner.SetFade(alpha);

        float rowH = KillCardLayoutMath.RowHeightFor(cfg.FontSizePlayerName.Value);
        float nameY = top - bannerH * 0.5f - 4f - rowH * 0.5f;
        _name.SetOutline(KillCardPalette.OutlineFor(cfg, cfg.FontSizePlayerName.Value), KillCardPalette.TextOutlineColor);
        _name.Apply(
            new Vector2(anchor.x, nameY),
            new Vector2(Mathf.Max(40f, _nameWidth + 8f), rowH),
            _nameRich,
            cfg.FontSizePlayerName.Value,
            UiTheme.Parse(cfg.ColorPlayerName.Value, new Color(1f, 0.35f, 0.3f)),
            FontStyles.Bold,
            TextAlignmentOptions.Right);
        _name.SetFade(alpha);
    }

    internal void Reset() => HideAll();

    private static int BannerFont(AyaConfig cfg)
        => Mathf.Max(9, Mathf.RoundToInt(cfg.FontSizeAchievement.Value * 0.9f));

    private void HideAll()
    {
        _active = false;
        _box?.Hide();
        _accent?.Hide();
        _banner?.Hide();
        _name?.Hide();
    }

    private static string Hex(Color c)
    {
        int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
        int g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
        int b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
        return $"{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>TMP 富文本以 &lt;&gt; 为标记，玩家昵称 / 武器名里出现就会吞标签，必须转义。</summary>
    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.IndexOf('<') < 0 && s.IndexOf('>') < 0) return s;
        return s.Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
