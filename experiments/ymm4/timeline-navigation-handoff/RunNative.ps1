param(
 [Parameter(Mandatory=$true)][string]$Ymm4Dir,
 [Parameter(Mandatory=$true)][string]$OutputDir,
 [Parameter(Mandatory=$true)][string]$MediaPath
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$result=Join-Path $OutputDir 'result.json'
$hostLog=Join-Path $OutputDir 'host-log.txt'
Remove-Item $result,$hostLog -Force -ErrorAction SilentlyContinue

Add-Type -TypeDefinition @'
using System; using System.Text; using System.Runtime.InteropServices;
public static class CnwlNavWin32 {
 public delegate bool EnumWindowsProc(IntPtr h,IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb,IntPtr l);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
}
'@
function Windows {
 $script:rows=@()
 $cb=[CnwlNavWin32+EnumWindowsProc]{param([IntPtr]$h,[IntPtr]$l)
  if([CnwlNavWin32]::IsWindowVisible($h)){
   $s=New-Object Text.StringBuilder 1024;[void][CnwlNavWin32]::GetWindowText($h,$s,1024)
   if($s.Length){$script:rows += [pscustomobject]@{Handle=$h;Title=$s.ToString()}}
  };return $true}
 [void][CnwlNavWin32]::EnumWindows($cb,[IntPtr]::Zero);$script:rows
}
$env:CNWL_YMM4_NAV_HANDOFF_DIR=$OutputDir
$env:CNWL_YMM4_NAV_HANDOFF_MEDIA=(Resolve-Path $MediaPath).Path
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try{
 for($i=0;$i -lt 300;$i++){
  if(Test-Path $result){break};if($p.HasExited){break}
  foreach($w in Windows){
   "tick=$i title=$($w.Title)"|Add-Content $hostLog
   if($w.Title -like '*Check for updates*' -or $w.Title -like '*About YukkuriMovieMaker*'){[void][CnwlNavWin32]::PostMessage($w.Handle,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)}
   elseif($w.Title -eq 'Confirm'){[void][CnwlNavWin32]::PostMessage($w.Handle,0x0100,[IntPtr]0x0D,[IntPtr]::Zero);[void][CnwlNavWin32]::PostMessage($w.Handle,0x0101,[IntPtr]0x0D,[IntPtr]::Zero)}
  }
  Start-Sleep -Milliseconds 500
 }
 if(-not(Test-Path $result)){throw 'No navigation-handoff result; inspect host-log/error evidence.'}
 $r=Get-Content -Raw $result|ConvertFrom-Json
 Get-Content $result
 if($r.schema-cne'cnwl.timeline-navigation-handoff.v1'-or$r.status-cne'PASS_NAVIGATION_HANDOFF_OBSERVATION'-or$r.host-cne'4.55.1.1 Lite'){throw 'Navigation handoff observation rejected'}
 if($r.pluginFocusObserved-cne$true -or $r.scrollFramePublic-cne$true -or $r.containFrameInViewportPublic-cne$true){throw 'Required plugin-focus / public viewport observation missing'}
 Write-Output "PASS_NAVIGATION_HANDOFF_OBSERVATION preview_probe_usable=$($r.previewPixelProbeUsable) real_click_updates_preview=$($r.realTimelineClickUpdatesPreview) programmatic_focus_updates_preview=$($r.programmaticFocusUpdatesPreview) scrollframe_moves_viewport=$($r.scrollFrameMovesViewport)"
}finally{
 if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
 Remove-Item Env:CNWL_YMM4_NAV_HANDOFF_DIR,Env:CNWL_YMM4_NAV_HANDOFF_MEDIA -ErrorAction SilentlyContinue
}
