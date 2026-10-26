namespace AyaHitmarker.Game;

/// <summary>命中级爆头是从哪个字段判出来的（诊断日志用，也决定了可信度）。</summary>
internal enum HeadSource : byte
{
    /// <summary>没有任何字段指向头部。</summary>
    None = 0,

    /// <summary>被打中的 <c>BodyPartCollider.BodyPartType</c> == Head。游戏判定击杀爆头用的就是同一个枚举，最可信。</summary>
    BallisticBodyPart = 1,

    /// <summary>被打中的 <c>BodyPartCollider.BodyPartColliderType</c> 落在头部碰撞体集合里（<c>BodyPartType</c> 读不到时的后备）。</summary>
    BallisticCollider = 2,

    /// <summary>
    /// <c>DamageInfo.BodyPartColliderType</c>。**子弹命中时它恒为 None**（2026-10-25 实机 106 条命中里 104 条是 None，
    /// 仅有的 2 条 HeadCommon 都是 <c>type=Barbed</c> 的铁丝网环境伤害），只有环境伤害路径会填。
    /// </summary>
    DamageInfoCollider = 3,
}

/// <summary>
/// 命中级爆头判定（纯逻辑，枚举值由调用方用 <c>(int)</c> 传进来，所以不引用 EFT，能进离线测试）。
///
/// **为什么要有这个文件**：0.6.1 及以前只看 <c>DamageInfo.BodyPartColliderType</c>，它在子弹命中时恒为
/// <c>None</c> —— 游戏是在 <c>BodyPartCollider.ApplyHit</c> 之后才把它写进 <c>DamageInfo</c>，
/// 而 <c>OnBeingHit</c> 比那更早触发。所以命中级爆头对枪械伤害从来没生效过（击杀级爆头走
/// <c>OnPlayerDeadStatic</c> 的 <c>EBodyPart</c> 参数，一直是对的）。
/// 现在改读 <c>DamageInfo.HittedBallisticCollider</c>（<c>Shot</c> 在构造 <c>DamageInfo</c> 时带进来的，命中瞬间就有）
/// 并转成 <c>BodyPartCollider</c>，取它的 <c>BodyPartType</c>。
///
/// 数值取自 <c>ref/Il2CppDumperOutput/dump.cs</c> 的 <c>EBodyPart</c> / <c>EBodyPartColliderType</c>。
/// </summary>
internal static class HeadshotRules
{
    /// <summary>读不到 / 没有。<c>EBodyPartColliderType.None</c> 也是 -1，所以两种“没有”不用区分。</summary>
    internal const int Unavailable = -1;

    /// <summary><c>EBodyPart.Head</c>。</summary>
    internal const int BodyPartHead = 0;

    /// <summary>
    /// 头部相关的碰撞体：HeadCommon(0) / ParietalHead(12) / BackHead(13) / Ears(14) / Eyes(15) / Jaw(16)。
    /// **颈部（NeckFront 17 / NeckBack 18）不算头部**：2026-10-26 真实战局日志里这两个碰撞体的 <c>BodyPartType</c> 是 Chest(1)，
    /// 游戏自己（击杀事件的 <c>EBodyPart</c>）也把颈部算胸部。这个函数只是读不到 <c>BodyPartType</c> 时的后备，必须与游戏的归类一致。
    /// </summary>
    internal static bool IsHeadCollider(int colliderType)
        => colliderType == 0 || (colliderType >= 12 && colliderType <= 16);

    /// <summary>
    /// 综合三处线索。<paramref name="ballisticBodyPart"/> 读得到（≥ 0）时它说了算——
    /// 即使碰撞体编号看起来像头部，部位枚举是什么就是什么；读不到才依次退到碰撞体编号、<c>DamageInfo</c> 自带字段。
    /// 参数传 <see cref="Unavailable"/> 表示该处没读到。
    /// </summary>
    internal static HeadSource Classify(int ballisticBodyPart, int ballisticColliderType, int infoColliderType)
    {
        if (ballisticBodyPart >= 0)
            return ballisticBodyPart == BodyPartHead ? HeadSource.BallisticBodyPart : HeadSource.None;

        if (ballisticColliderType >= 0)
            return IsHeadCollider(ballisticColliderType) ? HeadSource.BallisticCollider : HeadSource.None;

        return IsHeadCollider(infoColliderType) ? HeadSource.DamageInfoCollider : HeadSource.None;
    }
}
