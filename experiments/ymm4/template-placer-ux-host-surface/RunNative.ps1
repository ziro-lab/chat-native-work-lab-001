param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$result=Join-Path $OutputDir 'result.txt'
$surface=Join-Path $OutputDir 'surface.txt'
$behavior=Join-Path $OutputDir 'behavior.txt'
$hostLog=Join-Path $OutputDir 'host-windows.txt'
Remove-Item $result,$surface,$behavior,$hostLog -Force -ErrorAction SilentlyContinue
Add-Type -TypeDefinition @'
using System; using System.Text; using System.Runtime.InteropServices;
public static class CnwlUxSurfaceWin32 {
 public delegate bool EnumWindowsProc(IntPtr hWnd,IntPtr lParam);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback,IntPtr lParam);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd,StringBuilder text,int maxCount);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd,uint msg,IntPtr wParam,IntPtr lParam);
}
'@
function Get-CnwlWindows {
 $script:rows=@()
 $callback=[CnwlUxSurfaceWin32+EnumWindowsProc]{
  param([IntPtr]$hWnd,[IntPtr]$lParam)
  if([CnwlUxSurfaceWin32]::IsWindowVisible($hWnd)){
   $sb=New-Object System.Text.StringBuilder 1024
   [void][CnwlUxSurfaceWin32]::GetWindowText($hWnd,$sb,$sb.Capacity)
   $t=$sb.ToString()
   if(-not[string]::IsNullOrWhiteSpace($t)){$script:rows += [pscustomobject]@{Handle=$hWnd;Title=$t}}
  }
  return $true
 }
 [void][CnwlUxSurfaceWin32]::EnumWindows($callback,[IntPtr]::Zero)
 return $script:rows
}
$env:CNWL_YMM4_TP_UX_SURFACE_DIR=$OutputDir
$p=Start-Process -FilePath (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  for($i=0;$i -lt 180 -and (-not(Test-Path $result) -or -not(Test-Path $behavior)) -and -not $p.HasExited;$i++){
    foreach($w in (Get-CnwlWindows)){
      "tick=$i handle=$($w.Handle) title=$($w.Title)" | Add-Content $hostLog
      if($w.Title -like '*Check for updates*' -or $w.Title -like '*About YukkuriMovieMaker*'){
        [void][CnwlUxSurfaceWin32]::PostMessage($w.Handle,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
      } elseif($w.Title -eq 'Confirm') {
        [void][CnwlUxSurfaceWin32]::PostMessage($w.Handle,0x0100,[IntPtr]0x0D,[IntPtr]::Zero)
        [void][CnwlUxSurfaceWin32]::PostMessage($w.Handle,0x0101,[IntPtr]0x0D,[IntPtr]::Zero)
      }
    }
    Start-Sleep -Milliseconds 500
  }
  if(-not(Test-Path $result)){ throw 'UX host surface probe did not report a result.' }
  $rows=Get-Content $result
  if($rows -notcontains 'status=PASS_TEMPLATE_PLACER_UX_HOST_SURFACE'){ throw "UX host surface probe failed.`n$($rows -join "`n")" }
  if(-not(Test-Path $surface)){ throw 'surface.txt missing.' }
  if(-not(Test-Path $behavior)){ throw 'behavior.txt missing.' }
  $behaviorRows=Get-Content $behavior
  if($behaviorRows -notcontains 'status=PASS_TEMPLATE_PLACER_UX_BEHAVIOR'){ throw "UX host behavior probe failed.`n$($behaviorRows -join "`n")" }
} finally {
  if(-not $p.HasExited){ Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
  Remove-Item Env:CNWL_YMM4_TP_UX_SURFACE_DIR -ErrorAction SilentlyContinue
}
