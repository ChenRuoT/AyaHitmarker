using System;
using System.Collections.Generic;

namespace AyaHitmarker.Game;

/// <summary>
/// 喂给 <see cref="DamageAccumModel"/> 的一次命中（事件线程里冻结成值类型，见 AGENTS.md §3.4）。
///
/// 只含 string / 数值 / bool，不引用 <c>UnityEngine</c> 或 <c>EFT</c>，所以能进离线测试（AGENTS.md §2）。
/// <see cref="TargetKey"/> 用受害者的 <c>ProfileId</c>：模型只按「key 相同」判断是不是打在同一个人身上，
/// 不关心 key 是 ID 还是昵称。
/// </summary>
internal readonly struct DamageAccumHit
{
    /// <summary>目标身份（ProfileId）。null / 空串表示无法归组，模型会直接忽略这次命中。</summary>
    internal readonly string TargetKey;

    /// <summary>本次命中的身体伤害（主数字）。&lt;= 0 / NaN / 无限值按 0 处理。</summary>
    internal readonly float Damage;

    /// <summary>
    /// 本次命中的护甲耐久伤害（0.6.1：主数字上方另起一行显示；不需要这一行的调用方传 0）。
    /// 身体伤害与护甲伤害都没有显示价值（都 &lt;= 0）的命中，模型忽略。
    /// </summary>
    internal readonly float ArmorDamage;

    /// <summary>本次是不是爆头。槽里只要有过一次爆头，整槽都按爆头配色显示。</summary>
    internal readonly bool Headshot;

    /// <summary>主数字是否用护甲样式（旧「取值 = 总和」模式下，被挡下的命中整个数字都是护甲配色 + 盾标）。槽里每次都是才算。</summary>
    internal readonly bool ArmorStyle;

    /// <summary>命中的 UI 时钟时刻（<c>UiClock.Now</c>）。模型自己不读时间，全部由调用方传入。</summary>
    internal readonly float Time;

    internal DamageAccumHit(string targetKey, float damage, float armorDamage, bool headshot, bool armorStyle, float time)
    {
        TargetKey = targetKey;
        Damage = damage;
        ArmorDamage = armorDamage;
        Headshot = headshot;
        ArmorStyle = armorStyle;
        Time = time;
    }
}

/// <summary>
/// 一个目标的累加伤害数字槽 —— 战地 6 式「同一目标连续命中只显示一个不断变大的数字」（32 → 67 → 99）。
///
/// 生命周期：<c>首次命中(Born) —窗口内继续命中→ 累计 —— 最后一次命中(LastHit) —Window—► 淡出 —Lifetime—► 清理</c>。
/// 击杀时从 <see cref="KillTime"/> 重新计时并变红。
///
/// 槽的属性对外只读：写入口只有 <see cref="DamageAccumModel"/>（Add / PromoteKill / Tick）。
/// 视图只负责读它画 uGUI，不碰 uGUI 写入纪律（AGENTS.md §3.5）—— 模型只吐数据。
/// </summary>
internal sealed class DamageAccumSlot
{
    internal DamageAccumSlot(string targetKey, float damage, float armorDamage, bool headshot, bool armorStyle, float time)
    {
        TargetKey = targetKey;
        Damage = damage;
        ArmorDamage = armorDamage;
        HitCount = 1;
        Headshot = headshot;
        ArmorStyle = armorStyle;
        Born = time;
        LastHit = time;
        Changed = true;   // 新槽出生就要让视图显影 / 弹出
    }

    /// <summary>目标身份（ProfileId），槽的分组键。</summary>
    internal string TargetKey { get; }

    /// <summary>窗口内累计的身体伤害总和（主数字）。</summary>
    internal float Damage { get; private set; }

    /// <summary>窗口内累计的护甲耐久伤害总和（主数字上方那一行）。</summary>
    internal float ArmorDamage { get; private set; }

    /// <summary>累计了几次命中。</summary>
    internal int HitCount { get; private set; }

    /// <summary>窗口内只要有过一次爆头就为 true（数字用爆头配色）。</summary>
    internal bool Headshot { get; private set; }

    /// <summary>只有槽里<b>每一次</b>命中都要求护甲样式时才为 true（旧「总和」模式下整个数字用护甲配色 + 盾标）。</summary>
    internal bool ArmorStyle { get; private set; }

    /// <summary>是否已被 <see cref="DamageAccumModel.PromoteKill"/> 结算成击杀（变红、不再累计）。</summary>
    internal bool Kill { get; private set; }

    /// <summary>槽的出生时刻（第一次命中），窗口内累计时不变。</summary>
    internal float Born { get; }

    /// <summary>最后一次累计命中的时刻；窗口从它开始算，每次累计都刷新。</summary>
    internal float LastHit { get; private set; }

    /// <summary>击杀结算时刻；只有 <see cref="Kill"/> 为 true 时有意义。</summary>
    internal float KillTime { get; private set; }

    /// <summary>
    /// 本次 <c>Add</c> / <c>PromoteKill</c> 之后置 true：视图据此决定「这个数字要重新滚动 / 弹出 / 变色」。
    /// 由 <see cref="DamageAccumModel.Tick"/> 统一清掉 —— 所以 Tick 要放在视图消费完 Changed 之后
    /// （通常是一帧的最后，或下一帧的开头）。
    /// </summary>
    internal bool Changed { get; private set; }

    /// <summary>窗口内再命中一次：累计伤害 / 次数，刷新最后命中时刻。</summary>
    internal void Accumulate(float damage, float armorDamage, bool headshot, bool armorStyle, float time)
    {
        Damage += damage;
        ArmorDamage += armorDamage;
        HitCount++;
        Headshot |= headshot;
        ArmorStyle &= armorStyle;   // 一次不是护甲样式就永久失去护甲样式
        LastHit = time;
        Changed = true;
    }

    /// <summary>结算成击杀：变红并从 <paramref name="now"/> 重新计时。</summary>
    internal void MarkKill(float now)
    {
        Kill = true;
        KillTime = now;
        Changed = true;
    }

    internal void ClearChanged() => Changed = false;

    /// <summary>
    /// 是否该被 <see cref="DamageAccumModel.Tick"/> 清掉。
    /// 普通槽：最后命中之后 <c>Window</c>（定住）+ <c>Lifetime</c>（淡出）都过完；
    /// 击杀槽：同样是 <c>Window + Lifetime</c>，但从击杀时刻起算（见 <see cref="DamageAccumModel.Alpha"/>）。
    /// </summary>
    internal bool Expired(float now, float window, float lifetime)
        => now > (Kill ? KillTime : LastHit) + window + lifetime;
}

/// <summary>
/// 战地 6 式**累加伤害数字**的模型（纯逻辑，可离线测，不引用 Unity / EFT）。
///
/// 规则：同一目标（<see cref="DamageAccumHit.TargetKey"/> 相同）在窗口内连续命中，只维护**一个**槽：
/// 身体伤害与护甲伤害分别累计、命中次数累加、爆头取或、护甲样式取与。
/// 窗口过期或那个槽已经击杀结算 → **新槽插到最前**（<see cref="Slots"/> 最前 = 最新）。
///
/// 击杀：<see cref="PromoteKill"/> 把对应槽标红（<see cref="DamageAccumSlot.Kill"/>），
/// 显示时长从击杀时刻重新起算 —— 最后一击值得多看一眼。
///
/// 时间全部是调用方传入的 UI 时钟（<c>UiClock.Now</c> 语义，float），模型自己不读时间。
/// 视图每帧的顺序建议：<c>Add / PromoteKill →（画数字）→ Tick</c>，这样 <see cref="DamageAccumSlot.Changed"/>
/// 在本帧一直可见，下一帧 Tick 才清掉。
/// </summary>
internal sealed class DamageAccumModel
{
    private readonly List<DamageAccumSlot> _slots;
    private readonly int _maxSlots;

    /// <param name="window">累加窗口（秒）：最后一次命中之后这么久内的命中并进同一个槽。</param>
    /// <param name="lifetime">淡出时长（秒）；击杀槽的总显示时长也是它（从击杀时刻起算）。</param>
    /// <param name="maxSlots">同时存在的槽上限，超出时丢最旧的（列表尾部）。</param>
    internal DamageAccumModel(float window, float lifetime, int maxSlots = 8)
    {
        Window = MathF.Max(0f, window);
        Lifetime = MathF.Max(0f, lifetime);
        _maxSlots = Math.Clamp(maxSlots, 1, 64);
        _slots = new List<DamageAccumSlot>(_maxSlots);
    }

    /// <summary>累加窗口（秒）。</summary>
    internal float Window { get; }

    /// <summary>淡出时长（秒）；击杀槽从击杀时刻起的总显示时长。</summary>
    internal float Lifetime { get; }

    /// <summary>当前所有槽，<b>最前 = 最新</b>。</summary>
    internal IReadOnlyList<DamageAccumSlot> Slots => _slots;

    /// <summary>
    /// 吃进一次命中。同一目标、未击杀、且距上次命中不超过 <see cref="Window"/> → 累加；
    /// 否则新建一个槽插到最前。空 key、身体与护甲伤害都没有显示价值（&lt;= 0 / NaN / 无限）的命中直接忽略。
    /// </summary>
    internal void Add(in DamageAccumHit hit)
    {
        // 非正 / NaN / 无限的伤害按 0 处理（!(x > 0) 顺带挡住 NaN —— NaN > 0 为 false）
        float damage = hit.Damage > 0f && float.IsFinite(hit.Damage) ? hit.Damage : 0f;
        float armor = hit.ArmorDamage > 0f && float.IsFinite(hit.ArmorDamage) ? hit.ArmorDamage : 0f;

        // 空 key 没法归组；两种伤害都没有就没有显示价值
        if (string.IsNullOrEmpty(hit.TargetKey) || (damage <= 0f && armor <= 0f)) return;

        float now = hit.Time;
        for (int i = 0; i < _slots.Count; i++)
        {
            DamageAccumSlot slot = _slots[i];

            // 已经击杀结算过的槽不再累计（否则最后一击会悄悄涨数字）
            if (slot.Kill) continue;
            if (!string.Equals(slot.TargetKey, hit.TargetKey, StringComparison.Ordinal)) continue;

            // 窗口内（含边界）→ 并进这个槽
            if (now - slot.LastHit <= Window)
            {
                slot.Accumulate(damage, armor, hit.Headshot, hit.ArmorStyle, now);
                return;
            }

            // 同一个目标但窗口已过：新建槽。同一个 key 最多只有一个存活槽，
            // 更早的同类槽都已击杀结算，所以这里可以直接停。
            break;
        }

        var fresh = new DamageAccumSlot(hit.TargetKey, damage, armor, hit.Headshot, hit.ArmorStyle, now);
        _slots.Insert(0, fresh);

        // 上限：丢最旧的（列表尾部）。手写 while/RemoveAt，不用 LINQ（避免每帧分配）
        while (_slots.Count > _maxSlots) _slots.RemoveAt(_slots.Count - 1);
    }

    /// <summary>
    /// 击杀确认：把该目标的槽标红，显示时长从 <paramref name="now"/> 重新起算。
    /// 返回 <c>false</c> 表示模型里没有可结算的槽（还没收到那一击的命中数据 / 已经结算过），视图可以忽略。
    /// </summary>
    internal bool PromoteKill(string targetKey, float now)
    {
        if (string.IsNullOrEmpty(targetKey)) return false;

        for (int i = 0; i < _slots.Count; i++)
        {
            DamageAccumSlot slot = _slots[i];
            if (slot.Kill) continue;   // 已经结算过的不重复触发变红动画
            if (!string.Equals(slot.TargetKey, targetKey, StringComparison.Ordinal)) continue;

            slot.MarkKill(now);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 每帧调一次：清掉过期槽，并清掉存活槽的 <see cref="DamageAccumSlot.Changed"/>。
    /// 要在视图消费完 Changed 之后调用（通常是一帧的最后，或下一帧的开头）。
    ///
    /// 过期判据：普通槽 <c>now &gt; LastHit + Window + Lifetime</c>；击杀槽 <c>now &gt; KillTime + Window + Lifetime</c>。
    /// 刚好等于到期时刻时保留（严格大于才删）—— 此时 <see cref="Alpha"/> 已经是 0，下一帧就没了。
    /// </summary>
    internal void Tick(float now)
    {
        for (int i = _slots.Count - 1; i >= 0; i--)
        {
            DamageAccumSlot slot = _slots[i];
            if (slot.Expired(now, Window, Lifetime))
            {
                _slots.RemoveAt(i);
                continue;
            }
            slot.ClearChanged();
        }
    }

    /// <summary>
    /// 槽在 <paramref name="now"/> 时刻的透明度，0..1。
    ///
    /// <b>普通槽</b>：<c>now - LastHit &lt;= window</c> → 1；之后 <c>1 - (now - LastHit - window) / lifetime</c>
    /// 线性淡出并钳到 [0,1]（总时长 = window + lifetime）。
    ///
    /// <b>击杀槽</b>：同一条时间线，但从 <see cref="DamageAccumSlot.KillTime"/> 重新计时 ——
    /// 先全亮 <c>window</c> 秒，再用 <c>lifetime</c> 秒线性淡出，在 <c>KillTime + window + lifetime</c> 归零
    /// （= <see cref="Tick"/> 的清理时刻）。红数字至少和普通数字停得一样久，并且总有完整的淡出段。
    /// （0.6 初版把击杀槽的总时长压成 <c>lifetime</c>、全亮段取 <c>min(window, lifetime)</c>：
    /// 默认窗口 2 秒 &gt; 淡出 0.9 秒时淡出段为 0，红数字 0.9 秒后直接消失、比普通数字还短。）
    /// </summary>
    internal static float Alpha(DamageAccumSlot slot, float now, float window, float lifetime)
    {
        if (slot == null) return 0f;

        float full = MathF.Max(0f, window);
        float fade = MathF.Max(0f, lifetime);

        float age = now - (slot.Kill ? slot.KillTime : slot.LastHit);
        if (age <= full) return 1f;
        if (fade <= 0f) return 0f;
        return Math.Clamp(1f - (age - full) / fade, 0f, 1f);
    }

    /// <summary>清空所有槽（战局开始 / 视图重置时调）。时间基准由调用方自己管。</summary>
    internal void Reset() => _slots.Clear();
}
