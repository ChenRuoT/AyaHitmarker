using System;
using System.Collections.Generic;

namespace AyaHitmarker.Game;

/// <summary>
/// 0.6 c 助攻判定（纯逻辑，不引用 Unity / EFT）：记住「我最近打过的目标」，
/// 目标被别人打死时结算一次助攻（伤害累计、爆头取或）。
///
/// 只在主线程使用（<c>AyaBehaviour.DrainFeedback</c>）—— 命中与击杀事件都先排队到主线程，
/// 所以这里不需要任何锁；状态本身也只是几十个目标的小表。
/// </summary>
internal sealed class AssistTracker
{
    private sealed class Entry
    {
        internal float LastHitAt;
        internal float Damage;
        internal bool Headshot;
    }

    /// <summary>表超过这个大小就做一次全表清理（正常一局也就几十个目标）。</summary>
    private const int PruneThreshold = 96;

    /// <summary>旧条目最多保留这么久（防止长时间战局里积累；窗口本身通常远小于它）。</summary>
    private const float StaleAfter = 300f;

    private readonly Dictionary<string, Entry> _recent = new Dictionary<string, Entry>(StringComparer.Ordinal);

    internal int Count => _recent.Count;

    /// <summary>记一次我对目标的命中（伤害累计，任何一次爆头都算爆头）。key 为空时忽略。</summary>
    internal void NoteHit(string? key, float now, float damage, bool headshot)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (damage < 0f) damage = 0f;

        if (_recent.TryGetValue(key!, out var entry))
        {
            entry.LastHitAt = now;
            entry.Damage += damage;
            entry.Headshot |= headshot;
            return;
        }

        _recent[key!] = new Entry { LastHitAt = now, Damage = damage, Headshot = headshot };
        if (_recent.Count > PruneThreshold) Prune(now);
    }

    /// <summary>
    /// 目标被别人打死时结算：窗口内我打过 → 返回 true 并给出累计伤害 / 是否爆头，同时移除该目标
    /// （一次死亡只结算一次；之后再打同一个 profileId 不会重复助攻）。
    /// </summary>
    internal bool TryTakeAssist(string? key, float now, float window, out float damage, out bool headshot)
    {
        damage = 0f;
        headshot = false;
        if (string.IsNullOrEmpty(key)) return false;
        if (window <= 0f) return false;
        if (!_recent.Remove(key!, out var entry)) return false;

        if (now - entry.LastHitAt > window) return false;

        damage = entry.Damage;
        headshot = entry.Headshot;
        return true;
    }

    /// <summary>战局开始 / 结束、UI 重置时清空（跨局不残留）。</summary>
    internal void Reset() => _recent.Clear();

    private void Prune(float now)
    {
        List<string>? stale = null;
        foreach (var pair in _recent)
        {
            if (now - pair.Value.LastHitAt <= StaleAfter) continue;
            stale ??= new List<string>();
            stale.Add(pair.Key);
        }
        if (stale == null) return;
        for (int i = 0; i < stale.Count; i++) _recent.Remove(stale[i]);
    }
}
