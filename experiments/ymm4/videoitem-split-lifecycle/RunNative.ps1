param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir,[Parameter(Mandatory=$true)][string]$MediaPath)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$result=Join-Path $OutputDir 'result.json'
if(Test-Path $result){throw 'Evidence directory must be fresh.'}
$env:CNWL_SPLIT_OUTPUT=$OutputDir
$env:CNWL_SPLIT_MEDIA=$MediaPath
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class SplitWindow {
 public delegate bool Callback(IntPtr h,IntPtr p);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Callback c,IntPtr p);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h,StringBuilder b,int n);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
}
'@
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
 $limit=[DateTime]::UtcNow.AddSeconds(100)
 while([DateTime]::UtcNow -lt $limit -and -not $p.HasExited -and -not(Test-Path $result)){
  $callback=[SplitWindow+Callback]{param([IntPtr]$h,[IntPtr]$unused)
   [uint32]$ownerPid=0; [void][SplitWindow]::GetWindowThreadProcessId($h,[ref]$ownerPid)
   if($ownerPid -eq $p.Id){
    $b=[Text.StringBuilder]::new(512); [void][SplitWindow]::GetWindowText($h,$b,$b.Capacity)
    $title=$b.ToString()
    if($title -like '*Check for updates*' -or $title -like '*About YukkuriMovieMaker*'){[void][SplitWindow]::PostMessage($h,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)}
   }
   return $true
  }
  [void][SplitWindow]::EnumWindows($callback,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 400
 }
 if(-not(Test-Path $result)){throw 'No fresh native result.'}
 $r=Get-Content -Raw $result | ConvertFrom-Json
 Get-Content $result
 if(Test-Path (Join-Path $OutputDir 'split-methods.txt')){Get-Content (Join-Path $OutputDir 'split-methods.txt')}
 if(Test-Path (Join-Path $OutputDir 'split.json')){Get-Content (Join-Path $OutputDir 'split.json')}
 if($r.status -ne 'PASS_VIDEOITEM_SPLIT_LIFECYCLE'){throw 'Native split probe did not pass.'}
 $required=@('fixture_exists','fps_positive','split_surface_discovered',
 'split_50_two_pieces','split_50_original_replaced','split_50_timeline_partition','split_50_path_rate_preserved','split_50_source_partition',
 'split_100_two_pieces','split_100_original_replaced','split_100_timeline_partition','split_100_path_rate_preserved','split_100_source_partition',
 'split_200_two_pieces','split_200_original_replaced','split_200_timeline_partition','split_200_path_rate_preserved','split_200_source_partition',
 'second_split_can_execute','double_split_three_pieces','double_split_source_ranges','double_split_previous_left_survives','double_split_target_reference_replaced')
 if($r.schema -ne 'cnwl.videoitem-split-lifecycle.v1' -or $r.host -ne '4.56.1.0 Lite' -or $r.sourceHead -ne $env:GITHUB_SHA -or $r.error){throw 'Native result identity/status rejected.'}
 if($r.requirements.Count -ne $required.Count){throw 'Wrong requirement count.'}
 foreach($id in $required){$found=@($r.requirements|Where-Object id -eq $id); if($found.Count -ne 1 -or $found[0].passed -cne $true){throw "Missing/failed requirement: $id"}}
 Write-Output 'PASS_VIDEOITEM_SPLIT_LIFECYCLE'
} finally {
 if(-not $p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
 Remove-Item Env:CNWL_SPLIT_OUTPUT,Env:CNWL_SPLIT_MEDIA -ErrorAction SilentlyContinue
}
