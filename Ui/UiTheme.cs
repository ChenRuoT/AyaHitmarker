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
    ///  1. 配置里的 OS 字体（默认 Microsoft YaHei）→ <c>TMP_FontAsset.CreateFontAsset</c>
    ///     这条能同时覆盖拉丁 / 西里尔 / CJK，所以优先。
    ///  2. 游戏自带的 <c>TMP_Settings.defaultFontAsset</c>（覆盖拉丁+西里尔，可能没有中文）。
    ///  3. 常见 OS 字体兜底。
    /// </summary>
    internal static TMP_FontAsset? Font
    {
        get
        {
            if (_font != null || _fontAttempted) return _font;
            _fontAttempted = true;

            _font = TryCreateFromOsFont(Plugin.Cfg.UiFontName.Value);
            if (_font == null) _font = TryCreateFromOsFont("Arial");
            if (_font == null) _font = TryCreateFromOsFont("Segoe UI");

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

            if (_font == null) Plugin.Logger.LogError("[ui] 找不到任何可用字体，文本将不显示。可在配置里改「5. 界面 → 字体名称」。");
            return _font;
        }
    }

    /// <summary>配置改字体后调用，下次访问重新解析。</summary>
    internal static void InvalidateFont()
    {
        _font = null;
        _osFont = null;
        _fontAttempted = false;
    }

    private static TMP_FontAsset? TryCreateFromOsFont(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        try
        {
            // 注意：必须写全 Unity 的 Font。UiTheme 自己有个 Font 属性，简单名会被它遮蔽。
            var osFont = UnityEngine.Font.CreateDynamicFontFromOSFont(name.Trim(), 48);
            if (osFont == null) return null;

            var asset = TMP_FontAsset.CreateFontAsset(osFont);
            if (asset == null) return null;

            _osFont = osFont; // 保活
            asset.name = $"AyaHitmarker-{name}";
            Plugin.Logger.LogInfo($"[ui] 已从系统字体创建 TMP 字体资源: {name}");
            return asset;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogDebug($"[ui] 系统字体 '{name}' 不可用: {ex.Message}");
            return null;
        }
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
}
