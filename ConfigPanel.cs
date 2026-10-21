using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace AyaHitmarker;

// ConfigurationManager reads these public fields by name. No assembly dependency is required.
// Contract: https://github.com/BepInEx/BepInEx.ConfigurationManager/blob/master/ConfigurationManagerAttributes.cs
internal sealed class ConfigurationManagerAttributes
{
    public string? Category;
    public int? Order;
    public bool? IsAdvanced;
    public bool? Browsable;
    public bool? ReadOnly;
    public bool? ShowRangeAsPercent;
    public System.Action<ConfigEntryBase>? CustomDrawer;
}

internal static class ConfigPanel
{
    private readonly record struct Presentation(string Category, int Order, bool Advanced);
    private static Presentation For(string name) => name switch
    {
        "DebugEnabled" => new("90. 调试与排障", -1, true),
        "TestHotkeysEnabled" => new("90. 调试与排障", -2, true),
        "TestHitKey" => new("90. 调试与排障", -3, true),
        "TestKillKey" => new("90. 调试与排障", -4, true),
        "TestDeathKey" => new("90. 调试与排障", -5, true),
        "ConfigVersion" => new("90. 调试与排障", -6, true),
        "HitMarkerEnabled" => new("10. 命中提示", 999, false),
        "HitBaseSize" => new("10. 命中提示", 998, false),
        "HitDuration" => new("10. 命中提示", 997, false),
        "HitMarkerAnimationScale" => new("10. 命中提示", -4, true),
        "HitMarkerCenterOffset" => new("10. 命中提示", 995, false),
        "HitMarkerColor" => new("10. 命中提示", 994, false),
        "HitMarkerAtHitPoint" => new("10. 命中提示", 993, false),
        "HitMarkerHeadshotColor" => new("10. 命中提示", 992, false),
        "HitMarkerHeadshotScale" => new("10. 命中提示", -9, true),
        "HitMarkerPopScale" => new("10. 命中提示", -10, true),
        "HitMarkerPopDuration" => new("10. 命中提示", -11, true),
        "HitMarkerOutlineWidth" => new("10. 命中提示", -12, true),
        "HitMarkerExpand" => new("10. 命中提示", -13, true),
        "KillMarkerEnabled" => new("10. 命中提示", 986, false),
        "KillMarkerColor" => new("10. 命中提示", 985, false),
        "KillMarkerScale" => new("10. 命中提示", -16, true),
        "KillMarkerDuration" => new("10. 命中提示", -17, true),
        "ArmorHitMarkerEnabled" => new("10. 命中提示", 984, false),
        "ArmorHitMarkerColor" => new("10. 命中提示", 983, false),
        "HitDirectionEnabled" => new("10. 命中提示", 982, false),
        "HitDirectionColorAbsorb" => new("10. 命中提示", -20, true),
        "HitDirectionColorNormal" => new("10. 命中提示", -21, true),
        "HitDirectionColorHeavy" => new("10. 命中提示", -22, true),
        "HitDirectionHeavyDamage" => new("10. 命中提示", -23, true),
        "HitDirectionEllipse" => new("10. 命中提示", -24, true),
        "HitDirectionDuration" => new("10. 命中提示", -25, true),
        "DamageTextEnabled" => new("20. 伤害数字", 999, false),
        "DamageTextMode" => new("20. 伤害数字", 998, false),
        "DamageTextSize" => new("20. 伤害数字", 997, false),
        "DamageTextColor" => new("20. 伤害数字", 996, false),
        "DamageTextOutlineColor" => new("20. 伤害数字", -5, true),
        "DamageTextHeadshotOutlineColor" => new("20. 伤害数字", -6, true),
        "DamageTextOutlineWidth" => new("20. 伤害数字", -7, true),
        "DamageTextPadding" => new("20. 伤害数字", -8, true),
        "DamageMultiTextPadding" => new("20. 伤害数字", -9, true),
        "DamageTextLifetime" => new("20. 伤害数字", 990, false),
        "DamageTextPopScale" => new("20. 伤害数字", -11, true),
        "DamageTextPopDuration" => new("20. 伤害数字", -12, true),
        "DamageTextHeadshotColor" => new("20. 伤害数字", 987, false),
        "DamageTextKillColor" => new("20. 伤害数字", 986, false),
        "DamageTextLayout" => new("20. 伤害数字", 985, false),
        "DamageTextArmorColor" => new("20. 伤害数字", 984, false),
        "DamageAccumulateWindow" => new("20. 伤害数字", -13, true),
        "DamageAccumulateLifetime" => new("20. 伤害数字", -14, true),
        "KillFeedEnabled" => new("30. 击杀卡片 · 内容", 999, false),
        "KillFeedHorizontalOffset" => new("31. 击杀卡片 · 布局", 999, false),
        "KillFeedVerticalOffset" => new("31. 击杀卡片 · 布局", 998, false),
        "KillFeedLineSpacing" => new("31. 击杀卡片 · 布局", -3, true),
        "KillFeedDuration" => new("30. 击杀卡片 · 内容", 998, false),
        "CardGlitchEnabled" => new("30. 击杀卡片 · 内容", 997, false),
        "CardExitDuration" => new("33. 击杀卡片 · 动画", -1, true),
        "CardTextOutline" => new("31. 击杀卡片 · 布局", -4, true),
        "ShowSkull" => new("30. 击杀卡片 · 内容", 996, false),
        "SkullSize" => new("31. 击杀卡片 · 布局", -5, true),
        "SkullSpacing" => new("31. 击杀卡片 · 布局", -6, true),
        "StreakWindow" => new("50. 成就与计分规则", -1, true),
        "SkullPushAnimationSpeed" => new("33. 击杀卡片 · 动画", -2, true),
        "KillFeedMaxSkulls" => new("31. 击杀卡片 · 布局", -7, true),
        "SkullPopScale" => new("33. 击杀卡片 · 动画", -3, true),
        "SkullPopDuration" => new("33. 击杀卡片 · 动画", -4, true),
        "ShowScore" => new("30. 击杀卡片 · 内容", 995, false),
        "FontSizeScore" => new("31. 击杀卡片 · 布局", -8, true),
        "ColorScore" => new("32. 击杀卡片 · 配色", -1, true),
        "ScoreHorizontalOffset" => new("31. 击杀卡片 · 布局", -9, true),
        "ScoreCountDuration" => new("33. 击杀卡片 · 动画", -5, true),
        "ScorePopScale" => new("33. 击杀卡片 · 动画", -6, true),
        "ScorePopDuration" => new("33. 击杀卡片 · 动画", -7, true),
        "ShowAchievement" => new("30. 击杀卡片 · 内容", 994, false),
        "FontSizeAchievement" => new("31. 击杀卡片 · 布局", -10, true),
        "AchievementHeight" => new("31. 击杀卡片 · 布局", -11, true),
        "AchievementPadding" => new("31. 击杀卡片 · 布局", -12, true),
        "AchievementVerticalOffset" => new("31. 击杀卡片 · 布局", -13, true),
        "ColorAchievementText" => new("32. 击杀卡片 · 配色", -2, true),
        "ColorAchievementNormal" => new("32. 击杀卡片 · 配色", -3, true),
        "ColorAchievementGood" => new("32. 击杀卡片 · 配色", -4, true),
        "ColorAchievementGreat" => new("32. 击杀卡片 · 配色", -5, true),
        "ColorAchievementEpic" => new("32. 击杀卡片 · 配色", -6, true),
        "ShowStreakBar" => new("30. 击杀卡片 · 内容", 993, false),
        "StreakBarWidth" => new("31. 击杀卡片 · 布局", -14, true),
        "StreakBarHeight" => new("31. 击杀卡片 · 布局", -15, true),
        "StreakBarGap" => new("31. 击杀卡片 · 布局", -16, true),
        "ColorStreakBar" => new("32. 击杀卡片 · 配色", -7, true),
        "ColorStreakBarBack" => new("32. 击杀卡片 · 配色", -8, true),
        "ShowTags" => new("30. 击杀卡片 · 内容", 992, false),
        "FontSizeTag" => new("31. 击杀卡片 · 布局", -17, true),
        "TagMinDistance" => new("31. 击杀卡片 · 布局", -18, true),
        "ShowFaction" => new("30. 击杀卡片 · 内容", 991, false),
        "ShowFactionIcon" => new("30. 击杀卡片 · 内容", 990, false),
        "ShowLevelChip" => new("30. 击杀卡片 · 内容", 989, false),
        "ShowPlayerName" => new("30. 击杀卡片 · 内容", 988, false),
        "ShowKillDetails" => new("30. 击杀卡片 · 内容", 987, false),
        "ShowKillMethod" => new("30. 击杀卡片 · 内容", 986, false),
        "ShowBossName" => new("30. 击杀卡片 · 内容", 985, false),
        "FontSizePlayerName" => new("31. 击杀卡片 · 布局", -19, true),
        "FontSizeKillDetails" => new("31. 击杀卡片 · 布局", -20, true),
        "FactionIconSize" => new("31. 击杀卡片 · 布局", -21, true),
        "FactionIconVerticalOffset" => new("31. 击杀卡片 · 布局", -22, true),
        "ColorFaction" => new("32. 击杀卡片 · 配色", -9, true),
        "ColorPlayerName" => new("32. 击杀卡片 · 配色", -10, true),
        "ColorKillDetails" => new("32. 击杀卡片 · 配色", -11, true),
        "ColorUSEC" => new("32. 击杀卡片 · 配色", -12, true),
        "ColorBEAR" => new("32. 击杀卡片 · 配色", -13, true),
        "ColorSCAV" => new("32. 击杀卡片 · 配色", -14, true),
        "ColorBOSS" => new("32. 击杀卡片 · 配色", -15, true),
        "ColorFollower" => new("32. 击杀卡片 · 配色", -16, true),
        "ColorRAIDER" => new("32. 击杀卡片 · 配色", -17, true),
        "ColorRouges" => new("32. 击杀卡片 · 配色", -18, true),
        "ColorSectant" => new("32. 击杀卡片 · 配色", -19, true),
        "EnableRingEffect" => new("40. 击杀图标特效", 999, false),
        "RingHeadshotEnabled" => new("40. 击杀图标特效", 998, false),
        "RingEffectDelay" => new("40. 击杀图标特效", -3, true),
        "RingEffectDuration" => new("40. 击杀图标特效", -4, true),
        "RingExplosionSecondRingDelay" => new("40. 击杀图标特效", -5, true),
        "RingHeadshotRadius" => new("40. 击杀图标特效", -6, true),
        "RingHeadshotThickness" => new("40. 击杀图标特效", -7, true),
        "RingHeadshotColor" => new("40. 击杀图标特效", -8, true),
        "RingCritRadius" => new("40. 击杀图标特效", -9, true),
        "RingCritThickness" => new("40. 击杀图标特效", -10, true),
        "RingCritColor" => new("40. 击杀图标特效", -11, true),
        "RingExplosionRadius" => new("40. 击杀图标特效", -12, true),
        "RingExplosionThickness" => new("40. 击杀图标特效", -13, true),
        "RingExplosionColor" => new("40. 击杀图标特效", -14, true),
        "EnableIconGlow" => new("40. 击杀图标特效", 985, false),
        "IconGlowColor" => new("40. 击杀图标特效", -16, true),
        "IconGlowIntensity" => new("40. 击杀图标特效", -17, true),
        "IconGlowSize" => new("40. 击杀图标特效", -18, true),
        "RevengeWindow" => new("50. 成就与计分规则", -2, true),
        "MultiKillWindow" => new("50. 成就与计分规则", -3, true),
        "GunnerWindow" => new("50. 成就与计分规则", -4, true),
        "MarauderWindow" => new("50. 成就与计分规则", -5, true),
        "LongShotDistance" => new("50. 成就与计分规则", -6, true),
        "MarksmanDistance" => new("50. 成就与计分规则", -7, true),
        "EliteLevel" => new("50. 成就与计分规则", -8, true),
        "ScoreBase" => new("50. 成就与计分规则", -9, true),
        "ScoreHeadshotBonus" => new("50. 成就与计分规则", -10, true),
        "ScoreLongShotBonus" => new("50. 成就与计分规则", -11, true),
        "ScoreRevengeBonus" => new("50. 成就与计分规则", -12, true),
        "ScoreMultiKillBonus" => new("50. 成就与计分规则", -13, true),
        "ScoreBossBonus" => new("50. 成就与计分规则", -14, true),
        "SoundEnabled" => new("60. 音效", 999, false),
        "SoundHitEnabled" => new("60. 音效", 998, false),
        "SoundKillEnabled" => new("60. 音效", 997, false),
        "SoundMasterVolume" => new("60. 音效", 996, false),
        "SoundHitVolume" => new("60. 音效", 995, false),
        "SoundHitHeadshotVolume" => new("60. 音效", 994, false),
        "SoundKillVolume" => new("60. 音效", 993, false),
        "SoundKillHeadshotVolume" => new("60. 音效", 992, false),
        "SoundFileHit" => new("60. 音效", -9, true),
        "SoundFileHitHeadshot" => new("60. 音效", -10, true),
        "SoundFileKill" => new("60. 音效", -11, true),
        "SoundFileKillHeadshot" => new("60. 音效", -12, true),
        "SoundMultiKillEnabled" => new("60. 音效", 991, false),
        "SoundFileDoubleKill" => new("60. 音效", -13, true),
        "SoundFileTripleKill" => new("60. 音效", -14, true),
        "SoundFileMultiKill" => new("60. 音效", -15, true),
        "SoundSpatialMode" => new("90. 调试与排障", -7, true),
        "SoundBypassEffects" => new("90. 调试与排障", -8, true),
        "SoundIgnoreListenerPause" => new("90. 调试与排障", -9, true),
        "UiScale" => new("00. 常用与整体界面", 999, false),
        "UiSortingOrder" => new("00. 常用与整体界面", -2, true),
        "UiFontName" => new("00. 常用与整体界面", 997, false),
        "IconStyleMode" => new("00. 常用与整体界面", 996, false),
        _ => throw new ArgumentException("Missing configuration presentation: " + name),
    };

    internal static ConfigEntry<T> Bind<T>(ConfigFile cfg, string name, string section, string key,
        T defaultValue, ConfigDescription? description = null)
    {
        var presentation = For(name);
        var attributes = new ConfigurationManagerAttributes
        {
            Category = presentation.Category,
            Order = presentation.Order,
            IsAdvanced = presentation.Advanced,
            Browsable = name != "ConfigVersion",
            ReadOnly = name == "ConfigVersion",
            ShowRangeAsPercent = name.StartsWith("Sound", StringComparison.Ordinal) && name.Contains("Volume"),
        };
        var range = description?.AcceptableValues;
        // Historical numeric tags were not a ConfigurationManager contract. Use real ranges.
        var tags = description?.Tags;
        if (range == null && tags?.Length == 2)
        {
            if (typeof(T) == typeof(float) && tags[0] is float f0 && tags[1] is float f1)
                range = new AcceptableValueRange<float>(f0, f1);
            else if (typeof(T) == typeof(int) && tags[0] is int i0 && tags[1] is int i1)
                range = new AcceptableValueRange<int>(i0, i1);
        }
        if (range == null && typeof(T) == typeof(float) && name.StartsWith("Ring", StringComparison.Ordinal))
            range = name.Contains("Radius") ? new AcceptableValueRange<float>(1f, 256f)
                : name.Contains("Thickness") ? new AcceptableValueRange<float>(0.1f, 32f)
                : new AcceptableValueRange<float>(0f, 5f);
        if (name == "UiSortingOrder") range = new AcceptableValueRange<int>(-32768, 32767);

        if (name == "HitMarkerCenterOffset") attributes.CustomDrawer = DrawOffset;

        var hint = description?.Description ?? "";
        if (string.IsNullOrWhiteSpace(hint)) hint = Hint(name, key);
        if (defaultValue is string color && color.StartsWith("#", StringComparison.Ordinal))
        {
            attributes.CustomDrawer = DrawColor;
            hint += " 支持 #RRGGBB / #RRGGBBAA；A 为不透明度，0 = 透明，255 = 不透明。";
        }
        if (section == "3. 击杀信息") hint += " 样式调整在下一次击杀时完整生效。";
        if (name.StartsWith("SoundFile", StringComparison.Ordinal))
            hint += " 更换路径后在下一局开始时重新加载，或重启游戏。";
        if (range != null) hint += " 超出范围的输入会自动限制在允许范围内。";
        return cfg.Bind(section, key, defaultValue, new ConfigDescription(hint, range, attributes));
    }

    private static string Hint(string name, string key) => name switch
    {
        "TestHitKey" => "模拟一次命中；需启用测试快捷键，主菜单也可使用。",
        "TestKillKey" => "轮换模拟击杀；快速连按可预览多杀累加卡片。",
        "TestDeathKey" => "记录下一次测试击杀的目标为凶手，再测试击杀可预览复仇成就。",
        "HitMarkerPopDuration" or "DamageTextPopDuration" or "SkullPopDuration" or "ScorePopDuration"
            => "从弹出比例回到正常大小所需的时间，单位为秒。",
        "KillMarkerDuration" => "击杀确认标记从出现到完全消失的时间，单位为秒。",
        "LongShotDistance" => "击杀距离达到此值时触发远程判定与远程得分加成，单位为米。",
        "ScoreBase" => "每次击杀的基础反馈分数；此分数不是游戏经验值。",
        "ScoreHeadshotBonus" => "爆头击杀额外获得的反馈分数。",
        "ScoreRevengeBonus" => "复仇击杀额外获得的反馈分数。",
        "ScoreBossBonus" => "击杀首领额外获得的反馈分数。",
        "RingEffectDelay" => "击杀后等待多久开始播放环形特效，单位为秒。",
        "RingEffectDuration" => "环形特效扩散与淡出的总时长，单位为秒。",
        "RingExplosionSecondRingDelay" => "爆炸击杀的第二个环相对第一个环的延迟，单位为秒。",
        "RingHeadshotEnabled" => "启用环形特效后，爆头击杀也播放环形反馈。",
        "EnableRingEffect" => "在骷髅位置播放击杀环形反馈；关闭时下方环形参数无效。",
        "EnableIconGlow" => "为击杀骷髅叠加辉光；关闭时辉光颜色、强度和扩散无效。",
        "IconGlowIntensity" => "辉光的不透明度倍率；0 = 不显示辉光。",
        "ShowPlayerName" => "显示最近一次击杀的目标昵称。",
        "SoundMasterVolume" => "所有命中与击杀音效的统一音量倍率，0 = 静音，100% = 原音量。",
        "HitMarkerEnabled" => "显示普通命中与爆头命中提示；击杀确认标记由独立开关控制。",
        "DamageTextEnabled" => "在命中图标右侧显示每次命中的伤害数字。",
        "KillFeedEnabled" => "显示击杀卡片；关闭后下方卡片内容、布局与动画参数无效。",
        "AchievementHeight" => "成就横幅的高度，单位为画布像素。",
        "StreakBarWidth" => "连杀进度条的最小宽度；骷髅行更宽时会自动延长。",
        "StreakBarHeight" => "连杀进度条的高度，单位为画布像素。",
        "SoundEnabled" => "音效总开关；关闭后命中和击杀音效均不播放。",
        "SoundHitEnabled" => "播放普通与爆头命中音效；仍受音效总开关控制。",
        "SoundKillEnabled" => "播放普通与爆头击杀音效；仍受音效总开关控制。",
        _ when name.Contains("Volume") => "音量范围 0–100%；实际播放音量为总音量乘以此类音效音量。",
        _ when name.Contains("Color") => key + "，用于对应元素的着色。",
        _ when name.Contains("Radius") => key + "，单位为画布像素。",
        _ when name.Contains("Thickness") => key + "，单位为画布像素。",
        _ when name.StartsWith("FontSize", StringComparison.Ordinal) || name == "DamageTextSize"
            => key + "，单位为画布像素；整体大小还受 UI 缩放影响。",
        _ => key + "。数值尺寸以画布像素为单位，并随 UI 缩放变化。",
    };

    // Keep partial/invalid hex input in the editor without replacing a valid persisted value.
    private static readonly Dictionary<ConfigEntryBase, (string Saved, string Text)> ColorInputs = new();
    private static void DrawColor(ConfigEntryBase entry)
    {
        string saved = (string)entry.BoxedValue;
        if (!ColorInputs.TryGetValue(entry, out var input) || input.Saved != saved)
            input = (saved, saved);
        GUILayout.BeginHorizontal();
        try
        {
            string edited = GUILayout.TextField(input.Text);
            if (edited != input.Text)
            {
                input.Text = edited;
                if (TryColor(edited, out _)) { entry.BoxedValue = edited; input.Saved = edited; }
            }
            var color = AyaHitmarker.Ui.UiTheme.Parse((string)entry.BoxedValue, Color.white);
            Color oldColor = GUI.color;
            try { GUI.color = color; GUILayout.Label("██"); }
            finally { GUI.color = oldColor; }
            float r = Channel("R", color.r), g = Channel("G", color.g);
            float b = Channel("B", color.b), a = Channel("A", color.a);
            if (r != color.r || g != color.g || b != color.b || a != color.a)
            {
                string hex = $"#{Byte(r):X2}{Byte(g):X2}{Byte(b):X2}{Byte(a):X2}";
                entry.BoxedValue = hex;
                input = (hex, hex);
            }
            if (!TryColor(input.Text, out _)) GUILayout.Label("未保存：需6/8位HEX");
            ColorInputs[entry] = input;
        }
        finally { GUILayout.EndHorizontal(); }
    }

    private static readonly Dictionary<ConfigEntryBase, (string Saved, string X, string Y)> OffsetInputs = new();
    private static void DrawOffset(ConfigEntryBase entry)
    {
        string saved = (string)entry.BoxedValue;
        if (!OffsetInputs.TryGetValue(entry, out var input) || input.Saved != saved)
        {
            string[] parts = saved.Split(',');
            input = (saved, parts.Length == 2 ? parts[0] : "0", parts.Length == 2 ? parts[1] : "5");
        }
        GUILayout.BeginHorizontal();
        try
        {
            GUILayout.Label("X →");
            string x = GUILayout.TextField(input.X);
            GUILayout.Label("Y ↓");
            string y = GUILayout.TextField(input.Y);
            if (x != input.X || y != input.Y)
            {
                input.X = x; input.Y = y;
                if (FiniteNumber(x) && FiniteNumber(y))
                {
                    input.Saved = x.Trim() + "," + y.Trim();
                    entry.BoxedValue = input.Saved;
                }
            }
            if (!FiniteNumber(input.X) || !FiniteNumber(input.Y)) GUILayout.Label("未保存：请输入数字");
            OffsetInputs[entry] = input;
        }
        finally { GUILayout.EndHorizontal(); }
    }

    private static bool FiniteNumber(string text) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value);

    private static int Byte(float channel) => (int)Math.Round(channel * 255f);
    private static float Channel(string label, float value)
    {
        GUILayout.BeginHorizontal();
        try
        {
            GUILayout.Label(label);
            float result = GUILayout.HorizontalSlider(value, 0f, 1f);
            GUILayout.Label(Byte(result).ToString(CultureInfo.InvariantCulture));
            return result;
        }
        finally { GUILayout.EndHorizontal(); }
    }

    private static bool TryColor(string text, out uint value)
    {
        string hex = text.Trim();
        if (hex.StartsWith("#", StringComparison.Ordinal)) hex = hex.Substring(1);
        value = 0;
        return (hex.Length == 6 || hex.Length == 8) &&
            uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }
}
