using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// 配置项的「面板展示映射」契约。
///
/// <see cref="AyaHitmarker.AyaConfig"/> 里每个 <c>ConfigPanel.Bind(cfg, nameof(X), …)</c> 都要求
/// <c>ConfigPanel.For</c> 里有 <c>"X" =&gt; new(...)</c> 的分支；漏一个，插件会在加载阶段抛
/// <c>ArgumentException: Missing configuration presentation: X</c> **整个加载失败**
/// （2026-10-20 真实踩过：0.6 新增的 11 个键漏了面板映射，进游戏插件没起来）。
///
/// 这个测试直接读两个源文件做静态对账，不加载 Unity / BepInEx，用离线测试就能挡住同类问题。
/// </summary>
internal static class ConfigPresentationFacts
{
    private static readonly Regex BindPattern = new(
        @"ConfigPanel\.Bind\(cfg,\s*nameof\(([A-Za-z0-9_]+)\)", RegexOptions.Compiled);

    private static readonly Regex PresentationPattern = new(
        @"""([A-Za-z0-9_]+)""\s*=>", RegexOptions.Compiled);

    internal static void Run()
    {
        Assert.Suite("配置：面板展示映射");

        string root = FindRepoRoot();
        string configSource = File.ReadAllText(Path.Combine(root, "AyaConfig.cs"));
        string panelSource = File.ReadAllText(Path.Combine(root, "ConfigPanel.cs"));

        var bound = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in BindPattern.Matches(configSource)) bound.Add(m.Groups[1].Value);

        var presented = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in PresentationPattern.Matches(panelSource)) presented.Add(m.Groups[1].Value);

        Assert.Greater(bound.Count, 100, "AyaConfig 里应能解析出 100 个以上的面板绑定（防止正则失效后空跑）");

        var missing = new List<string>();
        foreach (var name in bound)
        {
            if (!presented.Contains(name)) missing.Add(name);
        }
        missing.Sort(StringComparer.Ordinal);

        Assert.Equal(0, missing.Count,
            "每个 ConfigPanel.Bind 的键都要在 ConfigPanel.For 里有分类映射；漏掉会导致插件加载失败。缺失: " +
            (missing.Count == 0 ? "(none)" : string.Join(", ", missing)));
    }

    /// <summary>从测试程序集所在目录往上找仓库根（含 AyaConfig.cs 与 ConfigPanel.cs 的目录）。</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AyaConfig.cs"))
                && File.Exists(Path.Combine(dir.FullName, "ConfigPanel.cs")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("找不到仓库根（AyaConfig.cs / ConfigPanel.cs）: " + AppContext.BaseDirectory);
    }
}
