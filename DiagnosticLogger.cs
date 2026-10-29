using BepInEx.Logging;
using AyaHitmarker.Game;

namespace AyaHitmarker;

internal sealed class DiagnosticLogger
{
    private readonly ManualLogSource _source;
    private readonly DiagClock _clock = new();
    internal DiagnosticLogger(ManualLogSource source) => _source = source;
    internal void LogInfo(object data) => _source.LogInfo($"{data} {_clock.Stamp()}");
    internal void LogWarning(object data) => _source.LogWarning($"{data} {_clock.Stamp()}");
    internal void LogError(object data) => _source.LogError($"{data} {_clock.Stamp()}");
    internal void LogDebug(object data) => _source.LogDebug($"{data} {_clock.Stamp()}");
}
