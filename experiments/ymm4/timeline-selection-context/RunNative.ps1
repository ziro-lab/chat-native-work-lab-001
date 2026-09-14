param(
    [Parameter(Mandatory = $true)][string]$Ymm4Dir,
    [Parameter(Mandatory = $true)][string]$OutputDir
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$resultPath = Join-Path $OutputDir 'result.txt'
$behaviorPath = Join-Path $OutputDir 'behavior-result.txt'
$hostLog = Join-Path $OutputDir 'host-log.txt'
Remove-Item $resultPath,$behaviorPath,$hostLog -Force -ErrorAction SilentlyContinue

$exe = Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe'
$pluginDll = Join-Path $Ymm4Dir 'user\plugin\Ymm4SelectionProbe\Ymm4SelectionProbe.dll'
if (-not (Test-Path $exe)) { throw "YMM4 executable not found: $exe" }
if (-not (Test-Path $pluginDll)) { throw "Probe DLL not found: $pluginDll" }

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class CnwlSelectionWin32 {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
'@

function Get-CnwlWindows {
    $script:rows = @()
    $callback = [CnwlSelectionWin32+EnumWindowsProc]{
        param([IntPtr]$hWnd, [IntPtr]$lParam)
        if ([CnwlSelectionWin32]::IsWindowVisible($hWnd)) {
            $sb = New-Object System.Text.StringBuilder 1024
            [void][CnwlSelectionWin32]::GetWindowText($hWnd, $sb, $sb.Capacity)
            $title = $sb.ToString()
            if (-not [string]::IsNullOrWhiteSpace($title)) {
                $script:rows += [pscustomobject]@{ Handle = $hWnd; Title = $title }
            }
        }
        return $true
    }
    [void][CnwlSelectionWin32]::EnumWindows($callback, [IntPtr]::Zero)
    return $script:rows
}

$env:CNWL_YMM4_SELECTION_DIR = $OutputDir
$p = Start-Process -FilePath $exe -WorkingDirectory $Ymm4Dir -PassThru

try {
    for ($i = 0; $i -lt 120; $i++) {
        if ((Test-Path $resultPath) -and (Test-Path $behaviorPath)) { break }
        if ($p.HasExited) { break }

        foreach ($w in (Get-CnwlWindows)) {
            "tick=$i handle=$($w.Handle) title=$($w.Title)" | Add-Content $hostLog
            if ($w.Title -like '*Check for updates*' -or $w.Title -like '*About YukkuriMovieMaker*') {
                [void][CnwlSelectionWin32]::PostMessage($w.Handle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
            } elseif ($w.Title -eq 'Confirm') {
                [void][CnwlSelectionWin32]::PostMessage($w.Handle, 0x0100, [IntPtr]0x0D, [IntPtr]::Zero)
                [void][CnwlSelectionWin32]::PostMessage($w.Handle, 0x0101, [IntPtr]0x0D, [IntPtr]::Zero)
            }
        }
        Start-Sleep -Milliseconds 500
    }

    if (-not (Test-Path $resultPath)) { throw 'Selection probe did not produce result.txt.' }
    $result = Get-Content $resultPath
    if ($result -notcontains 'status=PASS_DISCOVERY') {
        throw "Selection discovery did not pass. Result:`n$($result -join "`n")"
    }
    if ($result -notcontains 'tool_info_received=True') { throw 'Timeline Tool did not receive TimelineToolInfo.' }
    if ($result -notcontains 'fixture_inserted=True') { throw 'Synthetic Timeline fixture was not inserted.' }

    if (-not (Test-Path $behaviorPath)) { throw 'Behavioral selection probe did not produce behavior-result.txt.' }
    $behavior = Get-Content $behaviorPath
    $status = $behavior | Where-Object { $_ -like 'status=*' } | Select-Object -First 1
    if ($status -notin @('status=PASS_BEHAVIOR_MODEL','status=PASS_BEHAVIOR_VM','status=PARTIAL_BEHAVIOR')) {
        throw "Unexpected behavioral result:`n$($behavior -join "`n")"
    }
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Remove-Item Env:CNWL_YMM4_SELECTION_DIR -ErrorAction SilentlyContinue
}
