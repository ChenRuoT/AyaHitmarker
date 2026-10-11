using System;
using System.Collections.Generic;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// 极简断言器：不引任何测试框架，直接 <c>dotnet run</c>，失败时退出码 1。
/// 这个仓库刻意不引 NUnit/xUnit —— 插件本身零依赖，测试也不该反过来把包管理搞复杂。
/// </summary>
internal static class Assert
{
    private static readonly List<string> Failures = new List<string>();
    private static int _passed;
    private static string _suite = string.Empty;

    internal static void Suite(string name)
    {
        _suite = name;
        Console.WriteLine();
        Console.WriteLine("== " + name + " ==");
    }

    internal static void True(bool condition, string what)
        => Check(condition, what, "true", "false");

    internal static void False(bool condition, string what)
        => Check(!condition, what, "false", "true");

    internal static void Equal(object? expected, object? actual, string what)
        => Check(Equals(expected, actual), what, Str(expected), Str(actual));

    internal static void NotEqual(object? unexpected, object? actual, string what)
        => Check(!Equals(unexpected, actual), what, "not " + Str(unexpected), Str(actual));

    internal static void Near(double expected, double actual, double tolerance, string what)
        => Check(Math.Abs(expected - actual) <= tolerance, what,
            expected.ToString("0.####"), actual.ToString("0.####"));

    internal static void Greater(double a, double b, string what)
        => Check(a > b, what, "> " + b.ToString("0.####"), a.ToString("0.####"));

    private static string Str(object? v) => v?.ToString() ?? "null";

    private static void Check(bool ok, string what, string expected, string actual)
    {
        if (ok)
        {
            _passed++;
            Console.WriteLine("  ok    " + what);
            return;
        }

        string msg = $"  FAIL  [{_suite}] {what} — expected {expected}, actual {actual}";
        Console.WriteLine(msg);
        Failures.Add(msg);
    }

    internal static int Summary()
    {
        Console.WriteLine();
        if (Failures.Count == 0)
        {
            Console.WriteLine($"ALL PASS ({_passed} checks)");
            return 0;
        }

        Console.WriteLine($"{Failures.Count} FAILED, {_passed} passed");
        foreach (string f in Failures) Console.WriteLine(f);
        return 1;
    }
}
