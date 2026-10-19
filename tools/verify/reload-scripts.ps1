<#
.SYNOPSIS
  让运行中的游戏通过 ScriptEngine.IL2CPP 重载 BepInEx/scripts/ 下的插件，并打印结果摘要。

.DESCRIPTION
  只走引擎的「命令通道」（纯文件，不开端口）：往
    <游戏>/BepInEx/ScriptEngine.IL2CPP/commands/<id>.reload
  放一个命令文件，等引擎写出 <id>.result.json，解析后打印 generation / loaded / errors。

  正常情况下不需要它：文件监视默认开启，dotnet build 写进 scripts/ 后 1 秒左右会自动重载。
  这个脚本用于：强制重载一次、关掉 AutoReload 时、或者只想看 status。

  退出码：0 = 成功，1 = 引擎报告失败，2 = 超时（游戏没开 / 引擎没装 / 命令通道关了）。

.EXAMPLE
  .\reload-scripts.ps1                  # 强制重载
  .\reload-scripts.ps1 -Command status  # 只看当前 generation / 已加载插件
  .\reload-scripts.ps1 -Command unload  # 卸载所有脚本
#>
param(
    [ValidateSet('reload', 'unload', 'status')][string]$Command = 'reload',
    [string]$GameDir,
    [int]$TimeoutSec = 60
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($GameDir)) { $GameDir = $env:SE_GAME_DIR }

if ([string]::IsNullOrWhiteSpace($GameDir)) {
    # 从构建配置里取 GameDir（Directory.Build.local.props > 环境变量 > 默认值都由 MSBuild 解析）
    $repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $csproj = Join-Path $repo 'AyaHitmarker.csproj'
    $resolved = & dotnet msbuild $csproj -getProperty:GameDir -nologo 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($resolved)) { $GameDir = $resolved.Trim() }
}

if ([string]::IsNullOrWhiteSpace($GameDir)) {
    throw '没给游戏根目录：传 -GameDir，或设环境变量 SE_GAME_DIR，或在 Directory.Build.local.props 里设 GameDir'
}

$commandsDir = Join-Path $GameDir 'BepInEx\ScriptEngine.IL2CPP\commands'
New-Item -ItemType Directory -Force $commandsDir | Out-Null

$id = [guid]::NewGuid().ToString('N').Substring(0, 8)
$resultPath = Join-Path $commandsDir "$id.result.json"
Set-Content -Path (Join-Path $commandsDir "$id.$Command") -Value $Command -NoNewline -Encoding UTF8

$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSec)
while ([DateTime]::UtcNow -lt $deadline) {
    if (Test-Path $resultPath) {
        $json = Get-Content $resultPath -Raw -Encoding UTF8
        Remove-Item $resultPath -ErrorAction SilentlyContinue

        try { $result = $json | ConvertFrom-Json }
        catch { throw "结果 JSON 解析失败：$json" }

        # 命令结果顶层：id / command / ok / generation / plugins / report。
        # (lastReload / engine 只存在于 status.json，不要在这里读。)
        $report = $result.report
        $plugins = @($result.plugins)
        if ($null -ne $report) {
            $loaded = @($report.loaded)
            $unloaded = @($report.unloaded)
            $warnings = @($report.warnings)
            $errors = @($report.errors)
            $phase = if ($report.skipped) { ' skipped' } elseif ($report.deferred) { ' deferred' } else { '' }
            Write-Host ("ScriptEngine.IL2CPP | {0} ok={1} generation={2} trigger={3}{4} duration={5}ms loaded={6} unloaded={7} errors={8}" -f `
                $result.command, $result.ok, $result.generation, $report.trigger, $phase, $report.durationMs, $loaded.Count, $unloaded.Count, $errors.Count)
            if ($null -ne $result.message) { Write-Host "  message: $($result.message)" }
            foreach ($p in $loaded) { Write-Host ("  loaded: {0} {1} ({2})" -f $p.name, $p.version, $p.guid) }
            foreach ($w in $warnings) { Write-Host "  warning: $w" }
            foreach ($e in $errors) { Write-Host "  error: $e" }
        }
        else {
            Write-Host ("ScriptEngine.IL2CPP | {0} ok={1} generation={2}" -f $result.command, $result.ok, $result.generation)
            if ($null -ne $result.message) { Write-Host "  message: $($result.message)" }
            foreach ($p in $plugins) { Write-Host ("  loaded: {0} {1} ({2})" -f $p.name, $p.version, $p.guid) }
        }

        if ($result.ok) { exit 0 }
        exit 1
    }
    Start-Sleep -Milliseconds 200
}

$status = Join-Path $GameDir 'BepInEx\ScriptEngine.IL2CPP\status.json'
Write-Error ("超时：{0} 秒内没有收到结果。游戏在运行吗？ScriptEngine.IL2CPP 装在 BepInEx\plugins 里吗？" -f $TimeoutSec) `
    -ErrorAction Continue
if (Test-Path $status) { Write-Host "（引擎上次写出的 status.json：$status）" }
exit 2
