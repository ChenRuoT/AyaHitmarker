<#
.SYNOPSIS
  给运行中的游戏窗口发键盘 / 鼠标输入，配合 DebugHarness 的遥控模式（watch）做需要交互的进图验证。

.DESCRIPTION
  坐标用「游戏画面坐标」（和 DebugHarness 截图同一套：2560×1600 下左上角 0,0），脚本按窗口客户区自动换算到屏幕坐标，
  所以不用关心窗口位置 / 缩放。进程需要 DPI 感知，否则坐标会被 Windows 虚拟化 —— 脚本开头已经设置。

  动作按顺序执行，每个动作一个参数：
    focus                  把游戏窗口切到前台
    key:F12                按一下键（System.Windows.Forms.Keys 的名字：F1..F12 / Escape / Enter / Tab / A / D1 …）
    keydown:Shift / keyup:Shift
    move:x,y               移动鼠标
    click:x,y              左键单击      rclick:x,y 右键单击      dclick:x,y 左键双击
    drag:x1,y1,x2,y2       左键按下 → 拖动 → 松开
    wheel:x,y,n            在 x,y 处滚轮 n 格（正 = 向上）
    text:abc               逐字输入（字母 / 数字 / # . - 空格）
    wait:0.3               等 0.3 秒

  退出码：0 = 成功，2 = 找不到游戏窗口。

.EXAMPLE
  .\send-input.ps1 focus key:F12 wait:0.5 click:1280,800
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Actions,
    [string]$ProcessName = 'EscapeFromTarkov',
    [int]$FbWidth = 2560,
    [int]$FbHeight = 1600,
    [int]$KeyDelayMs = 30            # 每次按键的按下 / 抬起间隔；游戏在低帧率时调大，否则会丢字符
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Win32Input
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int n);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);

    /// <summary>进程里类名为 UnityWndClass 的可见窗口（MainWindowHandle 可能抓到 BepInEx 控制台）。</summary>
    public static IntPtr FindUnityWindow(uint pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) =>
        {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid || !IsWindowVisible(h)) return true;
            var sb = new System.Text.StringBuilder(64);
            GetClassName(h, sb, 64);
            if (sb.ToString() == "UnityWndClass") { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@
[void][Win32Input]::SetProcessDPIAware()

$proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
$hwnd = if ($proc) { [Win32Input]::FindUnityWindow([uint32]$proc.Id) } else { [IntPtr]::Zero }
if ($hwnd -eq [IntPtr]::Zero) { Write-Error "找不到 $ProcessName 的 Unity 游戏窗口（游戏在运行吗？）" -ErrorAction Continue; exit 2 }

function Get-Client {
    $r = New-Object Win32Input+RECT
    [void][Win32Input]::GetClientRect($hwnd, [ref]$r)
    $p = New-Object Win32Input+POINT
    [void][Win32Input]::ClientToScreen($hwnd, [ref]$p)
    [pscustomobject]@{ X = $p.X; Y = $p.Y; W = $r.Right - $r.Left; H = $r.Bottom - $r.Top }
}

function To-Screen([int]$x, [int]$y) {
    $c = Get-Client
    @([int]($c.X + $x * $c.W / $FbWidth), [int]($c.Y + $y * $c.H / $FbHeight))
}

function Focus-Game {
    [void][Win32Input]::ShowWindow($hwnd, 9)          # SW_RESTORE
    # 前台锁：按一下 Alt 才能让 SetForegroundWindow 生效
    [Win32Input]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
    [void][Win32Input]::SetForegroundWindow($hwnd)
    [Win32Input]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 150
}

function Move-To([int]$x, [int]$y) {
    $s = To-Screen $x $y
    [void][Win32Input]::SetCursorPos($s[0], $s[1])
    Start-Sleep -Milliseconds 40
}

function Mouse([uint32]$flags) { [Win32Input]::mouse_event($flags, 0, 0, 0, [UIntPtr]::Zero) }

function Vk([string]$name) { [byte][int][System.Windows.Forms.Keys]::$name }

function Press([byte]$vk, [bool]$shift = $false) {
    if ($shift) { [Win32Input]::keybd_event(0x10, 0, 0, [UIntPtr]::Zero) }
    [Win32Input]::keybd_event($vk, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds $KeyDelayMs
    [Win32Input]::keybd_event($vk, 0, 2, [UIntPtr]::Zero)
    if ($shift) { [Win32Input]::keybd_event(0x10, 0, 2, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds $KeyDelayMs
}

$LEFT_DOWN = 0x0002; $LEFT_UP = 0x0004; $RIGHT_DOWN = 0x0008; $RIGHT_UP = 0x0010; $WHEEL = 0x0800

foreach ($a in $Actions) {
    $name, $arg = $a -split ':', 2
    # 只有坐标 / 滚轮类动作用得到数字；key:F12、text:abc 这类参数不是数字，解析不了就记 0
    $nums = if ($arg) { @($arg -split ',' | ForEach-Object { $v = 0; if ([int]::TryParse($_, [ref]$v)) { $v } else { 0 } }) } else { @() }
    switch ($name) {
        'focus'   { Focus-Game }
        'key'     { Press (Vk $arg) }
        'keydown' { [Win32Input]::keybd_event((Vk $arg), 0, 0, [UIntPtr]::Zero) }
        'keyup'   { [Win32Input]::keybd_event((Vk $arg), 0, 2, [UIntPtr]::Zero) }
        'move'    { Move-To $nums[0] $nums[1] }
        'click'   { Move-To $nums[0] $nums[1]; Mouse $LEFT_DOWN; Start-Sleep -Milliseconds 50; Mouse $LEFT_UP; Start-Sleep -Milliseconds 80 }
        'rclick'  { Move-To $nums[0] $nums[1]; Mouse $RIGHT_DOWN; Start-Sleep -Milliseconds 50; Mouse $RIGHT_UP; Start-Sleep -Milliseconds 80 }
        'dclick'  {
            Move-To $nums[0] $nums[1]
            1..2 | ForEach-Object { Mouse $LEFT_DOWN; Start-Sleep -Milliseconds 40; Mouse $LEFT_UP; Start-Sleep -Milliseconds 60 }
        }
        'drag'    {
            Move-To $nums[0] $nums[1]; Mouse $LEFT_DOWN; Start-Sleep -Milliseconds 80
            $steps = 12
            for ($i = 1; $i -le $steps; $i++) {
                Move-To ([int]($nums[0] + ($nums[2] - $nums[0]) * $i / $steps)) ([int]($nums[1] + ($nums[3] - $nums[1]) * $i / $steps))
                Start-Sleep -Milliseconds 25
            }
            Mouse $LEFT_UP; Start-Sleep -Milliseconds 80
        }
        'wheel'   { Move-To $nums[0] $nums[1]; [Win32Input]::mouse_event($WHEEL, 0, 0, [int]($nums[2] * 120), [UIntPtr]::Zero); Start-Sleep -Milliseconds 80 }
        'text'    {
            foreach ($ch in $arg.ToCharArray()) {
                if ($ch -match '[a-zA-Z]') { Press (Vk ([string]$ch).ToUpper()) ([char]::IsUpper($ch)) }
                elseif ($ch -match '[0-9]') { Press (Vk ('D' + $ch)) }
                elseif ($ch -eq '#') { Press (Vk 'D3') $true }
                elseif ($ch -eq '.') { Press 0xBE }
                elseif ($ch -eq '-') { Press 0xBD }
                elseif ($ch -eq ' ') { Press 0x20 }
                else { Write-Warning "text: 不支持的字符 '$ch'" }
            }
        }
        'wait'    { Start-Sleep -Milliseconds ([int]([double]$arg * 1000)) }
        default   { Write-Error "未知动作: $a" }
    }
}

$c = Get-Client
Write-Host ("ok: {0} 个动作；游戏客户区 {1}x{2} @ ({3},{4})" -f $Actions.Count, $c.W, $c.H, $c.X, $c.Y)
