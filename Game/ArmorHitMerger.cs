using System;
using System.Collections.Generic;

namespace AyaHitmarker.Game;

/// <summary>一次命中与护甲的关系（0.6.1：由 <see cref="PlateStageMerger{T}"/> 判定）。</summary>
internal enum ArmorOutcome
{
    /// <summary>没有护甲参与（<c>DidArmorDamage == 0</c>）。</summary>
    None = 0,

    /// <summary>子弹被护甲挡下（可能带一点钝伤）：蓝色辉光 / 护甲数字 / 受击方向白色点阵。</summary>
    Blocked = 1,

    /// <summary>子弹击穿了护甲：身体伤害照常显示，护甲耐久伤害另起一行放在身体伤害上方。</summary>
    Penetrated = 2,
}

/// <summary>合并器需要的最小只读视图；<c>HitRecord</c> 和测试里的假记录都实现它。</summary>
internal interface IArmorSample
{
    string TargetKey { get; }
    bool VictimIsLocalPlayer { get; }
    float BodyDamage { get; }
    float ArmorDamage { get; }
    bool Headshot { get; }
}

/// <summary>合并器的输出：保留下来的那条原始记录 + 合并后的伤害 + 护甲结局。</summary>
internal readonly struct ResolvedHit<T> where T : struct, IArmorSample
{
    /// <summary>位置 / 方向等其余字段取自这条记录（有后续事件时是后续事件，即击穿后落在身体上的那一下）。</summary>
    internal readonly T Primary;
    internal readonly float Body;
    internal readonly float Armor;
    internal readonly ArmorOutcome Outcome;

    /// <summary>
    /// 合并后的爆头标志：板阶段与后续事件**任一个**打在头上就算。头盔挡板在头部碰撞体上，
    /// 击穿后的第二个事件不保证还带着同一个碰撞体信息，只看后者会把“打穿头盔”的爆头漏掉。
    /// </summary>
    internal readonly bool Headshot;

    internal ResolvedHit(T primary, float body, float armor, ArmorOutcome outcome, bool headshot)
    {
        Primary = primary;
        Body = body;
        Armor = armor;
        Outcome = outcome;
        Headshot = headshot;
    }
}

/// <summary>
/// 护甲命中的判据（纯函数，数值来自 2026-10-25 实机日志 40 条命中，见 README 0.6.1）。
///
/// 实测事实：一发子弹在有护甲的目标上会产生 1~2 个 <c>OnBeingHit</c> 事件 ——
/// <list type="bullet">
///   <item><b>板阶段</b>（<c>body=0, armor≈52, raw=0</c>）：子弹打在护甲板上、扣了耐久，伤害归零；</item>
///   <item>子弹若击穿，<b>紧跟着</b>（间隔 2 行日志、命中点相距 0.02~0.16 米）再来一个落在身体上的事件
///         （<c>body≈50, armor=0</c>，穿过软甲时还会带 <c>armor≈7</c>）；</item>
///   <item>子弹若被挡下，只有<b>一个</b>事件，并且同时带护甲耐久和一点钝伤
///         （<c>body 0.6~7.4, armor 29~59</c>）。游戏数据库里 26% 的护甲 <c>BluntThroughput = 0</c>，
///         所以挡下时 <c>body</c> 也可能恰好是 0 —— 这时它和「板阶段」长得一模一样，只能靠「有没有后续事件」区分。</item>
/// </list>
/// </summary>
internal static class ArmorHitRules
{
    internal const float Epsilon = 0.01f;

    /// <summary>
    /// 钝伤相对护甲耐久伤害的上限：实测被挡下的最大 <c>body/armor = 0.19</c>，
    /// 击穿软甲的第二段最小 <c>3.8</c>，取 1 落在两者之间的空档里。
    /// 只用于「没有板阶段前置事件」的单个事件，防止只穿过软甲的一发被误当成挡下。
    /// </summary>
    internal const float MaxBluntToArmorRatio = 1f;

    /// <summary>板阶段：只有护甲耐久伤害、身体伤害为 0。可能是击穿的前半段，也可能是零钝伤的挡下。</summary>
    internal static bool IsPlateStage(float body, float armor)
        => armor > Epsilon && body <= Epsilon;

    /// <summary>单个事件里同时带护甲耐久和钝伤，且钝伤明显小于耐久伤害 → 被挡下。</summary>
    internal static bool IsBlockedWithBlunt(float body, float armor)
        => armor > Epsilon && body > Epsilon && body < armor * MaxBluntToArmorRatio;
}

/// <summary>
/// 把「板阶段」事件与它的后续事件合并成一次命中；没有后续事件的板阶段按挡下处理。
///
/// 用法：主线程每个 Update 调一次 <see cref="Process"/>（<b>没有新事件也要调</b>，否则等不到后续事件的板阶段永远出不来）。
/// 一个板阶段事件最多等一个 Update：同一帧或下一帧里同一目标（同一受害者）的下一个<b>有身体伤害</b>的事件就是它的后续。
/// 事件在非主线程入队、主线程成批取出，同一发子弹的两个事件几乎总在同一批里，跨批也能接上。
/// </summary>
internal sealed class PlateStageMerger<T> where T : struct, IArmorSample
{
    private struct Pending
    {
        internal T Sample;
        /// <summary>true = 上一次 Process 就在等了；本次再没等到就按挡下放行。</summary>
        internal bool Carried;
    }

    private readonly List<Pending> _pending = new List<Pending>(4);

    /// <summary>当前还在等后续事件的板阶段数（测试用）。</summary>
    internal int PendingCount => _pending.Count;

    internal void Reset() => _pending.Clear();

    /// <param name="batch">本帧从队列里取出的命中，按到达顺序；可为空。</param>
    /// <param name="output">结果追加到这里（调用方负责事先清空）。</param>
    internal void Process(List<T> batch, List<ResolvedHit<T>> output)
    {
        // 上一轮遗留的都标成 Carried：本轮没配上就放行
        for (int i = 0; i < _pending.Count; i++)
        {
            var p = _pending[i];
            p.Carried = true;
            _pending[i] = p;
        }

        for (int i = 0; i < batch.Count; i++)
        {
            T h = batch[i];
            float body = h.BodyDamage;
            float armor = h.ArmorDamage;

            if (ArmorHitRules.IsPlateStage(body, armor))
            {
                _pending.Add(new Pending { Sample = h, Carried = false });
                continue;
            }

            // 有身体伤害：先看有没有同一目标的板阶段在等（取最老的一条）
            int match = FindPending(h);
            if (match >= 0)
            {
                T plate = _pending[match].Sample;
                _pending.RemoveAt(match);
                output.Add(new ResolvedHit<T>(h, body, plate.ArmorDamage + armor, ArmorOutcome.Penetrated,
                    h.Headshot || plate.Headshot));
                continue;
            }

            ArmorOutcome outcome =
                armor <= ArmorHitRules.Epsilon ? ArmorOutcome.None
                : ArmorHitRules.IsBlockedWithBlunt(body, armor) ? ArmorOutcome.Blocked
                : ArmorOutcome.Penetrated;   // 只穿过软甲：单个事件里身体伤害远大于耐久伤害
            output.Add(new ResolvedHit<T>(h, body, armor, outcome, h.Headshot));
        }

        // 等满一个 Update 还没配上 → 零钝伤的挡下
        for (int i = 0; i < _pending.Count; )
        {
            if (_pending[i].Carried)
            {
                T s = _pending[i].Sample;
                _pending.RemoveAt(i);
                output.Add(new ResolvedHit<T>(s, 0f, s.ArmorDamage, ArmorOutcome.Blocked, s.Headshot));
            }
            else
            {
                i++;
            }
        }
    }

    private int FindPending(T h)
    {
        for (int i = 0; i < _pending.Count; i++)
        {
            T s = _pending[i].Sample;
            if (s.VictimIsLocalPlayer == h.VictimIsLocalPlayer
                && string.Equals(s.TargetKey, h.TargetKey, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }
}
