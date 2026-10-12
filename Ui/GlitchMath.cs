using System;

namespace AyaHitmarker.Ui;

/// <summary>
/// 战地 6 式「故障（glitch）」动效的曲线。**纯数学，不引用 UnityEngine**，所以能被
/// <c>tests/AyaHitmarker.LogicTests</c> 离线验证。
///
/// 这些参数来自对两段 BF6 实录（30fps）的逐帧拆解：
/// <list type="bullet">
///   <item><b>入场</b>：元素先以一块实心色块闪出（约 0.1s），再在 ~0.12s 内「显影」成真正的图标 / 文字，
///     显影窗口里内容会随机掉几帧；分数数字不是淡入，而是快速滚到终值。</item>
///   <item><b>退场</b>：不是平滑淡出，而是<b>闪烁熄灭</b>——先整体灭掉一帧，再以逐渐降低的亮度闪几下，
///     骷髅 + 名字先走，标签随后，分数最后<b>硬切</b>。</item>
/// </list>
/// 闪烁按 <see cref="FlickerHz"/> 量化：视频是 30fps，如果按 120fps 逐帧随机，看上去就是噪点而不是故障。
/// 随机用整数哈希（不依赖 <c>Random</c>），同一时间点永远得到同一结果，截图可复现。
/// </summary>
internal static class GlitchMath
{
    /// <summary>闪烁/故障的量化频率。</summary>
    internal const float FlickerHz = 30f;

    /// <summary>确定性整数哈希 → [0,1)。同一 (a,b) 永远同一个值。</summary>
    internal static float Hash01(int a, int b)
    {
        unchecked
        {
            uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return (h & 0xFFFFFFu) / 16777216f;
        }
    }

    /// <summary>当前时刻落在第几个 30Hz 桶里。</summary>
    internal static int Bucket(float age) => age <= 0f ? 0 : (int)(age * FlickerHz);

    // ------------------------------------------------------------------ 入场

    /// <summary>入场显影参数。</summary>
    internal readonly struct RevealTiming
    {
        /// <summary>实心色块全亮的时长。</summary>
        internal readonly float BlockHold;

        /// <summary>色块褪去 / 内容显影的时长。</summary>
        internal readonly float Settle;

        /// <summary>色块的峰值不透明度（BF6 的色块不是 100% 实心，约 0.9）。</summary>
        internal readonly float BlockPeak;

        internal RevealTiming(float blockHold, float settle, float blockPeak)
        {
            BlockHold = blockHold;
            Settle = settle;
            BlockPeak = blockPeak;
        }

        internal float Total => BlockHold + Settle;

        /// <summary>骷髅 / 名字 / 分数这类主体元素。</summary>
        internal static readonly RevealTiming Standard = new RevealTiming(0.10f, 0.12f, 0.92f);

        /// <summary>标签框：BF6 里它是最先出现的，几乎没有色块阶段。</summary>
        internal static readonly RevealTiming Quick = new RevealTiming(0.04f, 0.08f, 0.85f);
    }

    /// <summary>
    /// 入场显影。<paramref name="age"/> 是元素自己的年龄（已扣掉错峰延迟）；
    /// <paramref name="content"/> 是内容的不透明度，<paramref name="block"/> 是实心色块的不透明度。
    /// </summary>
    internal static void Reveal(float age, int seed, in RevealTiming t, bool glitch, out float content, out float block)
    {
        if (age <= 0f)
        {
            content = 0f;
            block = 0f;
            return;
        }

        if (!glitch)
        {
            // 无故障模式：不要色块，内容按三次缓出淡入
            content = UiAnim.OutCubic(UiAnim.Clamp01(age / Math.Max(0.0001f, t.Total)));
            block = 0f;
            return;
        }

        if (age < t.BlockHold)
        {
            content = 0f;
            block = t.BlockPeak;
            return;
        }

        float k = UiAnim.Clamp01((age - t.BlockHold) / Math.Max(0.0001f, t.Settle));
        block = t.BlockPeak * (1f - k);
        content = k;

        // 显影窗口里内容随机掉几帧（越接近完成，掉得越少，最终一定稳定在 1）
        if (k < 1f && Hash01(Bucket(age), seed) < 0.35f * (1f - k))
        {
            content *= 0.15f;
        }
    }

    /// <summary>
    /// 数字滚动：从 <paramref name="from"/> 滚到 <paramref name="to"/>。
    /// 前段带噪声（像 BF6 里 14 → 72 → 100 那样乱跳），噪声随进度收敛，<paramref name="duration"/> 之后恒等于终值。
    /// </summary>
    internal static int CountValue(float age, float duration, int from, int to, int seed, bool glitch)
    {
        if (age >= duration || duration <= 0f) return to;
        if (age <= 0f) return from;

        float k = UiAnim.OutQuad(UiAnim.Clamp01(age / duration));
        float v = from + (to - from) * k;

        if (glitch)
        {
            // 噪声幅度 = 总变化量的 25%，随 (1-k) 收敛；按 30Hz 量化
            float span = Math.Abs(to - from);
            float noise = (Hash01(Bucket(age), seed) - 0.5f) * 2f * span * 0.25f * (1f - k);
            v += noise;
        }

        float lo = Math.Min(from, to);
        float hi = Math.Max(from, to);
        v = Math.Clamp(v, lo, hi);
        return (int)MathF.Round(v);
    }

    // ------------------------------------------------------------------ 退场

    /// <summary>
    /// 闪烁熄灭。返回 0..1 的不透明度。
    /// <paramref name="age"/> 是自退场开始起的时间，<paramref name="duration"/> 之后恒为 0（硬切）。
    /// </summary>
    internal static float FlickerOut(float age, float duration, int seed, bool glitch)
    {
        if (age <= 0f) return 1f;
        if (duration <= 0f || age >= duration) return 0f;

        float k = age / duration;

        if (!glitch)
        {
            // 无故障模式：平滑的二次淡出
            return 1f - k * k;
        }

        // 包络：从 1 平滑掉到 0
        float envelope = 1f - 0.5f * k - 0.5f * k * k;

        int bucket = Bucket(age);

        // BF6 的退场是「先整体灭一帧，再回到约 70% 的亮度」——第一个桶一定熄灭
        if (bucket == 0) return 0.04f;

        // 越往后越容易掉线
        float pDrop = 0.12f + 0.5f * k;
        float r = Hash01(bucket, seed);
        if (r < pDrop) return envelope * 0.08f;

        float amp = 0.55f + 0.45f * ((r - pDrop) / Math.Max(0.0001f, 1f - pDrop));
        return envelope * amp;
    }

    /// <summary>
    /// 分组错峰：把一次退场拆成「第一梯队 → 第二梯队 → 分数硬切」。
    /// 返回 0..1 的不透明度。<paramref name="group"/>：0 = 骷髅/名字，1 = 标签/横幅/详情/徽章，2 = 分数。
    /// 比例取自 BF6 实录：第一梯队占前 46%，第二梯队 40%~100%，分数撑到最后一刻再硬切。
    /// </summary>
    internal static float ExitAlpha(int group, float age, float duration, int seed, bool glitch)
    {
        if (age <= 0f) return 1f;
        if (duration <= 0f || age >= duration) return 0f;

        switch (group)
        {
            case 0:
                return FlickerOut(age, duration * 0.46f, seed, glitch);

            case 1:
            {
                float start = duration * 0.40f;
                if (age <= start) return 1f;
                return FlickerOut(age - start, duration - start, seed + 17, glitch);
            }

            default:
                // 分数：无故障模式下仍然跟着整体淡出；故障模式下一直亮到最后硬切
                return glitch ? 1f : 1f - (age / duration) * (age / duration);
        }
    }
}
