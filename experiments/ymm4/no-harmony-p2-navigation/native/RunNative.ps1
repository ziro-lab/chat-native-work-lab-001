param([Parameter(Mandatory)][string]$Ymm4Dir,[Parameter(Mandatory)][string]$OutputDir)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$result=Join-Path $OutputDir 'result.json'
if(Test-Path $result){throw 'Refuse stale result'}
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class P2NavigationWindow {
 public delegate bool Callback(IntPtr w, IntPtr p);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Callback cb,IntPtr p);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr w);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr w,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr w,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr w,uint m,IntPtr wp,IntPtr lp);
}
'@
$env:CNWL_P2_NAVIGATION_DIR=$OutputDir
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  $script:hostPid=$p.Id
  $cb=[P2NavigationWindow+Callback]{
    param([IntPtr]$w,[IntPtr]$unused)
    [uint32]$owner=0
    [void][P2NavigationWindow]::GetWindowThreadProcessId($w,[ref]$owner)
    if($owner -eq $script:hostPid -and [P2NavigationWindow]::IsWindowVisible($w)){
      $s=New-Object System.Text.StringBuilder 1024
      [void][P2NavigationWindow]::GetWindowText($w,$s,1024)
      $title=$s.ToString()
      if($title -like '*Check for updates*' -or $title -like '*About YukkuriMovieMaker*'){
        [void][P2NavigationWindow]::PostMessage($w,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
      }elseif($title -eq 'Confirm'){
        [void][P2NavigationWindow]::PostMessage($w,0x0100,[IntPtr]0x0D,[IntPtr]::Zero)
        [void][P2NavigationWindow]::PostMessage($w,0x0101,[IntPtr]0x0D,[IntPtr]::Zero)
      }
    }
    return $true
  }
  $deadline=[DateTime]::UtcNow.AddSeconds(120)
  while([DateTime]::UtcNow -lt $deadline -and -not $p.HasExited -and -not(Test-Path $result)){
    [void][P2NavigationWindow]::EnumWindows($cb,[IntPtr]::Zero)
    Start-Sleep -Milliseconds 500
  }
  if(-not(Test-Path $result)){throw 'No P2 navigation result; inspect host/startup logs'}
  $r=Get-Content $result -Raw|ConvertFrom-Json -AsHashtable
  Get-Content $result|Write-Host
  if($r.status -ne 'PASS_P2_NAVIGATION'){throw 'P2 navigation failed'}
  $required=@('visible_context_bound','fixture_hidden_nested_target','raw_selection_keeps_target_folded','selection_event_observed','nested_selection_revealed','unrelated_folder_stays_collapsed','navigation_preserves_selection_seek','navigation_preserves_horizontal','nested_owner_keeps_own_collapse','nested_owner_visible','far_target_initially_offscreen','offscreen_selection_followed','visible_target_preserves_collapses','latest_selection_wins','stale_hidden_request_does_not_expand','ambiguous_multi_selection_not_guessed','cleared_selection_cancels_pending','detach_cancels_pending','detach_unsubscribes','item_geometry_unchanged','no_navigation_failure','no_display_failure_or_reentry','no_harmony_loaded','boundary_visual_row_maps_owner','boundary_next_visual_row_skips_hidden_body','method_lower_crosses_fold_by_one_display_row','method_higher_crosses_fold_back','keyboard_down_crosses_fold_by_one_display_row','keyboard_up_crosses_fold_back','boundary_navigation_preserves_fold_state','boundary_navigation_preserves_item_state','display_subscriptions_released')
  foreach($name in $required){if(-not $r.checks.ContainsKey($name) -or $r.checks[$name] -ne $true){throw "Missing/failed assertion: $name"}}
} finally {
  if(-not $p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_P2_NAVIGATION_DIR -ErrorAction SilentlyContinue
}
