param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$result=Join-Path $OutputDir 'result.txt';$hostLog=Join-Path $OutputDir 'host-windows.txt'
Remove-Item $result,$hostLog -Force -ErrorAction SilentlyContinue

Add-Type -TypeDefinition @'
using System; using System.Text; using System.Runtime.InteropServices;
public static class CnwlPlayheadWin32 {
 public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd,StringBuilder text,int maxCount);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd,uint msg,IntPtr wParam,IntPtr lParam);
}
'@
function Get-CnwlWindows {
 $script:rows=@();$callback=[CnwlPlayheadWin32+EnumWindowsProc]{param([IntPtr]$hWnd,[IntPtr]$lParam)
  if([CnwlPlayheadWin32]::IsWindowVisible($hWnd)){$sb=New-Object System.Text.StringBuilder 1024;[void][CnwlPlayheadWin32]::GetWindowText($hWnd,$sb,$sb.Capacity);$title=$sb.ToString();if(-not[string]::IsNullOrWhiteSpace($title)){$script:rows += [pscustomobject]@{Handle=$hWnd;Title=$title}}};return $true};
 [void][CnwlPlayheadWin32]::EnumWindows($callback,[IntPtr]::Zero);return $script:rows
}

$env:CNWL_YMM4_PLAYHEAD_DIR=$OutputDir
$exe=Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe';$p=Start-Process -FilePath $exe -WorkingDirectory $Ymm4Dir -PassThru
try {
 for($i=0;$i -lt 120;$i++) {
  if(Test-Path $result){break};if($p.HasExited){break}
  foreach($w in (Get-CnwlWindows)){
   "tick=$i handle=$($w.Handle) title=$($w.Title)"|Add-Content $hostLog
   if($w.Title -like '*Check for updates*' -or $w.Title -like '*About YukkuriMovieMaker*'){[void][CnwlPlayheadWin32]::PostMessage($w.Handle,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)}
   elseif($w.Title -eq 'Confirm'){[void][CnwlPlayheadWin32]::PostMessage($w.Handle,0x0100,[IntPtr]0x0D,[IntPtr]::Zero);[void][CnwlPlayheadWin32]::PostMessage($w.Handle,0x0101,[IntPtr]0x0D,[IntPtr]::Zero)}
  }
  Start-Sleep -Milliseconds 500
 }
 if(-not(Test-Path $result)){throw 'Playhead probe did not produce result.txt.'}
 $lines=Get-Content $result;$status=$lines|Where-Object{$_ -like 'status=*'}|Select-Object -First 1
 if($status -notin @('status=PASS_PLAYHEAD_QUICK_DROP','status=DISCOVERY_NO_PUBLIC_FRAME','status=DISCOVERY_PUBLIC_FRAME_READ_ONLY')){throw "Unexpected playhead result:`n$($lines -join "`n")"}
} finally {if(-not $p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue};Remove-Item Env:CNWL_YMM4_PLAYHEAD_DIR -ErrorAction SilentlyContinue}
