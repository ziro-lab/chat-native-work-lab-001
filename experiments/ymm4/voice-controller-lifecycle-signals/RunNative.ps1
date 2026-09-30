param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class CnwlVoiceControllerWin32 {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
'@

function Get-CnwlWindows {
  $script:rows=@()
  $callback=[CnwlVoiceControllerWin32+EnumWindowsProc]{
    param([IntPtr]$hWnd,[IntPtr]$lParam)
    if([CnwlVoiceControllerWin32]::IsWindowVisible($hWnd)){
      $sb=New-Object System.Text.StringBuilder 1024
      [void][CnwlVoiceControllerWin32]::GetWindowText($hWnd,$sb,$sb.Capacity)
      $title=$sb.ToString()
      if(-not [string]::IsNullOrWhiteSpace($title)){
        $script:rows += [pscustomobject]@{Handle=$hWnd;Title=$title}
      }
    }
    return $true
  }
  [void][CnwlVoiceControllerWin32]::EnumWindows($callback,[IntPtr]::Zero)
  return $script:rows
}

$env:CNWL_VOICE_CONTROLLER_SIGNAL_OUTPUT=$OutputDir

$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  $limit=[DateTime]::UtcNow.AddSeconds(120)
  $result=Join-Path $OutputDir 'result.json'
  while([DateTime]::UtcNow-lt$limit -and -not$p.HasExited -and -not(Test-Path $result)){
    foreach($w in (Get-CnwlWindows)){
      "$([DateTime]::UtcNow.ToString('O')) title=$($w.Title)" | Add-Content (Join-Path $OutputDir 'host-windows.txt')
      if($w.Title -like '*Check for updates*' -or $w.Title -like '*About YukkuriMovieMaker*'){
        [void][CnwlVoiceControllerWin32]::PostMessage($w.Handle,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
      } elseif($w.Title -eq 'Confirm'){
        [void][CnwlVoiceControllerWin32]::PostMessage($w.Handle,0x0100,[IntPtr]0x0D,[IntPtr]::Zero)
        [void][CnwlVoiceControllerWin32]::PostMessage($w.Handle,0x0101,[IntPtr]0x0D,[IntPtr]::Zero)
      }
    }
    Start-Sleep -Milliseconds 350
  }

  if(-not(Test-Path $result)){throw 'No native result'}

  $r=Get-Content -Raw $result|ConvertFrom-Json
  Get-Content $result

  foreach($name in @('progress.txt','host-windows.txt','lifecycle-signals-observation.json')){
    $path=Join-Path $OutputDir $name
    if(Test-Path $path){Write-Output "--- $name ---";Get-Content $path}
  }

  if($r.schema-ne'cnwl.voice-controller-lifecycle-signals.v1' -or
     $r.status-ne'PASS_VOICE_CONTROLLER_LIFECYCLE_SIGNALS' -or
     $r.host-ne'4.56.1.0 Lite' -or
     $r.sourceHead-ne$env:GITHUB_SHA -or
     $r.error){throw 'Native controller lifecycle result rejected'}

  $req=@(
    'host_timeline_info_received',
    'timeline_nonnull',
    'undo_manager_nonnull',
    'initial_probe_count_zero',
    'try_add_voice_succeeded',
    'add_recorded_event_observed',
    'rescan_sees_added_voice',
    'undoed_event_removes_voice',
    'redoed_event_restores_voice',
    'restored_voice_resolved_by_rescan',
    'delete_recorded_event_observed',
    'rescan_sees_deleted_voice',
    'delete_undo_restores_voice',
    'timeline_undo_command_traffic_observed'
  )

  if($r.requirements.Count-ne$req.Count){throw "Wrong requirement count: $($r.requirements.Count)"}
  foreach($id in $req){
    $found=@($r.requirements|Where-Object {$_.id-eq$id})
    if($found.Count-ne1-or$found[0].passed-cne$true){throw "Missing/failed $id"}
  }

  $obsPath=Join-Path $OutputDir 'lifecycle-signals-observation.json'
  if(-not(Test-Path $obsPath)){throw 'Lifecycle observation missing'}
  $obs=Get-Content -Raw $obsPath|ConvertFrom-Json
  if($obs.events.recorded-lt2){throw 'Expected at least two Recorded events'}
  if($obs.events.undoed-lt2){throw 'Expected at least two Undoed events'}
  if($obs.events.redoed-lt1){throw 'Expected at least one Redoed event'}
  if($obs.finalProbeVoiceCount-ne1){throw 'Final rescan did not restore one probe VoiceItem'}

  Write-Output 'PASS_VOICE_CONTROLLER_LIFECYCLE_SIGNALS_E2E'
}
finally {
  if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_VOICE_CONTROLLER_SIGNAL_OUTPUT -ErrorAction SilentlyContinue
}
