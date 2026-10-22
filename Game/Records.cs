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

    /// <summary>本次命中只有护甲伤害、没有肉体伤害（打中防弹板）。0.6 a 的护甲标记 / 护甲配色用。</summary>
    internal readonly bool ArmorOnly;

    /// <summary>
    /// 目标标识（0.6 b 累加伤害用）：优先 <c>Player.ProfileId</c>，拿不到时退回归一化昵称。
    /// 只读的一次性字符串快照，不持有 interop 对象。
    /// </summary>
    internal readonly string TargetKey;

    internal readonly Vector3 HitPoint;

    /// <summary>
    /// 0.6 e 受击方向：从**受害者指向攻击者来源**的世界向量（<c>MasterOrigin - HitPoint</c>，
    /// 取不到时退回 <c>-Direction</c>）。事件线程上算好、存值类型，主线程再用相机换算成屏幕方位角。
    /// </summary>
    internal readonly Vector3 IncomingDirection;

    internal HitRecord(
        float bodyDamage,
        float armorDamage,
        bool headshot,
        bool victimIsLocalPlayer,
        bool armorOnly,
        string targetKey,
        Vector3 hitPoint,
        Vector3 incomingDirection)
    {
        BodyDamage = bodyDamage;
        ArmorDamage = armorDamage;
        Headshot = headshot;
        VictimIsLocalPlayer = victimIsLocalPlayer;
        ArmorOnly = armorOnly;
        TargetKey = targetKey;
        HitPoint = hitPoint;
        IncomingDirection = incomingDirection;
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

    /// <summary>受害者 <c>ProfileId</c>（0.6 b/c：把击杀与命中侧的目标对上；测试击杀用 <c>test:</c> 前缀）。</summary>
    internal readonly string VictimProfileId;

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

    /// <summary>击杀者是不是本地玩家。0.6 c：false 表示「别人打死的」—— 主线程拿它去助攻表里查。</summary>
    internal readonly bool KillerIsLocalPlayer;

    internal KillRecord(
        string victimName,
        string victimKey,
        string victimProfileId,
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
        float distance,
        bool killerIsLocalPlayer)
    {
        VictimName = victimName;
        VictimKey = victimKey;
        VictimProfileId = victimProfileId;
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
        KillerIsLocalPlayer = killerIsLocalPlayer;
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

/// <summary>
/// 一次助攻的冻结快照（0.6 c）：我打过、但被别人打死的目标。
/// 只带显示所需字段（名字 / 阵营 / 累计伤害 / 是否爆头），和 <see cref="KillRecord"/> 一样是跨线程值类型。
/// </summary>
internal readonly struct AssistRecord
{
    internal readonly string VictimName;
    internal readonly WildSpawnType Role;
    internal readonly string BotType;
    internal readonly bool IsPmc;
    internal readonly bool VictimIsBoss;
    internal readonly int VictimLevel;
    internal readonly float Damage;
    internal readonly bool Headshot;

    internal AssistRecord(string victimName, WildSpawnType role, string botType, bool isPmc,
        bool victimIsBoss, int victimLevel, float damage, bool headshot)
    {
        VictimName = victimName;
        Role = role;
        BotType = botType;
        IsPmc = isPmc;
        VictimIsBoss = victimIsBoss;
        VictimLevel = victimLevel;
        Damage = damage;
        Headshot = headshot;
    }
}

/// <summary>一次「我方阵亡」的冻结快照，只用于复仇判定。</summary>
internal readonly struct LocalDeathRecord{
    internal readonly string KillerName;
    internal readonly string KillerKey;

    internal LocalDeathRecord(string killerName, string killerKey)
    {
        KillerName = killerName;
        KillerKey = killerKey;
    }
}
