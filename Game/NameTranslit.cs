using System.Collections.Generic;
using System.Text;

namespace AyaHitmarker.Game;

/// <summary>
/// 西里尔字母 → 拉丁字母的昵称音译（0.7.0，移植旧项目 <c>Utils/LocalizedHelper.Transliterate</c> 的有效路径）。
///
/// 只处理西里尔字母（U+0400–U+052F）：没有西里尔字母的名字原样返回。
/// 旧项目还会对其余名字做 NFKD 去音标，这里不做 —— 游戏字体（Bender）本来就能显示带音标的拉丁字母，没有收益。
///
/// **只用于显示**。复仇判定的 <c>VictimKey</c> / <c>NameKey</c> 必须继续用原名：音译有损（ъ ь 消失，ё → e），
/// 先音译再归一化会让两个不同的人撞成同一个 key，凭空判出“复仇者”。
///
/// 纯托管、不引用 Unity / EFT；**只在主线程用**（缓存是普通字典，不加锁）。
/// </summary>
internal static class NameTranslit
{
    private const int CacheLimit = 512;

    private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>(64);

    private static readonly Dictionary<char, string> Map = BuildMap();

    private static Dictionary<char, string> BuildMap()
    {
        var map = new Dictionary<char, string>(160);

        // 大写 / 小写成对写：大写取首字母大写，小写全小写
        void Add(char upper, char lower, string latin)
        {
            map[upper] = latin.Length == 0 ? "" : char.ToUpperInvariant(latin[0]) + latin.Substring(1);
            map[lower] = latin;
        }

        Add('А', 'а', "a");   Add('Б', 'б', "b");   Add('В', 'в', "v");    Add('Г', 'г', "g");
        Add('Д', 'д', "d");   Add('Е', 'е', "e");   Add('Ё', 'ё', "e");    Add('Ж', 'ж', "zh");
        Add('З', 'з', "z");   Add('И', 'и', "i");   Add('Й', 'й', "y");    Add('К', 'к', "k");
        Add('Л', 'л', "l");   Add('М', 'м', "m");   Add('Н', 'н', "n");    Add('О', 'о', "o");
        Add('П', 'п', "p");   Add('Р', 'р', "r");   Add('С', 'с', "s");    Add('Т', 'т', "t");
        Add('У', 'у', "u");   Add('Ф', 'ф', "f");   Add('Х', 'х', "kh");   Add('Ц', 'ц', "ts");
        Add('Ч', 'ч', "ch");  Add('Ш', 'ш', "sh");  Add('Щ', 'щ', "shch"); Add('Ъ', 'ъ', "");
        Add('Ы', 'ы', "y");   Add('Ь', 'ь', "");    Add('Э', 'э', "e");    Add('Ю', 'ю', "yu");
        Add('Я', 'я', "ya");

        // 乌克兰语 / 白俄罗斯语里常见的几个（bot 名字池里偶有出现）
        Add('І', 'і', "i");   Add('Ї', 'ї', "yi");  Add('Є', 'є', "ye");   Add('Ґ', 'ґ', "g");
        Add('Ў', 'ў', "u");

        return map;
    }

    private static bool IsCyrillic(char c) => c >= 'Ѐ' && c <= 'ԯ';

    /// <summary>名字里有没有西里尔字母。</summary>
    internal static bool HasCyrillic(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        foreach (char c in value!)
        {
            if (IsCyrillic(c)) return true;
        }
        return false;
    }

    /// <summary>音译。没有西里尔字母（或为空）原样返回；映射表里没有的字符原样保留。</summary>
    internal static string Transliterate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        if (!HasCyrillic(value)) return value!;

        if (Cache.TryGetValue(value!, out var cached)) return cached;

        var sb = new StringBuilder(value!.Length + 4);
        foreach (char c in value)
        {
            if (Map.TryGetValue(c, out var latin)) sb.Append(latin);
            else sb.Append(c);
        }

        string result = sb.ToString();
        if (result.Trim().Length == 0) result = value;   // 比如整个名字只有 ъ / ь：别变成空串

        if (Cache.Count >= CacheLimit) Cache.Clear();
        Cache[value] = result;
        return result;
    }

    /// <summary>显示用：开关打开才音译。</summary>
    internal static string ForDisplay(string name, bool enabled) => enabled ? Transliterate(name) : name;
}
