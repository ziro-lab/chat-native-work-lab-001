param([Parameter(Mandatory)][string]$Ymm4Dir,[Parameter(Mandatory)][string]$OutputDir)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$result=Join-Path $OutputDir 'result.json'
if(Test-Path $result){throw 'Refuse stale result'}

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class P3RoundtripWindow {
 public delegate bool Callback(IntPtr w, IntPtr p);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Callback cb,IntPtr p);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr w);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr w,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr w,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr w,uint m,IntPtr wp,IntPtr lp);
}
'@

$env:CNWL_P3_TOOLSTATE_ROUNDTRIP_DIR=$OutputDir
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  $script:hostPid=$p.Id
  $cb=[P3RoundtripWindow+Callback]{
    param([IntPtr]$w,[IntPtr]$unused)
    [uint32]$owner=0
    [void][P3RoundtripWindow]::GetWindowThreadProcessId($w,[ref]$owner)
    if($owner -eq $script:hostPid -and [P3RoundtripWindow]::IsWindowVisible($w)){
      $s=New-Object System.Text.StringBuilder 1024
      [void][P3RoundtripWindow]::GetWindowText($w,$s,1024)
      $title=$s.ToString()
      if($title -like '*Check for updates*' -or $title -like '*About YukkuriMovieMaker*'){
        [void][P3RoundtripWindow]::PostMessage($w,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
      }elseif($title -eq 'Confirm'){
        [void][P3RoundtripWindow]::PostMessage($w,0x0100,[IntPtr]0x0D,[IntPtr]::Zero)
        [void][P3RoundtripWindow]::PostMessage($w,0x0101,[IntPtr]0x0D,[IntPtr]::Zero)
      }
    }
    return $true
  }
  $deadline=[DateTime]::UtcNow.AddSeconds(120)
  while([DateTime]::UtcNow -lt $deadline -and -not $p.HasExited -and -not(Test-Path $result)){
    [void][P3RoundtripWindow]::EnumWindows($cb,[IntPtr]::Zero)
    Start-Sleep -Milliseconds 400
  }
  if(-not(Test-Path $result)){throw 'No P3 ToolState roundtrip result'}
  $r=Get-Content $result -Raw|ConvertFrom-Json -AsHashtable
  Get-Content $result|Write-Host
  if($r.status -ne 'PASS_P3_TOOLSTATE_ROUNDTRIP'){throw 'P3 ToolState roundtrip failed'}
  $required=@(
    'timeline_a_id_nonempty','timeline_id_public_guid','tool_area_found',
    'area_seed_a_roundtrip','project_a_saved','project_a_toolstate_embedded',
    'area_seed_b_roundtrip','project_b_saved','project_b_toolstate_embedded','project_files_hold_distinct_toolstate',
    'open_a_project_path_applied','open_a_timeline_id_stable','open_a_toolstate_callback_restored','open_a_area_state_restored','open_a_document_valid',
    'open_b_project_path_applied','open_b_timeline_id_stable','open_b_toolstate_callback_restored','open_b_area_state_restored',
    'project_state_isolated','no_harmony_loaded')
  foreach($name in $required){if(-not $r.checks.ContainsKey($name) -or $r.checks[$name] -ne $true){throw "Missing/failed assertion: $name"}}
} finally {
  if(-not $p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_P3_TOOLSTATE_ROUNDTRIP_DIR -ErrorAction SilentlyContinue
}
