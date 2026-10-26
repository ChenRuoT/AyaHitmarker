using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AyaHitmarker.Game;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// 设置面板英文文案（0.7.0）的对账契约。<c>AyaConfig</c> 不能离线引用，所以静态解析源码：
/// 每个 <c>ConfigPanel.Bind</c> 的键都要有英文显示名；中文版有说明的，英文版也必须有；英文里不许出现 CJK；
/// 英文说明里引用的其它设置名必须真实存在（改名之后文案里的交叉引用最容易悄悄失效）。
/// 英文措辞本身好不好读，只能靠人看，这里只保证“不漏、不串、不乱引用”。
/// </summary>
internal static class ConfigTextFacts
{
    private static readonly Regex QuotePattern = new(@"(?<![A-Za-z])'([^']+)'(?![A-Za-z])", RegexOptions.Compiled);

    /// <summary>说明文字里单引号括起来的、不是“设置名”的固定词（成就名、取值名、示例文字）。</summary>
    private static readonly HashSet<string> AllowedQuotedPhrases = new(StringComparer.Ordinal)
    {
        "low damage", "full strength", "heavy",
        "Kill", "Headshot", "Elite Kill", "Gunner", "Marauder", "Assist + damage",
        "Melee / Explosive / Sniper…", "Melee / Explosive / Sniper", "Killa / Glukhar / Shturman…",
        "white at 25% opacity", "main row + name + tags", "Headshot + reaching this distance",
        "Протасий Могила",
    };

    internal static void Run()
    {
        var keys = ConfigSourceModel.Load();

        Assert.Suite("英文面板: 每个配置键都有英文条目");
        {
            var missing = new List<string>();
            var stale = new List<string>();
            var tableNames = new HashSet<string>(ConfigTextEn.Names, StringComparer.Ordinal);
            foreach (var name in keys.Keys) if (!tableNames.Contains(name)) missing.Add(name);
            foreach (var name in tableNames) if (!keys.ContainsKey(name)) stale.Add(name);
            missing.Sort(StringComparer.Ordinal);
            stale.Sort(StringComparer.Ordinal);

            Assert.Equal(0, missing.Count, "每个 Bind 的键在 ConfigTextEn 里都有英文显示名。缺失: " + (missing.Count == 0 ? "(none)" : string.Join(", ", missing)));
            Assert.Equal(0, stale.Count, "ConfigTextEn 里没有指向已不存在的键。多余: " + (stale.Count == 0 ? "(none)" : string.Join(", ", stale)));
        }

        Assert.Suite("英文面板: 英文里没有 CJK、没有空名字");
        {
            var problems = new List<string>();
            foreach (var name in keys.Keys)
            {
                if (!ConfigTextEn.TryGet(name, out string enName, out string? enDesc)) continue;
                if (string.IsNullOrWhiteSpace(enName)) problems.Add(name + ": 英文显示名为空");
                if (Loc.ContainsCjk(enName)) problems.Add($"{name}: 英文显示名含 CJK: {enName}");
                if (enDesc != null && string.IsNullOrWhiteSpace(enDesc)) problems.Add(name + ": 英文说明是空白串（想表示“没有说明”请用 null）");
                if (Loc.ContainsCjk(enDesc)) problems.Add($"{name}: 英文说明含 CJK");
            }
            Assert.Equal(0, problems.Count, "英文文案干净" + (problems.Count > 0 ? "：\n    " + string.Join("\n    ", problems) : ""));
        }

        Assert.Suite("英文面板: 中文有说明的，英文也必须有");
        {
            var hintSpecific = new HashSet<string>(ConfigSourceModel.HintSpecificNames(), StringComparer.Ordinal);
            Assert.Greater(hintSpecific.Count, 20, "解析出 20 个以上的专用兜底句（防止正则失效后空跑）");

            var problems = new List<string>();
            foreach (var info in keys.Values)
            {
                if (!ConfigTextEn.TryGet(info.Name, out _, out string? enDesc)) continue;
                bool zhHasText = info.HasExplicitDescription || hintSpecific.Contains(info.Name);
                if (zhHasText && enDesc == null) problems.Add(info.Name);
            }
            Assert.Equal(0, problems.Count, "中文有显式说明或专用兜底句的条目都有英文说明" + (problems.Count > 0 ? "。缺: " + string.Join(", ", problems) : ""));
        }

        Assert.Suite("英文面板: 分类名");
        {
            var zhCategories = new HashSet<string>(StringComparer.Ordinal);
            foreach (var info in keys.Values) zhCategories.Add(info.Category);

            var problems = new List<string>();
            foreach (var zh in zhCategories)
            {
                string en = ConfigTextEn.Category(zh);
                if (en == zh || Loc.ContainsCjk(en)) problems.Add($"{zh} → {en}（没翻译）");
                // 两边的 "NN." 数字前缀一致：面板按它在视觉上排序，翻译不能改了顺序
                else if (zh.Length < 3 || !en.StartsWith(zh.Substring(0, 3), StringComparison.Ordinal)) problems.Add($"{zh} → {en}（编号前缀不一致）");
            }
            Assert.Equal(0, problems.Count, "每个面板分类都有英文名，且编号前缀不变" + (problems.Count > 0 ? "：" + string.Join("; ", problems) : ""));
            Assert.Equal(13, zhCategories.Count, "共 13 个面板分类（新增分类时同步 ConfigTextEn.Categories）");

            // 英文显示名在同一个分类里不重名（面板里两个同名的条目没法区分）
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            var dupes = new List<string>();
            foreach (var info in keys.Values)
            {
                string id = ConfigTextEn.Category(info.Category) + "|" + ConfigTextEn.Name(info.Name);
                if (seen.TryGetValue(id, out var other)) dupes.Add($"{info.Name} 与 {other}");
                else seen[id] = info.Name;
            }
            Assert.Equal(0, dupes.Count, "同一分类里英文显示名不重复" + (dupes.Count > 0 ? "：" + string.Join("; ", dupes) : ""));
        }

        Assert.Suite("英文面板: 说明里引用的设置名都真实存在");
        {
            // 允许的“设置名”形式：完整英文名；去掉 (s) / (m) / (px) 单位后缀；冒号后的后半段（Hit Direction: Max Dots per Cluster → Max Dots per Cluster）；
            // 'X = Y' 形式里的 X；分类名；固定词。
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in keys.Keys)
            {
                string en = ConfigTextEn.Name(name);
                known.Add(en);
                known.Add(StripUnit(en));
                int colon = en.IndexOf(": ", StringComparison.Ordinal);
                if (colon >= 0)
                {
                    known.Add(en.Substring(colon + 2));
                    known.Add(StripUnit(en.Substring(colon + 2)));
                }
            }
            foreach (var zh in ConfigTextEn.CategoryKeys) known.Add(ConfigTextEn.Category(zh));

            var bad = new List<string>();
            foreach (var name in keys.Keys)
            {
                ConfigTextEn.TryGet(name, out _, out string? desc);
                if (desc == null) continue;
                foreach (Match m in QuotePattern.Matches(desc))
                {
                    string phrase = m.Groups[1].Value;
                    string head = phrase.Contains(" = ", StringComparison.Ordinal) ? phrase.Substring(0, phrase.IndexOf(" = ", StringComparison.Ordinal)) : phrase;
                    if (AllowedQuotedPhrases.Contains(phrase) || known.Contains(phrase) || known.Contains(head)) continue;
                    bad.Add($"{name}: '{phrase}'");
                }
            }
            Assert.Equal(0, bad.Count, "英文说明里单引号括起来的设置名都能在面板上找到" + (bad.Count > 0 ? "：\n    " + string.Join("\n    ", bad) : ""));
        }

        Assert.Suite("英文面板: 完整说明的拼装（与中文版 Bind 的附加句同条件）");
        {
            string colorFull = ConfigTextEn.Compose("HitMarkerColor", "1. 命中提示", isColor: true, hasNumericRange: false);
            Assert.True(colorFull.EndsWith(ConfigTextEn.ColorNote, StringComparison.Ordinal), "颜色项追加格式说明");
            Assert.False(colorFull.Contains(ConfigTextEn.RangeNote, StringComparison.Ordinal), "颜色项没有范围句");

            string killCard = ConfigTextEn.Compose("SkullSize", "3. 击杀信息", isColor: false, hasNumericRange: true);
            Assert.True(killCard.Contains(ConfigTextEn.KillCardNote, StringComparison.Ordinal), "击杀信息小节追加“下一次击杀生效”");
            Assert.True(killCard.EndsWith(ConfigTextEn.RangeNote, StringComparison.Ordinal), "数值范围句在最后");

            string cardColor = ConfigTextEn.Compose("ColorTagKill", "3. 击杀信息", isColor: true, hasNumericRange: false);
            Assert.True(cardColor.IndexOf(ConfigTextEn.ColorNote, StringComparison.Ordinal) < cardColor.IndexOf(ConfigTextEn.KillCardNote, StringComparison.Ordinal),
                "附加句顺序：颜色格式 → 下一次击杀生效（与中文版一致）");

            string sound = ConfigTextEn.Compose("SoundFileKill", "4. 音频", isColor: false, hasNumericRange: false);
            Assert.True(sound.Contains("kill.ogg", StringComparison.Ordinal), "音效路径项带文件名说明");
            Assert.True(sound.EndsWith(ConfigTextEn.SoundFileNote, StringComparison.Ordinal), "音效路径项追加“下一局重新加载”");

            string list = ConfigTextEn.Compose("Language", "5. 界面", isColor: false, hasNumericRange: false);
            Assert.False(list.Contains(ConfigTextEn.RangeNote, StringComparison.Ordinal), "候选值列表没有“超出范围”句");

            // 通用兜底句：中文版拼中文键名，英文版拼英文显示名
            Assert.Equal("Color: BEAR, used to tint the corresponding element.", ConfigTextEn.Generic("ColorBEAR", "Color: BEAR"), "颜色类兜底");
            Assert.Equal("Crit Radius, in canvas pixels.", ConfigTextEn.Generic("RingCritRadius", "Crit Radius"), "半径类兜底");
            Assert.Equal("Explosion Thickness, in canvas pixels.", ConfigTextEn.Generic("RingExplosionThickness", "Explosion Thickness"), "粗细类兜底");
            Assert.Equal("Score Font Size, in canvas pixels; the overall size also scales with UI Scale.",
                ConfigTextEn.Generic("FontSizeScore", "Score Font Size"), "字号类兜底");
            Assert.True(ConfigTextEn.Generic("SoundKillVolume", "x").StartsWith("Volume range", StringComparison.Ordinal), "音量类兜底");

            // 全量：每个键拼出来的完整英文说明都不含 CJK、不为空
            var bad = new List<string>();
            foreach (var info in keys.Values)
            {
                bool isColor = info.DefaultLiteral != null && info.DefaultLiteral.StartsWith("#", StringComparison.Ordinal);
                bool numeric = info.Min != null && !info.IsValueList;
                string full = ConfigTextEn.Compose(info.Name, info.Section, isColor, numeric);
                if (string.IsNullOrWhiteSpace(full) || Loc.ContainsCjk(full)) bad.Add(info.Name);
            }
            Assert.Equal(0, bad.Count, "每个键的完整英文说明都可显示" + (bad.Count > 0 ? "：" + string.Join(", ", bad) : ""));
        }
    }

    private static string StripUnit(string s)
    {
        foreach (var suffix in new[] { " (s)", " (m)", " (px)" })
            if (s.EndsWith(suffix, StringComparison.Ordinal)) return s.Substring(0, s.Length - suffix.Length);
        return s;
    }
}
