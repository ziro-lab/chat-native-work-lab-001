param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$result=Join-Path $OutputDir 'result.json'
$hostLog=Join-Path $OutputDir 'host-log.txt'
Remove-Item $result,$hostLog -Force -ErrorAction SilentlyContinue

Add-Type -TypeDefinition @'
using System; using System.Text; using System.Runtime.InteropServices;
public static class CnwlPresetWin32 {
 public delegate bool EnumWindowsProc(IntPtr h,IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb,IntPtr l);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
}
'@
function Windows {
 $script:rows=@()
 $cb=[CnwlPresetWin32+EnumWindowsProc]{param([IntPtr]$h,[IntPtr]$l)
  if([CnwlPresetWin32]::IsWindowVisible($h)){
   $s=New-Object Text.StringBuilder 1024;[void][CnwlPresetWin32]::GetWindowText($h,$s,1024)
   if($s.Length){$script:rows += [pscustomobject]@{Handle=$h;Title=$s.ToString()}}
  };return $true}
 [void][CnwlPresetWin32]::EnumWindows($cb,[IntPtr]::Zero);$script:rows
}

$env:CNWL_YMM4_EXPRESSION_PRESET_DIR=$OutputDir
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try{
 for($i=0;$i-lt300;$i++){
  if(Test-Path $result){break}
  if($p.HasExited){break}
  foreach($w in Windows){
   "tick=$i handle=$($w.Handle) title=$($w.Title)"|Add-Content $hostLog
   if($w.Title -like '*Check for updates*' -or $w.Title -like '*About YukkuriMovieMaker*'){
    [void][CnwlPresetWin32]::PostMessage($w.Handle,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
   }elseif($w.Title -eq 'Confirm'){
    [void][CnwlPresetWin32]::PostMessage($w.Handle,0x0100,[IntPtr]0x0D,[IntPtr]::Zero)
    [void][CnwlPresetWin32]::PostMessage($w.Handle,0x0101,[IntPtr]0x0D,[IntPtr]::Zero)
   }
  }
  Start-Sleep -Milliseconds 500
 }
 if(-not(Test-Path $result)){
  if($p.HasExited){throw "No result; YMM4 exited $($p.ExitCode)"}
  throw 'No expression-preset surface result'
 }
 $r=Get-Content -Raw $result|ConvertFrom-Json
 Get-Content $result
 if($r.schema-cne'cnwl.expression-preset-surface.v1'-or$r.status-cne'PASS_EXPRESSION_PRESET_PUBLIC_SURFACE_DISCOVERY'-or$r.host-cne'4.55.1.1 Lite'){throw 'Expression preset discovery rejected'}
 Write-Output "PASS_EXPRESSION_PRESET_PUBLIC_SURFACE_DISCOVERY bare_face=$($r.bareFaceConstructed) complete_candidate=$($r.completeCandidateRoute) preset_types=$(@($r.presetTypes).Count) containers=$(@($r.presetContainers).Count) apply_candidates=$(@($r.presetApplicationCandidates).Count)"
}finally{
 if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
 Remove-Item Env:CNWL_YMM4_EXPRESSION_PRESET_DIR -ErrorAction SilentlyContinue
}
