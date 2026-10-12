namespace AyaHitmarker.Assets;

/// <summary>音效数据契约。不依赖 Unity，可复现日志中的 Loaded 零采样空壳。</summary>
internal static class AudioClipHealth
{
    internal static bool IsUsable(int samples, int channels, int frequency, float length, bool loaded)
        => loaded && samples > 0 && channels > 0 && frequency > 0
           && float.IsFinite(length) && length > 0f;
}
