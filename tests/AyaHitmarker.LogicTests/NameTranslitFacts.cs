using System.Text;
using AyaHitmarker.Game;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// 昵称音译（0.7.0）。
///
/// 输入一律用 <c>\uXXXX</c> 转义写：西里尔字母和拉丁字母有一堆长得一模一样的（А / A、е / e、о / o、с / c …），
/// 直接敲字面量，哪天编辑器或输入法悄悄换成拉丁字母，测试会“通过”但什么也没测到。
/// 样本是 2026-10-25 实机日志里真实出现的 bot 名字。
/// </summary>
internal static class NameTranslitFacts
{
    internal static void Run()
    {
        Assert.Suite("音译: 实机日志里的真实 bot 名字");
        {
            // Протасий Могила / Гиви Кацман / ГенералЛеденев_Superstar
            Assert.Equal("Protasiy Mogila",
                NameTranslit.Transliterate("Протасий Могила"),
                "Протасий Могила");
            Assert.Equal("Givi Katsman",
                NameTranslit.Transliterate("Гиви Кацман"),
                "Гиви Кацман");
            Assert.Equal("GeneralLedenev_Superstar",
                NameTranslit.Transliterate("ГенералЛеденев_Superstar"),
                "西里尔 + 拉丁混合的名字，拉丁部分保持原样");
        }

        Assert.Suite("音译: 多字符映射与大小写");
        {
            Assert.Equal("Zhuk", NameTranslit.Transliterate("Жук"), "Ж → Zh（只有首字母大写）");
            Assert.Equal("Shchuka", NameTranslit.Transliterate("Щука"), "Щ → Shch");
            Assert.Equal("Khrushchev", NameTranslit.Transliterate("Хрущев"), "Х → Kh，щ → shch");
            Assert.Equal("Yuriy", NameTranslit.Transliterate("Юрий"), "Ю → Yu，й → y");
            Assert.Equal("Yakov", NameTranslit.Transliterate("Яков"), "Я → Ya");
            Assert.Equal("Tanka", NameTranslit.Transliterate("Танька"), "软音符号 ь 消失");
            Assert.Equal("Elka", NameTranslit.Transliterate("Ёлка"), "Ёлка：Ё → E（沿用旧项目映射）");
        }

        Assert.Suite("音译: 不该动的");
        {
            Assert.Equal("Nikita", NameTranslit.Transliterate("Nikita"), "纯拉丁原样返回");
            Assert.Equal("天空", NameTranslit.Transliterate("天空"), "汉字原样返回");
            Assert.Equal(string.Empty, NameTranslit.Transliterate(string.Empty), "空串");
            Assert.Equal(string.Empty, NameTranslit.Transliterate(null), "null → 空串");
            Assert.Equal("José", NameTranslit.Transliterate("José"), "带音标的拉丁字母不动（游戏字体能显示）");

            // 整个名字只有被吞掉的字母（ъ / ь）：别变成空串，原样保留
            string onlySigns = "ьъ";
            Assert.Equal(onlySigns, NameTranslit.Transliterate(onlySigns), "只有 ь ъ → 原样（不变空串）");
        }

        Assert.Suite("音译: 整个字母表都有映射");
        {
            var all = new StringBuilder();
            for (char c = 'А'; c <= 'я'; c++) all.Append(c);   // А–я
            all.Append('Ё').Append('ё');                       // Ё ё
            all.Append("ІіЇїЄєҐґЎў");   // І і Ї ї Є є Ґ ґ Ў ў
            string result = NameTranslit.Transliterate(all.ToString());
            Assert.False(NameTranslit.HasCyrillic(result), "音译后不残留任何西里尔字母: " + result);
            Assert.True(result.Length > 0, "结果非空");

            Assert.True(NameTranslit.HasCyrillic("А"), "HasCyrillic: А");
            Assert.False(NameTranslit.HasCyrillic("A"), "HasCyrillic: 拉丁 A（长得一样但不是西里尔）");
            Assert.False(NameTranslit.HasCyrillic(null), "HasCyrillic: null");
        }

        Assert.Suite("音译: 缓存与显示开关");
        {
            string name = "Гиви";
            string a = NameTranslit.Transliterate(name);
            string b = NameTranslit.Transliterate(name);
            Assert.Equal(a, b, "同一输入两次结果一致（走缓存）");
            Assert.Equal("Givi", a, "Гиви → Givi");

            Assert.Equal(name, NameTranslit.ForDisplay(name, enabled: false), "开关关闭 → 原名");
            Assert.Equal("Givi", NameTranslit.ForDisplay(name, enabled: true), "开关打开 → 音译");

            // 缓存超过上限会清空重来，但结果依然正确
            for (int i = 0; i < 700; i++)
                NameTranslit.Transliterate("А" + i);
            Assert.Equal("Givi", NameTranslit.Transliterate(name), "缓存溢出清空后结果依然正确");
        }

        Assert.Suite("音译: 复仇判定用原名 —— 音译有损，不能进 key");
        {
            // ё 和 е 音译后相同；ъ 和 ь 都消失。如果拿音译结果当 key，这两组不同的名字会撞成同一个人。
            string p1 = "Пётр";   // Пётр
            string p2 = "Петр";   // Петр
            Assert.Equal(NameTranslit.Transliterate(p1), NameTranslit.Transliterate(p2), "音译后 Пётр / Петр 相同（有损）");
            Assert.NotEqual(NameKey.Normalize(p1), NameKey.Normalize(p2), "但归一化昵称（复仇 key）仍然区分它们");
        }
    }
}
