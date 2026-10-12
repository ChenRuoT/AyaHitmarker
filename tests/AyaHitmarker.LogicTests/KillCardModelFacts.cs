using AyaHitmarker.Game;
using AyaHitmarker.Ui;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// 击杀卡片累加模型的行为契约：窗口内连续击杀并到同一张卡上（骷髅并排 / 分数累计 / 标签取并集 / 名字换最新），
/// 过了窗口才退场并重新开卡；退场期间补杀会取消退场。
/// 这是从 BF6 连杀演示第 4 杀（红骷髅 + 白骷髅 / 200 / [HEADSHOT][KILL]）反推出来的规则。
/// </summary>
internal static class KillCardModelFacts
{
    private const float Hold = 3.6f;
    private const float Exit = 0.5f;

    private static CardKillInput Kill(
        bool headshot = false,
        int score = 100,
        KillAchievement? ribbon = null,
        int multi = 1,
        float distance = 50f,
        bool melee = false,
        bool explosive = false,
        bool sniper = false)
        => new CardKillInput(headshot, melee, explosive, sniper, distance, score, ribbon ?? KillAchievement.None, multi);

    private static KillAchievement Ribbon(string title, KillAchievementKind kind = KillAchievementKind.MultiKill)
        => new KillAchievement(title, string.Empty, kind, KillAchievementTier.Good);

    private static bool Add(KillCardModel m, float now, in CardKillInput k, int shown = 0, int maxIcons = 8)
        => m.AddKill(now, k, shown, Hold, Exit, maxIcons);

    internal static void Run()
    {
        Assert.Suite("CardModel: 第一杀开卡");
        {
            var m = new KillCardModel();
            Assert.False(m.Active, "初始没有卡片");

            bool fresh = Add(m, 10f, Kill());
            Assert.True(fresh, "第一杀返回 true（新开了一张卡）");
            Assert.True(m.Active, "卡片在场");
            Assert.Equal(1, m.KillCount, "击杀数 1");
            Assert.Equal(100, m.TotalScore, "总分 = 本杀得分");
            Assert.Equal(0, m.PreviousScore, "新卡的滚动起点是 0");
            Assert.Equal(1, m.Icons.Count, "一个骷髅");
            Assert.False(m.Icons[0].Headshot, "普通击杀 → 白骷髅");
            Assert.Equal(1, m.Tags.Count, "一个标签");
            Assert.Equal(CardTagKind.Kill, m.Tags[0].Kind, "普通击杀 → 「击杀」标签");
            Assert.False(m.HasBanner, "没有成就就没有横幅");
            Assert.Near(10f + Hold, m.ExitStart, 1e-5, "退场开始 = 最后一杀 + Hold");
            Assert.Near(10f + Hold + Exit, m.ExitEnd, 1e-5, "卡片结束 = 退场开始 + ExitDuration");
        }

        Assert.Suite("CardModel: 爆头与附加标签");
        {
            var m = new KillCardModel();
            Add(m, 0f, Kill(headshot: true));
            Assert.True(m.Icons[0].Headshot, "爆头 → 红骷髅");
            Assert.Equal(1, m.Tags.Count, "爆头击杀只给「爆头」，不再额外加「击杀」（BF6 同款）");
            Assert.Equal(CardTagKind.Headshot, m.Tags[0].Kind, "标签是爆头");

            var m2 = new KillCardModel();
            Add(m2, 0f, Kill(melee: true, explosive: true, sniper: true));
            Assert.Equal(4, m2.Tags.Count, "近战 + 爆炸 + 狙击 → 四个标签");
            Assert.Equal(CardTagKind.Kill, m2.Tags[0].Kind, "击杀类标签排第一");
            Assert.Equal(CardTagKind.Melee, m2.Tags[1].Kind, "然后是近战");
            Assert.Equal(CardTagKind.Explosive, m2.Tags[2].Kind, "然后是爆炸");
            Assert.Equal(CardTagKind.Sniper, m2.Tags[3].Kind, "最后是狙击");
        }

        Assert.Suite("CardModel: 窗口内连杀并到同一张卡（BF6 第 4 杀）");
        {
            var m = new KillCardModel();
            Add(m, 10f, Kill(headshot: true, score: 100, distance: 20f));
            int serialBefore = m.VictimSerial;

            bool fresh = Add(m, 12f, Kill(headshot: false, score: 100, distance: 80f), shown: 100);
            Assert.False(fresh, "窗口内的第二杀返回 false（并卡，不是新卡）");
            Assert.Equal(2, m.KillCount, "击杀数 2");
            Assert.Equal(200, m.TotalScore, "分数累计 100 + 100 = 200");
            Assert.Equal(100, m.PreviousScore, "滚动起点 = 屏幕上当时显示的分数（100），不是 0");
            Assert.Near(10f, m.StartTime, 1e-5, "卡片起点仍是第一杀");
            Assert.Equal(2, m.Icons.Count, "两个骷髅并排");
            Assert.True(m.Icons[0].Headshot && !m.Icons[1].Headshot, "红骷髅在前（旧），白骷髅在后（新）");
            Assert.True(m.Icons[1].Serial > m.Icons[0].Serial, "骷髅序号单调递增");
            Assert.Equal(2, m.Tags.Count, "标签取并集：爆头 + 击杀");
            Assert.Equal(CardTagKind.Headshot, m.Tags[0].Kind, "标签保持首次出现的顺序（爆头在前）");
            Assert.Equal(CardTagKind.Kill, m.Tags[1].Kind, "击杀在后");
            Assert.Near(80f, m.LastDistance, 1e-5, "距离是最新那一杀的");
            Assert.Equal(serialBefore + 1, m.VictimSerial, "受害者序号 +1（视图据此让名字行重新显影）");
            Assert.Near(12f + Hold, m.ExitStart, 1e-5, "退场时间从最新一杀重新计");

            // 再来一个同类杀 → 标签不重复
            Add(m, 13f, Kill(headshot: false));
            Assert.Equal(2, m.Tags.Count, "同种标签不重复");
            Assert.Equal(3, m.Icons.Count, "但骷髅每杀一个");
        }

        Assert.Suite("CardModel: 退场与重新开卡");
        {
            var m = new KillCardModel();
            Add(m, 0f, Kill(score: 100));

            Assert.False(m.IsExiting(Hold - 0.01f), "Hold 之内不在退场");
            Assert.True(m.IsExiting(Hold + 0.01f), "过了 Hold 开始退场");
            Assert.Near(0.25, m.ExitAge(Hold + 0.25f), 1e-5, "ExitAge = 距退场开始的秒数");
            Assert.True(m.ExitAge(1f) < 0f, "退场开始前 ExitAge 为负");

            // 退场期间补一杀：取消退场，继续累加
            float duringExit = Hold + 0.2f;
            bool fresh = Add(m, duringExit, Kill(score: 100), shown: 100);
            Assert.False(fresh, "退场期间补杀不是新卡");
            Assert.Equal(200, m.TotalScore, "继续累加");
            Assert.False(m.IsExiting(duringExit + 0.01f), "补杀后退场被取消");
            Assert.Near(duringExit + Hold, m.ExitStart, 1e-5, "退场重新从这一杀起算");

            // 彻底结束之后再来一杀：新卡，累计清零
            float after = m.ExitEnd + 1f;
            fresh = Add(m, after, Kill(score: 50), shown: 200);
            Assert.True(fresh, "卡片结束后的击杀会新开一张卡");
            Assert.Equal(1, m.KillCount, "新卡击杀数从 1 开始");
            Assert.Equal(50, m.TotalScore, "新卡分数从零累计");
            Assert.Equal(0, m.PreviousScore, "新卡无视传入的显示分数，从 0 滚起");
            Assert.Equal(1, m.Icons.Count, "新卡只有一个骷髅");
            Assert.Equal(1, m.Tags.Count, "新卡标签重新累计");
            Assert.Near(after, m.StartTime, 1e-5, "新卡的起点是这一杀");
        }

        Assert.Suite("CardModel: 骷髅上限");
        {
            var m = new KillCardModel();
            for (int i = 0; i < 5; i++) Add(m, i * 0.5f, Kill(headshot: i % 2 == 0), maxIcons: 3);
            Assert.Equal(3, m.Icons.Count, "上限 3：最老的被挤掉");
            Assert.Equal(5, m.KillCount, "击杀数不受骷髅上限影响");
            Assert.Equal(500, m.TotalScore, "分数不受骷髅上限影响");
            Assert.Near(1.0, m.Icons[0].Born, 1e-5, "留下的是最新的三个（第 3、4、5 杀）");
            Assert.Near(2.0, m.Icons[2].Born, 1e-5, "最后一个是最新那杀");

            var m2 = new KillCardModel();
            Add(m2, 0f, Kill(), maxIcons: 0);
            Assert.Equal(1, m2.Icons.Count, "上限被钳到至少 1，不会一个骷髅都不显示");
        }

        Assert.Suite("CardModel: 横幅（成就）");
        {
            var m = new KillCardModel();
            Add(m, 0f, Kill());
            Assert.False(m.HasBanner, "普通击杀没有横幅");
            Assert.Equal(0, m.BannerSerial, "横幅序号 0");

            Add(m, 1f, Kill(ribbon: Ribbon("双杀"), multi: 2));
            Assert.True(m.HasBanner, "触发成就后出现横幅");
            Assert.Equal("双杀", m.Banner.Title, "横幅内容是双杀");
            Assert.Equal(2, m.BannerMulti, "横幅记着连杀段数（徽记里放 2 个头）");
            Assert.Equal(1, m.BannerSerial, "横幅序号 +1");
            Assert.Near(1f, m.BannerBorn, 1e-5, "横幅出生时间");

            // 后来的击杀没有成就：横幅保持
            Add(m, 2f, Kill());
            Assert.Equal("双杀", m.Banner.Title, "后来的击杀没有成就时，横幅保持");
            Assert.Equal(1, m.BannerSerial, "横幅序号不变（视图不会重复触发动画）");

            // 换了成就：替换 + 序号再 +1
            Add(m, 3f, Kill(ribbon: Ribbon("三杀"), multi: 3));
            Assert.Equal("三杀", m.Banner.Title, "新成就替换旧横幅");
            Assert.Equal(2, m.BannerSerial, "横幅序号 +1（视图据此做「旧的闪灭 → 新的显影」）");

            // 新开卡：横幅清掉，但序号不回绕
            Add(m, m.ExitEnd + 1f, Kill());
            Assert.False(m.HasBanner, "新卡没有上一张卡的横幅");
            Assert.Equal(2, m.BannerSerial, "序号不清零（回绕会让新元素被当成旧的）");
        }

        Assert.Suite("CardModel: Tick 到期清空");
        {
            var m = new KillCardModel();
            Assert.False(m.Tick(1f), "没有卡片时 Tick 什么也不做");

            Add(m, 0f, Kill());
            Assert.False(m.Tick(Hold), "退场中还没结束：保持在场");
            Assert.True(m.Active, "仍在场");
            Assert.True(m.Tick(Hold + Exit), "到点返回 true 并清空");
            Assert.False(m.Active, "清空后不在场");
            Assert.False(m.Tick(Hold + Exit + 1f), "只返回一次");
            Assert.Equal(0, m.Icons.Count, "清空后没有骷髅");
            Assert.Equal(0, m.TotalScore, "清空后没有分数");
        }

        Assert.Suite("CardModel: 参数钳制");
        {
            var m = new KillCardModel();
            m.AddKill(0f, Kill(), 0, hold: -5f, exitDuration: -1f, maxIcons: 8);
            Assert.True(m.Hold >= 0.1f, "Hold 至少 0.1 秒（避免卡片一出现就退场）");
            Assert.True(m.ExitDuration >= 0f, "ExitDuration 不为负");

            var neg = new KillCardModel();
            neg.AddKill(0f, Kill(score: -50), 0, Hold, Exit, 8);
            Assert.Equal(0, neg.TotalScore, "负分被钳到 0");
        }
    }
}
