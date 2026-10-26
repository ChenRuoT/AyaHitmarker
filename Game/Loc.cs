using System;

namespace AyaHitmarker.Game;

/// <summary>界面语言。只有显示层用它；cfg 的键名永远是中文（改键名 = 新键 + 旧键成孤儿，见 AGENTS.md §1）。</summary>
internal enum Lang
{
    Zh = 0,
    En = 1,
}

/// <summary>
/// 屏幕文字的语言选择（0.7.0）。纯托管、不引用 Unity / EFT，所以能进离线测试。
///
/// **用法**：中英文并排写在调用点 —— <c>Loc.Pick("双杀", "Double Kill")</c>。
/// 约 70 条屏幕文字，zh / en 相邻最不容易漂移，也不需要另维护一张 id 表。
/// 日志一律保持中文，不走这里。
///
/// **线程**：<see cref="Current"/> 只在主线程写（<c>AyaBehaviour</c> 约 1Hz 重新解析），
/// 任何线程都可以读（事件线程在冻结击杀部位名时会读）。底层是一个 int 的 volatile 读写，不需要锁。
/// </summary>
internal static class Loc
{
    /// <summary>设置项的三个取值。设置项是字符串（<c>AcceptableValueList</c>），所以面板上能直接显示成这三个词。</summary>
    internal const string SettingAuto = "自动";
    internal const string SettingChinese = "中文";
    internal const string SettingEnglish = "English";

    private static volatile int _current;   // (int)Lang；enum 不能直接 volatile

    internal static Lang Current
    {
        get => (Lang)_current;
        set => _current = (int)value;
    }

    internal static bool IsEnglish => Current == Lang.En;

    /// <summary>按当前语言取其一。</summary>
    internal static T Pick<T>(T zh, T en) => IsEnglish ? en : zh;

    /// <summary>
    /// 设置 + 游戏语言 → 实际语言。
    /// <c>中文</c> / <c>English</c> 是强制的；其余（含 <c>自动</c> 与任何认不出的值）跟随游戏语言。
    /// </summary>
    internal static Lang Resolve(string? setting, string? gameCulture)
    {
        string s = (setting ?? string.Empty).Trim();
        if (string.Equals(s, SettingEnglish, StringComparison.OrdinalIgnoreCase)) return Lang.En;
        if (string.Equals(s, SettingChinese, StringComparison.Ordinal)) return Lang.Zh;
        return FromGameCulture(gameCulture);
    }

    /// <summary>
    /// 游戏语言代码 → 界面语言（<c>EFT.LocalizationManager.Instance.Culture</c>，见 docs/recon-s4-language.md）。
    /// 中文 → Zh；取不到（null / 空）→ Zh，保持 0.6 的行为；其余语言一律 En
    /// （游戏有 ru / fr / ge 等十几种，本插件只提供中英两套）。
    /// 取值格式（<c>ch</c> 还是 <c>zh-CN</c>）还没在真实环境里确认，所以两种都认。
    /// </summary>
    internal static Lang FromGameCulture(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture)) return Lang.Zh;

        string c = culture!.Trim().ToLowerInvariant();
        if (c == "ch" || c == "cn" || c.StartsWith("zh", StringComparison.Ordinal) || c.Contains("chinese"))
            return Lang.Zh;

        return Lang.En;
    }

    /// <summary>字符串里有没有 CJK 字符（测试用：英文模式下屏幕文字不该含中文）。</summary>
    internal static bool ContainsCjk(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char ch in s!)
        {
            if ((ch >= '一' && ch <= '鿿') || (ch >= '㐀' && ch <= '䶿')
                || (ch >= '　' && ch <= '〿') || (ch >= '＀' && ch <= '￯'))
                return true;
        }
        return false;
    }
}
