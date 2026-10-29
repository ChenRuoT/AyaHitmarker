using System.Diagnostics;
using System.Globalization;

namespace AyaHitmarker.Game;

internal sealed class DiagClock
{
    private readonly long _start = Stopwatch.GetTimestamp();
    internal string Stamp() => "@t=" + ((Stopwatch.GetTimestamp() - _start) / (double)Stopwatch.Frequency).ToString("F1", CultureInfo.InvariantCulture);
}
