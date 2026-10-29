using System;
using System.Collections.Generic;

namespace AyaHitmarker.Game;

/// <summary>
/// 击杀成就的种类。UI 用它决定缎带配色/图标；逻辑层只产生它。
/// 刻意不依赖 <c>UnityEngine</c> / <c>EFT</c> —— 这样它可以在没有游戏程序集的
/// 情况下被 <c>tests/AyaHitmarker.Tests</c> 直接跑（见 AGENTS.md §2）。
/// </summary>
internal enum KillAchievementKind
{
    None = 0,

    /// <summary>先锋：本局第一杀（对应 BF6 的 Vanguard/先锋）</summary>
    FirstBlood,

    /// <summary>复仇者（Avenger）：击杀刚刚杀死过我的玩家</summary>
    Revenge,

    /// <summary>双杀 / 三杀 / 多重击杀</summary>
    MultiKill,

    /// <summary>连杀天梯（猎手 / 狂战士 / 势不可挡 …）</summary>
    Streak,

    /// <summary>枪手（Gunner）：窗口内用**不同武器**再次击杀（BF6 官方荣誉）</summary>
    Gunner,

    /// <summary>掠夺者（Marauder）：第 4 杀之后很快又杀一个（BF6 官方荣誉，可重复）</summary>
    Marauder,

    BossSlayer,
    Marksman,
    LongShot,
    Melee,
    Explosive,
    EliteKill,
    Headshot,
}

/// <summary>成就的高低档次，UI 用来选颜色强度与动效力度。</summary>
internal enum KillAchievementTier
{
    Normal = 0,
    Good = 1,
    Great = 2,
    Epic = 3,
}

/// <summary>主成就（击杀卡片上那条缎带）。</summary>
internal readonly struct KillAchievement
{
    internal readonly string Title;
    internal readonly string Detail;
    internal readonly KillAchievementKind Kind;
    internal readonly KillAchievementTier Tier;

    internal KillAchievement(string title, string detail, KillAchievementKind kind, KillAchievementTier tier)
    {
        Title = title;
        Detail = detail;
        Kind = kind;
        Tier = tier;
    }

    internal bool HasTitle => !string.IsNullOrEmpty(Title);

    internal static readonly KillAchievement None = new KillAchievement(string.Empty, string.Empty, KillAchievementKind.None, KillAchievementTier.Normal);
}

/// <summary>
/// 一次击杀的**纯数据**快照。
///
/// 和 <see cref="KillRecord"/> 的区别：KillRecord 里带着 <c>WildSpawnType</c> 这种 interop 枚举，
/// 而这里只有 string/bool/float，方便离线跑、也方便以后换数据源而不动判定逻辑。
/// </summary>
internal readonly struct KillFact
{
    internal readonly string VictimKey;   // 归一化昵称，用于复仇判定
    internal readonly string WeaponKey;   // 归一化武器名，用于「枪手」（换武器）判定
    internal readonly string BotType;     // USEC/BEAR/SCAV/BOSS/...
    internal readonly int VictimLevel;
    internal readonly float Distance;
    internal readonly bool Headshot;
    internal readonly bool Melee;
    internal readonly bool Explosive;
    internal readonly bool SniperDamage;
    internal readonly bool VictimIsBoss;
    internal readonly bool VictimIsPmc;

    internal KillFact(
        string victimKey,
        string weaponKey,
        string botType,
        int victimLevel,
        float distance,
        bool headshot,
        bool melee,
        bool explosive,
        bool sniperDamage,
        bool victimIsBoss,
        bool victimIsPmc)
    {
        VictimKey = victimKey ?? string.Empty;
        WeaponKey = weaponKey ?? string.Empty;
        BotType = botType ?? string.Empty;
        VictimLevel = victimLevel;
        Distance = distance;
        Headshot = headshot;
        Melee = melee;
        Explosive = explosive;
        SniperDamage = sniperDamage;
        VictimIsBoss = victimIsBoss;
        VictimIsPmc = victimIsPmc;
    }
}

/// <summary>判定阈值。由配置构造，逻辑层不读 <c>Plugin.Cfg</c>，便于测试。</summary>
internal readonly struct KillStreakSettings
{
    internal readonly float RevengeWindow;
    internal readonly float MultiKillWindow;
    internal readonly float StreakResetWindow;
    internal readonly float GunnerWindow;
    internal readonly float MarauderWindow;
    internal readonly float LongShotDistance;
    internal readonly float MarksmanDistance;
    internal readonly int EliteLevel;
    internal readonly int ScoreBase;
    internal readonly int ScoreHeadshotBonus;
    internal readonly int ScoreLongShotBonus;
    internal readonly int ScoreRevengeBonus;
    internal readonly int ScoreMultiKillBonus;
    internal readonly int ScoreBossBonus;

    internal KillStreakSettings(
        float revengeWindow,
        float multiKillWindow,
        float streakResetWindow,
        float gunnerWindow,
        float marauderWindow,
        float longShotDistance,
        float marksmanDistance,
        int eliteLevel,
        int scoreBase,
        int scoreHeadshotBonus,
        int scoreLongShotBonus,
        int scoreRevengeBonus,
        int scoreMultiKillBonus,
        int scoreBossBonus)
    {
        RevengeWindow = revengeWindow;
        MultiKillWindow = multiKillWindow;
        StreakResetWindow = streakResetWindow;
        GunnerWindow = gunnerWindow;
        MarauderWindow = marauderWindow;
        LongShotDistance = longShotDistance;
        MarksmanDistance = marksmanDistance;
        EliteLevel = eliteLevel;
        ScoreBase = scoreBase;
        ScoreHeadshotBonus = scoreHeadshotBonus;
        ScoreLongShotBonus = scoreLongShotBonus;
        ScoreRevengeBonus = scoreRevengeBonus;
        ScoreMultiKillBonus = scoreMultiKillBonus;
        ScoreBossBonus = scoreBossBonus;
    }

    /// <summary>与旧项目 cfg 对齐的默认值（旧配置没有这些项，这里给一套合理基线）。</summary>
    internal static KillStreakSettings Default => new KillStreakSettings(
        revengeWindow: 120f,
        multiKillWindow: 2.5f,
        streakResetWindow: 10f,
        gunnerWindow: 4f,
        marauderWindow: 8f,
        longShotDistance: 100f,
        marksmanDistance: 200f,
        eliteLevel: 40,
        scoreBase: 100,
        scoreHeadshotBonus: 50,
        scoreLongShotBonus: 50,
        scoreRevengeBonus: 25,
        scoreMultiKillBonus: 25,
        scoreBossBonus: 200);
}

/// <summary>连杀天梯的一档。</summary>
internal readonly struct StreakTier
{
    internal readonly int Count;
    internal readonly string TitleZh;
    internal readonly string TitleEn;
    internal readonly KillAchievementTier Tier;

    internal StreakTier(int count, string titleZh, string titleEn, KillAchievementTier tier)
    {
        Count = count;
        TitleZh = titleZh;
        TitleEn = titleEn;
        Tier = tier;
    }

    /// <summary>按当前界面语言取称号（天梯是静态表，所以每次读都重新挑，切语言立即生效）。</summary>
    internal string Title => Loc.Pick(TitleZh, TitleEn);
}

/// <summary>
/// 连杀 / 成就判定引擎。**纯托管、无 Unity API、无 EFT 类型**，可在主线程任意调用。
///
/// 状态很小（最近一次击杀时间、连杀数、本局击杀数、最近几次阵亡凶手），
/// 所以不需要事件流回放，收到一条 <see cref="KillFact"/> 直接算即可。
///
/// 阈值不从配置里读，而是每次调用由调用方传入（<see cref="KillStreakSettings"/>）——
/// 这样改配置能立即生效，而引擎自己的连杀状态不会被重建。
///
/// 一个 raid 结束时调用 <see cref="Reset"/>（由 GameEvents.RaidStarted/RaidEnded 驱动）。
/// </summary>
internal sealed class KillStreakTracker
{
    /// <summary>连杀天梯：达到该连杀数时给出该称号。顺序必须按 Count 升序。</summary>
    private static readonly StreakTier[] Ladder =
    {
        new StreakTier(3, "猎手", "Hunter", KillAchievementTier.Good),
        new StreakTier(5, "狂战士", "Berserker", KillAchievementTier.Great),
        new StreakTier(7, "势不可挡", "Unstoppable", KillAchievementTier.Great),
        new StreakTier(10, "主宰", "Dominator", KillAchievementTier.Epic),
        new StreakTier(15, "无双", "Peerless", KillAchievementTier.Epic),
        new StreakTier(20, "战神", "Godlike", KillAchievementTier.Epic),
    };

    private readonly List<LocalDeath> _localDeaths = new List<LocalDeath>(4);

    private struct LocalDeath
    {
        internal float Time;
        internal string KillerKey;
    }

    private float _lastKillTime = float.NegativeInfinity;
    private float _fourthKillTime = float.NegativeInfinity;
    private string _lastWeaponKey = string.Empty;
    private int _streak;
    private int _raidKills;
    private int _multiCount;

    internal KillStreakTracker() { }

    /// <summary>天梯的档位数量。</summary>
    internal static int LadderLength => Ladder.Length;

    /// <summary>第 <paramref name="index"/> 档需要的连杀数；越界返回 -1。</summary>
    internal static int TierCountAt(int index)
        => index >= 0 && index < Ladder.Length ? Ladder[index].Count : -1;

    /// <summary>比 <paramref name="streak"/> 高的下一档连杀数；已到顶返回 -1。</summary>
    internal static int NextTierAfter(int streak)
    {
        for (int i = 0; i < Ladder.Length; i++)
        {
            if (Ladder[i].Count > streak) return Ladder[i].Count;
        }
        return -1;
    }

    internal int CurrentStreak => _streak;
    internal int RaidKills => _raidKills;

    /// <summary>连杀进度：距离天梯下一档的完成度 0..1；已到顶则恒为 1。</summary>
    internal float StreakProgress
    {
        get
        {
            int next = NextTierAfter(_streak);
            if (next < 0) return 1f;
            int prev = PreviousTierCount(_streak);
            if (next <= prev) return 1f;
            float p = (_streak - prev) / (float)(next - prev);
            return Math.Clamp(p, 0f, 1f);
        }
    }

    private static int PreviousTierCount(int streak)
    {
        int prev = 0;
        for (int i = 0; i < Ladder.Length; i++)
        {
            if (Ladder[i].Count <= streak) prev = Ladder[i].Count;
        }
        return prev;
    }

    /// <summary>
    /// 记录一次「我被打死」。用来判定复仇者：之后击杀同一凶手即为复仇。
    /// </summary>
    internal void NoteLocalDeath(float time, string killerKey)
    {
        if (string.IsNullOrWhiteSpace(killerKey)) return;

        // 只保留最近几次，避免长局无界增长
        if (_localDeaths.Count >= 8) _localDeaths.RemoveAt(0);
        _localDeaths.Add(new LocalDeath { Time = time, KillerKey = killerKey });
    }

    /// <summary>战局开始/结束：清空全部连杀状态。</summary>
    internal void Reset()
    {
        _localDeaths.Clear();
        _lastKillTime = float.NegativeInfinity;
        _fourthKillTime = float.NegativeInfinity;
        _lastWeaponKey = string.Empty;
        _streak = 0;
        _raidKills = 0;
        _multiCount = 0;
    }

    /// <summary>
    /// 记一次击杀并给出成就。返回的 <see cref="KillAnalysis.Ribbon"/> 可能是
    /// <see cref="KillAchievement.None"/>（普通击杀），此时 UI 不画缎带。
    /// </summary>
    internal KillAnalysis Register(float now, in KillFact fact, in KillStreakSettings settings)
    {
        float gap = now - _lastKillTime;

        _raidKills++;
        _streak = gap <= settings.StreakResetWindow ? _streak + 1 : 1;
        _multiCount = gap <= settings.MultiKillWindow ? _multiCount + 1 : 1;

        bool revenge = ConsumeRevenge(now, fact.VictimKey, settings.RevengeWindow);

        // 枪手（BF6 Gunner）：短时间内用**不同武器**再杀一个
        bool gunner = gap <= settings.GunnerWindow
                      && !string.IsNullOrEmpty(_lastWeaponKey)
                      && !string.Equals(_lastWeaponKey, fact.WeaponKey, StringComparison.Ordinal);

        // 掠夺者（BF6 Marauder）：第 4 杀之后很快又杀一个
        if (_streak == 4) _fourthKillTime = now;
        bool marauder = _streak > 4 && now - _fourthKillTime <= settings.MarauderWindow;

        float score = ScoreFor(in fact, revenge, _multiCount, in settings);
        var ribbon = PickRibbon(in fact, revenge, _raidKills, _streak, _multiCount, gunner, marauder, in settings);

        _lastKillTime = now;
        _lastWeaponKey = fact.WeaponKey;

        return new KillAnalysis(
            ribbon,
            _streak,
            _multiCount,
            _raidKills,
            (int)MathF.Round(score),
            StreakProgress);
    }

    private bool ConsumeRevenge(float now, string victimKey, float window)
    {
        if (string.IsNullOrWhiteSpace(victimKey)) return false;

        for (int i = _localDeaths.Count - 1; i >= 0; i--)
        {
            var d = _localDeaths[i];
            if (now - d.Time > window) continue;
            if (!string.Equals(d.KillerKey, victimKey, StringComparison.Ordinal)) continue;

            _localDeaths.RemoveAt(i);
            return true;
        }
        return false;
    }

    private static float ScoreFor(in KillFact fact, bool revenge, int multiCount, in KillStreakSettings s)
    {
        float score = s.ScoreBase;
        if (fact.Headshot) score += s.ScoreHeadshotBonus;
        if (fact.Distance >= s.LongShotDistance) score += s.ScoreLongShotBonus;
        if (revenge) score += s.ScoreRevengeBonus;
        if (multiCount > 1) score += s.ScoreMultiKillBonus * (multiCount - 1);
        if (fact.VictimIsBoss) score += s.ScoreBossBonus;
        return score;
    }

    /// <summary>连杀天梯的位置，-1 表示还没到第一档。</summary>
    private static int TierIndex(int streak)
    {
        int idx = -1;
        for (int i = 0; i < Ladder.Length; i++)
        {
            if (streak >= Ladder[i].Count) idx = i;
        }
        return idx;
    }

    /// <summary>
    /// 按优先级挑一条主成就。优先级刻意把「罕见 / 有故事性」的排在前面：
    /// 复仇 &gt; 多杀 &gt; 掠夺者 &gt; 连杀天梯 &gt; 枪手 &gt; 首领 &gt; 神射手 &gt; 远程 &gt; 近战/爆破 &gt; 精锐 &gt; 先锋 &gt; 爆头。
    /// 名字尽量对齐 BF6 的官方荣誉（EA zh-hans）与社区惯用译名。
    /// </summary>
    private KillAchievement PickRibbon(
        in KillFact fact,
        bool revenge,
        int raidKills,
        int streak,
        int multiCount,
        bool gunner,
        bool marauder,
        in KillStreakSettings settings)
    {
        // 1. 复仇者 —— 优先级最高，它承载「刚刚就是这个人打死我」的叙事
        if (revenge)
        {
            return new KillAchievement(Loc.Pick("复仇者", "Avenger"), Describe(in fact), KillAchievementKind.Revenge, KillAchievementTier.Epic);
        }

        // 2. 多杀（战地的 DOUBLE / TRIPLE KILL）
        if (multiCount >= 2)
        {
            string title = multiCount switch
            {
                2 => Loc.Pick("双杀", "Double Kill"),
                3 => Loc.Pick("三杀", "Triple Kill"),
                4 => Loc.Pick("四杀", "Quad Kill"),
                5 => Loc.Pick("五杀", "Penta Kill"),
                _ => Loc.Pick("多杀 x", "Multi Kill x") + multiCount,
            };
            var tier = multiCount >= 4 ? KillAchievementTier.Epic
                     : multiCount == 3 ? KillAchievementTier.Great
                     : KillAchievementTier.Good;
            string multiDetail = Loc.IsEnglish
                ? $"Kill #{multiCount} within {settings.MultiKillWindow:0.#}s"
                : $"间隔 {settings.MultiKillWindow:0.#} 秒内第 {multiCount} 杀";
            return new KillAchievement(title, multiDetail, KillAchievementKind.MultiKill, tier);
        }

        int tierIdx = TierIndex(streak);
        bool freshTier = tierIdx >= 0 && streak == Ladder[tierIdx].Count;
        bool aboveTopTier = tierIdx == Ladder.Length - 1 && streak > Ladder[tierIdx].Count;

        // 3. 掠夺者（BF6 Marauder）—— 刚好踩到天梯新档时让位给「狂战士」这类称号
        if (marauder && !freshTier)
        {
            string marauderDetail = Loc.IsEnglish
                ? $"Another kill within {settings.MarauderWindow:0.#}s of the 4th"
                : $"第 4 杀后 {settings.MarauderWindow:0.#} 秒内再杀";
            return new KillAchievement(Loc.Pick("掠夺者", "Marauder"), marauderDetail, KillAchievementKind.Marauder, KillAchievementTier.Great);
        }

        // 4. 连杀天梯（猎手 / 狂战士 / 势不可挡 …）
        if (freshTier || aboveTopTier)
        {
            var t = Ladder[tierIdx];
            return new KillAchievement(t.Title, Loc.IsEnglish ? $"Streak x{streak}" : $"连杀 x{streak}", KillAchievementKind.Streak, t.Tier);
        }

        // 5. 枪手（BF6 Gunner）—— 短时间内换武器再杀
        if (gunner)
        {
            string gunnerDetail = Loc.IsEnglish
                ? $"Switched weapon within {settings.GunnerWindow:0.#}s"
                : $"{settings.GunnerWindow:0.#} 秒内换了武器";
            return new KillAchievement(Loc.Pick("枪手", "Gunner"), gunnerDetail, KillAchievementKind.Gunner, KillAchievementTier.Great);
        }

        // 6. 具体情况：首领 > 神射手 > 远程 > 近战 > 爆破 > 精锐 PMC > 先锋 > 爆头
        if (fact.VictimIsBoss)
        {
            return new KillAchievement(Loc.Pick("首领猎杀者", "Boss Slayer"), Describe(in fact), KillAchievementKind.BossSlayer, KillAchievementTier.Epic);
        }

        if (fact.Headshot && fact.Distance >= settings.MarksmanDistance)
        {
            return new KillAchievement(Loc.Pick("神射手", "Marksman"), Describe(in fact), KillAchievementKind.Marksman, KillAchievementTier.Epic);
        }

        if (fact.Distance >= settings.LongShotDistance
            || (fact.SniperDamage && fact.Distance >= settings.LongShotDistance))
        {
            return new KillAchievement(Loc.Pick("远程击杀", "Long Shot"), Describe(in fact), KillAchievementKind.LongShot, KillAchievementTier.Great);
        }

        if (fact.Melee)
        {
            return new KillAchievement(Loc.Pick("冷兵器", "Melee Kill"), Describe(in fact), KillAchievementKind.Melee, KillAchievementTier.Great);
        }

        if (fact.Explosive)
        {
            return new KillAchievement(Loc.Pick("爆破专家", "Demolition Expert"), Describe(in fact), KillAchievementKind.Explosive, KillAchievementTier.Great);
        }

        if (fact.VictimIsPmc && fact.VictimLevel >= settings.EliteLevel)
        {
            return new KillAchievement(Loc.Pick("精锐猎杀", "Elite Kill"), Describe(in fact), KillAchievementKind.EliteKill, KillAchievementTier.Good);
        }

        if (raidKills == 1)
        {
            return new KillAchievement(Loc.Pick("先锋", "Vanguard"), Loc.Pick("本局首杀", "First kill"), KillAchievementKind.FirstBlood, KillAchievementTier.Good);
        }

        if (fact.Headshot)
        {
            return new KillAchievement(Loc.Pick("爆头", "Headshot"), Describe(in fact), KillAchievementKind.Headshot, KillAchievementTier.Normal);
        }

        return KillAchievement.None;
    }

    private static string Describe(in KillFact fact)
    {
        if (fact.Distance <= 0.5f) return string.Empty;
        return fact.Distance.ToString("0") + "m";
    }
}

/// <summary>一次击杀的完整判定结果。</summary>
internal readonly struct KillAnalysis
{
    internal readonly int? Experience;
    internal readonly KillAchievement Ribbon;
    internal readonly int Streak;
    internal readonly int MultiCount;
    internal readonly int RaidKills;
    internal readonly int Score;

    /// <summary>距离下一档连杀称号的进度 0..1（连杀进度条用）。</summary>
    internal readonly float StreakProgress;

    internal KillAnalysis(
        KillAchievement ribbon,
        int streak,
        int multiCount,
        int raidKills,
        int score,
        float streakProgress,
        int? experience = null)
    {
        Ribbon = ribbon;
        Streak = streak;
        MultiCount = multiCount;
        RaidKills = raidKills;
        Score = score;
        StreakProgress = streakProgress;
        Experience = experience;
    }

    internal bool HasScore => Score > 0;
    internal KillAnalysis WithExperience(int? experience) => new KillAnalysis(
        Ribbon, Streak, MultiCount, RaidKills, Score, StreakProgress, experience);

    internal static readonly KillAnalysis Empty = default;
}
