using AyaHitmarker.Game;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// 连杀 / 成就判定引擎的行为契约。
/// 这些是「游戏里应该看到哪条缎带」的可执行规格 —— 改判定逻辑时先让这里说话。
/// </summary>
internal static class KillStreakFacts
{
    // ------------------------------------------------------------------ 构造辅助

    private static KillStreakSettings Settings(
        float revengeWindow = 120f,
        float multiKillWindow = 2.5f,
        float streakResetWindow = 10f,
        float gunnerWindow = 4f,
        float marauderWindow = 8f,
        float longShot = 100f,
        float marksman = 200f,
        int eliteLevel = 40)
        => new KillStreakSettings(
            revengeWindow,
            multiKillWindow,
            streakResetWindow,
            gunnerWindow,
            marauderWindow,
            longShot,
            marksman,
            eliteLevel,
            scoreBase: 100,
            scoreHeadshotBonus: 50,
            scoreLongShotBonus: 50,
            scoreRevengeBonus: 25,
            scoreMultiKillBonus: 25,
            scoreBossBonus: 200);

    private static KillFact Fact(
        string victim = "victim",
        string weapon = "AK-74M",
        string botType = "SCAV",
        int level = 0,
        float distance = 10f,
        bool headshot = false,
        bool melee = false,
        bool explosive = false,
        bool sniper = false,
        bool boss = false,
        bool pmc = false)
        => new KillFact(
            victim,
            weapon,
            botType,
            level,
            distance,
            headshot,
            melee,
            explosive,
            sniper,
            boss,
            pmc);

    // ------------------------------------------------------------------ 用例

    internal static void Run()
    {
        Assert.Suite("KillStreak: 基础计数");
        {
            var t = new KillStreakTracker();
            KillAnalysis a = t.Register(0f, Fact(), Settings());
            Assert.Equal("先锋", a.Ribbon.Title, "本局第一杀 → 先锋");
            Assert.Equal(KillAchievementKind.FirstBlood, a.Ribbon.Kind, "先锋的 Kind");
            Assert.Equal(1, a.Streak, "第一杀 streak=1");
            Assert.Equal(1, a.MultiCount, "第一杀 multiCount=1");
            Assert.Equal(1, a.RaidKills, "第一杀 raidKills=1");
            Assert.Equal(100, a.Score, "基础得分 100");

            // 间隔 100 秒 > 连杀重置窗口 10 秒 → 连杀归 1，且没有任何成就
            KillAnalysis b = t.Register(100f, Fact(), Settings());
            Assert.Equal(2, b.RaidKills, "第二杀 raidKills=2");
            Assert.Equal(1, b.Streak, "超时后 streak 归 1");
            Assert.False(b.Ribbon.HasTitle, "普通击杀不画缎带");
        }

        Assert.Suite("KillStreak: 多杀");
        {
            var t = new KillStreakTracker();
            var s = Settings(multiKillWindow: 2.5f);

            t.Register(0f, Fact(), s);
            KillAnalysis two = t.Register(1f, Fact(), s);
            Assert.Equal("双杀", two.Ribbon.Title, "1 秒内第二杀 → 双杀");
            Assert.Equal(2, two.MultiCount, "multiCount=2");
            Assert.Equal(125, two.Score, "双杀得分 = 100 + 25");

            KillAnalysis three = t.Register(1.5f, Fact(), s);
            Assert.Equal("三杀", three.Ribbon.Title, "1.5 秒内第三杀 → 三杀");
            Assert.Equal(KillAchievementTier.Great, three.Ribbon.Tier, "三杀是 Great 档");

            KillAnalysis four = t.Register(2f, Fact(), s);
            Assert.Equal("四杀", four.Ribbon.Title, "第四杀 → 四杀");
            Assert.Equal(KillAchievementTier.Epic, four.Ribbon.Tier, "四杀是 Epic 档");
            Assert.Equal(175, four.Score, "四杀得分 = 100 + 25*3");

            KillAnalysis six = t.Register(2.5f, Fact(), s);
            Assert.Equal("五杀", six.Ribbon.Title, "第五杀 → 五杀");
            KillAnalysis seven = t.Register(2.6f, Fact(), s);
            Assert.Equal("多杀 x6", seven.Ribbon.Title, "第六杀 → 多杀 xN");
        }

        Assert.Suite("KillStreak: 复仇者");
        {
            var t = new KillStreakTracker();
            var s = Settings(revengeWindow: 120f);
            t.NoteLocalDeath(0f, "nikita");
            KillAnalysis a = t.Register(10f, Fact(victim: "nikita"), s);
            Assert.Equal("复仇者", a.Ribbon.Title, "击杀刚刚杀死我的人 → 复仇者");
            Assert.Equal(KillAchievementKind.Revenge, a.Ribbon.Kind, "复仇者 Kind");
            Assert.Equal(KillAchievementTier.Epic, a.Ribbon.Tier, "复仇者是 Epic 档");
            Assert.Equal(125, a.Score, "复仇得分 = 100 + 25");

            // 复仇只能兑现一次
            KillAnalysis b = t.Register(11f, Fact(victim: "nikita"), s);
            Assert.NotEqual("复仇者", b.Ribbon.Title, "同一个凶手不重复给复仇者");

            // 窗口外不算复仇
            var t2 = new KillStreakTracker();
            t2.NoteLocalDeath(0f, "nikita");
            KillAnalysis late = t2.Register(200f, Fact(victim: "nikita"), Settings(revengeWindow: 120f));
            Assert.NotEqual("复仇者", late.Ribbon.Title, "超过复仇窗口不算复仇");

            // 昵称大小写 / 空白不影响判定（NormalizeKey 的契约）
            var t3 = new KillStreakTracker();
            t3.NoteLocalDeath(0f, NameKey.Normalize("  Nikita "));
            KillAnalysis cased = t3.Register(5f, Fact(victim: NameKey.Normalize("nikita")), s);
            Assert.Equal("复仇者", cased.Ribbon.Title, "归一化后的昵称大小写无关");
        }

        Assert.Suite("KillStreak: 连杀天梯");
        {
            // 间隔 5 秒：> 多杀窗口 2.5，≤ 连杀重置 10 → 只涨连杀，不出多杀
            var t = new KillStreakTracker();
            var s = Settings();
            t.Register(0f, Fact(), s);
            KillAnalysis two = t.Register(5f, Fact(), s);
            Assert.Equal(2, two.Streak, "间隔 5 秒仍是连杀");
            Assert.False(two.Ribbon.HasTitle, "streak=2 还没到天梯第一档（3）");

            KillAnalysis three = t.Register(10f, Fact(), s);
            Assert.Equal("猎手", three.Ribbon.Title, "连杀 3 → 猎手");
            Assert.Equal(KillAchievementKind.Streak, three.Ribbon.Kind, "天梯的 Kind 是 Streak");

            KillAnalysis four = t.Register(15f, Fact(), s);
            Assert.False(four.Ribbon.HasTitle, "连杀 4 不是天梯档位，不重复给称号");

            KillAnalysis five = t.Register(20f, Fact(), s);
            Assert.Equal("狂战士", five.Ribbon.Title, "连杀 5 → 狂战士");

            // 进度条：streak 3 刚踩到档位 → 0；streak 4 → 到 5 档的一半
            var p = new KillStreakTracker();
            p.Register(0f, Fact(), s);
            p.Register(5f, Fact(), s);
            KillAnalysis at3 = p.Register(10f, Fact(), s);
            Assert.Near(0.0, at3.StreakProgress, 0.0001, "streak=3 时进度归 0");
            KillAnalysis at4 = p.Register(15f, Fact(), s);
            Assert.Near(0.5, at4.StreakProgress, 0.0001, "streak=4 时进度 0.5");
        }

        Assert.Suite("KillStreak: 枪手（BF6 Gunner）");
        {
            // 把多杀窗口压到 0.1，隔离出枪手规则
            var s = Settings(multiKillWindow: 0.1f, gunnerWindow: 4f);

            var t = new KillStreakTracker();
            t.Register(0f, Fact(weapon: "AK-74M"), s);
            KillAnalysis changed = t.Register(1f, Fact(weapon: "M4A1"), s);
            Assert.Equal("枪手", changed.Ribbon.Title, "4 秒内换武器再杀 → 枪手");

            var same = new KillStreakTracker();
            same.Register(0f, Fact(weapon: "AK-74M"), s);
            KillAnalysis unchanged = same.Register(1f, Fact(weapon: "AK-74M"), s);
            Assert.NotEqual("枪手", unchanged.Ribbon.Title, "同一把武器不给枪手");

            var slow = new KillStreakTracker();
            slow.Register(0f, Fact(weapon: "AK-74M"), s);
            KillAnalysis late = slow.Register(10f, Fact(weapon: "M4A1"), s);
            Assert.NotEqual("枪手", late.Ribbon.Title, "超过枪手窗口不算");
        }

        Assert.Suite("KillStreak: 掠夺者（BF6 Marauder）");
        {
            // 间隔 2 秒：> 多杀窗口 0.5（隔离掠夺者），≤ 连杀重置 10
            var s = Settings(multiKillWindow: 0.5f, marauderWindow: 8f, streakResetWindow: 10f);
            var t = new KillStreakTracker();

            for (int i = 0; i < 4; i++) t.Register(i * 2f, Fact(), s);          // 0,2,4,6 → streak 4

            KillAnalysis fifth = t.Register(8f, Fact(), s);
            Assert.Equal("狂战士", fifth.Ribbon.Title, "第 5 杀踩到天梯档位 → 让位给狂战士（不给掠夺者）");

            KillAnalysis sixth = t.Register(10f, Fact(), s);
            Assert.Equal("掠夺者", sixth.Ribbon.Title, "第 4 杀后 8 秒内再杀 → 掠夺者");
            Assert.Equal(KillAchievementKind.Marauder, sixth.Ribbon.Kind, "掠夺者 Kind");

            var slow = new KillStreakTracker();
            for (int i = 0; i < 6; i++) slow.Register(i * 7f, Fact(), s);       // 0,7,14,21,28,35
            KillAnalysis far = slow.Register(40f, Fact(), s);
            Assert.NotEqual("掠夺者", far.Ribbon.Title, "超时不算掠夺者");
        }

        Assert.Suite("KillStreak: 情况类成就");
        {
            var s = Settings(longShot: 100f, marksman: 200f, eliteLevel: 40);

            var sniper = new KillStreakTracker();
            KillAnalysis marksman = sniper.Register(0f, Fact(distance: 250f, headshot: true), s);
            Assert.Equal("神射手", marksman.Ribbon.Title, "250m 爆头 → 神射手");

            var longRange = new KillStreakTracker();
            Assert.Equal("远程击杀", longRange.Register(0f, Fact(distance: 250f), s).Ribbon.Title, "250m 非爆头 → 远程击杀");

            var melee = new KillStreakTracker();
            Assert.Equal("冷兵器", melee.Register(0f, Fact(melee: true, distance: 1f), s).Ribbon.Title, "近战 → 冷兵器");

            var boom = new KillStreakTracker();
            Assert.Equal("爆破专家", boom.Register(0f, Fact(explosive: true), s).Ribbon.Title, "爆炸 → 爆破专家");

            var elite = new KillStreakTracker();
            Assert.Equal("精锐猎杀",
                elite.Register(0f, Fact(pmc: true, botType: "USEC", level: 50), s).Ribbon.Title,
                "等级 50 的 PMC → 精锐猎杀");

            var low = new KillStreakTracker();
            Assert.NotEqual("精锐猎杀",
                low.Register(0f, Fact(pmc: true, botType: "USEC", level: 20), s).Ribbon.Title,
                "等级 20 的 PMC 不算精锐");

            var boss = new KillStreakTracker();
            Assert.Equal("首领猎杀者",
                boss.Register(0f, Fact(boss: true, botType: "BOSS"), s).Ribbon.Title,
                "首领 → 首领猎杀者");

            var hs = new KillStreakTracker();
            hs.Register(0f, Fact(), s);                     // 让 raidKills 离开 1
            Assert.Equal("爆头", hs.Register(50f, Fact(headshot: true), s).Ribbon.Title, "普通爆头 → 爆头");
        }

        Assert.Suite("KillStreak: 得分公式与状态隔离");
        {
            var s = Settings();
            var t = new KillStreakTracker();
            KillAnalysis all = t.Register(0f, Fact(distance: 250f, headshot: true, boss: true), s);
            // 100 基础 + 50 爆头 + 50 远程 + 200 首领
            Assert.Equal(400, all.Score, "基础+爆头+远程+首领 = 400");

            var t2 = new KillStreakTracker();
            t2.NoteLocalDeath(0f, "nikita");
            KillAnalysis revenge = t2.Register(5f, Fact(victim: "nikita", distance: 250f, headshot: true), s);
            // 100 + 50 爆头 + 50 远程 + 25 复仇
            Assert.Equal(225, revenge.Score, "复仇 + 爆头 + 远程 = 225");

            Assert.True(all.HasScore, "有得分时 HasScore=true");

            t.Reset();
            Assert.Equal(0, t.RaidKills, "Reset 清空 raidKills");
            Assert.Equal(0, t.CurrentStreak, "Reset 清空 streak");
            KillAnalysis afterReset = t.Register(0f, Fact(), s);
            Assert.Equal("先锋", afterReset.Ribbon.Title, "Reset 之后第一杀重新是先锋");
        }

        Assert.Suite("KillStreak: 天梯配置不变式");
        {
            Assert.Equal(6, KillStreakTracker.LadderLength, "天梯共 6 档");
            int[] expected = { 3, 5, 7, 10, 15, 20 };
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], KillStreakTracker.TierCountAt(i), $"第 {i} 档的连杀数");
            }

            Assert.Equal(-1, KillStreakTracker.TierCountAt(expected.Length), "越界档位返回 -1");
            Assert.Equal(3, KillStreakTracker.NextTierAfter(0), "streak=0 的下一档是 3");
            Assert.Equal(5, KillStreakTracker.NextTierAfter(3), "streak=3 的下一档是 5");
            Assert.Equal(-1, KillStreakTracker.NextTierAfter(20), "已到顶返回 -1");
        }
    }
}
