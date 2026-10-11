using System.Collections.Generic;
using EFT;

namespace AyaHitmarker.Game;

/// <summary>
/// <c>WildSpawnType</c> → 显示分类（USEC / BEAR / SCAV / BOSS / Follower / Raider / Rouges / Sectant / Infected / Spirit）。
///
/// 与旧 Mono 项目不同的一点：这里**按枚举名字符串**查表，而不是 <c>Dictionary&lt;WildSpawnType, string&gt;</c>。
/// 原因是跨版本时枚举会增删成员，硬编码成员名会编译失败；按名字查表在游戏新增变体时只是
/// 查不到 → 回退到 <c>role.ToString()</c>，不会崩也不会漏编译错误。
/// </summary>
internal static class BotTypeMapping
{
    private static readonly Dictionary<string, string> Map = new Dictionary<string, string>
    {
        // ---- SCAV 系 ----
        ["marksman"] = "SCAV",
        ["assault"] = "SCAV",
        ["cursedAssault"] = "SCAV",
        ["test"] = "SCAV",
        ["assaultGroup"] = "SCAV",
        ["gifter"] = "SCAV",
        ["crazyAssaultEvent"] = "SCAV",
        ["shooterBTR"] = "SCAV",
        ["peacemaker"] = "SCAV",
        ["skier"] = "SCAV",
        ["tagillaHelperAgro"] = "SCAV",

        // ---- PMC ----
        ["pmcBEAR"] = "BEAR",
        ["pmcUSEC"] = "USEC",

        // ---- Raider ----
        ["pmcBot"] = "Raider",
        ["arenaFighter"] = "Raider",
        ["arenaFighterEvent"] = "Raider",

        // ---- Boss ----
        ["bossTest"] = "BOSS",
        ["bossBully"] = "BOSS",
        ["bossKilla"] = "BOSS",
        ["bossKojaniy"] = "BOSS",
        ["bossGluhar"] = "BOSS",
        ["bossSanitar"] = "BOSS",
        ["bossTagilla"] = "BOSS",
        ["bossKnight"] = "BOSS",
        ["followerBigPipe"] = "BOSS",
        ["followerBirdEye"] = "BOSS",
        ["bossZryachiy"] = "BOSS",
        ["bossBoar"] = "BOSS",
        ["bossBoarSniper"] = "BOSS",
        ["peacefullZryachiyEvent"] = "BOSS",
        ["ravangeZryachiyEvent"] = "BOSS",
        ["bossKolontay"] = "BOSS",
        ["bossPartisan"] = "BOSS",
        ["bossTagillaAgro"] = "BOSS",
        ["bossKillaAgro"] = "BOSS",

        // ---- Follower ----
        ["followerTest"] = "Follower",
        ["followerBully"] = "Follower",
        ["followerKojaniy"] = "Follower",
        ["followerGluharAssault"] = "Follower",
        ["followerGluharSecurity"] = "Follower",
        ["followerGluharScout"] = "Follower",
        ["followerGluharSnipe"] = "Follower",
        ["followerSanitar"] = "Follower",
        ["followerTagilla"] = "Follower",
        ["followerZryachiy"] = "Follower",
        ["followerBoar"] = "Follower",
        ["followerBoarClose1"] = "Follower",
        ["followerBoarClose2"] = "Follower",
        ["followerKolontayAssault"] = "Follower",
        ["followerKolontaySecurity"] = "Follower",

        // ---- 其他 ----
        ["exUsec"] = "Rouges",
        ["sectantWarrior"] = "Sectant",
        ["sectantPriest"] = "Sectant",
        ["sectactPriestEvent"] = "Sectant",
        ["sectantPredvestnik"] = "Sectant",
        ["sectantPrizrak"] = "Sectant",
        ["sectantOni"] = "Sectant",
        ["spiritWinter"] = "Spirit",
        ["spiritSpring"] = "Spirit",
        ["infectedAssault"] = "Infected",
        ["infectedPmc"] = "Infected",
        ["infectedCivil"] = "Infected",
        ["infectedLaborant"] = "Infected",
        ["infectedTagilla"] = "Infected",
    };

    /// <summary>
    /// 首领的显示名。旧项目把 <c>bossKilla</c> / <c>bossGluhar</c> 全部压成「BOSS」，
    /// 但击杀播报里报出**具体是谁**才有味道（战地的 "AVENGER" 那种手感）。
    /// 查不到就回退到分类名。
    /// </summary>
    private static readonly Dictionary<string, string> BossNames = new Dictionary<string, string>
    {
        ["bossBully"] = "Reshala",
        ["bossKilla"] = "Killa",
        ["bossKillaAgro"] = "Killa",
        ["bossTagilla"] = "Tagilla",
        ["bossTagillaAgro"] = "Tagilla",
        ["bossKojaniy"] = "Shturman",
        ["bossGluhar"] = "Glukhar",
        ["bossSanitar"] = "Sanitar",
        ["bossZryachiy"] = "Zryachiy",
        ["bossKnight"] = "Knight",
        ["bossBoar"] = "Kaban",
        ["bossBoarSniper"] = "Kaban",
        ["bossKolontay"] = "Kolontay",
        ["bossPartisan"] = "Partisan",
    };

    internal static string GetBotType(WildSpawnType role)
    {
        string name = role.ToString();
        return Map.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v : name;
    }

    /// <summary>只有 PMC 才显示等级（与旧项目一致）。</summary>
    internal static bool IsPmc(string botType)
        => string.Equals(botType, "USEC", System.StringComparison.OrdinalIgnoreCase)
        || string.Equals(botType, "BEAR", System.StringComparison.OrdinalIgnoreCase);

    internal static bool IsBoss(string botType)
        => string.Equals(botType, "BOSS", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>首领显示名；不是首领（或查不到）返回 <paramref name="botType"/> 原值。</summary>
    internal static string DisplayName(WildSpawnType role, string botType)
        => BossNames.TryGetValue(role.ToString(), out var boss) && !string.IsNullOrWhiteSpace(boss)
            ? boss
            : botType;
}
