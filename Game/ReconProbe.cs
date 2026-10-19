using System;
using System.Text;
using System.Threading;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.Counters;

namespace AyaHitmarker.Game;

/// <summary>
/// 探查局探针（路线图「探查局」S1–S3）。**只在 `0. 调试 → 启用诊断日志` 打开时输出**，
/// 不参与任何玩法判定，也不产生 UI / 音效。
///
/// <para>
/// 线程纪律（AGENTS.md §3.4）：
/// 命中 / 击杀回调**不保证在主线程** → <see cref="LogHit"/> / <see cref="LogKill"/> 只读 interop 值、
/// 拼字符串、写日志；<c>SessionCounters</c> 是游戏对象，只在主线程的 <see cref="Poll"/> 里读。
/// </para>
///
/// <para>
/// 要回答的问题：
/// S1 命中时能否稳定拿到受害者 <c>ProfileId</c>；
/// S2 我方被打时 <c>OnBeingHit</c> 是否触发、<c>Direction</c> / <c>MasterOrigin</c> 是否可用；
/// S3 战局中客户端能否读到真实击杀经验（<c>ProfileStats.SessionCounters</c> / <c>TotalSessionExperience</c>）。
/// </para>
/// </summary>
internal static class ReconProbe
{
    private static int _rawHits;
    private static int _rawKills;

    /// <summary>OnBeingHit 回调总次数（含被过滤的），用来判断「事件到底有没有触发」。</summary>
    internal static int RawHitEvents => Volatile.Read(ref _rawHits);

    /// <summary>OnPlayerDeadStatic 回调总次数（含别人的击杀）。</summary>
    internal static int RawKillEvents => Volatile.Read(ref _rawKills);

    internal static void NoteHitEvent() => Interlocked.Increment(ref _rawHits);

    internal static void NoteKillEvent() => Interlocked.Increment(ref _rawKills);

    // ------------------------------------------------------------------ S1 / S2：事件线程

    /// <summary>
    /// 在现有过滤（鞭尸 / 非我方击杀）**之前**落一行原始事件，回答 S1 / S2。
    /// 只对「受害者是我」或「攻击者是我」的事件调用，避免 bot 混战时刷屏。
    /// </summary>
    internal static void LogHit(DamageInfo info, Player? victim, IPlayer? aggressor, bool victimIsLocal, bool aggressorIsLocal)
    {
        try
        {
            Plugin.Logger.LogInfo(
                $"[recon-hit] victimLocal={victimIsLocal} attackerLocal={aggressorIsLocal} " +
                $"victim={Nick(victim)}({Id(victim)}) attacker={Nick(aggressor)}({Id(aggressor)}) " +
                $"body={info.DidBodyDamage:0.##} armor={info.DidArmorDamage:0.##} raw={info.Damage:0.##} " +
                $"type={info.DamageType} collider={info.BodyPartColliderType} penetrated={info.Penetrated} " +
                $"dir={Vec(info.Direction)} origin={Vec(info.MasterOrigin)} normal={Vec(info.HitNormal)}");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[recon-hit] 记录失败: {ex.Message}");
        }
    }

    /// <summary>击杀原始字段：受害者的 <c>ProfileId</c> 与本地/远程归属（0.6 b/c 需要跨命中与击杀对上同一个人）。</summary>
    internal static void LogKill(Player? dead, IPlayer? killer, bool victimIsLocal, bool killerIsLocal)
    {
        try
        {
            Plugin.Logger.LogInfo(
                $"[recon-kill] victimLocal={victimIsLocal} killerLocal={killerIsLocal} " +
                $"victim={Nick(dead)}({Id(dead)}) killer={Nick(killer)}({Id(killer)})");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[recon-kill] 记录失败: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ S3：主线程

    private static readonly string[] TrackedCounters =
    {
        "Exp", "ExpKill", "ExpKillBase", "ExpKillBodyPartBonus", "ExpKillStreakBonus",
        "Kills", "HeadShots", "HitCount", "CauseBodyDamage", "CauseArmorDamage",
        "LongShots", "LongestShot",
    };

    private static readonly long[] LastValues = new long[TrackedCounters.Length];
    private static bool _hasLast;
    private static int _lastTotalExp;
    private static float _nextPoll;
    private static bool _warned;

    /// <summary>换局 / 卸载时清掉缓存，避免把上一局的计数当成这一局的基准。</summary>
    internal static void Reset()
    {
        _hasLast = false;
        _lastTotalExp = -1;
        _nextPoll = 0f;
        for (int i = 0; i < LastValues.Length; i++) LastValues[i] = 0;
    }

    /// <summary>
    /// 主线程每帧调用（内部 1Hz 限频）。读本局 <c>SessionCounters</c> 与 <c>TotalSessionExperience</c>，
    /// 值有变化才打一行 —— 用来判断客户端在战局中是否真的更新击杀经验（路线图 S3）。
    /// </summary>
    internal static void Poll(float now)
    {
        if (now < _nextPoll) return;
        _nextPoll = now + 1f;

        CountersCollection? counters;
        ProfileStats? stats;
        string profileId;
        try
        {
            var world = Singleton<GameWorld>.Instance;
            var profile = world?.MainPlayer?.Profile;
            if (profile == null)
            {
                _hasLast = false; // 不在战局：回到菜单后清掉基准
                return;
            }

            profileId = SafeId(profile);
            stats = profile.EftStats;
            counters = stats?.SessionCounters;
        }
        catch (Exception ex)
        {
            if (!_warned)
            {
                _warned = true;
                Plugin.Logger.LogWarning($"[recon-xp] 读取 SessionCounters 失败（本次会话只报一次）: {ex.Message}");
            }
            return;
        }

        if (counters == null || stats == null) return;

        int totalExp;
        try { totalExp = stats.TotalSessionExperience; }
        catch { totalExp = -1; }

        var values = new long[TrackedCounters.Length];
        for (int i = 0; i < TrackedCounters.Length; i++)
            values[i] = ReadLong(counters, TrackedCounters[i]);

        bool changed = !_hasLast || totalExp != _lastTotalExp;
        for (int i = 0; i < values.Length; i++)
            if (!_hasLast || values[i] != LastValues[i]) changed = true;
        if (!changed) return;

        var sb = new StringBuilder(512);
        sb.Append("[recon-xp] profile=").Append(profileId)
          .Append(" totalSessionExp=").Append(totalExp)
          .Append(" counters:");
        for (int i = 0; i < TrackedCounters.Length; i++)
        {
            sb.Append(' ').Append(TrackedCounters[i]).Append('=').Append(values[i]);
            if (_hasLast && values[i] != LastValues[i])
                sb.Append('(').Append(values[i] - LastValues[i] >= 0 ? "+" : "").Append(values[i] - LastValues[i]).Append(')');
        }

        Plugin.Logger.LogInfo(sb.ToString());

        _hasLast = true;
        _lastTotalExp = totalExp;
        Array.Copy(values, LastValues, values.Length);
    }

    /// <summary>
    /// 按计数器名读一个 long。原生 <c>GetLong(object[] enums)</c> 会把元素 <c>ToString()</c> 当标识符，
    /// 所以传字符串 <c>"ExpKill"</c> 与传 <c>CounterTag.ExpKill</c> 等价（枚举名就是标识符）。
    /// 读不到时返回 -1，便于和真实的 0 区分。
    /// </summary>
    private static long ReadLong(CountersCollection counters, string tag)
    {
        try
        {
            var args = new Il2CppSystem.Object[1];
            args[0] = tag; // string -> Il2CppSystem.Object 的隐式转换
            return counters.GetLong(args);
        }
        catch
        {
            return -1;
        }
    }

    // ------------------------------------------------------------------ 安全取值（事件线程）

    private static string Nick(Player? p)
    {
        try { return p?.Profile?.Info?.Nickname ?? "Unknown"; }
        catch { return "Unknown"; }
    }

    private static string Nick(IPlayer? p)
    {
        try { return p?.Profile?.Nickname ?? "Unknown"; }
        catch { return "Unknown"; }
    }

    private static string Id(Player? p)
    {
        try { return p?.ProfileId ?? "Unknown"; }
        catch { return "Unknown"; }
    }

    private static string Id(IPlayer? p)
    {
        try { return p?.ProfileId ?? "Unknown"; }
        catch { return "Unknown"; }
    }

    private static string SafeId(EFT.Profile profile)
    {
        try { return profile.Id ?? "Unknown"; }
        catch { return "Unknown"; }
    }

    private static string Vec(UnityEngine.Vector3 v) => $"({v.x:0.#},{v.y:0.#},{v.z:0.#})";
}
