param(
    [Parameter(Mandatory = $true)][string]$Ymm4Dir,
    [Parameter(Mandatory = $true)][string]$Package,
    [Parameter(Mandatory = $true)][string]$ExpectedDll,
    [Parameter(Mandatory = $true)][string]$OutputDir
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$windowLog = Join-Path $OutputDir 'installer-windows.txt'
$resultPath = Join-Path $OutputDir 'ymme-install-result.json'
Remove-Item $windowLog,$resultPath -Force -ErrorAction SilentlyContinue

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$exe = Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe'
if (-not (Test-Path $exe)) { throw "YMM4 executable not found: $exe" }
if (-not (Test-Path $Package)) { throw "ymme package not found: $Package" }
if (-not (Test-Path $ExpectedDll)) { throw "expected candidate DLL not found: $ExpectedDll" }

$expectedHash = (Get-FileHash $ExpectedDll -Algorithm SHA256).Hash.ToLowerInvariant()
$packageHash = (Get-FileHash $Package -Algorithm SHA256).Hash.ToLowerInvariant()

function Stop-Ymm4 {
    Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.ProcessName -like 'YukkuriMovieMaker*' } |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 700
}

function Write-WindowSnapshot([string]$phase) {
    try {
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $windows = $root.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition)

        foreach ($window in $windows) {
            $ownerId = $window.Current.ProcessId
            if ($ownerId -le 0) { continue }
            $proc = Get-Process -Id $ownerId -ErrorAction SilentlyContinue
            if ($null -eq $proc -or $proc.ProcessName -notlike 'YukkuriMovieMaker*') { continue }

            $title = $window.Current.Name
            "[$phase] pid=$ownerId window=$title" | Add-Content $windowLog

            $isHostUpdater = $title -match '(?i)(UpdateNotiferViewModel|Check for updates|Updating YukkuriMovieMaker4)'
            $isInstallerLike = $title -match '(?i)(plugin|プラグイン|extension|拡張|ymme|install)'

            if ($isInstallerLike) {
                $all = $window.FindAll(
                    [System.Windows.Automation.TreeScope]::Descendants,
                    [System.Windows.Automation.Condition]::TrueCondition)

                foreach ($element in $all) {
                    $name = $element.Current.Name
                    if ([string]::IsNullOrWhiteSpace($name)) { continue }

                    if ($element.Current.IsEnabled -and
                        $element.Current.ControlType -eq [System.Windows.Automation.ControlType]::CheckBox) {
                        try {
                            $toggle = $element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
                            if ($toggle.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) {
                                $toggle.Toggle()
                                "[$phase] toggled-checkbox=$name" | Add-Content $windowLog
                                Start-Sleep -Milliseconds 180
                            }
                        } catch {
                            "[$phase] checkbox-toggle-failed=$name :: $($_.Exception.Message)" | Add-Content $windowLog
                        }
                    }

                    if ($element.Current.IsEnabled -and
                        $name -match '(?i)(Notes on plugin installation|プラグインのインストールに関する注意)') {
                        try {
                            $element.SetFocus()
                            [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
                            "[$phase] keyboard-activated=$name" | Add-Content $windowLog
                            Start-Sleep -Milliseconds 220
                        } catch {
                            "[$phase] keyboard-activate-failed=$name :: $($_.Exception.Message)" | Add-Content $windowLog
                        }
                    }
                }
            }

            $buttonCondition = [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button)
            $buttons = $window.FindAll(
                [System.Windows.Automation.TreeScope]::Descendants,
                $buttonCondition)

            foreach ($button in $buttons) {
                $name = $button.Current.Name
                if (-not $button.Current.IsEnabled) { continue }

                $invoke = $false
                if ($isHostUpdater) {
                    $invoke = $name -match '(?i)(cancel|キャンセル|後で|skip)'
                } else {
                    $invoke = $name -match '(?i)(インストール|install|上書き|overwrite|はい|yes|^ok$|続行|continue|実行)'
                    if (-not $invoke -and $isInstallerLike) {
                        $invoke = $name -match '(?i)(更新|update)'
                    }
                }

                if ($invoke) {
                    try {
                        $pattern = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
                        $pattern.Invoke()
                        "[$phase] invoked=$name" | Add-Content $windowLog
                        Start-Sleep -Milliseconds 280
                    } catch {
                        "[$phase] invoke-failed=$name :: $($_.Exception.Message)" | Add-Content $windowLog
                    }
                }
            }
        }
    }
    catch {
        "[$phase] ui-automation-transient=$($_.Exception.GetBaseException().Message)" | Add-Content $windowLog
    }
}

function Wait-ForInstalledDll([int]$seconds) {
    for ($i = 0; $i -lt $seconds; $i++) {
        Write-WindowSnapshot "install-$i"
        $dlls = @(Get-ChildItem (Join-Path $Ymm4Dir 'user\plugin') -Recurse -Filter 'Ymm4NoHarmonyFolderHandsOn.dll' -ErrorAction SilentlyContinue)
        foreach ($dll in $dlls) {
            $hash = (Get-FileHash $dll.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($hash -eq $expectedHash) {
                return $dll.FullName
            }
        }
        Start-Sleep -Seconds 1
    }
    return $null
}

Stop-Ymm4
$init = Start-Process -FilePath $exe -WorkingDirectory $Ymm4Dir -PassThru
for ($i = 0; $i -lt 18; $i++) {
    Write-WindowSnapshot "init-$i"
    if ($init.HasExited) { break }
    Start-Sleep -Seconds 1
}
Stop-Ymm4

"package=$Package package_sha256=$packageHash expected_dll_sha256=$expectedHash" | Add-Content $windowLog
Start-Process -FilePath $exe -ArgumentList @('"' + $Package + '"') -WorkingDirectory $Ymm4Dir | Out-Null
$installedDll = Wait-ForInstalledDll 70
if (-not $installedDll) {
    throw 'Real .ymme installation did not install the expected DLL hash.'
}
Stop-Ymm4

$installedHash = (Get-FileHash $installedDll -Algorithm SHA256).Hash.ToLowerInvariant()
$pluginRoot = Split-Path $installedDll
$harmonyFiles = @(Get-ChildItem $pluginRoot -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in @('0Harmony.dll','HarmonyLib.dll') })
if ($harmonyFiles.Count -ne 0) {
    throw 'Harmony file unexpectedly installed with no-Harmony candidate.'
}

$diag = Join-Path $OutputDir 'startup'
New-Item -ItemType Directory -Force $diag | Out-Null
$env:CNWL_P4_HANDS_ON_DIAG_DIR = $diag
$env:CNWL_P4_HANDS_ON_SMOKE_CREATE_PROJECT = '1'

$p = Start-Process -FilePath $exe -WorkingDirectory $Ymm4Dir -PassThru
try {
    $ready = Join-Path $diag 'ready.txt'
    $deadline = [DateTime]::UtcNow.AddSeconds(100)

    while ([DateTime]::UtcNow -lt $deadline -and -not $p.HasExited -and -not (Test-Path $ready)) {
        Write-WindowSnapshot "startup"
        Start-Sleep -Milliseconds 400
    }

    if (-not (Test-Path $ready)) {
        if (Test-Path (Join-Path $diag 'runtime.log')) {
            Get-Content (Join-Path $diag 'runtime.log') -Tail 120 | Write-Host
        }
        throw 'Installed .ymme candidate did not attach to the realized Timeline UI.'
    }

    'PASS_P4_YMME_STARTUP' | Set-Content (Join-Path $diag 'PASS_P4_YMME_STARTUP.txt')
}
finally {
    if (-not $p.HasExited) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    }
    Remove-Item Env:CNWL_P4_HANDS_ON_DIAG_DIR -ErrorAction SilentlyContinue
    Remove-Item Env:CNWL_P4_HANDS_ON_SMOKE_CREATE_PROJECT -ErrorAction SilentlyContinue
}

$result = [ordered]@{
    status = 'PASS_P4_YMME_INSTALL'
    package_sha256 = $packageHash
    expected_dll_sha256 = $expectedHash
    installed_dll_sha256 = $installedHash
    installed_dll = $installedDll
    plugin_root = $pluginRoot
    harmony_files_installed = $harmonyFiles.Count
    startup_marker = 'PASS_P4_YMME_STARTUP'
}

$result | ConvertTo-Json -Depth 5 | Set-Content $resultPath
Get-Content $resultPath
