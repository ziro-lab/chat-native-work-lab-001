param(
    [Parameter(Mandatory = $true)][string]$Ymm4Dir,
    [Parameter(Mandatory = $true)][string]$OutputDir,
    [Parameter(Mandatory = $true)][string]$ProbeEnv,
    [Parameter(Mandatory = $true)][string]$ResultFile,
    [Parameter(Mandatory = $true)][string]$PassMarker,
    [int]$TimeoutSeconds = 240
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$result = Join-Path $OutputDir $ResultFile
Remove-Item $result -Force -ErrorAction SilentlyContinue

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class P6FinalProbeWindow {
    public delegate bool Callback(IntPtr w, IntPtr p);
    [DllImport("user32.dll")] public static extern bool EnumWindows(Callback cb, IntPtr p);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr w);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr w, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr w, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr w, uint m, IntPtr wp, IntPtr lp);
}
'@

$env:CNWL_P4_HANDS_ON_DIAG_DIR = $OutputDir
$env:CNWL_P4_HANDS_ON_SMOKE_CREATE_PROJECT = '1'
$env:CNWL_P6_HARDENING_SMOKE = '1'
Set-Item -Path ("Env:" + $ProbeEnv) -Value '1'

$p = Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
    $script:hostPid = $p.Id
    $cb = [P6FinalProbeWindow+Callback]{
        param([IntPtr]$w, [IntPtr]$unused)
        [uint32]$owner = 0
        [void][P6FinalProbeWindow]::GetWindowThreadProcessId($w, [ref]$owner)
        if ($owner -eq $script:hostPid -and [P6FinalProbeWindow]::IsWindowVisible($w)) {
            $s = New-Object System.Text.StringBuilder 1024
            [void][P6FinalProbeWindow]::GetWindowText($w, $s, 1024)
            $title = $s.ToString()

            if ($title -like '*Check for updates*' -or $title -like '*About YukkuriMovieMaker*') {
                [void][P6FinalProbeWindow]::PostMessage($w, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
            }
            elseif ($title -eq 'Confirm' -or $title -eq 'Profile') {
                [void][P6FinalProbeWindow]::PostMessage($w, 0x0100, [IntPtr]0x0D, [IntPtr]::Zero)
                [void][P6FinalProbeWindow]::PostMessage($w, 0x0101, [IntPtr]0x0D, [IntPtr]::Zero)
            }
        }
        return $true
    }

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline -and -not $p.HasExited -and -not (Test-Path $result)) {
        [void][P6FinalProbeWindow]::EnumWindows($cb, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 400
    }

    if (-not (Test-Path $result)) {
        if (Test-Path (Join-Path $OutputDir 'runtime.log')) {
            Get-Content (Join-Path $OutputDir 'runtime.log') -Tail 900 | Write-Host
        }
        throw "P6 final probe did not produce $ResultFile"
    }

    $content = Get-Content $result
    $content | Write-Host

    if ($content.Count -eq 0 -or $content[0] -ne $PassMarker) {
        if (Test-Path (Join-Path $OutputDir 'runtime.log')) {
            Get-Content (Join-Path $OutputDir 'runtime.log') -Tail 900 | Write-Host
        }
        throw "P6 final probe failed: expected $PassMarker"
    }
}
finally {
    if (-not $p.HasExited) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    }

    foreach ($name in @(
        'CNWL_P4_HANDS_ON_DIAG_DIR',
        'CNWL_P4_HANDS_ON_SMOKE_CREATE_PROJECT',
        'CNWL_P6_HARDENING_SMOKE',
        $ProbeEnv
    )) {
        Remove-Item ("Env:" + $name) -ErrorAction SilentlyContinue
    }
}
