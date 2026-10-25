using System.Collections.Generic;
using AyaHitmarker.Game;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// 护甲命中的合并与判据（0.6.1）。
///
/// 数据来自 2026-10-25 实机日志（SPT 离线战局，MCX .300 BLK 打各种有甲 bot，共 40 条 <c>OnBeingHit</c>）：
/// 一发击穿护甲的子弹 = 「板阶段」（body=0, armor≈52）+ 紧跟着的身体事件；被挡下的子弹 = 单个事件（armor 29~59，body 0.6~7.4）。
/// 末尾的整局回放把这 40 条原样喂进合并器，锁死「7 次击穿 / 7 次挡下 / 19 次无护甲」。
/// </summary>
internal static class ArmorHitMergerFacts
{
    private readonly struct Sample : IArmorSample
    {
        public string TargetKey { get; }
        public bool VictimIsLocalPlayer { get; }
        public float BodyDamage { get; }
        public float ArmorDamage { get; }

        internal Sample(string key, float body, float armor, bool self = false)
        {
            TargetKey = key;
            VictimIsLocalPlayer = self;
            BodyDamage = body;
            ArmorDamage = armor;
        }
    }

    private static List<ResolvedHit<Sample>> Run1(PlateStageMerger<Sample> m, params Sample[] batch)
    {
        var output = new List<ResolvedHit<Sample>>();
        m.Process(new List<Sample>(batch), output);
        return output;
    }

    internal static void Run()
    {
        Assert.Suite("护甲判据: 板阶段 / 挡下带钝伤");
        {
            Assert.True(ArmorHitRules.IsPlateStage(0f, 51.95f), "body=0 且 armor>0 → 板阶段");
            Assert.False(ArmorHitRules.IsPlateStage(3.48f, 58.72f), "带钝伤的不是板阶段");
            Assert.False(ArmorHitRules.IsPlateStage(0f, 0f), "0/0（鞭尸）不是板阶段");
            Assert.False(ArmorHitRules.IsPlateStage(49.63f, 0f), "纯身体伤害不是板阶段");
            Assert.False(ArmorHitRules.IsPlateStage(0.005f, 0.005f), "低于 epsilon 的噪声不算");

            // 实测的 7 个挡下事件：body/armor 最大 0.185
            Assert.True(ArmorHitRules.IsBlockedWithBlunt(3.48f, 58.72f), "实测挡下 3.48/58.72");
            Assert.True(ArmorHitRules.IsBlockedWithBlunt(6.58f, 46.36f), "实测挡下 6.58/46.36");
            Assert.True(ArmorHitRules.IsBlockedWithBlunt(7.39f, 40.01f), "实测挡下 7.39/40.01（比值最高 0.185）");
            Assert.True(ArmorHitRules.IsBlockedWithBlunt(0.63f, 45.48f), "实测挡下 0.63/45.48");
            Assert.True(ArmorHitRules.IsBlockedWithBlunt(2.95f, 28.94f), "实测挡下 2.95/28.94（耐久伤害最小）");
            // 击穿软甲的第二段：body/armor 最小 3.8
            Assert.False(ArmorHitRules.IsBlockedWithBlunt(29.7f, 7.74f), "击穿后的第二段 29.7/7.74 不是挡下");
            Assert.False(ArmorHitRules.IsBlockedWithBlunt(30.89f, 7.25f), "击穿后的第二段 30.89/7.25 不是挡下");
            Assert.False(ArmorHitRules.IsBlockedWithBlunt(49.63f, 0f), "没有护甲伤害不是挡下");
        }

        Assert.Suite("板阶段合并: 同一批 / 跨批 / 放行");
        {
            // 击穿：板阶段 + 紧随的身体事件（日志 7717/7719：armor 51.95 → body 49.63）
            var m = new PlateStageMerger<Sample>();
            var o = Run1(m, new Sample("D", 0f, 51.95f), new Sample("D", 49.63f, 0f));
            Assert.Equal(1, o.Count, "一发子弹的两个事件合并成一次命中");
            Assert.Equal(ArmorOutcome.Penetrated, o[0].Outcome, "合并后 = 击穿");
            Assert.Near(49.63f, o[0].Body, 1e-4, "身体伤害取后续事件");
            Assert.Near(51.95f, o[0].Armor, 1e-4, "护甲耐久伤害取板阶段");
            Assert.Near(49.63f, o[0].Primary.BodyDamage, 1e-4, "Primary 是后续事件（命中点等字段取它）");
            Assert.Equal(0, m.PendingCount, "没有残留等待");

            // 击穿软甲：板阶段 + (29.7, 7.74) → 护甲伤害相加
            var m2 = new PlateStageMerger<Sample>();
            var o2 = Run1(m2, new Sample("X", 0f, 53.6f), new Sample("X", 29.7f, 7.74f));
            Assert.Equal(1, o2.Count, "板阶段 + 带软甲耐久的身体事件也合并");
            Assert.Near(61.34f, o2[0].Armor, 1e-3, "两段护甲耐久伤害相加");
            Assert.Near(29.7f, o2[0].Body, 1e-4, "身体伤害 29.7");
            Assert.Equal(ArmorOutcome.Penetrated, o2[0].Outcome, "击穿");

            // 跨批：板阶段这一批先到，身体事件下一批到
            var m3 = new PlateStageMerger<Sample>();
            var o3a = Run1(m3, new Sample("D", 0f, 52.01f));
            Assert.Equal(0, o3a.Count, "板阶段先等一帧，不立刻出");
            Assert.Equal(1, m3.PendingCount, "挂起 1 个");
            var o3b = Run1(m3, new Sample("D", 51.44f, 0f));
            Assert.Equal(1, o3b.Count, "下一帧身体事件到了 → 合并");
            Assert.Equal(ArmorOutcome.Penetrated, o3b[0].Outcome, "跨批合并也是击穿");
            Assert.Equal(0, m3.PendingCount, "没有残留");

            // 没有后续事件：等满一个 Update 后按挡下放行（零钝伤的挡下）
            var m4 = new PlateStageMerger<Sample>();
            Assert.Equal(0, Run1(m4, new Sample("T", 0f, 40f)).Count, "第一帧还在等");
            var o4 = Run1(m4);
            Assert.Equal(1, o4.Count, "第二帧（空批）放行");
            Assert.Equal(ArmorOutcome.Blocked, o4[0].Outcome, "无后续 = 挡下");
            Assert.Near(0f, o4[0].Body, 1e-6, "身体伤害 0");
            Assert.Near(40f, o4[0].Armor, 1e-6, "护甲伤害保留");
            Assert.Equal(0, m4.PendingCount, "放行后清空");
            Assert.Equal(0, Run1(m4).Count, "不会重复放行");
        }

        Assert.Suite("板阶段合并: 单个事件的分类");
        {
            var m = new PlateStageMerger<Sample>();

            var blocked = Run1(m, new Sample("A", 3.48f, 58.72f, self: true));
            Assert.Equal(ArmorOutcome.Blocked, blocked[0].Outcome, "带钝伤的单个事件 = 挡下（立刻出，不等）");
            Assert.Near(3.48f, blocked[0].Body, 1e-4, "钝伤原样保留");
            Assert.Near(58.72f, blocked[0].Armor, 1e-4, "护甲伤害原样保留");

            var plain = Run1(m, new Sample("A", 49.63f, 0f));
            Assert.Equal(ArmorOutcome.None, plain[0].Outcome, "纯身体伤害 = 无护甲");

            var softPen = Run1(m, new Sample("A", 30f, 7f));
            Assert.Equal(ArmorOutcome.Penetrated, softPen[0].Outcome, "单个事件里身体远大于耐久（只穿软甲）= 击穿，不是挡下");
        }

        Assert.Suite("板阶段合并: 多目标 / 连发 / 受害者区分");
        {
            // 板阶段挂着时，别的目标的身体事件不能把它吃掉
            var m = new PlateStageMerger<Sample>();
            var o = Run1(m, new Sample("A", 0f, 50f), new Sample("B", 40f, 0f));
            Assert.Equal(1, o.Count, "B 的命中立即出，A 的板阶段还在等");
            Assert.Equal(ArmorOutcome.None, o[0].Outcome, "B 无护甲");
            Assert.Equal(1, m.PendingCount, "A 仍挂着");
            var flushed = Run1(m);
            Assert.Equal(1, flushed.Count, "A 等满一帧放行");
            Assert.Equal(ArmorOutcome.Blocked, flushed[0].Outcome, "A = 挡下");

            // 同一个人但受害者身份不同（我挨打 vs 我打别人用同一个 key 的极端情况）不合并
            var m2 = new PlateStageMerger<Sample>();
            var o2 = Run1(m2, new Sample("K", 0f, 50f, self: true), new Sample("K", 40f, 0f, self: false));
            Assert.Equal(1, o2.Count, "VictimIsLocalPlayer 不同 → 不合并");
            Assert.Equal(ArmorOutcome.None, o2[0].Outcome, "后者是普通命中");
            Assert.Equal(1, m2.PendingCount, "前者仍在等");

            // 连发：两个板阶段 + 两个身体事件，同一目标 → 各自合并成一次
            var m3 = new PlateStageMerger<Sample>();
            var o3 = Run1(m3,
                new Sample("D", 0f, 52f), new Sample("D", 0f, 53f),
                new Sample("D", 49f, 0f), new Sample("D", 50f, 0f));
            Assert.Equal(2, o3.Count, "两发 = 两次命中");
            Assert.Equal(ArmorOutcome.Penetrated, o3[0].Outcome, "第一发击穿");
            Assert.Equal(ArmorOutcome.Penetrated, o3[1].Outcome, "第二发击穿");
            Assert.Near(52f, o3[0].Armor, 1e-4, "按先后配对：最老的板阶段先配");
            Assert.Near(53f, o3[1].Armor, 1e-4, "第二个板阶段配第二个身体事件");
            Assert.Equal(0, m3.PendingCount, "没有残留");

            // 击杀夹在两个事件之间（日志 8108 → kill → 8113）不影响合并：合并器只看命中队列
            var m4 = new PlateStageMerger<Sample>();
            var o4 = Run1(m4, new Sample("T", 0f, 52.91f), new Sample("T", 36.64f, 0f));
            Assert.Equal(ArmorOutcome.Penetrated, o4[0].Outcome, "致死一枪也是击穿");

            // Reset 丢弃挂起项（换局 / 脚本重置）
            var m5 = new PlateStageMerger<Sample>();
            Run1(m5, new Sample("Z", 0f, 30f));
            m5.Reset();
            Assert.Equal(0, m5.PendingCount, "Reset 清空");
            Assert.Equal(0, Run1(m5).Count, "Reset 后不会再放行旧的板阶段");
        }

        Assert.Suite("板阶段合并: 2026-10-25 整局回放（40 条真实命中）");
        {
            // (目标, 身体, 护甲, 我是受害者)；顺序与日志一致
            var replay = new (string Key, float Body, float Armor, bool Self)[]
            {
                ("S", 40f, 0f, false),
                ("D", 0f, 51.95f, false), ("D", 49.63f, 0f, false),
                ("me", 3.48f, 58.72f, true), ("me", 3.48f, 58.7f, true),
                ("D", 0f, 52.01f, false), ("D", 51.44f, 0f, false),
                ("D", 51.92f, 0f, false), ("D", 8.74f, 0f, false), ("D", 4.36f, 0f, false),
                ("D", 50.09f, 0f, false), ("D", 30f, 0f, false),
                ("T", 6.58f, 46.36f, false),
                ("me", 6.39f, 0f, true), ("me", 6.39f, 0f, true),
                ("T", 0f, 52.96f, false), ("T", 36.19f, 0f, false),
                ("me", 58.76f, 0f, true),
                ("T", 0f, 52.91f, false), ("T", 36.64f, 0f, false),
                ("B", 53.13f, 0f, false), ("B", 7.39f, 40.01f, false),
                ("X", 53.48f, 0f, false),
                ("X", 0f, 53.6f, false), ("X", 29.7f, 7.74f, false),
                ("X", 0f, 53.65f, false), ("X", 30.89f, 7.25f, false),
                ("X", 53.61f, 0f, false), ("X", 0.63f, 45.48f, false),
                ("W", 0f, 60.88f, false), ("W", 41.83f, 0f, false),
                ("V", 59.09f, 0f, false), ("V", 59.18f, 0f, false), ("V", 60.19f, 0f, false),
                ("V", 58.67f, 0f, false), ("V", 62.48f, 0f, false), ("V", 62.56f, 0f, false),
                ("V", 44.13f, 0f, false),
                ("me", 4.48f, 44.82f, true), ("me", 2.95f, 28.94f, true),
            };
            Assert.Equal(40, replay.Length, "回放数据 = 40 条");

            for (int mode = 0; mode < 2; mode++)
            {
                // mode 0：所有事件塞进同一批；mode 1：每个事件各占一帧（最坏情况：同一发子弹的两个事件跨批）
                var merger = new PlateStageMerger<Sample>();
                var output = new List<ResolvedHit<Sample>>();
                var batch = new List<Sample>();

                if (mode == 0)
                {
                    foreach (var r in replay) batch.Add(new Sample(r.Key, r.Body, r.Armor, r.Self));
                    merger.Process(batch, output);
                    batch.Clear();
                    merger.Process(batch, output);   // 空批放行遗留的板阶段
                }
                else
                {
                    foreach (var r in replay)
                    {
                        batch.Clear();
                        batch.Add(new Sample(r.Key, r.Body, r.Armor, r.Self));
                        merger.Process(batch, output);
                    }
                    batch.Clear();
                    merger.Process(batch, output);
                    merger.Process(batch, output);
                }

                int pen = 0, blk = 0, none = 0;
                float bodySum = 0f;
                foreach (var h in output)
                {
                    if (h.Outcome == ArmorOutcome.Penetrated) pen++;
                    else if (h.Outcome == ArmorOutcome.Blocked) blk++;
                    else none++;
                    bodySum += h.Body;
                }

                string tag = mode == 0 ? "同批" : "逐帧";
                Assert.Equal(33, output.Count, tag + ": 40 条事件 → 33 次命中（7 个板阶段并进了各自的后续事件）");
                Assert.Equal(7, pen, tag + ": 7 次击穿（7 个板阶段全部有后续事件）");
                Assert.Equal(7, blk, tag + ": 7 次挡下（单个事件、钝伤 0.6~7.4、耐久 29~59；其中 4 次是我挨的）");
                Assert.Equal(19, none, tag + ": 19 次无护甲");
                Assert.Equal(0, merger.PendingCount, tag + ": 回放结束没有残留");

                // 身体伤害总和不受合并影响（板阶段 body=0）：从原始数据直接求和对账
                float expected = 0f;
                foreach (var r in replay) expected += r.Body;
                Assert.Near(expected, bodySum, 1e-2, tag + ": 合并前后身体伤害总和一致");
            }
        }
    }
}
