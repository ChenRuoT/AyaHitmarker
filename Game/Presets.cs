using System;
using System.Collections.Generic;
using System.Globalization;

namespace AyaHitmarker.Game;

/// <summary>
/// 一套预设 = 一组「(配置属性名, 序列化值)」。键是 <c>AyaConfig</c> 的 C# 属性名（<c>nameof</c>），不是中文 cfg 键；
/// 值是 BepInEx 的 TOML 序列化文本（<c>"1.15"</c> / <c>"true"</c> / <c>"#FFD24AFF"</c> / 枚举成员名，浮点一律不变区域）。
/// </summary>
internal sealed class Preset
{
    internal readonly string Id;
    internal readonly string NameZh;
    internal readonly string NameEn;
    internal readonly string DescriptionZh;
    internal readonly string DescriptionEn;
    internal readonly (string Key, string Value)[] Overrides;

    internal Preset(string id, string nameZh, string nameEn, string descriptionZh, string descriptionEn,
        params (string Key, string Value)[] overrides)
    {
        Id = id;
        NameZh = nameZh;
        NameEn = nameEn;
        DescriptionZh = descriptionZh;
        DescriptionEn = descriptionEn;
        Overrides = overrides;
    }

    internal string Name => Loc.Pick(NameZh, NameEn);
    internal string Description => Loc.Pick(DescriptionZh, DescriptionEn);
}

/// <summary>
/// 0.7.0 预设目录（纯逻辑，不引用 Unity / BepInEx，离线测试逐键校验）。
///
/// **套用语义**（用户确认）：先把 <see cref="UnionKeys"/>（任一预设提到过的键）全部复位到默认，再写入所选预设的值。
/// 所以切换预设不会留下上一套的残留；而没有任何预设提到过的键（偏移、音效路径、快捷键、计分规则、调试项 …）永远不会被碰。
/// 套用前会把 cfg 备份成 <c>.bak-preset</c>。<see cref="Default"/> 是空覆盖：只做复位，等于“回到出厂外观”。
///
/// **哪些键预设不能碰**由 <c>PresetFacts</c> 静态校验：偏移 / 音效 / 快捷键 / 面板分类 50（计分规则）与 90（调试）/
/// 字体 / 画布层级 / 语言 / 选择器本身。
/// </summary>
internal static class Presets
{
    /// <summary>选择器的占位值：什么都不套用。套用完成后选择器回到它，这样同一套预设可以再次选择。</summary>
    internal const string None = "None";

    internal const string Default = "Default";

    private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>"D4B800" → "#D4B800FF"（旧项目的预设是 6 位 RGB，新配置是 8 位 RGBA）。</summary>
    private static string Hex(string rgb) => "#" + rgb.ToUpperInvariant() + "FF";

    private static (string Key, string Value) K(string key, string value) => (key, value);

    /// <summary>
    /// 旧项目（SimpleHitMarker）的 8 套样式预设之一。旧预设只改渲染期的 17 个字段，映射到 15 个新键：
    /// 三种环的颜色与粗细、图标辉光（开关 / 颜色 / 强度 / 大小）、骷髅大小、名字 / 阵营 / 详情 / 得分颜色
    /// （旧 <c>ColorExperience</c> → 新 <c>ColorScore</c>：新卡片显示的是得分不是经验）。
    /// 数值取自旧项目 <c>KillFeed/StyleCatalog.cs</c> 生成的 <c>Presets/*.json</c>。
    /// 骷髅大小按 44 / 64 缩放（旧基线 64，新默认 44）。
    /// </summary>
    private static Preset Theme(
        string id, string nameZh, string nameEn, string descZh, string descEn,
        string ringHead, string ringCrit, string ringExpl, double thickHead, double thickCrit, double thickExpl,
        bool glow, string glowColor, double glowIntensity, double glowSize, double oldSkull,
        string name, string faction, string details, string score)
        => new Preset(id, nameZh, nameEn, descZh, descEn,
            K("RingHeadshotColor", Hex(ringHead)),
            K("RingCritColor", Hex(ringCrit)),
            K("RingExplosionColor", Hex(ringExpl)),
            K("RingHeadshotThickness", Num(thickHead)),
            K("RingCritThickness", Num(thickCrit)),
            K("RingExplosionThickness", Num(thickExpl)),
            K("EnableIconGlow", glow ? "true" : "false"),
            K("IconGlowColor", Hex(glowColor)),
            K("IconGlowIntensity", Num(glowIntensity)),
            K("IconGlowSize", Num(glowSize)),
            K("SkullSize", Num(Math.Round(oldSkull * 44.0 / 64.0))),
            K("ColorPlayerName", Hex(name)),
            K("ColorFaction", Hex(faction)),
            K("ColorKillDetails", Hex(details)),
            K("ColorScore", Hex(score)));

    internal static readonly Preset[] All =
    {
        new Preset(Default, "战地 6 默认", "Battlefield 6 Default",
            "出厂外观。把预设涉及的所有设置复位成默认值。",
            "Factory look. Resets every setting the presets touch back to its default."),

        new Preset("Legacy", "经典（0.3 尺寸与配色）", "Classic (0.3 sizes and colours)",
            "回到 0.3.0 的卡片尺寸 / 字号 / 配色：命中标记不带描边，伤害数字描边是半透明白，没有故障特效和击杀红色扩散。卡片版式仍是新版。",
            "0.3.0 card sizes, font sizes and colours: no hit-marker outline, translucent white damage-number outline, no glitch effect, no red kill burst. The card layout itself stays the new one.",
            K("HitMarkerOutlineWidth", "0"),
            K("KillMarkerEnabled", "false"),
            K("CardGlitchEnabled", "false"),
            K("DamageTextOutlineColor", "#FFFFFF40"),
            K("DamageTextHeadshotOutlineColor", "#FF0000FF"),
            K("DamageTextOutlineWidth", "0.2"),
            K("KillFeedLineSpacing", "0"),
            K("SkullSize", "40"),
            K("SkullSpacing", "20"),
            K("FontSizeScore", "28"),
            K("ScoreCountDuration", "0.35"),
            K("ScorePopScale", "1.6"),
            K("ScorePopDuration", "0.22"),
            K("FontSizeAchievement", "22"),
            K("AchievementHeight", "30"),
            K("AchievementPadding", "14"),
            K("StreakBarHeight", "4"),
            K("FontSizePlayerName", "18"),
            K("FontSizeKillDetails", "20"),
            K("FactionIconSize", "40"),
            K("ColorScore", "#FFE08AFF"),
            K("ColorPlayerName", "#FF000FFF"),
            K("ColorKillDetails", "#CCCCCCFF"),
            K("ColorStreakBarBack", "#00000099"),
            K("ColorAchievementText", "#FFFFFFFF"),
            K("ColorAchievementNormal", "#46505CFF"),
            K("ColorAchievementGood", "#2F6FB5FF"),
            K("ColorAchievementGreat", "#C2681AFF"),
            K("ColorAchievementEpic", "#A32434FF")),

        new Preset("Minimal", "极简", "Minimal",
            "只留命中标记、伤害数字、骷髅 + 得分 + 名字：关掉标签 / 详情 / 徽章 / 成就横幅 / 连杀进度条 / 环形与辉光特效 / 受击方向 / 助攻提示 / 战局小结。",
            "Hit marker, damage numbers, skull + score + name only. Turns off tags, details, badge, achievement banner, streak bar, ring and glow effects, hit direction, assist notice and the raid summary.",
            K("ShowFaction", "false"),
            K("ShowFactionIcon", "false"),
            K("ShowLevelChip", "false"),
            K("ShowKillDetails", "false"),
            K("ShowTags", "false"),
            K("ShowStreakBar", "false"),
            K("ShowAchievement", "false"),
            K("CardGlitchEnabled", "false"),
            K("EnableRingEffect", "false"),
            K("EnableIconGlow", "false"),
            K("KillMarkerEnabled", "false"),
            K("HitDirectionEnabled", "false"),
            K("AssistEnabled", "false"),
            K("RaidSummaryEnabled", "false")),

        // 高对比 / 色盲友好：Okabe–Ito 色板为基础，亮度另外拉开（深色背景上不选太暗的蓝）。
        // 挑色用的是 Game/ColorVision.cs 的同一套公式，PresetFacts 里逐组断言“红色盲 / 绿色盲 / 蓝色盲模拟下最难分的一对”不低于阈值。
        // 除颜色外还加强了非颜色通道：爆头 / 击杀标记更大、描边更粗、环形粗细拉开。
        new Preset("HighContrast", "高对比 / 色盲友好", "High Contrast / Colour-blind Safe",
            "换成对红绿 / 蓝黄色盲都分得开的配色，并加大爆头 / 击杀标记、加粗描边与环形，让区分不只靠颜色。",
            "Colours that stay distinguishable for red-green and blue-yellow colour blindness, plus larger headshot/kill markers and thicker outlines and rings so the cues don't rely on colour alone.",
            // 命中标记：白 / 橙 / 蓝；护甲辉光用天蓝
            K("HitMarkerColor", "#FFFFFFFF"),
            K("HitMarkerHeadshotColor", "#FFA21FFF"),
            K("KillMarkerColor", "#3FA0FFFF"),
            K("ArmorHitMarkerColor", "#56B4E9FF"),
            K("HitMarkerHeadshotScale", "1.35"),
            K("KillMarkerScale", "2"),
            K("HitMarkerOutlineWidth", "2.2"),
            // 受击方向：天蓝（护甲吸收）/ 黄（普通）/ 朱红（重伤）；簇大小本身也随伤害变
            K("HitDirectionColorAbsorb", "#56B4E9FF"),
            K("HitDirectionColorNormal", "#F0E442FF"),
            K("HitDirectionColorHeavy", "#D55E00FF"),
            K("HitDirectionHeavyDamage", "30"),
            // 伤害数字：白 / 粉紫（爆头）/ 亮朱红（击杀）/ 蓝（护甲，另有盾标）
            K("DamageTextColor", "#FFFFFFFF"),
            K("DamageTextHeadshotColor", "#CC79A7FF"),
            K("DamageTextKillColor", "#FF7A33FF"),
            K("DamageTextArmorColor", "#3FA0FFFF"),
            K("DamageTextOutlineWidth", "0.32"),
            // 击杀卡片：爆头骷髅 / 标签用橙；特殊击杀标签用天蓝；成就档位按亮度递增（灰 → 蓝 → 橙 → 黄）
            K("ColorCardHeadshot", "#FFA21FFF"),
            K("ColorTagHeadshot", "#FFA21FFF"),
            K("ColorTagSpecial", "#56B4E9FF"),
            K("ColorAchievementNormal", "#5A6672FF"),
            K("ColorAchievementGood", "#0072B2FF"),
            K("ColorAchievementGreat", "#E69F00FF"),
            K("ColorAchievementEpic", "#F0E442FF"),
            K("CardTextOutline", "0.2"),
            // 环形特效：黄 / 天蓝 / 朱红，粗细也拉开（4 / 2.5 / 7）
            K("RingHeadshotColor", "#F0E442FF"),
            K("RingCritColor", "#56B4E9FF"),
            K("RingExplosionColor", "#D55E00FF"),
            K("RingHeadshotThickness", "4"),
            K("RingCritThickness", "2.5"),
            K("RingExplosionThickness", "7")),

        // ---- 旧项目的 8 套（只改颜色 / 环 / 辉光 / 骷髅大小）----
        Theme("Tactical", "战术（军绿）", "Tactical (Olive)",
            "旧项目的默认风格：军绿 + 暗金。", "The old project's default look: olive and dull gold.",
            "D4B800", "9CCC65", "F77F00", 3.0, 1.8, 5.4, false, "8EB264", 0.45, 4.0, 64,
            "FF4C4C", "FFFFFF", "CCCCCC", "FFFFCC"),
        Theme("Operator", "特勤（暗红）", "Operator (Dark Red)",
            "暗色隐秘风，红色强调。", "Dark stealth look with red accents.",
            "E62626", "66BFD9", "F2660D", 3.5, 2.0, 6.0, true, "E62626", 0.35, 3.5, 68,
            "E62626", "B2B2BF", "8C8C99", "E6D9B2"),
        Theme("Bloodbath", "血战（血红）", "Bloodbath (Blood Red)",
            "整体偏血红。", "Everything leans blood red.",
            "F2331A", "D91414", "FF5900", 3.5, 2.0, 6.5, true, "D91414", 0.50, 5.0, 70,
            "F2331A", "E68066", "D96659", "FFB280"),
        Theme("Headhunter", "猎头（金）", "Headhunter (Gold)",
            "金色强调。", "Gold accents.",
            "F2CC0D", "99CC59", "E6590D", 4.0, 2.2, 5.5, true, "D4AF37", 0.55, 5.5, 66,
            "F2CC0D", "E6D9B2", "CCB280", "FFE666"),
        Theme("Slick", "冷峻（青蓝）", "Slick (Cyan)",
            "干净的蓝青色。", "Clean blue and cyan.",
            "33B2F2", "4CCC8C", "268CF2", 2.8, 1.8, 5.0, true, "268CD9", 0.40, 4.5, 62,
            "33B2F2", "B2CCF2", "99B2CC", "B2E6FF"),
        Theme("NightOps", "夜行（紫）", "Night Ops (Purple)",
            "紫色夜战风。", "Purple night-ops look.",
            "B240E6", "7399E6", "D94C8C", 3.2, 2.0, 5.8, true, "8033B2", 0.50, 5.0, 66,
            "B240E6", "BFA6E6", "9980B2", "D9B2FF"),
        Theme("Inferno", "炼狱（橙）", "Inferno (Orange)",
            "火焰橙。", "Fire orange.",
            "FF730D", "D98C1A", "FF3300", 3.5, 2.0, 7.0, true, "E6590D", 0.55, 6.0, 70,
            "FF730D", "F2BF80", "D98C59", "FFCC66"),
        Theme("Plague", "瘟疫（毒绿）", "Plague (Toxic Green)",
            "毒绿色。", "Toxic green.",
            "8CD91A", "59BF26", "99B20D", 3.0, 2.5, 6.0, true, "59BF26", 0.45, 4.5, 64,
            "8CD91A", "B2D98C", "8CB266", "BFF266"),
    };

    private static HashSet<string>? _union;

    /// <summary>任一预设提到过的键。套用预设前，这些键会先全部复位到默认值。</summary>
    internal static IReadOnlyCollection<string> UnionKeys
    {
        get
        {
            if (_union == null)
            {
                var set = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in All)
                    foreach (var (key, _) in p.Overrides)
                        set.Add(key);
                _union = set;
            }
            return _union;
        }
    }

    internal static Preset? Find(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var p in All)
        {
            if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) return p;
        }
        return null;
    }

    /// <summary>选择器可选的全部取值：占位值 + 各预设 id（稳定的英文 id，不随界面语言变化）。</summary>
    internal static string[] SelectorValues()
    {
        var list = new List<string>(All.Length + 1) { None };
        foreach (var p in All) list.Add(p.Id);
        return list.ToArray();
    }
}
