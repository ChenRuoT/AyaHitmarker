<#
.SYNOPSIS
  启动游戏并执行 DebugHarness 脚本，脚本以 `quit` 结尾，游戏自己退出。

.DESCRIPTION
  不用手按键、不用抢窗口焦点：插件读环境变量 AYA_DEBUG_SCRIPT 指向的脚本，
  手动步进 UI 时钟，用 ScreenCapture 在每一帧截图（指令清单见 Behaviour/DebugHarness.cs 的类注释）。

  前提：
    * SPT 服务端已经在监听 6969（SPT_Runtime\SPT.Server.exe）；
    * 用 SPT Launcher 启动过至少一次游戏（启动参数里的 token 取自 Launcher.log，和 launcher 用同一串）；
    * 游戏当前没在运行（DLL 被锁时 dotnet build 也会失败；这里也不会去动你正在玩的那一局）。

  超时则强杀 —— 只杀本脚本自己启动的那个进程。

.EXAMPLE
  .\run_game.ps1 -Script .\kill-card.txt -ShotsDir D:\shots\v1
#>
param(
    [Parameter(Mandatory = $true)][string]$Script,
    [Parameter(Mandatory = $true)][string]$ShotsDir,
    [string]$GameDir = 'D:\EFTdev\Client.1.2.0.0.47965',
    [int]$TimeoutSec = 420
)

$ErrorActionPreference = 'Stop'

if (Get-Process EscapeFromTarkov -ErrorAction SilentlyContinue) {
    throw '游戏已经在运行：先退出它（脚本不会去动正在玩的那一局）'
}

if (-not (Get-NetTCPConnection -LocalPort 6969 -State Listen -ErrorAction SilentlyContinue)) {
    throw 'SPT 服务端没有在监听 6969：先启动 SPT_Runtime\SPT.Server.exe'
}

$launcherLog = Join-Path $GameDir 'SPT_Runtime\user\logs\Launcher.log'
$line = Select-String -Path $launcherLog -Pattern 'args: .*-token=([0-9a-fA-F]+)' | Select-Object -Last 1
if (-not $line) { throw "在 $launcherLog 里找不到 -token=…（先用 SPT Launcher 启动过一次游戏）" }
$token = $line.Matches[0].Groups[1].Value

New-Item -ItemType Directory -Force $ShotsDir | Out-Null
$env:AYA_DEBUG_SCRIPT = (Resolve-Path $Script).Path
$env:AYA_DEBUG_SHOTS = (Resolve-Path $ShotsDir).Path

# 注意：-config 里的 JSON 用单引号，双引号会被 Windows 命令行吃掉
$gameArgs = "-force-gfx-jobs native -token=$token -config={'BackendUrl':'https://127.0.0.1:6969','Version':'live','MatchingVersion':'live'}"
$p = Start-Process -FilePath (Join-Path $GameDir 'EscapeFromTarkov.exe') -WorkingDirectory $GameDir -ArgumentList $gameArgs -PassThru
Write-Host "launched pid=$($p.Id) script=$env:AYA_DEBUG_SCRIPT shots=$env:AYA_DEBUG_SHOTS"

if (-not $p.WaitForExit($TimeoutSec * 1000)) {
    Write-Host "TIMEOUT after $TimeoutSec s -> killing pid $($p.Id)"
    Stop-Process -Id $p.Id -Force
    exit 2
}
Write-Host "game exited, code=$($p.ExitCode)"
