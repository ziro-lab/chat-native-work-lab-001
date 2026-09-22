param(
    [Parameter(Mandatory = $true)][string]$Ymm4Dir,
    [Parameter(Mandatory = $true)][string]$V1Dll,
    [Parameter(Mandatory = $true)][string]$V2Dll,
    [Parameter(Mandatory = $true)][string]$OutputDir
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$windowLog = Join-Path $OutputDir 'installer-windows.txt'
$resultPath = Join-Path $OutputDir 'result.json'
$inventoryPath = Join-Path $OutputDir 'inventory.txt'
Remove-Item $windowLog,$resultPath,$inventoryPath -Force -ErrorAction SilentlyContinue

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$exe = Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe'
if (-not (Test-Path $exe)) { throw "YMM4 executable not found: $exe" }
foreach ($dll in @($V1Dll,$V2Dll)) {
    if (-not (Test-Path $dll)) { throw "Probe DLL not found: $dll" }
}

$v1Hash = (Get-FileHash $V1Dll -Algorithm SHA256).Hash.ToLowerInvariant()
$v2Hash = (Get-FileHash $V2Dll -Algorithm SHA256).Hash.ToLowerInvariant()
if ($v1Hash -eq $v2Hash) { throw 'v1 and v2 probe DLL hashes unexpectedly match.' }

function Stop-Ymm4 {
    Get-Process -Name 'YukkuriMovieMaker' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 700
}

function Write-WindowSnapshot([string]$phase) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($window in $windows) {
        $ownerId = $window.Current.ProcessId
        if ($ownerId -le 0) { continue }
        $proc = Get-Process -Id $ownerId -ErrorAction SilentlyContinue
        if ($null -eq $proc -or $proc.ProcessName -notlike 'YukkuriMovieMaker*') { continue }
        $title = $window.Current.Name
        "[$phase] pid=$ownerId window=$title" | Add-Content $windowLog

        $condition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)
        $buttons = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
        foreach ($button in $buttons) {
            $name = $button.Current.Name
            "[$phase]   button=$name enabled=$($button.Current.IsEnabled)" | Add-Content $windowLog
            if (-not $button.Current.IsEnabled) { continue }
            if ($name -match '(?i)(インストール|install|上書き|overwrite|更新|update|はい|yes|^ok$|続行|continue|実行)') {
                try {
                    $invoke = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
                    $invoke.Invoke()
                    "[$phase]   invoked=$name" | Add-Content $windowLog
                    Start-Sleep -Milliseconds 300
                } catch {
                    "[$phase]   invoke-failed=$name :: $($_.Exception.Message)" | Add-Content $windowLog
                }
            }
        }
    }
}

function Wait-ForInstalledVersion([string]$version, [int]$seconds) {
    for ($i = 0; $i -lt $seconds; $i++) {
        Write-WindowSnapshot "wait-$version-$i"
        $markers = @(Get-ChildItem (Join-Path $Ymm4Dir 'user\plugin') -Recurse -Filter 'package-version.txt' -ErrorAction SilentlyContinue)
        foreach ($marker in $markers) {
            $value = (Get-Content -Raw $marker.FullName).Trim()
            if ($value -eq $version) { return $marker.Directory.FullName }
        }
        Start-Sleep -Seconds 1
    }
    return $null
}

function Start-InstallerAttempt([string]$package, [string]$version, [string]$mode) {
    Stop-Ymm4
    "installer-attempt mode=$mode package=$package version=$version" | Add-Content $windowLog
    if ($mode -eq 'argument') {
        Start-Process -FilePath $exe -ArgumentList @('"' + $package + '"') -WorkingDirectory $Ymm4Dir | Out-Null
    } elseif ($mode -eq 'shell') {
        Start-Process -FilePath $package -WorkingDirectory (Split-Path $package) | Out-Null
    } else {
        throw "Unknown installer mode: $mode"
    }
    return Wait-ForInstalledVersion $version 45
}

function Install-Package([string]$package, [string]$version) {
    $root = Start-InstallerAttempt $package $version 'argument'
    if ($root) { return [pscustomobject]@{ Root = $root; Mode = 'argument' } }

    $root = Start-InstallerAttempt $package $version 'shell'
    if ($root) { return [pscustomobject]@{ Root = $root; Mode = 'shell' } }

    throw "Real .ymme install did not reach package version '$version'. See installer-windows.txt."
}

function New-ProbePackage([string]$dll, [string]$version, [string]$packagePath) {
    $stage = Join-Path $OutputDir ("stage-" + $version)
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
    $plugin = Join-Path $stage 'Ymm4PortableSettingsProbe'
    New-Item -ItemType Directory -Force $plugin | Out-Null
    Copy-Item $dll (Join-Path $plugin 'Ymm4PortableSettingsProbe.dll')
    Set-Content -NoNewline (Join-Path $plugin 'package-version.txt') $version
    if ($version -eq 'v1') {
        Set-Content -NoNewline (Join-Path $plugin 'obsolete-v1.txt') 'present only in v1 package'
    } else {
        Set-Content -NoNewline (Join-Path $plugin 'v2-only.txt') 'present only in v2 package'
    }

    $zip = [System.IO.Path]::ChangeExtension($packagePath, '.zip')
    Remove-Item $zip,$packagePath -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path $plugin -DestinationPath $zip -CompressionLevel Optimal
    Move-Item $zip $packagePath
}

# Launch once so a fresh portable host can perform its normal first-run/file-association setup.
Stop-Ymm4
$init = Start-Process -FilePath $exe -WorkingDirectory $Ymm4Dir -PassThru
for ($i = 0; $i -lt 15; $i++) {
    Write-WindowSnapshot "init-$i"
    if ($init.HasExited) { break }
    Start-Sleep -Seconds 1
}
Stop-Ymm4

$v1Package = Join-Path $OutputDir 'Ymm4PortableSettingsProbe-v1.ymme'
$v2Package = Join-Path $OutputDir 'Ymm4PortableSettingsProbe-v2.ymme'
New-ProbePackage $V1Dll 'v1' $v1Package
New-ProbePackage $V2Dll 'v2' $v2Package

$v1 = Install-Package $v1Package 'v1'
$pluginRoot = [System.IO.Path]::GetFullPath($v1.Root)
Stop-Ymm4

$installedV1Dll = Join-Path $pluginRoot 'Ymm4PortableSettingsProbe.dll'
if (-not (Test-Path $installedV1Dll)) { throw 'v1 installed marker exists but probe DLL is missing.' }
$installedV1Hash = (Get-FileHash $installedV1Dll -Algorithm SHA256).Hash.ToLowerInvariant()
if ($installedV1Hash -ne $v1Hash) { throw 'Installed v1 DLL hash does not match package v1 DLL.' }

$pluginData = Join-Path $pluginRoot 'Data\settings-probe.json'
$pluginNested = Join-Path $pluginRoot 'Data\nested\keep.txt'
$pluginRootUserFile = Join-Path $pluginRoot 'user-root-probe.txt'
$userSibling = Join-Path $Ymm4Dir 'user\Ymm4PortableSettingsProbe\settings-probe.json'
New-Item -ItemType Directory -Force (Split-Path $pluginData),(Split-Path $pluginNested),(Split-Path $userSibling) | Out-Null
Set-Content -NoNewline $pluginData '{"source":"user-created","value":42}'
Set-Content -NoNewline $pluginNested 'nested-user-data'
Set-Content -NoNewline $pluginRootUserFile 'root-user-data'
Set-Content -NoNewline $userSibling '{"source":"user-sibling","value":84}'

$before = [ordered]@{
    plugin_data_sha256 = (Get-FileHash $pluginData -Algorithm SHA256).Hash.ToLowerInvariant()
    plugin_nested_sha256 = (Get-FileHash $pluginNested -Algorithm SHA256).Hash.ToLowerInvariant()
    plugin_root_user_sha256 = (Get-FileHash $pluginRootUserFile -Algorithm SHA256).Hash.ToLowerInvariant()
    user_sibling_sha256 = (Get-FileHash $userSibling -Algorithm SHA256).Hash.ToLowerInvariant()
}

$v2 = Install-Package $v2Package 'v2'
Stop-Ymm4

if ([System.IO.Path]::GetFullPath($v2.Root) -ne $pluginRoot) {
    throw "v2 installed to a different plugin root: $($v2.Root)"
}

$installedV2Dll = Join-Path $pluginRoot 'Ymm4PortableSettingsProbe.dll'
$installedV2Hash = if (Test-Path $installedV2Dll) { (Get-FileHash $installedV2Dll -Algorithm SHA256).Hash.ToLowerInvariant() } else { $null }
$versionText = if (Test-Path (Join-Path $pluginRoot 'package-version.txt')) { (Get-Content -Raw (Join-Path $pluginRoot 'package-version.txt')).Trim() } else { $null }
$v2Only = Test-Path (Join-Path $pluginRoot 'v2-only.txt')
$obsoleteV1 = Test-Path (Join-Path $pluginRoot 'obsolete-v1.txt')

function Test-Preserved([string]$path, [string]$expectedHash) {
    if (-not (Test-Path $path)) { return $false }
    return (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant() -eq $expectedHash
}

$result = [ordered]@{
    status = 'PASS'
    ymm4_version = '4.55.1.1 Lite'
    v1_install_mode = $v1.Mode
    v2_install_mode = $v2.Mode
    plugin_root = $pluginRoot
    v1_dll_sha256 = $v1Hash
    v2_dll_sha256 = $v2Hash
    installed_v1_dll_sha256 = $installedV1Hash
    installed_v2_dll_sha256 = $installedV2Hash
    installed_package_version = $versionText
    v2_only_file_present = $v2Only
    obsolete_v1_package_file_preserved = $obsoleteV1
    plugin_data_file_preserved = (Test-Preserved $pluginData $before.plugin_data_sha256)
    plugin_nested_data_file_preserved = (Test-Preserved $pluginNested $before.plugin_nested_sha256)
    plugin_root_unknown_file_preserved = (Test-Preserved $pluginRootUserFile $before.plugin_root_user_sha256)
    user_sibling_data_file_preserved = (Test-Preserved $userSibling $before.user_sibling_sha256)
}

if ($installedV2Hash -ne $v2Hash) { throw 'Installed v2 DLL hash does not match package v2 DLL.' }
if ($versionText -ne 'v2') { throw "Expected installed package-version v2, got '$versionText'." }
if (-not $v2Only) { throw 'v2-only package file was not installed.' }

$result | ConvertTo-Json -Depth 4 | Set-Content $resultPath

"=== plugin root after v2 ===" | Set-Content $inventoryPath
Get-ChildItem $pluginRoot -Recurse -Force | Sort-Object FullName | ForEach-Object {
    $kind = if ($_.PSIsContainer) { 'DIR ' } else { 'FILE' }
    "$kind $($_.FullName.Substring($pluginRoot.Length).TrimStart('\'))" | Add-Content $inventoryPath
}
"=== user sibling after v2 ===" | Add-Content $inventoryPath
Get-ChildItem (Split-Path $userSibling) -Recurse -Force | Sort-Object FullName | ForEach-Object {
    "$($_.FullName)" | Add-Content $inventoryPath
}

Get-Content $resultPath
