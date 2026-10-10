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
/// 音效库。默认从 <c>BepInEx/plugins/AyaHitmarker/Sounds/</c> 读取。
///
/// 解码交给 Unity（<see cref="UnityWebRequestMultimedia"/> + <c>DownloadHandlerAudioClip</c>），
/// 所以 ogg / wav / mp3 都能吃，不需要自己写解码器。
///
/// 关键：**用协程，不要同步轮询**。旧 Mono 项目用 <c>Thread.Sleep(10)</c> 死等
/// <c>UnityWebRequest</c>，结果进图和首次命中时冻主线程。这里走 BepInEx 的
/// <c>WrapToIl2Cpp()</c> 把托管迭代器挂到 Unity 协程上，主线程全程不阻塞。
/// </summary>
internal static class AudioBank
{
    private const int SlotCount = 4;

    private static readonly AudioClip?[] _clips = new AudioClip?[SlotCount];
    private static readonly string[] _sources = new string[SlotCount];

    /// <summary>0 = 未尝试，1 = 成功，2 = 失败（不重试）。</summary>
    private static readonly int[] _state = new int[SlotCount];

    private static bool _loading;

    internal static bool IsLoading => _loading;

    internal static void Invalidate()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            _state[i] = 0;
            _clips[i] = null;
            _sources[i] = string.Empty;
        }
    }

    internal static AudioClip? Get(SoundKind kind)
    {
        int i = (int)kind;
        return _state[i] == 1 ? _clips[i] : null;
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
        // 用轮询而不是 yield return op，是因为托管迭代器经 WrapToIl2Cpp 桥接时，
        // yield null 是唯一完全无歧义的用法。
        UnityWebRequestAsyncOperation? op = null;
        try
        {
            op = request.SendWebRequest();
        }
        catch (Exception ex)
        {
            _state[i] = 2;
            Plugin.Logger.LogError($"[audio] {kind} 发送请求失败: {ex.Message}");
            try { request.Dispose(); } catch { /* ignore */ }
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

            clip.name = $"AyaHitmarker.{kind}";
            _clips[i] = clip;
            _sources[i] = path;
            _state[i] = 1;
            Plugin.Logger.LogInfo($"[audio] {kind} <- {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            _state[i] = 2;
            Plugin.Logger.LogError($"[audio] {kind} 加载异常: {ex}");
        }
        finally
        {
            try { request.Dispose(); } catch { /* ignore */ }
        }
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
