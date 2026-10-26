using AyaHitmarker.Game;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// 命中级爆头判定（0.7.0）。
///
/// 背景：0.6.1 及以前只看 <c>DamageInfo.BodyPartColliderType</c>，它在子弹命中时恒为 None
/// （2026-10-25 实机日志 106 条命中里 104 条 None，仅有的 2 条 HeadCommon 都是 <c>type=Barbed</c> 铁丝网伤害），
/// 所以命中级爆头对枪械伤害从来没生效过。现在读被打中的 <c>BodyPartCollider.BodyPartType</c>。
/// 这里锁死的是“怎么综合三处线索”和枚举数值（取自 dump.cs），读字段本身只能进游戏验证。
/// </summary>
internal static class HeadshotFacts
{
    private const int None = HeadshotRules.Unavailable;

    internal static void Run()
    {
        Assert.Suite("爆头: 头部碰撞体集合（EBodyPartColliderType 数值，取自 dump.cs）");
        {
            // HeadCommon=0, ParietalHead=12, BackHead=13, Ears=14, Eyes=15, Jaw=16
            foreach (int id in new[] { 0, 12, 13, 14, 15, 16 })
                Assert.True(HeadshotRules.IsHeadCollider(id), $"碰撞体 {id} 是头部");

            // RibcageUp=1, Pelvis=3, LeftUpperArm=4 … RightCalf=11, RightSideChestUp=19 … RibcageLow=26
            // 颈部 NeckFront=17 / NeckBack=18 也不是头部：真实战局里它们的 BodyPartType 是 Chest
            foreach (int id in new[] { 1, 3, 4, 5, 6, 7, 8, 9, 10, 11, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26 })
                Assert.False(HeadshotRules.IsHeadCollider(id), $"碰撞体 {id} 不是头部");

            Assert.False(HeadshotRules.IsHeadCollider(-1), "None(-1) 不是头部");
            Assert.False(HeadshotRules.IsHeadCollider(2), "编号 2 在枚举里是空档，不算头部");
            Assert.False(HeadshotRules.IsHeadCollider(27), "越界编号不算头部");
        }

        Assert.Suite("爆头: 实机场景");
        {
            // 子弹命中：DamageInfo 自带的碰撞体是 None，靠被打中的 BodyPartCollider
            Assert.Equal(HeadSource.BallisticBodyPart, HeadshotRules.Classify(0, 12, None), "子弹打头：BodyPartType=Head");
            Assert.Equal(HeadSource.None, HeadshotRules.Classify(1, 1, None), "子弹打胸：BodyPartType=Chest");
            Assert.Equal(HeadSource.None, HeadshotRules.Classify(5, 8, None), "子弹打腿");
            Assert.Equal(HeadSource.None, HeadshotRules.Classify(2, 22, None), "子弹打腹");

            // 0.6.1 的真实问题：DamageInfo.BodyPartColliderType=None 时旧代码永远判不出爆头
            Assert.Equal(HeadSource.None, HeadshotRules.Classify(None, None, None), "三处都读不到 → 不是爆头（不瞎猜）");

            // 环境伤害（铁丝网）：没有被打中的碰撞体，DamageInfo 自带 HeadCommon
            Assert.Equal(HeadSource.DamageInfoCollider, HeadshotRules.Classify(None, None, 0), "铁丝网 HeadCommon 仍然识别");
            Assert.Equal(HeadSource.None, HeadshotRules.Classify(None, None, 1), "环境伤害打在胸口");
        }

        Assert.Suite("爆头: 优先级");
        {
            // BodyPartType 读得到就以它为准，哪怕碰撞体编号像头部（反之亦然）
            Assert.Equal(HeadSource.None, HeadshotRules.Classify(1, 12, 0), "BodyPartType=Chest 压过头部碰撞体编号");
            Assert.Equal(HeadSource.BallisticBodyPart, HeadshotRules.Classify(0, 1, 1), "BodyPartType=Head 压过非头部编号");

            // BodyPartType 读不到才退到碰撞体编号
            Assert.Equal(HeadSource.BallisticCollider, HeadshotRules.Classify(None, 16, None), "退到被打中的碰撞体编号：Jaw");
            Assert.Equal(HeadSource.None, HeadshotRules.Classify(None, 4, 0), "被打中的碰撞体是手臂，压过 DamageInfo 的旧字段");

            // 都没有才看 DamageInfo 自带字段
            Assert.Equal(HeadSource.None, HeadshotRules.Classify(None, None, 18), "NeckBack 不算头部（与游戏对颈部的归类一致）");
        }

        Assert.Suite("爆头: 2026-10-26 真实战局的 98 条命中（part / partCol 实测组合）");
        {
            // 头部：part=0（Head），partCol 取过 0 / 12 / 13 / 14 / 15
            foreach (int col in new[] { 0, 12, 13, 14, 15 })
                Assert.Equal(HeadSource.BallisticBodyPart, HeadshotRules.Classify(0, col, None), $"真实命中 part=0 partCol={col} → 爆头");

            // 非头部：(part, partCol) 的实测组合，包含颈部（part=1 partCol=17/18：游戏把颈部归到胸部）
            var nonHead = new (int Part, int Col)[]
            {
                (1, 1), (1, 21), (1, 17), (1, 18),    // 胸 / 脊柱上段 / 颈前 / 颈后
                (2, 26), (2, 25), (2, 3), (2, 23),    // 腹：肋下 / 左侧下肋 / 骨盆 / 骨盆背
                (3, 5), (3, 4), (4, 6), (4, 7),       // 手臂
                (5, 8), (6, 10),                      // 腿
            };
            bool allNone = true;
            foreach (var (part, col) in nonHead)
                allNone &= HeadshotRules.Classify(part, col, None) == HeadSource.None;
            Assert.True(allNone, "真实战局里出现过的全部非头部组合都不判爆头（含颈部）");

            // 实测里 infoCol 恒为 -1（子弹命中时 DamageInfo.BodyPartColliderType 是 None），这正是 0.6.1 判不出爆头的原因
            Assert.Equal(HeadSource.None, HeadshotRules.Classify(None, None, None), "旧读法（只看 DamageInfo 自带字段）对子弹命中永远是 None");
        }

        Assert.Suite("爆头: 数值契约");
        {
            Assert.Equal(0, HeadshotRules.BodyPartHead, "EBodyPart.Head = 0（与 BodyPartNames.FromOrdinal 一致）");
            Assert.Equal("头部", BodyPartNames.FromOrdinal(HeadshotRules.BodyPartHead), "两个文件对 Head 的数值看法一致");
            Assert.Equal(-1, HeadshotRules.Unavailable, "没有 = -1（EBodyPartColliderType.None 也是 -1）");
        }
    }
}
