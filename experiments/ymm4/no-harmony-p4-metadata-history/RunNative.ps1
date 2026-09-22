param([Parameter(Mandatory)][string]$Ymm4Dir,[Parameter(Mandatory)][string]$OutputDir)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$result=Join-Path $OutputDir 'result.json'
if(Test-Path $result){throw 'Refuse stale result'}

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class P4HistoryWindow {
 public delegate bool Callback(IntPtr w, IntPtr p);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Callback cb,IntPtr p);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr w);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr w,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr w,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr w,uint m,IntPtr wp,IntPtr lp);
}
'@

$env:CNWL_P4_METADATA_HISTORY_DIR=$OutputDir
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  $script:hostPid=$p.Id
  $cb=[P4HistoryWindow+Callback]{
    param([IntPtr]$w,[IntPtr]$unused)
    [uint32]$owner=0
    [void][P4HistoryWindow]::GetWindowThreadProcessId($w,[ref]$owner)
    if($owner -eq $script:hostPid -and [P4HistoryWindow]::IsWindowVisible($w)){
      $s=New-Object System.Text.StringBuilder 1024
      [void][P4HistoryWindow]::GetWindowText($w,$s,1024)
      $title=$s.ToString()
      if($title -like '*Check for updates*' -or $title -like '*About YukkuriMovieMaker*'){
        [void][P4HistoryWindow]::PostMessage($w,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
      }elseif($title -eq 'Confirm' -or $title -eq 'Profile'){
        [void][P4HistoryWindow]::PostMessage($w,0x0100,[IntPtr]0x0D,[IntPtr]::Zero)
        [void][P4HistoryWindow]::PostMessage($w,0x0101,[IntPtr]0x0D,[IntPtr]::Zero)
      }
    }
    return $true
  }
  $deadline=[DateTime]::UtcNow.AddSeconds(100)
  while([DateTime]::UtcNow -lt $deadline -and -not $p.HasExited -and -not(Test-Path $result)){
    [void][P4HistoryWindow]::EnumWindows($cb,[IntPtr]::Zero)
    Start-Sleep -Milliseconds 400
  }
  if(-not(Test-Path $result)){throw 'No P4 metadata history result'}
  $r=Get-Content $result -Raw|ConvertFrom-Json -AsHashtable
  Get-Content $result|Write-Host
  if($r.status -ne 'PASS_P4_METADATA_HISTORY'){throw 'P4 metadata history failed'}
  $required=@(
    'baseline_saved','baseline_reports_saved','pure_change_is_metadata_only',
    'metadata_recorded_once','metadata_marks_project_unsaved','metadata_forward_state',
    'metadata_undo_callback_once','metadata_undo_restores_document',
    'metadata_redo_callback_once','metadata_redo_restores_document',
    'save_after_metadata_succeeds','save_after_metadata_reports_saved',
    'timeline_identity_unchanged','no_harmony_loaded')
  foreach($name in $required){if(-not $r.checks.ContainsKey($name) -or $r.checks[$name] -ne $true){throw "Missing/failed assertion: $name"}}
} finally {
  if(-not $p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_P4_METADATA_HISTORY_DIR -ErrorAction SilentlyContinue
}
