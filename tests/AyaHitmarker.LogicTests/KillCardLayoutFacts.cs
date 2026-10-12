using AyaHitmarker.Ui;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// 击杀卡片布局的可验证不变式：行序、行间距、缺席的行不占位、内容左缘、军衔徽章的位置。
///
/// 这些检查不能告诉你「好不好看」，但能挡住真正会出事的那类回归 ——
/// 行序颠倒、横幅压到主行、名字和标签重叠、军衔徽章盖到文字上、不存在的行还留着一个空洞。
/// </summary>
internal static class KillCardLayoutFacts
{
    /// <summary>接近 AyaConfig 的默认值，所有行都在。</summary>
    private static KillCardLayoutInput Full() => new KillCardLayoutInput
    {
        SkullSize = 44f,
        IconGap = 8f,
        RowGap = 4f,
        BarHeight = 3f,
        BarGap = 4f,
        BannerHeight = 30f,
        NameHeight = 26f,
        TagHeight = 26f,
        DetailsHeight = 20f,
        BadgeSize = 64f,
        BadgeGap = 12f,
        HasBanner = true,
        HasBar = true,
        HasName = true,
        HasTags = true,
        HasDetails = true,
        HasBadge = true,
        SkullCount = 1,
        BannerWidth = 160f,
        NameWidth = 150f,
        TagsWidth = 120f,
        DetailsWidth = 110f,
        BannerOverhang = 14f,
    };

    internal static void Run()
    {
        Assert.Suite("Layout: 行序（从上到下）");
        {
            KillCardLayout l = KillCardLayoutMath.Compute(Full());
            Assert.Near(0.0, l.MainY, 0.0001, "主行（骷髅 + 分数）中心在锚点上");
            Assert.Greater(l.BannerY, l.MainY, "横幅在主行上方");
            Assert.Greater(l.MainY, l.BarY, "进度条在主行下方");
            Assert.Greater(l.BarY, l.NameY, "名字在进度条下方");
            Assert.Greater(l.NameY, l.TagY, "标签在名字下方");
            Assert.Greater(l.TagY, l.DetailsY, "详情在标签下方");
        }

        Assert.Suite("Layout: 行间距不变量");
        {
            KillCardLayoutInput i = Full();
            KillCardLayout l = KillCardLayoutMath.Compute(i);

            double mainTop = i.SkullSize / 2.0;
            double mainBottom = -i.SkullSize / 2.0;

            Assert.Near(i.RowGap, (l.BannerY - i.BannerHeight / 2.0) - mainTop, 0.001, "主行 ↔ 横幅的空隙 = RowGap");
            Assert.Near(i.BarGap, mainBottom - (l.BarY + i.BarHeight / 2.0), 0.001, "主行 ↔ 进度条的空隙 = BarGap");
            Assert.Near(i.RowGap, (l.BarY - i.BarHeight / 2.0) - (l.NameY + i.NameHeight / 2.0), 0.001, "进度条 ↔ 名字的空隙 = RowGap");
            Assert.Near(i.RowGap, (l.NameY - i.NameHeight / 2.0) - (l.TagY + i.TagHeight / 2.0), 0.001, "名字 ↔ 标签的空隙 = RowGap");
            Assert.Near(i.RowGap, (l.TagY - i.TagHeight / 2.0) - (l.DetailsY + i.DetailsHeight / 2.0), 0.001, "标签 ↔ 详情的空隙 = RowGap");

            // 加大行间距：越往下累计得越多
            KillCardLayoutInput wide = Full();
            wide.RowGap = 10f;
            KillCardLayout lw = KillCardLayoutMath.Compute(wide);
            Assert.Greater(l.DetailsY, lw.DetailsY, "RowGap 变大后详情行更靠下");
            Assert.Greater(lw.BannerY, l.BannerY, "RowGap 变大后横幅更靠上");
        }

        Assert.Suite("Layout: 缺席的行不占位");
        {
            KillCardLayout full = KillCardLayoutMath.Compute(Full());

            KillCardLayoutInput noBar = Full();
            noBar.HasBar = false;
            KillCardLayout lb = KillCardLayoutMath.Compute(noBar);
            Assert.Near(3.0 + 4.0, lb.NameY - full.NameY, 0.001, "关掉进度条：名字上移 BarGap + BarHeight");
            Assert.Near(7.0, lb.DetailsY - full.DetailsY, 0.001, "进度条的空位一路传到最后一行");

            KillCardLayoutInput noTags = Full();
            noTags.HasTags = false;
            KillCardLayout lt = KillCardLayoutMath.Compute(noTags);
            double gapNameDetails = (lt.NameY - 26.0 / 2.0) - (lt.DetailsY + 20.0 / 2.0);
            Assert.Near(4.0, gapNameDetails, 0.001, "没有标签时详情直接贴在名字下面（间距仍是 RowGap）");

            KillCardLayoutInput noBanner = Full();
            noBanner.HasBanner = false;
            noBanner.HasBadge = false;
            KillCardLayout ln = KillCardLayoutMath.Compute(noBanner);
            Assert.Near(22.0, ln.Top, 0.001, "没有横幅（也没徽章）时卡片上缘 = 主行上缘");
            Assert.Near(full.NameY, ln.NameY, 0.001, "横幅在主行上方，有没有它都不影响下面各行");

            KillCardLayoutInput onlyMain = Full();
            onlyMain.HasBanner = false; onlyMain.HasBar = false; onlyMain.HasName = false;
            onlyMain.HasTags = false; onlyMain.HasDetails = false; onlyMain.HasBadge = false;
            KillCardLayout lm = KillCardLayoutMath.Compute(onlyMain);
            Assert.Near(-22.0, lm.Bottom, 0.001, "只剩主行时下缘 = 主行下缘");
        }

        Assert.Suite("Layout: 内容左缘与骷髅排布");
        {
            KillCardLayoutInput i = Full();
            double pitch = KillCardLayoutMath.SkullPitchFor(i.SkullSize, i.IconGap);
            Assert.Near(44.0 * 0.9 + 8.0, pitch, 0.001, "骷髅间距 = 0.9 × 尺寸 + 图标间隔");

            KillCardLayout one = KillCardLayoutMath.Compute(i);
            Assert.Near(-22.0, one.SkullCenterX(0), 0.001, "最新的骷髅紧贴脊线（中心 = -尺寸/2）");
            Assert.Near(-22.0 - pitch, one.SkullCenterX(1), 0.001, "第 2 个骷髅在左边隔一个间距");

            // 默认下最宽的是名字行 150
            Assert.Near(-150.0, one.LeftEdge, 0.001, "左缘由最宽的一行决定（名字 150）");

            // 骷髅多到比所有文字都宽时，左缘由骷髅决定
            i.SkullCount = 6;
            KillCardLayout six = KillCardLayoutMath.Compute(i);
            Assert.Near(-(44.0 + 5.0 * pitch), six.LeftEdge, 0.001, "6 个骷髅撑开左缘 = -(尺寸 + 5×间距)");

            // 横幅的右缘探出脊线 14，所以只有 (宽 - 14) 算向左伸出的部分
            KillCardLayoutInput wideBanner = Full();
            wideBanner.BannerWidth = 400f;
            Assert.Near(-(400.0 - 14.0), KillCardLayoutMath.Compute(wideBanner).LeftEdge, 0.001, "横幅宽度按「减去探出量」计入左缘");

            // 骷髅被关掉（SkullCount = 0）：不预留骷髅的宽度，否则军衔徽章会在短名字旁边凭空多空出一个骷髅
            KillCardLayoutInput noSkull = Full();
            noSkull.HasBanner = false;
            noSkull.HasName = false;
            noSkull.HasTags = false;
            noSkull.HasDetails = false;
            noSkull.SkullCount = 0;
            Assert.Near(0.0, KillCardLayoutMath.Compute(noSkull).LeftEdge, 0.001, "没有骷髅、也没有任何文字行时左缘 = 脊线（不预留骷髅宽度）");
            noSkull.SkullCount = 1;
            Assert.Near(-44.0, KillCardLayoutMath.Compute(noSkull).LeftEdge, 0.001, "只有 1 个骷髅时左缘 = -骷髅尺寸");

            // 缺席的行不参与左缘
            KillCardLayoutInput hiddenName = Full();
            hiddenName.NameWidth = 999f;
            hiddenName.HasName = false;
            Assert.Near(-146.0, KillCardLayoutMath.Compute(hiddenName).LeftEdge, 0.001,
                "名字行不存在时它的宽度不参与左缘（剩下最宽的是横幅：160 - 探出 14 = 146）");
        }

        Assert.Suite("Layout: 军衔徽章");
        {
            KillCardLayoutInput i = Full();
            KillCardLayout l = KillCardLayoutMath.Compute(i);

            Assert.Near(l.LeftEdge - 12.0 - 32.0, l.BadgeX, 0.001, "徽章右缘 = 内容左缘 - BadgeGap（中心再减半个尺寸）");
            Assert.True(l.BadgeX < l.LeftEdge, "徽章在内容的左边，不压到文字上");

            // 身份区 = 主行上缘 → 标签下缘；徽章居中其上
            double identityBottom = l.TagY - 26.0 / 2.0;
            double expectedCenter = (22.0 + identityBottom) / 2.0;
            Assert.Near(expectedCenter, l.BadgeY, 0.001, "徽章竖直居中在「主行 + 名字 + 标签」上");

            // 徽章不会顶到横幅上
            double badgeTop = l.BadgeY + 32.0;
            Assert.True(badgeTop <= 22.0 + 4.0 + 0.001, "徽章上缘不超过主行上缘 + RowGap");

            // 徽章巨大时：只往下探，不往上冲；并且被计入卡片的上下缘
            KillCardLayoutInput huge = Full();
            huge.BadgeSize = 200f;
            KillCardLayout lh = KillCardLayoutMath.Compute(huge);
            Assert.True(lh.BadgeY + 100.0 <= 22.0 + 4.0 + 0.001, "超大徽章也不超过主行上缘 + RowGap");
            Assert.True(lh.Bottom <= lh.BadgeY - 100.0 + 0.001, "超大徽章的下缘被计入卡片下缘");

            // 没徽章时 BadgeX/Y 不参与 Top/Bottom
            KillCardLayoutInput none = Full();
            none.HasBadge = false;
            KillCardLayout ln = KillCardLayoutMath.Compute(none);
            Assert.Near(l.Top, ln.Top, 0.001, "默认尺寸下徽章没有超出卡片上缘");
        }

        Assert.Suite("Layout: 行高随字号变化并被钳制");
        {
            Assert.Near(26.0, KillCardLayoutMath.RowHeightFor(20), 0.001, "字号 20 → 行高 26");
            Assert.Near(KillCardLayoutMath.MinRowHeight, KillCardLayoutMath.RowHeightFor(1), 0.001, "过小字号被钳到下限");
            Assert.Near(KillCardLayoutMath.MaxRowHeight, KillCardLayoutMath.RowHeightFor(5000), 0.001, "离谱字号被钳到上限，不会把整块 UI 撞出屏幕");
            Assert.Greater(KillCardLayoutMath.RowHeightFor(30), KillCardLayoutMath.RowHeightFor(20), "字号越大行越高");
        }
    }
}
