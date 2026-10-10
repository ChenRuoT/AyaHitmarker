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

/// <summary>一次击杀的冻结快照。</summary>
internal readonly struct KillRecord
{
    internal readonly string VictimName;
    internal readonly int VictimLevel;
    internal readonly WildSpawnType Role;
    internal readonly bool Headshot;
    internal readonly bool VictimIsLocalPlayer;
    internal readonly string BodyPart;
    internal readonly string Weapon;
    internal readonly float Distance;
    internal readonly int Experience;
    internal readonly float Time;

    internal KillRecord(
        string victimName,
        int victimLevel,
        WildSpawnType role,
        bool headshot,
        bool victimIsLocalPlayer,
        string bodyPart,
        string weapon,
        float distance,
        int experience,
        float time)
    {
        VictimName = victimName;
        VictimLevel = victimLevel;
        Role = role;
        Headshot = headshot;
        VictimIsLocalPlayer = victimIsLocalPlayer;
        BodyPart = bodyPart;
        Weapon = weapon;
        Distance = distance;
        Experience = experience;
        Time = time;
    }
}
