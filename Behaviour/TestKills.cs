using AyaHitmarker.Game;

namespace AyaHitmarker.Behaviour;

/// <summary>
/// 测试用的「造一次击杀」脚本表：F9 热键和调试脚本（<see cref="DebugHarness"/>）共用。
///
/// 表里按顺序覆盖了所有成就分支 / 阵营着色 / 军衔徽章档位 / 部位映射，
/// 这样不需要真的打架就能把击杀卡片的所有版式看一遍。
/// </summary>
internal static class TestKills
{
    private static int _next;

    private static KillRecord[]? _script;
    private static Lang _scriptLang;

    /// <summary>
    /// 脚本表。部位名在 <see cref="Make"/> 里按当前语言冻结，所以切语言后要重建一份，
    /// 否则 F9 还是上一种语言的“胸部”。
    /// </summary>
    internal static KillRecord[] Script
    {
        get
        {
            if (_script == null || _scriptLang != Loc.Current)
            {
                _scriptLang = Loc.Current;
                _script = BuildScript();
            }
            return _script;
        }
    }

    private static KillRecord[] BuildScript() => new[]
    {
        // 1. 普通 PMC 击杀：阵营着色 + 军衔徽章 + 得分基础值 + 距离标签（部位会显示「胸部」）
        Make("Nikita", EFT.WildSpawnType.pmcUSEC, 42, false, EBodyPart.Chest, "M4A1", Loc.Pick("枪械", "Firearm"), false, false, false, 87.4f),
        // 2. PMC 爆头：爆头标签 + 红骷髅 + Headshot 环
        Make("Target", EFT.WildSpawnType.pmcBEAR, 35, true, EBodyPart.Head, "SR-25", Loc.Pick("狙击", "Sniper"), true, false, false, 142.7f),
        // 3. 远程 + 爆头 → 神射手（史诗横幅 + 爆炸环）
        Make("Sniper", EFT.WildSpawnType.pmcUSEC, 55, true, EBodyPart.Head, "M700", Loc.Pick("狙击", "Sniper"), true, false, false, 268f),
        // 4. SCAV 近战 → 冷兵器；左腿用来验部位映射；没有军衔徽章（只有 PMC 有）
        Make("Scav", EFT.WildSpawnType.assault, 0, false, EBodyPart.LeftLeg, Loc.Pick("砍刀", "Machete"), Loc.Pick("近战", "Melee"), true, true, false, 1.2f),
        // 5. 首领 + 爆炸 → 首领猎杀者（名字与阵营标记相同，只显示一次）
        Make("Killa", EFT.WildSpawnType.bossKilla, 0, false, EBodyPart.Stomach, Loc.Pick("F-1 手雷", "F-1 Grenade"), Loc.Pick("爆炸", "Explosion"), true, false, true, 12.5f),
        // 6. 右臂：再验一次部位映射；连推几次就会看到双杀 / 三杀 / 狂战士 与连杀进度条
        Make("Guard", EFT.WildSpawnType.pmcBot, 0, false, EBodyPart.RightArm, "AK-74M", Loc.Pick("枪械", "Firearm"), false, false, false, 33f),
        // 7. 低等级 PMC 爆头近距离：没有距离标签，徽章是最低档
        Make("Rookie", EFT.WildSpawnType.pmcUSEC, 3, true, EBodyPart.Head, "AKM", Loc.Pick("枪械", "Firearm"), false, false, false, 8f),
        // 8. 满级 PMC：最高档徽章 + 长名字（检验右对齐 / 徽章随内容变宽往左让）
        Make("ГенералЛеденев_Superstar", EFT.WildSpawnType.pmcBEAR, 78, false, EBodyPart.Chest, "MCX SPEAR", Loc.Pick("枪械", "Firearm"), false, false, false, 120f),
    };

    internal static int Count => Script.Length;

    /// <summary>F9：轮换到下一条。</summary>
    internal static KillRecord Next()
    {
        var rec = Script[_next % Script.Length];
        _next++;
        return rec;
    }

    /// <summary>当前下标（只给日志用）。</summary>
    internal static int Index => _next;

    /// <summary>F10 模拟阵亡时的「凶手」= 下一次 F9 要杀的那个人，这样 F10 再 F9 必定出「复仇者」。</summary>
    internal static KillRecord Peek() => Script[_next % Script.Length];

    /// <summary>按 1 起始的序号取（调试脚本用，越界回绕）。</summary>
    internal static KillRecord At(int oneBased)
    {
        int i = ((oneBased - 1) % Script.Length + Script.Length) % Script.Length;
        return Script[i];
    }

    private static KillRecord Make(
        string name,
        EFT.WildSpawnType role,
        int level,
        bool headshot,
        EBodyPart bodyPart,
        string weapon,
        string method,
        bool methodNotable,
        bool melee,
        bool explosive,
        float distance)
    {
        string botType = BotTypeMapping.GetBotType(role);
        return new KillRecord(
            name,
            NameKey.Normalize(name),
            // 测试击杀没有真实 ProfileId：用稳定前缀，方便 0.6 b 的累加数字在 F8→F9 时对上同一个槽
            "test:" + NameKey.Normalize(name),
            level,
            role,
            botType,
            BotTypeMapping.IsPmc(botType),
            BotTypeMapping.IsBoss(botType),
            headshot,
            // 走和真实击杀完全相同的部位解析（Game/BodyPartNames.cs）——
            // 这样按 F9 就能直接看到映射结果，不用等真打一枪
            GameEvents.LocalizeBodyPart(bodyPart),
            weapon,
            method,
            methodNotable,
            melee,
            explosive,
            sniperDamage: false,
            distance,
            killerIsLocalPlayer: true);
    }
}
