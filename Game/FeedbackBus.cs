using System.Collections.Generic;

namespace AyaHitmarker.Game;

/// <summary>
/// 事件 → 主线程的搬运工。
/// IL2CPP 事件的回调线程不保证是主线程，而 Unity API 只能在主线程碰，
/// 所以 handler 只做「冻结 + 入队」，主线程在 Update 里 Drain 后再驱动 UI。
/// </summary>
internal static class FeedbackBus
{
    private static readonly Queue<HitRecord> _hits = new Queue<HitRecord>(64);
    private static readonly Queue<KillRecord> _kills = new Queue<KillRecord>(16);
    private static readonly object _lock = new object();

    private static int _droppedHits;
    private static int _droppedKills;

    internal static void EnqueueHit(in HitRecord r)
    {
        lock (_lock)
        {
            if (_hits.Count > 256) { _hits.Dequeue(); _droppedHits++; }
            _hits.Enqueue(r);
        }
    }

    internal static void EnqueueKill(in KillRecord r)
    {
        lock (_lock)
        {
            if (_kills.Count > 64) { _kills.Dequeue(); _droppedKills++; }
            _kills.Enqueue(r);
        }
    }

    internal static void Drain(List<HitRecord> hitsInto, List<KillRecord> killsInto)
    {
        lock (_lock)
        {
            while (_hits.Count > 0) hitsInto.Add(_hits.Dequeue());
            while (_kills.Count > 0) killsInto.Add(_kills.Dequeue());
        }
    }

    internal static void Clear()
    {
        lock (_lock)
        {
            _hits.Clear();
            _kills.Clear();
        }
    }

    internal static int DroppedHits => _droppedHits;
    internal static int DroppedKills => _droppedKills;
}
