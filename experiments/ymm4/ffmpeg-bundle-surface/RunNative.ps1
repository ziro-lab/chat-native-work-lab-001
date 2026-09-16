param(
    [Parameter(Mandatory = $true)][string]$Ymm4Dir,
    [Parameter(Mandatory = $true)][string]$OutputDir
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$marker = Join-Path $OutputDir 'host-locator.txt'
Remove-Item $marker -Force -ErrorAction SilentlyContinue

$exe = Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe'
$pluginDll = Join-Path $Ymm4Dir 'user\plugin\FfmpegHostProbe\FfmpegHostProbe.dll'
if (-not (Test-Path $exe)) { throw "YMM4 executable not found: $exe" }
if (-not (Test-Path $pluginDll)) { throw "Probe DLL not found: $pluginDll" }

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class CnwlFfmpegWin32 {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
'@

function Get-CnwlWindows {
    $script:rows = @()
    $callback = [CnwlFfmpegWin32+EnumWindowsProc]{
        param([IntPtr]$hWnd, [IntPtr]$lParam)
        if ([CnwlFfmpegWin32]::IsWindowVisible($hWnd)) {
            $sb = New-Object System.Text.StringBuilder 1024
            [void][CnwlFfmpegWin32]::GetWindowText($hWnd, $sb, $sb.Capacity)
            $title = $sb.ToString()
            if (-not [string]::IsNullOrWhiteSpace($title)) { $script:rows += [pscustomobject]@{ Handle=$hWnd; Title=$title } }
        }
        return $true
    }
    [void][CnwlFfmpegWin32]::EnumWindows($callback, [IntPtr]::Zero)
    return $script:rows
}

$env:CNWL_FFMPEG_LOCATOR_MARKER = $marker
$p = Start-Process -FilePath $exe -WorkingDirectory $Ymm4Dir -PassThru
try {
    for ($i=0; $i -lt 120; $i++) {
        if (Test-Path $marker) { break }
        if ($p.HasExited) { break }
        foreach ($w in (Get-CnwlWindows)) {
            if ($w.Title -like '*Check for updates*' -or $w.Title -like '*About YukkuriMovieMaker*') {
                [void][CnwlFfmpegWin32]::PostMessage($w.Handle,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
            } elseif ($w.Title -eq 'Confirm') {
                [void][CnwlFfmpegWin32]::PostMessage($w.Handle,0x0100,[IntPtr]0x0D,[IntPtr]::Zero)
                [void][CnwlFfmpegWin32]::PostMessage($w.Handle,0x0101,[IntPtr]0x0D,[IntPtr]::Zero)
            }
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not (Test-Path $marker)) { throw 'YMM4 did not execute FFmpeg locator probe.' }
    $lines=Get-Content $marker
    foreach($required in @('status=PASS_HOST_LOCATOR','ffmpeg_directory_exists=True','ffmpeg_exe_exists=True','derived_ffprobe_exists=True')){
        if($lines -notcontains $required){ throw "Missing host-locator invariant: $required" }
    }
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Remove-Item Env:CNWL_FFMPEG_LOCATOR_MARKER -ErrorAction SilentlyContinue
}
