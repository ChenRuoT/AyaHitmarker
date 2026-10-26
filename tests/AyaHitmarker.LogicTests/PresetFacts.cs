using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using AyaHitmarker.Game;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// 预设目录（0.7.0）的契约。<c>AyaConfig</c> 依赖 BepInEx 不能离线引用，所以这里静态解析源码，逐键对账：
/// 每个预设里的键真实存在、值能按键的类型解析且在范围内、不碰不该碰的键；
/// 以及“高对比 / 色盲友好”预设在红 / 绿 / 蓝色盲模拟下确实比默认更分得开。
/// 写错一个键名、一个数值越界，进游戏时是被 <c>BepInEx</c> 静默钳制 / 忽略，肉眼几乎看不出来，只能靠这里挡。
/// </summary>
internal static class PresetFacts
{
    private static readonly Regex ColorPattern = new(@"^#[0-9A-Fa-f]{8}$", RegexOptions.Compiled);

    private static readonly ColorVisionMode[] CvdModes =
    {
        ColorVisionMode.Normal, ColorVisionMode.Protanopia, ColorVisionMode.Deuteranopia, ColorVisionMode.Tritanopia,
    };

    /// <summary>预设可以涉及的面板分类前缀。50（计分规则）、90（调试）、00（字体 / 画布层级 / 图标样式 / 语言）不在其中。</summary>
    private static readonly string[] AllowedCategoryPrefixes = { "10.", "11.", "12.", "20.", "30.", "31.", "32.", "33.", "40." };

    internal static void Run()
    {
        var keys = ConfigSourceModel.Load();

        Assert.Suite("预设: 目录完整性");
        {
            Assert.Greater(keys.Count, 150, "静态解析出 150 个以上的配置键（防止解析失效后空跑）");

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool uniqueIds = true;
            foreach (var p in Presets.All) uniqueIds &= ids.Add(p.Id);
            Assert.True(uniqueIds, "预设 id 不重复（不区分大小写）");
            Assert.Equal(12, Presets.All.Length, "12 套：默认 / 经典 / 极简 / 高对比 + 旧项目 8 套");

            foreach (string id in new[] { "Default", "Legacy", "Minimal", "HighContrast", "Tactical", "Operator", "Bloodbath", "Headhunter", "Slick", "NightOps", "Inferno", "Plague" })
                Assert.True(Presets.Find(id) != null, "有预设 " + id);

            Assert.Equal(0, Presets.Find("Default")!.Overrides.Length, "Default 是空覆盖（只复位）");
            bool allHaveOverrides = true;
            foreach (var p in Presets.All)
                if (p.Id != "Default" && p.Overrides.Length == 0) allHaveOverrides = false;
            Assert.True(allHaveOverrides, "除 Default 外每套都有内容");

            bool namesOk = true;
            foreach (var p in Presets.All)
            {
                namesOk &= Loc.ContainsCjk(p.NameZh) && !Loc.ContainsCjk(p.NameEn);
                namesOk &= !string.IsNullOrWhiteSpace(p.DescriptionZh) && !string.IsNullOrWhiteSpace(p.DescriptionEn);
                namesOk &= Loc.ContainsCjk(p.DescriptionZh) && !Loc.ContainsCjk(p.DescriptionEn);
            }
            Assert.True(namesOk, "每套预设都有中文名 / 英文名 / 中文说明 / 英文说明，且英文里没有 CJK");

            Assert.True(Presets.Find("highcontrast") != null, "Find 不区分大小写");
            Assert.True(Presets.Find("nope") == null && Presets.Find("") == null && Presets.Find(null) == null, "Find: 未知 / 空 → null");

            var values = Presets.SelectorValues();
            Assert.Equal(Presets.None, values[0], "选择器第一个取值是占位值");
            Assert.Equal(Presets.All.Length + 1, values.Length, "选择器 = 占位值 + 全部预设");
            Assert.Equal("None", Presets.None, "占位值是 None（cfg 里稳定写入的值，不要改）");

            bool noDuplicateKeys = true;
            foreach (var p in Presets.All)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (key, _) in p.Overrides) noDuplicateKeys &= seen.Add(key);
            }
            Assert.True(noDuplicateKeys, "同一套预设里没有重复的键");
            Assert.Greater(Presets.UnionKeys.Count, 40, "并集覆盖 40 个以上的键");
        }

        Assert.Suite("预设: 每个键都真实存在，类型 / 值 / 范围都合法");
        {
            var problems = new List<string>();
            foreach (var preset in Presets.All)
            {
                foreach (var (key, value) in preset.Overrides)
                {
                    if (!keys.TryGetValue(key, out var info))
                    {
                        problems.Add($"{preset.Id}.{key}: AyaConfig 里没有这个键");
                        continue;
                    }
                    string? problem = Validate(info, value);
                    if (problem != null) problems.Add($"{preset.Id}.{key}={value}: {problem}");
                }
            }
            Assert.Equal(0, problems.Count, "全部预设值合法" + (problems.Count > 0 ? "：\n    " + string.Join("\n    ", problems) : ""));
        }

        Assert.Suite("预设: 不碰不该碰的键");
        {
            var problems = new List<string>();
            foreach (var key in Presets.UnionKeys)
            {
                if (!keys.TryGetValue(key, out var info)) continue;   // 不存在的键上面已经报过

                bool allowed = false;
                foreach (var prefix in AllowedCategoryPrefixes)
                    if (info.Category.StartsWith(prefix, StringComparison.Ordinal)) allowed = true;
                if (!allowed) problems.Add($"{key}: 面板分类 '{info.Category}' 不允许预设修改（偏移 / 计分 / 音效 / 调试 / 字体 / 语言都不在内）");

                if (key.Contains("Offset", StringComparison.Ordinal)) problems.Add($"{key}: 位置偏移是用户自己的几何设置，预设不能碰");
                if (key.StartsWith("Sound", StringComparison.Ordinal) || key.StartsWith("Test", StringComparison.Ordinal))
                    problems.Add($"{key}: 音效 / 测试快捷键不属于预设");
                if (key is "ConfigVersion" or "UiFontName" or "UiSortingOrder" or "UiScale" or "IconStyleMode" or "Language" or "StylePreset"
                    or "HitMarkerAtHitPoint" or "DebugEnabled")
                    problems.Add($"{key}: 不能出现在预设里");
                if (info.CsType == "KeyboardShortcut") problems.Add($"{key}: 快捷键不属于预设");
            }
            Assert.Equal(0, problems.Count, "预设并集里没有受保护的键" + (problems.Count > 0 ? "：\n    " + string.Join("\n    ", problems) : ""));
        }

        Assert.Suite("预设: 抽出来的卡片颜色默认值 == 原来写死的常量（默认外观不变）");
        {
            // 0.7.0 把 KillCardPalette / KillCardTagRowView 里写死的 7 个颜色抽成了配置键。
            // 默认外观不变的前提是：配置默认值 == 常量量化成 8 位的十六进制。
            string palette = ConfigSourceModel.ReadRepoFile("Ui/KillCardPalette.cs");
            string tags = ConfigSourceModel.ReadRepoFile("Ui/KillCardTagRowView.cs");

            void Check(string key, string constantsSource, string pattern)
            {
                var m = Regex.Match(constantsSource, pattern);
                if (!m.Success || !keys.TryGetValue(key, out var info) || info.DefaultLiteral == null)
                {
                    Assert.True(false, $"{key}: 找不到常量或默认值（解析失败）");
                    return;
                }
                string expected = HexOfUnityColor(m.Groups[1].Value);
                Assert.Equal(expected.ToUpperInvariant(), info.DefaultLiteral.ToUpperInvariant(), $"{key} 默认值 == 原常量");
            }

            Check("ColorCardNormal", palette, @"IceWhiteDefault\s*=\s*new Color\(([^)]*)\)");
            Check("ColorCardHeadshot", palette, @"CoralDefault\s*=\s*new Color\(([^)]*)\)");
            Check("ColorBannerFill", palette, @"BannerFillDefault\s*=\s*new Color\(([^)]*)\)");
            Check("ColorTagKill", tags, @"Cfg\.ColorTagKill\.Value,\s*new Color\(([^)]*)\)");
            Check("ColorTagHeadshot", tags, @"Cfg\.ColorTagHeadshot\.Value,\s*new Color\(([^)]*)\)");
            Check("ColorTagSpecial", tags, @"Cfg\.ColorTagSpecial\.Value,\s*new Color\(([^)]*)\)");
            Check("ColorTagDistance", tags, @"Cfg\.ColorTagDistance\.Value,\s*new Color\(([^)]*)\)");
        }

        Assert.Suite("预设: 旧项目 8 套的映射（与旧 JSON 一致）");
        {
            // 抽查：operator 的名字色 / 环色 / 辉光；骷髅大小按 44/64 缩放
            var op = Presets.Find("Operator")!;
            Assert.Equal("#E62626FF", ValueOf(op, "RingHeadshotColor"), "Operator 爆头环 E62626");
            Assert.Equal("#66BFD9FF", ValueOf(op, "RingCritColor"), "Operator 暴击环 66BFD9");
            Assert.Equal("3.5", ValueOf(op, "RingHeadshotThickness"), "Operator 爆头环粗细 3.5");
            Assert.Equal("true", ValueOf(op, "EnableIconGlow"), "Operator 辉光开");
            Assert.Equal("0.35", ValueOf(op, "IconGlowIntensity"), "Operator 辉光强度");
            Assert.Equal("47", ValueOf(op, "SkullSize"), "Operator 骷髅 68 → 47");

            var tac = Presets.Find("Tactical")!;
            Assert.Equal("false", ValueOf(tac, "EnableIconGlow"), "Tactical 辉光关");
            Assert.Equal("44", ValueOf(tac, "SkullSize"), "Tactical 骷髅 64 → 44（= 新默认）");
            Assert.Equal("#FFFFCCFF", ValueOf(tac, "ColorScore"), "旧 ColorExperience FFFFCC → 新 ColorScore");

            var inf = Presets.Find("Inferno")!;
            Assert.Equal("48", ValueOf(inf, "SkullSize"), "Inferno 骷髅 70 → 48");
            Assert.Equal("7", ValueOf(inf, "RingExplosionThickness"), "Inferno 爆炸环粗细 7");

            foreach (string id in new[] { "Tactical", "Operator", "Bloodbath", "Headhunter", "Slick", "NightOps", "Inferno", "Plague" })
                Assert.Equal(15, Presets.Find(id)!.Overrides.Length, id + " 恰好 15 个键（旧预设的 17 个字段去掉 DisplayName / ColorAccent）");
        }

        Assert.Suite("高对比: 色觉模拟下三类提示依然分得开");
        {
            var hc = Presets.Find("HighContrast")!;

            // 每组：键名 + 阈值。阈值是 ΔE76（四种色觉里最难分的一对）；≥ 25 一眼能分开，≥ 45 非常明显。
            var groups = new (string Label, string[] Keys, double Min)[]
            {
                ("命中标记 普通 / 爆头 / 击杀", new[] { "HitMarkerColor", "HitMarkerHeadshotColor", "KillMarkerColor" }, 45),
                ("受击方向 吸收 / 普通 / 重伤", new[] { "HitDirectionColorAbsorb", "HitDirectionColorNormal", "HitDirectionColorHeavy" }, 30),
                ("伤害数字 普通 / 爆头 / 击杀 / 护甲", new[] { "DamageTextColor", "DamageTextHeadshotColor", "DamageTextKillColor", "DamageTextArmorColor" }, 28),
                ("环形特效 爆头 / 暴击 / 爆炸", new[] { "RingHeadshotColor", "RingCritColor", "RingExplosionColor" }, 30),
                ("骷髅 普通 / 爆头", new[] { "ColorCardNormal", "ColorCardHeadshot" }, 45),
                ("标签 击杀 / 爆头 / 特殊击杀", new[] { "ColorTagKill", "ColorTagHeadshot", "ColorTagSpecial" }, 35),
                ("成就档位强调条 普通 / 精良 / 精英 / 史诗", new[] { "ColorAchievementNormal", "ColorAchievementGood", "ColorAchievementGreat", "ColorAchievementEpic" }, 15),
            };

            foreach (var (label, groupKeys, min) in groups)
            {
                double worst = WorstDeltaE(hc, keys, groupKeys, useOverrides: true);
                Assert.True(worst >= min, $"{label}: 最难分的一对 ΔE = {worst:0.0}，要求 ≥ {min}");
            }

            // 对照 0.6.1 默认：问题组确实被改善了（不是随手换了一套颜色）
            foreach (var (label, groupKeys, _) in groups)
            {
                if (label.StartsWith("成就", StringComparison.Ordinal) || label.StartsWith("骷髅", StringComparison.Ordinal)
                    || label.StartsWith("标签", StringComparison.Ordinal)) continue;   // 默认已经够分，不要求改善

                double before = WorstDeltaE(hc, keys, groupKeys, useOverrides: false);
                double after = WorstDeltaE(hc, keys, groupKeys, useOverrides: true);
                Assert.True(after > before + 2, $"{label}: 默认 ΔE = {before:0.0} → 高对比 ΔE = {after:0.0}（要提高）");
            }

            // 命中标记那一组是 0.6.1 最糟的：爆头标记 #FF4D4D 与击杀标记 #FF4A3D 几乎同色
            double markersBefore = WorstDeltaE(hc, keys, new[] { "HitMarkerColor", "HitMarkerHeadshotColor", "KillMarkerColor" }, useOverrides: false);
            Assert.True(markersBefore < 10, $"（确认问题存在）默认命中标记三色最难分的一对 ΔE = {markersBefore:0.0} < 10");

            // 非颜色通道：高对比预设同时把爆头 / 击杀标记做得更大、描边更粗
            Assert.True(float.Parse(ValueOf(hc, "HitMarkerHeadshotScale"), CultureInfo.InvariantCulture)
                        > float.Parse(keys["HitMarkerHeadshotScale"].DefaultLiteral!, CultureInfo.InvariantCulture),
                "爆头标记倍率大于默认");
            Assert.True(float.Parse(ValueOf(hc, "KillMarkerScale"), CultureInfo.InvariantCulture)
                        > float.Parse(keys["KillMarkerScale"].DefaultLiteral!, CultureInfo.InvariantCulture),
                "击杀标记扩散倍率大于默认");
            Assert.True(float.Parse(ValueOf(hc, "HitMarkerOutlineWidth"), CultureInfo.InvariantCulture)
                        > float.Parse(keys["HitMarkerOutlineWidth"].DefaultLiteral!, CultureInfo.InvariantCulture),
                "命中标记描边更粗");

            // 环形特效三种粗细彼此拉开（颜色之外的第二通道）
            float rh = float.Parse(ValueOf(hc, "RingHeadshotThickness"), CultureInfo.InvariantCulture);
            float rc = float.Parse(ValueOf(hc, "RingCritThickness"), CultureInfo.InvariantCulture);
            float re = float.Parse(ValueOf(hc, "RingExplosionThickness"), CultureInfo.InvariantCulture);
            Assert.True(rh > rc * 1.3f && re > rh * 1.3f, $"环形粗细 暴击 {rc} < 爆头 {rh} < 爆炸 {re}，每档至少差 30%");
        }
    }

    // ------------------------------------------------------------------ 辅助

    private static string ValueOf(Preset preset, string key)
    {
        foreach (var (k, v) in preset.Overrides)
            if (k == key) return v;
        return "(缺失)";
    }

    /// <summary>某个键的有效颜色：预设里写了就用预设的，否则（或 useOverrides=false）用 AyaConfig 里的默认字面量。</summary>
    private static ColorVision.Rgb Resolve(Preset preset, IReadOnlyDictionary<string, ConfigKeyInfo> keys, string key, bool useOverrides)
    {
        string? hex = null;
        if (useOverrides)
        {
            foreach (var (k, v) in preset.Overrides)
                if (k == key) hex = v;
        }
        hex ??= keys[key].DefaultLiteral;
        Assert.True(ColorVision.TryParseHex(hex, out var rgb), $"{key} 能解析出颜色: {hex}");
        return rgb;
    }

    private static double WorstDeltaE(Preset preset, IReadOnlyDictionary<string, ConfigKeyInfo> keys, string[] groupKeys, bool useOverrides)
    {
        var colors = new List<ColorVision.Rgb>(groupKeys.Length);
        foreach (var k in groupKeys) colors.Add(Resolve(preset, keys, k, useOverrides));

        double worst = double.PositiveInfinity;
        foreach (var mode in CvdModes)
            worst = Math.Min(worst, ColorVision.MinPairwiseDeltaE(colors, mode));
        return worst;
    }

    /// <summary>"0.91f, 0.94f, 0.96f, 1f" → "#E8F0F5FF"（按 Unity 的 8 位量化：四舍五入）。</summary>
    private static string HexOfUnityColor(string args)
    {
        var parts = args.Split(',');
        int Channel(int i)
        {
            string s = parts[i].Trim().TrimEnd('f', 'F');
            return (int)Math.Round(double.Parse(s, CultureInfo.InvariantCulture) * 255.0, MidpointRounding.AwayFromZero);
        }
        return $"#{Channel(0):X2}{Channel(1):X2}{Channel(2):X2}{(parts.Length > 3 ? Channel(3) : 255):X2}";
    }

    /// <summary>值是否合法；合法返回 null，否则返回原因。</summary>
    private static string? Validate(ConfigKeyInfo info, string value)
    {
        switch (info.CsType)
        {
            case "bool":
                return value is "true" or "false" ? null : "bool 只能写小写 true / false";

            case "int":
            {
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return "不是整数";
                return InRange(info, i);
            }

            case "float":
            {
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return "不是浮点数（不变区域）";
                return InRange(info, f);
            }

            case "string":
            {
                // 预设里的字符串只有颜色
                if (info.DefaultLiteral == null || !info.DefaultLiteral.StartsWith("#", StringComparison.Ordinal))
                    return "预设暂只支持颜色字符串";
                return ColorPattern.IsMatch(value) ? null : "颜色必须是 #RRGGBBAA（8 位）";
            }

            default:
                return $"预设暂不支持类型 {info.CsType}";
        }
    }

    private static string? InRange(ConfigKeyInfo info, double v)
    {
        if (info.Min == null || info.Max == null) return null;   // 没有声明范围的键不检查
        return v >= info.Min.Value - 1e-9 && v <= info.Max.Value + 1e-9
            ? null
            : $"超出范围 [{info.Min}, {info.Max}]（BepInEx 会静默钳制）";
    }
}
