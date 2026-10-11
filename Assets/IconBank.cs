using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AyaHitmarker.Assets;

/// <summary>
/// 图标资源库。加载 <c>BepInEx/plugins/AyaHitmarker/Icons/</c> 下的 PNG：
/// <list type="bullet">
///   <item><c>hit.png</c> —— 命中提示图标</item>
///   <item><c>skull.png</c> —— 击杀骷髅</item>
///   <item><c>skull_head.png</c> —— 爆头击杀骷髅</item>
/// </list>
///
/// 另外两样不落盘、运行时烘：
/// <list type="bullet">
///   <item>环形特效：按 <c>厚度/半径</c> 比例分桶缓存的小纹理（旧项目的 GL 三角带在 uGUI 下没有对应物）</item>
///   <item>辉光：把旧项目「8+8+1 次偏移叠加」的数学在 CPU 上烘成一张纹理</item>
/// </list>
/// </summary>
internal static class IconBank
{
    internal static Sprite? Hit { get; private set; }
    internal static Sprite? Skull { get; private set; }
    internal static Sprite? SkullHead { get; private set; }

    internal static bool Loaded { get; private set; }

    // ---- 环形特效纹理缓存：key = 量化后的 (thickness / radius) ----
    private const int RingTextureSize = 128;
    private const int RingBuckets = 64;
    private static readonly Dictionary<int, Sprite?> _ringCache = new Dictionary<int, Sprite?>();

    // ---- 辉光缓存：key = 图标名 + 量化后的 Size ----
    private static readonly Dictionary<string, Sprite?> _glowCache = new Dictionary<string, Sprite?>();

    internal static void Load()
    {
        if (Loaded) return;
        Loaded = true;

        string dir = Plugin.IconsDirectory;
        Hit = LoadSprite(Path.Combine(dir, "hit.png"), "hit.png");
        Skull = LoadSprite(Path.Combine(dir, "skull.png"), "skull.png");
        SkullHead = LoadSprite(Path.Combine(dir, "skull_head.png"), "skull_head.png");

        // 爆头骷髅缺失就退回普通骷髅（旧项目也是这个降级策略）
        if (SkullHead == null && Skull != null)
        {
            SkullHead = Skull;
            Plugin.Logger.LogInfo("[icons] skull_head.png 缺失，爆头沿用 skull.png");
        }

        Plugin.Logger.LogInfo($"[icons] loaded: hit={(Hit != null)} skull={(Skull != null)} skull_head={(SkullHead != null)}  (dir={dir})");

        // 军衔图标在启动时就探一次。历史上它是在第一次击杀 PMC 时才懒加载，
        // 结果日志里什么都不打，无法区分「没装图标」和「没碰到 PMC」。
        EnsureRankIcons();
    }

    internal static void Invalidate()
    {
        _ringCache.Clear();
        _glowCache.Clear();
    }
    // ------------------------------------------------------------------ PMC 军衔图标（可选）
    //
    // 旧 Mono 项目的 SimpleHitMarker/RankIcons/Rank{tier}.png 在实际仓库里并不存在，
    // 所以从来没真正显示过。这里把加载器补齐：把文件丢到 Icons/RankIcons/ 就能生效，
    // 没有就静默跳过（阵营行会自动给文本腾出空间）。

    private static readonly int[] RankTierStarts =
        { 1, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75 };

    private const int MaxPmcRankIconLevel = 79;

    private static readonly Dictionary<int, Sprite?> _rankIcons = new Dictionary<int, Sprite?>();
    private static bool _rankAttempted;

    /// <summary>军衔图标的加载情况（诊断日志用）。</summary>
    internal static string RankIconStatus { get; private set; } = "未探测";

    private static void EnsureRankIcons()
    {
        if (_rankAttempted) return;
        _rankAttempted = true;

        string baseDir = Plugin.IconsDirectory;
        string rankDir = Path.Combine(baseDir, "RankIcons");
        int found = 0;

        for (int i = 0; i < RankTierStarts.Length; i++)
        {
            int tier = RankTierStarts[i];
            string file = $"Rank{tier}.png";

            var sprite = LoadSprite(Path.Combine(rankDir, file), file, quiet: true)
                         ?? LoadSprite(Path.Combine(baseDir, file), file, quiet: true);
            if (sprite != null)
            {
                _rankIcons[tier] = sprite;
                found++;
            }
        }

        // 只有「一个都没找到」才需要告警 —— 找到一部分是正常的（用户可以只放几档）。
        if (found > 0)
        {
            RankIconStatus = $"{found}/{RankTierStarts.Length} 档";
            Plugin.Logger.LogInfo($"[icons] PMC 军衔图标已加载 {found}/{RankTierStarts.Length}（目录 {rankDir}）");
        }
        else
        {
            RankIconStatus = "缺失";
            Plugin.Logger.LogWarning(
                $"[icons] 没有找到任何 PMC 军衔图标（应为 {rankDir}\\Rank{{1,5,...,75}}.png）——" +
                "击杀播报会只显示「[等级] 阵营」文字，不显示军衔徽章。");
        }
    }

    /// <summary>
    /// 只有 PMC 才有军衔图标；没有相应文件就返回 <c>null</c>。
    /// 具体档位缺文件时会**向下取最近的可用档位**（宁可显示低一档的徽章，也不要空着），
    /// 一档都没有才返回 <c>null</c>。
    /// </summary>
    internal static Sprite? GetRankIcon(int level)
    {
        EnsureRankIcons();
        if (_rankIcons.Count == 0 || level <= 0) return null;

        int tier = TierForLevel(level);

        for (int i = 0; i < RankTierStarts.Length; i++)
        {
            int candidate = tier - i * 5;
            if (candidate < 1) break;
            if (_rankIcons.TryGetValue(candidate, out var sprite) && sprite != null) return sprite;
        }

        // 低档位全缺但高档位有文件（很反常）——退而求其次取最高的一个
        for (int i = RankTierStarts.Length - 1; i >= 0; i--)
        {
            if (_rankIcons.TryGetValue(RankTierStarts[i], out var sprite) && sprite != null) return sprite;
        }

        return null;
    }

    /// <summary>等级 → 档位起点（1 &lt;= level &lt;= 4 归到 1，其余每 5 级一档，上限 75）。</summary>
    internal static int TierForLevel(int level)
    {
        if (level <= 4) return 1;
        int clamped = Mathf.Clamp(level, 5, MaxPmcRankIconLevel);
        int steps = (clamped - 5) / 5;
        return Mathf.Clamp(5 + steps * 5, 5, 75);
    }

    internal static Sprite? SkullFor(bool headshot) => headshot ? (SkullHead ?? Skull) : Skull;

    private static Sprite? LoadSprite(string path, string label, bool quiet = false)
    {
        try
        {
            if (!File.Exists(path))
            {
                if (!quiet) Plugin.Logger.LogWarning($"[icons] 找不到 {label}: {path}");
                return null;
            }

            byte[] bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, bytes))
            {
                UnityEngine.Object.Destroy(tex);
                Plugin.Logger.LogError($"[icons] {label} 不是有效图片");
                return null;
            }

            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[icons] 加载 {label} 失败: {ex}");
            return null;
        }
    }

    // ------------------------------------------------------------------ 环形特效

    /// <summary>
    /// 取一张「外边缘正好在半径 1.0、内边缘在 <c>1 - thickness/radius</c>」的环形纹理。
    /// 调用方把 Image 的 sizeDelta 设成 <c>2 * (radius + thickness/2)</c> 即可，纹理会被等比缩放。
    /// 用 <c>thickness/radius</c> 分桶缓存：一段 0.3 秒的动画只会命中 ~16 个桶。
    /// </summary>
    internal static Sprite? GetRing(float thickness, float radius)
    {
        if (radius <= 0.01f || thickness <= 0.01f) return null;

        float ratio = Mathf.Clamp(thickness / radius, 0.01f, 0.95f);
        int bucket = Mathf.Clamp(Mathf.RoundToInt(ratio * RingBuckets), 1, RingBuckets);

        if (_ringCache.TryGetValue(bucket, out var cached)) return cached;

        float band = bucket / (float)RingBuckets;      // 相对外半径的带宽
        var sprite = BuildRingSprite(band);
        _ringCache[bucket] = sprite;
        return sprite;
    }

    private static Sprite? BuildRingSprite(float band)
    {
        try
        {
            int s = RingTextureSize;
            int half = s / 2;
            float rOuter = 1f;
            float rInner = Mathf.Clamp(1f - band, 0.02f, 0.99f);
            float aa = 1.2f / half;                    // 1.2 像素的软边

            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var pixels = new Color32[s * s];

            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float dx = (x - half + 0.5f) / (float)half;
                    float dy = (y - half + 0.5f) / (float)half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);

                    // 外沿软切 + 内沿软切
                    float a = Mathf.Clamp01((rOuter - d) / aa) * Mathf.Clamp01((d - rInner) / aa);
                    pixels[y * s + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var sprite = Sprite.Create(tex, new Rect(0f, 0f, s, s), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[icons] 生成环形纹理失败: {ex}");
            return null;
        }
    }

    // ------------------------------------------------------------------ 辉光

    /// <summary>
    /// 把 <paramref name="icon"/> 的 alpha 按旧项目的辉光数学烘成一张带外边距的纹理：
    /// <code>
    ///   glow = 0.16 * Σ(8 个方向偏移 Size)     ← 外环
    ///        + 0.11 * Σ(8 个方向偏移 Size*0.55) ← 内环
    ///        + 0.09 * 原图                       ← 中心
    /// </code>
    /// 旧版是 17 次叠加绘制（additive）；烘成一张后只需 1 个 Image，
    /// 视觉等价（都是同一背景上的叠加），但少 16 次 draw。
    /// <c>Intensity</c> 与颜色由 Image 的 color 承担，不需要重烘。
    /// </summary>
    internal static Sprite? GetGlow(Sprite? icon, int spread)
    {
        if (icon == null || spread <= 0) return null;

        int bucket = Mathf.Clamp(spread, 1, 64);
        string key = $"{icon.GetInstanceID()}:{bucket}";
        if (_glowCache.TryGetValue(key, out var cached)) return cached;

        var built = BuildGlowSprite(icon, bucket);
        _glowCache[key] = built;
        return built;
    }

    /// <summary>辉光纹理相对图标的膨胀比例（用于把 Image 尺寸换算回去）。</summary>
    internal static float GlowPadRatio(Sprite icon, int spread)
    {
        int w = icon.texture != null ? icon.texture.width : 1;
        if (w <= 0) w = 1;
        return (w + 2f * (spread + 1)) / w;
    }

    private static readonly Vector2[] GlowOffsets =
    {
        new Vector2(-1f, -1f), new Vector2(0f, -1f), new Vector2(1f, -1f),
        new Vector2(-1f, 0f), new Vector2(1f, 0f),
        new Vector2(-1f, 1f), new Vector2(0f, 1f), new Vector2(1f, 1f),
    };

    private static Sprite? BuildGlowSprite(Sprite icon, int spread)
    {
        try
        {
            var src = icon.texture;
            if (src == null) return null;

            int w = src.width, h = src.height;
            int pad = spread + 1;
            int tw = w + pad * 2, th = h + pad * 2;

            var srcPixels = src.GetPixels32();
            var acc = new float[tw * th];

            Accumulate(srcPixels, w, h, acc, tw, th, pad, 0f, 0f, 0.09f);

            float inner = Mathf.Max(0.5f, spread * 0.55f);
            for (int i = 0; i < GlowOffsets.Length; i++)
            {
                var o = GlowOffsets[i];
                Accumulate(srcPixels, w, h, acc, tw, th, pad, o.x * spread, o.y * spread, 0.16f);
                Accumulate(srcPixels, w, h, acc, tw, th, pad, o.x * inner, o.y * inner, 0.11f);
            }

            var outPixels = new Color32[tw * th];
            for (int i = 0; i < acc.Length; i++)
            {
                // 只烘 alpha；颜色交给 Image.color 着色
                outPixels[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(acc[i]) * 255f));
            }

            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false);
            tex.SetPixels32(outPixels);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var sprite = Sprite.Create(tex, new Rect(0f, 0f, tw, th), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[icons] 生成辉光纹理失败: {ex}");
            return null;
        }
    }

    private static void Accumulate(
        Color32[] src, int sw, int sh, float[] acc, int tw, int th,
        int pad, float ox, float oy, float weight)
    {
        int ix = Mathf.RoundToInt(ox);
        int iy = Mathf.RoundToInt(oy);

        for (int y = 0; y < sh; y++)
        {
            int ty = y + pad + iy;
            if (ty < 0 || ty >= th) continue;
            int srcRow = y * sw;
            int dstRow = ty * tw;

            for (int x = 0; x < sw; x++)
            {
                int tx = x + pad + ix;
                if (tx < 0 || tx >= tw) continue;
                acc[dstRow + tx] += src[srcRow + x].a / 255f * weight;
            }
        }
    }
}
