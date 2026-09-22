param([Parameter(Mandatory)][string]$Ymm4Dir,[Parameter(Mandatory)][string]$OutputDir,[Parameter(Mandatory)][string]$SubjectDll,[Parameter(Mandatory)][string]$ControllerDll)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class P3MissingWindow {
 public delegate bool Callback(IntPtr w, IntPtr p);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Callback cb,IntPtr p);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr w);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr w,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr w,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr w,uint m,IntPtr wp,IntPtr lp);
}
'@

function Close-NoiseWindows([int]$processId){
  $script:hostPid=$processId
  $cb=[P3MissingWindow+Callback]{
    param([IntPtr]$w,[IntPtr]$unused)
    [uint32]$owner=0
    [void][P3MissingWindow]::GetWindowThreadProcessId($w,[ref]$owner)
    if($owner -eq $script:hostPid -and [P3MissingWindow]::IsWindowVisible($w)){
      $s=New-Object System.Text.StringBuilder 1024
      [void][P3MissingWindow]::GetWindowText($w,$s,1024)
      $title=$s.ToString()
      if($title -like '*Check for updates*' -or $title -like '*About YukkuriMovieMaker*'){
        [void][P3MissingWindow]::PostMessage($w,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
      }elseif($title -eq 'Confirm'){
        [void][P3MissingWindow]::PostMessage($w,0x0100,[IntPtr]0x0D,[IntPtr]::Zero)
        [void][P3MissingWindow]::PostMessage($w,0x0101,[IntPtr]0x0D,[IntPtr]::Zero)
      }
    }
    return $true
  }
  [void][P3MissingWindow]::EnumWindows($cb,[IntPtr]::Zero)
}

function Run-Phase([string]$phase,[string]$marker){
  $env:CNWL_P3_MISSING_PHASE=$phase
  $env:CNWL_P3_MISSING_DIR=$OutputDir
  if(Test-Path $marker){Remove-Item $marker -Force}
  $p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
  try{
    $deadline=[DateTime]::UtcNow.AddSeconds(100)
    while([DateTime]::UtcNow -lt $deadline -and -not $p.HasExited -and -not(Test-Path $marker)){
      Close-NoiseWindows $p.Id
      Start-Sleep -Milliseconds 400
    }
    if(-not(Test-Path $marker)){throw "No marker for phase $phase"}
  } finally {
    if(-not $p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
    Wait-Process -Id $p.Id -Timeout 10 -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
  }
}

$subjectDir=Join-Path $Ymm4Dir 'user\plugin\CNWL_P3MissingSubject'
$controllerDir=Join-Path $Ymm4Dir 'user\plugin\CNWL_P3MissingController'
New-Item -ItemType Directory -Force $subjectDir,$controllerDir|Out-Null
Copy-Item $SubjectDll (Join-Path $subjectDir 'Ymm4P3MissingSubject.dll')
Copy-Item $ControllerDll (Join-Path $controllerDir 'Ymm4P3MissingController.dll')

Run-Phase 'seed' (Join-Path $OutputDir 'seed.done')
if(Test-Path (Join-Path $OutputDir 'seed.error.txt')){throw (Get-Content (Join-Path $OutputDir 'seed.error.txt') -Raw)}
$source=Join-Path $OutputDir 'subject-source.ymmp'
$expected=Get-Content (Join-Path $OutputDir 'expected-state.txt') -Raw
if(-not(Test-Path $source)){throw 'Seed project missing'}

# The subject plugin is physically absent for the reopen + resave phase.
Remove-Item $subjectDir -Recurse -Force
if(Test-Path (Join-Path $subjectDir 'Ymm4P3MissingSubject.dll')){throw 'Subject plugin removal failed'}

Run-Phase 'preserve' (Join-Path $OutputDir 'controller-result.json')
$controller=Get-Content (Join-Path $OutputDir 'controller-result.json') -Raw|ConvertFrom-Json -AsHashtable
if($controller.status -ne 'PASS_P3_MISSING_CONTROLLER'){throw 'Controller phase failed'}

$target=Join-Path $OutputDir 'subject-resaved-without-plugin.ymmp'
if(-not(Test-Path $target)){throw 'Resaved project missing'}

function Find-Expected([string]$path,[string]$value){
  $json=Get-Content $path -Raw|ConvertFrom-Json -AsHashtable
  if(-not $json.ContainsKey('ToolStates')){return $false}
  foreach($entry in $json.ToolStates.Values){
    if($entry -is [System.Collections.IDictionary] -and $entry.Contains('SavedState') -and $entry.SavedState -eq $value){return $true}
  }
  return $false
}
$sourceHas=Find-Expected $source $expected
$targetHas=Find-Expected $target $expected
$checks=[ordered]@{
  source_contains_subject_state=$sourceHas
  subject_plugin_absent_on_second_launch=(-not $controller.subjectAssemblyLoaded)
  source_open_path_applied=[bool]$controller.pathApplied
  marker_survives_source_open=[bool]$controller.markerAfterSourceOpen
  target_saved=[bool]$controller.targetExists
  target_reopen_path_applied=[bool]$controller.targetPathApplied
  marker_survives_target_reopen=[bool]$controller.markerAfterTargetReopen
  absent_plugin_state_is_dropped=(-not $targetHas)
  no_harmony_loaded=(-not $controller.harmonyLoaded)
}
$pass=($checks.Values -notcontains $false)
$final=[ordered]@{status=if($pass){'PASS_P3_PLUGIN_MISSING_LIMITATION'}else{'FAIL_P3_PLUGIN_MISSING_LIMITATION'};checks=$checks}
$final|ConvertTo-Json -Depth 6|Set-Content (Join-Path $OutputDir 'result.json')
$final|ConvertTo-Json -Depth 6|Write-Host
if(-not $pass){throw 'Missing-plugin limitation contract failed'}
Remove-Item Env:CNWL_P3_MISSING_PHASE -ErrorAction SilentlyContinue
Remove-Item Env:CNWL_P3_MISSING_DIR -ErrorAction SilentlyContinue
