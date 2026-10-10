using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace AyaHitmarker.Assets;

internal enum SoundKind
{
    Hit = 0,
    HitHeadshot = 1,
    Kill = 2,
    KillHeadshot = 3,
}

/// <summary>
/// 音效库。默认从 <c>BepInEx/plugins/AyaHitmarker/Sounds/</c> 读取，
/// 解码交给 Unity（<see cref="UnityWebRequestMultimedia"/> + <c>DownloadHandlerAudioClip</c>），
/// 所以 ogg / wav / mp3 都能吃。
///
/// <para>
/// **两个必须记住的坑**
/// </para>
///
/// <para>
/// 1. <b>不能 dispose <c>UnityWebRequest</c></b>。<c>DownloadHandlerAudioClip</c> 持有它解出来的
/// AudioClip，一旦 handler 被 dispose（或因为没人引用而被终结器 dispose），那个 clip 的原生对象
/// 就一起没了 —— 表现为「加载时日志说成功，播放时 <c>clip == null</c>（Unity 的假 null）却一条报错都没有」。
/// 旧 Mono 项目从不 dispose，所以没踩到；我加了个 <c>finally { request.Dispose(); }</c> 就踩了。
/// </para>
///
/// <para>
/// 2. 因此这里采取「能克隆就克隆」的策略：<c>GetData</c> + <c>AudioClip.Create</c> + <c>SetData</c>
/// 复制出一份自己拥有的 clip，成功后就能安全地 dispose 请求；失败则把请求放进
/// <see cref="_keepAliveRequests"/> 一直留着，靠 handler 的存活来保住 clip。
/// </para>
///
/// <para>
/// 另外：整个加载走协程（<c>WrapToIl2Cpp()</c>），主线程不阻塞 —— 旧项目用 <c>Thread.Sleep</c>
/// 死等 <c>UnityWebRequest</c>，进图和首次命中时会冻住游戏。
/// </para>
/// </summary>
internal static class AudioBank
{
    private const int SlotCount = 4;

    private static readonly AudioClip?[] _clips = new AudioClip?[SlotCount];
    private static readonly string[] _sources = new string[SlotCount];

    /// <summary>0 = 未尝试，1 = 成功，2 = 失败（不重试）。</summary>
    private static readonly int[] _state = new int[SlotCount];

    /// <summary>
    /// 这些请求不能 dispose —— 它们持有的 DownloadHandlerAudioClip 就是对应 clip 的所有者。
    /// 只有在成功克隆出副本的情况下才允许放掉请求。
    /// </summary>
    private static readonly List<UnityWebRequest> _keepAliveRequests = new List<UnityWebRequest>(4);

    private static bool _loading;

    internal static bool IsLoading => _loading;

    internal static void Invalidate()
    {
        // 旧提交即将被替换，可以安全释放
        for (int i = 0; i < _keepAliveRequests.Count; i++)
        {
            try { _keepAliveRequests[i].Dispose(); } catch { /* ignore */ }
        }
        _keepAliveRequests.Clear();
        _state[0] = _state[1] = _state[2] = _state[3] = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            _clips[i] = null;
            _sources[i] = string.Empty;
        }
    }

    internal static AudioClip? Get(SoundKind kind)
    {
        int i = (int)kind;
        if (_state[i] != 1) return null;

        var clip = _clips[i];
        // Unity 的假 null：托管包装还在，原生对象已经销毁
        if (clip == null) return null;
        return clip;
    }

    internal static string SourceOf(SoundKind kind)
    {
        var s = _sources[(int)kind];
        return string.IsNullOrEmpty(s) ? "<未加载>" : s;
    }

    /// <summary>托管迭代器。调用方负责 <c>StartCoroutine(LoadAll().WrapToIl2Cpp())</c>。</summary>
    internal static IEnumerator LoadAll()
    {
        if (_loading) yield break;
        _loading = true;

        try
        {
            string dir = Plugin.SoundsDirectory;
            Plugin.Logger.LogInfo($"[audio] 开始加载音效: {dir}");

            for (int i = 0; i < SlotCount; i++)
            {
                if (_state[i] == 1 && _clips[i] != null) continue;

                var kind = (SoundKind)i;
                string? path = ResolvePath(kind);
                if (path == null)
                {
                    _state[i] = 2;
                    Plugin.Logger.LogWarning($"[audio] {kind}: 在 Sounds/ 下没找到文件（候选: {string.Join(", ", CandidateNames(kind))}）");
                    continue;
                }

                yield return LoadOne(kind, path);
            }

            Plugin.Logger.LogInfo(
                $"[audio] 加载完成 hit={_state[0] == 1} headshot_hit={_state[1] == 1} " +
                $"kill={_state[2] == 1} headshot_kill={_state[3] == 1}");
        }
        finally
        {
            _loading = false;
        }
    }

    private static IEnumerator LoadOne(SoundKind kind, string path)
    {
        int i = (int)kind;

        var type = AudioTypeFor(path);
        if (type == AudioType.UNKNOWN)
        {
            _state[i] = 2;
            Plugin.Logger.LogWarning($"[audio] {kind}: 不支持的扩展名 {Path.GetExtension(path)}");
            yield break;
        }

        UnityWebRequest? request = null;
        try
        {
            request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type);
        }
        catch (Exception ex)
        {
            _state[i] = 2;
            Plugin.Logger.LogError($"[audio] {kind} 创建请求失败: {ex.Message}");
            yield break;
        }

        if (request == null)
        {
            _state[i] = 2;
            Plugin.Logger.LogError($"[audio] {kind} 创建请求返回 null");
            yield break;
        }

        // 注意：yield return 不能出现在带 catch 的 try 里（CS1626），所以等待循环必须裸在外面。
        UnityWebRequestAsyncOperation? op = null;
        try
        {
            op = request.SendWebRequest();
        }
        catch (Exception ex)
        {
            _state[i] = 2;
            Plugin.Logger.LogError($"[audio] {kind} 发送请求失败: {ex.Message}");
            SafeDispose(request);
            yield break;
        }

        while (op != null && !op.isDone) yield return null;

        try
        {
            string? error = request.error;
            if (!string.IsNullOrEmpty(error))
            {
                _state[i] = 2;
                Plugin.Logger.LogError($"[audio] {kind} 加载失败: {error} ({path})");
                SafeDispose(request);
                yield break;
            }

            var clip = DownloadHandlerAudioClip.GetContent(request);
            if (clip == null)
            {
                _state[i] = 2;
                Plugin.Logger.LogError($"[audio] {kind} 解码结果为空: {path}");
                SafeDispose(request);
                yield break;
            }

            // 直接用 handler 给的 clip（旧 Mono 项目就是这么做的并且能出声），
            // 但**必须一直持有请求** —— 否则 handler 被终结器回收时 clip 一起消失。
            _clips[i] = clip;
            _keepAliveRequests.Add(request);
            _sources[i] = path;
            _state[i] = 1;

            // 诊断：量一次 PCM 峰值，用来区分「解码出来就是静音」和「解码没问题但播不出声」
            float peak = MeasurePeak(clip, out bool readOk);

            Plugin.Logger.LogInfo(
                $"[audio] {kind} <- {Path.GetFileName(path)} " +
                $"(len={clip.length:0.###}s samples={clip.samples} ch={clip.channels} " +
                $"freq={clip.frequency} loadState={SafeState(clip)} " +
                $"readOk={readOk} peak={peak:0.####})");
        }
        catch (Exception ex)
        {
            _state[i] = 2;
            Plugin.Logger.LogError($"[audio] {kind} 加载异常: {ex}");
            SafeDispose(request);
        }
    }

    /// <summary>
    /// 读一次 PCM 并返回峰值（0..1），只用于诊断。
    /// 用来区分两种情况：解码出来本身就是静音（peak=0），还是解码没问题但播不出声（peak&gt;0）。
    /// </summary>
    private static float MeasurePeak(AudioClip clip, out bool readOk)
    {
        readOk = false;
        try
        {
            int samples = clip.samples;
            int channels = clip.channels;
            if (samples <= 0 || channels <= 0) return 0f;

            var buffer = new float[samples * channels];
            if (!clip.GetData(buffer, 0)) return 0f;
            readOk = true;

            float peak = 0f;
            for (int i = 0; i < buffer.Length; i++)
            {
                float v = buffer[i] < 0f ? -buffer[i] : buffer[i];
                if (v > peak) peak = v;
            }
            return peak;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[audio] 读取 PCM 峰值失败: {ex.Message}");
            return 0f;
        }
    }

    private static string SafeState(AudioClip clip)
    {
        try { return clip.loadState.ToString(); }
        catch { return "?"; }
    }

    private static void SafeDispose(UnityWebRequest? request)
    {
        if (request == null) return;
        try { request.Dispose(); } catch { /* ignore */ }
    }

    private static AudioType AudioTypeFor(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".ogg": return AudioType.OGGVORBIS;
            case ".wav": return AudioType.WAV;
            case ".mp3": return AudioType.MPEG;
            default: return AudioType.UNKNOWN;
        }
    }

    /// <summary>配置里的绝对/相对路径优先，其次按 <c>Sounds/</c> 下的约定文件名找。</summary>
    private static string? ResolvePath(SoundKind kind)
    {
        string configured = kind switch
        {
            SoundKind.Hit => Plugin.Cfg.SoundFileHit.Value,
            SoundKind.HitHeadshot => Plugin.Cfg.SoundFileHitHeadshot.Value,
            SoundKind.Kill => Plugin.Cfg.SoundFileKill.Value,
            _ => Plugin.Cfg.SoundFileKillHeadshot.Value,
        };

        if (!string.IsNullOrWhiteSpace(configured))
        {
            string p = configured.Trim();
            if (!Path.IsPathRooted(p)) p = Path.Combine(Plugin.PluginDirectory, p);
            if (File.Exists(p)) return p;
            Plugin.Logger.LogWarning($"[audio] 配置里指定的文件不存在，回退到 Sounds/: {p}");
        }

        string dir = Plugin.SoundsDirectory;
        if (!Directory.Exists(dir)) return null;

        foreach (var name in CandidateNames(kind))
        {
            string candidate = Path.Combine(dir, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string[] CandidateNames(SoundKind kind) => kind switch
    {
        SoundKind.Hit => new[] { "hit.ogg", "hit.wav", "hit.mp3" },
        SoundKind.HitHeadshot => new[] { "headshot_hit.ogg", "hit_headshot.ogg", "headshot_hit.wav", "headshot_hit.mp3" },
        SoundKind.Kill => new[] { "kill.ogg", "kill.wav", "kill.mp3" },
        _ => new[] { "headshot_kill.ogg", "kill_headshot.ogg", "headshot_kill.wav", "headshot_kill.mp3" },
    };
}
