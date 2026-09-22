param([Parameter(Mandatory)][string]$Ymm4Dir,[Parameter(Mandatory)][string]$OutputDir)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$result=Join-Path $OutputDir 'result.json'
if(Test-Path $result){throw 'Refuse stale result'}

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class P4UiWindow {
 public delegate bool Callback(IntPtr w, IntPtr p);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Callback cb,IntPtr p);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr w);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr w,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr w,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr w,uint m,IntPtr wp,IntPtr lp);
}
'@

$env:CNWL_P4_UI_SURFACE_DIR=$OutputDir
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  $script:hostPid=$p.Id
  $cb=[P4UiWindow+Callback]{
    param([IntPtr]$w,[IntPtr]$unused)
    [uint32]$owner=0
    [void][P4UiWindow]::GetWindowThreadProcessId($w,[ref]$owner)
    if($owner -eq $script:hostPid -and [P4UiWindow]::IsWindowVisible($w)){
      $s=New-Object System.Text.StringBuilder 1024
      [void][P4UiWindow]::GetWindowText($w,$s,1024)
      $title=$s.ToString()
      if($title -like '*Check for updates*' -or $title -like '*About YukkuriMovieMaker*'){
        [void][P4UiWindow]::PostMessage($w,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
      }elseif($title -eq 'Confirm' -or $title -eq 'Profile'){
        [void][P4UiWindow]::PostMessage($w,0x0100,[IntPtr]0x0D,[IntPtr]::Zero)
        [void][P4UiWindow]::PostMessage($w,0x0101,[IntPtr]0x0D,[IntPtr]::Zero)
      }
    }
    return $true
  }
  $deadline=[DateTime]::UtcNow.AddSeconds(120)
  while([DateTime]::UtcNow -lt $deadline -and -not $p.HasExited -and -not(Test-Path $result)){
    [void][P4UiWindow]::EnumWindows($cb,[IntPtr]::Zero)
    Start-Sleep -Milliseconds 400
  }
  if(-not(Test-Path $result)){throw 'No P4 UI surface result'}
  $r=Get-Content $result -Raw|ConvertFrom-Json -AsHashtable
  Get-Content $result|Write-Host
  if($r.status -ne 'PASS_P4_UI_SURFACE'){throw 'P4 UI surface failed'}
  $required=@(
    'fixture_folded','layer_labels_found','adorner_attached','adorner_input_transparent','toggle_on_screen',
    'native_toggle_expands','native_toggle_collapses',
    'outside_overlay_preserves_native_layer_click',
    'context_open_observed','native_menu_preserved_and_extended',
    'native_menu_action_click','menu_cleanup_after_close','adorner_detached',
    'no_display_failure_or_reentry','no_harmony_loaded','display_subscriptions_released')
  foreach($name in $required){if(-not $r.checks.ContainsKey($name) -or $r.checks[$name] -ne $true){throw "Missing/failed assertion: $name"}}
} finally {
  if(-not $p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_P4_UI_SURFACE_DIR -ErrorAction SilentlyContinue
}
