namespace AyaHitmarker.Ui;

/// <summary>
/// 击杀卡片的**纯数学**布局输入。
///
/// 刻意只有 <c>float</c>/<c>int</c>，不引用 <c>UnityEngine</c>：这样布局可以脱离游戏程序集
/// 被 <c>tests/AyaHitmarker.LogicTests</c> 直接验证（见 AGENTS.md §2）。
/// </summary>
internal readonly struct KillCardLayoutInput
{
    internal readonly float SkullSize;

    /// <summary>相邻两行之间的垂直空隙。</summary>
    internal readonly float LineSpacing;

    internal readonly float AchievementHeight;
    internal readonly float AchievementVerticalOffset;

    /// <summary>连杀进度条的高度；0 = 不占位（关掉进度条时名字 / 详情行不会凭空多出一条空隙）。</summary>
    internal readonly float StreakBarHeight;

    /// <summary>骷髅行下缘到进度条上缘的间距。</summary>
    internal readonly float StreakBarGap;

    internal readonly int FontFaction;
    internal readonly int FontName;
    internal readonly int FontDetails;
    internal readonly int FontScore;
    internal readonly int FontAchievement;

    internal KillCardLayoutInput(
        float skullSize,
        float lineSpacing,
        float achievementHeight,
        float achievementVerticalOffset,
        float streakBarHeight,
        float streakBarGap,
        int fontFaction,
        int fontName,
        int fontDetails,
        int fontScore,
        int fontAchievement)
    {
        SkullSize = skullSize;
        LineSpacing = lineSpacing;
        AchievementHeight = achievementHeight;
        AchievementVerticalOffset = achievementVerticalOffset;
        StreakBarHeight = streakBarHeight;
        StreakBarGap = streakBarGap;
        FontFaction = fontFaction;
        FontName = fontName;
        FontDetails = fontDetails;
        FontScore = fontScore;
        FontAchievement = fontAchievement;
    }
}

/// <summary>
/// 布局结果。所有 Y 都是**相对锚点**的偏移，正数 = 在锚点上方（canvas 局部坐标，Y 向上为正）。
/// 旧项目是 IMGUI 的屏幕坐标（Y 向下），换算在调用方（<c>Anchor()</c>）一次做完，这里不再掺符号。
/// </summary>
internal readonly struct KillCardLayout
{
    /// <summary>每一行的统一高度（所有行共用，保证右对齐时视觉基线一致）。</summary>
    internal readonly float LineHeight;

    /// <summary>骷髅行中心（恒为 0，写出来是为了让行序自解释）。</summary>
    internal readonly float SkullCenterY;

    internal readonly float FactionY;
    internal readonly float NameY;
    internal readonly float DetailsY;
    internal readonly float AchievementY;

    /// <summary>连杀进度条中心；进度条关闭时与 <see cref="SkullCenterY"/> 重合（不显示）。</summary>
    internal readonly float StreakBarY;

    internal KillCardLayout(
        float lineHeight,
        float skullCenterY,
        float factionY,
        float nameY,
        float detailsY,
        float achievementY,
        float streakBarY)
    {
        LineHeight = lineHeight;
        SkullCenterY = skullCenterY;
        FactionY = factionY;
        NameY = nameY;
        DetailsY = detailsY;
        AchievementY = achievementY;
        StreakBarY = streakBarY;
    }
}

internal static class KillCardLayoutMath
{
    /// <summary>行高的上下限，防止配置填出离谱的值把整块 UI 撑爆或压扁。</summary>
    internal const float MinLineHeight = 14f;
    internal const float MaxLineHeight = 200f;

    /// <summary>
    /// 计算卡片各行位置。
    ///
    /// 行序（从下到上）：详情 → 名字 → 连杀进度条 → 骷髅 → 阵营 → 成就缎带。
    /// 相邻元素的间距恒为 <see cref="KillCardLayoutInput.LineSpacing"/>（进度条用 <see cref="KillCardLayoutInput.StreakBarGap"/>），
    /// 这是可以被离线验证的不变量。
    /// </summary>
    internal static KillCardLayout Compute(in KillCardLayoutInput input)
    {
        float skullSize = Max(8f, input.SkullSize);
        float spacing = input.LineSpacing;

        float maxFont = Max(
            Max(input.FontFaction, input.FontName),
            Max(input.FontDetails, Max(input.FontScore, input.FontAchievement)));
        float lineHeight = Clamp(maxFont * 1.2f, MinLineHeight, MaxLineHeight);

        float skullCenterY = 0f;

        float factionY = skullSize * 0.5f + spacing + lineHeight * 0.5f;

        float barHeight = Max(0f, input.StreakBarHeight);
        float barGap = Max(0f, input.StreakBarGap);
        float streakBarY = -(skullSize * 0.5f + barGap + barHeight * 0.5f);
        float barBlock = barHeight > 0f ? barGap + barHeight : 0f;

        float nameY = -(skullSize * 0.5f + barBlock + spacing + lineHeight * 0.5f);
        float detailsY = nameY - (spacing + lineHeight);

        float achievementHeight = Max(8f, input.AchievementHeight);
        float achievementY = factionY + lineHeight * 0.5f + spacing + achievementHeight * 0.5f
                             + input.AchievementVerticalOffset;

        return new KillCardLayout(lineHeight, skullCenterY, factionY, nameY, detailsY, achievementY, streakBarY);
    }

    private static float Max(float a, float b) => a > b ? a : b;
    private static int Max(int a, int b) => a > b ? a : b;

    private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
}
