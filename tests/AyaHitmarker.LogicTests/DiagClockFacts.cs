using System;
using System.Globalization;
using AyaHitmarker.Game;

namespace AyaHitmarker.LogicTests;

internal static class DiagClockFacts
{
    internal static void Run()
    {
        Assert.Suite("DiagClock");
        var clock = new DiagClock();
        string first = clock.Stamp();
        string second = clock.Stamp();
        Assert.True(first.StartsWith("@t=", StringComparison.Ordinal), "prefix");
        Assert.True(double.TryParse(first.Substring(3), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double a), "invariant format");
        Assert.True(double.TryParse(second.Substring(3), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double b) && b >= a, "monotonic");
    }
}
