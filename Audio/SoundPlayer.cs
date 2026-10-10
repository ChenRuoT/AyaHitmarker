using System;
using AyaHitmarker.Assets;
using UnityEngine;

namespace AyaHitmarker.Audio;

/// <summary>
/// 唯一的 AudioSource 宿主。
///
/// 两个与"听不到声音"直接相关的设置：
/// <list type="bullet">
///   <item><c>ignoreListenerPause = true</c> —— EFT 会在若干状态下暂停 AudioListener（例如读盘、
///     某些 UI 状态）。普通声源在暂停期间是**静音**的，插件音效不应该被这个状态连带。</item>
///   <item><c>bypassEffects / bypassListenerEffects / bypassReverbZones = true</c> ——
///     EFT 给 listener 挂了室内混响、耳鸣低通、耳机滤波等效果链；不绕过的话插件音效会被
///     那些效果染色甚至压得很低。</item>
/// </list>
///
/// GameObject 标记 <c>DontDestroyOnLoad</c> 并在每次播放前做存活检查，
/// 而不是像旧项目那样再挂一个 2 秒轮询去"恢复"它。
/// </summary>
internal sealed class SoundPlayer
{
    private GameObject? _go;
    private AudioSource? _source;
    private bool _envLogged;

    internal void Shutdown()
    {
        _source = null;
        if (_go != null)
        {
            try { UnityEngine.Object.Destroy(_go); } catch { /* ignore */ }
            _go = null;
        }
        _envLogged = false;
    }

    internal void PlayHit(bool headshot) => Play(headshot ? SoundKind.HitHeadshot : SoundKind.Hit);

    internal void PlayKill(bool headshot) => Play(headshot ? SoundKind.KillHeadshot : SoundKind.Kill);

    private void Play(SoundKind kind)
    {
        var cfg = Plugin.Cfg;

        // 每一条提前返回都必须说得出理由。
        // 之前 `clip == null` 那条是静默的，于是「加载日志说成功、播放却无声」查了很久 ——
        // 实际是 Unity 的假 null（clip 的原生对象被 handler 的 Dispose 一起销毁了）。
        if (!cfg.SoundEnabled.Value)
        {
            WarnOnce(kind, "总开关关闭");
            return;
        }

        bool isKill = kind == SoundKind.Kill || kind == SoundKind.KillHeadshot;
        if (isKill ? !cfg.SoundKillEnabled.Value : !cfg.SoundHitEnabled.Value)
        {
            WarnOnce(kind, isKill ? "击杀音效开关关闭" : "命中音效开关关闭");
            return;
        }

        try
        {
            // 还没加载完就跳过，绝不在这里触发加载（那会把 IO 拉进战斗热路径）
            var clip = AudioBank.Get(kind);
            if (clip == null)
            {
                WarnOnce(kind, "clip 不可用（未加载，或原生对象已销毁 → 见 AudioBank 关于 Dispose 的注释）");
                return;
            }

            var source = EnsureSource();
            if (source == null)
            {
                WarnOnce(kind, "AudioSource 创建失败");
                return;
            }

            float vol = cfg.SoundMasterVolume.Value * (kind switch
            {
                SoundKind.Hit => cfg.SoundHitVolume.Value,
                SoundKind.HitHeadshot => cfg.SoundHitHeadshotVolume.Value,
                SoundKind.Kill => cfg.SoundKillVolume.Value,
                _ => cfg.SoundKillHeadshotVolume.Value,
            });

            if (vol <= 0.0001f)
            {
                WarnOnce(kind, "音量算下来是 0");
                return;
            }

            // 数据没进内存时 PlayOneShot 会静默失败
            try
            {
                if (clip.loadState != AudioDataLoadState.Loaded)
                {
                    bool ok = clip.LoadAudioData();
                    Plugin.Logger.LogWarning($"[audio] {kind} clip.loadState={clip.loadState}，LoadAudioData() -> {ok}");
                }
            }
            catch { /* 拿不到状态就照常播放 */ }

            source.PlayOneShot(clip, Mathf.Clamp(vol, 0f, 1f));

            if (cfg.DebugEnabled.Value) LogPlayback(kind, clip, source, vol);
            _warned[(int)kind] = false;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[audio] 播放 {kind} 失败: {ex}");
        }
    }

    private static readonly bool[] _warned = new bool[4];

    /// <summary>同一种原因每种音效只告警一次，避免战斗里每枪刷屏。</summary>
    private static void WarnOnce(SoundKind kind, string reason)
    {
        int i = (int)kind;
        if (_warned[i]) return;
        _warned[i] = true;
        Plugin.Logger.LogWarning($"[audio] {kind} 未播放: {reason}（同类原因只提示一次）");
    }

    private AudioSource? EnsureSource()
    {
        // Unity 对已销毁对象的重载 == 会返回 true，所以这里能正确识别"原生对象已没了"
        if (_go != null && _source != null)
        {
            if (!_envLogged) LogEnvironment(_source);
            return _source;
        }

        _go = new GameObject("AyaHitmarker.AudioSource");
        UnityEngine.Object.DontDestroyOnLoad(_go);

        _source = _go.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.spatialBlend = 0f;            // 2D
        _source.volume = 1f;
        _source.priority = 0;                 // 最高优先级，不被音频语音数上限挤掉
        _source.ignoreListenerPause = true;
        _source.bypassEffects = true;
        _source.bypassListenerEffects = true;
        _source.bypassReverbZones = true;

        LogEnvironment(_source);
        return _source;
    }

    private void LogEnvironment(AudioSource source)
    {
        _envLogged = true;
        try
        {
            Plugin.Logger.LogInfo(
                $"[audio] AudioSource ready: enabled={source.enabled} volume={source.volume} " +
                $"spatialBlend={source.spatialBlend} mute={source.mute} " +
                $"| AudioListener.volume={AudioListener.volume} pause={AudioListener.pause}");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[audio] 读取 AudioListener 状态失败: {ex.Message}");
        }
    }

    private static void LogPlayback(SoundKind kind, AudioClip clip, AudioSource source, float vol)
    {
        try
        {
            string state;
            try { state = clip.loadState.ToString(); } catch { state = "?"; }

            Plugin.Logger.LogInfo(
                $"[audio] play {kind}: clip={clip.name} len={clip.length:0.###}s samples={clip.samples} " +
                $"ch={clip.channels} freq={clip.frequency} loadState={state} " +
                $"vol={vol:0.##} srcVol={source.volume} isPlaying={source.isPlaying}");

            // loadState 不是 Loaded 说明数据还没进内存，PlayOneShot 会静默失败
            if (!string.Equals(state, "Loaded", StringComparison.Ordinal))
            {
                bool ok = clip.LoadAudioData();
                Plugin.Logger.LogWarning($"[audio] {kind} 的 clip.loadState={state}，已尝试 LoadAudioData() -> {ok}");
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[audio] 播放诊断日志失败: {ex.Message}");
        }
    }
}
