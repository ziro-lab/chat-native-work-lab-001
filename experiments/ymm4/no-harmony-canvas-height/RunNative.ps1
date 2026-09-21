param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$result=Join-Path $OutputDir 'result.txt'
Add-Type -TypeDefinition @'
using System;using System.Text;using System.Runtime.InteropServices;
public static class CnwlCanvasHeightWin32{
 public delegate bool EnumWindowsProc(IntPtr h,IntPtr l);
 [DllImport("user32.dll")]public static extern bool EnumWindows(EnumWindowsProc c,IntPtr l);
 [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int GetWindowText(IntPtr h,StringBuilder t,int m);
 [DllImport("user32.dll")]public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
}
'@
function Wins{
 $script:r=@()
 $cb=[CnwlCanvasHeightWin32+EnumWindowsProc]{
  param([IntPtr]$h,[IntPtr]$l)
  if([CnwlCanvasHeightWin32]::IsWindowVisible($h)){
   $s=New-Object System.Text.StringBuilder 1024
   [void][CnwlCanvasHeightWin32]::GetWindowText($h,$s,$s.Capacity)
   if($s.Length){$script:r+=[pscustomobject]@{H=$h;T=$s.ToString()}}
  }
  return $true
 }
 [void][CnwlCanvasHeightWin32]::EnumWindows($cb,[IntPtr]::Zero)
 $script:r
}
$env:CNWL_YMM4_NO_HARMONY_CANVAS_HEIGHT_DIR=$OutputDir
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try{
 for($i=0;$i-lt180;$i++){
  if(Test-Path $result){break}
  foreach($w in (Wins)){
   if($w.T-like'*Check for updates*'-or$w.T-like'*About YukkuriMovieMaker*'){
    [void][CnwlCanvasHeightWin32]::PostMessage($w.H,0x10,[IntPtr]::Zero,[IntPtr]::Zero)
   }elseif($w.T-eq'Confirm'){
    [void][CnwlCanvasHeightWin32]::PostMessage($w.H,0x100,[IntPtr]0x0D,[IntPtr]::Zero)
    [void][CnwlCanvasHeightWin32]::PostMessage($w.H,0x101,[IntPtr]0x0D,[IntPtr]::Zero)
   }
  }
  Start-Sleep -Milliseconds 500
 }
 if(-not(Test-Path $result)){throw'no result.txt'}
 Get-Content $result
 if(-not((Get-Content $result)-contains'status=PASS_NO_HARMONY_CANVAS_HEIGHT_OBSERVATION')){throw'probe failed'}
}finally{
 if(-not$p.HasExited){Stop-Process $p.Id -Force -ErrorAction SilentlyContinue}
 Remove-Item Env:CNWL_YMM4_NO_HARMONY_CANVAS_HEIGHT_DIR -ErrorAction SilentlyContinue
}