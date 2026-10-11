using EFT;
using UnityEngine;

namespace AyaHitmarker.Game;

/// <summary>
/// 一次命中的冻结快照。
///
/// 关键点：事件可能在非主线程触发，handler 里**不能碰 Unity API**，也**不能长期持有**
/// <c>Player</c> / <c>DamageInfo</c> 这些 interop 代理（原生对象销毁后就是 use-after-free）。
/// 所以只在这里存值类型和已转好的字符串。
/// </summary>
internal readonly struct HitRecord
{
    internal readonly float BodyDamage;
    internal readonly float ArmorDamage;
    internal readonly bool Headshot;
    internal readonly bool VictimIsLocalPlayer;
    internal readonly Vector3 HitPoint;

    internal HitRecord(
        float bodyDamage,
        float armorDamage,
        bool headshot,
        bool victimIsLocalPlayer,
        Vector3 hitPoint)
    {
        BodyDamage = bodyDamage;
        ArmorDamage = armorDamage;
        Headshot = headshot;
        VictimIsLocalPlayer = victimIsLocalPlayer;
        HitPoint = hitPoint;
    }

    internal float TotalDamage => BodyDamage + ArmorDamage;

    internal float DisplayDamage(DamageNumberMode mode)
        => mode == DamageNumberMode.BodyOnly ? BodyDamage : BodyDamage + ArmorDamage;
}

/// <summary>
/// 一次击杀的冻结快照。
///
/// 除了「谁被打死」，这里还把**展示所需的全部派生字段**在事件线程上算好：
/// 阵营分类、击杀方式、近战/爆炸/狙击归类、复仇判定用的昵称键。
/// 这样主线程只做排版和动画，不再回头碰 interop 对象。
/// </summary>
internal readonly struct KillRecord
{
    internal readonly string VictimName;

    /// <summary>归一化昵称（去空白 + 小写），只用于复仇判定，不显示。</summary>
    internal readonly string VictimKey;

    internal readonly int VictimLevel;
    internal readonly WildSpawnType Role;

    /// <summary><see cref="BotTypeMapping.GetBotType"/> 的结果：USEC / BEAR / SCAV / BOSS / …</summary>
    internal readonly string BotType;

    internal readonly bool IsPmc;
    internal readonly bool VictimIsBoss;
    internal readonly bool Headshot;

    internal readonly string BodyPart;
    internal readonly string Weapon;

    /// <summary>击杀方式（枪械 / 近战 / 爆炸 / 狙击 / 地雷 …），来自 <c>DamageInfo.DamageType</c>。</summary>
    internal readonly string Method;

    /// <summary>方式是否值得单独显示（枪械这种默认方式就不显示，避免刷屏）。</summary>
    internal readonly bool MethodIsNotable;

    internal readonly bool Melee;
    internal readonly bool Explosive;
    internal readonly bool SniperDamage;

    internal readonly float Distance;

    internal KillRecord(
        string victimName,
        string victimKey,
        int victimLevel,
        WildSpawnType role,
        string botType,
        bool isPmc,
        bool victimIsBoss,
        bool headshot,
        string bodyPart,
        string weapon,
        string method,
        bool methodIsNotable,
        bool melee,
        bool explosive,
        bool sniperDamage,
        float distance)
    {
        VictimName = victimName;
        VictimKey = victimKey;
        VictimLevel = victimLevel;
        Role = role;
        BotType = botType;
        IsPmc = isPmc;
        VictimIsBoss = victimIsBoss;
        Headshot = headshot;
        BodyPart = bodyPart;
        Weapon = weapon;
        Method = method;
        MethodIsNotable = methodIsNotable;
        Melee = melee;
        Explosive = explosive;
        SniperDamage = sniperDamage;
        Distance = distance;
    }

    /// <summary>转成判定引擎用的纯数据快照。</summary>
    internal KillFact ToFact() => new KillFact(
        VictimKey,
        NameKey.Normalize(Weapon),
        BotType,
        VictimLevel,
        Distance,
        Headshot,
        Melee,
        Explosive,
        SniperDamage,
        VictimIsBoss,
        IsPmc);
}

/// <summary>一次「我方阵亡」的冻结快照，只用于复仇判定。</summary>
internal readonly struct LocalDeathRecord
{
    internal readonly string KillerName;
    internal readonly string KillerKey;

    internal LocalDeathRecord(string killerName, string killerKey)
    {
        KillerName = killerName;
        KillerKey = killerKey;
    }
}
