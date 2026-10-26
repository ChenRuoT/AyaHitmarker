using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// 从 <c>AyaConfig.cs</c> / <c>ConfigPanel.cs</c> 源码里静态读出每个配置键的类型、默认值、范围和面板分类。
///
/// 为什么要读源码：<c>AyaConfig</c> 依赖 BepInEx / Unity，离线测试工程不能引用（见 <c>ConfigPresentationFacts</c>）。
/// 预设、英文面板这类“和全部配置键对账”的契约，只能走静态解析。
/// 解析有边界：只认字面量默认值（<c>"#FFFFFFFF"</c> / <c>true</c> / <c>190f</c>）；
/// <c>hd.WidthLow</c> 这种符号默认值读不出来，<see cref="ConfigKeyInfo.DefaultLiteral"/> 为 null，调用方自己处理。
/// </summary>
internal sealed class ConfigKeyInfo
{
    internal string Name = string.Empty;
    internal string CsType = string.Empty;
    internal string Section = string.Empty;
    internal string Key = string.Empty;
    internal string Category = string.Empty;
    internal bool Advanced;

    /// <summary>字面量默认值的文本（字符串去掉引号，数字去掉 f 后缀）；符号默认值为 null。</summary>
    internal string? DefaultLiteral;

    internal double? Min;
    internal double? Max;

    /// <summary>描述里的 <c>AcceptableValueList</c>（比如语言）。</summary>
    internal bool IsValueList;

    /// <summary>Bind 时给了非空的显式说明（含 <c>SoundHint(...)</c>）。没给的走 <c>ConfigPanel.Hint</c> 兜底。</summary>
    internal bool HasExplicitDescription;

    /// <summary>整条 Bind 语句的原文（调试 / 个别断言用）。</summary>
    internal string Statement = string.Empty;
}

internal static class ConfigSourceModel
{
    private static Dictionary<string, ConfigKeyInfo>? _cache;
    private static string? _root;

    internal static string RepoRoot
    {
        get
        {
            if (_root != null) return _root;
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "AyaConfig.cs"))
                    && File.Exists(Path.Combine(dir.FullName, "ConfigPanel.cs")))
                {
                    return _root = dir.FullName;
                }
                dir = dir.Parent;
            }
            throw new InvalidOperationException("找不到仓库根（AyaConfig.cs / ConfigPanel.cs）: " + AppContext.BaseDirectory);
        }
    }

    internal static string ReadRepoFile(string relative) => File.ReadAllText(Path.Combine(RepoRoot, relative));

    internal static IReadOnlyDictionary<string, ConfigKeyInfo> Load()
    {
        if (_cache != null) return _cache;

        string cfg = ReadRepoFile("AyaConfig.cs");
        string panel = ReadRepoFile("ConfigPanel.cs");

        // 面板分类：  "Name" => new("30. 击杀卡片 · 内容", 988, false),
        var categories = new Dictionary<string, (string Category, bool Advanced)>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(panel, @"""(\w+)""\s*=>\s*new\(""([^""]+)"",\s*(-?\d+),\s*(true|false)\)"))
            categories[m.Groups[1].Value] = (m.Groups[2].Value, m.Groups[4].Value == "true");

        // 属性声明：  internal ConfigEntry<float> Name { get; }
        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(cfg, @"ConfigEntry<([\w\.<>]+)>\s+(\w+)\s*\{\s*get;\s*\}"))
            types[m.Groups[2].Value] = m.Groups[1].Value;

        var result = new Dictionary<string, ConfigKeyInfo>(StringComparer.Ordinal);
        int pos = 0;
        const string marker = "ConfigPanel.Bind(cfg,";
        while ((pos = cfg.IndexOf(marker, pos, StringComparison.Ordinal)) >= 0)
        {
            int open = pos + "ConfigPanel.Bind".Length;
            var args = SplitArgs(cfg, open, out int end);
            pos = end + 1;
            if (args.Count < 5) continue;

            var nameMatch = Regex.Match(args[1], @"nameof\((\w+)\)");
            if (!nameMatch.Success) continue;

            string name = nameMatch.Groups[1].Value;
            var info = new ConfigKeyInfo
            {
                Name = name,
                CsType = types.TryGetValue(name, out var t) ? t : "?",
                Section = Unquote(args[2]),
                Key = Unquote(args[3]),
                Statement = cfg.Substring(open - "ConfigPanel.Bind".Length, end - open + "ConfigPanel.Bind".Length + 1),
            };
            if (categories.TryGetValue(name, out var cat))
            {
                info.Category = cat.Category;
                info.Advanced = cat.Advanced;
            }

            info.DefaultLiteral = LiteralOf(args[4]);

            if (args.Count > 5)
            {
                string desc = args[5];
                info.IsValueList = desc.Contains("AcceptableValueList<", StringComparison.Ordinal);

                var dm = Regex.Match(desc, @"new ConfigDescription\(");
                if (dm.Success)
                {
                    var sub = SplitArgs(desc, dm.Index + dm.Length - 1, out _);
                    string first = sub.Count > 0 ? sub[0] : string.Empty;
                    info.HasExplicitDescription = first.Contains("SoundHint(", StringComparison.Ordinal)
                                                  || Regex.IsMatch(first, "\"[^\"]+\"");
                }

                var r = Regex.Match(desc, @"new object\[\]\s*\{\s*(-?[0-9.]+)f?\s*,\s*(-?[0-9.]+)f?\s*\}");
                if (r.Success)
                {
                    info.Min = double.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture);
                    info.Max = double.Parse(r.Groups[2].Value, CultureInfo.InvariantCulture);
                }
                else if (desc.Contains("unitRange", StringComparison.Ordinal))
                {
                    info.Min = 0;
                    info.Max = 1;
                }
            }

            // ConfigPanel.Bind 里按名字补的范围（与 ConfigPanel.cs 的实现保持一致）
            if (info.Min == null && info.CsType == "float" && name.StartsWith("Ring", StringComparison.Ordinal))
            {
                if (name.Contains("Radius", StringComparison.Ordinal)) { info.Min = 1; info.Max = 256; }
                else if (name.Contains("Thickness", StringComparison.Ordinal)) { info.Min = 0.1; info.Max = 32; }
                else { info.Min = 0; info.Max = 5; }
            }
            if (name == "UiSortingOrder") { info.Min = -32768; info.Max = 32767; }

            result[name] = info;
        }

        return _cache = result;
    }

    /// <summary>
    /// <c>ConfigPanel.Hint</c> 里有<b>专用</b>兜底句的属性名（不含最后几条按名字模式匹配的通用句）。
    /// 这些条目在中文版里没写显式说明，面板上显示的是专用句，所以英文版也必须有专用说明。
    /// </summary>
    internal static IReadOnlyCollection<string> HintSpecificNames()
    {
        string panel = ReadRepoFile("ConfigPanel.cs");
        int start = panel.IndexOf("private static string Hint(", StringComparison.Ordinal);
        int end = panel.IndexOf("_ when", start, StringComparison.Ordinal);
        if (start < 0 || end < 0) throw new InvalidOperationException("ConfigPanel.cs 里找不到 Hint 函数");

        var names = new List<string>();
        foreach (Match m in Regex.Matches(panel.Substring(start, end - start), @"""(\w+)""\s*(?:=>|or\b)"))
            names.Add(m.Groups[1].Value);
        return names;
    }

    private static string Unquote(string s)
    {
        s = s.Trim();
        return s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"' ? s.Substring(1, s.Length - 2) : s;
    }

    /// <summary>字面量 → 文本；认不出（符号 / 表达式）返回 null。</summary>
    private static string? LiteralOf(string expr)
    {
        expr = expr.Trim();
        if (expr.Length >= 2 && expr[0] == '"' && expr[expr.Length - 1] == '"') return expr.Substring(1, expr.Length - 2);
        if (expr == "true" || expr == "false") return expr;
        var num = Regex.Match(expr, @"^(-?[0-9]+(?:\.[0-9]+)?)f?$");
        return num.Success ? num.Groups[1].Value : null;
    }

    /// <summary>
    /// 按括号深度切参数：<c>src[openParen]</c> 必须是 '('。字符串字面量（含转义）里的括号 / 逗号不算。
    /// 返回时 <paramref name="end"/> 指向匹配的 ')'。
    /// </summary>
    private static List<string> SplitArgs(string src, int openParen, out int end)
    {
        var args = new List<string>();
        int depth = 0;
        int start = openParen + 1;
        bool inString = false;

        for (int i = openParen; i < src.Length; i++)
        {
            char c = src[i];
            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '(':
                case '{':
                case '[':
                    depth++;
                    break;
                case ')':
                case '}':
                case ']':
                    depth--;
                    if (depth == 0)
                    {
                        args.Add(src.Substring(start, i - start));
                        end = i;
                        return args;
                    }
                    break;
                case ',':
                    if (depth == 1)
                    {
                        args.Add(src.Substring(start, i - start));
                        start = i + 1;
                    }
                    break;
            }
        }

        throw new InvalidOperationException("AyaConfig.cs 里有一条 ConfigPanel.Bind 括号不匹配");
    }
}
