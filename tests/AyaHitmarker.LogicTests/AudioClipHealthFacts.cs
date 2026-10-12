using AyaHitmarker.Assets;

namespace AyaHitmarker.LogicTests;

internal static class AudioClipHealthFacts
{
    internal static void Run()
    {
        Assert.Suite("Audio clip data integrity");
        Assert.True(AudioClipHealth.IsUsable(25754, 2, 44100, 0.583f, true), "日志中的正常命中音效可播放");
        Assert.False(AudioClipHealth.IsUsable(0, 0, 0, 0f, true), "Loaded 零采样空壳必须拒绝");
        Assert.False(AudioClipHealth.IsUsable(0, 2, 44100, 0.583f, true), "零采样必须拒绝");
        Assert.False(AudioClipHealth.IsUsable(25754, 0, 44100, 0.583f, true), "零声道必须拒绝");
        Assert.False(AudioClipHealth.IsUsable(25754, 2, 0, 0.583f, true), "零频率必须拒绝");
        Assert.False(AudioClipHealth.IsUsable(25754, 2, 44100, 0f, true), "零长度必须拒绝");
        Assert.False(AudioClipHealth.IsUsable(25754, 2, 44100, float.NaN, true), "NaN 长度必须拒绝");
        Assert.False(AudioClipHealth.IsUsable(25754, 2, 44100, float.PositiveInfinity, true), "无限长度必须拒绝");
        Assert.False(AudioClipHealth.IsUsable(25754, 2, 44100, 0.583f, false), "数据未 Loaded 必须拒绝");
    }
}
