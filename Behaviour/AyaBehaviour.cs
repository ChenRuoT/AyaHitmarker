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
    private DebugHarness? _harness;
    private readonly SoundPlayer _sound = new SoundPlayer();
    private readonly List<HitRecord> _hits = new List<HitRecord>(32);
    private readonly List<KillRecord> _kills = new List<KillRecord>(16);
    private readonly List<LocalDeathRecord> _deaths = new List<LocalDeathRecord>(4);

    /// <summary>连杀/成就判定。纯托管状态，只在主线程碰。</summary>
    private KillStreakTracker _streaks = new KillStreakTracker();

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
            // UI 时钟必须最先推进：下面的 Show / Tick 都读它
            UiClock.Advance();

            // 事件源存活检查：GlobalEventDispatcher 每个 raid 一个新实例
            GameEvents.Poll();

            _ui.ApplyScaleIfChanged();
            ReloadIfConfigurationChanged();
            HandleAudioReloadRequest();
            HandleHotkeys();

            DrainFeedback();
            _ui.Tick();
            UiClock.EndFrame();
            _harness?.Tick();   // 在 UI 画完之后：此刻截图拿到的就是当前时钟下的画面
            _sound.Tick();      // 3D 空间模式下让声源跟随相机
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
            AudioBank.Invalidate(resetRetries: true);

            // 热重载路径：画布根是 DontDestroyOnLoad 的，静态图标缓存也会钉住旧纹理。
            // 不在这里清掉，重载一次就多一层画布 / 一套纹理（见 AGENTS.md §5 热重载迭代）。
            _ui.Destroy();
            IconBank.Unload();
            ProceduralIcons.ClearCache();
            UiTheme.Unload();
            Plugin.Logger.LogInfo("[behaviour] OnDestroy：画布 / 音频宿主 / 静态缓存已清理");
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

        Step("audio", () => StartAudioLoad());

        var hit = Plugin.Cfg.TestHitKey.Value;
        var kill = Plugin.Cfg.TestKillKey.Value;
        var death = Plugin.Cfg.TestDeathKey.Value;

        Plugin.Logger.LogInfo(
            $"[behaviour] initialized | 测试快捷键: {hit.MainKey}=命中 {kill.MainKey}=击杀 {death.MainKey}=阵亡 " +
            $"(启用={Plugin.Cfg.TestHotkeysEnabled.Value}, 需 DebugEnabled=False 也生效)");

        // 主动探一次键盘：旧版 Input 后端被游戏禁用时这里就能报出来，
        // 而不是等玩家按了半天没反应才发现。
        Step("input-probe", () => { _ = UnityEngine.Input.anyKey; });

        // 调试脚本（只有设置了环境变量 AYA_DEBUG_SCRIPT 才会创建，见 DebugHarness）
        Step("harness", () => _harness = DebugHarness.TryCreate(MakeHarnessHooks()));
    }

    private DebugHarness.Hooks MakeHarnessHooks() => new DebugHarness.Hooks
    {
        Hit = headshot =>
        {
            _ui.Hitmarker.Show(Vector2.zero, headshot);
            _ui.DamageNumbers.Show(Vector2.zero, headshot ? 142f : 76f, headshot);
        },
        Kill = index => FireTestKill(TestKills.At(index), playSound: false),
        Death = index =>
        {
            string killer = TestKills.At(index).VictimName;
            _streaks.NoteLocalDeath(UiClock.Now, NameKey.Normalize(killer));
        },
        Reset = () =>
        {
            _ui.ResetAll();
            _streaks.Reset();
        },
        Backdrop = color => _ui.SetBackdrop(color),
        MenuReady = IsMainMenuLoaded,
        Audio = command =>
        {
            switch (command.ToLowerInvariant())
            {
                case "reload": StartAudioLoad(invalidate: true); break;
                case "hit": _sound.PlayHit(false); break;
                case "head": _sound.PlayHit(true); break;
                case "kill": _sound.PlayKill(false); break;
                case "killhead": _sound.PlayKill(true); break;
                case "status": break;
                case "unload": AudioBank.DebugUnloadData(); break;
                case "cleanup": _ = Resources.UnloadUnusedAssets(); GC.Collect(); break;
                default: throw new ArgumentException($"未知音频指令: {command}");
            }
            Plugin.Logger.LogInfo($"[harness] audio {command}: loading={AudioBank.IsLoading} {AudioBank.DebugStatus()}");
        },
    };

    /// <summary>
    /// DebugHarness 的 <c>waitmenu</c> 判据：主菜单场景是否已经加载。
    ///
    /// **不能用 <see cref="GameEvents.DispatcherPresent"/>**：实测插件在主菜单加载之前就已经拿到
    /// GlobalEventDispatcher 单例（启动日志里 `[events] raid started` 出现在主菜单之前），拿它当判据会让
    /// 冷启动的脚本在加载画面里就跑完。这里检查所有已加载场景里有没有主菜单的 <c>MenuUIScene</c>。
    /// 场景名对不上（或 API 不可用）时返回 false，由 waitmenu 的超时兜底。
    /// </summary>
    private static bool _sceneProbeWarned;

    private static bool IsMainMenuLoaded()
    {
        try
        {
            int count = UnityEngine.SceneManagement.SceneManager.sceneCount;
            for (int i = 0; i < count; i++)
            {
                string name = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name;
                if (!string.IsNullOrEmpty(name) &&
                    name.IndexOf("MenuUIScene", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            if (!_sceneProbeWarned)
            {
                _sceneProbeWarned = true;
                Plugin.Logger.LogWarning($"[harness] 无法判断主菜单场景: {ex.Message}");
            }
        }
        return false;
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

    /// <summary>
    /// 进图时重新加载音效。
    ///
    /// **这是音效能不能用的关键**：实测在**主菜单场景**里创建出来的 AudioClip 会随场景切换
    /// 被销毁（加载日志说 ok，进 raid 后全是 Unity 假 null）。所以每个 raid 开始时重建一批。
    /// 之前靠"播放失败 → 兜底重载"也能自愈，但代价是第一次命中/按 F8 没声音 + 一堆告警。
    /// </summary>
    private void OnRaidStarted()
    {
        _ui.ResetAll();
        _streaks.Reset();

        StartAudioLoad(invalidate: true);
    }

    private void OnRaidEnded()
    {
        _ui.ResetAll();
        _streaks.Reset();
    }

    private void StartAudioLoad(bool invalidate = false)
    {
        try
        {
            StopAudioLoad();
            if (invalidate) AudioBank.Invalidate(resetRetries: true);
            AudioBank.ConsumeReloadRequest();

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

        // StopCoroutine 只停止 Unity 调度，未必执行托管 finally。
        // 显式 Dispose 释放嵌套请求并清除 AudioBank._loading，之后才能重载。
        try { (_audioEnumerator as IDisposable)?.Dispose(); }
        catch (Exception ex) { Plugin.Logger.LogWarning($"[audio] 清理加载协程失败: {ex.Message}"); }

        _audioCoroutine = null;
        _audioWrapped = null;
        _audioEnumerator = null;
    }

    private void DrainFeedback()
    {
        _hits.Clear();
        _kills.Clear();
        _deaths.Clear();
        FeedbackBus.Drain(_hits, _kills, _deaths);

        if (_hits.Count == 0 && _kills.Count == 0 && _deaths.Count == 0) return;

        var cfg = Plugin.Cfg;
        bool atHitPoint = cfg.HitMarkerAtHitPoint.Value;

        // 事件线程只带得回「谁杀了我」，时间戳在主线程打
        if (_deaths.Count > 0)
        {
            float now = UiClock.Now;
            for (int i = 0; i < _deaths.Count; i++)
            {
                _streaks.NoteLocalDeath(now, _deaths[i].KillerKey);
                if (cfg.DebugEnabled.Value)
                {
                    Plugin.Logger.LogInfo($"[kill] 我方阵亡，凶手={_deaths[i].KillerName}（供复仇判定）");
                }
            }
        }

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

            _ui.Hitmarker.Show(anchor, hit.Headshot);
            _ui.DamageNumbers.Show(anchor, hit.DisplayDamage(cfg.DamageTextMode.Value), hit.Headshot);
            _sound.PlayHit(hit.Headshot);
        }

        // 击杀：骷髅 + 环形特效 + 得分 + 成就缎带
        for (int i = 0; i < _kills.Count; i++)
        {
            var kill = _kills[i];

            // 阈值每次重读配置：ConfigurationManager 里改完立即生效（连杀状态不重建）
            var analysis = _streaks.Register(UiClock.Now, kill.ToFact(), Plugin.Cfg.BuildStreakSettings());

            if (cfg.DebugEnabled.Value)
            {
                Plugin.Logger.LogInfo(
                    $"[kill] 判定: score={analysis.Score} streak={analysis.Streak} multi={analysis.MultiCount} " +
                    $"raidKills={analysis.RaidKills} ribbon={analysis.Ribbon.Title}({analysis.Ribbon.Kind}/{analysis.Ribbon.Tier})");
            }

            _ui.KillFeed.Show(kill, analysis);

            // 击杀确认：命中标记升级成红色扩散版，最新的伤害数字变红（命中事件在上面的循环里已经处理过了）
            _ui.Hitmarker.PromoteToKill();
            _ui.DamageNumbers.PromoteToKill();

            _sound.PlayKill(kill.Headshot);
        }
    }

    /// <summary>F9 与调试脚本共用：造一次击杀，走和真实击杀完全相同的判定 → 卡片 → 命中标记升级。</summary>
    private void FireTestKill(KillRecord rec, bool playSound)
    {
        var analysis = _streaks.Register(UiClock.Now, rec.ToFact(), Plugin.Cfg.BuildStreakSettings());
        Plugin.Logger.LogInfo(
            $"[debug] 判定: score={analysis.Score} streak={analysis.Streak} multi={analysis.MultiCount} " +
            $"ribbon={analysis.Ribbon.Title}");

        _ui.KillFeed.Show(rec, analysis);
        _ui.Hitmarker.PromoteToKill();
        _ui.DamageNumbers.PromoteToKill();
        if (playSound) _sound.PlayKill(rec.Headshot);
    }

    /// <summary>
    /// 测试快捷键。**独立于「启用调试日志」** —— 之前两者绑在一起，
    /// 结果日志开关默认关闭时按 F8/F9 毫无反应，看起来像快捷键坏了。
    ///
    /// F9 不是「造一条固定击杀」，而是按一张脚本表**依次**造出各种分支的地形：
    /// PMC 爆头远程 / SCAV 近战 / 首领爆炸 / 连续多杀（连押多次）…这样不需要真的打架
    /// 就能把得分、成就缎带、连杀进度条、军衔图标全部看一遍。
    /// F10 模拟「我被打死」，用来验证「复仇者」。
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
                _ui.Hitmarker.Show(Vector2.zero, false);
                _ui.DamageNumbers.Show(Vector2.zero, 76f, false);
                _sound.PlayHit(false);
            }

            var killKey = Plugin.Cfg.TestKillKey.Value;
            if (killKey.MainKey != KeyCode.None && killKey.IsDown())
            {
                var rec = TestKills.Next();
                Plugin.Logger.LogInfo(
                    $"[debug] 测试击杀 #{TestKills.Index}: {rec.VictimName} bot={rec.BotType} lv={rec.VictimLevel} " +
                    $"head={rec.Headshot} body={rec.BodyPart} method={rec.Method} weapon={rec.Weapon} dist={rec.Distance:0.#}");

                FireTestKill(rec, playSound: true);
            }

            var deathKey = Plugin.Cfg.TestDeathKey.Value;
            if (deathKey.MainKey != KeyCode.None && deathKey.IsDown())
            {
                // 「凶手」取下一发 F9 会击杀的那个人 —— 这样 F10 然后按 F9 必定看到「复仇者」，
                // 不用去数脚本表第几条才是同名。
                string killer = TestKills.Peek().VictimName;
                Plugin.Logger.LogInfo(
                    $"[debug] 模拟我方阵亡，凶手={killer}（下一次 F9 目标同名 → 复仇者）");
                _streaks.NoteLocalDeath(UiClock.Now, NameKey.Normalize(killer));
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
    /// 播放或健康检查发现 clip 被销毁/数据失效 → 重新加载一次。
    /// 场景切换有可能把 handler 的 clip 带走，这条是自愈路径，重试次数有上限。
    /// </summary>
    private float _nextAudioHealthCheck;

    private void HandleAudioReloadRequest()
    {
        // 场景切换也可能发生在 dispatcher 创建之后。提前发现假 null / 零采样空壳，
        // 不必等第一次命中失败才重载；仍由这里在主线程启动协程。
        float now = Time.unscaledTime;
        if (!AudioBank.IsLoading && now >= _nextAudioHealthCheck)
        {
            _nextAudioHealthCheck = now + 0.25f;
            AudioBank.CheckClipHealth();
        }

        if (AudioBank.IsLoading || !AudioBank.ReloadRequested) return;

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
            StartAudioLoad(invalidate: true);
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
            $"font={(UiTheme.Font != null ? "ok" : "MISSING")} rankIcons={IconBank.RankIconStatus} " +
            $"streak={_streaks.CurrentStreak} raidKills={_streaks.RaidKills} " +
            $"audioLoading={AudioBank.IsLoading} audio[{AudioBank.DebugStatus()}] " +
            $"hit={AudioBank.SourceOf(SoundKind.Hit)}");
    }
}
