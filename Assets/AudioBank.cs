using System;
using System.Collections;
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
/// 音效异步解码后直接使用 handler 的 clip，并逐槽持有请求。
/// GetData 在本环境返回全 0，不能用它克隆或判断是否静音。
/// clip 用 HideAndDontSave 防止资源清理；播放前和低频健康检查还需验证元数据，
/// 因为实测 clip 非 null、loadState=Loaded 时也可能已变成零采样空壳。
/// </summary>
internal static class AudioBank
{
    private const int SlotCount = 4;

    private static readonly AudioClip?[] _clips = new AudioClip?[SlotCount];
    private static readonly string[] _sources = new string[SlotCount];

    /// <summary>0 = 待加载，1 = 已加载（仍须检查数据），2 = 失败/重试耗尽。</summary>
    private static readonly int[] _state = new int[SlotCount];

    /// <summary>每个槽位自己失败后允许的重试次数（场景切换可能带走 clip）。</summary>
    private static readonly int[] _retries = new int[SlotCount];

    private const int MaxRetries = 3;

    // 请求拥有 handler/clip。只在槽位替换或整个缓存失效时释放，避免重载累积泄漏。
    private static readonly UnityWebRequest?[] _keepAliveRequests = new UnityWebRequest?[SlotCount];

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
            if (_state[i] == 0 || (_state[i] == 2 && _retries[i] < MaxRetries)) return true;
        }
        return false;
    }

    internal static void Invalidate(bool resetRetries = false)
    {
        for (int i = 0; i < SlotCount; i++) ReleaseSlot(i);

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
        if (!IsUsable(clip, out string reason))
        {
            // 不碰 IO：这里只记状态，由 AyaBehaviour 的主线程协程异步恢复。
            _state[i] = 2;
            if (_retries[i] < MaxRetries)
            {
                _state[i] = 0;
                _retries[i]++;
                ReloadRequested = true;
            }
            Plugin.Logger.LogWarning($"[audio] {kind} 数据失效: {reason}，retry={_retries[i]}/{MaxRetries}");
            return null;
        }
        return clip;
    }

    /// <summary>主线程低频检查对象与数据完整性，避免场景切换后的第一发静音。</summary>
    internal static void CheckClipHealth()
    {
        if (_loading) return;
        for (int i = 0; i < SlotCount; i++)
        {
            if (_state[i] == 1) _ = Get((SoundKind)i);
        }
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
            string alive = _state[i] != 1 ? "missing" : (IsUsable(clip, out string reason) ? "ok" : reason);
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
                if (_state[i] == 1 && IsUsable(_clips[i], out _)) continue;
                if (_state[i] == 2 && _retries[i] >= MaxRetries) continue;

                ReleaseSlot(i);
                var kind = (SoundKind)i;
                string? path = ResolvePath(kind);
                if (path == null)
                {
                    _state[i] = 2;
                    _retries[i] = MaxRetries;
                    Plugin.Logger.LogWarning($"[audio] {kind}: 在 Sounds/ 下没找到文件（候选: {string.Join(", ", CandidateNames(kind))}）");
                    continue;
                }

                // Unity StopCoroutine 不保证 Dispose 托管迭代器。外层显式释放时，
                // 也必须释放当前嵌套加载，回收尚未交给缓存的请求。
                var load = LoadOne(kind, path);
                using (load as IDisposable)
                {
                    yield return load;
                }
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
        UnityWebRequest? request = null;
        bool retained = false;
        try
        {

            var type = AudioTypeFor(path);
            if (type == AudioType.UNKNOWN)
            {
                _state[i] = 2;
                Plugin.Logger.LogWarning($"[audio] {kind}: 不支持的扩展名 {Path.GetExtension(path)}");
                yield break;
            }

            try
            {
                request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type);

                // 下载后解码，保留请求；不依赖 GetData 取 PCM。
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
                    yield break;
                }

                var clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip == null)
                {
                    _state[i] = 2;
                    Plugin.Logger.LogError($"[audio] {kind} 解码结果为空: {path}");
                    yield break;
                }

                // 确保数据在内存里（compressed=false 时通常已经是）
                try
                {
                    if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
                }
                catch { /* 忽略 */ }

                // handler 给的 clip 没有名字，补一个方便日志辨认
                try { if (string.IsNullOrEmpty(clip.name)) clip.name = $"AyaHitmarker.{kind}"; } catch { /* ignore */ }

                float peak = MeasurePeak(clip, out bool readOk);

                if (!IsUsable(clip, out string reason))
                {
                    _state[i] = 2;
                    Plugin.Logger.LogWarning($"[audio] {kind} 解码数据不可用: {reason} ({path})");
                    yield break;
                }

                // 仅持有托管包装/请求不阻止 Unity 的原生资源清理。
                // DontUnloadUnusedAsset 包含在 HideAndDontSave 中；仍保留完整性检查兜底。
                clip.hideFlags = HideFlags.HideAndDontSave;
                _clips[i] = clip;
                _keepAliveRequests[i] = request;
                retained = true;
                Plugin.Logger.LogInfo(
                    $"[audio] {kind} <- {Path.GetFileName(path)} " +
                    $"(len={clip.length:0.###}s samples={clip.samples} ch={clip.channels} freq={clip.frequency} " +
                    $"loadState={SafeState(clip)} dataPeak={peak:0.####} readOk={readOk} flags={clip.hideFlags}) [持有请求保活]");

                _sources[i] = path;
                _state[i] = 1;
                _retries[i] = 0;
            }
            catch (Exception ex)
            {
                _state[i] = 2;
                Plugin.Logger.LogError($"[audio] {kind} 加载异常: {ex}");
            }
        }
        finally
        {
            // 已交给缓存的请求必须保活；中断/失败的请求则在这里释放。
            if (!retained) SafeDispose(request);
        }
    }

    private static bool IsUsable(AudioClip? clip, out string reason)
    {
        if (clip == null)
        {
            reason = "destroyed";
            return false;
        }
        try
        {
            int samples = clip.samples, channels = clip.channels, frequency = clip.frequency;
            float length = clip.length;
            var state = clip.loadState;
            if (AudioClipHealth.IsUsable(samples, channels, frequency, length,
                    state == AudioDataLoadState.Loaded))
            {
                reason = "ok";
                return true;
            }
            reason = $"invalid(len={length:0.###}s samples={samples} ch={channels} freq={frequency} state={state})";
        }
        catch (Exception ex) { reason = $"unreadable({ex.Message})"; }
        return false;
    }

    private static void ReleaseSlot(int i)
    {
        var clip = _clips[i];
        _clips[i] = null;
        _state[i] = 0;
        _sources[i] = string.Empty;
        SafeDispose(_keepAliveRequests[i]);
        _keepAliveRequests[i] = null;
        // HideAndDontSave 需要显式销毁，不能等资源清理回收。
        if (clip != null) UnityEngine.Object.Destroy(clip);
    }

    /// <summary>仅调试脚本调用：制造非 null 但数据未就绪的 clip，验证健康检查恢复。</summary>
    internal static void DebugUnloadData()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            var clip = _clips[i];
            if (clip != null)
                Plugin.Logger.LogInfo($"[audio] debug unload {(SoundKind)i}: {clip.UnloadAudioData()}");
        }
    }

    private static string SafeState(AudioClip clip)
    {
        try { return clip.loadState.ToString(); }
        catch { return "?"; }
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
