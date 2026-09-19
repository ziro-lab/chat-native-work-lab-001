param(
    [Parameter(Mandatory = $true)][string]$Ymm4Dir,
    [Parameter(Mandatory = $true)][string]$OutputDir
)

$ErrorActionPreference = 'Stop'
$OutputDir = [IO.Path]::GetFullPath($OutputDir)
New-Item -ItemType Directory -Force $OutputDir | Out-Null
Get-ChildItem $OutputDir -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

$exe = Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe'
if (-not (Test-Path $exe)) { throw "YMM4 executable not found: $exe" }

Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class CnwlPreviewOverflowWin32 {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
'@

function Get-CnwlWindows {
    $script:rows = @()
    $callback = [CnwlPreviewOverflowWin32+EnumWindowsProc]{
        param([IntPtr]$hWnd, [IntPtr]$lParam)
        if ([CnwlPreviewOverflowWin32]::IsWindowVisible($hWnd)) {
            $pidValue = [uint32]0
            [void][CnwlPreviewOverflowWin32]::GetWindowThreadProcessId($hWnd, [ref]$pidValue)
            $sb = New-Object System.Text.StringBuilder 1024
            [void][CnwlPreviewOverflowWin32]::GetWindowText($hWnd, $sb, $sb.Capacity)
            if (-not [string]::IsNullOrWhiteSpace($sb.ToString())) {
                $script:rows += [pscustomobject]@{ Handle = $hWnd; ProcessId = [int]$pidValue; Title = $sb.ToString() }
            }
        }
        return $true
    }
    [void][CnwlPreviewOverflowWin32]::EnumWindows($callback, [IntPtr]::Zero)
    return $script:rows
}

function Pump-Ymm4Dialogs([int]$ProcessId) {
    foreach ($w in (Get-CnwlWindows)) {
        if ($w.ProcessId -ne $ProcessId) { continue }
        if ($w.Title -like '*Check for updates*' -or $w.Title -like '*About YukkuriMovieMaker*') {
            [void][CnwlPreviewOverflowWin32]::PostMessage($w.Handle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
        } elseif ($w.Title -eq 'Confirm') {
            [void][CnwlPreviewOverflowWin32]::PostMessage($w.Handle, 0x0100, [IntPtr]0x0D, [IntPtr]::Zero)
            [void][CnwlPreviewOverflowWin32]::PostMessage($w.Handle, 0x0101, [IntPtr]0x0D, [IntPtr]::Zero)
        }
    }
}

function Activate-Ymm4([int]$ProcessId) {
    foreach ($w in (Get-CnwlWindows)) {
        if ($w.ProcessId -eq $ProcessId -and $w.Title -like '*YukkuriMovieMaker*') {
            if ([CnwlPreviewOverflowWin32]::SetForegroundWindow($w.Handle)) {
                Start-Sleep -Milliseconds 250
                return $true
            }
        }
    }
    return $false
}

function Wait-File([string]$Path, [System.Diagnostics.Process]$Process, [int]$Seconds) {
    for ($i = 0; $i -lt ($Seconds * 4); $i++) {
        if (Test-Path $Path) { return $true }
        if ($Process.HasExited) { return $false }
        Pump-Ymm4Dialogs $Process.Id
        Start-Sleep -Milliseconds 250
    }
    return (Test-Path $Path)
}

function Send-SpeedUp([System.Diagnostics.Process]$Process) {
    if (-not (Activate-Ymm4 $Process.Id)) { throw 'Could not foreground the YMM4 main window.' }
    [System.Windows.Forms.SendKeys]::SendWait('^.')
    Start-Sleep -Milliseconds 650
}

function Start-ProbePhase([string]$Phase) {
    $env:CNWL_YMM4_PREVIEW_RATE_OVERFLOW_DIR = $OutputDir
    $env:CNWL_YMM4_PREVIEW_RATE_OVERFLOW_PHASE = $Phase
    return Start-Process -FilePath $exe -WorkingDirectory $Ymm4Dir -PassThru
}

$seed = Start-ProbePhase 'seed'
try {
    $validationReady = Join-Path $OutputDir 'input-validation-ready.txt'
    if (-not (Wait-File $validationReady $seed 120)) { throw 'Seed phase did not reach input-validation-ready.' }

    $boundaryReady = Join-Path $OutputDir 'boundary-ready.txt'
    for ($attempt = 0; $attempt -lt 4 -and -not (Test-Path $boundaryReady); $attempt++) {
        Send-SpeedUp $seed
        [void](Wait-File $boundaryReady $seed 2)
    }
    if (-not (Test-Path $boundaryReady)) {
        throw 'Synthetic Ctrl+. route could not prove 126 -> 127; boundary result would be ambiguous.'
    }

    1..3 | ForEach-Object { Send-SpeedUp $seed }
    New-Item -ItemType File -Force (Join-Path $OutputDir 'shortcut-complete.txt') | Out-Null

    $seedResult = Join-Path $OutputDir 'seed-result.txt'
    if (-not (Wait-File $seedResult $seed 30)) { throw 'Seed phase did not produce seed-result.txt.' }
    Get-Content $seedResult
    if (-not (Select-String -Path $seedResult -Pattern '^status=PASS$')) { throw 'Seed observation failed.' }

    if (-not $seed.WaitForExit(15000)) {
        Stop-Process -Id $seed.Id -Force -ErrorAction SilentlyContinue
        throw 'Seed YMM4 did not exit through normal close path.'
    }
}
finally {
    if (-not $seed.HasExited) { Stop-Process -Id $seed.Id -Force -ErrorAction SilentlyContinue }
}

Start-Sleep -Seconds 2

$restart = Start-ProbePhase 'restart'
try {
    $restartResult = Join-Path $OutputDir 'restart-result.txt'
    if (-not (Wait-File $restartResult $restart 120)) { throw 'Restart phase did not produce restart-result.txt.' }
    Get-Content $restartResult
    if (-not (Select-String -Path $restartResult -Pattern '^status=PASS$')) { throw 'Restart observation failed.' }

    if (-not $restart.WaitForExit(15000)) {
        Stop-Process -Id $restart.Id -Force -ErrorAction SilentlyContinue
        throw 'Restart YMM4 did not exit through normal close path.'
    }
}
finally {
    if (-not $restart.HasExited) { Stop-Process -Id $restart.Id -Force -ErrorAction SilentlyContinue }
    Remove-Item Env:CNWL_YMM4_PREVIEW_RATE_OVERFLOW_DIR -ErrorAction SilentlyContinue
    Remove-Item Env:CNWL_YMM4_PREVIEW_RATE_OVERFLOW_PHASE -ErrorAction SilentlyContinue
}

$aggregate = @(
    '=== seed ==='
    (Get-Content (Join-Path $OutputDir 'seed-result.txt'))
    '=== restart ==='
    (Get-Content (Join-Path $OutputDir 'restart-result.txt'))
)
$aggregate | Set-Content -LiteralPath (Join-Path $OutputDir 'result.txt') -Encoding utf8
Get-Content (Join-Path $OutputDir 'result.txt')
