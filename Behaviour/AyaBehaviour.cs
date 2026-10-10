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

        // 事件源存活检查：GlobalEventDispatcher 每个 raid 一个新实例
        GameEvents.Poll();

        _ui.ApplyScaleIfChanged();
        ReloadIfConfigurationChanged();
        HandleHotkeys();

        DrainFeedback();
        _ui.Tick();
        DumpDiagnostics();
    }

    private void OnDestroy()
    {
        try
        {
            GameEvents.RaidStarted -= OnRaidStarted;
            GameEvents.RaidEnded -= OnRaidEnded;
            _sound.Shutdown();
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[behaviour] OnDestroy: {ex.Message}");
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        IconBank.Load();
        _ui.Ensure();

        GameEvents.EnsureStaticSubscribed();
        GameEvents.RaidStarted += OnRaidStarted;
        GameEvents.RaidEnded += OnRaidEnded;

        _lastFontName = Plugin.Cfg.UiFontName.Value;
        _lastSoundFingerprint = SoundFingerprint();
        _lastGlowFingerprint = GlowFingerprint();

        StartAudioLoad();

        Plugin.Logger.LogInfo("[behaviour] initialized");
    }

    private void OnRaidStarted() => _ui.ResetAll();

    private void OnRaidEnded() => _ui.ResetAll();

    private void StartAudioLoad()
    {
        try
        {
            // WrapToIl2Cpp 把托管迭代器桥到 Unity 协程；解码在 Unity 侧异步完成，主线程不阻塞。
            StartCoroutine(AudioBank.LoadAll().WrapToIl2Cpp());
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[audio] 启动音效加载协程失败: {ex}");
        }
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

    private void HandleHotkeys()
    {
        if (!Plugin.Cfg.DebugEnabled.Value) return;

        var hitKey = Plugin.Cfg.TestHitKey.Value;
        if (hitKey.MainKey != KeyCode.None && hitKey.IsDown())
        {
            _ui.Hitmarker.Show(Vector2.zero);
            _ui.DamageNumbers.Show(Vector2.zero, 76f, false);
            _sound.PlayHit(false);
        }

        var killKey = Plugin.Cfg.TestKillKey.Value;
        if (killKey.MainKey != KeyCode.None && killKey.IsDown())
        {
            _ui.KillFeed.Show(new KillRecord(
                "Nikita", 42, EFT.WildSpawnType.pmcUSEC, true, false,
                "头部", "M4A1", 87.4f, 150, Time.unscaledTime));
            _sound.PlayKill(true);
        }
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
            AudioBank.Invalidate();
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
            $"audioLoading={AudioBank.IsLoading} " +
            $"hit={AudioBank.SourceOf(SoundKind.Hit)}");
    }
}
