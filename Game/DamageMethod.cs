using EFT;

namespace AyaHitmarker.Game;

/// <summary>
/// <c>DamageInfo.DamageType</c>（<see cref="EDamageType"/>，位标志）→ 中文「击杀方式」。
///
/// 这个枚举在本版本里是**位标志**（每个成员是 2 的幂，见 <c>ref/Il2CppDumperOutput/dump.cs</c>），
/// 所以判定一律用 <c>&amp;</c>，不要用 <c>==</c>：一发狙击子弹可能同时带
/// <c>Bullet | Sniper</c> 两个位。
///
/// 描述的顺序 = 从「最具体」到「最泛」，先命中先返回。
/// </summary>
internal static class DamageMethod
{
    internal static bool Has(EDamageType type, EDamageType flag) => (type & flag) != 0;

    internal static bool IsMelee(EDamageType t) => Has(t, EDamageType.Melee) || Has(t, EDamageType.Blunt);

    internal static bool IsExplosive(EDamageType t)
        => Has(t, EDamageType.Explosion)
        || Has(t, EDamageType.GrenadeFragment)
        || Has(t, EDamageType.Landmine)
        || Has(t, EDamageType.Artillery)
        || Has(t, EDamageType.ThermobaricExplosion)
        || Has(t, EDamageType.Btr);

    internal static bool IsSniper(EDamageType t)
        => Has(t, EDamageType.Sniper) || Has(t, EDamageType.SniperBlackDivision);

    /// <summary>中文方式名。</summary>
    internal static string Describe(EDamageType t)
    {
        if (t == EDamageType.Undefined) return "未知";

        if (Has(t, EDamageType.SniperBlackDivision)) return "狙击";
        if (Has(t, EDamageType.Sniper)) return "狙击";
        if (Has(t, EDamageType.Melee)) return "近战";
        if (Has(t, EDamageType.Blunt)) return "钝击";
        if (Has(t, EDamageType.Explosion)) return "爆炸";
        if (Has(t, EDamageType.ThermobaricExplosion)) return "温压爆炸";
        if (Has(t, EDamageType.GrenadeFragment)) return "破片";
        if (Has(t, EDamageType.Landmine)) return "地雷";
        if (Has(t, EDamageType.Artillery)) return "炮击";
        if (Has(t, EDamageType.Btr)) return "BTR";
        if (Has(t, EDamageType.MachineGunner)) return "机枪";
        if (Has(t, EDamageType.Flame)) return "火焰";
        if (Has(t, EDamageType.HotGases)) return "高温气体";
        if (Has(t, EDamageType.TearGas)) return "催泪瓦斯";
        if (Has(t, EDamageType.Barbed)) return "倒刺";
        if (Has(t, EDamageType.Impact)) return "冲击";
        if (Has(t, EDamageType.Bullet)) return "枪械";
        if (Has(t, EDamageType.Fall)) return "坠落";
        if (Has(t, EDamageType.Existence)) return "湮灭";
        if (Has(t, EDamageType.LethalToxin)) return "致命毒素";
        if (Has(t, EDamageType.Poison)) return "中毒";
        if (Has(t, EDamageType.RadExposure)) return "辐射";
        if (Has(t, EDamageType.Medicine)) return "药物";
        if (Has(t, EDamageType.Stimulator)) return "兴奋剂";
        if (Has(t, EDamageType.HeavyBleeding) || Has(t, EDamageType.LightBleeding)) return "失血";
        if (Has(t, EDamageType.Dehydration)) return "脱水";
        if (Has(t, EDamageType.Exhaustion)) return "力竭";
        if (Has(t, EDamageType.Environment)) return "环境";

        return t.ToString();
    }

    /// <summary>
    /// 只有「枪械」以外的死法才值得单独占一行 —— 默认击杀方式就是枪械，
    /// 每一条都写「方式:枪械」只会让详情行变长。
    /// </summary>
    internal static bool IsNotable(EDamageType t)
        => t != EDamageType.Undefined && !(t == EDamageType.Bullet);
}
