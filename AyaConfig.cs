using System;
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

/// <summary>图标来源。</summary>
internal enum IconStyle
{
    /// <summary>程序化绘制（默认）：骷髅 / 命中标记 / 徽记都是 SDF 现画的，任意尺寸边缘干净、自带深色描边。</summary>
    Procedural = 0,
    /// <summary>沿用 Icons/ 里的 PNG（hit.png / skull.png / skull_head.png），按原样显示，不加描边。</summary>
    CustomFiles = 1,
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
    internal ConfigEntry<int> ConfigVersion { get; }

    // ---- 1. 命中提示 ----
    internal ConfigEntry<bool> HitMarkerEnabled { get; }
    internal ConfigEntry<float> HitBaseSize { get; }
    internal ConfigEntry<float> HitDuration { get; }
    internal ConfigEntry<float> HitMarkerAnimationScale { get; }
    internal ConfigEntry<string> HitMarkerCenterOffset { get; }
    internal ConfigEntry<string> HitMarkerColor { get; }
    internal ConfigEntry<bool> HitMarkerAtHitPoint { get; }
    internal ConfigEntry<float> HitMarkerOutlineWidth { get; }
    internal ConfigEntry<float> HitMarkerExpand { get; }
    internal ConfigEntry<bool> KillMarkerEnabled { get; }
    internal ConfigEntry<string> KillMarkerColor { get; }
    internal ConfigEntry<float> KillMarkerScale { get; }
    internal ConfigEntry<float> KillMarkerDuration { get; }

    // ---- 2. 伤害数字 ----
    internal ConfigEntry<bool> DamageTextEnabled { get; }
    internal ConfigEntry<DamageNumberMode> DamageTextMode { get; }
    internal ConfigEntry<int> DamageTextSize { get; }
    internal ConfigEntry<string> DamageTextColor { get; }
    internal ConfigEntry<string> DamageTextOutlineColor { get; }
    internal ConfigEntry<string> DamageTextHeadshotOutlineColor { get; }
    internal ConfigEntry<string> DamageTextHeadshotColor { get; }
    internal ConfigEntry<string> DamageTextKillColor { get; }
    internal ConfigEntry<float> DamageTextOutlineWidth { get; }
    internal ConfigEntry<float> DamageTextPadding { get; }
    internal ConfigEntry<float> DamageMultiTextPadding { get; }
    internal ConfigEntry<float> DamageTextLifetime { get; }

    // ---- 3. 击杀播报 ----
    internal ConfigEntry<bool> KillFeedEnabled { get; }
    internal ConfigEntry<float> KillFeedHorizontalOffset { get; }
    internal ConfigEntry<float> KillFeedVerticalOffset { get; }
    internal ConfigEntry<float> KillFeedLineSpacing { get; }
    internal ConfigEntry<float> KillFeedDuration { get; }

    internal ConfigEntry<bool> ShowSkull { get; }
    internal ConfigEntry<float> SkullSize { get; }
    internal ConfigEntry<float> SkullSpacing { get; }
    internal ConfigEntry<float> StreakWindow { get; }
    internal ConfigEntry<float> SkullPushAnimationSpeed { get; }
    internal ConfigEntry<int> KillFeedMaxSkulls { get; }

    // ---- 3. 击杀播报：卡片动效 ----
    internal ConfigEntry<bool> CardGlitchEnabled { get; }
    internal ConfigEntry<float> CardExitDuration { get; }
    internal ConfigEntry<float> CardTextOutline { get; }
    internal ConfigEntry<float> SkullPopScale { get; }
    internal ConfigEntry<float> SkullPopDuration { get; }

    // ---- 3. 击杀播报：得分 ----
    internal ConfigEntry<bool> ShowScore { get; }
    internal ConfigEntry<int> FontSizeScore { get; }
    internal ConfigEntry<string> ColorScore { get; }
    internal ConfigEntry<float> ScoreHorizontalOffset { get; }
    internal ConfigEntry<float> ScoreCountDuration { get; }
    internal ConfigEntry<float> ScorePopScale { get; }
    internal ConfigEntry<float> ScorePopDuration { get; }

    // ---- 3. 击杀播报：成就横幅 ----
    internal ConfigEntry<bool> ShowAchievement { get; }
    internal ConfigEntry<int> FontSizeAchievement { get; }
    internal ConfigEntry<float> AchievementHeight { get; }
    internal ConfigEntry<float> AchievementPadding { get; }
    internal ConfigEntry<float> AchievementVerticalOffset { get; }
    internal ConfigEntry<string> ColorAchievementText { get; }
    internal ConfigEntry<string> ColorAchievementNormal { get; }
    internal ConfigEntry<string> ColorAchievementGood { get; }
    internal ConfigEntry<string> ColorAchievementGreat { get; }
    internal ConfigEntry<string> ColorAchievementEpic { get; }

    // ---- 3. 击杀播报：连杀进度条 ----
    internal ConfigEntry<bool> ShowStreakBar { get; }
    internal ConfigEntry<float> StreakBarWidth { get; }
    internal ConfigEntry<float> StreakBarHeight { get; }
    internal ConfigEntry<float> StreakBarGap { get; }
    internal ConfigEntry<string> ColorStreakBar { get; }
    internal ConfigEntry<string> ColorStreakBarBack { get; }

    // ---- 3. 击杀播报：标签 / 名字 / 军衔徽章 ----
    internal ConfigEntry<bool> ShowTags { get; }
    internal ConfigEntry<int> FontSizeTag { get; }
    internal ConfigEntry<float> TagMinDistance { get; }

    internal ConfigEntry<bool> ShowFaction { get; }
    internal ConfigEntry<bool> ShowFactionIcon { get; }
    internal ConfigEntry<bool> ShowLevelChip { get; }
    internal ConfigEntry<bool> ShowPlayerName { get; }
    internal ConfigEntry<bool> ShowKillDetails { get; }
    internal ConfigEntry<bool> ShowKillMethod { get; }
    internal ConfigEntry<bool> ShowBossName { get; }

    internal ConfigEntry<int> FontSizePlayerName { get; }
    internal ConfigEntry<int> FontSizeKillDetails { get; }
    internal ConfigEntry<float> FactionIconSize { get; }
    internal ConfigEntry<float> FactionIconVerticalOffset { get; }

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
    internal ConfigEntry<IconStyle> IconStyleMode { get; }

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
        ConfigVersion = cfg.Bind("0. 调试", "配置版本(请勿修改)", 0,
            new ConfigDescription("内部使用：升级配置时据此一次性重置「默认值变了」的样式项。改了没有意义，下次启动会被覆盖。"));

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
        HitMarkerOutlineWidth = cfg.Bind("1. 命中提示", "描边宽度(像素)", 1.5f,
            new ConfigDescription(
                "命中图标外面的深色描边。旧图只有 ~3px 粗的白线，白色背景（雪地 / 天空）上会整个消失；" +
                "加上描边后任何背景都看得见。0 = 不描边。仅「图标样式 = Procedural」时生效。", null, new object[] { 0f, 6f }));
        HitMarkerExpand = cfg.Bind("1. 命中提示", "淡出时向外扩散比例", 1.12f,
            new ConfigDescription("图标在淡出过程中缓缓向外撑开到这个比例（战地的命中标记就是这样往外散）。1 = 不扩散。", null, new object[] { 1f, 2f }));
        KillMarkerEnabled = cfg.Bind("1. 命中提示", "击杀时显示红色扩散标记", true,
            new ConfigDescription("击杀时命中标记变成更大的红色版本，并向外扩散（战地 6 的击杀确认）。"));
        KillMarkerColor = cfg.Bind("1. 命中提示", "击杀标记颜色", "#FF4A3DFF");
        KillMarkerScale = cfg.Bind("1. 命中提示", "击杀标记扩散比例", 1.6f,
            new ConfigDescription("击杀标记从基础大小向外扩散到的比例。", null, new object[] { 1f, 4f }));
        KillMarkerDuration = cfg.Bind("1. 命中提示", "击杀标记时长(秒)", 0.5f,
            new ConfigDescription("", null, new object[] { 0.1f, 3f }));

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
        DamageTextOutlineColor = cfg.Bind("2. 伤害数字", "描边颜色", "#000000D0",
            new ConfigDescription(
                "深色描边。旧默认是「白色 25% 不透明」，白字配白描边等于没有，亮背景上看不清。" +
                "末两位是透明度，设成 00 等于不描边。"));
        DamageTextHeadshotOutlineColor = cfg.Bind("2. 伤害数字", "爆头描边颜色", "#000000D0");
        DamageTextOutlineWidth = cfg.Bind("2. 伤害数字", "描边宽度", 0.24f,
            new ConfigDescription("TMP 的归一化描边宽度（0 = 关闭，0.24 约 2 像素）。", null, new object[] { 0f, 1f }));
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
        DamageTextHeadshotColor = cfg.Bind("2. 伤害数字", "爆头颜色", "#FF8A7AFF",
            new ConfigDescription("爆头命中的数字填充色（旧项目爆头只改描边，填充仍是白色）。填成和「颜色」一样就等于不区分。"));
        DamageTextKillColor = cfg.Bind("2. 伤害数字", "击杀颜色", "#FF4A3DFF",
            new ConfigDescription("击杀那一下的数字会变成这个颜色（战地里最后一击的数字是红的）。"));

        // ---------------------------------------------------------------- 3. 击杀播报
        // 卡片按战地 6 重做：一张卡累加窗口内的连续击杀（骷髅并排 / 分数累计 / 标签取并集），
        // 入场是「色块显影 + 数字滚动」，退场是「错峰闪烁熄灭」。
        KillFeedEnabled = cfg.Bind("3. 击杀信息", "启用", true);
        KillFeedHorizontalOffset = cfg.Bind("3. 击杀信息", "水平中心偏移", 190f,
            new ConfigDescription(
                "卡片脊线（骷髅 / 名字 / 标签的右对齐边）在屏幕中心**左侧**的像素数。正值向左，负值向右。" +
                "分数从脊线往右长约 110 像素，所以默认 190 让它刚好落在准星左边、不压命中标记（旧项目的 107 会压到）。", null, new object[] { -2000f, 2000f }));
        KillFeedVerticalOffset = cfg.Bind("3. 击杀信息", "垂直中心偏移", 0f,
            new ConfigDescription("主行（骷髅 + 分数）的中心位置。正值向下，负值向上。", null, new object[] { -2000f, 2000f }));
        KillFeedLineSpacing = cfg.Bind("3. 击杀信息", "行间距", 4f,
            new ConfigDescription("卡片相邻两行之间的空隙。", null, new object[] { 0f, 50f }));
        KillFeedDuration = cfg.Bind("3. 击杀信息", "卡片停留时长(秒)", 4.2f,
            new ConfigDescription(
                "从**最后一杀**到卡片完全消失的总时长（含最后的闪烁熄灭）。窗口内再杀一个会把卡片续上并累加。" +
                "战地 6 实录里整张卡约 3.4 秒；这里信息更多，默认长一点。", null, new object[] { 1f, 30f }));

        // ---- 卡片动效 ----
        CardGlitchEnabled = cfg.Bind("3. 击杀信息", "故障特效", true,
            new ConfigDescription(
                "战地式的故障动效：入场时元素先以实心色块闪出再显影、分数数字乱跳着滚到终值；" +
                "退场时错峰闪烁熄灭。**关闭 = 普通淡入淡出**（对闪烁敏感的话请关掉）。"));
        CardExitDuration = cfg.Bind("3. 击杀信息", "卡片淡出时长(秒)", 0.5f,
            new ConfigDescription("卡片最后的退场动画有多长（骷髅 + 名字先闪灭，标签随后，分数最后硬切）。", null, new object[] { 0f, 5f }));
        CardTextOutline = cfg.Bind("3. 击杀信息", "卡片文字描边", 0.14f,
            new ConfigDescription(
                "卡片文字的深色描边强度（TMP 归一化宽度，0 = 不描边）。这是 24px 字号下的基准值，其他字号按比例缩放 —— " +
                "同一个数值落在小字上会把笔画吃掉一大半。亮背景上靠它保证可读。", null, new object[] { 0f, 1f }));

        // 旧 cfg: 骷髅 40 / 间距 20 / 推挤速度 7
        ShowSkull = cfg.Bind("3. 击杀信息", "显示骷髅头", true);
        SkullSize = cfg.Bind("3. 击杀信息", "骷髅头大小", 44f,
            new ConfigDescription("骷髅本体的高度（像素）。", null, new object[] { 8f, 256f }));
        SkullSpacing = cfg.Bind("3. 击杀信息", "骷髅头间距", 8f,
            new ConfigDescription("相邻骷髅之间的空隙（像素）。", null, new object[] { 0f, 200f }));
        StreakWindow = cfg.Bind("3. 击杀信息", "连杀时间窗口(秒)", 10f,
            new ConfigDescription("两次击杀间隔小于这个值就算连杀（连杀称号 / 进度条用）。", null, new object[] { 1f, 60f }));
        SkullPushAnimationSpeed = cfg.Bind("3. 击杀信息", "骷髅头推挤动画速度", 7f,
            new ConfigDescription("新骷髅出现时，旧骷髅向左让位的平滑速度。", null, new object[] { 1f, 30f }));
        KillFeedMaxSkulls = cfg.Bind("3. 击杀信息", "最多骷髅数", 8,
            new ConfigDescription("一张卡上最多并排几个骷髅，超出时挤掉最老的。", null, new object[] { 1, 24 }));
        SkullPopScale = cfg.Bind("3. 击杀信息", "骷髅弹出比例", 1.35f,
            new ConfigDescription("新骷髅出现瞬间的缩放比例（>1 = 先放大再回弹）。", null, new object[] { 1f, 3f }));
        SkullPopDuration = cfg.Bind("3. 击杀信息", "骷髅弹出时长(秒)", 0.16f,
            new ConfigDescription("", null, new object[] { 0.01f, 1f }));

        // 得分（战地式：白字，在骷髅右边）
        ShowScore = cfg.Bind("3. 击杀信息", "显示得分", true,
            new ConfigDescription("显示卡片累计得分。数值来自「3c. 击杀成就」的公式。"));
        FontSizeScore = cfg.Bind("3. 击杀信息", "得分字号", 34, new ConfigDescription("", null, new object[] { 8, 72 }));
        ColorScore = cfg.Bind("3. 击杀信息", "得分颜色", "#FFFFFFFF");
        ScoreHorizontalOffset = cfg.Bind("3. 击杀信息", "得分水平偏移", 14f,
            new ConfigDescription("得分文字的左缘相对脊线向右多少像素。", null, new object[] { -500f, 500f }));
        ScoreCountDuration = cfg.Bind("3. 击杀信息", "得分计数字时长(秒)", 0.25f,
            new ConfigDescription("数字从原来的分数滚到新分数的时间（战地 6 实录约 0.25 秒）；0 = 直接显示终值。", null, new object[] { 0f, 2f }));
        ScorePopScale = cfg.Bind("3. 击杀信息", "得分弹出比例", 1.15f,
            new ConfigDescription("得分每次变化时的缩放弹出比例，1 = 不弹。", null, new object[] { 1f, 3f }));
        ScorePopDuration = cfg.Bind("3. 击杀信息", "得分弹出时长(秒)", 0.18f,
            new ConfigDescription("", null, new object[] { 0.01f, 1f }));

        // 成就横幅（战地 6 的「DOUBLE KILL」那条：浅色底 + 深色字 + 右边的骷髅徽记）
        ShowAchievement = cfg.Bind("3. 击杀信息", "显示成就缎带", true,
            new ConfigDescription("仅在触发成就（双杀 / 复仇者 / 狂战士 …）时出现在主行上方；多杀时右边带骷髅徽记。"));
        FontSizeAchievement = cfg.Bind("3. 击杀信息", "成就字号", 18, new ConfigDescription("", null, new object[] { 8, 72 }));
        AchievementHeight = cfg.Bind("3. 击杀信息", "成就缎带高度", 34f, new ConfigDescription("", null, new object[] { 8f, 120f }));
        AchievementPadding = cfg.Bind("3. 击杀信息", "成就缎带左右留白", 12f,
            new ConfigDescription("横幅比文字宽出来的部分。", null, new object[] { 0f, 120f }));
        AchievementVerticalOffset = cfg.Bind("3. 击杀信息", "成就缎带垂直偏移", 0f,
            new ConfigDescription("相对默认位置再往上多少像素。", null, new object[] { -400f, 400f }));
        ColorAchievementText = cfg.Bind("3. 击杀信息", "成就文字颜色", "#2E3E4EFF",
            new ConfigDescription("横幅底是浅钢色（战地风格），所以字是深色。"));
        ColorAchievementNormal = cfg.Bind("3. 击杀信息", "成就底色_普通", "#6B7C8CFF",
            new ConfigDescription("横幅左缘那条**强调色**（不是整个底）：按成就档次区分。"));
        ColorAchievementGood = cfg.Bind("3. 击杀信息", "成就底色_优秀", "#2F7BD6FF");
        ColorAchievementGreat = cfg.Bind("3. 击杀信息", "成就底色_精英", "#E0801EFF");
        ColorAchievementEpic = cfg.Bind("3. 击杀信息", "成就底色_史诗", "#E03A4EFF");

        // 连杀进度条
        ShowStreakBar = cfg.Bind("3. 击杀信息", "显示连杀进度条", true,
            new ConfigDescription("主行下面那条细条，显示距离下一档连杀称号的进度。"));
        StreakBarWidth = cfg.Bind("3. 击杀信息", "进度条宽度", 120f, new ConfigDescription("", null, new object[] { 20f, 600f }));
        StreakBarHeight = cfg.Bind("3. 击杀信息", "进度条高度", 3f, new ConfigDescription("", null, new object[] { 1f, 20f }));
        StreakBarGap = cfg.Bind("3. 击杀信息", "进度条距骷髅间距", 4f,
            new ConfigDescription("主行下缘到进度条的间距；名字 / 标签 / 详情会自动往下让出这块空间。", null, new object[] { 0f, 60f }));
        ColorStreakBar = cfg.Bind("3. 击杀信息", "进度条颜色", "#FF7A18FF");
        ColorStreakBarBack = cfg.Bind("3. 击杀信息", "进度条背景色", "#00000080");

        // 标签（战地的 KILL / HEADSHOT 方框）
        ShowTags = cfg.Bind("3. 击杀信息", "显示击杀标签", true,
            new ConfigDescription("名字下面的描边小方框：击杀 / 爆头（窗口内的击杀会取并集）、近战 / 爆炸 / 狙击、距离。"));
        FontSizeTag = cfg.Bind("3. 击杀信息", "标签字号", 16, new ConfigDescription("", null, new object[] { 8, 48 }));
        TagMinDistance = cfg.Bind("3. 击杀信息", "距离标签最小距离(米)", 20f,
            new ConfigDescription("击杀距离达到这个值才显示距离标签；0 = 只要有距离就显示，很大的数 = 永远不显示。", null, new object[] { 0f, 1000f }));

        ShowFaction = cfg.Bind("3. 击杀信息", "显示阵营文字", true,
            new ConfigDescription("名字前面的阵营标记（USEC / BEAR / SCAV …，按阵营着色）。"));
        ShowFactionIcon = cfg.Bind("3. 击杀信息", "显示阵营图标", true,
            new ConfigDescription(
                "PMC 的军衔徽章（Icons/RankIcons/RankN.png），显示在卡片**最左边**。其他阵营没有徽章图，这块不显示。"));
        ShowLevelChip = cfg.Bind("3. 击杀信息", "徽章上显示等级", true,
            new ConfigDescription("军衔徽章右下角的等级小标。"));
        ShowPlayerName = cfg.Bind("3. 击杀信息", "显示玩家名字", true);
        ShowKillDetails = cfg.Bind("3. 击杀信息", "显示击杀详情", true,
            new ConfigDescription("标签下面那行暗色小字：命中部位 · 武器。"));
        ShowKillMethod = cfg.Bind("3. 击杀信息", "显示击杀方式", true,
            new ConfigDescription("显示「近战 / 爆炸 / 狙击…」这类方式标签；枪械击杀不显示（默认方式，写了只是噪音）。"));
        ShowBossName = cfg.Bind("3. 击杀信息", "显示首领名称", true,
            new ConfigDescription("首领用「Killa / Glukhar / Shturman…」代替笼统的 BOSS。"));

        // 旧 cfg: 名字 18 / 详情 20
        FontSizePlayerName = cfg.Bind("3. 击杀信息", "玩家名字号", 20, new ConfigDescription("", null, new object[] { 8, 48 }));
        FontSizeKillDetails = cfg.Bind("3. 击杀信息", "击杀详情字号", 15, new ConfigDescription("", null, new object[] { 8, 48 }));
        FactionIconSize = cfg.Bind("3. 击杀信息", "阵营图标大小", 72f,
            new ConfigDescription(
                "PMC 军衔徽章的边长（像素），外面还会垫一块 7px 边距的深色底板。贴图原始尺寸是 66×66；旧默认 40 太小，" +
                "浅色浮雕在 40px 下只剩一团灰。", null, new object[] { 8f, 128f }));
        FactionIconVerticalOffset = cfg.Bind("3. 击杀信息", "阵营图标垂直偏移", 0f,
            new ConfigDescription(
                "相对默认位置上下微调（负值向上）。默认垂直居中在「主行 + 名字 + 标签」上。",
                null, new object[] { -200f, 200f }));

        // 颜色：旧默认里有几个深色（USEC #003399 / Rouges #333333 / Sectant #006600），
        // 在新卡片的深色背景上几乎看不见，已改成同色相的亮色。
        ColorFaction = cfg.Bind("3. 击杀信息", "阵营文字颜色", "#FFFFFFFF");
        ColorPlayerName = cfg.Bind("3. 击杀信息", "玩家名字颜色", "#FF5A4DFF",
            new ConfigDescription("战地 6 里敌人的名字是这种珊瑚红；纯红在暗背景上不够亮。"));
        ColorKillDetails = cfg.Bind("3. 击杀信息", "击杀详情颜色", "#D3D9E0FF");
        ColorUSEC = cfg.Bind("3. 击杀信息", "颜色_USEC", "#5B9BFFFF");
        ColorBEAR = cfg.Bind("3. 击杀信息", "颜色_BEAR", "#FF9A1FFF");
        ColorSCAV = cfg.Bind("3. 击杀信息", "颜色_SCAV", "#FFE600FF");
        ColorBOSS = cfg.Bind("3. 击杀信息", "颜色_BOSS", "#FF3B30FF");
        ColorFollower = cfg.Bind("3. 击杀信息", "颜色_Follower", "#E63333FF");
        ColorRAIDER = cfg.Bind("3. 击杀信息", "颜色_RAIDER", "#FF66B2FF");
        ColorRouges = cfg.Bind("3. 击杀信息", "颜色_Rouges", "#9AA0A8FF");
        ColorSectant = cfg.Bind("3. 击杀信息", "颜色_Sectant", "#3FBF5FFF");

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
        IconStyleMode = cfg.Bind("5. 界面", "图标样式", IconStyle.Procedural,
            new ConfigDescription(
                "Procedural（默认）= 骷髅 / 命中标记用程序化绘制，任意大小边缘干净，自带深色描边；" +
                "CustomFiles = 沿用 Icons/ 里的 PNG（hit.png / skull.png / skull_head.png），按原样显示、不加描边。"));

        ApplyConfigMigration(cfg);
    }

    /// <summary>
    /// 配置版本。每次「已有键的默认值变了」的设计改版 +1：
    /// <c>1</c> = 0.3.0（旧卡片），<c>2</c> = 0.4.0（战地 6 卡片 + 带描边的命中标记）。
    /// </summary>
    private const int CurrentConfigVersion = 2;

    /// <summary>
    /// 一次性配置升级。BepInEx 会把**首次运行时的默认值写死在磁盘上**，所以改了代码里的默认值，
    /// 已存在的 cfg 永远拿不到新值（AGENTS.md 里「改了默认值之后必须删 cfg」说的就是这个）。
    /// 这里把「默认值变了」的样式项重置成新默认 —— 但**只重置值还等于 0.3.0 旧默认的**：用户自己调过的原样保留。
    /// 垂直偏移、命中图标中心偏移、各种开关和音效相关的键压根不在名单里。
    /// 旧版独有、新版已经不存在的动效键（入场位移、骷髅显示时长 …）会作为「孤儿」被清出文件。
    /// </summary>
    private void ApplyConfigMigration(ConfigFile cfg)
    {
        int from = ConfigVersion.Value;
        if (from >= CurrentConfigVersion) return;

        // (键, 旧默认值, 这个改动是哪个配置版本引入的)。只有「值还等于旧默认」的才重置成新默认 ——
        // 用户自己调过的值（≠ 旧默认）原样保留。第三项保证以后再把 CurrentConfigVersion 往上加时，
        // 不会把「升级之后用户特意设回 0.3.0 默认值」的项目再重置一遍。
        (ConfigEntryBase Entry, object OldDefault, int Since)[] changedDefaults =
        {
            // 伤害数字：描边从「白 25%」改成深色
            (DamageTextOutlineColor, "#FFFFFF40", 2), (DamageTextHeadshotOutlineColor, "#FF0000FF", 2), (DamageTextOutlineWidth, 0.2f, 2),

            // 击杀卡片：位置 / 版式 / 字号 / 尺寸
            (KillFeedHorizontalOffset, 107f, 2), (KillFeedLineSpacing, 0f, 2), (SkullSize, 40f, 2), (SkullSpacing, 20f, 2),
            (FontSizeScore, 28, 2), (ScoreHorizontalOffset, 35f, 2), (ScoreCountDuration, 0.35f, 2), (ScorePopScale, 1.6f, 2), (ScorePopDuration, 0.22f, 2),
            (FontSizeAchievement, 22, 2), (AchievementHeight, 30f, 2), (AchievementPadding, 14f, 2), (StreakBarHeight, 4f, 2),
            (FontSizePlayerName, 18, 2), (FontSizeKillDetails, 20, 2), (FactionIconSize, 40f, 2),

            // 击杀卡片：配色（旧的深蓝 / 深灰 / 深绿在深色底上看不见）
            (ColorScore, "#FFE08AFF", 2), (ColorPlayerName, "#FF000FFF", 2), (ColorKillDetails, "#CCCCCCFF", 2), (ColorStreakBarBack, "#00000099", 2),
            (ColorAchievementText, "#FFFFFFFF", 2), (ColorAchievementNormal, "#46505CFF", 2), (ColorAchievementGood, "#2F6FB5FF", 2),
            (ColorAchievementGreat, "#C2681AFF", 2), (ColorAchievementEpic, "#A32434FF", 2),
            (ColorUSEC, "#003399FF", 2), (ColorBEAR, "#FF8C00FF", 2), (ColorBOSS, "#FF0000FF", 2), (ColorRouges, "#333333FF", 2), (ColorSectant, "#006600FF", 2),
        };

        int reset = 0;     // 还是旧默认 → 换成新默认
        int already = 0;   // 本来就是新默认（全新安装 / 之前已经换过）
        int kept = 0;      // 用户自己调过的 → 保持原样
        cfg.SaveOnConfigSet = false;   // 批量改完再存一次，不要每改一项写一次盘
        try
        {
            foreach (var (entry, oldDefault, since) in changedDefaults)
            {
                if (from >= since) continue;   // 这一项在更早的版本里已经升级过了

                if (SameValue(entry.BoxedValue, oldDefault))
                {
                    entry.BoxedValue = entry.DefaultValue;
                    reset++;
                }
                else if (SameValue(entry.BoxedValue, entry.DefaultValue))
                {
                    already++;
                }
                else
                {
                    kept++;
                }
            }
            ConfigVersion.Value = CurrentConfigVersion;
            ClearOrphanedEntries(cfg);
        }
        finally
        {
            cfg.SaveOnConfigSet = true;
        }
        cfg.Save();

        Plugin.Logger.LogInfo(
            $"[config] 配置从 v{from} 升级到 v{CurrentConfigVersion}：{reset} 个仍是旧默认值的样式项已换成新默认，" +
            $"{already} 个本来就是新默认，{kept} 个被你改过的保持原样；开关 / 音效相关的键没动。");
    }

    /// <summary>配置值与「旧默认值」是否相同。浮点留一点容差（文本往返会有末位误差），颜色不分大小写。</summary>
    private static bool SameValue(object? current, object oldDefault)
    {
        if (current is float cf && oldDefault is float of) return Math.Abs(cf - of) < 1e-4f;
        if (current is string cs && oldDefault is string os) return string.Equals(cs, os, StringComparison.OrdinalIgnoreCase);
        return Equals(current, oldDefault);
    }

    /// <summary>
    /// 清掉 cfg 里已经不存在的旧键。<c>ConfigFile.OrphanedEntries</c> 在 BepInEx 6 里不是 public，只能反射；
    /// 这只是让文件干净一点的「锦上添花」，失败了不影响任何功能。
    /// </summary>
    private static void ClearOrphanedEntries(ConfigFile cfg)
    {
        try
        {
            var prop = typeof(ConfigFile).GetProperty(
                "OrphanedEntries",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            (prop?.GetValue(cfg) as System.Collections.IDictionary)?.Clear();
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[config] 清理旧版遗留的配置键失败（不影响使用）: {ex.Message}");
        }
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
