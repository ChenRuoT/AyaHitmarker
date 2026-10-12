namespace AyaHitmarker.Ui;

/// <summary>
/// 击杀卡片的**纯数学**布局输入。
///
/// 刻意只有 <c>float</c>/<c>int</c>/<c>bool</c>，不引用 <c>UnityEngine</c>：这样布局可以脱离游戏程序集
/// 被 <c>tests/AyaHitmarker.LogicTests</c> 直接验证（见 AGENTS.md §2）。
///
/// 坐标约定：画布局部坐标，Y 向上为正；<b>脊线（spine）在 x = 0</b>，
/// 骷髅 / 名字 / 标签 / 详情都在脊线左边<b>右对齐</b>，分数从脊线往右长 —— 这是战地 6 卡片的骨架。
/// 主行（骷髅 + 分数）的中心恒在 y = 0，其余行相对它上下排开。
/// </summary>
internal struct KillCardLayoutInput
{
    // ---- 尺寸 ----
    public float SkullSize;
    public float IconGap;
    public float RowGap;
    public float BarHeight;
    public float BarGap;
    public float BannerHeight;
    public float NameHeight;
    public float TagHeight;
    public float DetailsHeight;
    public float BadgeSize;
    public float BadgeGap;

    // ---- 哪些行存在（不存在的行不占位，下面的行会往上补）----
    public bool HasBanner;
    public bool HasBar;
    public bool HasName;
    public bool HasTags;
    public bool HasDetails;
    public bool HasBadge;

    // ---- 实测宽度（每杀量一次）----
    public int SkullCount;
    public float BannerWidth;
    public float NameWidth;
    public float TagsWidth;
    public float DetailsWidth;

    /// <summary>横幅右缘超出脊线的距离（BF6 里横幅的骷髅徽记会探到分数上方）。</summary>
    public float BannerOverhang;
}

/// <summary>布局结果。所有 Y 都是相对锚点的偏移（Y 向上为正），X 相对脊线（脊线 = 0，向左为负）。</summary>
internal readonly struct KillCardLayout
{
    internal readonly float MainY;
    internal readonly float BannerY;
    internal readonly float BarY;
    internal readonly float NameY;
    internal readonly float TagY;
    internal readonly float DetailsY;

    /// <summary>相邻两个骷髅中心的水平间距。</summary>
    internal readonly float SkullPitch;

    /// <summary>整张卡片内容的左缘（负数）；不含军衔徽章。</summary>
    internal readonly float LeftEdge;

    internal readonly float BadgeX;
    internal readonly float BadgeY;

    /// <summary>整张卡片（含徽章）的上缘 / 下缘。</summary>
    internal readonly float Top;
    internal readonly float Bottom;

    private readonly float _skullSize;

    internal KillCardLayout(
        float mainY, float bannerY, float barY, float nameY, float tagY, float detailsY,
        float skullPitch, float leftEdge, float badgeX, float badgeY, float top, float bottom, float skullSize)
    {
        MainY = mainY;
        BannerY = bannerY;
        BarY = barY;
        NameY = nameY;
        TagY = tagY;
        DetailsY = detailsY;
        SkullPitch = skullPitch;
        LeftEdge = leftEdge;
        BadgeX = badgeX;
        BadgeY = badgeY;
        Top = top;
        Bottom = bottom;
        _skullSize = skullSize;
    }

    /// <summary>第 <paramref name="index"/> 个骷髅的中心 X（0 = 最新那个，紧贴脊线）。</summary>
    internal float SkullCenterX(int index) => -(_skullSize * 0.5f) - index * SkullPitch;
}

internal static class KillCardLayoutMath
{
    /// <summary>行高的上下限，防止配置填出离谱的值把整块 UI 撑爆或压扁。</summary>
    internal const float MinRowHeight = 12f;
    internal const float MaxRowHeight = 200f;

    /// <summary>字号 → 行高。Bender 的上下行距偏大，1.3 倍刚好不挤也不松。</summary>
    internal static float RowHeightFor(int fontSize)
        => Clamp(fontSize * 1.3f, MinRowHeight, MaxRowHeight);

    /// <summary>骷髅中心间距：图标之间只留 <c>iconGap</c>，不再像旧版那样每个都隔一个身位。</summary>
    internal static float SkullPitchFor(float skullSize, float iconGap)
        => Max(8f, skullSize) * 0.9f + Max(0f, iconGap);

    /// <summary>
    /// 计算卡片各行位置。
    ///
    /// 行序（从上到下）：横幅 → <b>主行（骷髅 + 分数，中心 y=0）</b> → 连杀进度条 → 名字 → 标签 → 详情。
    /// 不存在的行不占位；相邻行的间距恒为 <see cref="KillCardLayoutInput.RowGap"/>
    /// （进度条与主行之间用 <see cref="KillCardLayoutInput.BarGap"/>），这些都是可被离线验证的不变量。
    /// 军衔徽章贴在整张卡片内容的最左边，竖直方向居中在「主行 + 名字 + 标签」这块身份区上。
    /// </summary>
    internal static KillCardLayout Compute(in KillCardLayoutInput input)
    {
        float skull = Max(8f, input.SkullSize);
        float rowGap = Max(0f, input.RowGap);
        float pitch = SkullPitchFor(skull, input.IconGap);

        float mainTop = skull * 0.5f;
        float mainBottom = -skull * 0.5f;

        // ---- 上方：横幅 ----
        float bannerY = 0f;
        float top = mainTop;
        if (input.HasBanner)
        {
            float h = Max(8f, input.BannerHeight);
            bannerY = mainTop + rowGap + h * 0.5f;
            top = bannerY + h * 0.5f;
        }

        // ---- 下方：进度条 → 名字 → 标签 → 详情，逐行往下垒 ----
        float cursor = mainBottom;

        float barY = 0f;
        if (input.HasBar)
        {
            float h = Max(1f, input.BarHeight);
            barY = cursor - Max(0f, input.BarGap) - h * 0.5f;
            cursor = barY - h * 0.5f;
        }

        float nameY = 0f;
        float identityBottom = mainBottom;
        if (input.HasName)
        {
            float h = Max(8f, input.NameHeight);
            nameY = cursor - rowGap - h * 0.5f;
            cursor = nameY - h * 0.5f;
            identityBottom = cursor;
        }

        float tagY = 0f;
        if (input.HasTags)
        {
            float h = Max(8f, input.TagHeight);
            tagY = cursor - rowGap - h * 0.5f;
            cursor = tagY - h * 0.5f;
            identityBottom = cursor;
        }

        float detailsY = 0f;
        if (input.HasDetails)
        {
            float h = Max(8f, input.DetailsHeight);
            detailsY = cursor - rowGap - h * 0.5f;
            cursor = detailsY - h * 0.5f;
        }

        float bottom = cursor;

        // ---- 水平：内容左缘 = 各行向左伸得最远的那个 ----
        // 骷髅被关掉（SkullCount = 0）时不预留骷髅的宽度，否则军衔徽章会在短名字旁边凭空多空出一个骷髅
        int count = input.SkullCount < 0 ? 0 : input.SkullCount;
        float skullsWidth = count > 0 ? skull + (count - 1) * pitch : 0f;
        float leftExtent = skullsWidth;
        if (input.HasName) leftExtent = Max(leftExtent, input.NameWidth);
        if (input.HasTags) leftExtent = Max(leftExtent, input.TagsWidth);
        if (input.HasDetails) leftExtent = Max(leftExtent, input.DetailsWidth);
        if (input.HasBanner) leftExtent = Max(leftExtent, input.BannerWidth - Max(0f, input.BannerOverhang));
        float leftEdge = -leftExtent;

        // ---- 军衔徽章 ----
        float badgeX = 0f;
        float badgeY = 0f;
        if (input.HasBadge)
        {
            float badge = Max(8f, input.BadgeSize);
            badgeX = leftEdge - Max(0f, input.BadgeGap) - badge * 0.5f;
            badgeY = (mainTop + identityBottom) * 0.5f;

            // 徽章比身份区还高时，宁可往下探，也别顶到横幅上
            float badgeTop = badgeY + badge * 0.5f;
            float limit = mainTop + rowGap;
            if (badgeTop > limit) badgeY -= badgeTop - limit;

            float badgeBottom = badgeY - badge * 0.5f;
            float badgeTopFinal = badgeY + badge * 0.5f;
            if (badgeBottom < bottom) bottom = badgeBottom;
            if (badgeTopFinal > top) top = badgeTopFinal;
        }

        return new KillCardLayout(0f, bannerY, barY, nameY, tagY, detailsY, pitch, leftEdge, badgeX, badgeY, top, bottom, skull);
    }

    private static float Max(float a, float b) => a > b ? a : b;

    private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
}
