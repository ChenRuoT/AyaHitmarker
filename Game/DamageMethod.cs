using EFT;

namespace AyaHitmarker.Game;

/// <summary>
/// <c>DamageInfo.DamageType</c>（<see cref="EDamageType"/>，位标志）→「击杀方式」（中 / 英随界面语言）。
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

    /// <summary>方式名（按当前界面语言）。</summary>
    internal static string Describe(EDamageType t)
    {
        if (t == EDamageType.Undefined) return Loc.Pick("未知", "Unknown");

        if (Has(t, EDamageType.SniperBlackDivision)) return Loc.Pick("狙击", "Sniper");
        if (Has(t, EDamageType.Sniper)) return Loc.Pick("狙击", "Sniper");
        if (Has(t, EDamageType.Melee)) return Loc.Pick("近战", "Melee");
        if (Has(t, EDamageType.Blunt)) return Loc.Pick("钝击", "Blunt");
        if (Has(t, EDamageType.Explosion)) return Loc.Pick("爆炸", "Explosion");
        if (Has(t, EDamageType.ThermobaricExplosion)) return Loc.Pick("温压爆炸", "Thermobaric");
        if (Has(t, EDamageType.GrenadeFragment)) return Loc.Pick("破片", "Fragmentation");
        if (Has(t, EDamageType.Landmine)) return Loc.Pick("地雷", "Landmine");
        if (Has(t, EDamageType.Artillery)) return Loc.Pick("炮击", "Artillery");
        if (Has(t, EDamageType.Btr)) return "BTR";
        if (Has(t, EDamageType.MachineGunner)) return Loc.Pick("机枪", "Machine Gun");
        if (Has(t, EDamageType.Flame)) return Loc.Pick("火焰", "Fire");
        if (Has(t, EDamageType.HotGases)) return Loc.Pick("高温气体", "Hot Gas");
        if (Has(t, EDamageType.TearGas)) return Loc.Pick("催泪瓦斯", "Tear Gas");
        if (Has(t, EDamageType.Barbed)) return Loc.Pick("倒刺", "Barbed Wire");
        if (Has(t, EDamageType.Impact)) return Loc.Pick("冲击", "Impact");
        if (Has(t, EDamageType.Bullet)) return Loc.Pick("枪械", "Firearm");
        if (Has(t, EDamageType.Fall)) return Loc.Pick("坠落", "Fall");
        if (Has(t, EDamageType.Existence)) return Loc.Pick("湮灭", "Annihilation");
        if (Has(t, EDamageType.LethalToxin)) return Loc.Pick("致命毒素", "Lethal Toxin");
        if (Has(t, EDamageType.Poison)) return Loc.Pick("中毒", "Poison");
        if (Has(t, EDamageType.RadExposure)) return Loc.Pick("辐射", "Radiation");
        if (Has(t, EDamageType.Medicine)) return Loc.Pick("药物", "Medicine");
        if (Has(t, EDamageType.Stimulator)) return Loc.Pick("兴奋剂", "Stimulant");
        if (Has(t, EDamageType.HeavyBleeding) || Has(t, EDamageType.LightBleeding)) return Loc.Pick("失血", "Bleeding");
        if (Has(t, EDamageType.Dehydration)) return Loc.Pick("脱水", "Dehydration");
        if (Has(t, EDamageType.Exhaustion)) return Loc.Pick("力竭", "Exhaustion");
        if (Has(t, EDamageType.Environment)) return Loc.Pick("环境", "Environment");

        return t.ToString();
    }

    /// <summary>
    /// 只有「枪械」以外的死法才值得单独占一行 —— 默认击杀方式就是枪械，
    /// 每一条都写「方式:枪械」只会让详情行变长。
    /// </summary>
    internal static bool IsNotable(EDamageType t)
        => t != EDamageType.Undefined && !(t == EDamageType.Bullet);
}
