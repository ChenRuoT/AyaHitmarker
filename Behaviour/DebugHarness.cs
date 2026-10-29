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
/// hit armor [N]          # 造一次被护甲挡下、带少量钝伤的命中（身体 3.5 + 护甲 41.2：蓝色辉光 + 钝伤数字，护甲数字在上方）；N = 脚本表目标序号（与 kill N 配对，累加样式下对得上同一个槽）
/// layout line|accumulate # 切换伤害数字排布（会写进 cfg：脚本结尾切回原值）
/// hit plate [N]          # 零钝伤的挡下（只有护甲 41.2：蓝色辉光，只有护甲数字）
/// hit pen [N]            # 击穿护甲（身体 52 + 护甲 41.2：普通标记，护甲数字在身体数字上方）
/// death 3                # 模拟「被脚本表第 3 条的那个人打死」（之后杀他 = 复仇者）
/// waitmenu [秒]          # 本进程首次运行时等主菜单场景（MenuUIScene）出现，之后各代直接返回
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
/// preset <Id>|restore   # 套用一套预设（None|Default|Legacy|Minimal|HighContrast|Tactical|…）；会写 cfg，第一次套用前记下当前值，脚本结尾必须 `preset restore`
/// summary [show|end|hide] # 战局小结：show（默认）= 立即显示当前累计；end = 走真实的「战局结束 → 等主菜单 → 延迟」调度路径；hide = 收起
/// lang zh|en|auto        # 强制界面语言（不写 cfg；auto = 恢复跟随设置 / 游戏语言）。屏幕文字从下一次击杀起切换
/// glyphs 击杀爆头         # 检查这些字符在当前 UI 字体（含回退链）里有没有字形，缺的会渲染成方块
/// watch                  # 脚本最后一条：不退出，持续监听 &lt;shots&gt;/cmd.txt，读到就把里面的指令追加执行并删掉文件（遥控模式）
/// log 文字               # 往日志里写一行（脚本里做标记用）
/// quit                   # 退出游戏
/// </code>
/// 截图用 <c>ScreenCapture.CaptureScreenshot</c>，由引擎在帧尾写文件：不依赖窗口焦点，
/// 也不会被 Windows DPI 虚拟化坑（README §11）。输出目录取环境变量 <c>AYA_DEBUG_SHOTS</c>，
/// 缺省为系统临时目录下的 <c>AyaHitmarker_shots</c>（**不要指向 BepInEx/scripts/ 内部** ——
/// 热重载时引擎会盯着那里，创建截图目录会多触发一次重载）。
/// </summary>
internal sealed class DebugHarness
{
    /// <summary>调试命中的护甲变体（F8 随机命中与 <c>hit</c> 指令共用）。</summary>
    internal enum HitVariant
    {
        Plain,
        /// <summary>被挡下，带少量钝伤（实测最常见的挡下形态）。</summary>
        Blocked,
        /// <summary>被挡下，钝伤为 0（只有板阶段事件、没有后续）。</summary>
        BlockedNoBlunt,
        /// <summary>击穿护甲：身体伤害 + 护甲耐久伤害。</summary>
        Penetrated,
    }

    internal sealed class Hooks
    {
        internal Action ProbeXp = () => { };
        /// <summary>命中：headshot / 护甲命中变体 / 目标序号（与 <c>kill</c> 共用，累加样式下用来对上同一个目标）。</summary>
        internal Action<bool, HitVariant, int> Hit = (_, _, _) => { };

        /// <summary>受击方向（0.6 e）：方位角度（0 = 正前，+ = 右，180 = 背后）/ 伤害 / 是否护甲吸收。</summary>
        internal Action<float, float, bool> Taken = (_, _, _) => { };

        /// <summary>助攻（0.6 c）：测试击杀表序号 / 我累计造成的伤害。</summary>
        internal Action<int, float> Assist = (_, _) => { };

        internal Action<int> Kill = _ => { };
        internal Action<int> Death = _ => { };
        internal Action<string> Audio = _ => { };

        /// <summary>预设（0.7.0）：预设 id 或 restore。</summary>
        internal Action<string> Preset = _ => { };

        /// <summary>战局小结（0.7.0）：show / end / hide。</summary>
        internal Action<string> Summary = _ => { };

        /// <summary>强制界面语言（0.7.0）：zh / en / auto。</summary>
        internal Action<string> SetLang = _ => { };
        internal Action Reset = () => { };
        internal Action<Color?> Backdrop = _ => { };

        /// <summary>「主菜单已就绪」判据（<c>waitmenu</c> 用）。默认 true，方便单独测试脚本解析。</summary>
        internal Func<bool> MenuReady = () => true;
    }

    private readonly Hooks _hooks;
    private readonly List<string> _lines = new List<string>();
    private readonly string _shotDir;

    /// <summary>「本进程已经过 waitmenu 启动等待」的标记。带进程 id：新开一局会重新等主菜单，热重载后跳过。</summary>
    private readonly string _bootMarker;

    private int _index;
    private int _frameWait;
    private float _waitUntil = -1f;
    private bool _waitingMenu;
    private float _menuDeadline = -1f;
    private bool _done;
    private int _shotCount;
    private bool _quitting;
    private bool _watching;
    private int _watchFrame;

    internal static DebugHarness? TryCreate(Hooks hooks)
    {
        string? path = Environment.GetEnvironmentVariable("AYA_DEBUG_SCRIPT");
        if (string.IsNullOrWhiteSpace(path)) return null;

        if (!File.Exists(path))
        {
            Plugin.Logger.LogWarning($"[harness] AYA_DEBUG_SCRIPT 指向的文件不存在: {path}");
            return null;
        }

        string dir = Environment.GetEnvironmentVariable("AYA_DEBUG_SHOTS")
                     ?? Path.Combine(Path.GetTempPath(), "AyaHitmarker_shots");
        return new DebugHarness(hooks, File.ReadAllLines(path), dir);
    }

    private DebugHarness(Hooks hooks, string[] lines, string shotDir)
    {
        _hooks = hooks;
        _shotDir = shotDir;
        _bootMarker = Path.Combine(shotDir, $".booted-{Environment.ProcessId}");
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

        if (_waitingMenu)
        {
            if (!_hooks.MenuReady() && Time.unscaledTime < _menuDeadline) return;

            bool timedOut = !_hooks.MenuReady();
            _waitingMenu = false;
            MarkBooted();
            if (timedOut) Plugin.Logger.LogWarning("[harness] waitmenu 超时（主菜单场景未出现），继续执行");
            else Plugin.Logger.LogInfo("[harness] waitmenu 结束（主菜单已就绪）");
        }

        if (_quitting)
        {
            _done = true;
            Plugin.Logger.LogInfo($"[harness] quit（共截图 {_shotCount} 张）");
            Application.Quit();
            return;
        }

        if (_watching && _index >= _lines.Count) PollRemoteCommands();

        if (_index >= _lines.Count)
        {
            if (_watching) return;   // 遥控模式：脚本跑完也不结束，继续等 cmd.txt

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

            case "waitmenu":
                if (File.Exists(_bootMarker))
                {
                    Plugin.Logger.LogInfo("[harness] waitmenu：本进程已经过启动等待，直接继续");
                    break;
                }
                _menuDeadline = Time.unscaledTime + ParseFloat(arg1, 90f);
                _waitingMenu = !_hooks.MenuReady();
                if (_waitingMenu) Plugin.Logger.LogInfo("[harness] 等待主菜单场景（MenuUIScene）…");
                else MarkBooted();
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

            case "layout":
                // layout line | accumulate：切换伤害数字排布（会写进 cfg，脚本结尾记得切回去）
                Plugin.Cfg.DamageTextLayout.Value = arg1.StartsWith("acc", StringComparison.OrdinalIgnoreCase)
                    ? DamageNumberLayout.Accumulate
                    : DamageNumberLayout.LineRight;
                break;

            case "hit":
            {
                // hit                → 普通命中（目标 1）
                // hit head           → 爆头
                // hit armor          → 被护甲挡下（带少量钝伤）
                // hit plate          → 零钝伤的挡下（只有护甲伤害）
                // hit pen            → 击穿护甲（身体伤害 + 护甲伤害）
                // hit head 3 / hit armor 3 / hit 3 → 指定测试击杀表里的目标序号（和 kill N 配对，累加样式下会对上同一个槽）
                bool head = arg1.Equals("head", StringComparison.OrdinalIgnoreCase);
                HitVariant variant = arg1.Equals("armor", StringComparison.OrdinalIgnoreCase) ? HitVariant.Blocked
                    : arg1.Equals("plate", StringComparison.OrdinalIgnoreCase) ? HitVariant.BlockedNoBlunt
                    : arg1.Equals("pen", StringComparison.OrdinalIgnoreCase) ? HitVariant.Penetrated
                    : HitVariant.Plain;
                int target = head || variant != HitVariant.Plain ? ParseInt(t.Length > 2 ? t[2] : string.Empty, 1) : ParseInt(arg1, 1);
                _hooks.Hit(head, variant, target);
                break;
            }

            case "taken":
            {
                // taken <角度°> [伤害] [armor]
                // taken 0            → 正前方、60 伤害、普通颜色
                // taken 180 80       → 背后、80 伤害
                // taken 90 30 armor  → 右侧、护甲吸收的钝伤（白色）
                float degrees = ParseFloat(arg1, 0f);
                float damage = ParseFloat(t.Length > 2 ? t[2] : string.Empty, 60f);
                bool armorTaken = t.Length > 3 && t[3].Equals("armor", StringComparison.OrdinalIgnoreCase);
                _hooks.Taken(degrees, damage, armorTaken);
                break;
            }

            case "kill":
                _hooks.Kill(ParseInt(arg1, 1));
                break;

            case "assist":
            {
                // assist <测试击杀表序号> [伤害]
                // assist 1 120   → 用第 1 条测试击杀的受害者名字 / 阵营，伤害量 120
                _hooks.Assist(ParseInt(arg1, 1), ParseFloat(t.Length > 2 ? t[2] : string.Empty, 100f));
                break;
            }

            case "xp":
                _hooks.ProbeXp();
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

            case "lang":
                _hooks.SetLang(arg1);
                break;

            case "summary":
                _hooks.Summary(arg1);
                break;

            case "preset":
                _hooks.Preset(arg1);
                break;

            case "shot":
                Shot(arg1.Length > 0 ? arg1 : "shot");
                break;

            case "log":
                Plugin.Logger.LogInfo("[harness] " + line.Substring(3).Trim());
                break;

            case "watch":
                _watching = true;
                Plugin.Logger.LogInfo($"[harness] 遥控模式：往 {Path.Combine(_shotDir, "cmd.txt")} 写指令即可（读后即删）");
                break;

            case "glyphs":
                CheckGlyphs(line.Substring(6).Trim());
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

    /// <summary>
    /// 检查一批字符在当前 UI 字体（含回退链、允许动态补字）里是否都有字形，缺的会渲染成方块。
    /// 比肉眼看截图可靠：一次覆盖屏上可能出现的全部字符。
    /// </summary>
    private static void CheckGlyphs(string chars)
    {
        var font = Ui.UiTheme.Font;
        if (font == null)
        {
            Plugin.Logger.LogWarning("[harness] glyphs: UI 字体不可用（font=MISSING）");
            return;
        }

        var missing = new System.Text.StringBuilder();
        int checkedCount = 0;
        foreach (char c in chars)
        {
            if (char.IsWhiteSpace(c)) continue;
            checkedCount++;
            if (!font.HasCharacter(c, true, true)) missing.Append(c);
        }

        Plugin.Logger.LogInfo($"[harness] glyphs: font='{font.name}' 检查 {checkedCount} 个字符，缺字形 {missing.Length} 个 [{missing}]");
    }

    /// <summary>
    /// 遥控模式：每隔几帧看一眼 <c>&lt;shots&gt;/cmd.txt</c>，有就读出来追加到指令队列并删掉。
    /// 用途：需要「先给游戏发键鼠输入、再看一眼画面」的交互式验证（比如设置面板），脚本没法事先排好时序。
    /// </summary>
    private void PollRemoteCommands()
    {
        if (++_watchFrame % 6 != 0) return;

        string path = Path.Combine(_shotDir, "cmd.txt");
        try
        {
            if (!File.Exists(path)) return;

            string[] lines = File.ReadAllLines(path);
            File.Delete(path);

            int before = _lines.Count;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                ExpandInto(line);
            }
            Plugin.Logger.LogInfo($"[harness] 收到遥控指令 {_lines.Count - before} 条");
        }
        catch (Exception ex)
        {
            // 写入方还没写完 / 文件被占用：下次再读
            Plugin.Logger.LogDebug($"[harness] 读取 cmd.txt 失败: {ex.Message}");
        }
    }

    private void Shot(string name)
    {
        string path = Path.Combine(_shotDir, name + ".png");
        ScreenCapture.CaptureScreenshot(path);
        _shotCount++;
        _frameWait = 2;   // 引擎在帧尾写盘，等两帧再继续，别让下一条 step 抢在写盘前面
    }

    /// <summary>
    /// 记录「本进程已经过 waitmenu 启动等待」。
    /// 冷启动的脚本要等主菜单加载完才能截图；热重载后同一进程已经在菜单里，直接跳过。
    /// 文件名带进程 id，所以新开一局游戏会重新等待。
    /// </summary>
    private void MarkBooted()
    {
        try
        {
            File.WriteAllText(_bootMarker, DateTime.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[harness] 写启动标记失败 {_bootMarker}: {ex.Message}");
        }
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
