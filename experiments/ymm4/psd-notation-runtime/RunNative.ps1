param(
    [Parameter(Mandatory = $true)][string]$Ymm4Dir,
    [Parameter(Mandatory = $true)][string]$OutputDir
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$marker = Join-Path $OutputDir 'runtime-marker.txt'
$result = Join-Path $OutputDir 'runtime-result.json'
$windowsLog = Join-Path $OutputDir 'windows-seen.txt'
Remove-Item $marker,$result,$windowsLog -Force -ErrorAction SilentlyContinue

$exe = Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe'
$pluginDll = Join-Path $Ymm4Dir 'user\plugin\Ymm4PsdNotationRuntimeProbe\Ymm4PsdNotationRuntimeProbe.dll'
if (-not (Test-Path $exe)) { throw "YMM4 executable not found: $exe" }
if (-not (Test-Path $pluginDll)) { throw "Probe DLL not found: $pluginDll" }

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class CnwlPsdNotationWin32 {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
'@

function Get-CnwlWindows {
    $script:rows = @()
    $callback = [CnwlPsdNotationWin32+EnumWindowsProc]{
        param([IntPtr]$hWnd, [IntPtr]$lParam)
        if ([CnwlPsdNotationWin32]::IsWindowVisible($hWnd)) {
            $sb = New-Object System.Text.StringBuilder 1024
            [void][CnwlPsdNotationWin32]::GetWindowText($hWnd, $sb, $sb.Capacity)
            $title = $sb.ToString()
            if (-not [string]::IsNullOrWhiteSpace($title)) {
                $script:rows += [pscustomobject]@{ Handle = $hWnd; Title = $title }
            }
        }
        return $true
    }
    [void][CnwlPsdNotationWin32]::EnumWindows($callback, [IntPtr]::Zero)
    return $script:rows
}

$env:CNWL_YMM4_PSD_RUNTIME_OUTPUT = $OutputDir
$p = Start-Process -FilePath $exe -WorkingDirectory $Ymm4Dir -PassThru
$completed = $false

try {
    for ($i = 0; $i -lt 120; $i++) {
        if (Test-Path $marker) { $completed = $true; break }
        if ($p.HasExited) { break }

        foreach ($w in (Get-CnwlWindows)) {
            "tick=$i handle=$($w.Handle) title=$($w.Title)" | Add-Content $windowsLog
            if ($w.Title -like '*Check for updates*' -or $w.Title -like '*About YukkuriMovieMaker*') {
                [void][CnwlPsdNotationWin32]::PostMessage($w.Handle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
            } elseif ($w.Title -eq 'Confirm') {
                [void][CnwlPsdNotationWin32]::PostMessage($w.Handle, 0x0100, [IntPtr]0x0D, [IntPtr]::Zero)
                [void][CnwlPsdNotationWin32]::PostMessage($w.Handle, 0x0101, [IntPtr]0x0D, [IntPtr]::Zero)
            }
        }
        Start-Sleep -Seconds 1
    }

    if (-not $completed) { throw 'YMM4 runtime PSD notation probe did not produce a marker.' }
    if (-not (Test-Path $result)) { throw 'runtime-result.json was not produced.' }

    $json = Get-Content -Raw $result | ConvertFrom-Json
    if ($json.Status -ne 'PASS') {
        throw "Runtime probe reported status=$($json.Status). See runtime-result.json."
    }
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Remove-Item Env:CNWL_YMM4_PSD_RUNTIME_OUTPUT -ErrorAction SilentlyContinue
}
