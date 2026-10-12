using System;
using System.Collections.Generic;
using AyaHitmarker.Game;

namespace AyaHitmarker.Ui;

/// <summary>卡片上的标签种类。文案由视图层按种类查表，模型只关心「有没有」。</summary>
internal enum CardTagKind
{
    Kill = 0,
    Headshot = 1,
    Melee = 2,
    Explosive = 3,
    Sniper = 4,
}

/// <summary>喂给 <see cref="KillCardModel"/> 的一次击杀。刻意只含值类型和纯结构体，不碰 EFT / Unity。</summary>
internal readonly struct CardKillInput
{
    internal readonly bool Headshot;
    internal readonly bool Melee;
    internal readonly bool Explosive;
    internal readonly bool Sniper;
    internal readonly float Distance;
    internal readonly int Score;

    /// <summary>这一杀触发的成就；<see cref="KillAchievement.None"/> 表示没有。</summary>
    internal readonly KillAchievement Ribbon;

    /// <summary>窗口内的连杀段数（双杀 = 2 …），用来决定横幅骷髅徽记里放几个头。</summary>
    internal readonly int MultiCount;

    internal CardKillInput(
        bool headshot,
        bool melee,
        bool explosive,
        bool sniper,
        float distance,
        int score,
        KillAchievement ribbon,
        int multiCount)
    {
        Headshot = headshot;
        Melee = melee;
        Explosive = explosive;
        Sniper = sniper;
        Distance = distance;
        Score = score;
        Ribbon = ribbon;
        MultiCount = multiCount;
    }
}

/// <summary>卡片上的一个骷髅。<see cref="Serial"/> 单调递增，视图用它判断「这是不是新来的」。</summary>
internal readonly struct CardIcon
{
    internal readonly bool Headshot;
    internal readonly float Born;
    internal readonly int Serial;

    internal CardIcon(bool headshot, float born, int serial)
    {
        Headshot = headshot;
        Born = born;
        Serial = serial;
    }
}

/// <summary>卡片上的一个标签。</summary>
internal readonly struct CardTag
{
    internal readonly CardTagKind Kind;
    internal readonly float Born;

    internal CardTag(CardTagKind kind, float born)
    {
        Kind = kind;
        Born = born;
    }
}

/// <summary>
/// 击杀卡片的**累加模型**（纯逻辑，可离线测）。
///
/// 战地 6 的击杀卡片不是「一杀一张」：窗口内连续击杀会**并到同一张卡上**——
/// 骷髅并排、分数累计（100 → 200）、标签取并集（HEADSHOT + KILL）、名字换成最新那个受害者，
/// 窗口过了才整体退场。实录里连杀演示的第 4 杀就是这样（红骷髅 + 白骷髅 / 200 / [HEADSHOT][KILL]）。
///
/// 时间线：<c>最后一杀 ── Hold ──► 退场开始 ── ExitDuration ──► 卡片结束</c>。
/// 退场期间又来一杀 → 取消退场、继续累加（BF6 也是这样）。
/// 所有时间都是调用方传入的「UI 时钟」（<see cref="UiClock"/>），模型自己不读时间。
/// </summary>
internal sealed class KillCardModel
{
    private readonly List<CardIcon> _icons = new List<CardIcon>(8);
    private readonly List<CardTag> _tags = new List<CardTag>(5);
    private int _serial;

    /// <summary>卡片是否在场（从第一杀到退场结束）。</summary>
    internal bool Active { get; private set; }

    /// <summary>本张卡片的第一杀时间。</summary>
    internal float StartTime { get; private set; }

    internal float LastKillTime { get; private set; }

    /// <summary>本张卡片累计的击杀数。</summary>
    internal int KillCount { get; private set; }

    internal int TotalScore { get; private set; }

    /// <summary>上一次累加前<b>屏幕上显示的</b>分数 —— 数字滚动从这里出发，而不是从 0。</summary>
    internal int PreviousScore { get; private set; }

    internal float ScoreChangedAt { get; private set; }

    internal float LastDistance { get; private set; }

    /// <summary>每来一杀 +1。视图用它判断「名字行 / 距离标签要不要重新显影」。</summary>
    internal int VictimSerial { get; private set; }

    internal float VictimBorn { get; private set; }

    /// <summary>当前横幅（成就）。后来的击杀没有成就时，横幅保持，直到卡片结束。</summary>
    internal KillAchievement Banner { get; private set; } = KillAchievement.None;

    internal int BannerMulti { get; private set; }

    internal float BannerBorn { get; private set; }

    /// <summary>横幅每被替换一次 +1。视图用它触发「旧的闪灭 → 新的显影」。</summary>
    internal int BannerSerial { get; private set; }

    internal float Hold { get; private set; } = 3.6f;

    internal float ExitDuration { get; private set; } = 0.5f;

    internal IReadOnlyList<CardIcon> Icons => _icons;

    internal IReadOnlyList<CardTag> Tags => _tags;

    internal bool HasBanner => Banner.HasTitle;

    /// <summary>退场开始的时刻。</summary>
    internal float ExitStart => LastKillTime + Hold;

    /// <summary>卡片彻底结束的时刻。</summary>
    internal float ExitEnd => ExitStart + ExitDuration;

    /// <summary>自退场开始起的秒数；退场还没开始时为负。</summary>
    internal float ExitAge(float now) => now - ExitStart;

    internal bool IsExiting(float now) => Active && now >= ExitStart;

    /// <summary>
    /// 记一次击杀。返回 <c>true</c> 表示**新开了一张卡**（之前没有卡，或上一张已经退场结束）。
    /// </summary>
    /// <param name="displayedScore">此刻屏幕上显示的（可能还在滚动的）分数；新卡会无视它从 0 开始。</param>
    /// <param name="hold">最后一杀之后卡片停留多久才开始退场。</param>
    /// <param name="exitDuration">退场用多久。</param>
    /// <param name="maxIcons">骷髅上限，超出时挤掉最老的。</param>
    internal bool AddKill(
        float now,
        in CardKillInput k,
        int displayedScore,
        float hold,
        float exitDuration,
        int maxIcons)
    {
        bool fresh = !Active || now >= ExitEnd;
        if (fresh) Clear();

        Active = true;
        Hold = Math.Max(0.1f, hold);
        ExitDuration = Math.Max(0f, exitDuration);

        if (fresh) StartTime = now;
        LastKillTime = now;
        KillCount++;

        // ---- 分数 ----
        PreviousScore = fresh ? 0 : Math.Max(0, displayedScore);
        TotalScore += Math.Max(0, k.Score);
        ScoreChangedAt = now;

        // ---- 骷髅 ----
        _icons.Add(new CardIcon(k.Headshot, now, ++_serial));
        int cap = Math.Clamp(maxIcons, 1, 24);
        while (_icons.Count > cap) _icons.RemoveAt(0);

        // ---- 标签：取并集，保持首次出现的顺序；爆头击杀只算「爆头」，不再额外加「击杀」----
        AddTag(k.Headshot ? CardTagKind.Headshot : CardTagKind.Kill, now);
        if (k.Melee) AddTag(CardTagKind.Melee, now);
        if (k.Explosive) AddTag(CardTagKind.Explosive, now);
        if (k.Sniper) AddTag(CardTagKind.Sniper, now);

        // ---- 受害者信息（名字 / 距离）每杀都换 ----
        LastDistance = k.Distance;
        VictimSerial++;
        VictimBorn = now;

        // ---- 横幅：有新成就才替换 ----
        if (k.Ribbon.HasTitle)
        {
            Banner = k.Ribbon;
            BannerMulti = k.MultiCount;
            BannerBorn = now;
            BannerSerial++;
        }

        return fresh;
    }

    /// <summary>每帧调一次：过了退场结束时刻就清空并返回 <c>true</c>（只返回一次）。</summary>
    internal bool Tick(float now)
    {
        if (!Active || now < ExitEnd) return false;
        Clear();
        return true;
    }

    internal void Reset() => Clear();

    private void AddTag(CardTagKind kind, float now)
    {
        for (int i = 0; i < _tags.Count; i++)
        {
            if (_tags[i].Kind == kind) return;
        }
        _tags.Add(new CardTag(kind, now));
    }

    private void Clear()
    {
        Active = false;
        StartTime = 0f;
        LastKillTime = 0f;
        KillCount = 0;
        TotalScore = 0;
        PreviousScore = 0;
        ScoreChangedAt = 0f;
        LastDistance = 0f;
        VictimBorn = 0f;
        Banner = KillAchievement.None;
        BannerMulti = 0;
        BannerBorn = 0f;
        _icons.Clear();
        _tags.Clear();
        // 序号（_serial / VictimSerial / BannerSerial）不清零：视图靠它区分新旧，回绕会让新元素被当成旧的
    }
}
