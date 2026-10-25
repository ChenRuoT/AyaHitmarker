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

    // 0.6.1：一发击穿护甲的子弹会产生「板阶段 + 身体」两个事件，在这里合并；等不到后续的板阶段按挡下放行
    private readonly PlateStageMerger<HitRecord> _plateMerger = new PlateStageMerger<HitRecord>();
    private readonly List<ResolvedHit<HitRecord>> _resolvedHits = new List<ResolvedHit<HitRecord>>(32);
    private readonly List<KillRecord> _kills = new List<KillRecord>(16);
    private readonly List<LocalDeathRecord> _deaths = new List<LocalDeathRecord>(4);

    /// <summary>连杀/成就判定。纯托管状态，只在主线程碰。</summary>
    private KillStreakTracker _streaks = new KillStreakTracker();

    /// <summary>0.6 c 助攻：主线程记录「我最近打过谁」，别人打死该目标时结算（纯逻辑，可离线测）。</summary>
    private readonly AssistTracker _assists = new AssistTracker();

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
            if (Plugin.Cfg.DebugEnabled.Value) ReconProbe.Poll(Time.unscaledTime);
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
        // 每一项独立 try/catch：任何一项失败都不能拦住后面的清理。
        // 共用一个 try 时，画布一旦拆失败，图标 / 程序化图标 / 字体缓存就全部漏掉 ——
        // 热重载下表现为每代多一层画布和一套 Unity 资源（见 AGENTS.md §5 热重载迭代）。
        Cleanup("events", () =>
        {
            GameEvents.RaidStarted -= OnRaidStarted;
            GameEvents.RaidEnded -= OnRaidEnded;
        });
        Cleanup("audio-load", StopAudioLoad);
        Cleanup("audio-host", () => _sound.Shutdown());
        Cleanup("audio-bank", () => AudioBank.Invalidate(resetRetries: true));

        // 热重载路径：画布根是 DontDestroyOnLoad 的，静态图标缓存也会钉住旧纹理。
        // 不在这里清掉，重载一次就多一层画布 / 一套纹理。
        Cleanup("canvas", () => _ui.Destroy());
        Cleanup("icon-bank", IconBank.Unload);
        Cleanup("procedural-icons", ProceduralIcons.ClearCache);
        Cleanup("theme", UiTheme.Unload);

        // Plugin.Unload() 已经先调过一次 Shutdown()，但旧代次可能在同一帧还有一次 Update 在跑：
        // Poll() 看到 dispatcher 就会重新订阅 OnBeingHit。Shutdown() 自身幂等
        // （_staticSubscribed / _lastDispatcher 双重判空），这里补一次即可兜住。
        Cleanup("events-shutdown", GameEvents.Shutdown);

        Plugin.Logger.LogInfo("[behaviour] OnDestroy：画布 / 音频宿主 / 静态缓存已清理");
    }

    /// <summary>卸载清理的单步包装：失败只告警，不打断其余清理步骤。</summary>
    private static void Cleanup(string label, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[behaviour] 卸载步骤 '{label}' 失败（其余继续清理）: {ex.Message}");
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
        var taken = Plugin.Cfg.TestTakenKey.Value;

        Plugin.Logger.LogInfo(
            $"[behaviour] initialized | 测试快捷键: {hit.MainKey}=命中 {kill.MainKey}=击杀 {death.MainKey}=阵亡 {taken.MainKey}=受击 " +
            $"(启用={Plugin.Cfg.TestHotkeysEnabled.Value}, 需 DebugEnabled=False 也生效)");

        // 主动探一次键盘：旧版 Input 后端被游戏禁用时这里就能报出来，
        // 而不是等玩家按了半天没反应才发现。
        Step("input-probe", () => { _ = UnityEngine.Input.anyKey; });

        // 调试脚本（只有设置了环境变量 AYA_DEBUG_SCRIPT 才会创建，见 DebugHarness）
        Step("harness", () => _harness = DebugHarness.TryCreate(MakeHarnessHooks()));
    }

    private DebugHarness.Hooks MakeHarnessHooks() => new DebugHarness.Hooks
    {
        Hit = (headshot, variant, targetIndex) =>
        {
            var target = TestKills.At(targetIndex);
            // 数值取自 2026-10-25 实机日志的典型形态（挡下：钝伤 3.48 + 耐久 58.7；击穿：身体 ~52 + 耐久 ~52）
            switch (variant)
            {
                case DebugHarness.HitVariant.Blocked:
                    ShowHitFeedback(Vector2.zero, 3.5f, 41.2f, headshot, ArmorOutcome.Blocked, target.VictimProfileId);
                    break;
                case DebugHarness.HitVariant.BlockedNoBlunt:
                    ShowHitFeedback(Vector2.zero, 0f, 41.2f, headshot, ArmorOutcome.Blocked, target.VictimProfileId);
                    break;
                case DebugHarness.HitVariant.Penetrated:
                    ShowHitFeedback(Vector2.zero, 52f, 41.2f, headshot, ArmorOutcome.Penetrated, target.VictimProfileId);
                    break;
                default:
                    ShowHitFeedback(Vector2.zero, headshot ? 142f : 76f, 0f, headshot, ArmorOutcome.None, target.VictimProfileId);
                    break;
            }
        },
        Kill = index => FireTestKill(TestKills.At(index), playSound: false),
        Taken = (degrees, damage, armorOnly) =>
        {
            // 0.6 e：调试/验收入口，直接按给定方位角生成粒子（真实路径走 Camera 换算）
            _ui.HitDirection.ShowDebug(degrees * Mathf.Deg2Rad, damage, armorOnly);
        },
        Assist = (index, damage) =>
        {
            // 0.6 c：调试/验收入口，按测试击杀表的受害者数据生成一条助攻提示
            var rec = TestKills.At(index);
            _ui.AssistCards.Show(new AssistRecord(
                rec.VictimName,
                rec.Role,
                rec.BotType,
                rec.IsPmc,
                rec.VictimIsBoss,
                rec.VictimLevel,
                damage,
                rec.Headshot));
        },
        Death = index =>
        {
            string killer = TestKills.At(index).VictimName;
            _streaks.NoteLocalDeath(UiClock.Now, NameKey.Normalize(killer));
        },
        Reset = () =>
        {
            _ui.ResetAll();
            _streaks.Reset();
            _assists.Reset();
            _plateMerger.Reset();
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
        _assists.Reset();
        _plateMerger.Reset();
        ReconProbe.Reset();

        StartAudioLoad(invalidate: true);
    }

    private void OnRaidEnded()
    {
        _ui.ResetAll();
        _streaks.Reset();
        _assists.Reset();
        _plateMerger.Reset();
        ReconProbe.Reset();
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

    /// <summary>
    /// 一次命中的画面反馈（命中标记 + 伤害数字），真实命中 / F8 / 调试脚本共用。
    /// 蓝色辉光只给「被挡下」；击穿时标记保持普通，护甲耐久伤害作为一行数字放在身体伤害上方。
    /// </summary>
    private void ShowHitFeedback(Vector2 anchor, float body, float armor, bool headshot, ArmorOutcome outcome, string targetKey)
    {
        var cfg = Plugin.Cfg;
        bool blocked = outcome == ArmorOutcome.Blocked;

        _ui.Hitmarker.Show(anchor, headshot, blocked);

        if (cfg.DamageTextMode.Value == DamageNumberMode.Total)
        {
            // 旧行为：一个数字 = 身体 + 护甲；挡下时整个数字用护甲样式
            _ui.DamageNumbers.Show(anchor, body + armor, 0f, headshot, blocked, targetKey);
        }
        else
        {
            float armorLine = cfg.ArmorHitMarkerEnabled.Value ? armor : 0f;
            _ui.DamageNumbers.Show(anchor, body, armorLine, headshot, false, targetKey);
        }
    }

    private void DrainFeedback()
    {
        _hits.Clear();
        _kills.Clear();
        _deaths.Clear();
        FeedbackBus.Drain(_hits, _kills, _deaths);

        // 板阶段合并：每帧都要调（批次为空也调），等不到后续事件的板阶段靠它按挡下放行
        _resolvedHits.Clear();
        _plateMerger.Process(_hits, _resolvedHits);

        if (_resolvedHits.Count == 0 && _kills.Count == 0 && _deaths.Count == 0) return;

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

        for (int i = 0; i < _resolvedHits.Count; i++)
        {
            var resolved = _resolvedHits[i];
            var hit = resolved.Primary.Resolve(resolved.Body, resolved.Armor, resolved.Outcome);

            if (cfg.DebugEnabled.Value && resolved.Outcome != ArmorOutcome.None)
            {
                Plugin.Logger.LogInfo(
                    $"[hit] 护甲判定: {resolved.Outcome} body={resolved.Body:0.##} armor={resolved.Armor:0.##} " +
                    $"key={hit.TargetKey} self={hit.VictimIsLocalPlayer}");
            }

            // 自己被打：0.6 e 的受击方向指示走这条路径（不再像 v1 那样直接丢弃）
            if (hit.VictimIsLocalPlayer)
            {
                _ui.HitDirection.Show(hit);
                continue;
            }

            // 0.6 c：记下「我最近打过谁」，别人打死该目标时结算助攻
            // 只记身体伤害：护甲耐久伤害不是打在人身上的（旧版把 body+armor 都算进去，一发穿甲子弹会被记成 ~100）
            _assists.NoteHit(hit.TargetKey, UiClock.Now, hit.BodyDamage, hit.Headshot);

            Vector2 anchor = Vector2.zero;
            if (atHitPoint)
            {
                var projected = _ui.WorldToCanvas(hit.HitPoint);
                if (projected.HasValue) anchor = projected.Value;
            }

            ShowHitFeedback(anchor, hit.BodyDamage, hit.ArmorDamage, hit.Headshot, hit.Armor, hit.TargetKey);
            _sound.PlayHit(hit.Headshot);
        }

        // 击杀：骷髅 + 环形特效 + 得分 + 成就缎带
        for (int i = 0; i < _kills.Count; i++)
        {
            var kill = _kills[i];

            // 0.6 c：不是我打死的 → 到助攻表里结算；不会进入击杀卡片 / 得分 / 连杀
            if (!kill.KillerIsLocalPlayer)
            {
                HandleAssistCandidate(kill);
                continue;
            }

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
            _ui.DamageNumbers.PromoteToKill(kill.VictimProfileId);

            // 0.6 d：双杀 / 三杀 / 四杀及以上用可选连杀音效；文件缺失时回退普通击杀音效
            if (analysis.MultiCount >= 2) _sound.PlayMultiKill(analysis.MultiCount, kill.Headshot);
            else _sound.PlayKill(kill.Headshot);
        }
    }

    /// <summary>
    /// 0.6 c：一条「别人打死的」击杀记录 → 查助攻表。只有我在窗口内打过该目标才会出提示；
    /// 查不到就什么也不做（bot 互相打的记录止步于此）。
    /// </summary>
    private void HandleAssistCandidate(in KillRecord kill)
    {
        var cfg = Plugin.Cfg;
        if (!cfg.AssistEnabled.Value) return;

        if (!_assists.TryTakeAssist(kill.VictimProfileId, UiClock.Now, cfg.AssistWindowSeconds.Value,
                out float damage, out bool headshot))
        {
            return;
        }

        var record = new AssistRecord(
            kill.VictimName,
            kill.Role,
            kill.BotType,
            kill.IsPmc,
            kill.VictimIsBoss,
            kill.VictimLevel,
            damage,
            headshot);
        _ui.AssistCards.Show(record);

        if (cfg.DebugEnabled.Value)
        {
            Plugin.Logger.LogInfo(
                $"[assist] {kill.VictimName}（{kill.BotType}）伤害累计={damage:0.#} 爆头={headshot}");
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
        _ui.DamageNumbers.PromoteToKill(rec.VictimProfileId);
        if (playSound)
        {
            if (analysis.MultiCount >= 2) _sound.PlayMultiKill(analysis.MultiCount, rec.Headshot);
            else _sound.PlayKill(rec.Headshot);
        }
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
                // F8：**随机命中** —— 伤害数值 / 是否打中护甲 / 是否爆头全部随机。
                // （旧版用 Shift+F8 模拟护甲命中，但游戏里 Shift 的组合键读不到；改成单键随机后每次按都能看到不同分支。）
                bool headshot = UnityEngine.Random.value < 0.30f;
                // 35% 护甲命中，其中 45% 挡下带钝伤 / 20% 零钝伤挡下 / 35% 击穿（和实机日志里的比例大致相当）
                var outcome = ArmorOutcome.None;
                float body = Mathf.Round(UnityEngine.Random.Range(20f, 160f));
                float armor = 0f;
                if (UnityEngine.Random.value < 0.35f)
                {
                    armor = Mathf.Round(UnityEngine.Random.Range(28f, 60f));
                    float roll = UnityEngine.Random.value;
                    if (roll < 0.45f) { outcome = ArmorOutcome.Blocked; body = Mathf.Round(UnityEngine.Random.Range(1f, 8f)); }
                    else if (roll < 0.65f) { outcome = ArmorOutcome.Blocked; body = 0f; }
                    else { outcome = ArmorOutcome.Penetrated; body = Mathf.Round(UnityEngine.Random.Range(28f, 60f)); }
                }

                var target = TestKills.Peek();
                Plugin.Logger.LogInfo(
                    $"[debug] 测试命中（随机）：身体={body:0} 护甲={armor:0} 爆头={headshot} 结局={outcome} 目标={target.VictimName}");
                ShowHitFeedback(Vector2.zero, body, armor, headshot, outcome, target.VictimProfileId);
                _sound.PlayHit(headshot);
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

            var takenKey = Plugin.Cfg.TestTakenKey.Value;
            if (takenKey.MainKey != KeyCode.None && takenKey.IsDown())
            {
                // P：**随机受击** —— 方位 / 伤害 / 是否只被护甲吸收全部随机，用来在游戏里边调设置边看受击方向点阵。
                // 走 ShowDebug（直接给方位角），不经过相机换算；三色判定与真实路径一致（重伤阈值读设置）。
                float degrees = Mathf.Round(UnityEngine.Random.Range(-180f, 180f));
                bool armorOnly = UnityEngine.Random.value < 0.30f;
                float damage = Mathf.Round(armorOnly
                    ? UnityEngine.Random.Range(10f, 80f)
                    : UnityEngine.Random.Range(10f, 120f));
                Plugin.Logger.LogInfo(
                    $"[debug] 测试受击（随机）：方位={degrees:0}° 伤害={damage:0} 护甲吸收={armorOnly}");
                _ui.HitDirection.ShowDebug(degrees * Mathf.Deg2Rad, damage, armorOnly);
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
            c.SoundFileKill.Value, "|", c.SoundFileKillHeadshot.Value, "|",
            c.SoundFileDoubleKill.Value, "|", c.SoundFileTripleKill.Value, "|", c.SoundFileMultiKill.Value);
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
            $"raidState={GameEvents.DebugState} " +
            $"droppedHits={FeedbackBus.DroppedHits} droppedKills={FeedbackBus.DroppedKills} " +
            $"font={(UiTheme.Font != null ? "ok" : "MISSING")} rankIcons={IconBank.RankIconStatus} " +
            $"streak={_streaks.CurrentStreak} raidKills={_streaks.RaidKills} " +
            $"reconHits={ReconProbe.RawHitEvents} reconKills={ReconProbe.RawKillEvents} " +
            $"audioLoading={AudioBank.IsLoading} audio[{AudioBank.DebugStatus()}] " +
            $"hit={AudioBank.SourceOf(SoundKind.Hit)}");
    }
}
