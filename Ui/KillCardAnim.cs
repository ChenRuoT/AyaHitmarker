namespace AyaHitmarker.Ui;

/// <summary>
/// 击杀卡片各视图共用的「本帧动画参数」。
///
/// 卡片拆成多个视图类之后（横幅 / 骷髅行 / 标签行 / 军衔徽章），它们都需要同一套
/// 故障显影 / 错峰退场的曲线和同一帧的时间；把 <c>now</c> / <c>dt</c> / 故障开关打成一个值类型传下去，
/// 既省参数，也保证所有元素严格同帧（否则闪烁梯队会错位）。
///
/// 纯逻辑（<see cref="GlitchMath"/>）的薄包装，不持有任何 Unity 对象。
/// </summary>
internal readonly struct KillCardAnim
{
    /// <summary>本帧的 UI 时钟（<see cref="UiClock.Now"/>）。</summary>
    internal readonly float Now;

    /// <summary>本帧的 UI 时钟增量（<see cref="UiClock.Delta"/>）。</summary>
    internal readonly float Dt;

    /// <summary>「故障特效」开关；关闭后退化成普通淡入淡出。</summary>
    internal readonly bool Glitch;

    internal KillCardAnim(float now, float dt, bool glitch)
    {
        Now = now;
        Dt = dt;
        Glitch = glitch;
    }

    /// <summary>只算入场显影（不含退场）。</summary>
    internal void Reveal(float born, float delay, int seed, in GlitchMath.RevealTiming timing,
        out float content, out float block)
        => GlitchMath.Reveal(Now - born - delay, seed, timing, Glitch, out content, out block);

    /// <summary>一个梯队当前的退场不透明度。同一个元素的各个部件必须共用它，闪烁才会同步。</summary>
    internal float Exit(int group, int seed, float exitAge, float exitDur)
        => GlitchMath.ExitAlpha(group, exitAge, exitDur, seed, Glitch);

    /// <summary>元素当前的「内容不透明度 / 色块不透明度」，已经乘上了它所在梯队的退场不透明度。</summary>
    internal void Fades(float born, float delay, int seed, in GlitchMath.RevealTiming timing,
        int group, float exitAge, float exitDur, out float content, out float block)
    {
        GlitchMath.Reveal(Now - born - delay, seed, timing, Glitch, out content, out block);
        float exit = GlitchMath.ExitAlpha(group, exitAge, exitDur, seed, Glitch);
        content *= exit;
        block *= exit;
    }
}
