using System;
using System.Collections.Generic;
using UnityEngine;

namespace AyaHitmarker.Assets;

/// <summary>一张烘好的图标：精灵 + 「位图高度 / 图形本体高度」（含描边留白的放大系数）。</summary>
internal readonly struct BakedIcon
{
    internal readonly Sprite Sprite;

    /// <summary>
    /// 位图高度除以图形本体高度。想让本体在屏幕上正好 N 像素高，Image 的高度就设成 <c>N * PadRatio</c>
    /// （位图四周的描边 / 辉光留白会按同样比例溢出到本体之外）。
    /// </summary>
    internal readonly float PadRatio;

    internal BakedIcon(Sprite sprite, float padRatio)
    {
        Sprite = sprite;
        PadRatio = padRatio;
    }
}

/// <summary>
/// 把 <see cref="IconShapes"/> 画出来的纯位图变成 Unity 的 Texture2D / Sprite，并按「尺寸桶」缓存。
///
/// 烘焙尺寸 = 屏幕上实际显示像素的 <b>2 倍</b>（再乘 <c>UI 缩放</c>）：2 倍超采样 + mipmap + 三线性，
/// 缩小显示时边缘是干净的面积平均而不是双线性漏采样（细描边会闪烁）。
/// 尺寸被量化到 8 的倍数，所以 UI 缩放 / 图标大小调一点点不会每次都重烘。
/// </summary>
internal static class ProceduralIcons
{
    // 缓存不收缩：持有精灵的图层（命中标记 / 骷髅视图 / 横幅）各自缓存着引用，清缓存会让它们拿到被销毁的精灵。
    // 只有在 ConfigurationManager 里拖「图标大小 / 描边宽度」这类滑条时才会产生新变体，尺寸已量化到 8 的倍数、
    // 描边量化到 0.004，所以变体数是几十个而不是几百个。
    private static readonly Dictionary<string, BakedIcon?> Cache = new Dictionary<string, BakedIcon?>();
    private static Sprite? _tagBox;
    private static bool _tagBoxFailed;

    /// <summary>
    /// 取缓存。精灵可能被引擎销毁（场景切换 / <c>Resources.UnloadUnusedAssets</c>）—— 这时 C# 引用还在、
    /// 但 <c>sprite == null</c> 为真（Unity 假 null，和音效 clip 同一个坑），必须丢掉重烘，不能把死精灵递出去。
    /// 烘焙失败（<c>null</c>）也缓存，别每帧重试。
    /// </summary>
    private static bool TryGetCached(string key, out BakedIcon? icon)
    {
        if (Cache.TryGetValue(key, out icon))
        {
            if (!icon.HasValue || icon.Value.Sprite != null) return true;
            Cache.Remove(key);
        }
        icon = null;
        return false;
    }

    private static float UiScale() => Mathf.Clamp(Plugin.Cfg.UiScale.Value, 0.25f, 4f);

    private static int Quantize(float px, int min, int max)
        => Mathf.Clamp(Mathf.RoundToInt(px / 8f) * 8, min, max);

    // ------------------------------------------------------------------ 骷髅

    /// <param name="displayPx">骷髅本体在画布上的高度（画布单位）。</param>
    internal static BakedIcon? Skull(bool headshot, float displayPx)
    {
        float screenPx = Mathf.Max(8f, displayPx) * UiScale();
        int bakeH = Quantize(screenPx * 2f, 48, 256);
        string key = $"skull:{(headshot ? 1 : 0)}:{bakeH}";
        if (TryGetCached(key, out var cached)) return cached;

        // 描边按「屏幕像素」定：44px 的骷髅 ≈ 2.2px，再小也保底 1.2px
        float outlineScreen = Mathf.Max(1.2f, screenPx * 0.05f);
        float outlineBake = outlineScreen * (bakeH / screenPx);

        var bmp = IconShapes.Skull(bakeH, headshot, outlineBake);
        int pad = IconShapes.SkullPad(outlineBake);
        float padRatio = (bakeH + pad * 2f) / bakeH;

        BakedIcon? result = Build(bmp, padRatio, mipmaps: true);
        Cache[key] = result;
        return result;
    }

    // ------------------------------------------------------------------ 多杀徽记

    /// <param name="skullDisplayPx">徽记里主骷髅的显示高度（画布单位）。</param>
    internal static BakedIcon? Emblem(int killCount, float skullDisplayPx)
    {
        float screenPx = Mathf.Max(8f, skullDisplayPx) * UiScale();
        int bakeH = Quantize(screenPx * 2f, 48, 192);
        int count = Mathf.Clamp(killCount, 1, 5);
        string key = $"emblem:{count}:{bakeH}";
        if (TryGetCached(key, out var cached)) return cached;

        var bmp = IconShapes.Emblem(count, bakeH);
        BakedIcon? result = Build(bmp, bmp.Height / (float)bakeH, mipmaps: true);
        Cache[key] = result;
        return result;
    }

    // ------------------------------------------------------------------ 命中标记

    /// <param name="markerDisplayPx">标记整张图的显示边长（画布单位）。</param>
    /// <param name="outlineDisplayPx">深色描边的显示宽度（画布单位）。</param>
    /// <param name="bold">粗线版（击杀标记）。</param>
    internal static BakedIcon? HitMarker(float markerDisplayPx, float outlineDisplayPx, bool bold = false)
    {
        float markerScreen = Mathf.Max(16f, markerDisplayPx) * UiScale();
        int size = Quantize(markerScreen * 2f, 96, 256);

        // 描边宽度换成「半边长 = 1」的局部单位，量化到 0.004（≈ 0.14px，肉眼分不出）做缓存键
        float local = Mathf.Max(0f, outlineDisplayPx) / (Mathf.Max(16f, markerDisplayPx) * 0.5f);
        int localKey = Mathf.RoundToInt(Mathf.Clamp(local, 0f, 0.2f) * 250f);
        string key = $"hit:{size}:{localKey}:{(bold ? 1 : 0)}";
        if (TryGetCached(key, out var cached)) return cached;

        var bmp = IconShapes.HitMarker(size, localKey / 250f, bold);
        BakedIcon? result = Build(bmp, 1f, mipmaps: true);
        Cache[key] = result;
        return result;
    }

    // ------------------------------------------------------------------ 标签框

    /// <summary>标签框九宫格精灵（描边 2 像素）。全局只有一张，不随尺寸变化。</summary>
    internal static Sprite? TagBox()
    {
        if (_tagBox != null) return _tagBox;
        if (_tagBoxFailed) return null;   // 烘焙失败只试一次：每帧都有人来要，失败了还重试就是每帧一条 LogError

        const int border = 2;
        try
        {
            var bmp = IconShapes.TagBox(border);
            var tex = ToTexture(bmp, mipmaps: false);
            tex.filterMode = FilterMode.Bilinear;
            var sprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, bmp.Width, bmp.Height),
                new Vector2(0.5f, 0.5f),
                100f,
                0u,
                SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            _tagBox = sprite;
            return sprite;
        }
        catch (Exception ex)
        {
            _tagBoxFailed = true;
            Plugin.Logger.LogError($"[icons] 生成标签框精灵失败（之后不再重试）: {ex}");
            return null;
        }
    }

    // ------------------------------------------------------------------ 公共

    private static BakedIcon? Build(IconBitmap bmp, float padRatio, bool mipmaps)
    {
        try
        {
            var tex = ToTexture(bmp, mipmaps);
            var sprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, bmp.Width, bmp.Height),
                new Vector2(0.5f, 0.5f),
                100f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return new BakedIcon(sprite, padRatio);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[icons] 烘焙程序化图标失败: {ex}");
            return null;
        }
    }

    private static Texture2D ToTexture(IconBitmap bmp, bool mipmaps)
    {
        var tex = new Texture2D(bmp.Width, bmp.Height, TextureFormat.RGBA32, mipmaps);
        var pixels = new Color32[bmp.Width * bmp.Height];
        for (int i = 0; i < pixels.Length; i++)
        {
            int k = i * 4;
            pixels[i] = new Color32(bmp.Rgba[k], bmp.Rgba[k + 1], bmp.Rgba[k + 2], bmp.Rgba[k + 3]);
        }
        tex.SetPixels32(pixels);
        tex.Apply(mipmaps, false);   // 保持可读：辉光烘焙要从 alpha 取样
        tex.hideFlags = HideFlags.HideAndDontSave;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = mipmaps ? FilterMode.Trilinear : FilterMode.Bilinear;
        return tex;
    }
}
