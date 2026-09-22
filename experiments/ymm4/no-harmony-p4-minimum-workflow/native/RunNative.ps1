param([Parameter(Mandatory)][string]$Ymm4Dir,[Parameter(Mandatory)][string]$OutputDir)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$result=Join-Path $OutputDir 'result.json'
if(Test-Path $result){throw 'Refuse stale result'}

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class P4WorkflowWindow {
 public delegate bool Callback(IntPtr w, IntPtr p);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Callback cb,IntPtr p);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr w);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr w,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr w,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr w,uint m,IntPtr wp,IntPtr lp);
}
'@

$env:CNWL_P4_MINIMUM_WORKFLOW_DIR=$OutputDir
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  $script:hostPid=$p.Id
  $cb=[P4WorkflowWindow+Callback]{
    param([IntPtr]$w,[IntPtr]$unused)
    [uint32]$owner=0
    [void][P4WorkflowWindow]::GetWindowThreadProcessId($w,[ref]$owner)
    if($owner -eq $script:hostPid -and [P4WorkflowWindow]::IsWindowVisible($w)){
      $s=New-Object System.Text.StringBuilder 1024
      [void][P4WorkflowWindow]::GetWindowText($w,$s,1024)
      $title=$s.ToString()
      if($title -like '*Check for updates*' -or $title -like '*About YukkuriMovieMaker*'){
        [void][P4WorkflowWindow]::PostMessage($w,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
      }elseif($title -eq 'Confirm' -or $title -eq 'Profile'){
        [void][P4WorkflowWindow]::PostMessage($w,0x0100,[IntPtr]0x0D,[IntPtr]::Zero)
        [void][P4WorkflowWindow]::PostMessage($w,0x0101,[IntPtr]0x0D,[IntPtr]::Zero)
      }
    }
    return $true
  }

  $deadline=[DateTime]::UtcNow.AddSeconds(120)
  while([DateTime]::UtcNow -lt $deadline -and -not $p.HasExited -and -not(Test-Path $result)){
    [void][P4WorkflowWindow]::EnumWindows($cb,[IntPtr]::Zero)
    Start-Sleep -Milliseconds 400
  }

  if(-not(Test-Path $result)){throw 'No P4 minimum workflow result'}
  $r=Get-Content $result -Raw|ConvertFrom-Json -AsHashtable
  Get-Content $result|Write-Host
  if($r.status -ne 'PASS_P4_MINIMUM_WORKFLOW'){throw 'P4 minimum workflow failed'}

  $required=@(
    'layer_labels_found','identity_native_menu_control',
    'create_menu_visible','create_action_clicked','folder_created_exact_range','folder_overlay_present',
    'create_document_serializable','create_starts_expanded',
    'collapse_action_clicked','expand_action_clicked',
    'rename_menu_visible','rename_action_clicked','rename_document_serializable',
    'ungroup_menu_visible','ungroup_action_clicked','folder_metadata_removed',
    'ungroup_restores_identity_display','timeline_items_preserved','timeline_layers_preserved',
    'final_document_serializable','right_clicks_mapped',
    'no_display_failure_or_reentry','no_harmony_loaded','display_subscriptions_released')
  foreach($name in $required){
    if(-not $r.checks.ContainsKey($name) -or $r.checks[$name] -ne $true){
      throw "Missing/failed assertion: $name"
    }
  }
} finally {
  if(-not $p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_P4_MINIMUM_WORKFLOW_DIR -ErrorAction SilentlyContinue
}
