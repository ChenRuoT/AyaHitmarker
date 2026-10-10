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
/// 解码交给 Unity（<see cref="UnityWebRequestMultimedia"/> + <c>DownloadHandlerAudioClip</c>）。
///
/// <para><b>踩过的三个坑（都是"日志一切正常但没声音"）</b></para>
///
/// <para>
/// 1. <b>`DownloadHandlerAudioClip.compressed` 必须显式设成 <c>false</c>。</b>
/// Unity 的 <c>UnityWebRequestMultimedia.GetAudioClip()</c> 内部把它写死为 <c>true</c>：
/// 音频以**压缩态**留在内存里，播放时才解压。后果是 <c>AudioClip.GetData()</c> 拿到的**全是 0**
/// （用这堆 0 克隆出来的副本必然是静音），而且 clip 的数据绑在 handler 的压缩缓冲上。
/// 设成 <c>false</c> 才会在下载完成时解压进内存。
/// </para>
///
/// <para>
/// 2. <b>不能 dispose <c>UnityWebRequest</c></b>。handler 一被释放，它持有的 clip 就没了 ——
/// 表现为「加载成功、播放时 Unity 假 null」。所以克隆成功才放掉请求，否则把请求一直持有。
/// </para>
///
/// <para>
/// 3. 加载走协程（<c>WrapToIl2Cpp()</c>），主线程不阻塞 —— 旧项目用 <c>Thread.Sleep</c> 死等，
/// 进图和首次命中时会冻住游戏。
/// </para>
///
/// <para>
/// 策略：<b>解压 → 读 PCM 量峰值 → 克隆成自己拥有的 clip</b>。克隆成功就彻底不依赖 handler；
/// 克隆失败（拿不到 PCM）则退回「直接用 handler 的 clip + 持有请求保活」。
/// </para>
/// </summary>
internal static class AudioBank
{
    private const int SlotCount = 4;

    private static readonly AudioClip?[] _clips = new AudioClip?[SlotCount];
    private static readonly string[] _sources = new string[SlotCount];

    /// <summary>0 = 未尝试，1 = 成功，2 = 失败（不重试）。</summary>
    private static readonly int[] _state = new int[SlotCount];

    /// <summary>每个槽位自己失败后允许的重试次数（场景切换可能带走 clip）。</summary>
    private static readonly int[] _retries = new int[SlotCount];

    private const int MaxRetries = 3;

    /// <summary>
    /// 这些请求不能 dispose —— 它们持有的 DownloadHandlerAudioClip 就是对应 clip 的所有者
    /// （只有在成功克隆出副本后才允许放掉）。
    /// </summary>
    private static readonly List<UnityWebRequest> _keepAliveRequests = new List<UnityWebRequest>(4);

    private static bool _loading;

    internal static bool IsLoading => _loading;

    /// <summary>播放时发现 clip 不可用 → 置位；由 <c>AyaBehaviour</c> 在主线程重新起加载协程。</summary>
    internal static bool ReloadRequested { get; private set; }

    internal static void ConsumeReloadRequest() => ReloadRequested = false;

    /// <summary>有没有任何一个槽位是「已失败但还没用满重试次数」——用于决定要不要重新加载。</summary>
    internal static bool HasRetryableFailure()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (_state[i] != 1 && _retries[i] < MaxRetries) return true;
        }
        return false;
    }

    internal static void Invalidate(bool resetRetries = false)
    {
        for (int i = 0; i < _keepAliveRequests.Count; i++)
        {
            try { _keepAliveRequests[i].Dispose(); } catch { /* ignore */ }
        }
        _keepAliveRequests.Clear();

        for (int i = 0; i < SlotCount; i++)
        {
            _state[i] = 0;
            _clips[i] = null;
            _sources[i] = string.Empty;
            if (resetRetries) _retries[i] = 0;
        }
    }

    internal static AudioClip? Get(SoundKind kind)
    {
        int i = (int)kind;
        if (_state[i] != 1) return null;

        var clip = _clips[i];
        // Unity 的假 null：托管包装还在，原生对象已经销毁
        if (clip == null)
        {
            // 说明 clip 被销毁了。允许重载，但别无限刷。
            if (_retries[i] < MaxRetries && !_loading)
            {
                _state[i] = 0;
                _retries[i]++;
                ReloadRequested = true;
            }
            return null;
        }
        return clip;
    }

    internal static string SourceOf(SoundKind kind)
    {
        var s = _sources[(int)kind];
        return string.IsNullOrEmpty(s) ? "<未加载>" : s;
    }

    internal static string DebugStatus()
    {
        var parts = new string[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            var clip = _clips[i];
            string alive = _state[i] != 1 ? "missing" : (clip == null ? "destroyed" : "ok");
            parts[i] = $"{(SoundKind)i}={alive}(retry {_retries[i]})";
        }
        return string.Join(" ", parts);
    }

    /// <summary>托管迭代器。调用方负责 <c>StartCoroutine(LoadAll().WrapToIl2Cpp())</c>。</summary>
    internal static IEnumerator LoadAll()
    {
        if (_loading) yield break;
        _loading = true;

        try
        {
            Plugin.Logger.LogInfo($"[audio] 开始加载音效: {Plugin.SoundsDirectory}");

            for (int i = 0; i < SlotCount; i++)
            {
                if (_state[i] == 1 && _clips[i] != null) continue;
                if (_state[i] == 2 && _retries[i] >= MaxRetries) continue;

                var kind = (SoundKind)i;
                string? path = ResolvePath(kind);
                if (path == null)
                {
                    _state[i] = 2;
                    _retries[i] = MaxRetries;
                    Plugin.Logger.LogWarning($"[audio] {kind}: 在 Sounds/ 下没找到文件（候选: {string.Join(", ", CandidateNames(kind))}）");
                    continue;
                }

                yield return LoadOne(kind, path);
            }

            Plugin.Logger.LogInfo($"[audio] 加载完成 {DebugStatus()}");
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

            // ★ 关键：Unity 的 GetAudioClip 把 compressed 写死为 true（数据保持压缩态），
            //   于是 GetData() 只会给出全 0，而且 clip 的生命周期绑在 handler 的压缩缓冲上。
            //   显式改成「下载完成即解压」，我们才能读到真实 PCM 并自己拥有它。
            try
            {
                var handler = request.downloadHandler;
                var audioHandler = handler?.TryCast<DownloadHandlerAudioClip>();
                if (audioHandler != null)
                {
                    audioHandler.compressed = false;
                    audioHandler.streamAudio = false;
                }
                else
                {
                    Plugin.Logger.LogWarning($"[audio] {kind}: downloadHandler 不是 DownloadHandlerAudioClip（{handler?.GetType().Name ?? "null"}），无法关闭压缩模式");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[audio] {kind}: 设置 compressed=false 失败: {ex.Message}");
            }
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

            // 确保数据在内存里（compressed=false 时通常已经是）
            try
            {
                if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
            }
            catch { /* 忽略 */ }

            float peak = MeasurePeak(clip, out bool readOk);

            // 优先克隆：副本完全由我们拥有，不依赖 handler 的生命周期
            float clonePeak = 0f;
            AudioClip? owned = null;
            if (peak > 0.0001f) owned = TryClone(kind, clip, out clonePeak);

            if (owned != null)
            {
                _clips[i] = owned;
                SafeDispose(request);
                Plugin.Logger.LogInfo(
                    $"[audio] {kind} <- {Path.GetFileName(path)} " +
                    $"(len={owned.length:0.###}s samples={owned.samples} ch={owned.channels} " +
                    $"freq={owned.frequency} srcPeak={peak:0.####} clonePeak={clonePeak:0.####}) [自持副本]");
            }
            else
            {
                _clips[i] = clip;
                _keepAliveRequests.Add(request);
                Plugin.Logger.LogWarning(
                    $"[audio] {kind} <- {Path.GetFileName(path)} 未能取得 PCM（readOk={readOk} peak={peak:0.####}），" +
                    "退回使用 handler 的 clip 并持有请求保活");
            }

            _sources[i] = path;
            _state[i] = 1;
            _retries[i] = 0;
        }
        catch (Exception ex)
        {
            _state[i] = 2;
            Plugin.Logger.LogError($"[audio] {kind} 加载异常: {ex}");
            SafeDispose(request);
        }
    }

    /// <summary>把 PCM 复制进一个自建 AudioClip。拿不到数据返回 <c>null</c>。</summary>
    private static AudioClip? TryClone(SoundKind kind, AudioClip src, out float peak)
    {
        peak = 0f;
        try
        {
            int samples = src.samples;
            int channels = src.channels;
            int frequency = src.frequency;
            if (samples <= 0 || channels <= 0 || frequency <= 0)
            {
                Plugin.Logger.LogWarning($"[audio] {kind} clip 参数异常: samples={samples} ch={channels} freq={frequency}");
                return null;
            }

            var buffer = new float[samples * channels];
            if (!src.GetData(buffer, 0)) return null;

            for (int k = 0; k < buffer.Length; k++)
            {
                float v = buffer[k] < 0f ? -buffer[k] : buffer[k];
                if (v > peak) peak = v;
            }

            var clone = AudioClip.Create($"AyaHitmarker.{kind}", samples, channels, frequency, false);
            if (clone == null) return null;
            if (!clone.SetData(buffer, 0))
            {
                UnityEngine.Object.Destroy(clone);
                return null;
            }

            clone.hideFlags = HideFlags.HideAndDontSave;
            return clone;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[audio] {kind} 克隆 clip 异常: {ex.Message}");
            return null;
        }
    }

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
        catch
        {
            return 0f;
        }
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
