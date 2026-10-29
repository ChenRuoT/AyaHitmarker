using System;
using Comfort.Common;
using EFT;
using UnityEngine;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace AyaHitmarker.Game;

/// <summary>主线程读取后端规则；事件线程只冻结 KillExperienceInput。</summary>
internal sealed class KillExperienceRuntime
{
    private KillExperience.Rules? _rules;
    private Task<KillExperience.Rules>? _request;
    private CancellationTokenSource? _cancel;
    private double _retryAt;
    internal void Reset()
    {
        _cancel?.Cancel();
        _cancel?.Dispose();
        _cancel = null;
        _request = null;
        _rules = null;
        _retryAt = 0;
    }

    // 所有结果在 Update 发布；异步部分只有 HTTP 和纯 JSON，不接触 Unity。
    internal void Tick()
    {
        if (_rules != null) return;
        if (_request == null && AudioMetrics.NowMs >= _retryAt)
        {
            _cancel = new CancellationTokenSource();
            _request = FetchRules(_cancel.Token);
        }
        if (_request == null || !_request.IsCompleted) return;
        try
        {
            _rules = _request.GetAwaiter().GetResult();
            Plugin.Logger.LogInfo($"[xp] rules=backend-http pmc={_rules.PmcBase} bot={_rules.BotBase} " +
                $"headPmc={_rules.PmcHead} headBot={_rules.BotHead} combo={string.Join(",", _rules.Combo)} matchMult=1");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[xp] 后端经验规则暂不可读，30 秒后重试: {ex.GetBaseException().Message}");
            _retryAt = AudioMetrics.NowMs + 30000;
        }
        finally { _request = null; _cancel?.Dispose(); _cancel = null; }
    }

    private static async Task<KillExperience.Rules> FetchRules(CancellationToken cancellation)
    {
        // 使用 SPT 自己的 URL、会话、解压 / 解包路径；仅反射托管 Common API，
        // 不增加 SPT Reflection / Harmony 引用，也不读取或输出认证信息。
        Type? type = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (assembly.GetName().Name == "SPTushonka.Common")
                type = assembly.GetType("SPTushonka.Common.Http.RequestHandler");
        var method = type?.GetMethod("GetJsonAsync", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string) });
        if (method == null) throw new InvalidOperationException("SPT asynchronous request API unavailable");
        var fetch = method.CreateDelegate<Func<string, Task<string>>>();
        string json = await fetch("/client/globals").WaitAsync(TimeSpan.FromSeconds(15), cancellation).ConfigureAwait(false);
        return KillExperience.ParseBackendJson(json);
    }

    internal int? Evaluate(in KillRecord kill, int raidKills)
    {
        try
        {
            if (_rules == null)
            {
                // 与 HealthStatisticsManager.Init 的原生读取路径一致：
                // script.json @96711216 = Singleton<GlobalConfiguration>.get_Instance()。
                var global = Singleton<GlobalConfiguration>.Instance;
                var cfg = global?.Experience?.Kill;
                int applications = 0;
                if (cfg == null)
                {
                    // 菜单期间原生统计单例可能尚未填充；包含 inactive 的应用宿主。
                    var apps = Resources.FindObjectsOfTypeAll<TarkovApplication>();
                    applications = apps.Length;
                    for (int i = 0; i < apps.Length && cfg == null; i++)
                        cfg = apps[i]?.GetClientBackEndSession()?.BackEndConfig?.Config?.Experience?.Kill;
                }
                if (cfg == null) return Missing(kill, $"backend kill settings unavailable (global={global != null}, exp={global?.Experience != null}, applications={applications})");
                var table = cfg.Combo;
                if (table == null || table.Length == 0) return Missing(kill, "combo table unavailable");
                var percentages = new int[table.Length];
                for (int i = 0; i < percentages.Length; i++) percentages[i] = table[i].Percent;
                _rules = new KillExperience.Rules(cfg.VictimLevelExp, cfg.VictimBotLevelExp,
                    cfg.PmcHeadShotMult, cfg.BotHeadShotMult, percentages);
                Plugin.Logger.LogInfo($"[xp] rules=backend pmc={cfg.VictimLevelExp} bot={cfg.VictimBotLevelExp} " +
                    $"headPmc={cfg.PmcHeadShotMult} headBot={cfg.BotHeadShotMult} combo={string.Join(",", percentages)} matchMult=1");
            }

            bool penaltyUnsupported = false;
            if (kill.ExperienceInput.Available && kill.ExperienceInput.EffectiveSide == 4 &&
                ((int)kill.Role <= 1 || !kill.ExperienceInput.IsAI))
            {
                var player = Singleton<GameWorld>.Instance?.MainPlayer;
                if (player == null) return Missing(kill, "local player unavailable for special modifier");
                // 此特殊物品分支的取整尚未证实；宁可不显示，也不把未核实的值当成真实经验。
                penaltyUnsupported = player.HasMarkOfUnknown(out _);
            }
            if (penaltyUnsupported) return Missing(kill, "MarkOfUnknown scav XP penalty requires verification");
            if (!KillExperience.TryCalculate(kill.ExperienceInput, _rules, kill.Headshot, raidKills, out var xp))
                return Missing(kill, "victim experience input unavailable");
            if (Plugin.Cfg.DebugEnabled.Value)
                Plugin.Logger.LogInfo($"[xp] key={kill.VictimProfileId} raidKills={raidKills} base={xp.Base} " +
                    $"head={xp.HeadshotBonus} combo={xp.ComboBonus} total={xp.Total} matchMult=1");
            return xp.Total;
        }
        catch (Exception ex) { return Missing(kill, ex.Message); }
    }

    private static int? Missing(in KillRecord kill, string reason)
    {
        Plugin.Logger.LogWarning($"[xp] key={kill.VictimProfileId} experience unavailable: {reason}");
        return null;
    }
}
