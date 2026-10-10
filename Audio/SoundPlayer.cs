using System;
using AyaHitmarker.Assets;
using UnityEngine;

namespace AyaHitmarker.Audio;

/// <summary>
/// 唯一的 AudioSource 宿主。
///
/// <para>
/// **音频源配置刻意保持和旧 Mono 项目一致**（只有 <c>playOnAwake=false</c> / <c>spatialBlend=0</c> /
/// <c>volume=1</c>）。我曾额外加上 <c>ignoreListenerPause</c> 和
/// <c>bypassEffects / bypassListenerEffects / bypassReverbZones</c> 想去躲开游戏的音频链 ——
/// 结果反而更可能躲掉了出声的那一环：这个版本的 EFT 用 **Meta XR Audio** 做空间音频
/// （启动日志：<c>Applying Acoustic Propagation Settings: acoustic model = AcousticRayTracing</c>），
/// 而 Unity 文档明确写着 <c>bypassEffects</c> 会一并绕过 **AudioListener** 上的效果。
/// 现在这些都改成默认关闭的排障开关。
/// </para>
///
/// GameObject 标记 <c>DontDestroyOnLoad</c>，并在每次播放前做存活检查。
/// </summary>
internal sealed class SoundPlayer
{
    private GameObject? _go;
    private AudioSource? _source;
    private bool _envLogged;

    private static readonly bool[] _warned = new bool[4];

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

        // 每一条提前返回都必须说得出理由（上一版 `clip == null` 静默返回，排查成本极高）
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
                WarnOnce(kind, "clip 不可用（未加载，或原生对象已被销毁）");
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

            try
            {
                if (clip.loadState != AudioDataLoadState.Loaded)
                {
                    bool ok = clip.LoadAudioData();
                    Plugin.Logger.LogWarning($"[audio] {kind} clip.loadState={clip.loadState}，LoadAudioData() -> {ok}");
                }
            }
            catch { /* 拿不到状态就照常播放 */ }

            ApplySpatialMode(source);

            source.PlayOneShot(clip, Mathf.Clamp(vol, 0f, 1f));

            if (cfg.DebugEnabled.Value) LogPlayback(kind, clip, source, vol);
            _warned[(int)kind] = false;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[audio] 播放 {kind} 失败: {ex}");
        }
    }

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

        var cfg = Plugin.Cfg;

        _go = new GameObject("AyaHitmarker.AudioSource");
        UnityEngine.Object.DontDestroyOnLoad(_go);

        _source = _go.AddComponent<AudioSource>();
        // ==== 以下三项与旧 Mono 项目完全一致 ====
        _source.playOnAwake = false;
        _source.spatialBlend = 0f;
        _source.volume = 1f;
        // ==== 以下为排障开关，默认全部关闭（见类注释） ====
        _source.ignoreListenerPause = cfg.SoundIgnoreListenerPause.Value;
        _source.bypassEffects = cfg.SoundBypassEffects.Value;
        _source.bypassListenerEffects = cfg.SoundBypassEffects.Value;
        _source.bypassReverbZones = cfg.SoundBypassEffects.Value;

        ApplySpatialMode(_source);
        LogEnvironment(_source);
        return _source;
    }

    /// <summary>2D 或 3D-跟随相机。3D 模式下位置每帧由 <c>Tick</c> 更新。</summary>
    private void ApplySpatialMode(AudioSource source)
    {
        bool spatial3D = Plugin.Cfg.SoundSpatialMode.Value == SpatialMode.FollowCamera3D;
        if (Mathf.Approximately(source.spatialBlend, spatial3D ? 1f : 0f)) return;

        source.spatialBlend = spatial3D ? 1f : 0f;
        if (spatial3D)
        {
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = 100000f;   // 别让距离衰减把声音吃掉
            source.dopplerLevel = 0f;
        }
    }

    /// <summary>3D 模式下让声源跟着相机（主线程每帧调用，成本可忽略）。</summary>
    internal void Tick()
    {
        if (_go == null || _source == null) return;
        if (Plugin.Cfg.SoundSpatialMode.Value != SpatialMode.FollowCamera3D) return;

        try
        {
            var cam = Camera.main;
            if (cam != null) _go.transform.position = cam.transform.position;
        }
        catch { /* ignore */ }
    }

    private void LogEnvironment(AudioSource source)
    {
        _envLogged = true;
        try
        {
            int listeners = -1;
            try { listeners = UnityEngine.Object.FindObjectsOfType<AudioListener>().Length; }
            catch { /* 拿不到就算了 */ }

            string sampleRate, driver;
            try
            {
                sampleRate = AudioSettings.outputSampleRate.ToString();
                driver = AudioSettings.driverCapabilities.ToString();
            }
            catch { sampleRate = "?"; driver = "?"; }

            Plugin.Logger.LogInfo(
                $"[audio] AudioSource ready: enabled={source.enabled} volume={source.volume} " +
                $"spatialBlend={source.spatialBlend} mute={source.mute} " +
                $"bypassEffects={source.bypassEffects} ignorePause={source.ignoreListenerPause} " +
                $"| listenerVol={AudioListener.volume} pause={AudioListener.pause} " +
                $"listeners={listeners} sampleRate={sampleRate} driver={driver}");

            if (listeners == 0)
            {
                Plugin.Logger.LogWarning("[audio] 场景里找不到任何 AudioListener —— 这就是没声音的原因");
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[audio] 读取音频环境失败: {ex.Message}");
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
                $"vol={vol:0.##} srcVol={source.volume} spatial={source.spatialBlend} isPlaying={source.isPlaying}");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[audio] 播放诊断日志失败: {ex.Message}");
        }
    }
}
