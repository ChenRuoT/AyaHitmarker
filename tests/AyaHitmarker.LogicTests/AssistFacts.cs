using AyaHitmarker.Game;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// 0.6 c 助攻判定：窗口内命中记账、别人击杀时结算一次、超时 / 已结算 / 空 key 不重复。
/// </summary>
internal static class AssistFacts
{
    internal static void Run()
    {
        Assert.Suite("助攻：命中记账与窗口结算");

        var t = new AssistTracker();
        Assert.Equal(0, t.Count, "初始为空");
        Assert.False(t.TryTakeAssist("p1", 1f, 8f, out _, out _), "没打过的目标不产生助攻");

        // 普通：命中后窗口内被队友打死 → 助攻，伤害累计
        t.NoteHit("p1", 10f, 30f, headshot: false);
        t.NoteHit("p1", 11f, 45f, headshot: true);
        Assert.Equal(1, t.Count, "同一目标只保留一个条目");

        Assert.True(t.TryTakeAssist("p1", 15f, 8f, out float damage, out bool headshot), "窗口内可以结算助攻");
        Assert.Near(75f, damage, 1e-3, "伤害累计 30 + 45");
        Assert.True(headshot, "任一次爆头 → 助攻算爆头");
        Assert.Equal(0, t.Count, "结算后移除目标（一次死亡只算一次）");
        Assert.False(t.TryTakeAssist("p1", 15f, 8f, out _, out _), "同一目标不会重复助攻");

        // 超时：最后命中在窗口之外 → 不算
        t.NoteHit("p2", 100f, 88f, headshot: false);
        Assert.False(t.TryTakeAssist("p2", 109f, 8f, out _, out _), "超过窗口不产生助攻");
        Assert.Equal(0, t.Count, "超时的目标也会被移除（避免下次误判）");

        // 窗口边界：刚好等于窗口算助攻（判定用 >，不用 >=）
        t.NoteHit("p3", 200f, 10f, headshot: false);
        Assert.True(t.TryTakeAssist("p3", 208f, 8f, out _, out _), "刚好等于窗口仍算助攻");

        // 多次命中刷新时间：最后一次命中在窗口内就算
        t.NoteHit("p4", 300f, 10f, headshot: false);
        t.NoteHit("p4", 305f, 10f, headshot: false);
        Assert.True(t.TryTakeAssist("p4", 312f, 8f, out float d4, out _), "最后一次命中在窗口内 → 助攻");
        Assert.Near(20f, d4, 1e-3, "两次命中伤害累计");

        // 空 key / 负数伤害 / 窗口 <= 0
        t.NoteHit(null, 400f, 100f, headshot: false);
        t.NoteHit(string.Empty, 400f, 100f, headshot: false);
        Assert.Equal(0, t.Count, "空 key 不记账");
        t.NoteHit("p5", 400f, -50f, headshot: false);
        Assert.True(t.TryTakeAssist("p5", 401f, 8f, out float d5, out _), "负伤害仍可结算");
        Assert.Near(0f, d5, 1e-3, "负伤害按 0 累计");
        t.NoteHit("p6", 500f, 10f, headshot: false);
        Assert.False(t.TryTakeAssist("p6", 500f, 0f, out _, out _), "窗口 <= 0 不产生助攻");

        // Reset：跨局清空
        t.NoteHit("p7", 600f, 10f, headshot: false);
        t.Reset();
        Assert.Equal(0, t.Count, "Reset 后为空");
        Assert.False(t.TryTakeAssist("p7", 601f, 8f, out _, out _), "Reset 后旧命中不会产生助攻");

        // 多个目标互不影响
        t.NoteHit("a", 700f, 10f, headshot: false);
        t.NoteHit("b", 701f, 20f, headshot: false);
        Assert.True(t.TryTakeAssist("b", 702f, 8f, out float db, out _), "按目标 key 精确匹配");
        Assert.Near(20f, db, 1e-3, "只结算 b 的伤害");
        Assert.True(t.TryTakeAssist("a", 703f, 8f, out float da, out _), "另一个目标仍保留");
        Assert.Near(10f, da, 1e-3, "a 的伤害独立");
    }
}
