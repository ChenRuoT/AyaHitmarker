using System;
using System.Collections.Generic;

namespace AyaHitmarker.Game;

/// <summary>
/// 设置面板的英文文案（0.7.0）。纯托管、不引用 BepInEx / Unity，所以能进离线测试。
///
/// **只翻译显示层**：cfg 的键名永远是中文（改键名 = 新键 + 旧键成孤儿，AGENTS.md §1），
/// 英文只通过 <c>ConfigurationManagerAttributes.DispName / Description / Category</c> 覆盖面板显示；
/// cfg 文件里的 <c>##</c> 注释是 Bind 时写下的中文，不跟着语言变。
///
/// 表以 C# 属性名（<c>nameof(AyaConfig.X)</c>）为键。<c>Desc == null</c> 表示这个条目的中文版也没写说明，
/// 面板上显示的是 <c>ConfigPanel.Hint</c> 的通用兜底句 —— 英文对应 <see cref="Generic"/>。
/// <c>ConfigTextFacts</c> 静态对账：每个 Bind 的键都有条目、英文里没有 CJK、
/// 有显式中文说明（或专用兜底句）的条目一定有英文说明。
///
/// 术语与屏幕文字保持一致（Vanguard / Avenger / Marauder / Gunner / Marksman / Boss Slayer / Long Shot / Multi Kill）。
/// </summary>
internal static class ConfigTextEn
{
    internal const string ColorNote = " Supports #RRGGBB / #RRGGBBAA; A is opacity, 0 = transparent, 255 = opaque.";
    internal const string KillCardNote = " Style changes take full effect on the next kill.";
    internal const string SoundFileNote = " A changed path is reloaded when the next raid starts, or after restarting the game.";
    internal const string RangeNote = " Out-of-range input is automatically clamped to the allowed range.";

    private static string SoundHint(string fileName)
        => $"Empty = look for {fileName}.ogg / .wav / .mp3 in Sounds/. An absolute path or a path relative to the plugin folder also works.";

    /// <summary>面板分类（中文 → 英文）。两边的 "NN." 数字前缀一致，ConfigurationManager 按首次绑定顺序排，不看名字。</summary>
    private static readonly Dictionary<string, string> Categories = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["00. 常用与整体界面"] = "00. General & Interface",
        ["10. 命中提示"] = "10. Hit Marker",
        ["11. 受击方向"] = "11. Hit Direction",
        ["12. 战局小结"] = "12. Raid Summary",
        ["20. 伤害数字"] = "20. Damage Numbers",
        ["30. 击杀卡片 · 内容"] = "30. Kill Card · Content",
        ["31. 击杀卡片 · 布局"] = "31. Kill Card · Layout",
        ["32. 击杀卡片 · 配色"] = "32. Kill Card · Colors",
        ["33. 击杀卡片 · 动画"] = "33. Kill Card · Animation",
        ["40. 击杀图标特效"] = "40. Kill Icon Effects",
        ["50. 成就与计分规则"] = "50. Achievements & Scoring",
        ["60. 音效"] = "60. Sound",
        ["90. 调试与排障"] = "90. Debug & Troubleshooting",
    };

    private static (string Name, string? Desc) E(string name, string? desc = null) => (name, desc);

    private static readonly Dictionary<string, (string Name, string? Desc)> Table =
        new Dictionary<string, (string Name, string? Desc)>(StringComparer.Ordinal)
    {
        // ---------------------------------------------------------------- 00 General & Interface
        ["IconStyleMode"] = E("Icon Style",
            "Procedural (default) = the skull and hit marker are drawn procedurally: clean edges at any size, with a built-in dark outline. " +
            "CustomFiles = use the PNGs in Icons/ (hit.png / skull.png / skull_head.png) as they are, with no outline."),
        ["Language"] = E("Language",
            "Language of the on-screen banners, tags and kill methods, and of this settings panel's names and descriptions. " +
            "Auto = follow the game language (a Chinese game uses Chinese, anything else uses English). " +
            "Panel text switches when you reopen the panel; on-screen text switches from the next kill."),
        ["StylePreset"] = E("Apply Preset",
            "Apply a look preset in one click. It takes effect immediately, then this setting snaps back to None (select the same preset again to re-apply it). " +
            "Default = Battlefield 6 default (resets every setting the presets touch); Legacy = 0.3 sizes and colors; Minimal; " +
            "HighContrast = high contrast / colour-blind safe; Tactical / Operator / Bloodbath / Headhunter / Slick / NightOps / Inferno / Plague = the old project's 8 color themes. " +
            "Before applying, the cfg is backed up as com.aya.ayahitmarker.cfg.bak-preset-<timestamp> (the 5 newest are kept). " +
            "Every setting any preset touches is reset first and then written, so values you tuned by hand in those settings are overwritten; " +
            "position offsets, sound, hotkeys, scoring rules and debug options are never affected. " +
            "Kill cards and hit-direction clusters pick up the new look from the next kill / hit."),
        ["UiFontName"] = E("Font Name",
            "Leave empty = use the game's own TMP font (recommended). Its fallback chain already covers Chinese and Latin, so nothing needs to be filled in. " +
            "A system font name only works if Unity can build a TMP font from it; on the current game version Microsoft YaHei / Arial / SimHei were all tested and fail, " +
            "and the game font is used instead. If Chinese shows up as boxes, send the log lines starting with [ui] font … glyphs to the author."),
        ["UiScale"] = E("UI Scale", "Scales hit markers, damage numbers and the kill feed as a whole."),
        ["UiSortingOrder"] = E("Canvas Sorting Order", "Higher = in front. EFT's own HUD is usually below this value."),

        // ---------------------------------------------------------------- 10 Hit Marker
        ["ArmorHitMarkerColor"] = E("Armor Marker Color",
            "Glow color around the cross on an armor hit; the small shield icon next to the damage number uses it too."),
        ["ArmorHitMarkerEnabled"] = E("Armor Hit Marker",
            "When the hit is stopped by armor, the cross stays white with a blue glow around it and a small shield icon appears to the right of the damage number. " +
            "Turn it off and armor hits look exactly like normal hits."),
        ["HitBaseSize"] = E("Base Size",
            "Base draw size of the hit icon (canvas pixels); UI scale and the headshot multiplier stack on top."),
        ["HitDuration"] = E("Display Duration (s)", "Time from the icon appearing to it fully disappearing."),
        ["HitMarkerAnimationScale"] = E("Animation Scale",
            "Scale at the moment it appears, growing to 1.0 over the display duration. The old config was 1 (no scale animation)."),
        ["HitMarkerAtHitPoint"] = E("Show at Hit Point",
            "false (default, same as the old project) = fixed at the screen center plus the offset below; " +
            "true = project DamageInfo.HitPoint onto the screen (where the crosshair actually landed). " +
            "If the projection falls off screen it falls back to the screen center."),
        ["HitMarkerCenterOffset"] = E("Center Offset (X,Y)",
            "Canvas-pixel offset from the screen center: positive X goes right, positive Y goes down. Damage numbers use this offset too. " +
            "Applies when 'Show at Hit Point' is off; fill X and Y in the two boxes, with a dot as the decimal separator."),
        ["HitMarkerColor"] = E("Tint",
            "Overall tint of the hit icon; white keeps the icon's own colors, and the dark outline is not tinted."),
        ["HitMarkerEnabled"] = E("Enabled",
            "Show normal and headshot hit markers; the kill-confirm marker has its own switch."),
        ["HitMarkerExpand"] = E("Expansion While Fading",
            "While fading out, the icon slowly expands outward to this ratio (Battlefield's hit marker spreads like this). 1 = no expansion."),
        ["HitMarkerHeadshotColor"] = E("Headshot Tint",
            "Used instead of 'Tint' for headshot hits. The old project did not recolor; this defaults to red to strengthen headshot feedback."),
        ["HitMarkerHeadshotScale"] = E("Headshot Size Multiplier", "Size multiplier of the icon on headshot hits."),
        ["HitMarkerOutlineWidth"] = E("Outline Width (px)",
            "Dark outline around the hit icon. The old art was only a ~3 px white line that vanished entirely on white backgrounds (snow / sky); " +
            "with the outline it is visible on any background. 0 = no outline. Only applies when Icon Style = Procedural."),
        ["HitMarkerPopDuration"] = E("Pop Duration (s)",
            "Time to settle from the pop scale back to normal size, in seconds."),
        ["HitMarkerPopScale"] = E("Pop Scale",
            "On impact the icon first scales up to this ratio, then settles back to 1 (stacks with the old project's 'Animation Scale'). 1 = no pop."),
        ["KillMarkerColor"] = E("Kill Marker Color"),
        ["KillMarkerDuration"] = E("Kill Marker Duration (s)",
            "Time from the kill-confirm marker appearing to it fully disappearing, in seconds."),
        ["KillMarkerEnabled"] = E("Red Burst Marker on Kill",
            "On a kill the hit marker turns into a larger red version that spreads outward (Battlefield 6's kill confirmation)."),
        ["KillMarkerScale"] = E("Kill Marker Spread Ratio",
            "Ratio the kill marker spreads out to from its base size."),

        // ---------------------------------------------------------------- 11 Hit Direction
        ["HitDirectionArmorHeightScale"] = E("Hit Direction: Armor-Absorbed Height Ratio",
            "Blunt damage absorbed by armor only (white) has its bump height multiplied by this ratio."),
        ["HitDirectionColorAbsorb"] = E("Hit Direction: Armor-Absorbed Color",
            "Color of the hit-direction lattice for blunt damage that armor absorbed."),
        ["HitDirectionColorHeavy"] = E("Hit Direction: Heavy Damage Color",
            "Color of the hit-direction lattice for heavy hits (body damage at or above the heavy-damage threshold)."),
        ["HitDirectionColorNormal"] = E("Hit Direction: Normal Damage Color",
            "Color of the hit-direction lattice for normal damage."),
        ["HitDirectionDotSize"] = E("Hit Direction: Dot Size",
            "Edge length of one dot (canvas pixels, including the dark rim; the solid part is about 0.62 of it)."),
        ["HitDirectionDotSpacing"] = E("Hit Direction: Dot Spacing",
            "Center-to-center distance of neighboring dots (canvas pixels). Smaller than the dot size and the dots overlap into blobs; " +
            "when the dot count exceeds 'Max Dots per Cluster' the spacing is enlarged automatically."),
        ["HitDirectionDrift"] = E("Hit Direction: Outward Drift",
            "How far the whole cluster drifts outward along its tip direction while it is shown (canvas pixels). 0 = no drift."),
        ["HitDirectionDuration"] = E("Hit Direction: Duration (s)",
            "Time from a cluster appearing to it fully fading out."),
        ["HitDirectionEllipse"] = E("Hit Direction: Ellipse Radius",
            "Horizontal / vertical radius of the hit-direction ellipse (canvas pixels, long axis horizontal). Format: X,Y."),
        ["HitDirectionEnabled"] = E("Hit Direction Indicator",
            "When you take damage, a regular dot lattice (a tapered quadratic bump whose tip points at the attacker) lights up on a transparent ellipse " +
            "around the screen center, at the attacker's bearing. Blunt damage absorbed by armor = white, normal damage = yellow, heavy damage = red; " +
            "the more damage, the more prominent the bump."),
        ["HitDirectionFullDamage"] = E("Hit Direction: Full-Strength Damage",
            "Damage between 0 and this value scales the bump from 'low damage' up to 'full strength'; anything above is drawn at full strength."),
        ["HitDirectionHeavyDamage"] = E("Hit Direction: Heavy Damage Threshold",
            "Body damage at or above this value is drawn in the 'heavy' color."),
        ["HitDirectionHeightHigh"] = E("Hit Direction: Bump Height (Full Strength)",
            "Height of the bump at the full-strength damage (canvas pixels)."),
        ["HitDirectionHeightLow"] = E("Hit Direction: Bump Height (Low Damage)",
            "Height of the bump when damage is near 0 (canvas pixels, outward from the ellipse)."),
        ["HitDirectionMaxBursts"] = E("Hit Direction: Max Simultaneous Clusters",
            "Upper limit of clusters shown at once under sustained fire; when full, the oldest one is recycled early. " +
            "Lower it to reduce screen clutter during heavy fire."),
        ["HitDirectionMaxDots"] = E("Hit Direction: Max Dots per Cluster",
            "Maximum number of dots in one cluster; when the shape needs more, the dot spacing is enlarged instead of cutting one side off."),
        ["HitDirectionOpacity"] = E("Hit Direction: Overall Opacity",
            "Opacity multiplier of the whole lattice. If it blocks your view, lower this first."),
        ["HitDirectionRadialFade"] = E("Hit Direction: Tip Fade",
            "Opacity gradient from the baseline to the tip: 0 = no fade, 1 = the tip is fully transparent."),
        ["HitDirectionRetractStart"] = E("Hit Direction: Retract Start Time",
            "When the duration reaches this fraction, the cluster starts retracting from the tip and fading out (0.45 = at 45%). Smaller = it disappears earlier."),
        ["HitDirectionRevealPortion"] = E("Hit Direction: Grow-In Portion",
            "How much of the duration the lattice spends growing row by row from the baseline to the tip (0.16 = the first 16%)."),
        ["HitDirectionRimAlpha"] = E("Hit Direction: Dot Rim Opacity",
            "Opacity of each dot's dark outer rim: it keeps dot outlines readable on snow / daytime backgrounds and is barely visible on dark ones."),
        ["HitDirectionSizeShrink"] = E("Hit Direction: Tip Shrink",
            "How much smaller the dots get from the baseline to the tip: 0 = same size, 0.9 = the tip dots are only a tenth of the size."),
        ["HitDirectionTailRatio"] = E("Hit Direction: Baseline Tail Length",
            "How many times the half-width the faded dotted line on either side of the bump extends. 1 = no tail."),
        ["HitDirectionWidthHigh"] = E("Hit Direction: Bump Half-Width (Full Strength)",
            "Half-width of the bump at the full-strength damage (canvas pixels)."),
        ["HitDirectionWidthLow"] = E("Hit Direction: Bump Half-Width (Low Damage)",
            "Half-width of the bump when damage is near 0 (canvas pixels). Smaller = sharper and more concentrated."),

        // ---------------------------------------------------------------- 12 Raid Summary
        ["RaidSummaryDelay"] = E("Display Delay (s)",
            "How long to wait after the main menu has loaded before showing the summary, so the loading and menu intro animations finish first. " +
            "Right after a raid the game is still in transition / results screens; in testing the menu became usable after about 25–33 s, " +
            "so the plugin waits for the menu instead of showing as soon as the raid ends."),
        ["RaidSummaryDuration"] = E("Display Duration (s)",
            "How long the summary stays (including a 0.35 s fade-in and a 0.6 s fade-out)."),
        ["RaidSummaryEnabled"] = E("Enabled",
            "After a raid, once you are back at the main menu, show this raid's stats on screen for a few seconds: " +
            "kills / headshot kills / longest kill / best streak / score / damage dealt / assists / achievements. " +
            "Raids where nothing happened (e.g. entering and leaving the hideout) are not shown."),
        ["RaidSummaryFontSize"] = E("Font Size",
            "Font size of the stat rows; the title is 4 larger, and the panel size scales with the font."),
        ["RaidSummaryOffsetX"] = E("Horizontal Offset",
            "Horizontal pixels from the screen center to the panel center. Positive = right."),
        ["RaidSummaryOffsetY"] = E("Vertical Offset",
            "Vertical pixels from the screen center to the panel center. Positive = up."),

        // ---------------------------------------------------------------- 20 Damage Numbers
        ["DamageAccumulateLifetime"] = E("Accumulate Fade Duration (s)",
            "With 'Layout = Accumulate': how long the number takes to fade out after hits (or the kill) stop."),
        ["DamageAccumulateWindow"] = E("Accumulate Window (s)",
            "With 'Layout = Accumulate': another hit on the same target within this time keeps adding to the number; " +
            "after it expires, the next hit starts a new number."),
        ["DamageMultiTextPadding"] = E("Multi-Hit Spacing",
            "When several numbers within the same window are laid out in one row, this is the gap between them."),
        ["DamageTextArmorColor"] = E("Armor Hit Color",
            "Number color when only armor was hit (no body damage this hit); the small shield icon to the right of the number has the same color."),
        ["DamageTextColor"] = E("Color"),
        ["DamageTextEnabled"] = E("Enabled", "Show the damage number of each hit to the right of the hit icon."),
        ["DamageTextHeadshotColor"] = E("Headshot Color",
            "Fill color of the number on headshot hits (the old project only changed the outline on headshots; the fill stayed white). " +
            "Setting it equal to 'Color' means no distinction."),
        ["DamageTextHeadshotOutlineColor"] = E("Headshot Outline Color"),
        ["DamageTextKillColor"] = E("Kill Color",
            "The number of the killing blow turns this color (in Battlefield the last hit's number is red)."),
        ["DamageTextLayout"] = E("Layout",
            "LineRight (default, the old project's style) = every hit adds another number to the right, popping up as a row; " +
            "Accumulate (Battlefield 6 style) = one number per target rolls up within the window and turns red on a kill."),
        ["DamageTextLifetime"] = E("Lifetime (s)",
            "The old project shared the duration with the hit icon; this one is independent — set it equal to 'Display Duration' to match."),
        ["DamageTextMode"] = E("Value",
            "BodyOnly (default, same as the old project) = body damage only; an armor hit that does not penetrate shows 0, so nothing is drawn. " +
            "Total = body + armor; if the total is 0, no number is shown."),
        ["DamageTextOutlineColor"] = E("Outline Color",
            "Dark outline. The old default was 'white at 25% opacity', which is the same as no outline for white text and unreadable on bright backgrounds. " +
            "The last two hex digits are opacity; 00 = no outline."),
        ["DamageTextOutlineWidth"] = E("Outline Width", "TMP normalized outline width (0 = off, 0.24 ≈ 2 px)."),
        ["DamageTextPadding"] = E("Gap from Icon", "Gap between the first number and the right edge of the hit icon."),
        ["DamageTextPopDuration"] = E("Pop Duration (s)", "Time to settle from the pop scale back to normal size, in seconds."),
        ["DamageTextPopScale"] = E("Pop Scale",
            "Scale at the moment the number appears; it settles back to 1 over 'Pop Duration'. 1 = no pop."),
        ["DamageTextSize"] = E("Font Size"),

        // ---------------------------------------------------------------- 30 Kill Card · Content
        ["AssistEnabled"] = E("Enable Assist Notice",
            "When a target you hit is killed by someone else, an 'Assist + damage' notice pops up below the kill card; name and faction reuse the kill card's style."),
        ["AssistNoticeDuration"] = E("Assist Notice Duration (s)",
            "Total time from the assist notice appearing to it fully fading out."),
        ["AssistWindowSeconds"] = E("Assist Window (s)",
            "How long after your last hit a target killed by someone else still counts as an assist."),
        ["CardGlitchEnabled"] = E("Glitch Effect",
            "Battlefield-style glitch animation: elements flash in as solid color blocks and then resolve, and the score digits tumble to their final value; " +
            "on exit they flicker out staggered. Off = plain fade in / out (turn it off if you are sensitive to flicker)."),
        ["KillFeedDuration"] = E("Card Duration (s)",
            "Total time from the last kill until the card has fully disappeared (including the final flicker-out). " +
            "Another kill within the window extends the card and accumulates. The Battlefield 6 recordings show the whole card for about 3.4 s; " +
            "this card carries more info, so the default is a bit longer."),
        ["KillFeedEnabled"] = E("Enabled",
            "Show the kill card; when off, the card content, layout and animation settings below have no effect."),
        ["NameTransliterate"] = E("Transliterate Names",
            "Show Cyrillic nicknames on kill and assist cards (Russian bot names such as 'Протасий Могила') in Latin letters (Protasiy Mogila). " +
            "Display only; revenge detection still uses the original name. The game font can already show Cyrillic, so this is off by default."),
        ["ShowAchievement"] = E("Show Achievement Banner",
            "Appears above the main row only when an achievement is triggered (Double Kill / Avenger / Berserker …); on multi-kills a skull emblem is shown on its right."),
        ["ShowBossName"] = E("Show Boss Name", "Bosses use 'Killa / Glukhar / Shturman…' instead of the generic BOSS."),
        ["ShowFaction"] = E("Show Faction Text",
            "The faction label before the name (USEC / BEAR / SCAV …, tinted by faction)."),
        ["ShowFactionIcon"] = E("Show Faction Icon",
            "PMC rank badge (Icons/RankIcons/RankN.png), shown at the far left of the card. Other factions have no badge art, so nothing is shown for them."),
        ["ShowKillDetails"] = E("Show Kill Details", "The dim small line below the tags: hit location · weapon."),
        ["ShowKillMethod"] = E("Show Kill Method",
            "Show method tags such as 'Melee / Explosive / Sniper…'; firearm kills show none (it is the default method, writing it would only be noise)."),
        ["ShowLevelChip"] = E("Show Level on Badge", "The small level chip at the lower right of the rank badge."),
        ["ShowPlayerName"] = E("Show Player Name", "Show the nickname of the latest kill's target."),
        ["ShowScore"] = E("Show Score",
            "Show the card's accumulated feedback score. The formula is in '50. Achievements & Scoring'; it is not the game's experience points."),
        ["ShowSkull"] = E("Show Skull", "Show the skull icons on the kill card."),
        ["ShowStreakBar"] = E("Show Streak Progress Bar",
            "The thin bar under the main row showing progress toward the next streak title."),
        ["ShowTags"] = E("Show Kill Tags",
            "Outlined small boxes under the name: Kill / Headshot (kills within the window are merged), Melee / Explosive / Sniper, distance."),

        // ---------------------------------------------------------------- 31 Kill Card · Layout
        ["AchievementHeight"] = E("Achievement Banner Height", "Height of the achievement banner, in canvas pixels."),
        ["AchievementPadding"] = E("Achievement Banner Side Padding", "How much wider the banner is than its text."),
        ["AchievementVerticalOffset"] = E("Achievement Banner Vertical Offset", "Pixels further up from the default position."),
        ["CardTextOutline"] = E("Card Text Outline",
            "Strength of the dark outline on card text (TMP normalized width, 0 = no outline). This is the baseline value at 24 px; " +
            "other font sizes scale proportionally — the same value on small text would eat most of the strokes. It keeps text readable on bright backgrounds."),
        ["FactionIconSize"] = E("Faction Icon Size",
            "Edge length of the PMC rank badge (pixels), padded by a dark plate with a 7 px margin. The art is 66×66; " +
            "the old default of 40 was too small — the light relief turned into a gray blob at 40 px."),
        ["FactionIconVerticalOffset"] = E("Faction Icon Vertical Offset",
            "Fine-tune up/down from the default position (negative = up). By default it is vertically centered on 'main row + name + tags'."),
        ["FontSizeAchievement"] = E("Achievement Font Size"),
        ["FontSizeKillDetails"] = E("Kill Details Font Size"),
        ["FontSizePlayerName"] = E("Player Name Font Size"),
        ["FontSizeScore"] = E("Score Font Size"),
        ["FontSizeTag"] = E("Tag Font Size"),
        ["KillFeedHorizontalOffset"] = E("Horizontal Center Offset",
            "Pixels the card spine (the right-aligned edge of skull / name / tags) sits to the left of the screen center. Positive = left, negative = right. " +
            "The score extends about 110 px to the right of the spine, so the default 190 puts it just left of the crosshair without covering the hit marker " +
            "(the old project's 107 would cover it)."),
        ["KillFeedLineSpacing"] = E("Line Spacing", "Gap between adjacent card rows."),
        ["KillFeedMaxSkulls"] = E("Max Skulls",
            "Maximum number of skulls side by side on one card; beyond that the oldest is pushed out."),
        ["KillFeedVerticalOffset"] = E("Vertical Center Offset",
            "Position of the main row's (skull + score) center. Positive = down, negative = up."),
        ["ScoreHorizontalOffset"] = E("Score Horizontal Offset",
            "Pixels the score text's left edge sits to the right of the spine."),
        ["SkullSize"] = E("Skull Size", "Height of the skull body (pixels)."),
        ["SkullSpacing"] = E("Skull Spacing", "Gap between adjacent skulls (pixels)."),
        ["StreakBarGap"] = E("Progress Bar Gap from Skull",
            "Gap from the main row's lower edge to the progress bar; name / tags / details automatically move down to make room."),
        ["StreakBarHeight"] = E("Progress Bar Height", "Height of the streak progress bar, in canvas pixels."),
        ["StreakBarWidth"] = E("Progress Bar Width",
            "Minimum width of the streak progress bar; it extends automatically when the skull row is wider."),
        ["TagMinDistance"] = E("Distance Tag Minimum Distance (m)",
            "The distance tag is shown only when the kill distance reaches this value; 0 = show whenever there is a distance, a huge number = never show."),

        // ---------------------------------------------------------------- 32 Kill Card · Colors
        ["ColorAchievementEpic"] = E("Achievement Color: Epic"),
        ["ColorAchievementGood"] = E("Achievement Color: Good"),
        ["ColorAchievementGreat"] = E("Achievement Color: Great"),
        ["ColorAchievementNormal"] = E("Achievement Color: Normal",
            "The accent color on the banner's left edge (not the whole background): it differs by achievement tier."),
        ["ColorAchievementText"] = E("Achievement Text Color",
            "The banner background is light steel (Battlefield style), so the text is dark."),
        ["ColorBEAR"] = E("Color: BEAR"),
        ["ColorBOSS"] = E("Color: BOSS"),
        ["ColorBannerFill"] = E("Banner Background Color",
            "Background of the achievement banner and the assist strip (light steel). Banner text is fixed dark, so pick a light color when changing it."),
        ["ColorCardHeadshot"] = E("Skull Color: Headshot",
            "Skull color for headshot kills (coral red). Headshot skulls also have a V-shaped crack on top, so they are not told apart by color alone."),
        ["ColorCardNormal"] = E("Skull Color: Normal",
            "Color of skulls on normal kills, plus the multi-kill emblem and the score flash (Battlefield 6's ice white)."),
        ["ColorFaction"] = E("Faction Text Color"),
        ["ColorFollower"] = E("Color: Follower"),
        ["ColorKillDetails"] = E("Kill Details Color"),
        ["ColorPlayerName"] = E("Player Name Color",
            "Enemy names in Battlefield 6 are this coral red; pure red is not bright enough on dark backgrounds."),
        ["ColorRAIDER"] = E("Color: RAIDER"),
        ["ColorRouges"] = E("Color: Rouges"),
        ["ColorSCAV"] = E("Color: SCAV"),
        ["ColorScore"] = E("Score Color"),
        ["ColorSectant"] = E("Color: Sectant"),
        ["ColorStreakBar"] = E("Progress Bar Color"),
        ["ColorStreakBarBack"] = E("Progress Bar Background Color"),
        ["ColorTagDistance"] = E("Tag Color: Distance", "Outline and text color of the distance tag (e.g. 87m)."),
        ["ColorTagHeadshot"] = E("Tag Color: Headshot", "Outline and text color of the 'Headshot' tag."),
        ["ColorTagKill"] = E("Tag Color: Kill", "Outline and text color of the plain 'Kill' tag."),
        ["ColorTagSpecial"] = E("Tag Color: Special Kill", "Outline and text color of the 'Melee / Explosive / Sniper' tags."),
        ["ColorUSEC"] = E("Color: USEC"),

        // ---------------------------------------------------------------- 33 Kill Card · Animation
        ["CardExitDuration"] = E("Card Fade-Out Duration (s)",
            "How long the card's final exit animation lasts (skull + name flicker out first, tags follow, the score cuts last)."),
        ["ScoreCountDuration"] = E("Score Count Duration (s)",
            "Time for the number to roll from the old score to the new one (about 0.25 s in the Battlefield 6 recordings); 0 = jump straight to the final value."),
        ["ScorePopDuration"] = E("Score Pop Duration (s)", "Time to settle from the pop scale back to normal size, in seconds."),
        ["ScorePopScale"] = E("Score Pop Scale", "Pop-scale ratio each time the score changes; 1 = no pop."),
        ["SkullPopDuration"] = E("Skull Pop Duration (s)", "Time to settle from the pop scale back to normal size, in seconds."),
        ["SkullPopScale"] = E("Skull Pop Scale",
            "Scale at the moment a new skull appears (above 1 = it grows first, then settles)."),
        ["SkullPushAnimationSpeed"] = E("Skull Push Animation Speed",
            "Smoothing speed at which older skulls make room to the left when a new skull appears."),

        // ---------------------------------------------------------------- 40 Kill Icon Effects
        ["EnableIconGlow"] = E("Enable Icon Glow",
            "Adds a glow to the kill skull; when off, glow color, intensity and spread have no effect."),
        ["EnableRingEffect"] = E("Enable Ring Effect",
            "Plays a kill ring feedback at the skull position; when off, the ring settings below have no effect."),
        ["IconGlowColor"] = E("Glow Color"),
        ["IconGlowIntensity"] = E("Glow Intensity", "Opacity multiplier of the glow; 0 = no glow."),
        ["IconGlowSize"] = E("Glow Spread (px)", "Changing it triggers a glow texture re-bake (cached)."),
        ["RingCritColor"] = E("Crit Color"),
        ["RingCritRadius"] = E("Crit Radius"),
        ["RingCritThickness"] = E("Crit Thickness"),
        ["RingEffectDelay"] = E("Delay (s)", "How long to wait after a kill before the ring effect starts, in seconds."),
        ["RingEffectDuration"] = E("Duration (s)", "Total duration of the ring's expansion and fade-out, in seconds."),
        ["RingExplosionColor"] = E("Explosion Color"),
        ["RingExplosionRadius"] = E("Explosion Radius"),
        ["RingExplosionSecondRingDelay"] = E("Explosion Second-Ring Delay",
            "Delay of the second ring of explosion kills relative to the first, in seconds."),
        ["RingExplosionThickness"] = E("Explosion Thickness"),
        ["RingHeadshotColor"] = E("Headshot Color"),
        ["RingHeadshotEnabled"] = E("Trigger on Headshot",
            "With the ring effect enabled, headshot kills also play the ring feedback."),
        ["RingHeadshotRadius"] = E("Headshot Radius"),
        ["RingHeadshotThickness"] = E("Headshot Thickness"),

        // ---------------------------------------------------------------- 50 Achievements & Scoring
        ["EliteLevel"] = E("Elite Level Threshold", "Killing a PMC at or above this level counts as an 'Elite Kill'."),
        ["GunnerWindow"] = E("Gunner Window (s)", "BF6 'Gunner': kill another with a different weapon within the window."),
        ["LongShotDistance"] = E("Long Shot Distance (m)",
            "Kill distance at which the long-shot check and the long-shot score bonus trigger, in meters."),
        ["MarauderWindow"] = E("Marauder Window (s)", "BF6 'Marauder': kill another within the window after the 4th kill."),
        ["MarksmanDistance"] = E("Marksman Distance (m)", "'Headshot + reaching this distance' = Marksman."),
        ["MultiKillWindow"] = E("Multi-Kill Window (s)",
            "Two kills closer together than this → Double Kill / Triple Kill / Multi Kill."),
        ["RevengeWindow"] = E("Revenge Window (s)",
            "Remembers who killed you; killing the same one within this window = Avenger."),
        ["ScoreBase"] = E("Score: Base Value",
            "Base feedback score for each kill; this score is not the game's experience points."),
        ["ScoreBossBonus"] = E("Score: Boss Bonus", "Extra feedback score for killing a boss."),
        ["ScoreHeadshotBonus"] = E("Score: Headshot Bonus", "Extra feedback score for a headshot kill."),
        ["ScoreLongShotBonus"] = E("Score: Long Shot Bonus", "Added when the distance is at or above the 'Long Shot Distance' above."),
        ["ScoreMultiKillBonus"] = E("Score: Multi-Kill Bonus", "One share added for each extra kill in the chain."),
        ["ScoreRevengeBonus"] = E("Score: Revenge Bonus", "Extra feedback score for a revenge kill."),
        ["StreakWindow"] = E("Streak Time Window (s)",
            "Two kills closer together than this count as a streak (used by streak titles and the progress bar)."),

        // ---------------------------------------------------------------- 60 Sound
        ["SoundEnabled"] = E("Enable Sound", "Master switch; when off, no hit or kill sounds play."),
        ["SoundFileDoubleKill"] = E("Custom: Double Kill", SoundHint("double_kill")),
        ["SoundFileHit"] = E("Custom: Hit", SoundHint("hit")),
        ["SoundFileHitHeadshot"] = E("Custom: Headshot Hit", SoundHint("headshot_hit")),
        ["SoundFileKill"] = E("Custom: Kill", SoundHint("kill")),
        ["SoundFileKillHeadshot"] = E("Custom: Headshot Kill", SoundHint("headshot_kill")),
        ["SoundFileMultiKill"] = E("Custom: Four or More Kills", SoundHint("multi_kill")),
        ["SoundFileTripleKill"] = E("Custom: Triple Kill", SoundHint("triple_kill")),
        ["SoundHitEnabled"] = E("Enable Hit Sound",
            "Plays normal and headshot hit sounds; still controlled by the master sound switch."),
        ["SoundHitHeadshotVolume"] = E("Headshot Hit Sound Volume", "0..1"),
        ["SoundHitVolume"] = E("Hit Sound Volume", "0..1"),
        ["SoundKillEnabled"] = E("Enable Kill Sound",
            "Plays normal and headshot kill sounds; still controlled by the master sound switch."),
        ["SoundKillHeadshotVolume"] = E("Headshot Kill Sound Volume", "0..1"),
        ["SoundKillVolume"] = E("Kill Sound Volume", "0..1"),
        ["SoundMasterVolume"] = E("Master Volume", "0..1"),
        ["SoundMultiKillEnabled"] = E("Enable Streak Sounds",
            "Double / triple / four-plus kills use Sounds/double_kill, triple_kill and multi_kill respectively; " +
            "if a file is missing it falls back to the normal kill sound (never silent). The tier is decided by 'Multi-Kill Window'."),

        // ---------------------------------------------------------------- 90 Debug & Troubleshooting
        ["ConfigVersion"] = E("Config Version (do not edit)",
            "Internal: when upgrading the config, used to reset styles whose defaults changed, once. Editing it is pointless — it is overwritten on the next start."),
        ["DebugEnabled"] = E("Enable Debug Log",
            "Writes the raw fields of every hit / kill / sound playback to BepInEx/LogOutput.log. Turn it on only when troubleshooting."),
        ["SoundBypassEffects"] = E("Bypass Effects Chain (troubleshooting)",
            "Maps to AudioSource.bypassEffects + bypassListenerEffects + bypassReverbZones. Off by default — Unity's docs say it bypasses effects on the AudioListener, " +
            "which is exactly where Meta XR Audio renders sound, so turning it on may leave you with no sound at all."),
        ["SoundIgnoreListenerPause"] = E("Ignore Listener Pause (troubleshooting)",
            "Maps to AudioSource.ignoreListenerPause. Off by default."),
        ["SoundSpatialMode"] = E("Spatial Mode",
            "FollowCamera3D (default) = the sound source follows the camera every frame and goes through the game's sound-field pipeline " +
            "(this EFT version uses Meta XR Audio for spatial audio); it is the only mode confirmed to be audible in real raids. " +
            "Flat2D = the old project's approach; whether it is audible in raids has not been verified. " +
            "If 3D sounds muffled by spatialization, try Flat2D, and switch back if there is no sound."),
        ["TestDeathKey"] = E("Test Death Hotkey",
            "Records the next test kill's target as your killer; then test a kill to preview the Avenger achievement."),
        ["TestHitKey"] = E("Test Hit Hotkey",
            "Simulates a hit; requires test hotkeys to be enabled, and also works in the main menu."),
        ["TestHotkeysEnabled"] = E("Enable Test Hotkeys",
            "Press the keys below to fire a hit / kill feedback instantly without a real fight. Independent of the debug log switch."),
        ["TestKillKey"] = E("Test Kill Hotkey",
            "Cycles through simulated kills; press quickly several times to preview the accumulating multi-kill card."),
        ["TestTakenKey"] = E("Test Hit-Taken Hotkey",
            "Simulates taking a hit: bearing, damage and whether armor alone absorbed it are all random, to preview the hit-direction lattice; " +
            "requires test hotkeys to be enabled."),
    };

    /// <summary>表里所有属性名（测试用）。</summary>
    internal static IEnumerable<string> Names => Table.Keys;

    internal static bool TryGet(string name, out string enName, out string? enDesc)
    {
        if (Table.TryGetValue(name, out var e))
        {
            enName = e.Name;
            enDesc = e.Desc;
            return true;
        }
        enName = name;
        enDesc = null;
        return false;
    }

    /// <summary>英文显示名。表里没有就退回属性名（不抛异常：宁可显示得难看，也不能让面板加载失败）。</summary>
    internal static string Name(string name) => Table.TryGetValue(name, out var e) ? e.Name : name;

    /// <summary>英文分类名；认不出的分类原样返回。</summary>
    internal static string Category(string zhCategory)
        => Categories.TryGetValue(zhCategory, out var en) ? en : zhCategory;

    internal static IEnumerable<string> CategoryKeys => Categories.Keys;

    /// <summary>
    /// 没有专用说明的条目的通用兜底句，对应 <c>ConfigPanel.Hint</c> 的通用分支（中文版拼的是中文键名，英文版拼英文显示名）。
    /// </summary>
    internal static string Generic(string name, string enName)
    {
        if (name.Contains("Volume", StringComparison.Ordinal))
            return "Volume range 0–100%; the actual playback volume is the master volume multiplied by this volume.";
        if (name.Contains("Color", StringComparison.Ordinal))
            return enName + ", used to tint the corresponding element.";
        if (name.Contains("Radius", StringComparison.Ordinal) || name.Contains("Thickness", StringComparison.Ordinal))
            return enName + ", in canvas pixels.";
        if (name.StartsWith("FontSize", StringComparison.Ordinal) || name == "DamageTextSize")
            return enName + ", in canvas pixels; the overall size also scales with UI Scale.";
        return enName + ". Sizes are in canvas pixels and scale with UI Scale.";
    }

    /// <summary>
    /// 面板上显示的完整英文说明 = 专用说明（或通用兜底句）+ 与中文版 <c>ConfigPanel.Bind</c> 相同条件的附加句。
    /// </summary>
    /// <param name="section">cfg 小节名（中文，原样）。</param>
    /// <param name="isColor">默认值是 #RRGGBB… 颜色字符串。</param>
    /// <param name="hasNumericRange">声明了数值范围（候选值列表不算）。</param>
    internal static string Compose(string name, string section, bool isColor, bool hasNumericRange)
    {
        TryGet(name, out string enName, out string? desc);
        string text = desc ?? Generic(name, enName);

        if (isColor) text += ColorNote;
        if (section == "3. 击杀信息") text += KillCardNote;
        if (name.StartsWith("SoundFile", StringComparison.Ordinal)) text += SoundFileNote;
        if (hasNumericRange) text += RangeNote;
        return text;
    }
}
