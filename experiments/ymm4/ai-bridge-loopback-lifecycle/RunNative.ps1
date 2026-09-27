param(
    [Parameter(Mandatory = $true)][string]$Ymm4Dir,
    [Parameter(Mandatory = $true)][string]$OutputDir
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null
Get-ChildItem $OutputDir -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

$exe = Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe'
$pluginDll = Join-Path $Ymm4Dir 'user\plugin\Ymm4AiBridgeLoopbackProbe\Ymm4AiBridgeLoopbackProbe.dll'
if (-not (Test-Path $exe)) { throw "YMM4 executable not found: $exe" }
if (-not (Test-Path $pluginDll)) { throw "Probe DLL not found: $pluginDll" }

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class CnwlAiBridgeWin32 {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
'@

function Get-CnwlWindows {
    $script:rows = @()
    $callback = [CnwlAiBridgeWin32+EnumWindowsProc]{
        param([IntPtr]$hWnd, [IntPtr]$lParam)
        if ([CnwlAiBridgeWin32]::IsWindowVisible($hWnd)) {
            $sb = New-Object System.Text.StringBuilder 1024
            [void][CnwlAiBridgeWin32]::GetWindowText($hWnd, $sb, $sb.Capacity)
            $title = $sb.ToString()
            if (-not [string]::IsNullOrWhiteSpace($title)) { $script:rows += [pscustomobject]@{ Handle = $hWnd; Title = $title } }
        }
        return $true
    }
    [void][CnwlAiBridgeWin32]::EnumWindows($callback, [IntPtr]::Zero)
    return $script:rows
}

function Get-Value([string]$Path, [string]$Key) {
    $line = Get-Content $Path | Where-Object { $_ -like "$Key=*" } | Select-Object -First 1
    if (-not $line) { return $null }
    return $line.Substring($Key.Length + 1)
}

function Invoke-Ping([int]$Port) {
    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $client.Connect('127.0.0.1', $Port)
        $stream = $client.GetStream()
        $writer = [System.IO.StreamWriter]::new($stream, [System.Text.UTF8Encoding]::new($false))
        $reader = [System.IO.StreamReader]::new($stream, [System.Text.UTF8Encoding]::new($false))
        $writer.NewLine = [Environment]::NewLine
        $writer.AutoFlush = $true
        $writer.WriteLine('PING')
        return $reader.ReadLine()
    } finally { $client.Dispose() }
}

function Test-ConnectFails([int]$Port) {
    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $task = $client.ConnectAsync('127.0.0.1', $Port)
        if (-not $task.Wait(1500)) { return $true }
        return -not $client.Connected
    } catch { return $true } finally { $client.Dispose() }
}

$startup = Join-Path $OutputDir 'startup.txt'
$shutdown = Join-Path $OutputDir 'shutdown.txt'
$resultPath = Join-Path $OutputDir 'result.txt'
$windowsLog = Join-Path $OutputDir 'windows-seen.txt'

$env:CNWL_YMM4_AI_BRIDGE_OUTPUT = $OutputDir
$p = Start-Process -FilePath $exe -WorkingDirectory $Ymm4Dir -PassThru
$mainHandle = [IntPtr]::Zero
$startupSeen = $false

try {
    for ($i = 0; $i -lt 120; $i++) {
        if (Test-Path $startup) { $startupSeen = $true }
        foreach ($w in (Get-CnwlWindows)) {
            "tick=$i handle=$($w.Handle) title=$($w.Title)" | Add-Content $windowsLog
            if ($w.Title -like '*Check for updates*' -or $w.Title -like '*About YukkuriMovieMaker*') {
                [void][CnwlAiBridgeWin32]::PostMessage($w.Handle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
            } elseif ($w.Title -eq 'Confirm') {
                [void][CnwlAiBridgeWin32]::PostMessage($w.Handle, 0x0100, [IntPtr]0x0D, [IntPtr]::Zero)
                [void][CnwlAiBridgeWin32]::PostMessage($w.Handle, 0x0101, [IntPtr]0x0D, [IntPtr]::Zero)
            } elseif ($w.Title -like '*YukkuriMovieMaker v4.56.1.0 Lite*' -and $w.Title -notlike '*Starting*') {
                $mainHandle = $w.Handle
            }
        }
        if ($startupSeen -and $mainHandle -ne [IntPtr]::Zero) { break }
        if ($p.HasExited) { break }
        Start-Sleep -Seconds 1
    }

    if (-not $startupSeen) { throw 'Probe did not produce startup evidence.' }
    $address = Get-Value $startup 'address'
    $portText = Get-Value $startup 'port'
    $startCount = Get-Value $startup 'listener_start_count'
    $appPresent = Get-Value $startup 'application_present'
    if ($address -ne "127.0.0.1") { throw "Listener was not loopback-only: $address" }
    if ($startCount -ne "1") { throw "Listener start count was not 1: $startCount" }
    if ($appPresent -ne "True") { throw "WPF Application.Current was not available: $appPresent" }

    $port = [int]$portText
    $response = Invoke-Ping $port
    if ($response -ne "PONG") { throw "Unexpected probe response: $response" }

    if ($mainHandle -eq [IntPtr]::Zero) { throw 'YMM4 main window was not observed.' }
    [void][CnwlAiBridgeWin32]::PostMessage($mainHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)

    for ($i = 0; $i -lt 30 -and -not $p.HasExited; $i++) {
        foreach ($w in (Get-CnwlWindows)) {
            if ($w.Title -eq 'Confirm') {
                [void][CnwlAiBridgeWin32]::PostMessage($w.Handle, 0x0100, [IntPtr]0x0D, [IntPtr]::Zero)
                [void][CnwlAiBridgeWin32]::PostMessage($w.Handle, 0x0101, [IntPtr]0x0D, [IntPtr]::Zero)
            }
        }
        Start-Sleep -Milliseconds 500
    }

    if (-not $p.HasExited) { throw 'YMM4 did not exit through the normal window-close route.' }
    for ($i = 0; $i -lt 20 -and -not (Test-Path $shutdown); $i++) { Start-Sleep -Milliseconds 250 }
    if (-not (Test-Path $shutdown)) { throw 'Shutdown marker was not produced.' }

    $reason = Get-Value $shutdown 'reason'
    $shutdownStarts = Get-Value $shutdown 'listener_start_count'
    $portClosed = Test-ConnectFails $port
    if (-not $portClosed) { throw 'Listener still accepted connections after YMM4 shutdown.' }

    @(
        'status=PASS'
        "startup_seen=$startupSeen"
        "address=$address"
        "port=$port"
        "listener_start_count=$startCount"
        "ping_response=$response"
        "graceful_process_exit=$($p.HasExited)"
        "shutdown_reason=$reason"
        "shutdown_listener_start_count=$shutdownStarts"
        "port_closed_after_exit=$portClosed"
    ) | Set-Content $resultPath
} finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Remove-Item Env:CNWL_YMM4_AI_BRIDGE_OUTPUT -ErrorAction SilentlyContinue
}
