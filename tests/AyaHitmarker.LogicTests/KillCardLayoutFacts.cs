using AyaHitmarker.Ui;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// 击杀卡片布局的可验证不变式：行序、行间距、进度条占位。
///
/// 这些检查不能告诉你「好不好看」，但能挡住真正会出事的那类回归 ——
/// 行序颠倒、缎带压到阵营行、进度条和名字重叠、字号改完之后行高不跟着变。
/// </summary>
internal static class KillCardLayoutFacts
{
    /// <summary>接近 AyaConfig 的默认值。</summary>
    private static KillCardLayoutInput DefaultInput(
        float skullSize = 40f,
        float lineSpacing = 0f,
        float achievementHeight = 30f,
        float achievementOffset = 0f,
        float streakBarHeight = 4f,
        float streakBarGap = 4f,
        int fontScore = 28)
        => new KillCardLayoutInput(
            skullSize,
            lineSpacing,
            achievementHeight,
            achievementOffset,
            streakBarHeight,
            streakBarGap,
            fontFaction: 20,
            fontName: 18,
            fontDetails: 20,
            fontScore: fontScore,
            fontAchievement: 22);

    internal static void Run()
    {
        Assert.Suite("Layout: 行序（从下到上）");
        {
            KillCardLayout l = KillCardLayoutMath.Compute(DefaultInput());
            Assert.Near(0.0, l.SkullCenterY, 0.0001, "骷髅行在锚点上");
            Assert.Greater(l.FactionY, l.SkullCenterY, "阵营在骷髅上方");
            Assert.Greater(l.AchievementY, l.FactionY, "成就缎带在阵营上方");
            Assert.Greater(l.SkullCenterY, l.NameY, "名字在骷髅下方");
            Assert.Greater(l.NameY, l.DetailsY, "详情在名字下方");
        }

        Assert.Suite("Layout: 行间距不变量");
        {
            float spacing = 0f;
            // 骷髅 ↔ 名字的空隙只在**没有进度条占位**时等于 LineSpacing；
            // 有进度条时其中还会夹着 barGap + barHeight（那条不变量在下一组里验）。
            KillCardLayout l = KillCardLayoutMath.Compute(DefaultInput(skullSize: 40f, lineSpacing: spacing, streakBarHeight: 0f));

            // 骷髅行上缘 → 阵营行下缘之间的空隙应正好等于 LineSpacing
            double gapFaction = l.FactionY - l.LineHeight / 2.0 - 20.0;
            Assert.Near(spacing, gapFaction, 0.001, "骷髅 ↔ 阵营的空隙 = 行间距");

            double gapName = -20.0 - (l.NameY + l.LineHeight / 2.0);
            Assert.Near(spacing, gapName, 0.001, "骷髅 ↔ 名字的空隙 = 行间距（无进度条）");

            double gapDetails = (l.NameY - l.LineHeight / 2.0) - (l.DetailsY + l.LineHeight / 2.0);
            Assert.Near(spacing, gapDetails, 0.001, "名字 ↔ 详情的空隙 = 行间距");

            // 加大行间距，所有空隙跟着变大
            KillCardLayout wide = KillCardLayoutMath.Compute(DefaultInput(lineSpacing: 6f, streakBarHeight: 0f));
            Assert.Near(6.0, wide.FactionY - wide.LineHeight / 2.0 - 20.0, 0.001, "行间距 6 时阵营空隙 = 6");
            Assert.Greater(wide.DetailsY * -1.0, l.DetailsY * -1.0, "行间距变大后详情行更靠下");
        }

        Assert.Suite("Layout: 连杀进度条");
        {
            KillCardLayout l = KillCardLayoutMath.Compute(DefaultInput(skullSize: 40f, streakBarHeight: 4f, streakBarGap: 4f));

            Assert.Greater(l.SkullCenterY, l.StreakBarY, "进度条在骷髅下方");
            Assert.Greater(l.StreakBarY, l.NameY, "进度条在名字上方");

            double barTop = l.StreakBarY + 4.0 / 2.0;
            Assert.Near(-24.0, barTop, 0.001, "进度条上缘 = 骷髅下缘 - 间距");
            Assert.Near(4.0, -20.0 - barTop, 0.001, "骷髅下缘到进度条上缘 = StreakBarGap");

            double gapToName = (l.StreakBarY - 4.0 / 2.0) - (l.NameY + l.LineHeight / 2.0);
            Assert.Near(0.0, gapToName, 0.001, "进度条 ↔ 名字的空隙 = 行间距");

            // 关掉进度条时不占位：名字行回到旧公式的位置
            KillCardLayout noBar = KillCardLayoutMath.Compute(DefaultInput(streakBarHeight: 0f));
            Assert.Near(-20.0 - noBar.LineHeight / 2.0, noBar.NameY, 0.001, "无进度条时名字行紧贴骷髅");
            Assert.Greater(l.NameY * -1.0, noBar.NameY * -1.0, "有进度条时名字行更靠下");
        }

        Assert.Suite("Layout: 行高随字号变化并被钳制");
        {
            // 默认字号最大的是得分（28）→ 28 * 1.2
            Assert.Near(33.6, KillCardLayoutMath.Compute(DefaultInput(fontScore: 28)).LineHeight, 0.001,
                "最大字号 28 → 行高 33.6");

            // 其余字号最大是成就/阵营的 22 → 22 * 1.2
            Assert.Near(26.4, KillCardLayoutMath.Compute(DefaultInput(fontScore: 10)).LineHeight, 0.001,
                "得分字号缩到 10，行高由 22 字号决定");

            // 离谱字号被钳到 MaxLineHeight，不会把整块 UI 撞到屏幕外
            Assert.Near(KillCardLayoutMath.MaxLineHeight,
                KillCardLayoutMath.Compute(DefaultInput(fontScore: 1000)).LineHeight, 0.001,
                "超大字号被钳到 MaxLineHeight");
        }

        Assert.Suite("Layout: 缎带偏移与尺寸");
        {
            KillCardLayout baseLayout = KillCardLayoutMath.Compute(DefaultInput(achievementOffset: 0f));
            KillCardLayout shifted = KillCardLayoutMath.Compute(DefaultInput(achievementOffset: 12f));
            Assert.Near(12.0, shifted.AchievementY - baseLayout.AchievementY, 0.001,
                "AchievementVerticalOffset 精确平移缎带");

            KillCardLayout tall = KillCardLayoutMath.Compute(DefaultInput(achievementHeight: 60f));
            Assert.Near(15.0, tall.AchievementY - baseLayout.AchievementY, 0.001,
                "缎带高度 +30 → 中心再上移 15");

            // 骨架尺寸不影响缎带的相对位置（只影响阵营行及以上）
            KillCardLayout bigSkull = KillCardLayoutMath.Compute(DefaultInput(skullSize: 120f));
            Assert.Near(baseLayout.AchievementY - baseLayout.FactionY,
                bigSkull.AchievementY - bigSkull.FactionY, 0.001,
                "骷髅变大不影响「阵营 → 缎带」的相对距离");
            Assert.Greater(bigSkull.FactionY, baseLayout.FactionY, "骷髅变大把阵营行顶上去");
        }
    }
}
