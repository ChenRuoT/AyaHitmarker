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
    /// <summary>主线程可用的新命中（已过滤鞭尸与非本机击杀）。</summary>
    internal static event Action<HitRecord>? HitArrived;

    internal static event Action<KillRecord>? KillArrived;

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

    /// <summary>供 <c>Patches.RaidLifecyclePatch</c> 在 <c>GameWorld.OnDestroy</c> 里调用。</summary>
    internal static void RaidEndedFromPatch() => Raise(RaidEnded, "raid ended (GameWorld.OnDestroy)");

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

        HitArrived = null;
        KillArrived = null;
        RaidStarted = null;
        RaidEnded = null;
    }

    // ------------------------------------------------------------------ 命中

    private static void OnGlobalBeingHit(DamageInfo info, Player victim)
    {
        try
        {
            float body = info.DidBodyDamage;
            float armor = info.DidArmorDamage;

            // 鞭尸（打尸体）和「完全没造成伤害」都是 0/0，直接丢。
            // 这条过滤正是旧 Mono 项目 DamagePatch.cs 里那句注释的等价物。
            if (body + armor <= 0.01f) return;

            var aggressor = info.Player?.iPlayer;
            if (aggressor == null || !aggressor.IsYourPlayer) return;

            bool victimIsLocal = victim != null && victim.IsYourPlayer;
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
            HitArrived?.Invoke(record);
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
            if (killer == null || !killer.IsYourPlayer) return;

            bool victimIsLocal = dead != null && dead.IsYourPlayer;
            var record = new KillRecord(
                SafeNickname(dead),
                SafeLevel(dead),
                SafeRole(dead),
                bodyPart == EBodyPart.Head,
                victimIsLocal,
                LocalizeBodyPart(bodyPart),
                SafeWeaponName(info),
                SafeDistance(killer, dead),
                EstimateExperience(bodyPart == EBodyPart.Head),
                0f);

            if (Plugin.Cfg.DebugEnabled.Value)
            {
                Plugin.Logger.LogInfo(
                    $"[kill] victim={record.VictimName} lv={record.VictimLevel} role={record.Role} " +
                    $"bot={BotTypeMapping.GetBotType(record.Role)} body={record.BodyPart} head={record.Headshot} " +
                    $"weapon={record.Weapon} dist={record.Distance:0.#} self={victimIsLocal}");
            }

            FeedbackBus.EnqueueKill(record);
            KillArrived?.Invoke(record);
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

    private static string SafeWeaponName(DamageInfo info)
    {
        try
        {
            var item = info.Weapon;
            if (item == null) return info.DamageType.ToString();

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

            return info.DamageType.ToString();
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

    private static string LocalizeBodyPart(EBodyPart part)
    {
        try
        {
            var localized = EFT.LocalizationExtensions.Localized(part);
            if (!string.IsNullOrWhiteSpace(localized) && localized != part.ToString()) return localized!;
        }
        catch { /* fallthrough */ }
        return part.ToString();
    }

    /// <summary>
    /// 经验值是估算：客户端拿不到「这次击杀给了多少经验」的权威数值
    /// （真实值走 SkillManager / 战局结算，不是击杀事件能读到的）。要精确就得另找数据源。
    /// </summary>
    private static int EstimateExperience(bool headshot)
    {
        int baseExp = 100 + (headshot ? 50 : 0);
        return baseExp;
    }

    private static void Raise(Action? handler, string reason)
    {
        try
        {
            handler?.Invoke();
            Plugin.Logger.LogInfo($"[events] {reason}");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[events] {reason} 回调异常: {ex}");
        }
    }
}
