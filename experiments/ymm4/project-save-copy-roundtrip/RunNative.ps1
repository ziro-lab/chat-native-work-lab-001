param(
    [Parameter(Mandatory = $true)][string]$Ymm4Dir,
    [Parameter(Mandatory = $true)][string]$OutputDir
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$resultPath = Join-Path $OutputDir 'result.txt'
$hostLog = Join-Path $OutputDir 'host-log.txt'
Remove-Item $resultPath,$hostLog -Force -ErrorAction SilentlyContinue

$exe = Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe'
$pluginDll = Join-Path $Ymm4Dir 'user\plugin\Ymm4ProjectSaveCopyProbe\Ymm4ProjectSaveCopyProbe.dll'
if (-not (Test-Path $exe)) { throw "YMM4 executable not found: $exe" }
if (-not (Test-Path $pluginDll)) { throw "Probe DLL not found: $pluginDll" }

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class CnwlSaveCopyWin32 {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
'@

function Get-CnwlWindows {
    $script:rows = @()
    $callback = [CnwlSaveCopyWin32+EnumWindowsProc]{
        param([IntPtr]$hWnd, [IntPtr]$lParam)
        if ([CnwlSaveCopyWin32]::IsWindowVisible($hWnd)) {
            $sb = New-Object System.Text.StringBuilder 1024
            [void][CnwlSaveCopyWin32]::GetWindowText($hWnd, $sb, $sb.Capacity)
            $title = $sb.ToString()
            if (-not [string]::IsNullOrWhiteSpace($title)) { $script:rows += [pscustomobject]@{ Handle=$hWnd; Title=$title } }
        }
        return $true
    }
    [void][CnwlSaveCopyWin32]::EnumWindows($callback, [IntPtr]::Zero)
    return $script:rows
}

$env:CNWL_YMM4_PROJECT_SAVE_COPY_DIR = $OutputDir
$p = Start-Process -FilePath $exe -WorkingDirectory $Ymm4Dir -PassThru
try {
    for ($i=0; $i -lt 140; $i++) {
        if (Test-Path $resultPath) { break }
        if ($p.HasExited) { break }
        foreach ($w in (Get-CnwlWindows)) {
            "tick=$i handle=$($w.Handle) title=$($w.Title)" | Add-Content $hostLog
            if ($w.Title -like '*Check for updates*' -or $w.Title -like '*About YukkuriMovieMaker*') {
                [void][CnwlSaveCopyWin32]::PostMessage($w.Handle,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
            } elseif ($w.Title -eq 'Confirm') {
                [void][CnwlSaveCopyWin32]::PostMessage($w.Handle,0x0100,[IntPtr]0x0D,[IntPtr]::Zero)
                [void][CnwlSaveCopyWin32]::PostMessage($w.Handle,0x0101,[IntPtr]0x0D,[IntPtr]::Zero)
            }
        }
        Start-Sleep -Milliseconds 400
    }
    if (-not (Test-Path $resultPath)) { throw 'Project save-copy probe did not produce result.txt.' }
    $result = Get-Content $resultPath
    if ($result -notcontains 'status=PASS_PROJECT_SAVE_COPY_PATH_RESTORE_OBSERVATION') {
        throw "Project save-copy observation did not pass. Result:`n$($result -join "`n")"
    }
    foreach ($required in @('archive_exists=True','active_path_restored_source=True','source_byte_unchanged=True','archive_reload_marker=True')) {
        if ($result -notcontains $required) { throw "Missing required invariant: $required" }
    }
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Remove-Item Env:CNWL_YMM4_PROJECT_SAVE_COPY_DIR -ErrorAction SilentlyContinue
}
