param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$result=Join-Path $OutputDir 'result.json'
$hostLog=Join-Path $OutputDir 'host-log.txt'
Remove-Item $result,$hostLog -Force -ErrorAction SilentlyContinue

Add-Type -TypeDefinition @'
using System; using System.Text; using System.Runtime.InteropServices;
public static class CnwlLayerWin32 {
 public delegate bool EnumWindowsProc(IntPtr h,IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb,IntPtr l);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
}
'@
function Windows {
 $script:rows=@()
 $cb=[CnwlLayerWin32+EnumWindowsProc]{param([IntPtr]$h,[IntPtr]$l)
  if([CnwlLayerWin32]::IsWindowVisible($h)){
   $s=New-Object Text.StringBuilder 1024;[void][CnwlLayerWin32]::GetWindowText($h,$s,1024)
   if($s.Length){$script:rows += [pscustomobject]@{Handle=$h;Title=$s.ToString()}}
  };return $true}
 [void][CnwlLayerWin32]::EnumWindows($cb,[IntPtr]::Zero);$script:rows
}

$env:CNWL_YMM4_LAYER_CLICK_DIR=$OutputDir
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try{
 for($i=0;$i -lt 240;$i++){
  if(Test-Path $result){break};if($p.HasExited){break}
  foreach($w in Windows){
   "tick=$i title=$($w.Title)"|Add-Content $hostLog
   if($w.Title -like '*Check for updates*' -or $w.Title -like '*About YukkuriMovieMaker*'){[void][CnwlLayerWin32]::PostMessage($w.Handle,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)}
   elseif($w.Title -eq 'Confirm'){[void][CnwlLayerWin32]::PostMessage($w.Handle,0x0100,[IntPtr]0x0D,[IntPtr]::Zero);[void][CnwlLayerWin32]::PostMessage($w.Handle,0x0101,[IntPtr]0x0D,[IntPtr]::Zero)}
  }
  Start-Sleep -Milliseconds 500
 }
 if(-not(Test-Path $result)){throw 'No layer-click result; inspect host-log/error evidence.'}
 $r=Get-Content -Raw $result|ConvertFrom-Json
 Get-Content $result
 if($r.schema -cne 'cnwl.timeline-click-layer.v1' -or $r.status -cne 'PASS_LAYER_CLICK_OBSERVATION' -or $r.host -cne '4.55.1.1 Lite'){throw 'Layer-click observation rejected'}
 if(@($r.samples).Count -ne 3){throw 'Expected three layer samples'}
 foreach($layer in 1,3,5){
  $x=@($r.samples|Where-Object {$_.expectedLayer -eq $layer})
  if($x.Count-ne1 -or $x[0].blankRouteVerified-cne$true){throw "Missing/invalid layer sample $layer"}
 }
 Write-Output "PASS_LAYER_CLICK_OBSERVATION usable_click_layer_route=$($r.usableClickLayerRoute)"
}finally{
 if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
 Remove-Item Env:CNWL_YMM4_LAYER_CLICK_DIR -ErrorAction SilentlyContinue
}
