using System.Collections.Generic;
using System.Diagnostics;

namespace AyaHitmarker.Game;

/// <summary>只记录已请求播放但被跳过的次数、实际发现失效到恢复的时间；不证明声音可听。</summary>
internal sealed class AudioMetrics
{
    private readonly Dictionary<int, double> _invalidSince = new();
    private bool _inRaid;
    internal int SilentPlays { get; private set; }
    internal int MissingClips { get; private set; }
    internal int DisabledPlays { get; private set; }
    internal int ZeroVolumePlays { get; private set; }
    internal int OtherFailures { get; private set; }
    internal int Reloads { get; private set; }
    internal double LongestRecoveryMs { get; private set; }
    internal int PendingRecoveries => _invalidSince.Count;
    internal static double NowMs => Stopwatch.GetTimestamp() * (1000d / Stopwatch.Frequency);

    internal enum SilentReason { MissingClip, Disabled, ZeroVolume, Other }
    internal void BeginRaid()
    {
        _inRaid = true;
        SilentPlays = MissingClips = DisabledPlays = ZeroVolumePlays = OtherFailures = Reloads = 0;
        LongestRecoveryMs = 0;
        _invalidSince.Clear();
    }
    internal void EndRaid() { _inRaid = false; _invalidSince.Clear(); }
    internal void NoteSilent(SilentReason reason)
    {
        if (!_inRaid) return;
        SilentPlays++;
        switch (reason)
        {
            case SilentReason.MissingClip: MissingClips++; break;
            case SilentReason.Disabled: DisabledPlays++; break;
            case SilentReason.ZeroVolume: ZeroVolumePlays++; break;
            default: OtherFailures++; break;
        }
    }
    internal void NoteInvalid(int slot, double nowMs)
    {
        if (_inRaid) _invalidSince.TryAdd(slot, nowMs);
    }
    internal double? NoteRecovered(int slot, double nowMs)
    {
        if (!_inRaid || !_invalidSince.Remove(slot, out double start)) return null;
        double elapsed = System.Math.Max(0, nowMs - start);
        Reloads++;
        LongestRecoveryMs = System.Math.Max(LongestRecoveryMs, elapsed);
        return elapsed;
    }
}
