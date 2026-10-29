using AyaHitmarker.Game;

namespace AyaHitmarker.LogicTests;

internal static class AudioMetricsFacts
{
    internal static void Run()
    {
        Assert.Suite("AudioMetrics");
        var m = new AudioMetrics();
        m.NoteSilent(AudioMetrics.SilentReason.MissingClip);
        Assert.Equal(0, m.SilentPlays, "menu playback excluded");
        m.BeginRaid();
        m.NoteSilent(AudioMetrics.SilentReason.MissingClip);
        m.NoteSilent(AudioMetrics.SilentReason.Disabled);
        m.NoteSilent(AudioMetrics.SilentReason.ZeroVolume);
        Assert.Equal(3, m.SilentPlays, "each skipped request counted");
        Assert.Equal(1, m.MissingClips, "missing clip distinguished from intentional silence");
        Assert.Equal(1, m.DisabledPlays, "disabled count");
        Assert.Equal(1, m.ZeroVolumePlays, "zero volume count");
        m.NoteInvalid(0, 100);
        m.NoteInvalid(0, 150);
        Assert.Near(200, m.NoteRecovered(0, 300)!.Value, 0.001, "retry preserves first invalidation time");
        Assert.Equal(1, m.Reloads, "one restored clip");
        Assert.Equal(null, m.NoteRecovered(0, 400), "no double count");
        m.NoteInvalid(1, 500);
        m.NoteRecovered(1, 600);
        Assert.Near(200, m.LongestRecoveryMs, 0.001, "longest recovery retained");
        m.NoteInvalid(2, 700);
        Assert.Equal(1, m.PendingRecoveries, "unrecovered clips remain visible");
        m.EndRaid();
        m.NoteSilent(AudioMetrics.SilentReason.Other);
        Assert.Equal(3, m.SilentPlays, "post-raid menu excluded");
        m.BeginRaid();
        Assert.Equal(0, m.SilentPlays, "new raid clears counters");
        Assert.Equal(0, m.PendingRecoveries, "new raid clears pending windows");
        Assert.Equal(null, m.NoteRecovered(2, 900), "old recovery cannot contaminate next raid");
    }
}
