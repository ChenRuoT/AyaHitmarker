using System;
using System.Text.Json;

namespace AyaHitmarker.Game;

/// <summary>
/// Build 47965 的 OnEnemyKill 经验算术（见 docs/recon-kill-experience-47965.md）。
/// 输入必须由游戏规则解析后冻结；这里不猜模板、阵营、友军或特殊物品减益。
/// </summary>
internal static class KillExperience
{
    internal sealed record Rules(int PmcBase, int BotBase, float PmcHead, float BotHead, int[] Combo);

    internal static Rules ParseBackendJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("err", out var error) && error.GetInt32() != 0)
            throw new FormatException("Backend returned an error");
        if (root.TryGetProperty("data", out var data)) root = data;
        var kill = root.GetProperty("config").GetProperty("exp").GetProperty("kill");
        int pmc = kill.GetProperty("victimLevelExp").GetInt32();
        int bot = kill.GetProperty("victimBotLevelExp").GetInt32();
        float pmcHead = kill.GetProperty("pmcHeadShotMult").GetSingle();
        float botHead = kill.GetProperty("botHeadShotMult").GetSingle();
        var table = kill.GetProperty("combo");
        if (pmc < 0 || bot < 0 || !float.IsFinite(pmcHead) || !float.IsFinite(botHead) || pmcHead < 0 || botHead < 0 ||
            table.GetArrayLength() is < 1 or > 1024) throw new FormatException("Invalid kill XP rules");
        var combo = new int[table.GetArrayLength()];
        for (int i = 0; i < combo.Length; i++)
        {
            combo[i] = table[i].GetProperty("percent").GetInt32();
            if (combo[i] < 0) throw new FormatException("Invalid XP combo percentage");
        }
        return new Rules(pmc, bot, pmcHead, botHead, combo);
    }

    internal static bool TryCalculate(in KillExperienceInput input, Rules rules, bool headshot,
        int raidKills, out Result result)
    {
        result = default;
        if (!input.Available) return false;
        if (input.SameGroup) return true; // 游戏不为同组击杀写经验，但 Kills 已增加。
        bool pmc = input.EffectiveSide is 1 or 2;
        if (!pmc && input.EffectiveSide != 4) return false;
        int basis = pmc ? rules.PmcBase : input.TemplateExperience < 0 ? rules.BotBase : input.TemplateExperience;
        result = Calculate(basis, headshot ? (pmc ? rules.PmcHead : rules.BotHead) : 0,
            ComboPercent(raidKills, rules.Combo));
        return true;
    }
    internal readonly record struct Result(int Base, int HeadshotBonus, int ComboBonus)
    {
        internal int Total => checked(Base + HeadshotBonus + ComboBonus);
    }

    // 原生 GetKillingBonusPercent：下标 killed - 1，钳到数组首尾。
    internal static int ComboPercent(int raidKills, ReadOnlySpan<int> percentages)
    {
        if (percentages.IsEmpty) throw new ArgumentException("Missing game combo table", nameof(percentages));
        int index = raidKills <= 1 ? 0 : Math.Min(raidKills - 1, percentages.Length - 1);
        return percentages[index];
    }

    /// <summary>
    /// baseExperience 是已解析的 killExp。headshotBonusMultiplier 对非爆头传 0；
    /// 爆头传游戏里的 1.1 / 1.2（它是额外奖励，不减 1）。每个奖励独立向零截断。
    /// 不含 ExpDamage、撤离状态或整局经验倍率。
    /// </summary>
    internal static Result Calculate(int baseExperience, float headshotBonusMultiplier, int comboPercent)
    {
        if (baseExperience < 0) throw new ArgumentOutOfRangeException(nameof(baseExperience));
        if (!float.IsFinite(headshotBonusMultiplier) || headshotBonusMultiplier < 0)
            throw new ArgumentOutOfRangeException(nameof(headshotBonusMultiplier));
        if (comboPercent < 0) throw new ArgumentOutOfRangeException(nameof(comboPercent));
        // 保持原生 mulss / divss / cvttss2si 的 float 运算顺序。
        int head = checked((int)(baseExperience * headshotBonusMultiplier));
        int combo = checked((int)((comboPercent / 100f) * baseExperience));
        return new Result(baseExperience, head, combo);
    }
}

/// <summary>仅值类型，允许跨线程入队；0 模板经验与“未读到”分别表示。</summary>
internal readonly record struct KillExperienceInput(bool Available, int EffectiveSide, int TemplateExperience,
    bool SameGroup, bool IsAI);
