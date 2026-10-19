using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace AyaHitmarker.Ui;

/// <summary>共用的 UI 资源：白色 1x1 精灵、TMP 字体、颜色解析。</summary>
internal static class UiTheme
{
    private static Texture2D? _whiteTexture;
    private static Sprite? _whiteSprite;
    private static TMP_FontAsset? _font;
    private static UnityEngine.Font? _osFont;   // 必须持有强引用，否则字体会被 GC 回收
    private static bool _fontAttempted;

    /// <summary>当前 <see cref="_font"/> 是不是我们自己创建的（系统字体 → TMP_FontAsset）。
    /// 只有自己创建的才能在卸载时销毁；游戏自带的 <c>TMP_Settings.defaultFontAsset</c> 绝不能 Destroy。</summary>
    private static bool _fontOwned;

    private static readonly Dictionary<string, Color> _colorCache = new Dictionary<string, Color>(32);

    internal static Sprite WhiteSprite
    {
        get
        {
            if (_whiteSprite == null)
            {
                _whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _whiteTexture.SetPixel(0, 0, Color.white);
                _whiteTexture.Apply();
                _whiteSprite = Sprite.Create(_whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            }
            return _whiteSprite;
        }
    }

    /// <summary>
    /// 字体解析顺序：
    ///  1. 配置里显式填写的系统字体名 → <c>TMP_FontAsset.CreateFontAsset</c>。
    ///     **本游戏版本上实测（README §6「字体」）所有系统字体都建不出来**（Microsoft YaHei / Arial / SimHei 全部返回 null），
    ///     所以只在用户显式填写时才尝试；默认（留空）不再探测 Arial / Segoe UI。
    ///  2. 游戏自带的 <c>TMP_Settings.defaultFontAsset</c>：它的回退链已覆盖屏上全部中文字符（<c>glyphs</c> 指令实测 0 缺字形）。
    /// </summary>
    internal static TMP_FontAsset? Font
    {
        get
        {
            if (_font != null || _fontAttempted) return _font;
            _fontAttempted = true;

            string wanted = Plugin.Cfg.UiFontName.Value;
            if (!string.IsNullOrWhiteSpace(wanted))
            {
                _font = TryCreateFromOsFont(wanted);
                if (_font == null)
                {
                    Plugin.Logger.LogWarning($"[ui] 系统字体 '{wanted}' 建不出 TMP 字体资源（本游戏版本上系统字体一律不可用），改用游戏自带字体。");
                }
            }

            if (_font == null)
            {
                try
                {
                    _font = TMP_Settings.defaultFontAsset;
                    if (_font != null) Plugin.Logger.LogInfo($"[ui] 使用游戏自带 TMP 字体: {_font.name}");
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning($"[ui] TMP_Settings.defaultFontAsset 不可用: {ex.Message}");
                }
            }

            if (_font == null) Plugin.Logger.LogError("[ui] 找不到任何可用字体，文本将不显示。");
            else LogGlyphCoverage(_font);
            return _font;
        }
    }

    /// <summary>
    /// 往日志里写一行字形覆盖：屏上用到的中文只要有一个字不在字体（含回退链）里就会渲染成方块。
    /// 用户反馈「中文显示成方块」时，这一行直接说明是字体的问题，不用再猜。
    /// </summary>
    private static void LogGlyphCoverage(TMP_FontAsset font)
    {
        const string sample = "击杀爆头双三复仇远程";
        try
        {
            var missing = new System.Text.StringBuilder();
            foreach (char c in sample)
            {
                if (!font.HasCharacter(c, true, true)) missing.Append(c);
            }

            if (missing.Length == 0) Plugin.Logger.LogInfo($"[ui] 字体 '{font.name}' 中文字形检查通过（{sample.Length} 个样本字）");
            else Plugin.Logger.LogWarning($"[ui] 字体 '{font.name}' 缺少中文字形 [{missing}]，对应文字会显示成方块（反馈问题时请附上这一行）");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogDebug($"[ui] 字形覆盖检查失败: {ex.Message}");
        }
    }

    /// <summary>配置改字体后调用，下次访问重新解析。</summary>
    internal static void InvalidateFont()
    {
        _font = null;
        _osFont = null;
        _fontAttempted = false;
        _fontOwned = false;
    }

    /// <summary>
    /// 释放静态 UI 资源（只在插件卸载 / 热重载路径调用）。
    ///
    /// 白色精灵 / 纹理是 <c>HideAndDontSave</c> 的，必须显式 Destroy（同 AudioBank 的 clip）。
    /// 字体只销毁「我们自己从系统字体创建的」：游戏自带的 TMP 字体是共享资源，Destroy 会连累别的 UI。
    /// </summary>
    internal static void Unload()
    {
        if (_whiteSprite != null) UnityEngine.Object.Destroy(_whiteSprite);
        if (_whiteTexture != null) UnityEngine.Object.Destroy(_whiteTexture);
        _whiteSprite = null;
        _whiteTexture = null;
        _colorCache.Clear();

        if (_fontOwned && _font != null)
        {
            Texture2D? atlas = null;
            Material? material = null;
            try { atlas = _font.atlas; } catch { /* 已经销毁 */ }
            try { material = _font.material; } catch { /* 已经销毁 */ }
            UnityEngine.Object.Destroy(_font);
            if (atlas != null) UnityEngine.Object.Destroy(atlas);
            if (material != null) UnityEngine.Object.Destroy(material);
        }

        if (_osFont != null) DestroyOsFont(_osFont);

        InvalidateFont();
    }

    private static TMP_FontAsset? TryCreateFromOsFont(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        UnityEngine.Font? osFont = null;
        bool kept = false;
        try
        {
            // 注意：必须写全 Unity 的 Font。UiTheme 自己有个 Font 属性，简单名会被它遮蔽。
            osFont = UnityEngine.Font.CreateDynamicFontFromOSFont(name.Trim(), 48);
            if (osFont == null) return null;

            var asset = TMP_FontAsset.CreateFontAsset(osFont);
            if (asset == null) return null;

            _osFont = osFont; // 保活
            kept = true;
            _fontOwned = true;
            asset.name = $"AyaHitmarker-{name}";
            Plugin.Logger.LogInfo($"[ui] 已从系统字体创建 TMP 字体资源: {name}");
            return asset;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogDebug($"[ui] 系统字体 '{name}' 不可用: {ex.Message}");
            return null;
        }
        finally
        {
            // 失败路径必须释放：热重载下每一代都会走到这里，不销毁就是每代漏一个 Font + 它的材质 / 贴图。
            if (!kept && osFont != null) DestroyOsFont(osFont);
        }
    }

    /// <summary>
    /// 销毁系统字体。<c>Font</c> 的材质与贴图是独立的 Unity 对象，<c>Destroy(font)</c> 不会连带销毁，
    /// 得先把它们摘出来逐个销毁（否则每个字体漏 1 个 Material + 1 个 Texture2D）。
    /// </summary>
    private static void DestroyOsFont(UnityEngine.Font font)
    {
        try
        {
            var material = font.material;
            if (material != null)
            {
                var texture = material.mainTexture;
                if (texture != null) UnityEngine.Object.Destroy(texture);
                UnityEngine.Object.Destroy(material);
            }
        }
        catch { /* 已经销毁 */ }

        UnityEngine.Object.Destroy(font);
    }

    /// <summary>解析 "#RRGGBB" / "#RRGGBBAA" / "RRGGBB"，带缓存（UI 每帧读配置，不能每帧 parse）。</summary>
    internal static Color Parse(string hex, Color fallback)
    {
        if (string.IsNullOrEmpty(hex)) return fallback;
        if (_colorCache.TryGetValue(hex, out var cached)) return cached;

        var parsed = ParseUncached(hex, fallback);
        if (_colorCache.Count > 256) _colorCache.Clear();
        _colorCache[hex] = parsed;
        return parsed;
    }

    private static Color ParseUncached(string hex, Color fallback)
    {
        var s = hex.Trim();
        if (s.Length > 0 && s[0] == '#') s = s.Substring(1);
        if (s.Length != 6 && s.Length != 8) return fallback;
        if (!uint.TryParse(s, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint v)) return fallback;

        if (s.Length == 6)
            return new Color(((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f, 1f);
        return new Color(((v >> 24) & 0xFF) / 255f, ((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f);
    }

    internal static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

    /// <summary>"24,20" → (24,20)。解析失败返回 fallback。</summary>
    internal static Vector2 ParseOffset(string text, Vector2 fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        var parts = text.Split(',');
        if (parts.Length != 2) return fallback;
        if (!float.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float x)) return fallback;
        if (!float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float y)) return fallback;
        return new Vector2(x, y);
    }

    internal static RectTransform? Rect(GameObject go)
    {
        try { return go.GetComponent<RectTransform>(); }
        catch { return null; }
    }

    /// <summary>创建一个「透明容器」——用 Image 是为了白拿一个 RectTransform，不额外引入类型注入。</summary>
    internal static GameObject CreateContainer(string name, RectTransform? parent)
    {
        var go = new GameObject(name);
        var img = go.AddComponent<UnityEngine.UI.Image>();
        img.sprite = WhiteSprite;
        img.color = new Color(0f, 0f, 0f, 0f);
        img.raycastTarget = false;

        var rt = Rect(go);
        if (rt != null && parent != null)
        {
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }
        return go;
    }

    /// <summary>
    /// 创建一个纯文本对象。
    /// 一定要走这里，**不要**在 <see cref="CreateContainer"/> 的物体上再加 TMP ——
    /// 那样同一个 GameObject 上会有两个 <c>Graphic</c>（Image + TextMeshProUGUI），
    /// 两者会争同一个 CanvasRenderer。
    /// </summary>
    internal static TextMeshProUGUI CreateTextObject(string name, RectTransform? parent, TextAlignmentOptions align)
    {
        var go = new GameObject(name);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.alignment = align;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.font = Font;

        var rt = Rect(go);
        if (rt != null && parent != null)
        {
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }
        return text;
    }

    /// <summary>TMP 描边靠材质关键字，不打开 OUTLINE_ON 就算设了 outlineWidth 也没效果。</summary>
    internal static void EnableTextOutline(TextMeshProUGUI text)
    {
        try
        {
            var mat = text.fontMaterial;
            if (mat != null) mat.EnableKeyword("OUTLINE_ON");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[ui] 启用 TMP 描边失败（不影响其他功能）: {ex.Message}");
        }
    }
}
