using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP.Configuration;
using UnityEngine;

namespace AyaHitmarker;

internal enum DamageNumberMode
{
    /// <summary>肉体 + 护甲，即本次命中造成的总伤害。</summary>
    Total = 0,
    /// <summary>只显示打进肉体的部分（旧 Mono 项目的行为；打中护甲会显示 0，因此会被跳过不画）。</summary>
    BodyOnly = 1,
}

/// <summary>
/// 全部可调项。分组与旧 Mono 项目对齐，方便对照迁移。
/// UI 每帧读 config，不要缓存值。
/// </summary>
internal sealed class AyaConfig
{
    // ---- 0. 诊断 ----
    internal ConfigEntry<bool> DebugEnabled { get; }
    internal ConfigEntry<bool> DebugVerbose { get; }
    internal ConfigEntry<KeyboardShortcut> TestHitKey { get; }
    internal ConfigEntry<KeyboardShortcut> TestKillKey { get; }

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
    internal ConfigEntry<string> DamageTextHeadshotColor { get; }
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

    internal ConfigEntry<bool> ShowFaction { get; }
    internal ConfigEntry<bool> ShowFactionIcon { get; }
    internal ConfigEntry<bool> ShowPlayerName { get; }
    internal ConfigEntry<bool> ShowKillDetails { get; }
    internal ConfigEntry<bool> ShowExperience { get; }

    internal ConfigEntry<int> FontSizeFaction { get; }
    internal ConfigEntry<int> FontSizePlayerName { get; }
    internal ConfigEntry<int> FontSizeKillDetails { get; }
    internal ConfigEntry<float> FontSizeExperience { get; }
    internal ConfigEntry<float> FactionIconSize { get; }
    internal ConfigEntry<float> FactionIconVerticalOffset { get; }
    internal ConfigEntry<float> ExperienceTextWidth { get; }
    internal ConfigEntry<float> KillFeedExperienceHorizontalOffset { get; }
    internal ConfigEntry<float> NameOutlineThickness { get; }

    internal ConfigEntry<string> ColorFaction { get; }
    internal ConfigEntry<string> ColorPlayerName { get; }
    internal ConfigEntry<string> ColorKillDetails { get; }
    internal ConfigEntry<string> ColorExperience { get; }
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

    // ---- 4. 音效 ----
    internal ConfigEntry<bool> SoundEnabled { get; }
    internal ConfigEntry<float> SoundMasterVolume { get; }
    internal ConfigEntry<float> SoundHitVolume { get; }
    internal ConfigEntry<float> SoundHitHeadshotVolume { get; }
    internal ConfigEntry<float> SoundKillVolume { get; }
    internal ConfigEntry<float> SoundKillHeadshotVolume { get; }
    internal ConfigEntry<string> SoundFileHit { get; }
    internal ConfigEntry<string> SoundFileHitHeadshot { get; }
    internal ConfigEntry<string> SoundFileKill { get; }
    internal ConfigEntry<string> SoundFileKillHeadshot { get; }

    // ---- 5. 界面 ----
    internal ConfigEntry<float> UiScale { get; }
    internal ConfigEntry<int> UiSortingOrder { get; }
    internal ConfigEntry<string> UiFontName { get; }

    internal AyaConfig(ConfigFile cfg)
    {
        // ---------------------------------------------------------------- 0. 诊断
        DebugEnabled = cfg.Bind("0. 诊断", "启用诊断日志", false,
            new ConfigDescription("把每次命中/击杀的原始字段写进 BepInEx/LogOutput.log。排查问题时再打开。"));
        DebugVerbose = cfg.Bind("0. 诊断", "详细诊断日志", false);
        TestHitKey = cfg.Bind("0. 诊断", "测试命中快捷键", new KeyboardShortcut(KeyCode.F8),
            new ConfigDescription("只在「启用诊断日志」打开时生效。"));
        TestKillKey = cfg.Bind("0. 诊断", "测试击杀快捷键", new KeyboardShortcut(KeyCode.F9));

        // ---------------------------------------------------------------- 1. 命中提示
        HitMarkerEnabled = cfg.Bind("1. 命中提示", "启用", true);
        HitBaseSize = cfg.Bind("1. 命中提示", "图标基础尺寸", 64f,
            new ConfigDescription("Icons/hit.png 的绘制尺寸（像素）。", null, new object[] { 8f, 400f }));
        HitDuration = cfg.Bind("1. 命中提示", "显示时长(秒)", 0.35f,
            new ConfigDescription("图标从出现到完全消失的时间，也是伤害数字的默认存活时长参照。", null, new object[] { 0.05f, 3f }));
        HitMarkerAnimationScale = cfg.Bind("1. 命中提示", "出现时缩放", 0.7f,
            new ConfigDescription("出现瞬间的比例，在显示时长内放大到 1.0。", null, new object[] { 0.1f, 3f }));
        HitMarkerCenterOffset = cfg.Bind("1. 命中提示", "屏幕中心偏移(X,Y)", "0,0",
            new ConfigDescription("只在「显示在命中点」关闭时使用。比如往上挪 20 像素写 0,-20。"));
        HitMarkerColor = cfg.Bind("1. 命中提示", "着色", "#FFFFFFFF",
            new ConfigDescription("对 hit.png 做整体着色，白色 = 保持原图。"));
        HitMarkerAtHitPoint = cfg.Bind("1. 命中提示", "显示在命中点", true,
            new ConfigDescription(
                "true = 把 DamageInfo.HitPoint 投影到屏幕（准星实际落点，会随弹道/后座偏移）；" +
                "false = 固定在屏幕中心 + 上面的偏移。"));

        // ---------------------------------------------------------------- 2. 伤害数字
        DamageTextEnabled = cfg.Bind("2. 伤害数字", "启用", true);
        DamageTextMode = cfg.Bind("2. 伤害数字", "取值", DamageNumberMode.Total,
            new ConfigDescription(
                "Total = 肉体+护甲总伤害；BodyOnly = 旧 Mono 项目的行为（只算肉体，打中护甲时显示 0，「0」会被跳过不画）。"));
        DamageTextSize = cfg.Bind("2. 伤害数字", "字号", 28,
            new ConfigDescription("", null, new object[] { 8, 120 }));
        DamageTextColor = cfg.Bind("2. 伤害数字", "普通颜色", "#FFFFFFFF");
        DamageTextHeadshotColor = cfg.Bind("2. 伤害数字", "爆头颜色", "#FFC94DFF");
        DamageTextPadding = cfg.Bind("2. 伤害数字", "距图标间距", 12f,
            new ConfigDescription("第一个数字相对命中图标右缘的间距。", null, new object[] { 0f, 200f }));
        DamageMultiTextPadding = cfg.Bind("2. 伤害数字", "数字之间间距", 24f,
            new ConfigDescription("连续命中时多个数字排成一行，这是彼此的间隔。", null, new object[] { 0f, 200f }));
        DamageTextLifetime = cfg.Bind("2. 伤害数字", "存活时长(秒)", 0.6f,
            new ConfigDescription("旧项目与命中图标共用时长；这里独立，想一致就设成和「显示时长」相同。", null, new object[] { 0.1f, 5f }));

        // ---------------------------------------------------------------- 3. 击杀播报
        KillFeedEnabled = cfg.Bind("3. 击杀播报", "启用", true);
        KillFeedHorizontalOffset = cfg.Bind("3. 击杀播报", "水平偏移", 220f,
            new ConfigDescription("锚点在屏幕中心**左侧**的像素数。越大越靠左。", null, new object[] { -400f, 900f }));
        KillFeedVerticalOffset = cfg.Bind("3. 击杀播报", "垂直偏移", 0f,
            new ConfigDescription("正数向下。", null, new object[] { -500f, 500f }));
        KillFeedBlockWidth = cfg.Bind("3. 击杀播报", "文本块宽度", 420f,
            new ConfigDescription("右对齐文本的可用宽度。", null, new object[] { 100f, 1200f }));
        KillFeedLineSpacing = cfg.Bind("3. 击杀播报", "行间距", 8f);
        KillFeedDuration = cfg.Bind("3. 击杀播报", "文字停留(秒)", 5f,
            new ConfigDescription("文字在这段时间内线性淡出。", null, new object[] { 0.5f, 30f }));

        ShowSkull = cfg.Bind("3. 击杀播报", "显示骷髅图标", true);
        SkullSize = cfg.Bind("3. 击杀播报", "骷髅尺寸", 64f,
            new ConfigDescription("", null, new object[] { 8f, 200f }));
        SkullSpacing = cfg.Bind("3. 击杀播报", "骷髅间距", 60f,
            new ConfigDescription("连杀时已有骷髅向左推的距离增量（还会加上骷髅尺寸）。", null, new object[] { 0f, 300f }));
        SkullDisplayDuration = cfg.Bind("3. 击杀播报", "骷髅停留(秒)", 2f,
            new ConfigDescription("", null, new object[] { 0.1f, 20f }));
        SkullFadeDuration = cfg.Bind("3. 击杀播报", "骷髅淡出(秒)", 0.3f,
            new ConfigDescription("停留时间末尾这段用于淡出。", null, new object[] { 0.01f, 5f }));
        StreakWindow = cfg.Bind("3. 击杀播报", "连杀窗口(秒)", 10f,
            new ConfigDescription("两次击杀间隔小于这个值就算连杀，会把之前的骷髅推向左。", null, new object[] { 1f, 60f }));
        SkullPushAnimationSpeed = cfg.Bind("3. 击杀播报", "推动动画速度", 5f,
            new ConfigDescription("", null, new object[] { 0.5f, 30f }));
        KillFeedMaxSkulls = cfg.Bind("3. 击杀播报", "最多骷髅数", 8,
            new ConfigDescription("", null, new object[] { 1, 24 }));

        ShowFaction = cfg.Bind("3. 击杀播报", "显示阵营行", true);
        ShowFactionIcon = cfg.Bind("3. 击杀播报", "显示阵营图标", true,
            new ConfigDescription("需要 Icons/RankIcons/RankN.png 之类的图标文件；没有就不显示。"));
        ShowPlayerName = cfg.Bind("3. 击杀播报", "显示玩家名", true);
        ShowKillDetails = cfg.Bind("3. 击杀播报", "显示详情行", true);
        ShowExperience = cfg.Bind("3. 击杀播报", "显示经验估算", false,
            new ConfigDescription("客户端拿不到权威经验值，这里只是估算。"));

        FontSizeFaction = cfg.Bind("3. 击杀播报", "字号:阵营", 16, new ConfigDescription("", null, new object[] { 6, 60 }));
        FontSizePlayerName = cfg.Bind("3. 击杀播报", "字号:玩家名", 18, new ConfigDescription("", null, new object[] { 6, 60 }));
        FontSizeKillDetails = cfg.Bind("3. 击杀播报", "字号:详情", 14, new ConfigDescription("", null, new object[] { 6, 60 }));
        FontSizeExperience = cfg.Bind("3. 击杀播报", "字号:经验", 20f, new ConfigDescription("", null, new object[] { 6f, 60f }));
        FactionIconSize = cfg.Bind("3. 击杀播报", "阵营图标尺寸", 24f);
        FactionIconVerticalOffset = cfg.Bind("3. 击杀播报", "阵营图标垂直偏移", -20f);
        ExperienceTextWidth = cfg.Bind("3. 击杀播报", "经验文本宽度", 180f);
        KillFeedExperienceHorizontalOffset = cfg.Bind("3. 击杀播报", "经验水平偏移", 40f,
            new ConfigDescription("相对锚点向右的距离。"));
        NameOutlineThickness = cfg.Bind("3. 击杀播报", "玩家名描边", 1.5f,
            new ConfigDescription("0 = 不描边。内部按 0..1 归一化喂给 TMP。", null, new object[] { 0f, 10f }));

        ColorFaction = cfg.Bind("3. 击杀播报", "颜色:阵营默认", "#FFFFFFFF");
        ColorPlayerName = cfg.Bind("3. 击杀播报", "颜色:玩家名", "#FF4D4DFF");
        ColorKillDetails = cfg.Bind("3. 击杀播报", "颜色:详情", "#CCCCCCFF");
        ColorExperience = cfg.Bind("3. 击杀播报", "颜色:经验", "#FFFFCCFF");
        ColorUSEC = cfg.Bind("3. 击杀播报", "颜色:USEC", "#4DA6FFFF");
        ColorBEAR = cfg.Bind("3. 击杀播报", "颜色:BEAR", "#FF6B6BFF");
        ColorSCAV = cfg.Bind("3. 击杀播报", "颜色:SCAV", "#C8C8C8FF");
        ColorBOSS = cfg.Bind("3. 击杀播报", "颜色:BOSS", "#FF3B3BFF");
        ColorFollower = cfg.Bind("3. 击杀播报", "颜色:Follower", "#FFAA66FF");
        ColorRAIDER = cfg.Bind("3. 击杀播报", "颜色:Raider", "#E0B0FFFF");
        ColorRouges = cfg.Bind("3. 击杀播报", "颜色:Rouges", "#7FD4FFFF");
        ColorSectant = cfg.Bind("3. 击杀播报", "颜色:Sectant", "#B6FF6BFF");

        // ---------------------------------------------------------------- 3b. 击杀图标特效
        EnableRingEffect = cfg.Bind("3. 击杀播报 · 特效", "启用环形特效", true);
        RingHeadshotEnabled = cfg.Bind("3. 击杀播报 · 特效", "爆头时触发", true);
        RingEffectDelay = cfg.Bind("3. 击杀播报 · 特效", "延迟(秒)", 0.1f);
        RingEffectDuration = cfg.Bind("3. 击杀播报 · 特效", "时长(秒)", 0.3f);
        RingExplosionSecondRingDelay = cfg.Bind("3. 击杀播报 · 特效", "爆炸第二环延迟", 0.1f);
        RingHeadshotRadius = cfg.Bind("3. 击杀播报 · 特效", "爆头半径", 42f);
        RingHeadshotThickness = cfg.Bind("3. 击杀播报 · 特效", "爆头粗细", 3f);
        RingHeadshotColor = cfg.Bind("3. 击杀播报 · 特效", "爆头颜色", "#D4B800FF");
        RingCritRadius = cfg.Bind("3. 击杀播报 · 特效", "暴击半径", 42f);
        RingCritThickness = cfg.Bind("3. 击杀播报 · 特效", "暴击粗细", 1.8f);
        RingCritColor = cfg.Bind("3. 击杀播报 · 特效", "暴击颜色", "#9CCB65FF");
        RingExplosionRadius = cfg.Bind("3. 击杀播报 · 特效", "爆炸半径", 42f);
        RingExplosionThickness = cfg.Bind("3. 击杀播报 · 特效", "爆炸粗细", 5.4f);
        RingExplosionColor = cfg.Bind("3. 击杀播报 · 特效", "爆炸颜色", "#F77F00FF");

        EnableIconGlow = cfg.Bind("3. 击杀播报 · 特效", "启用图标辉光", true);
        IconGlowColor = cfg.Bind("3. 击杀播报 · 特效", "辉光颜色", "#FFFFFFFF");
        IconGlowIntensity = cfg.Bind("3. 击杀播报 · 特效", "辉光强度", 0.45f,
            new ConfigDescription("", null, new object[] { 0f, 2f }));
        IconGlowSize = cfg.Bind("3. 击杀播报 · 特效", "辉光扩散(像素)", 4f,
            new ConfigDescription("改动会触发辉光纹理重烘（有缓存）。", null, new object[] { 0f, 32f }));

        // ---------------------------------------------------------------- 4. 音效
        SoundEnabled = cfg.Bind("4. 音效", "启用", true);
        SoundMasterVolume = cfg.Bind("4. 音效", "总音量", 0.75f, new ConfigDescription("", null, new object[] { 0f, 1f }));
        SoundHitVolume = cfg.Bind("4. 音效", "命中音量", 0.55f, new ConfigDescription("", null, new object[] { 0f, 2f }));
        SoundHitHeadshotVolume = cfg.Bind("4. 音效", "爆头命中音量", 0.75f, new ConfigDescription("", null, new object[] { 0f, 2f }));
        SoundKillVolume = cfg.Bind("4. 音效", "击杀音量", 0.85f, new ConfigDescription("", null, new object[] { 0f, 2f }));
        SoundKillHeadshotVolume = cfg.Bind("4. 音效", "爆头击杀音量", 1f, new ConfigDescription("", null, new object[] { 0f, 2f }));

        SoundFileHit = cfg.Bind("4. 音效", "自定义:命中", "",
            new ConfigDescription(BuildSoundHint("hit"), null, null));
        SoundFileHitHeadshot = cfg.Bind("4. 音效", "自定义:爆头命中", "", new ConfigDescription(BuildSoundHint("headshot_hit")));
        SoundFileKill = cfg.Bind("4. 音效", "自定义:击杀", "", new ConfigDescription(BuildSoundHint("kill")));
        SoundFileKillHeadshot = cfg.Bind("4. 音效", "自定义:爆头击杀", "", new ConfigDescription(BuildSoundHint("headshot_kill")));

        // ---------------------------------------------------------------- 5. 界面
        UiScale = cfg.Bind("5. 界面", "UI 缩放", 1f,
            new ConfigDescription("整体缩放命中提示 / 伤害数字 / 击杀播报。", null, new object[] { 0.5f, 3f }));
        UiSortingOrder = cfg.Bind("5. 界面", "画布层级", 9000,
            new ConfigDescription("越大越靠前。EFT 自己的 HUD 一般在这个值以下。"));
        UiFontName = cfg.Bind("5. 界面", "字体名称", "Microsoft YaHei",
            new ConfigDescription(
                "从系统字体创建 TMP 字体资源，默认微软雅黑可同时覆盖拉丁/西里尔/中文。" +
                "留空则用游戏自带的 TMP 默认字体（可能缺中文字形）。"));
    }

    private static string BuildSoundHint(string fileName)
        => $"留空 = 自动到 Sounds/ 找 {fileName}.ogg / .wav / .mp3。也可以写绝对路径或相对插件目录的路径。";
}
