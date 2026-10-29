using System.Collections.Generic;
using AyaHitmarker.Game;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// 战局小结（0.7.0）：累计口径、快照、文案行，以及“什么时候弹出”的状态机。
/// 状态机的转移表对应 2026-10-25 实机日志的时序：raid ended → 12~17s 结算界面 → 25~33s 菜单可交互；
/// 藏身处也会触发 raid started / ended（所以空局不能覆盖真实小结）。
/// </summary>
internal static class RaidStatsFacts
{
    private static KillFact Fact(string victim = "v", string weapon = "AK-74M", float distance = 10f,
        bool headshot = false, bool boss = false, bool pmc = false, int level = 0)
        => new KillFact(victim, weapon, boss ? "BOSS" : pmc ? "USEC" : "SCAV", level, distance,
            headshot, false, false, false, boss, pmc);

    private static KillAnalysis Kill(KillStreakTracker t, float at, KillFact fact)
        => t.Register(at, fact, KillStreakSettings.Default).WithExperience(55);

    private static RaidSummary SomethingHappened()
    {
        var s = new RaidStats();
        s.NoteHit(40f, 0f);
        return s.Snapshot();
    }

    internal static void Run()
    {
        Assert.Suite("小结: 经验缺失");
        {
            var stats = new RaidStats();
            var tracker = new KillStreakTracker();
            stats.NoteKill(Kill(tracker, 0, Fact()), false, 10);
            stats.NoteKill(Kill(tracker, 1, Fact()).WithExperience(null), false, 10);
            Assert.Equal(55, stats.Snapshot().Score, "保留已知部分供日志对账");
            Assert.Equal(1, stats.Snapshot().MissingExperienceKills, "缺失击杀单独计数");
            Loc.Current = Lang.Zh;
            Assert.True(RaidStats.Rows(stats.Snapshot()).Exists(r => r.Label == "击杀经验" && r.Value == "不可用"), "小结不把已知部分当总数");
            stats.Reset();
            Assert.Equal(0, stats.Snapshot().MissingExperienceKills, "换局清掉缺失状态");
        }
        Assert.Suite("小结: 击杀口径");
        {
            var tracker = new KillStreakTracker();
            var stats = new RaidStats();

            // 0s 普通、1s 爆头 120m、2s 爆头 250m（三连：多杀 + 连杀 3）
            var a1 = Kill(tracker, 0f, Fact(distance: 30f));
            stats.NoteKill(a1, headshot: false, distance: 30f);
            var a2 = Kill(tracker, 1f, Fact(distance: 120f, headshot: true));
            stats.NoteKill(a2, headshot: true, distance: 120f);
            var a3 = Kill(tracker, 2f, Fact(distance: 250f, headshot: true));
            stats.NoteKill(a3, headshot: true, distance: 250f);

            var s = stats.Snapshot();
            Assert.Equal(3, s.Kills, "三杀");
            Assert.Equal(2, s.HeadshotKills, "两个爆头击杀");
            Assert.Near(250f, s.LongestKill, 1e-4, "最远一杀 250m（不是最后一杀就是最大值）");
            Assert.Equal(3, s.BestStreak, "最高连杀 3");
            Assert.Equal(165, s.Score, "得分 = 每一杀经验之和");
            Assert.True(s.HasActivity, "有击杀 → 有活动");

            // 最远一杀不会被更近的覆盖
            var a4 = Kill(tracker, 100f, Fact(distance: 5f));
            stats.NoteKill(a4, false, 5f);
            Assert.Near(250f, stats.Snapshot().LongestKill, 1e-4, "之后的近距离击杀不拉低最远记录");
            Assert.Equal(4, stats.Snapshot().Kills, "第四杀计入");
            Assert.Equal(3, stats.Snapshot().BestStreak, "连杀断了之后最高连杀仍是 3");
        }

        Assert.Suite("小结: 伤害 / 受击 / 助攻 / 阵亡");
        {
            var stats = new RaidStats();
            stats.NoteHit(40f, 0f);
            stats.NoteHit(0f, 52f);       // 打在防弹板上：身体 0，护甲 52
            stats.NoteHit(30f, 7.5f);
            stats.NoteHit(-5f, -3f);      // 异常数据不能把累计拉成负数
            var s = stats.Snapshot();
            Assert.Near(70f, s.BodyDamage, 1e-4, "造成伤害只累计身体伤害");
            Assert.Near(59.5f, s.ArmorDamage, 1e-4, "护甲耐久伤害单列");

            stats.NoteTaken(12f);
            stats.NoteTaken(0f);          // 被护甲完全吸收的一下也算被打了一次
            stats.NoteAssist();
            stats.NoteAssist();
            stats.NoteDeath();
            s = stats.Snapshot();
            Assert.Equal(2, s.HitsTaken, "受击 2 次");
            Assert.Near(12f, s.DamageTaken, 1e-4, "承受伤害 12");
            Assert.Equal(2, s.Assists, "助攻 2 次");
            Assert.Equal(1, s.Deaths, "阵亡 1 次");
        }

        Assert.Suite("小结: 有没有活动 / 快照互不影响 / Reset");
        {
            var stats = new RaidStats();
            Assert.False(stats.Snapshot().HasActivity, "什么都没发生 → 没有活动（藏身处的空局）");

            stats.NoteHit(0f, 30f);
            Assert.True(stats.Snapshot().HasActivity, "只打了护甲也算有活动");

            var first = stats.Snapshot();
            stats.NoteHit(100f, 0f);
            Assert.Near(0f, first.BodyDamage, 1e-6, "已取走的快照不随后续累计变化");
            Assert.Near(100f, stats.Snapshot().BodyDamage, 1e-4, "新快照包含后续累计");

            stats.Reset();
            var cleared = stats.Snapshot();
            Assert.False(cleared.HasActivity, "Reset 后清空");
            Assert.Equal(0, cleared.Achievements.Length, "Reset 后成就也清空");

            var onlyTaken = new RaidStats();
            onlyTaken.NoteTaken(5f);
            Assert.True(onlyTaken.Snapshot().HasActivity, "只被打过也算有活动");
        }

        Assert.Suite("小结: 成就计数与排序");
        {
            var tracker = new KillStreakTracker();
            var stats = new RaidStats();

            // 先锋(Good) → 100s 后爆头(Normal) ×2 → 再 100s 后：首领猎杀者(Epic)
            stats.NoteKill(Kill(tracker, 0f, Fact()), false, 10f);
            stats.NoteKill(Kill(tracker, 100f, Fact(headshot: true)), true, 10f);
            stats.NoteKill(Kill(tracker, 200f, Fact(headshot: true)), true, 10f);
            stats.NoteKill(Kill(tracker, 300f, Fact(boss: true)), false, 10f);
            stats.NoteKill(Kill(tracker, 400f, Fact()), false, 10f);   // 普通击杀：没有成就

            var s = stats.Snapshot();
            Assert.Equal(3, s.Achievements.Length, "三种成就：先锋 / 爆头 / 首领猎杀者");
            Assert.Equal(KillAchievementKind.BossSlayer, s.Achievements[0].Kind, "档次最高的排最前（Epic）");
            Assert.Equal(KillAchievementKind.FirstBlood, s.Achievements[1].Kind, "其次 Good");
            Assert.Equal(KillAchievementKind.Headshot, s.Achievements[2].Kind, "Normal 最后");
            Assert.Equal(2, s.Achievements[2].Count, "爆头成就拿了两次");
            Assert.Equal(1, s.Achievements[0].Count, "首领猎杀者一次");
        }

        Assert.Suite("小结: 文案行（中文）");
        {
            var before = Loc.Current;
            try
            {
                Loc.Current = Lang.Zh;

                var minimal = new RaidSummary { Kills = 1 };
                var rows = RaidStats.Rows(minimal);
                Assert.Equal(1, rows.Count, "只有 1 个击杀时只显示「击杀」一行");
                Assert.Equal("击杀", rows[0].Label, "标签");
                Assert.Equal("1", rows[0].Value, "数值");
                Assert.Equal("战局小结", RaidStats.Title(), "标题");

                var full = new RaidSummary
                {
                    Kills = 12, HeadshotKills = 4, LongestKill = 187.4f, BestStreak = 5, Score = 1850,
                    BodyDamage = 1342.3f, Assists = 2, DamageTaken = 123.6f, Deaths = 1,
                    Achievements = new[]
                    {
                        new SummaryAchievement(KillAchievementKind.MultiKill, KillAchievementTier.Epic, "四杀", 1),
                        new SummaryAchievement(KillAchievementKind.Headshot, KillAchievementTier.Normal, "爆头", 3),
                    },
                };
                rows = RaidStats.Rows(full);
                var labels = new List<string>();
                foreach (var r in rows) labels.Add(r.Label);
                Assert.Equal("击杀,爆头击杀,最远击杀,最高连杀,击杀经验,造成伤害,助攻,承受伤害,阵亡,,四杀,爆头",
                    string.Join(",", labels), "行顺序：统计 → 分隔 → 成就");
                Assert.Equal("187 m", rows[2].Value, "最远击杀取整并带单位");
                Assert.Equal("1342", rows[5].Value, "伤害取整");
                Assert.Equal(SummaryRowKind.Divider, rows[9].Kind, "统计区与成就区之间有分隔行");
                Assert.Equal("×3", rows[11].Value, "成就次数");
                Assert.Equal(SummaryRowKind.Achievement, rows[11].Kind, "成就行的类型");

                // 成就行数上限
                var many = new SummaryAchievement[6];
                for (int i = 0; i < many.Length; i++)
                    many[i] = new SummaryAchievement(KillAchievementKind.Headshot, KillAchievementTier.Normal, "成就" + i, 1);
                var capped = RaidStats.Rows(new RaidSummary { Kills = 1, Achievements = many }, maxAchievementRows: 3);
                int achRows = 0;
                foreach (var r in capped) if (r.Kind == SummaryRowKind.Achievement) achRows++;
                Assert.Equal(3, achRows, "成就行数受上限约束");
                var none = RaidStats.Rows(new RaidSummary { Kills = 1, Achievements = many }, maxAchievementRows: 0);
                Assert.Equal(1, none.Count, "上限为 0：没有分隔线也没有成就行");
            }
            finally
            {
                Loc.Current = before;
            }
        }

        Assert.Suite("小结: 文案行（英文，不含 CJK）");
        {
            var before = Loc.Current;
            try
            {
                Loc.Current = Lang.En;
                var full = new RaidSummary
                {
                    Kills = 12, HeadshotKills = 4, LongestKill = 187.4f, BestStreak = 5, Score = 1850,
                    BodyDamage = 1342.3f, Assists = 2, DamageTaken = 123.6f, Deaths = 1,
                    Achievements = new[] { new SummaryAchievement(KillAchievementKind.MultiKill, KillAchievementTier.Epic, "Quad Kill", 1) },
                };
                var rows = RaidStats.Rows(full);
                bool anyCjk = Loc.ContainsCjk(RaidStats.Title());
                foreach (var r in rows) anyCjk |= Loc.ContainsCjk(r.Label) || Loc.ContainsCjk(r.Value);
                Assert.False(anyCjk, "英文模式下标题 / 标签 / 数值都不含 CJK");
                Assert.Equal("Raid Summary", RaidStats.Title(), "标题");
                Assert.Equal("Kills", rows[0].Label, "第一行");
                Assert.Equal("Headshot Kills", rows[1].Label, "第二行");
                Assert.Equal("Damage Dealt", rows[5].Label, "造成伤害");
            }
            finally
            {
                Loc.Current = before;
            }
        }

        Assert.Suite("小结调度: 正常流程（战局结束 → 菜单出现 → 等延迟 → 显示）");
        {
            var sch = new SummaryScheduler();
            Assert.False(sch.IsWaiting, "初始空闲");

            sch.OnRaidEnded(100f, SomethingHappened());
            Assert.True(sch.IsWaiting, "战局结束后开始等待");
            Assert.Equal(SummaryScheduler.Phase.Pending, sch.State, "挂起中");

            // 结算界面期间菜单场景还没加载：一直等
            Assert.Equal(SummaryAction.None, sch.Tick(105f, false, menuLoaded: false, delay: 3f), "战局刚结束，菜单未加载");
            Assert.Equal(SummaryAction.None, sch.Tick(115f, false, menuLoaded: false, delay: 3f), "结算界面期间，菜单仍未加载");
            Assert.Equal(SummaryScheduler.Phase.Pending, sch.State, "仍在挂起");

            // 菜单场景出现（上升沿）→ 进入延迟
            Assert.Equal(SummaryAction.None, sch.Tick(130f, false, menuLoaded: true, delay: 3f), "菜单加载：先不显示，等延迟");
            Assert.Equal(SummaryScheduler.Phase.Armed, sch.State, "已就绪");
            Assert.Equal(SummaryAction.None, sch.Tick(132f, false, true, 3f), "延迟没到");
            Assert.Equal(SummaryAction.Show, sch.Tick(133.5f, false, true, 3f), "延迟到了 → 显示");

            var shown = sch.Take();
            Assert.True(shown != null && shown.Kills == 0 && shown.BodyDamage > 0f, "取走的就是那份小结");
            Assert.False(sch.IsWaiting, "显示后回到空闲");
            Assert.Equal(SummaryAction.None, sch.Tick(140f, false, true, 3f), "不会重复显示");
            Assert.True(sch.Take() == null, "小结已被取走");
        }

        Assert.Suite("小结调度: 空局不弹、不覆盖、可取消");
        {
            // 藏身处的空局
            var sch = new SummaryScheduler();
            sch.OnRaidEnded(10f, new RaidStats().Snapshot());
            Assert.False(sch.IsWaiting, "没有任何活动的局不挂起");

            // 已经挂着真实小结，又来一个空局（进出藏身处）：不能覆盖
            var real = SomethingHappened();
            sch.OnRaidEnded(100f, real);
            sch.OnRaidEnded(105f, new RaidStats().Snapshot());
            Assert.True(ReferenceEquals(real, sch.Current), "空局不覆盖已挂起的真实小结");
            Assert.True(sch.IsWaiting, "仍在等待");

            // 新战局开始 → 取消
            sch.OnRaidStarted();
            Assert.False(sch.IsWaiting, "新战局开始取消挂起的小结");
            Assert.True(sch.Current == null, "并清空");

            // 已就绪（Armed）时开始新战局，同样取消
            sch.OnRaidEnded(200f, SomethingHappened());
            sch.Tick(210f, false, false, 3f);
            sch.Tick(220f, false, true, 3f);
            Assert.Equal(SummaryScheduler.Phase.Armed, sch.State, "进入 Armed");
            sch.OnRaidStarted();
            Assert.Equal(SummaryAction.None, sch.Tick(300f, false, true, 3f), "取消后不再显示");

            // 新的真实战局小结替换旧的
            var older = SomethingHappened();
            var newer = SomethingHappened();
            sch.OnRaidEnded(400f, older);
            sch.OnRaidEnded(500f, newer);
            Assert.True(ReferenceEquals(newer, sch.Current), "更晚的真实小结替换更早的");

            // 在战局里不推进
            var inRaid = new SummaryScheduler();
            inRaid.OnRaidEnded(0f, SomethingHappened());
            Assert.Equal(SummaryAction.None, inRaid.Tick(50f, inRaid: true, menuLoaded: true, delay: 0f), "inRaid=true 不推进");
            Assert.Equal(SummaryScheduler.Phase.Pending, inRaid.State, "状态不变");
        }

        Assert.Suite("小结调度: 兜底 / 过期 / 零延迟");
        {
            // 菜单场景在战局中其实没卸载：上升沿永远等不到，靠兜底放行
            var sch = new SummaryScheduler();
            sch.OnRaidEnded(0f, SomethingHappened());
            Assert.Equal(SummaryAction.None, sch.Tick(10f, false, menuLoaded: true, delay: 3f), "菜单一直在、时间还早：不放行");
            Assert.Equal(SummaryScheduler.Phase.Pending, sch.State, "仍在挂起");
            Assert.Equal(SummaryAction.None, sch.Tick(39f, false, true, 3f), "39s 仍不放行");
            Assert.Equal(SummaryAction.None, sch.Tick(40f, false, true, 3f), "40s：兜底放行进入 Armed");
            Assert.Equal(SummaryScheduler.Phase.Armed, sch.State, "兜底后就绪");
            Assert.Equal(SummaryAction.Show, sch.Tick(43.1f, false, true, 3f), "再等延迟后显示");

            // 过期：一直没进菜单
            var gone = new SummaryScheduler();
            gone.OnRaidEnded(0f, SomethingHappened());
            Assert.Equal(SummaryAction.None, gone.Tick(299f, false, false, 3f), "299s 还在等");
            Assert.True(gone.IsWaiting, "未过期");
            Assert.Equal(SummaryAction.None, gone.Tick(301f, false, false, 3f), "超过 300s 丢弃");
            Assert.False(gone.IsWaiting, "过期后回到空闲");
            Assert.True(gone.Current == null, "过期后清空");

            // 零延迟：就绪后的下一次 Tick 立即显示
            var instant = new SummaryScheduler();
            instant.OnRaidEnded(0f, SomethingHappened());
            instant.Tick(1f, false, false, 0f);
            Assert.Equal(SummaryAction.None, instant.Tick(2f, false, true, 0f), "菜单出现的这一帧只做就绪");
            Assert.Equal(SummaryAction.Show, instant.Tick(2f, false, true, 0f), "零延迟：下一次 Tick 就显示");
        }
    }
}
