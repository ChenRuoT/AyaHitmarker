using System;
using AyaHitmarker.Assets;
using UnityEngine;

namespace AyaHitmarker.Audio;

/// <summary>
/// 唯一的 AudioSource 宿主。
/// EFT 切场景会连 GameObject 一起销毁，所以 DontDestroyOnLoad + 每次播放前做存活检查，
/// 而不是像旧项目那样再挂一个 2 秒轮询去"恢复"它。
/// </summary>
internal sealed class SoundPlayer
{
    private GameObject? _go;
    private AudioSource? _source;

    internal void Shutdown()
    {
        _source = null;
        if (_go != null)
        {
            try { UnityEngine.Object.Destroy(_go); } catch { /* ignore */ }
            _go = null;
        }
    }

    internal void PlayHit(bool headshot) => Play(headshot ? SoundKind.HitHeadshot : SoundKind.Hit);

    internal void PlayKill(bool headshot) => Play(headshot ? SoundKind.KillHeadshot : SoundKind.Kill);

    private void Play(SoundKind kind)
    {
        if (!Plugin.Cfg.SoundEnabled.Value) return;

        try
        {
            // 还没加载完就静默跳过，绝不在这里触发加载（那会把 IO 拉进战斗热路径）
            var clip = AudioBank.Get(kind);
            if (clip == null) return;

            var source = EnsureSource();
            if (source == null) return;

            float vol = Plugin.Cfg.SoundMasterVolume.Value * kind switch
            {
                SoundKind.Hit => Plugin.Cfg.SoundHitVolume.Value,
                SoundKind.HitHeadshot => Plugin.Cfg.SoundHitHeadshotVolume.Value,
                SoundKind.Kill => Plugin.Cfg.SoundKillVolume.Value,
                _ => Plugin.Cfg.SoundKillHeadshotVolume.Value,
            };

            if (vol <= 0.0001f) return;
            source.PlayOneShot(clip, Mathf.Clamp(vol, 0f, 4f));
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[audio] 播放 {kind} 失败: {ex}");
        }
    }

    private AudioSource? EnsureSource()
    {
        // Unity 对已销毁对象的重载 == 会返回 true，所以这里能正确识别"原生对象已没了"
        if (_go != null && _source != null) return _source;

        _go = new GameObject("AyaHitmarker.Audio");
        UnityEngine.Object.DontDestroyOnLoad(_go);

        _source = _go.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.spatialBlend = 0f;   // 2D
        _source.volume = 1f;
        return _source;
    }
}
