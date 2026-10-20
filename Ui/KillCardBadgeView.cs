using AyaHitmarker.Assets;
using AyaHitmarker.Game;
using TMPro;
using UnityEngine;

namespace AyaHitmarker.Ui;

/// <summary>
/// 击杀卡片最左边的**军衔徽章**：阵营色描边底板 + 米灰色军衔图 + 右下角等级小标。
///
/// 只有 PMC 有徽章（按等级向下借用档位，见 <see cref="IconBank.GetRankIcon"/>）；
/// 内容变宽 / 变窄时徽章平滑地挪到新位置（和骷髅的推挤同一个速度）。
///
/// 从 <see cref="KillFeedLayer"/> 拆出（0.6 卡片拆分），行为保持不变。
/// </summary>
internal sealed class KillCardBadgeView
{
    private const float ChipHeight = 17f;
    private const float PlatePad = 7f;

    private const float DelayBadge = 0.03f;
    private const int SeedBadge = 67;

    private UiImageRow? _plate;
    private UiImageRow? _badge;
    private UiImageRow? _flash;
    private UiImageRow? _chip;
    private UiTextRow? _level;

    private Sprite? _rankSprite;
    private string _levelText = string.Empty;
    private float _levelWidth;
    private Color _factionColor = Color.white;
    private float _x = float.NaN;

    /// <summary>这一杀有没有徽章（PMC + 图标文件存在 + 开关打开）；<see cref="KillFeedLayer"/> 布局用。</summary>
    internal bool HasBadge { get; private set; }

    /// <summary>诊断日志用的徽章 RectTransform（没建出来时返回 null）。</summary>
    internal RectTransform? Rt => _badge?.Rt;

    internal void Attach(RectTransform parent)
    {
        // 创建顺序 = 绘制顺序：底板 → 军衔图 → 等级小标 → 色块闪出
        _plate = UiImageRow.Create("AyaHitmarker.KillBadgePlate", parent);
        _plate.SetSliced(true);
        _badge = UiImageRow.Create("AyaHitmarker.KillBadge", parent);
        _chip = UiImageRow.Create("AyaHitmarker.KillBadgeChip", parent);
        _level = UiTextRow.Create("AyaHitmarker.KillBadgeLevel", parent, TextAlignmentOptions.Center);
        _flash = UiImageRow.Create("AyaHitmarker.KillBadgeFlash", parent);
    }

    /// <summary>每杀一次：决定徽章图 / 等级文字 / 阵营色。宽度实测一次，逐帧只读缓存。</summary>
    internal void Prepare(AyaConfig cfg, in KillRecord rec, Color factionColor)
    {
        _factionColor = factionColor;

        // 军衔徽章（只有 PMC 有）
        _rankSprite = cfg.ShowFactionIcon.Value && rec.IsPmc ? IconBank.GetRankIcon(rec.VictimLevel) : null;
        HasBadge = _rankSprite != null;

        _levelText = rec.IsPmc && rec.VictimLevel > 0 ? rec.VictimLevel.ToString() : string.Empty;
        _levelWidth = string.IsNullOrEmpty(_levelText) || _level == null
            ? 0f
            : _level.Measure(_levelText, 13, FontStyles.Bold);
    }

    /// <summary>新开一张卡：丢掉上一张卡的徽章位置。</summary>
    internal void ResetViewState() => _x = float.NaN;

    internal void Draw(AyaConfig cfg, in KillCardAnim anim, KillCardModel model, Vector2 anchor,
        in KillCardLayout layout, float exitAge, float exitDur)
    {
        if (_plate == null || _badge == null || _flash == null || _chip == null || _level == null) return;

        var plateSprite = ProceduralIcons.TagBox();
        if (!cfg.ShowFactionIcon.Value || _rankSprite == null || plateSprite == null)
        {
            HideAll();
            return;
        }

        int seed = SeedBadge + model.VictimSerial * 11;
        anim.Reveal(model.VictimBorn, DelayBadge, seed, GlitchMath.RevealTiming.Standard, out float content, out float block);
        float exit = anim.Exit(1, seed, exitAge, exitDur);
        content *= exit;
        block *= exit;

        if (content <= 0.003f && block <= 0.003f)
        {
            HideAll();
            return;
        }

        float icon = Mathf.Max(8f, cfg.FactionIconSize.Value);
        float plate = icon + PlatePad * 2f;

        // 内容变宽 / 变窄时徽章平滑地挪到新位置（和骷髅的推挤同一个速度）
        float targetX = anchor.x + layout.BadgeX;
        if (float.IsNaN(_x)) _x = targetX;
        else if (Mathf.Abs(_x - targetX) > 0.1f) _x = Mathf.Lerp(_x, targetX, Mathf.Clamp01(anim.Dt * Mathf.Max(0.01f, cfg.SkullPushAnimationSpeed.Value)));
        else _x = targetX;

        float y = anchor.y + layout.BadgeY + cfg.FactionIconVerticalOffset.Value;
        Vector2 center = new Vector2(Mathf.Round(_x), Mathf.Round(y));

        float age = anim.Now - (model.VictimBorn + DelayBadge);
        float pop = UiAnim.OutBack(UiAnim.Clamp01(age / 0.16f));
        float scale = 1.18f + (1f - 1.18f) * pop;

        if (content > 0.003f)
        {
            // 底板：战地标签同款的描边方框，描边取阵营色（USEC 蓝 / BEAR 橙 …），底是半透明黑 ——
            // 军衔图本身是米灰色的浅浮雕，直接放在浅色背景上几乎看不见，垫一块深色底就醒目了
            _plate.Apply(center, new Vector2(plate, plate), plateSprite, _factionColor, scale);
            _plate.SetFade(content);

            _badge.Img.preserveAspect = true;
            _badge.Apply(center, new Vector2(icon, icon), _rankSprite, Color.white, scale);
            _badge.SetFade(content);

            // 等级小标：压在底板右下角
            if (cfg.ShowLevelChip.Value && !string.IsNullOrEmpty(_levelText))
            {
                float chipW = Mathf.Round(_levelWidth + 12f);
                float chipH = ChipHeight;
                Vector2 chipCenter = center + new Vector2(plate * 0.5f - chipW * 0.5f + 3f, -plate * 0.5f + chipH * 0.5f - 2f) * scale;
                _chip.SetSliced(true);
                _chip.Apply(chipCenter, new Vector2(chipW, chipH), plateSprite, _factionColor, scale);
                _chip.SetFade(content);
                _level.Apply(chipCenter, new Vector2(chipW, chipH), _levelText, 13, Color.white, FontStyles.Bold, TextAlignmentOptions.Center, scale);
                _level.SetFade(content);
            }
            else
            {
                _chip.Hide();
                _level.Hide();
            }
        }
        else
        {
            _plate.Hide();
            _badge.Hide();
            _chip.Hide();
            _level.Hide();
        }

        if (block > 0.003f)
        {
            _flash.Apply(center, new Vector2(plate * 0.9f, plate * 0.9f), UiTheme.WhiteSprite, _factionColor, scale);
            _flash.SetFade(block * 0.45f);
        }
        else
        {
            _flash.Hide();
        }
    }

    internal void HideAll()
    {
        _plate?.Hide();
        _badge?.Hide();
        _flash?.Hide();
        _chip?.Hide();
        _level?.Hide();
    }
}
