using System;
using AyaHitmarker.Ui;

namespace AyaHitmarker.LogicTests;

/// <summary>
/// 故障动效曲线的可验证性质：哈希确定性、入场显影的单调性与终态、数字滚动的边界、闪烁熄灭的起止与硬切。
/// 看起来「像不像 BF6」要进游戏看；这里只挡「终态不对 / 超出 0..1 / 同一时刻两次结果不同」这类硬错误。
/// </summary>
internal static class GlitchFacts
{
    internal static void Run()
    {
        Assert.Suite("Glitch: 哈希");
        {
            Assert.Equal(GlitchMath.Hash01(7, 3), GlitchMath.Hash01(7, 3), "同一输入永远同一输出（截图可复现）");
            Assert.NotEqual(GlitchMath.Hash01(7, 3), GlitchMath.Hash01(8, 3), "不同桶得到不同值");

            double sum = 0;
            bool inRange = true;
            const int n = 20000;
            for (int i = 0; i < n; i++)
            {
                float h = GlitchMath.Hash01(i, 12345);
                if (h < 0f || h >= 1f) inRange = false;
                sum += h;
            }
            Assert.True(inRange, "哈希值全部落在 [0,1)");
            Assert.Near(0.5, sum / n, 0.02, "哈希分布均值接近 0.5");
        }

        Assert.Suite("Glitch: 入场显影");
        {
            var t = GlitchMath.RevealTiming.Standard;

            GlitchMath.Reveal(0f, 1, t, true, out float c0, out float b0);
            Assert.Near(0.0, c0, 1e-6, "age=0：内容不可见");
            Assert.Near(0.0, b0, 1e-6, "age=0：色块也还没出现（还在错峰延迟里）");

            GlitchMath.Reveal(-0.2f, 1, t, true, out float cn, out float bn);
            Assert.True(cn == 0f && bn == 0f, "age<0：什么都没有");

            GlitchMath.Reveal(t.BlockHold * 0.5f, 1, t, true, out float cm, out float bm);
            Assert.Near(0.0, cm, 1e-6, "色块全亮阶段内容仍不可见");
            Assert.Near(t.BlockPeak, bm, 1e-6, "色块全亮阶段不透明度 = 峰值");

            GlitchMath.Reveal(t.Total + 0.5f, 1, t, true, out float cf, out float bf);
            Assert.Near(1.0, cf, 1e-6, "显影结束：内容完全可见");
            Assert.Near(0.0, bf, 1e-6, "显影结束：色块完全消失");

            // 色块在显影窗口里单调不增；任何时刻值都在 [0,1]
            bool blockMonotonic = true, bounded = true;
            float prev = 2f;
            for (float a = t.BlockHold; a <= t.Total + 0.001f; a += 0.001f)
            {
                GlitchMath.Reveal(a, 5, t, true, out float c, out float b);
                if (b > prev + 1e-5f) blockMonotonic = false;
                prev = b;
                if (c < 0f || c > 1f || b < 0f || b > 1f) bounded = false;
            }
            Assert.True(blockMonotonic, "显影窗口里色块不透明度单调下降");
            Assert.True(bounded, "内容 / 色块不透明度始终在 [0,1]");

            // 终点之后不再抖：连续采样内容恒为 1
            bool stable = true;
            for (float a = t.Total; a < t.Total + 0.5f; a += 0.002f)
            {
                GlitchMath.Reveal(a, 9, t, true, out float c, out _);
                if (c != 1f) stable = false;
            }
            Assert.True(stable, "显影结束后内容恒为 1，不会再闪");

            // 无故障模式：永远没有色块，内容单调淡入
            bool noBlock = true, mono = true;
            prev = -1f;
            for (float a = 0f; a <= 0.5f; a += 0.002f)
            {
                GlitchMath.Reveal(a, 3, t, false, out float c, out float b);
                if (b != 0f) noBlock = false;
                if (c < prev - 1e-6f) mono = false;
                prev = c;
            }
            Assert.True(noBlock, "无故障模式没有色块");
            Assert.True(mono, "无故障模式内容单调淡入");
        }

        Assert.Suite("Glitch: 数字滚动");
        {
            Assert.Equal(100, GlitchMath.CountValue(0f, 0.25f, 100, 225, 1, true), "age=0 显示起点");
            Assert.Equal(225, GlitchMath.CountValue(0.25f, 0.25f, 100, 225, 1, true), "滚完显示终值");
            Assert.Equal(225, GlitchMath.CountValue(9f, 0.25f, 100, 225, 1, true), "滚完之后恒为终值");
            Assert.Equal(300, GlitchMath.CountValue(0.1f, 0f, 0, 300, 1, true), "时长为 0 = 直接显示终值");

            bool inRangeUp = true, inRangeDown = true;
            for (float a = 0f; a < 0.25f; a += 0.003f)
            {
                int up = GlitchMath.CountValue(a, 0.25f, 0, 100, 4, true);
                if (up < 0 || up > 100) inRangeUp = false;
                int down = GlitchMath.CountValue(a, 0.25f, 100, 40, 4, true);
                if (down < 40 || down > 100) inRangeDown = false;
            }
            Assert.True(inRangeUp, "向上滚动时数值始终在 [起点, 终点] 内（噪声不会冲出范围）");
            Assert.True(inRangeDown, "向下滚动时数值同样被钳在范围内");

            Assert.Equal(GlitchMath.CountValue(0.08f, 0.25f, 0, 100, 4, true),
                GlitchMath.CountValue(0.08f, 0.25f, 0, 100, 4, true), "同一时刻两次结果一致");

            // 无故障模式：纯缓出，单调不减
            bool mono = true;
            int prev = -1;
            for (float a = 0f; a <= 0.25f; a += 0.002f)
            {
                int v = GlitchMath.CountValue(a, 0.25f, 0, 100, 4, false);
                if (v < prev) mono = false;
                prev = v;
            }
            Assert.True(mono, "无故障模式下数字滚动单调不减");
        }

        Assert.Suite("Glitch: 闪烁熄灭");
        {
            Assert.Near(1.0, GlitchMath.FlickerOut(0f, 0.5f, 1, true), 1e-6, "退场开始前保持全亮");
            Assert.Near(1.0, GlitchMath.FlickerOut(-1f, 0.5f, 1, true), 1e-6, "还没到退场时刻也是全亮");
            Assert.Near(0.0, GlitchMath.FlickerOut(0.5f, 0.5f, 1, true), 1e-6, "退场结束恒为 0（硬切）");
            Assert.Near(0.0, GlitchMath.FlickerOut(3f, 0.5f, 1, true), 1e-6, "退场结束之后恒为 0");

            // BF6：先整体灭一帧
            Assert.True(GlitchMath.FlickerOut(0.01f, 0.5f, 1, true) < 0.1f, "第一个 30Hz 桶一定熄灭（先灭一帧）");

            bool bounded = true;
            for (int seed = 0; seed < 8; seed++)
            {
                for (float a = 0.001f; a < 0.5f; a += 0.001f)
                {
                    float v = GlitchMath.FlickerOut(a, 0.5f, seed, true);
                    if (v < 0f || v > 1f) bounded = false;
                }
            }
            Assert.True(bounded, "闪烁熄灭的不透明度始终在 [0,1]");

            // 确实在闪：同一段里既有暗帧也有亮帧
            int dark = 0, bright = 0;
            for (float a = 0.05f; a < 0.4f; a += 1f / 30f)
            {
                float v = GlitchMath.FlickerOut(a, 0.5f, 2, true);
                if (v < 0.15f) dark++;
                if (v > 0.35f) bright++;
            }
            Assert.True(dark > 0 && bright > 0, "闪烁段里既有暗帧也有亮帧");

            // 无故障模式：平滑单调
            bool smooth = true;
            float prev = 2f;
            for (float a = 0f; a < 0.5f; a += 0.002f)
            {
                float v = GlitchMath.FlickerOut(a, 0.5f, 1, false);
                if (v > prev + 1e-6f) smooth = false;
                prev = v;
            }
            Assert.True(smooth, "无故障模式下淡出单调下降");
        }

        Assert.Suite("Glitch: 退场分组错峰");
        {
            const float dur = 0.5f;

            Assert.Near(1.0, GlitchMath.ExitAlpha(0, 0f, dur, 1, true), 1e-6, "退场开始瞬间各组都还是全亮");
            Assert.Near(1.0, GlitchMath.ExitAlpha(2, 0f, dur, 1, true), 1e-6, "分数组同样");

            // 分数组：故障模式下一直全亮到最后一刻，然后硬切
            Assert.Near(1.0, GlitchMath.ExitAlpha(2, dur * 0.99f, dur, 1, true), 1e-6, "分数撑到最后一刻（故障模式）");
            Assert.Near(0.0, GlitchMath.ExitAlpha(2, dur, dur, 1, true), 1e-6, "分数在退场结束时硬切为 0");

            // 第二梯队在 40% 之前保持全亮；第一梯队在 46% 处已经结束
            Assert.Near(1.0, GlitchMath.ExitAlpha(1, dur * 0.39f, dur, 1, true), 1e-6, "标签等第二梯队在 40% 前保持全亮");
            Assert.Near(0.0, GlitchMath.ExitAlpha(0, dur * 0.47f, dur, 1, true), 1e-6, "骷髅 / 名字第一梯队在 46% 处已经灭完");

            bool ordered = true;
            for (float a = dur * 0.05f; a < dur * 0.40f; a += 0.003f)
            {
                // 第一梯队开始闪的时候，第二梯队仍然全亮
                if (GlitchMath.ExitAlpha(1, a, dur, 1, true) != 1f) ordered = false;
            }
            Assert.True(ordered, "第二梯队在第一梯队闪烁期间保持全亮（错峰）");

            // 所有组在 duration 之后全部为 0
            Assert.True(GlitchMath.ExitAlpha(0, dur + 1f, dur, 1, true) == 0f
                        && GlitchMath.ExitAlpha(1, dur + 1f, dur, 1, true) == 0f
                        && GlitchMath.ExitAlpha(2, dur + 1f, dur, 1, true) == 0f, "退场结束后三组全部为 0");
        }

        Assert.Suite("UiAnim: 纯数学缓动");
        {
            Assert.Near(0.0, UiAnim.OutCubic(0f), 1e-6, "OutCubic(0)=0");
            Assert.Near(1.0, UiAnim.OutCubic(1f), 1e-6, "OutCubic(1)=1");
            Assert.Near(0.75, UiAnim.OutQuad(0.5f), 1e-6, "OutQuad(0.5)=0.75");
            Assert.True(UiAnim.OutBack(0.7f) > 1f, "OutBack 会冲过终点（弹一下的手感）");
            Assert.Near(1.0, UiAnim.OutBack(1f), 1e-5, "OutBack 最终落在 1");
            Assert.Near(0.0, UiAnim.Clamp01(-3f), 1e-9, "Clamp01 下限");
            Assert.Near(1.0, UiAnim.Clamp01(3f), 1e-9, "Clamp01 上限");
        }
    }
}
