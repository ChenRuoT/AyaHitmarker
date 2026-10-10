using System;
using System.Collections.Generic;
using AyaHitmarker.Assets;
using AyaHitmarker.Audio;
using AyaHitmarker.Game;
using AyaHitmarker.Ui;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using UnityEngine;

namespace AyaHitmarker.Behaviour;

/// <summary>
/// 唯一的注入 MonoBehaviour：Unity 生命周期 + 主线程边界。
///
/// <see cref="BepInEx.Unity.IL2CPP.BasePlugin"/> 不是 MonoBehaviour（基类是 <c>System.Object</c>），
/// 所以由 <c>Plugin.Load()</c> 用 <c>AddComponent&lt;T&gt;()</c> 注入这一个组件，
/// 其余 UI 全部是纯 interop 组件（Canvas / Image / TextMeshProUGUI），不需要再注入托管类型。
/// </summary>
internal sealed class AyaBehaviour : MonoBehaviour
{
    private readonly OverlayRoot _ui = new OverlayRoot();
    private readonly SoundPlayer _sound = new SoundPlayer();
    private readonly List<HitRecord> _hits = new List<HitRecord>(32);
    private readonly List<KillRecord> _kills = new List<KillRecord>(16);

    // 协程相关引用必须自己持有：Il2CppManagedEnumerator 内部用 GCHandle 指向托管迭代器，
    // 托管侧的包装对象一旦被 GC，原生侧就是 "Handle is not initialized"。
    private Coroutine? _audioCoroutine;
    private System.Collections.IEnumerator? _audioEnumerator;
    private Il2CppSystem.Collections.IEnumerator? _audioWrapped;

    private bool _initialized;
    private string _lastFontName = string.Empty;
    private string _lastSoundFingerprint = string.Empty;
    private string _lastGlowFingerprint = string.Empty;
    private float _nextDiagDump;

    private void Awake()
    {
        Plugin.Logger.LogInfo("[behaviour] Awake");
    }

    private void Update()
    {
        EnsureInitialized();

        // 整帧兜底：任何一处异常都不该让 Unity 每帧刷一整页堆栈（限流到 5 秒一次）
        try
        {
            // 事件源存活检查：GlobalEventDispatcher 每个 raid 一个新实例
            GameEvents.Poll();

            _ui.ApplyScaleIfChanged();
            ReloadIfConfigurationChanged();
            HandleAudioReloadRequest();
            HandleHotkeys();

            DrainFeedback();
            _ui.Tick();
            _sound.Tick();   // 3D 空间模式下让声源跟随相机
            DumpDiagnostics();
        }
        catch (Exception ex)
        {
            float now = Time.unscaledTime;
            if (now - _lastUpdateErrorTime > 5f)
            {
                _lastUpdateErrorTime = now;
                Plugin.Logger.LogError($"[behaviour] Update 异常（5 秒内不再重复）: {ex}");
            }
        }
    }

    private float _lastUpdateErrorTime = -999f;

    private void OnDestroy()
    {
        try
        {
            GameEvents.RaidStarted -= OnRaidStarted;
            GameEvents.RaidEnded -= OnRaidEnded;
            StopAudioLoad();
            _sound.Shutdown();
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[behaviour] OnDestroy: {ex.Message}");
        }
    }

    /// <summary>
    /// 分步初始化。每一步都独立 try/catch —— 插图/字体/事件/音频互不阻塞，
    /// 某个环节炸了也能从日志一眼看出是哪一个，而不是整个插件静默失效。
    /// </summary>
    private void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        Step("icons", () => IconBank.Load());
        Step("ui", () => _ui.Ensure());

        Step("events", () =>
        {
            GameEvents.EnsureStaticSubscribed();
            GameEvents.RaidStarted += OnRaidStarted;
            GameEvents.RaidEnded += OnRaidEnded;
        });

        _lastFontName = Plugin.Cfg.UiFontName.Value;
        _lastSoundFingerprint = SoundFingerprint();
        _lastGlowFingerprint = GlowFingerprint();

        Step("audio", StartAudioLoad);

        var hit = Plugin.Cfg.TestHitKey.Value;
        var kill = Plugin.Cfg.TestKillKey.Value;
        Plugin.Logger.LogInfo(
            $"[behaviour] initialized | 测试快捷键: {hit.MainKey}=命中 {kill.MainKey}=击杀 " +
            $"(启用={Plugin.Cfg.TestHotkeysEnabled.Value}, 需 DebugEnabled=False 也生效)");

        // 主动探一次键盘：旧版 Input 后端被游戏禁用时这里就能报出来，
        // 而不是等玩家按了半天没反应才发现。
        Step("input-probe", () => { _ = UnityEngine.Input.anyKey; });
    }

    private static void Step(string label, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[behaviour] 初始化步骤 '{label}' 失败（其余功能继续）: {ex}");
        }
    }

    private void OnRaidStarted() => _ui.ResetAll();

    private void OnRaidEnded() => _ui.ResetAll();

    private void StartAudioLoad()
    {
        try
        {
            StopAudioLoad();

            // WrapToIl2Cpp 把托管迭代器桥到 Unity 协程；解码在 Unity 侧异步完成，主线程不阻塞。
            _audioEnumerator = AudioBank.LoadAll();
            _audioWrapped = _audioEnumerator.WrapToIl2Cpp();
            _audioCoroutine = StartCoroutine(_audioWrapped);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[audio] 启动音效加载协程失败: {ex}");
        }
    }

    private void StopAudioLoad()
    {
        try
        {
            if (_audioCoroutine != null) StopCoroutine(_audioCoroutine);
            else if (_audioWrapped != null) StopCoroutine(_audioWrapped);
        }
        catch { /* ignore */ }

        _audioCoroutine = null;
        _audioWrapped = null;
        _audioEnumerator = null;
    }

    private void DrainFeedback()
    {
        _hits.Clear();
        _kills.Clear();
        FeedbackBus.Drain(_hits, _kills);

        if (_hits.Count == 0 && _kills.Count == 0) return;

        var cfg = Plugin.Cfg;
        bool atHitPoint = cfg.HitMarkerAtHitPoint.Value;

        for (int i = 0; i < _hits.Count; i++)
        {
            var hit = _hits[i];

            // 自己被打也会触发 GlobalBeingHit（aggressor 是别人）。v1 只提示「我打中了别人」。
            if (hit.VictimIsLocalPlayer) continue;

            Vector2 anchor = Vector2.zero;
            if (atHitPoint)
            {
                var projected = _ui.WorldToCanvas(hit.HitPoint);
                if (projected.HasValue) anchor = projected.Value;
            }

            _ui.Hitmarker.Show(anchor);
            _ui.DamageNumbers.Show(anchor, hit.DisplayDamage(cfg.DamageTextMode.Value), hit.Headshot);
            _sound.PlayHit(hit.Headshot);
        }

        // 击杀：骷髅 + 环形特效 + 播报文字（旧项目命中图标只在命中时出现，击杀不再叠一次）
        for (int i = 0; i < _kills.Count; i++)
        {
            var kill = _kills[i];
            _ui.KillFeed.Show(kill);
            _sound.PlayKill(kill.Headshot);
        }
    }

    /// <summary>
    /// 测试快捷键。**独立于「启用调试日志」** —— 之前两者绑在一起，
    /// 结果日志开关默认关闭时按 F8/F9 毫无反应，看起来像快捷键坏了。
    /// </summary>
    private void HandleHotkeys()
    {
        if (!Plugin.Cfg.TestHotkeysEnabled.Value) return;
        if (_inputUnavailable) return;

        try
        {
            var hitKey = Plugin.Cfg.TestHitKey.Value;
            if (hitKey.MainKey != KeyCode.None && hitKey.IsDown())
            {
                Plugin.Logger.LogInfo("[debug] 测试命中");
                _ui.Hitmarker.Show(Vector2.zero);
                _ui.DamageNumbers.Show(Vector2.zero, 76f, false);
                _sound.PlayHit(false);
            }

            var killKey = Plugin.Cfg.TestKillKey.Value;
            if (killKey.MainKey != KeyCode.None && killKey.IsDown())
            {
                Plugin.Logger.LogInfo("[debug] 测试击杀");
                _ui.Hitmarker.Show(Vector2.zero);
                _ui.KillFeed.Show(new KillRecord(
                    "Nikita", 42, EFT.WildSpawnType.pmcUSEC, false, false,
                    "胸部", "M4A1", 87.4f, 100, Time.unscaledTime));
                _ui.KillFeed.Show(new KillRecord(
                    "Target", 35, EFT.WildSpawnType.pmcBEAR, true, false,
                    "头部", "SR-25", 142.7f, 150, Time.unscaledTime));
                _sound.PlayKill(false);
                _sound.PlayKill(true);
            }
        }
        catch (Exception ex)
        {
            _inputUnavailable = true;
            Plugin.Logger.LogError(
                $"[debug] 读取键盘失败，测试快捷键已停用（游戏可能禁用了旧版 Input 后端）: {ex.Message}");
        }
    }

    private bool _inputUnavailable;

    /// <summary>
    /// 播放时发现某个 clip 被销毁（Unity 假 null）→ 重新加载一次。
    /// 场景切换有可能把 handler 的 clip 带走，这条是自愈路径，重试次数有上限。
    /// </summary>
    private void HandleAudioReloadRequest()
    {
        if (!AudioBank.ReloadRequested) return;

        AudioBank.ConsumeReloadRequest();

        if (!AudioBank.HasRetryableFailure())
        {
            Plugin.Logger.LogWarning("[audio] clip 已多次不可用，不再重试");
            return;
        }

        Plugin.Logger.LogWarning("[audio] clip 不可用，重新加载一次");
        StartAudioLoad();
    }

    /// <summary>字体名变了要重建 TMP 字体资源；音效路径/辉光扩散变了要重建对应缓存。</summary>
    private void ReloadIfConfigurationChanged()
    {
        string fontName = Plugin.Cfg.UiFontName.Value ?? string.Empty;
        if (!string.Equals(fontName, _lastFontName, StringComparison.Ordinal))
        {
            _lastFontName = fontName;
            UiTheme.InvalidateFont();
            Plugin.Logger.LogInfo($"[ui] 字体改为 '{fontName}'，下次创建文本时生效");
        }

        string sound = SoundFingerprint();
        if (!string.Equals(sound, _lastSoundFingerprint, StringComparison.Ordinal))
        {
            _lastSoundFingerprint = sound;
            AudioBank.Invalidate(resetRetries: true);
            StartAudioLoad();
        }

        string glow = GlowFingerprint();
        if (!string.Equals(glow, _lastGlowFingerprint, StringComparison.Ordinal))
        {
            _lastGlowFingerprint = glow;
            IconBank.Invalidate();
        }
    }

    private static string SoundFingerprint()
    {
        var c = Plugin.Cfg;
        return string.Concat(
            c.SoundFileHit.Value, "|", c.SoundFileHitHeadshot.Value, "|",
            c.SoundFileKill.Value, "|", c.SoundFileKillHeadshot.Value);
    }

    private static string GlowFingerprint() => Plugin.Cfg.IconGlowSize.Value.ToString("0.##");

    private void DumpDiagnostics()
    {
        if (!Plugin.Cfg.DebugEnabled.Value) return;

        float now = Time.unscaledTime;
        if (now < _nextDiagDump) return;
        _nextDiagDump = now + 20f;

        Plugin.Logger.LogInfo(
            $"[diag] dispatcher={(GameEvents.DispatcherPresent ? "present" : "absent")} " +
            $"droppedHits={FeedbackBus.DroppedHits} droppedKills={FeedbackBus.DroppedKills} " +
            $"font={(UiTheme.Font != null ? "ok" : "MISSING")} " +
            $"audioLoading={AudioBank.IsLoading} audio[{AudioBank.DebugStatus()}] " +
            $"hit={AudioBank.SourceOf(SoundKind.Hit)}");
    }
}
