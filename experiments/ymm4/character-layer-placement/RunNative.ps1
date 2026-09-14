param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir)
$ErrorActionPreference='Stop';New-Item -ItemType Directory -Force $OutputDir|Out-Null
$result=Join-Path $OutputDir 'result.txt';$hostLog=Join-Path $OutputDir 'host-windows.txt';Remove-Item $result,$hostLog -Force -ErrorAction SilentlyContinue
Add-Type -TypeDefinition @'
using System; using System.Text; using System.Runtime.InteropServices;
public static class CnwlLayerWin32 {
 public delegate bool EnumWindowsProc(IntPtr hWnd,IntPtr lParam);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback,IntPtr lParam);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd,StringBuilder text,int maxCount);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd,uint msg,IntPtr wParam,IntPtr lParam);
}
'@
function Get-CnwlWindows{$script:rows=@();$callback=[CnwlLayerWin32+EnumWindowsProc]{param([IntPtr]$hWnd,[IntPtr]$lParam)if([CnwlLayerWin32]::IsWindowVisible($hWnd)){$sb=New-Object System.Text.StringBuilder 1024;[void][CnwlLayerWin32]::GetWindowText($hWnd,$sb,$sb.Capacity);$t=$sb.ToString();if(-not[string]::IsNullOrWhiteSpace($t)){$script:rows += [pscustomobject]@{Handle=$hWnd;Title=$t}}};return $true};[void][CnwlLayerWin32]::EnumWindows($callback,[IntPtr]::Zero);return $script:rows}
$env:CNWL_YMM4_LAYER_DIR=$OutputDir;$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try{
 for($i=0;$i -lt 120 -and -not(Test-Path $result) -and -not $p.HasExited;$i++){
  foreach($w in (Get-CnwlWindows)){"tick=$i handle=$($w.Handle) title=$($w.Title)"|Add-Content $hostLog;if($w.Title -like '*Check for updates*' -or $w.Title -like '*About YukkuriMovieMaker*'){[void][CnwlLayerWin32]::PostMessage($w.Handle,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)}elseif($w.Title -eq 'Confirm'){[void][CnwlLayerWin32]::PostMessage($w.Handle,0x0100,[IntPtr]0x0D,[IntPtr]::Zero);[void][CnwlLayerWin32]::PostMessage($w.Handle,0x0101,[IntPtr]0x0D,[IntPtr]::Zero)}}
  Start-Sleep -Milliseconds 500
 }
 if(-not(Test-Path $result)){throw 'Layer probe produced no result.'};$lines=Get-Content $result;if($lines -notcontains 'status=PASS_CHARACTER_LAYER_PLACEMENT'){throw "Layer proof failed:`n$($lines -join "`n")"}
}finally{if(-not $p.HasExited){Stop-Process $p.Id -Force -ErrorAction SilentlyContinue};Remove-Item Env:CNWL_YMM4_LAYER_DIR -ErrorAction SilentlyContinue}
