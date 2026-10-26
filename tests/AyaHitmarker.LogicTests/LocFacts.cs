using System;
using System.Collections.Generic;
using AyaHitmarker.Game;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// 界面语言（0.7.0）：设置 + 游戏语言 → 实际语言，以及屏幕文字的中 / 英两套。
///
/// 两条硬约束：
/// 1. **语言只影响文字，不影响判定** —— 同一串击杀在中英文下产生的缎带种类 / 档次 / 得分序列完全一致；
/// 2. **英文模式下屏幕文字不含 CJK**（漏翻一条就会在英文界面里冒出方块 / 中文）。
/// </summary>
internal static class LocFacts
{
    private readonly record struct Seen(KillAchievementKind Kind, KillAchievementTier Tier, string Title, string Detail, int Score);

    private static KillFact Fact(string victim = "v", string weapon = "AK-74M", string botType = "SCAV", int level = 0,
        float distance = 10f, bool headshot = false, bool melee = false, bool explosive = false, bool sniper = false,
        bool boss = false, bool pmc = false)
        => new KillFact(victim, weapon, botType, level, distance, headshot, melee, explosive, sniper, boss, pmc);

    /// <summary>覆盖每一种缎带的击杀剧本。返回每一杀得到的主成就。</summary>
    private static List<Seen> RunScenarios()
    {
        var seen = new List<Seen>();
        var settings = KillStreakSettings.Default;

        void Note(KillAnalysis a)
        {
            seen.Add(new Seen(a.Ribbon.Kind, a.Ribbon.Tier, a.Ribbon.Title, a.Ribbon.Detail, a.Score));
        }

        // 先锋 + 爆头（本局第一杀 / 之后一个爆头）
        var t = new KillStreakTracker();
        Note(t.Register(0f, Fact(), settings));
        Note(t.Register(100f, Fact(headshot: true), settings));

        // 复仇者
        t = new KillStreakTracker();
        t.NoteLocalDeath(0f, "nemesis");
        Note(t.Register(1f, Fact(victim: "nemesis"), settings));

        // 多杀 x2 … x6（间隔 1 秒 < 多杀窗口 2.5 秒）
        t = new KillStreakTracker();
        for (int i = 0; i < 6; i++) Note(t.Register(i * 1f, Fact(), settings));

        // 连杀天梯：每 5 秒一杀（> 多杀窗口、< 重置窗口 10 秒），一路到 20 连杀
        t = new KillStreakTracker();
        for (int i = 0; i < 20; i++) Note(t.Register(i * 5f, Fact(), settings));
        Note(t.Register(20 * 5f, Fact(), settings));   // 顶档之上的连杀

        // 枪手（换武器，间隔在 多杀窗口 与 枪手窗口 之间）
        t = new KillStreakTracker();
        Note(t.Register(0f, Fact(weapon: "AK"), settings));
        Note(t.Register(3.5f, Fact(weapon: "M4A1"), settings));

        // 掠夺者（第 4 杀之后很快又杀一个；每 3 秒一杀 → 第 6 杀踩中）
        t = new KillStreakTracker();
        for (int i = 0; i < 6; i++) Note(t.Register(i * 3f, Fact(), settings));

        // 情况类：每个一个新 tracker，隔开 100 秒避免串连杀
        foreach (var fact in new[]
        {
            Fact(botType: "BOSS", boss: true),                          // 首领猎杀者
            Fact(distance: 250f, headshot: true),                       // 神射手
            Fact(distance: 150f),                                       // 远程击杀
            Fact(melee: true, distance: 1f),                            // 冷兵器
            Fact(explosive: true, distance: 8f),                        // 爆破专家
            Fact(botType: "USEC", pmc: true, level: 60, distance: 20f), // 精锐猎杀
        })
        {
            t = new KillStreakTracker();
            t.Register(0f, Fact(), settings);                           // 吃掉“本局首杀”
            Note(t.Register(100f, fact, settings));
        }

        return seen;
    }

    internal static void Run()
    {
        Assert.Suite("Loc: 设置 + 游戏语言 → 实际语言");
        {
            Assert.Equal(Lang.En, Loc.Resolve("English", "ch"), "强制 English，哪怕游戏是中文");
            Assert.Equal(Lang.En, Loc.Resolve("english", "ch"), "设置不分大小写");
            Assert.Equal(Lang.Zh, Loc.Resolve("中文", "en"), "强制中文，哪怕游戏是英文");
            Assert.Equal(Lang.Zh, Loc.Resolve("自动", "ch"), "自动 + 游戏中文 → 中文");
            Assert.Equal(Lang.Zh, Loc.Resolve("自动", "zh-CN"), "自动 + zh-CN 也认");
            Assert.Equal(Lang.Zh, Loc.Resolve("自动", "ZH"), "自动 + ZH（大写）也认");
            Assert.Equal(Lang.Zh, Loc.Resolve("自动", "Chinese"), "自动 + 英文全称也认");
            Assert.Equal(Lang.En, Loc.Resolve("自动", "en"), "自动 + 游戏英文 → English");
            Assert.Equal(Lang.En, Loc.Resolve("自动", "ru"), "自动 + 游戏俄语 → English（只提供中英两套）");
            Assert.Equal(Lang.En, Loc.Resolve("自动", "ge"), "自动 + 游戏德语 → English");
            Assert.Equal(Lang.Zh, Loc.Resolve("自动", null), "取不到游戏语言 → 中文（保持 0.6 行为）");
            Assert.Equal(Lang.Zh, Loc.Resolve("自动", ""), "游戏语言为空串 → 中文");
            Assert.Equal(Lang.Zh, Loc.Resolve("自动", "   "), "游戏语言全是空白 → 中文");
            Assert.Equal(Lang.En, Loc.Resolve("乱写的值", "en"), "认不出的设置按自动处理");
            Assert.Equal(Lang.Zh, Loc.Resolve(null, "ch"), "设置为 null 按自动处理");
        }

        Assert.Suite("Loc: Pick / 当前语言");
        {
            var before = Loc.Current;
            try
            {
                Loc.Current = Lang.Zh;
                Assert.Equal("双杀", Loc.Pick("双杀", "Double Kill"), "中文取 zh");
                Assert.False(Loc.IsEnglish, "中文模式 IsEnglish=false");
                Loc.Current = Lang.En;
                Assert.Equal("Double Kill", Loc.Pick("双杀", "Double Kill"), "英文取 en");
                Assert.True(Loc.IsEnglish, "英文模式 IsEnglish=true");
                Assert.Equal(10, Loc.Pick(7, 10), "泛型 Pick 也能挑数值");
            }
            finally
            {
                Loc.Current = before;
            }

            Assert.True(Loc.ContainsCjk("双杀"), "ContainsCjk: 汉字");
            Assert.True(Loc.ContainsCjk("Kill ·双"), "ContainsCjk: 混合");
            Assert.False(Loc.ContainsCjk("Double Kill · 87m"), "ContainsCjk: 纯英文 + 中点不算");
            Assert.False(Loc.ContainsCjk(null), "ContainsCjk: null");
        }

        Assert.Suite("Loc: 击杀缎带 —— 语言不影响判定");
        {
            var before = Loc.Current;
            List<Seen> zh, en;
            try
            {
                Loc.Current = Lang.Zh;
                zh = RunScenarios();
                Loc.Current = Lang.En;
                en = RunScenarios();
            }
            finally
            {
                Loc.Current = before;
            }

            Assert.Equal(zh.Count, en.Count, "两种语言下击杀数一致");
            bool sameLogic = zh.Count == en.Count;
            for (int i = 0; sameLogic && i < zh.Count; i++)
            {
                if (zh[i].Kind != en[i].Kind || zh[i].Tier != en[i].Tier || zh[i].Score != en[i].Score)
                {
                    sameLogic = false;
                    Assert.True(false, $"第 {i} 杀判定不一致: zh={zh[i].Kind}/{zh[i].Tier}/{zh[i].Score} en={en[i].Kind}/{en[i].Tier}/{en[i].Score}");
                }
            }
            Assert.True(sameLogic, "每一杀的缎带种类 / 档次 / 得分在中英文下完全一致");

            // 每一种缎带都被剧本覆盖到（新增 Kind 却没写英文，这里会因为没覆盖而提醒）
            var kinds = new HashSet<KillAchievementKind>();
            foreach (var s in en) kinds.Add(s.Kind);
            foreach (KillAchievementKind k in Enum.GetValues(typeof(KillAchievementKind)))
            {
                if (k == KillAchievementKind.None) continue;
                Assert.True(kinds.Contains(k), $"剧本覆盖了缎带 {k}");
            }

            // 英文：标题与详情不含 CJK；中文：标题含 CJK
            var badEn = new List<string>();
            var badZh = new List<string>();
            foreach (var s in en)
            {
                if (s.Kind == KillAchievementKind.None) continue;
                if (Loc.ContainsCjk(s.Title) || Loc.ContainsCjk(s.Detail)) badEn.Add($"{s.Kind}: '{s.Title}' / '{s.Detail}'");
            }
            foreach (var s in zh)
            {
                if (s.Kind == KillAchievementKind.None) continue;
                if (!Loc.ContainsCjk(s.Title)) badZh.Add($"{s.Kind}: '{s.Title}'");
            }
            Assert.Equal(0, badEn.Count, "英文模式下缎带标题 / 详情不含 CJK" + (badEn.Count > 0 ? "：" + string.Join("; ", badEn) : ""));
            Assert.Equal(0, badZh.Count, "中文模式下缎带标题都是中文" + (badZh.Count > 0 ? "：" + string.Join("; ", badZh) : ""));

            // 抽查几条具体文案（对齐 BF6 英文荣誉名）
            string TitleOf(List<Seen> list, KillAchievementKind k, KillAchievementTier? tier = null)
            {
                foreach (var s in list) if (s.Kind == k && (tier == null || s.Tier == tier)) return s.Title;
                return string.Empty;
            }
            Assert.Equal("Vanguard", TitleOf(en, KillAchievementKind.FirstBlood), "先锋 → Vanguard");
            Assert.Equal("Avenger", TitleOf(en, KillAchievementKind.Revenge), "复仇者 → Avenger");
            Assert.Equal("Marauder", TitleOf(en, KillAchievementKind.Marauder), "掠夺者 → Marauder");
            Assert.Equal("Gunner", TitleOf(en, KillAchievementKind.Gunner), "枪手 → Gunner");
            Assert.Equal("Boss Slayer", TitleOf(en, KillAchievementKind.BossSlayer), "首领猎杀者 → Boss Slayer");
            Assert.Equal("Marksman", TitleOf(en, KillAchievementKind.Marksman), "神射手 → Marksman");
            Assert.Equal("Headshot", TitleOf(en, KillAchievementKind.Headshot), "爆头 → Headshot");

            var multi = new List<string>();
            foreach (var s in en) if (s.Kind == KillAchievementKind.MultiKill) multi.Add(s.Title);
            Assert.True(multi.Contains("Double Kill") && multi.Contains("Triple Kill") && multi.Contains("Quad Kill")
                        && multi.Contains("Penta Kill") && multi.Contains("Multi Kill x6"),
                "多杀: Double / Triple / Quad / Penta / Multi Kill x6");

            // 天梯：六档称号都出现过
            var ladder = new HashSet<string>();
            foreach (var s in en) if (s.Kind == KillAchievementKind.Streak) ladder.Add(s.Title);
            foreach (string name in new[] { "Hunter", "Berserker", "Unstoppable", "Dominator", "Peerless", "Godlike" })
                Assert.True(ladder.Contains(name), $"连杀天梯英文称号 {name}");
        }

        Assert.Suite("Loc: 英文横幅详情都够短（横幅上限 10 字符）");
        {
            // 横幅只放短详情：距离 / 「连杀 x5」/「本局首杀」。这些英文版不能比上限长，否则会被静默藏掉。
            var before = Loc.Current;
            try
            {
                Loc.Current = Lang.En;
                var t = new KillStreakTracker();
                var s = KillStreakSettings.Default;
                string first = t.Register(0f, Fact(), s).Ribbon.Detail;
                Assert.True(first.Length <= 10, $"先锋详情 '{first}' ≤ 10 字符");

                t = new KillStreakTracker();
                string streakDetail = string.Empty;
                for (int i = 0; i < 20; i++)
                {
                    var a = t.Register(i * 5f, Fact(), s);
                    if (a.Ribbon.Kind == KillAchievementKind.Streak) streakDetail = a.Ribbon.Detail;
                }
                Assert.Equal("Streak x20", streakDetail, "20 连杀详情");
                Assert.True(streakDetail.Length <= 10, "连杀详情 ≤ 10 字符（x20 是最长的一档）");
            }
            finally
            {
                Loc.Current = before;
            }
        }

        Assert.Suite("Loc: 击杀部位英文名");
        {
            var before = Loc.Current;
            try
            {
                Loc.Current = Lang.En;
                Assert.Equal("Head", BodyPartNames.Describe("Head", 0), "Head");
                Assert.Equal("Chest", BodyPartNames.Describe("Chest", 1), "Chest");
                Assert.Equal("Stomach", BodyPartNames.Describe("Stomach", 2), "Stomach");
                Assert.Equal("Left Arm", BodyPartNames.Describe("LeftArm", 3), "LeftArm");
                Assert.Equal("Right Arm", BodyPartNames.Describe("RightArm", 4), "RightArm");
                Assert.Equal("Left Leg", BodyPartNames.Describe("LeftLeg", 5), "LeftLeg");
                Assert.Equal("Right Leg", BodyPartNames.Describe("RightLeg", 6), "RightLeg");
                Assert.Equal("Torso", BodyPartNames.Describe("Common", 7), "Common");
                Assert.Equal("Unknown", BodyPartNames.Describe(null, 99), "完全认不出 → Unknown");
                Assert.Equal("Left Leg", BodyPartNames.Describe("EBodyPart/", 5), "畸形输入靠数值兜底，英文也一样");
            }
            finally
            {
                Loc.Current = before;
            }
        }
    }
}
