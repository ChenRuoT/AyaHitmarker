using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace AyaHitmarker.Behaviour;

/// <summary>
/// 调试脚本执行器 —— 让「进图实测」变成可重复、可逐帧截图的流程，不用人手按键、不用抢窗口焦点。
///
/// <b>只有设置了环境变量 <c>AYA_DEBUG_SCRIPT=脚本路径</c> 时才会创建</b>，正常游戏里零开销、零行为。
/// 一行一条指令，<c>#</c> 开头是注释，每帧执行一条（<c>wait</c> 会阻塞到真实时间过去）：
/// <code>
/// clock manual           # UI 时钟改为手动步进（动画时刻完全可控）
/// bg dark                # 画布后面垫一块纯色（dark|mid|white|snow|grass|#RRGGBB|off）
/// kill 2                 # 造脚本表里第 2 条击杀（走真实的连杀判定 / 卡片 / 音效）
/// hit [head]             # 造一次命中（head = 爆头）
/// death 3                # 模拟「被脚本表第 3 条的那个人打死」（之后杀他 = 复仇者）
/// step 0.0333            # 手动时钟前进 0.0333 秒
/// shot name              # 截一张全屏图 → &lt;shots&gt;/name.png
/// seq name 12 0.0333     # 连拍：shot name_00; step; shot name_01; step; ... 共 12 张
/// scale 0.1              # 自动时钟慢放
/// wait 2.5               # 真实时间等 2.5 秒
/// audio reload           # 中断并重载音效（复现进图/改路径时的协程中断）
/// audio unload           # 卸载音频数据，验证非 null 音效的恢复
/// audio cleanup          # 触发 Unity 未使用资源清理与托管 GC
/// audio status           # 输出加载状态
/// audio hit              # 播放音效（hit|head|kill|killhead），UI 截图指令仍保持静音
/// reset                  # 重置 UI 与连杀状态
/// log 文字               # 往日志里写一行（脚本里做标记用）
/// quit                   # 退出游戏
/// </code>
/// 截图用 <c>ScreenCapture.CaptureScreenshot</c>，由引擎在帧尾写文件：不依赖窗口焦点，
/// 也不会被 Windows DPI 虚拟化坑（README §11）。输出目录取环境变量 <c>AYA_DEBUG_SHOTS</c>，缺省为插件目录下的 <c>_shots</c>。
/// </summary>
internal sealed class DebugHarness
{
    internal sealed class Hooks
    {
        internal Action<bool> Hit = _ => { };
        internal Action<int> Kill = _ => { };
        internal Action<int> Death = _ => { };
        internal Action<string> Audio = _ => { };
        internal Action Reset = () => { };
        internal Action<Color?> Backdrop = _ => { };
    }

    private readonly Hooks _hooks;
    private readonly List<string> _lines = new List<string>();
    private readonly string _shotDir;
    private int _index;
    private int _frameWait;
    private float _waitUntil = -1f;
    private bool _done;
    private int _shotCount;
    private bool _quitting;

    internal static DebugHarness? TryCreate(Hooks hooks)
    {
        string? path = Environment.GetEnvironmentVariable("AYA_DEBUG_SCRIPT");
        if (string.IsNullOrWhiteSpace(path)) return null;

        if (!File.Exists(path))
        {
            Plugin.Logger.LogWarning($"[harness] AYA_DEBUG_SCRIPT 指向的文件不存在: {path}");
            return null;
        }

        string dir = Environment.GetEnvironmentVariable("AYA_DEBUG_SHOTS") ?? Path.Combine(Plugin.PluginDirectory, "_shots");
        return new DebugHarness(hooks, File.ReadAllLines(path), dir);
    }

    private DebugHarness(Hooks hooks, string[] lines, string shotDir)
    {
        _hooks = hooks;
        _shotDir = shotDir;
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            ExpandInto(line);
        }

        try { Directory.CreateDirectory(_shotDir); }
        catch (Exception ex) { Plugin.Logger.LogWarning($"[harness] 无法创建截图目录 {_shotDir}: {ex.Message}"); }

        Plugin.Logger.LogInfo($"[harness] 载入 {_lines.Count} 条指令，截图目录 = {_shotDir}");
    }

    /// <summary><c>seq</c> 在载入时展开成 shot / step 序列，运行时只有最基本的几种指令。</summary>
    private void ExpandInto(string line)
    {
        string[] t = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (t.Length >= 4 && t[0] == "seq")
        {
            int count = Math.Clamp(ParseInt(t[2], 1), 1, 400);
            string dt = t[3];
            for (int i = 0; i < count; i++)
            {
                _lines.Add($"shot {t[1]}_{i:00}");
                _lines.Add($"step {dt}");
            }
            return;
        }
        _lines.Add(line);
    }

    /// <summary>每帧在 UI 更新<b>之后</b>调用：这一帧的画面已经按当前时钟画好了，此时截图拿到的就是那个时刻。</summary>
    internal void Tick()
    {
        if (_done) return;

        if (_frameWait > 0)
        {
            _frameWait--;
            return;
        }

        if (_waitUntil >= 0f)
        {
            if (Time.unscaledTime < _waitUntil) return;
            _waitUntil = -1f;
        }

        if (_quitting)
        {
            _done = true;
            Plugin.Logger.LogInfo($"[harness] quit（共截图 {_shotCount} 张）");
            Application.Quit();
            return;
        }

        if (_index >= _lines.Count)
        {
            _done = true;
            Plugin.Logger.LogInfo($"[harness] 脚本执行完毕（共截图 {_shotCount} 张）");
            return;
        }

        string line = _lines[_index++];
        try
        {
            Execute(line);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[harness] 执行 '{line}' 失败: {ex.Message}");
        }
    }

    private void Execute(string line)
    {
        string[] t = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        string cmd = t[0].ToLowerInvariant();
        string arg1 = t.Length > 1 ? t[1] : string.Empty;

        switch (cmd)
        {
            case "wait":
                _waitUntil = Time.unscaledTime + ParseFloat(arg1, 1f);
                break;

            case "clock":
                Ui.UiClock.SetManual(arg1.Equals("manual", StringComparison.OrdinalIgnoreCase));
                Plugin.Logger.LogInfo($"[harness] clock = {(Ui.UiClock.Manual ? "manual" : "auto")}");
                break;

            case "scale":
                Ui.UiClock.Scale = Mathf.Clamp(ParseFloat(arg1, 1f), 0.01f, 4f);
                break;

            case "step":
                Ui.UiClock.Step(ParseFloat(arg1, 0.0333f));
                break;

            case "bg":
                _hooks.Backdrop(ParseBackdrop(arg1));
                break;

            case "hit":
                _hooks.Hit(arg1.Equals("head", StringComparison.OrdinalIgnoreCase));
                break;

            case "kill":
                _hooks.Kill(ParseInt(arg1, 1));
                break;

            case "audio":
                _hooks.Audio(arg1);
                break;

            case "death":
                _hooks.Death(ParseInt(arg1, 1));
                break;

            case "reset":
                _hooks.Reset();
                break;

            case "shot":
                Shot(arg1.Length > 0 ? arg1 : "shot");
                break;

            case "log":
                Plugin.Logger.LogInfo("[harness] " + line.Substring(3).Trim());
                break;

            case "quit":
                _frameWait = 10;       // 给引擎一点时间把最后一张截图写完
                _quitting = true;
                break;

            default:
                Plugin.Logger.LogWarning($"[harness] 未知指令: {line}");
                break;
        }
    }

    private void Shot(string name)
    {
        string path = Path.Combine(_shotDir, name + ".png");
        ScreenCapture.CaptureScreenshot(path);
        _shotCount++;
        _frameWait = 2;   // 引擎在帧尾写盘，等两帧再继续，别让下一条 step 抢在写盘前面
    }

    private static Color? ParseBackdrop(string s)
    {
        switch (s.ToLowerInvariant())
        {
            case "off":
            case "none":
            case "":
                return null;
            case "dark": return new Color(0.078f, 0.094f, 0.110f, 1f);
            case "mid": return new Color(0.5f, 0.5f, 0.5f, 1f);
            case "white": return Color.white;
            case "snow": return new Color(0.84f, 0.88f, 0.92f, 1f);
            case "grass": return new Color(0.275f, 0.376f, 0.204f, 1f);
            default:
                return Ui.UiTheme.Parse(s, new Color(0.5f, 0.5f, 0.5f, 1f));
        }
    }

    private static float ParseFloat(string s, float fallback)
        => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;

    private static int ParseInt(string s, int fallback)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
}
