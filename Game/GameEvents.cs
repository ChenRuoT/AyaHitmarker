using System;
using EFT;
using EFT.Ballistics;
using UnityEngine;

namespace AyaHitmarker.Game;

/// <summary>
/// 游戏事件桥。
///
/// 实测（2026-10-10，EFT 1.2.0.0 一局）后确定的两个**唯一**事件源：
///
///  命中 → <c>GlobalEventDispatcher.OnBeingHit</c>  (Action&lt;DamageInfo, Player&gt;)
///          一局 61 次，每命中恰好 1 条，DidBodyDamage / DidArmorDamage 已算好。
///          其中 6 条 bodyDmg=armorDmg=0 —— 那就是**鞭尸**（打尸体），过滤掉即可。
///
///  击杀 → <c>Player.OnPlayerDeadStatic</c>  (Action&lt;Player, IPlayer, DamageInfo, EBodyPart&gt;)
///          一局 11 次，与 GlobalEventDispatcher.OnKill 同刻同数，但多带 DamageInfo 和 EBodyPart，
///          击杀播报需要的信息（部位/武器/距离）只有它有。
///
/// **以下两个源已废弃，不要再接回来：**
///  - <c>Player.ApplyShot</c>：一发子弹触发多条，且 19/104 条 dmg=0（鞭尸），时序还在死亡事件之后。
///  - <c>Player.ApplyDamageInfo</c>：只在**护甲板**路径触发（全部 collider=RibcageUp + armorDmg&gt;0），
///    且 postfix 里 DidBodyDamage 恒为 0（by-value 拷贝，方法体内的写入看不到）。
///
/// 另外 5.0 里 <c>Player.Init</c> 已不存在，旧版「补 Init 订阅每个玩家的 BeingHitAction」的路子失效。
///
/// IL2CPP 的事件在 interop 里是**真 C# event**，事件类型是托管 <c>System.Action&lt;...&gt;</c>，
/// 直接 <c>+=</c> 订阅即可（<c>add_X_Managed</c> 是事件访问器，显式调用会 CS0571）。
/// </summary>
internal static class GameEvents
{
    /// <summary>新的 raid 会话开始（GlobalEventDispatcher 换了实例）。</summary>
    internal static event Action? RaidStarted;

    internal static event Action? RaidEnded;

    private static bool _staticSubscribed;
    private static IntPtr _lastDispatcher;
    private static bool _resolveLogged;
    private static float _nextPollTime;

    // 必须持有强引用，否则委托被 GC 回收后 IL2CPP 侧就是悬空指针。
    private static readonly Action<DamageInfo, Player> OnBeingHitCb = OnGlobalBeingHit;
    private static readonly Action<Player, IPlayer, DamageInfo, EBodyPart> OnPlayerDeadCb = OnPlayerDead;

    internal static bool DispatcherPresent => _lastDispatcher != IntPtr.Zero;

    /// <summary>进程内只需订阅一次（静态事件）。</summary>
    internal static void EnsureStaticSubscribed()
    {
        if (_staticSubscribed) return;
        try
        {
            Player.OnPlayerDeadStatic += OnPlayerDeadCb;
            _staticSubscribed = true;
            Plugin.Logger.LogInfo("[events] Player.OnPlayerDeadStatic 已订阅");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[events] 订阅 Player.OnPlayerDeadStatic 失败: {ex}");
        }
    }

    /// <summary>
    /// 主线程每帧调用（内部限频）。
    /// <c>GlobalEventDispatcher</c> 是 MonoBehaviour 单例，**每个 raid 一个新实例**，
    /// 所以实例指针变化 = 新的 raid / 上一局结束；老实例销毁后订阅也随之失效，必须重订。
    ///
    /// 解析要走 interop 的泛型静态属性（<c>Singleton&lt;T&gt;.Instance</c>），不是零成本，
    /// 所以降频到 ~4Hz，而不是每帧都查。
    /// </summary>
    internal static void Poll()
    {
        float now = Time.unscaledTime;
        if (now < _nextPollTime) return;
        _nextPollTime = now + 0.25f;

        PollNow();
    }

    private static void PollNow()
    {
        GlobalEventDispatcher? dispatcher;
        try
        {
            dispatcher = Comfort.Common.Singleton<GlobalEventDispatcher>.Instance;
        }
        catch (Exception ex)
        {
            if (!_resolveLogged)
            {
                _resolveLogged = true;
                Plugin.Logger.LogWarning($"[events] 无法解析 GlobalEventDispatcher.Instance（本局不会有命中提示）: {ex.Message}");
            }
            return;
        }

        if (dispatcher == null)
        {
            if (_lastDispatcher != IntPtr.Zero)
            {
                _lastDispatcher = IntPtr.Zero;
                Raise(RaidEnded, "raid ended (dispatcher gone)");
            }
            return;
        }

        IntPtr ptr = dispatcher.Pointer;
        if (ptr == _lastDispatcher) return;

        if (_lastDispatcher != IntPtr.Zero) Raise(RaidEnded, "raid ended (dispatcher replaced)");

        _lastDispatcher = ptr;
        try
        {
            dispatcher.OnBeingHit += OnBeingHitCb;
            Raise(RaidStarted, "raid started (new dispatcher)");
        }
        catch (Exception ex)
        {
            _lastDispatcher = IntPtr.Zero;
            Plugin.Logger.LogError($"[events] 订阅 GlobalEventDispatcher.OnBeingHit 失败: {ex}");
        }
    }

    internal static void Shutdown()
    {
        if (_staticSubscribed)
        {
            try { Player.OnPlayerDeadStatic -= OnPlayerDeadCb; } catch { /* ignore */ }
            _staticSubscribed = false;
        }

        if (_lastDispatcher != IntPtr.Zero)
        {
            try
            {
                var d = Comfort.Common.Singleton<GlobalEventDispatcher>.Instance;
                if (d != null) d.OnBeingHit -= OnBeingHitCb;
            }
            catch { /* ignore */ }
            _lastDispatcher = IntPtr.Zero;
        }

        RaidStarted = null;
        RaidEnded = null;
    }

    // ------------------------------------------------------------------ 命中

    private static void OnGlobalBeingHit(DamageInfo info, Player victim)
    {
        try
        {
            ReconProbe.NoteHitEvent();

            bool victimIsLocal = victim != null && victim.IsYourPlayer;
            var aggressor = info.Player?.iPlayer;
            bool aggressorIsLocal = aggressor != null && aggressor.IsYourPlayer;

            // 探查局（路线图 S1/S2）：在过滤之前记录本地玩家参与的事件。
            // 「受害者是我」这条路径原来被下面的 !IsYourPlayer 直接丢掉，所以日志里从来没有它。
            if (Plugin.Cfg.DebugEnabled.Value && (victimIsLocal || aggressorIsLocal))
                ReconProbe.LogHit(info, victim, aggressor, victimIsLocal, aggressorIsLocal);

            float body = info.DidBodyDamage;
            float armor = info.DidArmorDamage;

            // 鞭尸（打尸体）和「完全没造成伤害」都是 0/0，直接丢。
            // 这条过滤正是旧 Mono 项目 DamagePatch.cs 里那句注释的等价物。
            if (body + armor <= 0.01f) return;

            if (!aggressorIsLocal) return;

            var record = new HitRecord(
                body,
                armor,
                IsHeadCollider(info.BodyPartColliderType),
                victimIsLocal,
                info.HitPoint);

            if (Plugin.Cfg.DebugEnabled.Value)
            {
                Plugin.Logger.LogInfo(
                    $"[hit] victim={SafeNickname(victim)} body={record.BodyDamage:0.##} armor={record.ArmorDamage:0.##} " +
                    $"total={record.TotalDamage:0.##} head={record.Headshot} " +
                    $"collider={info.BodyPartColliderType} point={info.HitPoint} self={victimIsLocal}");
            }

            FeedbackBus.EnqueueHit(record);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[events] OnBeingHit 处理失败: {ex}");
        }
    }

    /// <summary>
    /// <c>GlobalEventDispatcher.OnBeingHit</c> 不带 <c>EBodyPart</c>，只有 <c>DamageInfo</c>，
    /// 所以爆头靠 <c>BodyPartColliderType</c> 判定。
    /// 头部相关的 collider 共 8 个：HeadCommon / ParietalHead / BackHead / Ears / Eyes / Jaw / NeckFront / NeckBack。
    /// </summary>
    private static bool IsHeadCollider(EBodyPartColliderType t)
    {
        switch (t)
        {
            case EBodyPartColliderType.HeadCommon:
            case EBodyPartColliderType.ParietalHead:
            case EBodyPartColliderType.BackHead:
            case EBodyPartColliderType.Ears:
            case EBodyPartColliderType.Eyes:
            case EBodyPartColliderType.Jaw:
            case EBodyPartColliderType.NeckFront:
            case EBodyPartColliderType.NeckBack:
                return true;
            default:
                return false;
        }
    }

    // ------------------------------------------------------------------ 击杀

    private static void OnPlayerDead(Player dead, IPlayer killer, DamageInfo info, EBodyPart bodyPart)
    {
        try
        {
            ReconProbe.NoteKillEvent();

            bool victimIsLocal = dead != null && dead.IsYourPlayer;
            bool killerIsLocal = killer != null && killer.IsYourPlayer;

            // 探查局（路线图 S1）：受害者 ProfileId 必须能跨「命中」与「击杀」对上同一个人。
            // 只记本地玩家参与的击杀，避免 bot 混战时刷屏。
            if (Plugin.Cfg.DebugEnabled.Value && (victimIsLocal || killerIsLocal))
                ReconProbe.LogKill(dead, killer, victimIsLocal, killerIsLocal);

            // 我被打死：只记下「谁杀了我」，供之后判定「复仇者」。不产生任何 UI。
            if (victimIsLocal)
            {
                string killerName = SafeNickname(killer);
                var death = new LocalDeathRecord(killerName, NameKey.Normalize(killerName));
                FeedbackBus.EnqueueLocalDeath(death);
                return;
            }

            if (killer == null || !killer.IsYourPlayer) return;

            bool headshot = bodyPart == EBodyPart.Head;
            WildSpawnType role = SafeRole(dead);
            string botType = BotTypeMapping.GetBotType(role);
            string victimName = SafeNickname(dead);

            EDamageType damageType = SafeDamageType(info);

            var record = new KillRecord(
                victimName,
                NameKey.Normalize(victimName),
                SafeLevel(dead),
                role,
                botType,
                BotTypeMapping.IsPmc(botType),
                BotTypeMapping.IsBoss(botType),
                headshot,
                LocalizeBodyPart(bodyPart),
                SafeWeaponName(info),
                DamageMethod.Describe(damageType),
                DamageMethod.IsNotable(damageType),
                DamageMethod.IsMelee(damageType),
                DamageMethod.IsExplosive(damageType),
                DamageMethod.IsSniper(damageType),
                SafeDistance(killer, dead));

            if (Plugin.Cfg.DebugEnabled.Value)
            {
                Plugin.Logger.LogInfo(
                    $"[kill] victim={record.VictimName} lv={record.VictimLevel} role={record.Role} " +
                    $"bot={record.BotType} boss={record.VictimIsBoss} body={record.BodyPart} head={record.Headshot} " +
                    $"method={record.Method} weapon={record.Weapon} dist={record.Distance:0.#}");
            }

            FeedbackBus.EnqueueKill(record);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[events] OnPlayerDead 处理失败: {ex}");
        }
    }

    // ------------------------------------------------------------------ 安全取值
    // 这些都在事件线程上跑，所以每步都要 try。interop 代理在对象销毁后访问会抛/崩。

    internal static string SafeNickname(Player? p)
    {
        if (p == null) return "Unknown";
        try
        {
            var n = p.Profile?.Info?.Nickname;
            return string.IsNullOrWhiteSpace(n) ? "Unknown" : n!;
        }
        catch { return "Unknown"; }
    }

    private static string SafeNickname(IPlayer? p)
    {
        if (p == null) return "Unknown";
        try
        {
            var n = p.Profile?.Nickname;
            return string.IsNullOrWhiteSpace(n) ? "Unknown" : n!;
        }
        catch { return "Unknown"; }
    }

    private static int SafeLevel(Player? p)
    {
        if (p == null) return 0;
        try { return p.Profile?.Info?.Level ?? 0; }
        catch { return 0; }
    }

    private static WildSpawnType SafeRole(Player? p)
    {
        if (p == null) return WildSpawnType.assault;
        try { return p.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault; }
        catch { return WildSpawnType.assault; }
    }

    private static EDamageType SafeDamageType(DamageInfo? info)
    {
        if (info == null) return EDamageType.Undefined;
        try { return info.DamageType; }
        catch { return EDamageType.Undefined; }
    }

    private static string SafeWeaponName(DamageInfo info)
    {
        try
        {
            var item = info.Weapon;
            if (item == null) return DamageMethod.Describe(info.DamageType);

            try
            {
                var shortName = EFT.LocalizationExtensions.LocalizedShortName(item);
                if (!string.IsNullOrWhiteSpace(shortName)) return shortName!;
            }
            catch { /* fallthrough */ }

            try
            {
                var longName = EFT.LocalizationExtensions.LocalizedName(item);
                if (!string.IsNullOrWhiteSpace(longName)) return longName!;
            }
            catch { /* fallthrough */ }

            return DamageMethod.Describe(info.DamageType);
        }
        catch
        {
            return "?";
        }
    }

    private static float SafeDistance(IPlayer killer, Player? dead)
    {
        if (dead == null) return 0f;
        try { return Vector3.Distance(killer.Position, dead.Position); }
        catch { return 0f; }
    }

    /// <summary>
    /// <c>EBodyPart</c> → 中文部位名。
    ///
    /// **不要写成 <c>LocalizationExtensions.Localized(part)</c>。** 那里有一组泛型重载，
    /// 这个调用会命中 <c>Localized&lt;TEnum&gt;(TEnum, EStringCase)</c> —— 它用「枚举类型名/成员名」
    /// 拼 key 查本地化表，**查不到就把 key 原样返回**，于是击杀详情里会出现
    /// <c>EBodyPart/LeftLeg</c> 这种字符串（实测 bug）。改走 <see cref="BodyPartNames"/> 的确定性查表。
    /// </summary>
    internal static string LocalizeBodyPart(EBodyPart part)
        => BodyPartNames.Describe(part.ToString(), (int)part);

    /// <summary>
    /// 触发一个无参事件并记日志。
    ///
    /// 日志做了限流：历史上这里被一个错误的补丁(source: 方法体共享)高频调用，
    /// 刷了 24603 行并拖垮启动。事件本身照常触发，只是相同 reason 每 2 秒最多记一次。
    /// </summary>
    private static void Raise(Action? handler, string reason)
    {
        try
        {
            handler?.Invoke();
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[events] {reason} 回调异常: {ex}");
            return;
        }

        float now = UnityEngine.Time.unscaledTime;
        if (string.Equals(reason, _lastRaiseReason, StringComparison.Ordinal) && now - _lastRaiseTime < 2f)
        {
            _suppressedRaises++;
            if (_suppressedRaises == 100)
            {
                Plugin.Logger.LogWarning($"[events] '{reason}' 正在高频触发（已抑制 100 次）—— 检查是不是有补丁打到了共享方法体上");
            }
            return;
        }

        _lastRaiseReason = reason;
        _lastRaiseTime = now;
        _suppressedRaises = 0;
        Plugin.Logger.LogInfo($"[events] {reason}");
    }

    private static string _lastRaiseReason = string.Empty;
    private static float _lastRaiseTime = -999f;
    private static int _suppressedRaises;
}
