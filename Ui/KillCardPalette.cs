using UnityEngine;

namespace AyaHitmarker.Ui;

/// <summary>
/// 击杀卡片共用的配色 / 描边常量。拆视图之后好几个视图都要用同一份「冰白 / 珊瑚红 / 黑色文字描边」，
/// 放这里避免在每个视图里各抄一份。
/// </summary>
internal static class KillCardPalette
{
    /// <summary>战地 6 的冰白色（普通骷髅 / 色块闪出 / 徽记）。</summary>
    internal static readonly Color IceWhite = new Color(0.91f, 0.94f, 0.96f, 1f);

    /// <summary>爆头骷髅的珊瑚红。</summary>
    internal static readonly Color Coral = new Color(1f, 0.416f, 0.369f, 1f);

    /// <summary>卡片文字的黑色描边（略透明）。</summary>
    internal static readonly Color32 TextOutlineColor = new Color32(0, 0, 0, 215);

    /// <summary>成就横幅 / 助攻横条的浅钢色底（战地 6 风格）。</summary>
    internal static readonly Color BannerFill = new Color(0.851f, 0.878f, 0.910f, 0.97f);

    /// <summary>按 <c>BotType</c> 取阵营色：击杀卡片（名字 / 徽章）与助攻提示共用。</summary>
    internal static Color FactionColor(AyaConfig cfg, string botType)
    {
        Color baseColor = UiTheme.Parse(cfg.ColorFaction.Value, Color.white);
        return botType switch
        {
            "USEC" => UiTheme.Parse(cfg.ColorUSEC.Value, baseColor),
            "BEAR" => UiTheme.Parse(cfg.ColorBEAR.Value, baseColor),
            "SCAV" => UiTheme.Parse(cfg.ColorSCAV.Value, baseColor),
            "BOSS" => UiTheme.Parse(cfg.ColorBOSS.Value, baseColor),
            "Follower" => UiTheme.Parse(cfg.ColorFollower.Value, baseColor),
            "Raider" => UiTheme.Parse(cfg.ColorRAIDER.Value, baseColor),
            "Rouges" => UiTheme.Parse(cfg.ColorRouges.Value, baseColor),
            "Sectant" => UiTheme.Parse(cfg.ColorSectant.Value, baseColor),
            _ => baseColor,
        };
    }

    /// <summary>
    /// 文字描边宽度随字号缩放。TMP 的描边宽度是归一化的，同一个数值落在 15px 的小字上会把笔画吃掉一大半
    /// （实测 0.22 的名字 / 详情发暗发细），落在 34px 的分数上才刚好。以 24px 为基准线性缩放，并限幅。
    /// </summary>
    internal static float OutlineFor(AyaConfig cfg, int fontSize)
        => Mathf.Clamp01(cfg.CardTextOutline.Value * Mathf.Clamp(fontSize / 24f, 0.7f, 1.3f));
}
