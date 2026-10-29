using AyaHitmarker.Game;

namespace AyaHitmarker.LogicTests;

internal static class KillExperienceFacts
{
    internal static void Run()
    {
        Assert.Suite("KillExperience: build 47965 native arithmetic");
        int[] combo = { 0, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
        Assert.Equal(0, KillExperience.ComboPercent(1, combo), "first kill");
        Assert.Equal(20, KillExperience.ComboPercent(2, combo), "second raid kill, independent of time");
        Assert.Equal(100, KillExperience.ComboPercent(100, combo), "combo saturates");
        Assert.Equal(0, KillExperience.ComboPercent(0, combo), "native lower clamp");
        Assert.Equal(55, KillExperience.Calculate(55, 0, 0).Total, "bot default body kill");
        var head = KillExperience.Calculate(55, 1.1f, 0);
        Assert.Equal(60, head.HeadshotBonus, "head multiplier is a separate truncated bonus");
        Assert.Equal(115, head.Total, "bot default headshot: 55 + 60, not 60");
        Assert.Equal(264, KillExperience.Calculate(120, 1.2f, 0).Total, "PMC default headshot");
        Assert.Equal(126, KillExperience.Calculate(55, 1.1f, 20).Total, "head and combo additive");
        var rounding = KillExperience.Calculate(57, 1.1f, 30);
        Assert.Equal(62, rounding.HeadshotBonus, "head rounds independently");
        Assert.Equal(17, rounding.ComboBonus, "combo rounds independently");
        Assert.Equal(136, rounding.Total, "independent rounding total");
        Assert.Equal(0, KillExperience.Calculate(0, 1.1f, 100).Total, "zero template is a valid zero");
        var rules = new KillExperience.Rules(120, 55, 1.2f, 1.1f, combo);
        Assert.False(KillExperience.TryCalculate(default, rules, false, 1, out _), "missing input cannot fabricate XP");
        Assert.True(KillExperience.TryCalculate(new(true, 4, -1, false, true), rules, true, 1, out var fallback), "negative template uses game bot default");
        Assert.Equal(115, fallback.Total, "game fallback differs from custom score");
        KillExperience.TryCalculate(new(true, 1, 7000, false, true), rules, false, 1, out var pmc);
        Assert.Equal(120, pmc.Total, "PMC side ignores template reward");
        KillExperience.TryCalculate(new(true, 4, 0, false, true), rules, true, 3, out var zero);
        Assert.Equal(0, zero.Total, "real zero does not use fallback");
        KillExperience.TryCalculate(new(true, 4, 7000, true, true), rules, true, 3, out var group);
        Assert.Equal(0, group.Total, "same group kill grants no XP");
        var custom = rules with { BotBase = 90, BotHead = 0.5f, Combo = new[] { 0, 50 } };
        KillExperience.TryCalculate(new(true, 4, -1, false, true), custom, true, 2, out var modified);
        Assert.Equal(180, modified.Total, "server-modified rules honored");
        string json = """{"err":0,"data":{"config":{"exp":{"kill":{"victimLevelExp":150,"victimBotLevelExp":80,"pmcHeadShotMult":1.3,"botHeadShotMult":0.5,"combo":[{"percent":0},{"percent":25}]}}}}}""";
        var parsed = KillExperience.ParseBackendJson(json);
        Assert.Equal(150, parsed.PmcBase, "backend envelope PMC base");
        Assert.Equal(80, parsed.BotBase, "backend envelope bot base");
        Assert.Near(1.3, parsed.PmcHead, 0.0001, "backend head multiplier");
        Assert.Equal(25, parsed.Combo[1], "backend combo percent");
        KillExperience.TryCalculate(new(true, 4, -1, false, true), parsed, true, 2, out var parsedXp);
        Assert.Equal(140, parsedXp.Total, "HTTP rules drive actual arithmetic");
        Assert.True(Rejected("{\"err\":1}"), "backend error rejected");
        Assert.True(Rejected(json.Replace("\"percent\":25", "\"percent\":-1")), "negative combo rejected");
        Assert.True(Rejected(json.Replace("\"victimBotLevelExp\":80", "\"victimBotLevelExp\":-1")), "invalid basis rejected");
        Assert.True(Rejected("{}"), "missing config is not replaced with constants");
    }

    private static bool Rejected(string json)
    {
        try { KillExperience.ParseBackendJson(json); return false; }
        catch (System.Exception) { return true; }
    }
}
