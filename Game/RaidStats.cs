using System;
using System.Collections.Generic;

namespace AyaHitmarker.Game;

/// <summary>一种成就在本局拿了几次。</summary>
internal readonly struct SummaryAchievement
{
    internal readonly KillAchievementKind Kind;
    internal readonly KillAchievementTier Tier;
    internal readonly string Title;
    internal readonly int Count;

    internal SummaryAchievement(KillAchievementKind kind, KillAchievementTier tier, string title, int count)
    {
        Kind = kind;
        Tier = tier;
        Title = title;
        Count = count;
    }
}

/// <summary>战局结束时冻结的一份统计（值类型 + 数组，不持有任何游戏对象）。</summary>
internal sealed class RaidSummary
{
    internal int Kills;
    internal int HeadshotKills;
    internal float LongestKill;
    internal int BestStreak;
    internal int Score;

    /// <summary>打在人身上的伤害（<b>身体</b>伤害）。护甲耐久伤害不是打在人身上的，单列、不并入（AGENTS.md §3.1）。</summary>
    internal float BodyDamage;
    internal float ArmorDamage;

    internal int Assists;
    internal int Deaths;
    internal int HitsTaken;
    internal float DamageTaken;

    /// <summary>按档次从高到低、再按次数从多到少排好。</summary>
    internal SummaryAchievement[] Achievements = Array.Empty<SummaryAchievement>();

    /// <summary>
    /// 这一局有没有“发生过什么”。藏身处也会触发 raid started / ended，那种局什么都没有，
    /// 不该弹出一张全是 0 的小结，更不能覆盖已经挂起的真实小结。
    /// </summary>
    internal bool HasActivity => Kills > 0 || BodyDamage > 0f || ArmorDamage > 0f || Assists > 0 || Deaths > 0 || HitsTaken > 0;
}

internal enum SummaryRowKind
{
    /// <summary>普通统计行：左标签 + 右数值。</summary>
    Stat = 0,

    /// <summary>成就行：左成就名 + 右「×N」。</summary>
    Achievement = 1,

    /// <summary>成就区与统计区之间的分隔。</summary>
    Divider = 2,
}

internal readonly struct SummaryRow
{
    internal readonly string Label;
    internal readonly string Value;
    internal readonly SummaryRowKind Kind;

    internal SummaryRow(string label, string value, SummaryRowKind kind = SummaryRowKind.Stat)
    {
        Label = label;
        Value = value;
        Kind = kind;
    }
}

/// <summary>
/// 战局小结的累计器（0.7.0）。纯托管、不引用 Unity / EFT，所以能离线测。
///
/// **口径**：
/// <list type="bullet">
///   <item>爆头只算**爆头击杀**（<c>OnPlayerDeadStatic</c> 的 <c>EBodyPart</c>）。命中级爆头靠碰撞体信息，
///         0.7.0 才修好（见 <see cref="HeadshotRules"/>），小结不依赖它；</item>
///   <item>伤害只算<b>身体</b>伤害；护甲耐久伤害单列，不并入；</item>
///   <item>得分是本插件自编的公式（基础 + 爆头 / 远程 / 复仇 / 多杀 / 首领加成），<b>不是游戏的经验值</b>，文案写“得分”；</item>
///   <item>成就按“种类 + 标题”计数；战局中途切换界面语言会让同一种成就按两个标题各算一次，罕见，不处理。</item>
/// </list>
/// </summary>
internal sealed class RaidStats
{
    private const int MaxAchievementKinds = 32;

    private int _kills;
    private int _headshotKills;
    private float _longestKill;
    private int _bestStreak;
    private int _score;
    private float _bodyDamage;
    private float _armorDamage;
    private int _assists;
    private int _deaths;
    private int _hitsTaken;
    private float _damageTaken;

    private readonly List<AchievementTally> _achievements = new List<AchievementTally>(8);

    private struct AchievementTally
    {
        internal KillAchievementKind Kind;
        internal KillAchievementTier Tier;
        internal string Title;
        internal int Count;
    }

    internal int Kills => _kills;

    internal void Reset()
    {
        _kills = 0;
        _headshotKills = 0;
        _longestKill = 0f;
        _bestStreak = 0;
        _score = 0;
        _bodyDamage = 0f;
        _armorDamage = 0f;
        _assists = 0;
        _deaths = 0;
        _hitsTaken = 0;
        _damageTaken = 0f;
        _achievements.Clear();
    }

    /// <summary>我打中了别人一次。</summary>
    internal void NoteHit(float bodyDamage, float armorDamage)
    {
        if (bodyDamage > 0f) _bodyDamage += bodyDamage;
        if (armorDamage > 0f) _armorDamage += armorDamage;
    }

    /// <summary>我被打中一次（<paramref name="damage"/> = 身体伤害）。</summary>
    internal void NoteTaken(float damage)
    {
        _hitsTaken++;
        if (damage > 0f) _damageTaken += damage;
    }

    /// <summary>我击杀了一个人。</summary>
    internal void NoteKill(in KillAnalysis analysis, bool headshot, float distance)
    {
        _kills++;
        if (headshot) _headshotKills++;
        if (distance > _longestKill) _longestKill = distance;
        if (analysis.Streak > _bestStreak) _bestStreak = analysis.Streak;
        _score += analysis.Score;

        var ribbon = analysis.Ribbon;
        if (ribbon.Kind != KillAchievementKind.None && ribbon.HasTitle) NoteAchievement(ribbon);
    }

    internal void NoteAssist() => _assists++;

    internal void NoteDeath() => _deaths++;

    private void NoteAchievement(in KillAchievement ribbon)
    {
        for (int i = 0; i < _achievements.Count; i++)
        {
            var t = _achievements[i];
            if (t.Kind == ribbon.Kind && string.Equals(t.Title, ribbon.Title, StringComparison.Ordinal))
            {
                t.Count++;
                _achievements[i] = t;
                return;
            }
        }

        // 种类数理论上不会多（十几种 × 多杀 / 连杀的几个标题），留个上限防止异常数据无界增长
        if (_achievements.Count >= MaxAchievementKinds) return;
        _achievements.Add(new AchievementTally { Kind = ribbon.Kind, Tier = ribbon.Tier, Title = ribbon.Title, Count = 1 });
    }

    /// <summary>冻结当前统计。之后继续累计不会影响已返回的快照。</summary>
    internal RaidSummary Snapshot()
    {
        var sorted = new List<AchievementTally>(_achievements);
        // 稳定排序：档次高的在前，同档次次数多的在前，其余保持首次出现的先后
        var indexed = new List<(AchievementTally T, int Order)>(sorted.Count);
        for (int i = 0; i < sorted.Count; i++) indexed.Add((sorted[i], i));
        indexed.Sort((a, b) =>
        {
            int c = ((int)b.T.Tier).CompareTo((int)a.T.Tier);
            if (c != 0) return c;
            c = b.T.Count.CompareTo(a.T.Count);
            return c != 0 ? c : a.Order.CompareTo(b.Order);
        });

        var list = new SummaryAchievement[indexed.Count];
        for (int i = 0; i < list.Length; i++)
        {
            var t = indexed[i].T;
            list[i] = new SummaryAchievement(t.Kind, t.Tier, t.Title, t.Count);
        }

        return new RaidSummary
        {
            Kills = _kills,
            HeadshotKills = _headshotKills,
            LongestKill = _longestKill,
            BestStreak = _bestStreak,
            Score = _score,
            BodyDamage = _bodyDamage,
            ArmorDamage = _armorDamage,
            Assists = _assists,
            Deaths = _deaths,
            HitsTaken = _hitsTaken,
            DamageTaken = _damageTaken,
            Achievements = list,
        };
    }

    /// <summary>面板标题。</summary>
    internal static string Title() => Loc.Pick("战局小结", "Raid Summary");

    /// <summary>
    /// 小结要显示的行（按当前界面语言）。零值的统计行直接省略，面板只显示这一局真正发生过的事；
    /// 成就最多列 <paramref name="maxAchievementRows"/> 行。
    /// </summary>
    internal static List<SummaryRow> Rows(RaidSummary s, int maxAchievementRows = 4)
    {
        var rows = new List<SummaryRow>(12);
        bool en = Loc.IsEnglish;

        rows.Add(new SummaryRow(en ? "Kills" : "击杀", s.Kills.ToString()));
        if (s.HeadshotKills > 0) rows.Add(new SummaryRow(en ? "Headshot Kills" : "爆头击杀", s.HeadshotKills.ToString()));
        if (s.LongestKill >= 1f) rows.Add(new SummaryRow(en ? "Longest Kill" : "最远击杀", Math.Round(s.LongestKill).ToString("0") + " m"));
        if (s.BestStreak >= 2) rows.Add(new SummaryRow(en ? "Best Streak" : "最高连杀", s.BestStreak.ToString()));
        if (s.Score > 0) rows.Add(new SummaryRow(en ? "Score" : "得分", s.Score.ToString()));
        if (s.BodyDamage >= 0.5f) rows.Add(new SummaryRow(en ? "Damage Dealt" : "造成伤害", Math.Round(s.BodyDamage).ToString("0")));
        if (s.Assists > 0) rows.Add(new SummaryRow(en ? "Assists" : "助攻", s.Assists.ToString()));
        if (s.DamageTaken >= 0.5f) rows.Add(new SummaryRow(en ? "Damage Taken" : "承受伤害", Math.Round(s.DamageTaken).ToString("0")));
        if (s.Deaths > 0) rows.Add(new SummaryRow(en ? "Deaths" : "阵亡", s.Deaths.ToString()));

        int shown = Math.Min(Math.Max(0, maxAchievementRows), s.Achievements.Length);
        if (shown > 0)
        {
            rows.Add(new SummaryRow(string.Empty, string.Empty, SummaryRowKind.Divider));
            for (int i = 0; i < shown; i++)
            {
                var a = s.Achievements[i];
                rows.Add(new SummaryRow(a.Title, "×" + a.Count, SummaryRowKind.Achievement));
            }
        }

        return rows;
    }
}

/// <summary>调度器每帧给调用方的指令。</summary>
internal enum SummaryAction
{
    None = 0,

    /// <summary>现在显示 <see cref="SummaryScheduler.Current"/>。</summary>
    Show = 1,
}

/// <summary>
/// 小结什么时候弹出（0.7.0）。纯逻辑状态机，输入是“战局结束 / 开始”事件和“现在是不是在战局里 / 主菜单加载了没有”。
///
/// 为什么不能在 <c>RaidEnded</c> 当场显示（2026-10-25 实机日志）：战局结束后约 12~17 秒才出结算界面、25~33 秒才进入可交互菜单，
/// 当场显示会盖在加载 / 结算过渡上，还会被加载卡顿的长帧吃掉显示时间。所以：
/// <code>
/// Idle ──RaidEnded(有活动)──▶ Pending ──菜单场景「缺席→出现」──▶ Armed ──再等 delay──▶ Show（回到 Idle）
///                                  └─ 兜底：战局结束超过 fallbackAfter 秒且菜单已加载 ─▶ Armed
/// 任何状态 ──RaidStarted──▶ Idle（取消）          Pending 超过 expireAfter 秒仍没等到菜单 ──▶ 丢弃
/// </code>
/// 兜底存在的原因：<c>MenuUIScene</c> 在战局中是否被卸载<b>没有直接证据</b>（只知道每次战局后都会重新加载它）。
/// 如果它其实一直在，“缺席→出现”的上升沿永远等不到，小结就永远不显示。
/// </summary>
internal sealed class SummaryScheduler
{
    internal enum Phase { Idle, Pending, Armed }

    /// <summary>战局结束后多久（秒）菜单一直在也强制放行。实测可交互菜单最晚约 33 秒，取 40。</summary>
    internal const float DefaultFallbackAfter = 40f;

    /// <summary>挂起超过多久（秒）还没进菜单就丢弃（比如玩家直接退出了游戏界面）。</summary>
    internal const float DefaultExpireAfter = 300f;

    private Phase _phase;
    private RaidSummary? _pending;
    private float _endedAt;
    private float _armedAt;
    private bool _sawMenuAbsent;

    internal Phase State => _phase;

    /// <summary>待显示 / 正在放行的那份小结。<see cref="Phase.Idle"/> 时为 null。</summary>
    internal RaidSummary? Current => _pending;

    internal void Reset()
    {
        _phase = Phase.Idle;
        _pending = null;
        _sawMenuAbsent = false;
    }

    /// <summary>新战局开始（含藏身处）：取消任何挂起的小结。</summary>
    internal void OnRaidStarted() => Reset();

    /// <summary>
    /// 战局结束。<paramref name="snapshot"/> 没有任何活动（藏身处的空局）时<b>忽略</b>，
    /// 不会覆盖已经挂起的真实小结。
    /// </summary>
    internal void OnRaidEnded(float now, RaidSummary snapshot)
    {
        if (snapshot == null || !snapshot.HasActivity) return;

        _pending = snapshot;
        _phase = Phase.Pending;
        _endedAt = now;
        _sawMenuAbsent = false;
    }

    /// <summary>每帧（或限频）调用。<paramref name="menuLoaded"/> 只在 Pending 阶段才会被用到，调用方可以据此省掉场景查询。</summary>
    internal SummaryAction Tick(
        float now,
        bool inRaid,
        bool menuLoaded,
        float delay,
        float fallbackAfter = DefaultFallbackAfter,
        float expireAfter = DefaultExpireAfter)
    {
        if (_phase == Phase.Idle || inRaid) return SummaryAction.None;

        if (_phase == Phase.Pending)
        {
            if (now - _endedAt > expireAfter)
            {
                Reset();
                return SummaryAction.None;
            }

            if (!menuLoaded)
            {
                _sawMenuAbsent = true;
                return SummaryAction.None;
            }

            bool risingEdge = _sawMenuAbsent;
            bool fallback = now - _endedAt >= fallbackAfter;
            if (!risingEdge && !fallback) return SummaryAction.None;

            _phase = Phase.Armed;
            _armedAt = now;
            return SummaryAction.None;
        }

        // Armed：菜单已就绪，再等 delay 让加载动画和菜单入场动画走完
        if (now - _armedAt < Math.Max(0f, delay)) return SummaryAction.None;

        _phase = Phase.Idle;   // Current 保留到下一次 Reset / OnRaidEnded，供调用方取用
        return SummaryAction.Show;
    }

    /// <summary>调用方在 <see cref="SummaryAction.Show"/> 之后调用：把待显示的小结取走并清空。</summary>
    internal RaidSummary? Take()
    {
        var s = _pending;
        _pending = null;
        return s;
    }

    /// <summary>当前是否有小结在排队（Pending 或 Armed）。调用方据此决定要不要去查主菜单场景。</summary>
    internal bool IsWaiting => _phase != Phase.Idle;
}
