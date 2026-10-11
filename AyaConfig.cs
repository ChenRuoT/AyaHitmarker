using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP.Configuration;
using AyaHitmarker.Game;
using UnityEngine;

namespace AyaHitmarker;

/// <summary>音效的空间模式。Meta XR Audio 在这个版本接管了空间音频，2D 源在某些配置下会不出声。</summary>
internal enum SpatialMode
{
    /// <summary>2D（旧 Mono 项目就是这么做并且能出声）。</summary>
    Flat2D = 0,
    /// <summary>3D，位置跟随主相机 —— 走游戏的空间化管线（Meta XR Audio）。2D 无声时改这个。</summary>
    FollowCamera3D = 1,
}

internal enum DamageNumberMode
{
    /// <summary>只显示打进肉体的部分（旧 Mono 项目的行为）。打中护甲且未穿透时 DidBodyDamage 为 0，数字会被跳过。</summary>
    BodyOnly = 0,
    /// <summary>肉体 + 护甲，即本次命中造成的总伤害。</summary>
    Total = 1,
}

/// <summary>
/// 全部可调项。
///
/// **默认值对齐旧 Mono 项目实际使用的 <c>com.shiunaya.simplehitmarker.cfg</c>**
/// （不是它的代码默认值 —— 是那份已经调过的实机配置）。
/// UI 每帧读 config，不要缓存值。
/// </summary>
internal sealed class AyaConfig
{
    // ---- 0. 诊断 ----
    internal ConfigEntry<bool> DebugEnabled { get; }
    internal ConfigEntry<bool> TestHotkeysEnabled { get; }
    internal ConfigEntry<KeyboardShortcut> TestHitKey { get; }
    internal ConfigEntry<KeyboardShortcut> TestKillKey { get; }
    internal ConfigEntry<KeyboardShortcut> TestDeathKey { get; }

    // ---- 1. 命中提示 ----
    internal ConfigEntry<bool> HitMarkerEnabled { get; }
    internal ConfigEntry<float> HitBaseSize { get; }
    internal ConfigEntry<float> HitDuration { get; }
    internal ConfigEntry<float> HitMarkerAnimationScale { get; }
    internal ConfigEntry<string> HitMarkerCenterOffset { get; }
    internal ConfigEntry<string> HitMarkerColor { get; }
    internal ConfigEntry<bool> HitMarkerAtHitPoint { get; }

    // ---- 2. 伤害数字 ----
    internal ConfigEntry<bool> DamageTextEnabled { get; }
    internal ConfigEntry<DamageNumberMode> DamageTextMode { get; }
    internal ConfigEntry<int> DamageTextSize { get; }
    internal ConfigEntry<string> DamageTextColor { get; }
    internal ConfigEntry<string> DamageTextOutlineColor { get; }
    internal ConfigEntry<string> DamageTextHeadshotOutlineColor { get; }
    internal ConfigEntry<float> DamageTextOutlineWidth { get; }
    internal ConfigEntry<float> DamageTextPadding { get; }
    internal ConfigEntry<float> DamageMultiTextPadding { get; }
    internal ConfigEntry<float> DamageTextLifetime { get; }

    // ---- 3. 击杀播报 ----
    internal ConfigEntry<bool> KillFeedEnabled { get; }
    internal ConfigEntry<float> KillFeedHorizontalOffset { get; }
    internal ConfigEntry<float> KillFeedVerticalOffset { get; }
    internal ConfigEntry<float> KillFeedBlockWidth { get; }
    internal ConfigEntry<float> KillFeedLineSpacing { get; }
    internal ConfigEntry<float> KillFeedDuration { get; }

    internal ConfigEntry<bool> ShowSkull { get; }
    internal ConfigEntry<float> SkullSize { get; }
    internal ConfigEntry<float> SkullSpacing { get; }
    internal ConfigEntry<float> SkullDisplayDuration { get; }
    internal ConfigEntry<float> SkullFadeDuration { get; }
    internal ConfigEntry<float> StreakWindow { get; }
    internal ConfigEntry<float> SkullPushAnimationSpeed { get; }
    internal ConfigEntry<int> KillFeedMaxSkulls { get; }

    // ---- 3. 击杀播报：卡片动效 ----
    internal ConfigEntry<float> CardEnterDuration { get; }
    internal ConfigEntry<float> CardEnterDistance { get; }
    internal ConfigEntry<float> CardRowStagger { get; }
    internal ConfigEntry<float> CardExitDuration { get; }
    internal ConfigEntry<float> SkullPopScale { get; }
    internal ConfigEntry<float> SkullPopDuration { get; }

    // ---- 3. 击杀播报：得分弹窗 ----
    internal ConfigEntry<bool> ShowScore { get; }
    internal ConfigEntry<int> FontSizeScore { get; }
    internal ConfigEntry<string> ColorScore { get; }
    internal ConfigEntry<float> ScoreIconSize { get; }
    internal ConfigEntry<float> ScoreHorizontalOffset { get; }
    internal ConfigEntry<float> ScoreTextWidth { get; }
    internal ConfigEntry<float> ScoreRiseDistance { get; }
    internal ConfigEntry<float> ScoreCountDuration { get; }
    internal ConfigEntry<float> ScorePopScale { get; }
    internal ConfigEntry<float> ScorePopDuration { get; }

    // ---- 3. 击杀播报：成就缎带 ----
    internal ConfigEntry<bool> ShowAchievement { get; }
    internal ConfigEntry<int> FontSizeAchievement { get; }
    internal ConfigEntry<float> AchievementHeight { get; }
    internal ConfigEntry<float> AchievementPadding { get; }
    internal ConfigEntry<float> AchievementVerticalOffset { get; }
    internal ConfigEntry<float> AchievementEnterDuration { get; }
    internal ConfigEntry<float> AchievementHoldDuration { get; }
    internal ConfigEntry<float> AchievementFadeDuration { get; }
    internal ConfigEntry<string> ColorAchievementText { get; }
    internal ConfigEntry<string> ColorAchievementNormal { get; }
    internal ConfigEntry<string> ColorAchievementGood { get; }
    internal ConfigEntry<string> ColorAchievementGreat { get; }
    internal ConfigEntry<string> ColorAchievementEpic { get; }
    internal ConfigEntry<float> AchievementPulseScale { get; }

    // ---- 3. 击杀播报：连杀进度条 ----
    internal ConfigEntry<bool> ShowStreakBar { get; }
    internal ConfigEntry<float> StreakBarWidth { get; }
    internal ConfigEntry<float> StreakBarHeight { get; }
    internal ConfigEntry<float> StreakBarGap { get; }
    internal ConfigEntry<string> ColorStreakBar { get; }
    internal ConfigEntry<string> ColorStreakBarBack { get; }

    internal ConfigEntry<bool> ShowFaction { get; }
    internal ConfigEntry<bool> ShowFactionIcon { get; }
    internal ConfigEntry<bool> ShowPlayerName { get; }
    internal ConfigEntry<bool> ShowKillDetails { get; }
    internal ConfigEntry<bool> ShowKillMethod { get; }
    internal ConfigEntry<bool> ShowBossName { get; }

    internal ConfigEntry<int> FontSizeFaction { get; }
    internal ConfigEntry<int> FontSizePlayerName { get; }
    internal ConfigEntry<int> FontSizeKillDetails { get; }
    internal ConfigEntry<float> FactionIconSize { get; }
    internal ConfigEntry<float> FactionIconVerticalOffset { get; }
    internal ConfigEntry<float> NameOutlineThickness { get; }

    internal ConfigEntry<string> ColorFaction { get; }
    internal ConfigEntry<string> ColorPlayerName { get; }
    internal ConfigEntry<string> ColorKillDetails { get; }
    internal ConfigEntry<string> ColorUSEC { get; }
    internal ConfigEntry<string> ColorBEAR { get; }
    internal ConfigEntry<string> ColorSCAV { get; }
    internal ConfigEntry<string> ColorBOSS { get; }
    internal ConfigEntry<string> ColorFollower { get; }
    internal ConfigEntry<string> ColorRAIDER { get; }
    internal ConfigEntry<string> ColorRouges { get; }
    internal ConfigEntry<string> ColorSectant { get; }

    // ---- 3b. 击杀图标特效 ----
    internal ConfigEntry<bool> EnableRingEffect { get; }
    internal ConfigEntry<bool> RingHeadshotEnabled { get; }
    internal ConfigEntry<float> RingEffectDelay { get; }
    internal ConfigEntry<float> RingEffectDuration { get; }
    internal ConfigEntry<float> RingExplosionSecondRingDelay { get; }
    internal ConfigEntry<float> RingHeadshotRadius { get; }
    internal ConfigEntry<float> RingHeadshotThickness { get; }
    internal ConfigEntry<string> RingHeadshotColor { get; }
    internal ConfigEntry<float> RingCritRadius { get; }
    internal ConfigEntry<float> RingCritThickness { get; }
    internal ConfigEntry<string> RingCritColor { get; }
    internal ConfigEntry<float> RingExplosionRadius { get; }
    internal ConfigEntry<float> RingExplosionThickness { get; }
    internal ConfigEntry<string> RingExplosionColor { get; }
    internal ConfigEntry<bool> EnableIconGlow { get; }
    internal ConfigEntry<string> IconGlowColor { get; }
    internal ConfigEntry<float> IconGlowIntensity { get; }
    internal ConfigEntry<float> IconGlowSize { get; }

    // ---- 3c. 击杀成就 / 连杀 ----
    internal ConfigEntry<float> RevengeWindow { get; }
    internal ConfigEntry<float> MultiKillWindow { get; }
    internal ConfigEntry<float> GunnerWindow { get; }
    internal ConfigEntry<float> MarauderWindow { get; }
    internal ConfigEntry<float> LongShotDistance { get; }
    internal ConfigEntry<float> MarksmanDistance { get; }
    internal ConfigEntry<int> EliteLevel { get; }
    internal ConfigEntry<int> ScoreBase { get; }
    internal ConfigEntry<int> ScoreHeadshotBonus { get; }
    internal ConfigEntry<int> ScoreLongShotBonus { get; }
    internal ConfigEntry<int> ScoreRevengeBonus { get; }
    internal ConfigEntry<int> ScoreMultiKillBonus { get; }
    internal ConfigEntry<int> ScoreBossBonus { get; }

    // ---- 1. 命中提示（爆头变体 / 弹出）----
    internal ConfigEntry<string> HitMarkerHeadshotColor { get; }
    internal ConfigEntry<float> HitMarkerHeadshotScale { get; }
    internal ConfigEntry<float> HitMarkerPopScale { get; }
    internal ConfigEntry<float> HitMarkerPopDuration { get; }

    // ---- 2. 伤害数字（弹出）----
    internal ConfigEntry<float> DamageTextPopScale { get; }
    internal ConfigEntry<float> DamageTextPopDuration { get; }

    // ---- 4. 音效 ----
    internal ConfigEntry<bool> SoundEnabled { get; }
    internal ConfigEntry<bool> SoundHitEnabled { get; }
    internal ConfigEntry<bool> SoundKillEnabled { get; }
    internal ConfigEntry<float> SoundMasterVolume { get; }
    internal ConfigEntry<float> SoundHitVolume { get; }
    internal ConfigEntry<float> SoundHitHeadshotVolume { get; }
    internal ConfigEntry<float> SoundKillVolume { get; }
    internal ConfigEntry<float> SoundKillHeadshotVolume { get; }
    internal ConfigEntry<string> SoundFileHit { get; }
    internal ConfigEntry<string> SoundFileHitHeadshot { get; }
    internal ConfigEntry<string> SoundFileKill { get; }
    internal ConfigEntry<string> SoundFileKillHeadshot { get; }
    internal ConfigEntry<SpatialMode> SoundSpatialMode { get; }
    internal ConfigEntry<bool> SoundBypassEffects { get; }
    internal ConfigEntry<bool> SoundIgnoreListenerPause { get; }

    // ---- 5. 界面 ----
    internal ConfigEntry<float> UiScale { get; }
    internal ConfigEntry<int> UiSortingOrder { get; }
    internal ConfigEntry<string> UiFontName { get; }

    internal AyaConfig(ConfigFile cfg)
    {
        // ---------------------------------------------------------------- 0. 诊断
        DebugEnabled = cfg.Bind("0. 调试", "启用调试日志", false,
            new ConfigDescription("把每次命中/击杀/播放音效的原始字段写进 BepInEx/LogOutput.log。排查问题时再打开。"));
        TestHotkeysEnabled = cfg.Bind("0. 调试", "启用测试快捷键", true,
            new ConfigDescription("按下面的键立刻造一次命中/击杀反馈，不需要真的打架。与调试日志开关无关。"));
        TestHitKey = cfg.Bind("0. 调试", "测试命中快捷键", new KeyboardShortcut(KeyCode.F8));
        TestKillKey = cfg.Bind("0. 调试", "测试击杀快捷键", new KeyboardShortcut(KeyCode.F9));
        TestDeathKey = cfg.Bind("0. 调试", "测试阵亡快捷键", new KeyboardShortcut(KeyCode.F10));

        // ---------------------------------------------------------------- 1. 命中提示
        // 旧 cfg: 基础大小 71 / 显示时长 0.868 / 中心偏移 (0,5) / 动画缩放 1
        HitMarkerEnabled = cfg.Bind("1. 命中提示", "启用", true);
        HitBaseSize = cfg.Bind("1. 命中提示", "基础大小", 71f,
            new ConfigDescription("Icons/hit.png 的绘制尺寸（像素）。", null, new object[] { 8f, 512f }));
        HitDuration = cfg.Bind("1. 命中提示", "显示时长(秒)", 0.868f,
            new ConfigDescription("图标从出现到完全消失的时间。", null, new object[] { 0.05f, 5f }));
        HitMarkerAnimationScale = cfg.Bind("1. 命中提示", "动画缩放", 1f,
            new ConfigDescription("出现瞬间的比例，在显示时长内放大到 1.0。旧配置就是 1（即不做缩放动画）。", null, new object[] { 1f, 2f }));
        HitMarkerCenterOffset = cfg.Bind("1. 命中提示", "中心偏移(X,Y)", "0,5",
            new ConfigDescription("相对屏幕中心的像素偏移，负 Y 向上。只在「显示在命中点」关闭时生效。"));
        HitMarkerColor = cfg.Bind("1. 命中提示", "着色", "#FFFFFFFF",
            new ConfigDescription("对 hit.png 做整体着色，白色 = 保持原图。"));
        HitMarkerAtHitPoint = cfg.Bind("1. 命中提示", "显示在命中点", false,
            new ConfigDescription(
                "false（默认，与旧项目一致）= 固定在屏幕中心 + 上面的偏移；" +
                "true = 把 DamageInfo.HitPoint 投影到屏幕（准星实际落点）。" +
                "true 时若投影结果落在屏幕外会退回屏幕中心。"));
        HitMarkerHeadshotColor = cfg.Bind("1. 命中提示", "爆头着色", "#FF4D4DFF",
            new ConfigDescription("爆头命中时用这个颜色代替上面的「着色」。旧项目不改色，这里默认给个红色以加强爆头反馈。"));
        HitMarkerHeadshotScale = cfg.Bind("1. 命中提示", "爆头尺寸倍率", 1.15f,
            new ConfigDescription("爆头命中的图标尺寸乘数。", null, new object[] { 1f, 3f }));
        HitMarkerPopScale = cfg.Bind("1. 命中提示", "弹出比例", 1.25f,
            new ConfigDescription(
                "命中瞬间先放大到这个比例再回弹到 1（与旧项目的「动画缩放」叠加）。" +
                "1 = 不做弹出。", null, new object[] { 1f, 3f }));
        HitMarkerPopDuration = cfg.Bind("1. 命中提示", "弹出时长(秒)", 0.09f,
            new ConfigDescription("", null, new object[] { 0.01f, 1f }));

        // ---------------------------------------------------------------- 2. 伤害数字
        // 旧 cfg: 字号 21 / 间距 8 / 多段间距 12 / 颜色 FFFFFF / 描边白 25% 不透明 / 爆头描边 FF0000
        DamageTextEnabled = cfg.Bind("2. 伤害数字", "启用", true);
        DamageTextMode = cfg.Bind("2. 伤害数字", "取值", DamageNumberMode.BodyOnly,
            new ConfigDescription(
                "BodyOnly（默认，与旧项目一致）= 只算肉体伤害，打中护甲未穿透时数字为 0 → 不绘制；" +
                "Total = 肉体 + 护甲，任何命中都有数字。"));
        DamageTextSize = cfg.Bind("2. 伤害数字", "字号", 21,
            new ConfigDescription("", null, new object[] { 8, 120 }));
        DamageTextColor = cfg.Bind("2. 伤害数字", "颜色", "#FFFFFFFF");
        DamageTextOutlineColor = cfg.Bind("2. 伤害数字", "描边颜色", "#FFFFFF40",
            new ConfigDescription("旧配置是白色 ~25% 不透明。末两位是透明度，设成 00 等于不描边。"));
        DamageTextHeadshotOutlineColor = cfg.Bind("2. 伤害数字", "爆头描边颜色", "#FF0000FF");
        DamageTextOutlineWidth = cfg.Bind("2. 伤害数字", "描边宽度", 0.2f,
            new ConfigDescription("TMP 的归一化描边宽度（0 = 关闭，0.2 约等于旧项目的 1.4 像素）。", null, new object[] { 0f, 1f }));
        DamageTextPadding = cfg.Bind("2. 伤害数字", "距图标间距", 8f,
            new ConfigDescription("第一个数字相对命中图标右缘的间距。", null, new object[] { 0f, 200f }));
        DamageMultiTextPadding = cfg.Bind("2. 伤害数字", "多段伤害间距", 12f,
            new ConfigDescription("同一窗口内多个数字排成一行，这是彼此的间隔。", null, new object[] { 0f, 200f }));
        DamageTextLifetime = cfg.Bind("2. 伤害数字", "存活时长(秒)", 0.868f,
            new ConfigDescription("旧项目与命中图标共用时长；这里独立，想一致就设成和「显示时长」相同。", null, new object[] { 0.1f, 5f }));
        DamageTextPopScale = cfg.Bind("2. 伤害数字", "弹出比例", 1.3f,
            new ConfigDescription("数字出现瞬间的缩放比例，在「弹出时长」内回弹到 1。1 = 不做弹出。", null, new object[] { 1f, 3f }));
        DamageTextPopDuration = cfg.Bind("2. 伤害数字", "弹出时长(秒)", 0.12f,
            new ConfigDescription("", null, new object[] { 0.01f, 1f }));

        // ---------------------------------------------------------------- 3. 击杀播报
        // 旧 cfg: 水平偏移 107 / 垂直 0 / 行间距 0 / 内容宽度 420 / 显示时长 6
        KillFeedEnabled = cfg.Bind("3. 击杀信息", "启用", true);
        KillFeedHorizontalOffset = cfg.Bind("3. 击杀信息", "水平中心偏移", 107f,
            new ConfigDescription("锚点在屏幕中心**左侧**的像素数。正值向左，负值向右。", null, new object[] { -2000f, 2000f }));
        KillFeedVerticalOffset = cfg.Bind("3. 击杀信息", "垂直中心偏移", 0f,
            new ConfigDescription("正值向下，负值向上。", null, new object[] { -2000f, 2000f }));
        KillFeedBlockWidth = cfg.Bind("3. 击杀信息", "内容宽度", 420f,
            new ConfigDescription("右对齐文本的可用宽度。", null, new object[] { 100f, 1000f }));
        KillFeedLineSpacing = cfg.Bind("3. 击杀信息", "行间距", 0f,
            new ConfigDescription("", null, new object[] { 0f, 50f }));
        KillFeedDuration = cfg.Bind("3. 击杀信息", "显示时长(秒)", 6f,
            new ConfigDescription("文字存活总时长；最后「卡片淡出时长」会走淡出 + 轻微位移。", null, new object[] { 1f, 30f }));

        // ---- 卡片动效 ----
        CardEnterDuration = cfg.Bind("3. 击杀信息", "卡片入场时长(秒)", 0.18f,
            new ConfigDescription("每一行从「略偏移 + 透明」滑入到位的时间。", null, new object[] { 0f, 2f }));
        CardEnterDistance = cfg.Bind("3. 击杀信息", "卡片入场位移(像素)", 26f,
            new ConfigDescription("各行从右边多少像素处滑入（右对齐卡片的「飞入」方向）。", null, new object[] { 0f, 400f }));
        CardRowStagger = cfg.Bind("3. 击杀信息", "卡片行错开(秒)", 0.035f,
            new ConfigDescription("相邻两行的入场时间差，做出「自上而下依次落位」的层次感。", null, new object[] { 0f, 0.3f }));
        CardExitDuration = cfg.Bind("3. 击杀信息", "卡片淡出时长(秒)", 0.5f,
            new ConfigDescription("显示时长结束前多久开始淡出（含轻微的位移与缩小）。", null, new object[] { 0f, 5f }));
        SkullPopScale = cfg.Bind("3. 击杀信息", "骷髅弹出比例", 1.35f,
            new ConfigDescription("新骷髅出现瞬间的缩放比例（>1 = 先放大再回弹）。", null, new object[] { 1f, 3f }));
        SkullPopDuration = cfg.Bind("3. 击杀信息", "骷髅弹出时长(秒)", 0.16f,
            new ConfigDescription("", null, new object[] { 0.01f, 1f }));

        // 旧 cfg: 骷髅 40 / 间距 20 / 停留 2 / 淡出 0.3 / 连杀窗口 10 / 推挤速度 7
        ShowSkull = cfg.Bind("3. 击杀信息", "显示骷髅头", true);
        SkullSize = cfg.Bind("3. 击杀信息", "骷髅头大小", 40f,
            new ConfigDescription("", null, new object[] { 8f, 256f }));
        SkullSpacing = cfg.Bind("3. 击杀信息", "骷髅头间距", 20f,
            new ConfigDescription("连杀时已有骷髅向左推的距离增量（还会加上骷髅尺寸）。", null, new object[] { 0f, 200f }));
        SkullDisplayDuration = cfg.Bind("3. 击杀信息", "骷髅头显示时长(秒)", 2f,
            new ConfigDescription("", null, new object[] { 0.5f, 10f }));
        SkullFadeDuration = cfg.Bind("3. 击杀信息", "骷髅头淡出时长(秒)", 0.3f,
            new ConfigDescription("", null, new object[] { 0.1f, 2f }));
        StreakWindow = cfg.Bind("3. 击杀信息", "连杀时间窗口(秒)", 10f,
            new ConfigDescription("两次击杀间隔小于这个值就算连杀，会把之前的骷髅推向左。", null, new object[] { 1f, 60f }));
        SkullPushAnimationSpeed = cfg.Bind("3. 击杀信息", "骷髅头推挤动画速度", 7f,
            new ConfigDescription("", null, new object[] { 1f, 30f }));
        KillFeedMaxSkulls = cfg.Bind("3. 击杀信息", "最多骷髅数", 8,
            new ConfigDescription("", null, new object[] { 1, 24 }));

        ShowFaction = cfg.Bind("3. 击杀信息", "显示阵营文字", true);
        ShowFactionIcon = cfg.Bind("3. 击杀信息", "显示阵营图标", true,
            new ConfigDescription(
                "PMC 显示 Icons/RankIcons/RankN.png 军衔徽章；其他阵营没有徽章时这段宽度会自动让给文字。"));
        ShowPlayerName = cfg.Bind("3. 击杀信息", "显示玩家名字", true);
        ShowKillDetails = cfg.Bind("3. 击杀信息", "显示击杀详情", true);
        ShowKillMethod = cfg.Bind("3. 击杀信息", "显示击杀方式", true,
            new ConfigDescription("显示「近战 / 爆炸 / 狙击…」这类方式；枪械击杀不显示（默认方式，写了只是噪音）。"));
        ShowBossName = cfg.Bind("3. 击杀信息", "显示首领名称", true,
            new ConfigDescription("首领用「Killa / Glukhar / Shturman…」代替笼统的 BOSS。"));

        // 得分弹窗（战地式的「+100」）：取代旧版的「经验值」
        ShowScore = cfg.Bind("3. 击杀信息", "显示得分", true,
            new ConfigDescription("显示本次击杀的得分。数值来自「3c. 击杀成就」的公式，不再是硬编码的 100/150。"));
        FontSizeScore = cfg.Bind("3. 击杀信息", "得分字号", 28, new ConfigDescription("", null, new object[] { 8, 72 }));
        ScoreIconSize = cfg.Bind("3. 击杀信息", "得分图标大小", 24f,
            new ConfigDescription("得分前面的骷髅图标尺寸；0 = 不显示图标。", null, new object[] { 0f, 96f }));
        ScoreHorizontalOffset = cfg.Bind("3. 击杀信息", "得分水平偏移", 35f,
            new ConfigDescription("相对锚点向右的距离。", null, new object[] { -500f, 500f }));
        ScoreTextWidth = cfg.Bind("3. 击杀信息", "得分文字宽度", 180f, new ConfigDescription("", null, new object[] { 50f, 500f }));
        ScoreRiseDistance = cfg.Bind("3. 击杀信息", "得分上浮距离(像素)", 26f,
            new ConfigDescription("得分在存活期间向上漂移的距离（战地风格）。0 = 不漂移。", null, new object[] { 0f, 200f }));
        ScoreCountDuration = cfg.Bind("3. 击杀信息", "得分计数字时长(秒)", 0.35f,
            new ConfigDescription("从 0 滚到最终数值的时间；0 = 直接显示最终值。", null, new object[] { 0f, 2f }));
        ScorePopScale = cfg.Bind("3. 击杀信息", "得分弹出比例", 1.6f,
            new ConfigDescription("得分出现瞬间的缩放比例，在「得分弹出时长」内回弹到 1（同时作用于骷髅图标）。", null, new object[] { 1f, 3f }));
        ScorePopDuration = cfg.Bind("3. 击杀信息", "得分弹出时长(秒)", 0.22f,
            new ConfigDescription("", null, new object[] { 0.01f, 1f }));

        // 成就缎带（「复仇者 / 狂战士 / 双杀」）
        ShowAchievement = cfg.Bind("3. 击杀信息", "显示成就缎带", true,
            new ConfigDescription("仅在该次击杀触发成就时出现；普通击杀不画缎带。"));
        FontSizeAchievement = cfg.Bind("3. 击杀信息", "成就字号", 22, new ConfigDescription("", null, new object[] { 8, 72 }));
        AchievementHeight = cfg.Bind("3. 击杀信息", "成就缎带高度", 30f, new ConfigDescription("", null, new object[] { 8f, 120f }));
        AchievementPadding = cfg.Bind("3. 击杀信息", "成就缎带左右留白", 14f,
            new ConfigDescription("缎带比文字宽出来的部分。", null, new object[] { 0f, 120f }));
        AchievementVerticalOffset = cfg.Bind("3. 击杀信息", "成就缎带垂直偏移", 0f,
            new ConfigDescription("相对阵营行再往上多少像素。", null, new object[] { -400f, 400f }));
        AchievementEnterDuration = cfg.Bind("3. 击杀信息", "成就出现时长(秒)", 0.24f,
            new ConfigDescription("缎带从左往右展开 + 回弹的时间。", null, new object[] { 0.01f, 3f }));
        AchievementHoldDuration = cfg.Bind("3. 击杀信息", "成就停留时长(秒)", 1.6f,
            new ConfigDescription("", null, new object[] { 0f, 20f }));
        AchievementFadeDuration = cfg.Bind("3. 击杀信息", "成就淡出时长(秒)", 0.35f,
            new ConfigDescription("", null, new object[] { 0.01f, 5f }));
        AchievementPulseScale = cfg.Bind("3. 击杀信息", "成就脉冲幅度", 0.04f,
            new ConfigDescription("停留期间缎带宽度轻微呼吸的幅度（0 = 关）。", null, new object[] { 0f, 0.3f }));

        // 连杀进度条
        ShowStreakBar = cfg.Bind("3. 击杀信息", "显示连杀进度条", true,
            new ConfigDescription("骷髅行下面那条细条，显示距离下一档连杀称号的进度。"));
        StreakBarWidth = cfg.Bind("3. 击杀信息", "进度条宽度", 120f, new ConfigDescription("", null, new object[] { 20f, 600f }));
        StreakBarHeight = cfg.Bind("3. 击杀信息", "进度条高度", 4f, new ConfigDescription("", null, new object[] { 1f, 20f }));
        StreakBarGap = cfg.Bind("3. 击杀信息", "进度条距骷髅间距", 4f,
            new ConfigDescription("骷髅行下缘到进度条的间距；名字 / 详情行会自动往下让出这块空间。", null, new object[] { 0f, 60f }));

        // 旧 cfg: 阵营 20 / 得分 20 / 名字 18 / 详情 20
        FontSizeFaction = cfg.Bind("3. 击杀信息", "阵营文字字号", 20, new ConfigDescription("", null, new object[] { 8, 48 }));
        FontSizePlayerName = cfg.Bind("3. 击杀信息", "玩家名字号", 18, new ConfigDescription("", null, new object[] { 8, 48 }));
        FontSizeKillDetails = cfg.Bind("3. 击杀信息", "击杀详情字号", 20, new ConfigDescription("", null, new object[] { 8, 48 }));
        FactionIconSize = cfg.Bind("3. 击杀信息", "阵营图标大小", 40f,
            new ConfigDescription(
                "PMC 军衔徽章尺寸。默认值跟行高（~34px）对齐 —— 旧项目 cfg 里的 86 是从没显示过的时候填的，" +
                "真渲染出来会盖到下面的骷髅行。", null, new object[] { 8f, 128f }));
        FactionIconVerticalOffset = cfg.Bind("3. 击杀信息", "阵营图标垂直偏移", 0f,
            new ConfigDescription(
                "相对阵营行中心上下微调（负值向上）。默认 0 = 与阵营文字同一基线。",
                null, new object[] { -200f, 200f }));
        NameOutlineThickness = cfg.Bind("3. 击杀信息", "玩家名字描边厚度", 1.1f,
            new ConfigDescription("0 = 不描边；按「像素 / 8」归一化喂给 TMP。", null, new object[] { 0f, 10f }));

        // 旧 cfg 的 Color 是 RGBA hex，本项目的解析器同样是 RRGGBBAA，可直接照搬
        ColorFaction = cfg.Bind("3. 击杀信息", "阵营文字颜色", "#FFFFFFFF");
        ColorPlayerName = cfg.Bind("3. 击杀信息", "玩家名字颜色", "#FF000FFF");
        ColorKillDetails = cfg.Bind("3. 击杀信息", "击杀详情颜色", "#CCCCCCFF");
        ColorScore = cfg.Bind("3. 击杀信息", "得分颜色", "#FFE08AFF");
        ColorAchievementText = cfg.Bind("3. 击杀信息", "成就文字颜色", "#FFFFFFFF");
        ColorAchievementNormal = cfg.Bind("3. 击杀信息", "成就底色_普通", "#46505CFF");
        ColorAchievementGood = cfg.Bind("3. 击杀信息", "成就底色_优秀", "#2F6FB5FF");
        ColorAchievementGreat = cfg.Bind("3. 击杀信息", "成就底色_精英", "#C2681AFF");
        ColorAchievementEpic = cfg.Bind("3. 击杀信息", "成就底色_史诗", "#A32434FF");
        ColorStreakBar = cfg.Bind("3. 击杀信息", "进度条颜色", "#FF7A18FF");
        ColorStreakBarBack = cfg.Bind("3. 击杀信息", "进度条背景色", "#00000099");
        ColorUSEC = cfg.Bind("3. 击杀信息", "颜色_USEC", "#003399FF");
        ColorBEAR = cfg.Bind("3. 击杀信息", "颜色_BEAR", "#FF8C00FF");
        ColorSCAV = cfg.Bind("3. 击杀信息", "颜色_SCAV", "#FFE600FF");
        ColorBOSS = cfg.Bind("3. 击杀信息", "颜色_BOSS", "#FF0000FF");
        ColorFollower = cfg.Bind("3. 击杀信息", "颜色_Follower", "#E63333FF");
        ColorRAIDER = cfg.Bind("3. 击杀信息", "颜色_RAIDER", "#FF66B2FF");
        ColorRouges = cfg.Bind("3. 击杀信息", "颜色_Rouges", "#333333FF");
        ColorSectant = cfg.Bind("3. 击杀信息", "颜色_Sectant", "#006600FF");

        // ---------------------------------------------------------------- 3b. 击杀图标特效
        EnableRingEffect = cfg.Bind("3b. 击杀特效", "启用环形特效", true);
        RingHeadshotEnabled = cfg.Bind("3b. 击杀特效", "爆头时触发", true);
        RingEffectDelay = cfg.Bind("3b. 击杀特效", "延迟(秒)", 0.1f);
        RingEffectDuration = cfg.Bind("3b. 击杀特效", "时长(秒)", 0.3f);
        RingExplosionSecondRingDelay = cfg.Bind("3b. 击杀特效", "爆炸第二环延迟", 0.1f);
        RingHeadshotRadius = cfg.Bind("3b. 击杀特效", "爆头半径", 42f);
        RingHeadshotThickness = cfg.Bind("3b. 击杀特效", "爆头粗细", 3f);
        RingHeadshotColor = cfg.Bind("3b. 击杀特效", "爆头颜色", "#D4B800FF");
        RingCritRadius = cfg.Bind("3b. 击杀特效", "暴击半径", 42f);
        RingCritThickness = cfg.Bind("3b. 击杀特效", "暴击粗细", 1.8f);
        RingCritColor = cfg.Bind("3b. 击杀特效", "暴击颜色", "#9CCB65FF");
        RingExplosionRadius = cfg.Bind("3b. 击杀特效", "爆炸半径", 42f);
        RingExplosionThickness = cfg.Bind("3b. 击杀特效", "爆炸粗细", 5.4f);
        RingExplosionColor = cfg.Bind("3b. 击杀特效", "爆炸颜色", "#F77F00FF");

        EnableIconGlow = cfg.Bind("3b. 击杀特效", "启用图标辉光", true);
        IconGlowColor = cfg.Bind("3b. 击杀特效", "辉光颜色", "#FFFFFFFF");
        IconGlowIntensity = cfg.Bind("3b. 击杀特效", "辉光强度", 0.45f,
            new ConfigDescription("", null, new object[] { 0f, 2f }));
        IconGlowSize = cfg.Bind("3b. 击杀特效", "辉光扩散(像素)", 4f,
            new ConfigDescription("改动会触发辉光纹理重烘（有缓存）。", null, new object[] { 0f, 32f }));

        // ---------------------------------------------------------------- 3c. 击杀成就 / 连杀
        // 「连杀重置窗口」复用 3. 击杀信息 里的「连杀时间窗口」，这里不重复给。
        RevengeWindow = cfg.Bind("3c. 击杀成就", "复仇判定窗口(秒)", 120f,
            new ConfigDescription("记下「谁杀了我」，在这个时间窗内击杀同一人 = 复仇者。", null, new object[] { 5f, 3600f }));
        MultiKillWindow = cfg.Bind("3c. 击杀成就", "多杀判定窗口(秒)", 2.5f,
            new ConfigDescription("两次击杀间隔小于这个值 → 双杀 / 三杀 / 多杀。", null, new object[] { 0.5f, 30f }));
        GunnerWindow = cfg.Bind("3c. 击杀成就", "枪手判定窗口(秒)", 4f,
            new ConfigDescription("BF6「枪手」：窗口内用**不同武器**再杀一人。", null, new object[] { 0.5f, 60f }));
        MarauderWindow = cfg.Bind("3c. 击杀成就", "掠夺者判定窗口(秒)", 8f,
            new ConfigDescription("BF6「掠夺者」：第 4 杀之后窗口内再杀一人。", null, new object[] { 0.5f, 60f }));
        LongShotDistance = cfg.Bind("3c. 击杀成就", "远程判定距离(米)", 100f,
            new ConfigDescription("", null, new object[] { 10f, 500f }));
        MarksmanDistance = cfg.Bind("3c. 击杀成就", "神射手判定距离(米)", 200f,
            new ConfigDescription("「爆头 + 达到这个距离」= 神射手。", null, new object[] { 10f, 500f }));
        EliteLevel = cfg.Bind("3c. 击杀成就", "精锐等级阈值", 40,
            new ConfigDescription("击杀等级 ≥ 该值的 PMC 记为「精锐猎杀」。", null, new object[] { 1, 79 }));

        ScoreBase = cfg.Bind("3c. 击杀成就", "得分基础值", 100, new ConfigDescription("", null, new object[] { 0, 10000 }));
        ScoreHeadshotBonus = cfg.Bind("3c. 击杀成就", "得分_爆头加成", 50, new ConfigDescription("", null, new object[] { 0, 10000 }));
        ScoreLongShotBonus = cfg.Bind("3c. 击杀成就", "得分_远程加成", 50,
            new ConfigDescription("距离 ≥ 上面「远程判定距离」时加。", null, new object[] { 0, 10000 }));
        ScoreRevengeBonus = cfg.Bind("3c. 击杀成就", "得分_复仇加成", 25, new ConfigDescription("", null, new object[] { 0, 10000 }));
        ScoreMultiKillBonus = cfg.Bind("3c. 击杀成就", "得分_多杀加成", 25,
            new ConfigDescription("每多一个连杀加一份。", null, new object[] { 0, 10000 }));
        ScoreBossBonus = cfg.Bind("3c. 击杀成就", "得分_首领加成", 200, new ConfigDescription("", null, new object[] { 0, 10000 }));

        // ---------------------------------------------------------------- 4. 音效
        // 旧 cfg: 命中 0.5 / 击杀 0.7，爆头不做区分
        SoundEnabled = cfg.Bind("4. 音频", "启用音效", true);
        SoundHitEnabled = cfg.Bind("4. 音频", "启用命中音效", true);
        SoundKillEnabled = cfg.Bind("4. 音频", "启用击杀音效", true);
        // 注意：音量必须用 AcceptableValueRange 而不是 ConfigDescription 的 tags。
        // tags 只给 ConfigurationManager 画滑条用，**不做钳制** —— 曾经有人把音量填成 80，
        // 于是 vol 算出来 6400，全靠代码里 Mathf.Clamp 才没变成噪音。
        var unitRange = new AcceptableValueRange<float>(0f, 1f);
        SoundMasterVolume = cfg.Bind("4. 音频", "总音量", 1f, new ConfigDescription("0..1", unitRange));
        SoundHitVolume = cfg.Bind("4. 音频", "命中音效音量", 0.5f, new ConfigDescription("0..1", unitRange));
        SoundHitHeadshotVolume = cfg.Bind("4. 音频", "爆头命中音效音量", 0.5f, new ConfigDescription("0..1", unitRange));
        SoundKillVolume = cfg.Bind("4. 音频", "击杀音效音量", 0.7f, new ConfigDescription("0..1", unitRange));
        SoundKillHeadshotVolume = cfg.Bind("4. 音频", "爆头击杀音效音量", 0.7f, new ConfigDescription("0..1", unitRange));

        SoundFileHit = cfg.Bind("4. 音频", "自定义:命中", "", new ConfigDescription(SoundHint("hit")));
        SoundFileHitHeadshot = cfg.Bind("4. 音频", "自定义:爆头命中", "", new ConfigDescription(SoundHint("headshot_hit")));
        SoundFileKill = cfg.Bind("4. 音频", "自定义:击杀", "", new ConfigDescription(SoundHint("kill")));
        SoundFileKillHeadshot = cfg.Bind("4. 音频", "自定义:爆头击杀", "", new ConfigDescription(SoundHint("headshot_kill")));

        // ---- 排障开关：默认全部保持旧 Mono 项目的配置（都不开） ----
        SoundSpatialMode = cfg.Bind("4. 音频", "空间模式", SpatialMode.Flat2D,
            new ConfigDescription(
                "2D = 旧项目做法。这个版本的 EFT 用 Meta XR Audio 做空间音频，" +
                "如果 2D 完全没声音，改成「3D 跟随相机」走游戏的声场管线。"));
        SoundBypassEffects = cfg.Bind("4. 音频", "绕过效果链(排障)", false,
            new ConfigDescription(
                "对应 AudioSource.bypassEffects + bypassListenerEffects + bypassReverbZones。" +
                "**默认关闭** —— Unity 文档说明它会绕过 AudioListener 上的效果，而这正是 " +
                "Meta XR Audio 渲染出声的环节，开了可能直接没声。"));
        SoundIgnoreListenerPause = cfg.Bind("4. 音频", "无视 listener 暂停(排障)", false,
            new ConfigDescription("对应 AudioSource.ignoreListenerPause。默认关闭。"));

        // ---------------------------------------------------------------- 5. 界面
        UiScale = cfg.Bind("5. 界面", "UI 缩放", 1f,
            new ConfigDescription("整体缩放命中提示 / 伤害数字 / 击杀播报。", null, new object[] { 0.5f, 3f }));
        UiSortingOrder = cfg.Bind("5. 界面", "画布层级", 9000,
            new ConfigDescription("越大越靠前。EFT 自己的 HUD 一般在这个值以下。"));
        UiFontName = cfg.Bind("5. 界面", "字体名称", "",
            new ConfigDescription(
                "留空 = 用游戏自带的 TMP 字体（实测可用，含西里尔字母）。" +
                "想显示中文就填系统字体名，比如 Microsoft YaHei（会运行时创建 TMP 字体资源）。"));
    }

    /// <summary>
    /// 把配置读成判定引擎用的阈值结构。每次都重新构造（纯结构体拷贝，开销可忽略），
    /// 这样 ConfigurationManager 里改完立即生效。
    /// </summary>
    internal KillStreakSettings BuildStreakSettings() => new KillStreakSettings(
        revengeWindow: RevengeWindow.Value,
        multiKillWindow: MultiKillWindow.Value,
        streakResetWindow: StreakWindow.Value,
        gunnerWindow: GunnerWindow.Value,
        marauderWindow: MarauderWindow.Value,
        longShotDistance: LongShotDistance.Value,
        marksmanDistance: MarksmanDistance.Value,
        eliteLevel: EliteLevel.Value,
        scoreBase: ScoreBase.Value,
        scoreHeadshotBonus: ScoreHeadshotBonus.Value,
        scoreLongShotBonus: ScoreLongShotBonus.Value,
        scoreRevengeBonus: ScoreRevengeBonus.Value,
        scoreMultiKillBonus: ScoreMultiKillBonus.Value,
        scoreBossBonus: ScoreBossBonus.Value);

    private static string SoundHint(string fileName)
        => $"留空 = 自动到 Sounds/ 找 {fileName}.ogg / .wav / .mp3。也可以写绝对路径或相对插件目录的路径。";
}
