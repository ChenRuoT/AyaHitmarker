using AyaHitmarker.Game;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// <c>EBodyPart</c> → 中文显示名。
///
/// 这一组是**回归测试**：曾经把 <c>LocalizationExtensions.Localized(part)</c> 当成「取本地化名」用，
/// 实际它命中了泛型重载 <c>Localized&lt;TEnum&gt;</c>，查不到时把 key 原样返回，
/// 于是击杀详情里出现 <c>EBodyPart/LeftLeg</c> 这种字符串。现在改成确定性查表，
/// 而且不管喂进来的是「类型/成员」还是纯成员名都要能认出来。
/// </summary>
internal static class BodyPartFacts
{
    internal static void Run()
    {
        Assert.Suite("BodyPart: 类型/成员 拆名");
        {
            Assert.Equal("LeftLeg", BodyPartNames.MemberName("EBodyPart/LeftLeg"), "斜杠形式");
            Assert.Equal("RightArm", BodyPartNames.MemberName("EFT.EBodyPart.RightArm"), "点号形式");
            Assert.Equal("Head", BodyPartNames.MemberName("Head"), "纯成员名保持原样");
            Assert.Equal("Stomach", BodyPartNames.MemberName("  Stomach  "), "去掉首尾空白");
            Assert.Equal(string.Empty, BodyPartNames.MemberName(""), "空字符串");
            Assert.Equal(string.Empty, BodyPartNames.MemberName(null), "null");
            // 分隔符在末尾时没有“后面那一段”可切，原样返回（别切出空串）
            Assert.Equal("EBodyPart/", BodyPartNames.MemberName("EBodyPart/"), "分隔符在末尾 → 原样返回");
            Assert.Equal("左腿", BodyPartNames.Describe("EBodyPart/", 5), "这种畸形输入也能靠数值兜底");
        }

        Assert.Suite("BodyPart: 中文映射（枚举成员名）");
        {
            Assert.Equal("头部", BodyPartNames.Describe("Head", 0), "Head");
            Assert.Equal("胸部", BodyPartNames.Describe("Chest", 1), "Chest");
            Assert.Equal("腹部", BodyPartNames.Describe("Stomach", 2), "Stomach");
            Assert.Equal("左臂", BodyPartNames.Describe("LeftArm", 3), "LeftArm");
            Assert.Equal("右臂", BodyPartNames.Describe("RightArm", 4), "RightArm");
            Assert.Equal("左腿", BodyPartNames.Describe("LeftLeg", 5), "LeftLeg");
            Assert.Equal("右腿", BodyPartNames.Describe("RightLeg", 6), "RightLeg");
            Assert.Equal("躯干", BodyPartNames.Describe("Common", 7), "Common（坠落/爆炸那种不分部位的）");
        }

        Assert.Suite("BodyPart: 回归 —— 喂进 Localized<TEnum> 产生的 key 也要认");
        {
            // 这就是实测在屏幕上看到的字符串
            Assert.Equal("左腿", BodyPartNames.Describe("EBodyPart/LeftLeg", 5), "EBodyPart/LeftLeg → 左腿");
            Assert.Equal("头部", BodyPartNames.Describe("EBodyPart/Head", 0), "EBodyPart/Head → 头部");
            // 就算本体在别处（比如 Mod 换了个枚举名），拆出成员名照样能映射
            Assert.Equal("右腿", BodyPartNames.Describe("SomeOtherEnum/RightLeg", -1), "换个类型名也认成员");
        }

        Assert.Suite("BodyPart: 数值兜底与未知回退");
        {
            // ToString() 给出数字（或给了个不认识的成员名）时，还有数值这条路
            Assert.Equal("左腿", BodyPartNames.Describe("5", 5), "纯数字 → 数值表");
            Assert.Equal("右臂", BodyPartNames.Describe("Watchamacallit", 4), "未知成员名 → 数值表");
            // 两条路都不通：退回成员名本身，而不是显示成空白
            Assert.Equal("Tentacle", BodyPartNames.Describe("EBodyPart/Tentacle", 99), "完全未知 → 成员名原样");
            Assert.Equal("未知", BodyPartNames.Describe("", -1), "什么都给不出来 → 未知");
        }
    }
}
