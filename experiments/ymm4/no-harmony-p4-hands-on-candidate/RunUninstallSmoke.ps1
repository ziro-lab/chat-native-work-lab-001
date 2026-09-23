param(
    [Parameter(Mandatory = $true)][string]$Ymm4Dir,
    [Parameter(Mandatory = $true)][string]$PluginDir,
    [Parameter(Mandatory = $true)][string]$OutputDir,
    [int]$StableSeconds = 10
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null

$resultPath = Join-Path $OutputDir 'uninstall-result.json'
$windowLog = Join-Path $OutputDir 'windows.txt'
Remove-Item $resultPath,$windowLog -Force -ErrorAction SilentlyContinue

$exe = Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe'
if (-not (Test-Path $exe)) {
    throw "YMM4 executable not found: $exe"
}

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class P7UninstallWindow {
    public delegate bool Callback(IntPtr w, IntPtr p);
    [DllImport("user32.dll")] public static extern bool EnumWindows(Callback cb, IntPtr p);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr w);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr w, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr w, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr w, uint m, IntPtr wp, IntPtr lp);
}
'@

function Stop-Ymm4 {
    Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.ProcessName -like 'YukkuriMovieMaker*' } |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 700
}

Stop-Ymm4

if (Test-Path $PluginDir) {
    Remove-Item $PluginDir -Recurse -Force
}

$remaining = @(
    Get-ChildItem (Join-Path $Ymm4Dir 'user\plugin') -Recurse -Filter 'Ymm4NoHarmonyFolderHandsOn.dll' -ErrorAction SilentlyContinue
)
if ($remaining.Count -ne 0) {
    throw 'Plugin DLL still exists after uninstall.'
}

$p = Start-Process -FilePath $exe -WorkingDirectory $Ymm4Dir -PassThru
$script:hostPid = $p.Id
$script:mainSeenAt = $null
$script:lastMainTitle = ''

$cb = [P7UninstallWindow+Callback]{
    param([IntPtr]$w, [IntPtr]$unused)

    [uint32]$owner = 0
    [void][P7UninstallWindow]::GetWindowThreadProcessId($w, [ref]$owner)
    if ($owner -ne $script:hostPid -or -not [P7UninstallWindow]::IsWindowVisible($w)) {
        return $true
    }

    $s = New-Object System.Text.StringBuilder 1024
    [void][P7UninstallWindow]::GetWindowText($w, $s, 1024)
    $title = $s.ToString()
    "window=$title" | Add-Content $windowLog

    if ($title -like '*Check for updates*' -or
        $title -like '*About YukkuriMovieMaker*' -or
        $title -like '*Updating YukkuriMovieMaker4*') {
        [void][P7UninstallWindow]::PostMessage($w, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
        return $true
    }

    if ($title -eq 'Confirm' -or $title -eq 'Profile') {
        [void][P7UninstallWindow]::PostMessage($w, 0x0100, [IntPtr]0x0D, [IntPtr]::Zero)
        [void][P7UninstallWindow]::PostMessage($w, 0x0101, [IntPtr]0x0D, [IntPtr]::Zero)
        return $true
    }

    if (-not [string]::IsNullOrWhiteSpace($title) -and
        $title -notmatch '(?i)(plugin|プラグイン|extension|拡張|install|ymme)') {
        if ($null -eq $script:mainSeenAt) {
            $script:mainSeenAt = [DateTime]::UtcNow
        }
        $script:lastMainTitle = $title
    }

    return $true
}

try {
    $deadline = [DateTime]::UtcNow.AddSeconds(90 + $StableSeconds)

    while ([DateTime]::UtcNow -lt $deadline) {
        if ($p.HasExited) {
            throw "YMM4 exited after uninstall with code $($p.ExitCode)."
        }

        [void][P7UninstallWindow]::EnumWindows($cb, [IntPtr]::Zero)

        if ($null -ne $script:mainSeenAt) {
            $stable = ([DateTime]::UtcNow - $script:mainSeenAt).TotalSeconds
            if ($stable -ge $StableSeconds) {
                break
            }
        }

        Start-Sleep -Milliseconds 400
    }

    if ($null -eq $script:mainSeenAt) {
        throw 'No stable YMM4 main window observed after uninstall.'
    }

    $stableSecondsActual = ([DateTime]::UtcNow - $script:mainSeenAt).TotalSeconds
    if ($stableSecondsActual -lt $StableSeconds) {
        throw "YMM4 main window did not remain stable for $StableSeconds seconds."
    }

    $remainingAfterStart = @(
        Get-ChildItem (Join-Path $Ymm4Dir 'user\plugin') -Recurse -Filter 'Ymm4NoHarmonyFolderHandsOn.dll' -ErrorAction SilentlyContinue
    )
    if ($remainingAfterStart.Count -ne 0) {
        throw 'Plugin DLL reappeared after uninstall startup.'
    }

    $result = [ordered]@{
        status = 'PASS_P7_UNINSTALL_STARTUP'
        plugin_dir = $PluginDir
        plugin_dll_remaining = 0
        host_process_alive = $true
        stable_seconds_required = $StableSeconds
        stable_seconds_observed = [Math]::Round($stableSecondsActual, 2)
        main_window_title = $script:lastMainTitle
    }

    $result | ConvertTo-Json -Depth 4 | Set-Content $resultPath
    Get-Content $resultPath
}
finally {
    if (-not $p.HasExited) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    }
}
