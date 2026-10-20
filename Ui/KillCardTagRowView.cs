using System.Collections.Generic;
using AyaHitmarker.Assets;
using AyaHitmarker.Game;
using TMPro;
using UnityEngine;

namespace AyaHitmarker.Ui;

/// <summary>
/// 击杀卡片上的**标签行**（距离 / 方式 / 击杀类，描边小方框）。
///
/// 排布从脊线往左：击杀类标签最靠脊线、和 BF6 一样「最新的在最右」；每个标签的宽度实测一次，
/// 之后逐帧只读缓存。渲染时框与字各带 CanvasGroup，由 <see cref="UiTagRow.SetFade"/> 一起淡入淡出。
///
/// 从 <see cref="KillFeedLayer"/> 拆出（0.6 卡片拆分），行为保持不变。
/// </summary>
internal sealed class KillCardTagRowView
{
    internal const int MaxTags = 6;
    private const float TagGap = 6f;
    private const float TagPadX = 9f;

    private const float DelayTag = 0f;   // 标签是 BF6 里最先出现的元素
    private const int SeedTag = 41;

    private static readonly Color TagWhite = new Color(0.95f, 0.96f, 0.98f, 1f);
    private static readonly Color TagCoral = new Color(1f, 0.478f, 0.431f, 1f);
    private static readonly Color TagAmber = new Color(1f, 0.784f, 0.341f, 1f);
    private static readonly Color TagSteel = new Color(0.686f, 0.769f, 0.839f, 1f);

    private struct TagView
    {
        internal string Text;
        internal float Width;
        internal Color Border;
        internal float Born;
        internal int Seed;
    }

    private readonly TagView[] _tags = new TagView[MaxTags];
    private readonly UiTagRow?[] _rows = new UiTagRow?[MaxTags];
    private readonly UiImageRow?[] _flashes = new UiImageRow?[MaxTags];
    private int _tagCount;

    private static readonly Dictionary<int, string> DistanceCache = new Dictionary<int, string>(64);

    /// <summary>标签行总宽度（含间隔）；<see cref="KillFeedLayer"/> 布局用。</summary>
    internal float Width { get; private set; }

    /// <summary>当前标签个数。</summary>
    internal int Count => _tagCount;

    internal void Attach(RectTransform parent)
    {
        for (int i = 0; i < MaxTags; i++)
        {
            _rows[i] = UiTagRow.Create("AyaHitmarker.KillTag" + i, parent);
            _flashes[i] = UiImageRow.Create("AyaHitmarker.KillTagFlash" + i, parent);
        }
    }

    /// <summary>
    /// 每杀一次：按模型里并集后的标签 + 配置重新量宽 / 定色。逐帧绘制只读这里的结果，不再拼串。
    /// </summary>
    internal void Prepare(AyaConfig cfg, KillCardModel model)
    {
        _tagCount = 0;
        Width = 0f;
        if (!cfg.ShowTags.Value) return;

        int fontSize = cfg.FontSizeTag.Value;

        // 距离（最左，次要信息）
        float minDist = Mathf.Max(0f, cfg.TagMinDistance.Value);
        float dist = model.LastDistance;
        if (dist > 0.5f && dist >= minDist)
            AddTag(DistanceString(dist), TagSteel, model.VictimBorn, fontSize, 0);

        // 方式标签在前，击杀类在脊线侧
        var tags = model.Tags;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < tags.Count && _tagCount < MaxTags; i++)
            {
                bool isKind = tags[i].Kind == CardTagKind.Kill || tags[i].Kind == CardTagKind.Headshot;
                if ((pass == 0) == isKind) continue;
                if (!cfg.ShowKillMethod.Value && !isKind) continue;
                AddTag(TagLabel(tags[i].Kind), TagColor(tags[i].Kind), tags[i].Born, fontSize, i + 1);
            }
        }

        for (int i = 0; i < _tagCount; i++) Width += _tags[i].Width;
        if (_tagCount > 1) Width += (_tagCount - 1) * TagGap;
    }

    private void AddTag(string text, Color border, float born, int fontSize, int seedOffset)
    {
        if (_tagCount >= MaxTags) return;
        var row = _rows[_tagCount];
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

    /// <summary>标签行当前高度（布局用）。</summary>
    internal static float TagHeight(AyaConfig cfg) => Mathf.Round(Mathf.Max(8f, cfg.FontSizeTag.Value * 1.55f));

    internal void Draw(AyaConfig cfg, in KillCardAnim anim, float exitAge, float exitDur,
        Vector2 anchor, in KillCardLayout layout)
    {
        var box = ProceduralIcons.TagBox();

        if (!cfg.ShowTags.Value || _tagCount == 0 || box == null)
        {
            HideAll();
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
            var row = _rows[j];
            var flash = _flashes[j];
            if (row == null || flash == null) continue;

            float center = Mathf.Round(right - tag.Width * 0.5f);
            right -= tag.Width + TagGap;

            anim.Fades(tag.Born, DelayTag, tag.Seed, GlitchMath.RevealTiming.Quick, 1, exitAge, exitDur,
                out float content, out float block);

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
            _rows[j]?.Hide();
            _flashes[j]?.Hide();
        }
    }

    internal void HideAll()
    {
        for (int i = 0; i < MaxTags; i++)
        {
            _rows[i]?.Hide();
            _flashes[i]?.Hide();
        }
    }

    /// <summary>清掉当前标签数据（卡片结束时调用；行由 <see cref="HideAll"/> 隐藏）。</summary>
    internal void Clear()
    {
        _tagCount = 0;
        Width = 0f;
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

    private static string DistanceString(float meters)
    {
        int m = Mathf.Max(0, Mathf.RoundToInt(meters));
        if (DistanceCache.TryGetValue(m, out var s)) return s;
        s = m + "m";
        if (DistanceCache.Count > 512) DistanceCache.Clear();
        DistanceCache[m] = s;
        return s;
    }
}
