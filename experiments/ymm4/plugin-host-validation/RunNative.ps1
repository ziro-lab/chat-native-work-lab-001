param(
    [Parameter(Mandatory = $true)][string]$Ymm4Dir,
    [Parameter(Mandatory = $true)][string]$OutputDir
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$marker = Join-Path $OutputDir 'plugin-marker.txt'
$windowsLog = Join-Path $OutputDir 'windows-seen.txt'
$resultPath = Join-Path $OutputDir 'result.txt'
Remove-Item $marker,$windowsLog,$resultPath -Force -ErrorAction SilentlyContinue

$exe = Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe'
$pluginDll = Join-Path $Ymm4Dir 'user\plugin\Ymm4HostProbe\Ymm4HostProbe.dll'
if (-not (Test-Path $exe)) { throw "YMM4 executable not found: $exe" }
if (-not (Test-Path $pluginDll)) { throw "Probe DLL not found: $pluginDll" }

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class CnwlWin32 {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
'@

function Get-CnwlWindows {
    $script:rows = @()
    $callback = [CnwlWin32+EnumWindowsProc]{
        param([IntPtr]$hWnd, [IntPtr]$lParam)
        if ([CnwlWin32]::IsWindowVisible($hWnd)) {
            $sb = New-Object System.Text.StringBuilder 1024
            [void][CnwlWin32]::GetWindowText($hWnd, $sb, $sb.Capacity)
            $title = $sb.ToString()
            if (-not [string]::IsNullOrWhiteSpace($title)) {
                $script:rows += [pscustomobject]@{ Handle = $hWnd; Title = $title }
            }
        }
        return $true
    }
    [void][CnwlWin32]::EnumWindows($callback, [IntPtr]::Zero)
    return $script:rows
}

$env:CNWL_YMM4_PROBE_MARKER = $marker
$p = Start-Process -FilePath $exe -WorkingDirectory $Ymm4Dir -PassThru
$loaded = $false
$mainSeen = $false

try {
    for ($i = 0; $i -lt 120; $i++) {
        if (Test-Path $marker) { $loaded = $true; break }
        if ($p.HasExited) { break }

        foreach ($w in (Get-CnwlWindows)) {
            "tick=$i handle=$($w.Handle) title=$($w.Title)" | Add-Content $windowsLog
            if ($w.Title -like '*Check for updates*' -or $w.Title -like '*About YukkuriMovieMaker*') {
                [void][CnwlWin32]::PostMessage($w.Handle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
            } elseif ($w.Title -eq 'Confirm') {
                [void][CnwlWin32]::PostMessage($w.Handle, 0x0100, [IntPtr]0x0D, [IntPtr]::Zero)
                [void][CnwlWin32]::PostMessage($w.Handle, 0x0101, [IntPtr]0x0D, [IntPtr]::Zero)
            } elseif ($w.Title -like '*YukkuriMovieMaker v4.55.1.1 Lite*' -and $w.Title -notlike '*Starting*') {
                $mainSeen = $true
            }
        }
        Start-Sleep -Seconds 1
    }

    $installedHash = (Get-FileHash $pluginDll -Algorithm SHA256).Hash.ToLowerInvariant()
    $markerHash = $null
    if (Test-Path $marker) {
        $hashLine = Get-Content $marker | Where-Object { $_ -like 'sha256=*' } | Select-Object -First 1
        if ($hashLine) { $markerHash = $hashLine.Substring('sha256='.Length) }
    }

    @(
        "status=$(if ($loaded) { 'PASS' } else { 'FAIL' })"
        "plugin_loaded=$loaded"
        "main_window_seen=$mainSeen"
        "process_has_exited=$($p.HasExited)"
        "installed_probe_sha256=$installedHash"
        "loaded_probe_sha256=$markerHash"
    ) | Set-Content $resultPath

    if (-not $loaded) { throw 'YMM4 did not execute the probe plugin callback.' }
    if (-not (Select-String -Path $marker -Pattern '^Chat Native Work Lab — YMM4 Host Probe$')) { throw 'Probe marker identity mismatch.' }
    if ([string]::IsNullOrWhiteSpace($markerHash) -or $markerHash -ne $installedHash) { throw 'Loaded probe SHA256 does not match the installed probe DLL.' }
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Remove-Item Env:CNWL_YMM4_PROBE_MARKER -ErrorAction SilentlyContinue
}
