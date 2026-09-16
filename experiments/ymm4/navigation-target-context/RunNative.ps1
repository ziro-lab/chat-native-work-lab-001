param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir,[Parameter(Mandatory=$true)][string]$MediaPath)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$result=Join-Path $OutputDir 'result.json'
if(Test-Path $result){throw 'Evidence directory must be fresh.'}
$env:CNWL_NAV_CONTEXT_OUTPUT=$OutputDir
$env:CNWL_NAV_CONTEXT_MEDIA=$MediaPath
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class NavWindow {
 public delegate bool Callback(IntPtr h,IntPtr p);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Callback c,IntPtr p);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h,StringBuilder b,int n);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
}
'@
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
 $limit=[DateTime]::UtcNow.AddSeconds(85)
 while([DateTime]::UtcNow -lt $limit -and -not $p.HasExited -and -not(Test-Path $result)){
  $callback=[NavWindow+Callback]{param([IntPtr]$h,[IntPtr]$unused)
   [uint32]$ownerPid=0
   [void][NavWindow]::GetWindowThreadProcessId($h,[ref]$ownerPid)
   if($ownerPid -eq $p.Id){
    $b=[Text.StringBuilder]::new(512); [void][NavWindow]::GetWindowText($h,$b,$b.Capacity)
    $title=$b.ToString()
    if($title -like '*Check for updates*' -or $title -like '*About YukkuriMovieMaker*'){
     [void][NavWindow]::PostMessage($h,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
    }
   }
   return $true
  }
  [void][NavWindow]::EnumWindows($callback,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 400
 }
 if(-not(Test-Path $result)){throw 'No fresh native result.'}
 $r=Get-Content -Raw $result | ConvertFrom-Json
 if($r.schema -ne 'cnwl.navigation-context.v1' -or $r.status -ne 'PASS_NAVIGATION_CONTEXT' -or $r.host -ne '4.56.1.0 Lite' -or $r.sourceHead -ne $env:GITHUB_SHA -or $r.error){throw 'Native result identity/status rejected.'}
 $required=@('real_tool_callback','public_timeline','public_fps','fixture_exists','insert_a','insert_b','selected_videoitems','separate_occurrences','snapshot_survives_selection','selection_event','integer_seek','second_occurrence_seek','fractional_item_time','fractional_target_integer_seek','item_state_unchanged','reference_membership')
 if($r.requirements.Count -ne $required.Count){throw 'Wrong requirement count.'}
 foreach($id in $required){$found=@($r.requirements|Where-Object id -eq $id); if($found.Count -ne 1 -or $found[0].passed -cne $true){throw "Missing/failed requirement: $id"}}
 Get-Content (Join-Path $OutputDir 'context.json')
 Get-Content (Join-Path $OutputDir 'assertions.txt')
 Write-Output 'PASS_NAVIGATION_CONTEXT: 16 independently required assertions'
} finally {
 if(-not $p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
 Remove-Item Env:CNWL_NAV_CONTEXT_OUTPUT,Env:CNWL_NAV_CONTEXT_MEDIA -ErrorAction SilentlyContinue
}
