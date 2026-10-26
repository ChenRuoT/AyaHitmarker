using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AyaHitmarker.Game;
using BepInEx.Configuration;

namespace AyaHitmarker;

/// <summary>
/// 把一套 <see cref="Preset"/> 写进 BepInEx 配置（0.7.0）。
///
/// 流程：① 备份 cfg → ② 关闭「每改一项写一次盘」→ ③ 把 <see cref="Presets.UnionKeys"/> 全部复位到默认
/// → ④ 写入该预设的值 → ⑤ 恢复开关并存盘一次 → ⑥ 日志。
/// 值走 <c>SetSerializedValue(string)</c>（TOML 文本），不用 <c>BoxedValue = (object)</c>：后者要求装箱类型与条目类型严格一致，
/// 一个 <c>int</c> 写进 <c>float</c> 条目就是 <c>InvalidCastException</c>。写入时 BepInEx 会按声明的范围静默钳制，
/// 所以写完回读对比，被钳制的计数并打日志（离线测试 <c>PresetFacts</c> 已经保证内置预设不会越界，这里是保险）。
///
/// **备份**：每次套用前把 cfg 复制成 <c>com.aya.ayahitmarker.cfg.bak-preset-时间戳</c>，只保留最近 <see cref="BackupsToKeep"/> 份。
/// 用户在一个会话里连着试好几套预设，最早那份（= 自己调好的状态）也不会被后面的覆盖掉。
/// </summary>
internal static class PresetApplier
{
    internal const int BackupsToKeep = 5;
    private const string BackupMarker = ".bak-preset-";

    internal readonly record struct Result(string Id, int Reset, int Written, int Clamped, int Unknown, string? BackupPath);

    /// <summary>必须在主线程调用（会写配置文件，也会触发条目的 SettingChanged）。</summary>
    internal static Result Apply(Preset preset, ConfigFile file)
    {
        string? backup = TryBackup(file);

        int reset = 0, written = 0, clamped = 0, unknown = 0;
        bool oldSave = file.SaveOnConfigSet;
        file.SaveOnConfigSet = false;   // 批量改完再存一次，不要每改一项写一次盘
        try
        {
            foreach (string key in Presets.UnionKeys)
            {
                if (!ConfigPanel.Entries.TryGetValue(key, out var entry))
                {
                    unknown++;
                    Plugin.Logger.LogWarning($"[preset] 预设并集里的键 '{key}' 在配置里找不到，跳过（离线测试本该挡住这种情况）");
                    continue;
                }
                entry.BoxedValue = entry.DefaultValue;
                reset++;
            }

            foreach (var (key, value) in preset.Overrides)
            {
                if (!ConfigPanel.Entries.TryGetValue(key, out var entry))
                {
                    unknown++;
                    continue;
                }

                try
                {
                    entry.SetSerializedValue(value);
                    written++;
                    string actual = entry.GetSerializedValue();
                    if (!SameSerialized(value, actual))
                    {
                        clamped++;
                        Plugin.Logger.LogWarning($"[preset] {preset.Id}.{key}: 写入 {value}，回读 {actual}（被范围钳制了）");
                    }
                }
                catch (Exception ex)
                {
                    unknown++;
                    Plugin.Logger.LogWarning($"[preset] {preset.Id}.{key}={value} 写入失败: {ex.Message}");
                }
            }
        }
        finally
        {
            file.SaveOnConfigSet = oldSave;
        }

        file.Save();

        var result = new Result(preset.Id, reset, written, clamped, unknown, backup);
        Plugin.Logger.LogInfo(
            $"[preset] 应用 {preset.Id}（{preset.NameZh}）：复位 {reset} 个，写入 {written} 个，被钳制 {clamped} 个，失败/未知 {unknown} 个；" +
            $"备份={(backup ?? "(未备份)")}");
        return result;
    }

    /// <summary>
    /// 给调试脚本用：记下并集里所有键当前的值，之后 <see cref="Restore"/> 原样写回。
    /// 验收脚本会连着套用好几套预设，跑完要把开发机上的 cfg 还原，否则就把用户自己的设置改掉了。
    /// </summary>
    internal static Dictionary<string, string> Snapshot()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string key in Presets.UnionKeys)
        {
            if (ConfigPanel.Entries.TryGetValue(key, out var entry)) map[key] = entry.GetSerializedValue();
        }
        return map;
    }

    internal static int Restore(Dictionary<string, string> snapshot, ConfigFile file)
    {
        int restored = 0;
        bool oldSave = file.SaveOnConfigSet;
        file.SaveOnConfigSet = false;
        try
        {
            foreach (var (key, value) in snapshot)
            {
                if (!ConfigPanel.Entries.TryGetValue(key, out var entry)) continue;
                entry.SetSerializedValue(value);
                restored++;
            }
        }
        finally
        {
            file.SaveOnConfigSet = oldSave;
        }
        file.Save();
        return restored;
    }

    /// <summary>写入值与回读值是否等价：字符串不分大小写；两边都能解析成数字就按数值比（"2" 与 "2.0"、"0.20" 与 "0.2"）。</summary>
    internal static bool SameSerialized(string requested, string actual)
    {
        if (string.Equals(requested, actual, StringComparison.OrdinalIgnoreCase)) return true;
        return double.TryParse(requested, NumberStyles.Float, CultureInfo.InvariantCulture, out double a)
               && double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out double b)
               && Math.Abs(a - b) < 1e-4;
    }

    private static string? TryBackup(ConfigFile file)
    {
        try
        {
            string path = file.ConfigFilePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            string backup = path + BackupMarker + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            File.Copy(path, backup, overwrite: true);
            PruneBackups(path);
            return backup;
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[preset] 备份 cfg 失败（继续套用）: {ex.Message}");
            return null;
        }
    }

    /// <summary>只删我们自己建的 <c>.bak-preset-*</c>，按文件名（= 时间）保留最新的 N 份。</summary>
    private static void PruneBackups(string cfgPath)
    {
        string? dir = Path.GetDirectoryName(cfgPath);
        if (dir == null) return;

        string[] backups = Directory.GetFiles(dir, Path.GetFileName(cfgPath) + BackupMarker + "*");
        if (backups.Length <= BackupsToKeep) return;

        Array.Sort(backups, StringComparer.Ordinal);   // 时间戳是定宽的，字典序 = 时间序
        for (int i = 0; i < backups.Length - BackupsToKeep; i++)
        {
            try { File.Delete(backups[i]); }
            catch { /* 删不掉就留着，下次再说 */ }
        }
    }
}
