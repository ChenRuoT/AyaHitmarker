using System;

namespace AyaHitmarker.Game;

/// <summary>
/// <c>EBodyPart</c> → 显示名（中 / 英随界面语言，见 <see cref="Loc"/>）。
///
/// **为什么不用游戏的本地化 API**：<c>LocalizationExtensions.Localized</c> 有一堆泛型重载，
/// <c>Localized(part)</c> 会命中 <c>Localized&lt;TEnum&gt;(TEnum, EStringCase)</c> —— 它会用
/// 「枚举类型名/成员名」拼一个 key 去查本地化表，**查不到时把 key 原样返回**。
/// 实测结果就是击杀详情报出 <c>EBodyPart/LeftLeg</c> 这种字符串（真实 bug）。
/// 所以这里自己查表，输出可预期、可离线测，也不受 interop / 本地化表影响。
///
/// 纯托管：不引用 <c>UnityEngine</c> / <c>EFT</c>，所以能进 <c>tests/AyaHitmarker.LogicTests</c>。
/// 接受字符串而不是枚举，也是为了让它保持纯净（枚举值由调用方用 <c>(int)part</c> 传进来）。
/// </summary>
internal static class BodyPartNames
{
    /// <summary>
    /// 把各种形式的枚举文本归一成**成员名**：
    /// <c>"EBodyPart/LeftLeg"</c>、<c>"EFT.EBodyPart.LeftLeg"</c>、<c>"LeftLeg"</c> → <c>"LeftLeg"</c>。
    /// 认不出分隔符时原样返回（去空白）。
    /// </summary>
    internal static string MemberName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        string s = raw!.Trim();

        // 取最后一个分隔符之后的部分："A/B" → "B"，"A.B.C" → "C"
        int slash = s.LastIndexOf('/');
        int dot = s.LastIndexOf('.');
        int cut = Math.Max(slash, dot);
        if (cut >= 0 && cut < s.Length - 1) s = s.Substring(cut + 1);

        return s.Trim();
    }

    /// <summary>成员名 → 显示名（按当前界面语言）。认不出返回 <c>null</c>（调用方决定怎么兜底）。</summary>
    internal static string? FromMemberName(string? memberName)
    {
        if (string.IsNullOrEmpty(memberName)) return null;

        switch (memberName)
        {
            case "Head": return Loc.Pick("头部", "Head");
            case "Chest": return Loc.Pick("胸部", "Chest");
            case "Stomach": return Loc.Pick("腹部", "Stomach");
            case "LeftArm": return Loc.Pick("左臂", "Left Arm");
            case "RightArm": return Loc.Pick("右臂", "Right Arm");
            case "LeftLeg": return Loc.Pick("左腿", "Left Leg");
            case "RightLeg": return Loc.Pick("右腿", "Right Leg");
            // AppliesDamage 用的「通用」部位（坠落 / 爆炸这类不分具体部位时用它）
            case "Common": return Loc.Pick("躯干", "Torso");
            default: return null;
        }
    }

    /// <summary>
    /// 按枚举的**数值**兜底（顺序取自 <c>ref/Il2CppDumperOutput/dump.cs</c> 的 <c>EBodyPart</c>）。
    /// 当 <c>ToString()</c> 给出数字或别的怪东西时还有一条路。
    /// </summary>
    internal static string? FromOrdinal(int ordinal) => ordinal switch
    {
        0 => Loc.Pick("头部", "Head"),
        1 => Loc.Pick("胸部", "Chest"),
        2 => Loc.Pick("腹部", "Stomach"),
        3 => Loc.Pick("左臂", "Left Arm"),
        4 => Loc.Pick("右臂", "Right Arm"),
        5 => Loc.Pick("左腿", "Left Leg"),
        6 => Loc.Pick("右腿", "Right Leg"),
        7 => Loc.Pick("躯干", "Torso"),
        _ => null,
    };

    /// <summary>
    /// 一站式：任何形式的原始文本 + 枚举数值 → 显示名。
    /// 依次尝试「类型/成员」拆名 → 中文表 → 数值表 → 拆出来的成员名 → "未知"。
    /// </summary>
    internal static string Describe(string? raw, int ordinal)
    {
        string member = MemberName(raw);

        return FromMemberName(member)
            ?? FromOrdinal(ordinal)
            ?? (member.Length > 0 ? member : Loc.Pick("未知", "Unknown"));
    }
}
