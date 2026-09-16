param(
    [Parameter(Mandatory = $true)][string]$Ymm4Dir,
    [Parameter(Mandatory = $true)][string]$OutputDir
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null
Remove-Item (Join-Path $OutputDir 'result.txt'),(Join-Path $OutputDir 'ui-dump.txt') -Force -ErrorAction SilentlyContinue

$exe = Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe'
if (-not (Test-Path $exe)) { throw "YMM4 executable not found: $exe" }

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class CnwlPreviewRateWin32 {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
'@

function Get-CnwlPreviewRateWindows {
    $script:rows = @()
    $callback = [CnwlPreviewRateWin32+EnumWindowsProc]{
        param([IntPtr]$hWnd, [IntPtr]$lParam)
        if ([CnwlPreviewRateWin32]::IsWindowVisible($hWnd)) {
            $sb = New-Object System.Text.StringBuilder 1024
            [void][CnwlPreviewRateWin32]::GetWindowText($hWnd, $sb, $sb.Capacity)
            if (-not [string]::IsNullOrWhiteSpace($sb.ToString())) {
                $script:rows += [pscustomobject]@{ Handle = $hWnd; Title = $sb.ToString() }
            }
        }
        return $true
    }
    [void][CnwlPreviewRateWin32]::EnumWindows($callback, [IntPtr]::Zero)
    return $script:rows
}

$env:CNWL_YMM4_PREVIEW_RATE_UI_DIR = $OutputDir
$p = Start-Process -FilePath $exe -WorkingDirectory $Ymm4Dir -PassThru
try {
    for ($i = 0; $i -lt 100; $i++) {
        if (Test-Path (Join-Path $OutputDir 'result.txt')) { break }
        if ($p.HasExited) { break }
        foreach ($w in (Get-CnwlPreviewRateWindows)) {
            if ($w.Title -like '*Check for updates*' -or $w.Title -like '*About YukkuriMovieMaker*') {
                [void][CnwlPreviewRateWin32]::PostMessage($w.Handle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
            } elseif ($w.Title -eq 'Confirm') {
                [void][CnwlPreviewRateWin32]::PostMessage($w.Handle, 0x0100, [IntPtr]0x0D, [IntPtr]::Zero)
                [void][CnwlPreviewRateWin32]::PostMessage($w.Handle, 0x0101, [IntPtr]0x0D, [IntPtr]::Zero)
            }
        }
        Start-Sleep -Seconds 1
    }

    $result = Join-Path $OutputDir 'result.txt'
    if (-not (Test-Path $result)) { throw 'Probe did not produce result.txt.' }
    Get-Content $result
    if (-not (Select-String -Path $result -Pattern '^status=PASS$')) { throw 'PlaybackRate UI probe did not pass.' }
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Remove-Item Env:CNWL_YMM4_PREVIEW_RATE_UI_DIR -ErrorAction SilentlyContinue
}
