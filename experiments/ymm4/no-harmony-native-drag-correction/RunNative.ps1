param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir)
$ErrorActionPreference='Stop';New-Item -ItemType Directory -Force $OutputDir|Out-Null;$result=Join-Path $OutputDir 'result.txt'
Add-Type -TypeDefinition @'
using System;using System.Text;using System.Runtime.InteropServices;
public static class CnwlNDWin32{
 public delegate bool EnumWindowsProc(IntPtr hWnd,IntPtr lParam);
 [DllImport("user32.dll")]public static extern bool EnumWindows(EnumWindowsProc callback,IntPtr lParam);
 [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr hWnd);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int GetWindowText(IntPtr hWnd,StringBuilder text,int maxCount);
 [DllImport("user32.dll")]public static extern bool PostMessage(IntPtr hWnd,uint msg,IntPtr wParam,IntPtr lParam);
}
'@
function Get-Wins{$script:r=@();$cb=[CnwlNDWin32+EnumWindowsProc]{param([IntPtr]$h,[IntPtr]$l)if([CnwlNDWin32]::IsWindowVisible($h)){$s=New-Object System.Text.StringBuilder 1024;[void][CnwlNDWin32]::GetWindowText($h,$s,$s.Capacity);if($s.Length){$script:r+=[pscustomobject]@{Handle=$h;Title=$s.ToString()}}};return $true};[void][CnwlNDWin32]::EnumWindows($cb,[IntPtr]::Zero);$script:r}
$env:CNWL_YMM4_NO_HARMONY_NATIVE_DRAG_DIR=$OutputDir
$p=Start-Process -FilePath (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try{
 for($i=0;$i -lt 180;$i++){
  if(Test-Path $result){break};if($p.HasExited){break}
  foreach($w in (Get-Wins)){
   if($w.Title -like '*Check for updates*' -or $w.Title -like '*About YukkuriMovieMaker*'){[void][CnwlNDWin32]::PostMessage($w.Handle,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)}
   elseif($w.Title -eq 'Confirm'){[void][CnwlNDWin32]::PostMessage($w.Handle,0x0100,[IntPtr]0x0D,[IntPtr]::Zero);[void][CnwlNDWin32]::PostMessage($w.Handle,0x0101,[IntPtr]0x0D,[IntPtr]::Zero)}
  };Start-Sleep -Milliseconds 500
 }
 if(-not(Test-Path $result)){throw 'no result.txt'};Get-Content $result
 if(-not((Get-Content $result)-contains 'status=PASS_NO_HARMONY_NATIVE_DRAG_OBSERVATION')){throw 'probe failed'}
}finally{if(-not $p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue};Remove-Item Env:CNWL_YMM4_NO_HARMONY_NATIVE_DRAG_DIR -ErrorAction SilentlyContinue}