param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class CnwlUndoRouteWin32 {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
'@

function Get-CnwlWindows {
  $script:rows=@()
  $callback=[CnwlUndoRouteWin32+EnumWindowsProc]{
    param([IntPtr]$hWnd,[IntPtr]$lParam)
    if([CnwlUndoRouteWin32]::IsWindowVisible($hWnd)){
      $sb=New-Object System.Text.StringBuilder 1024
      [void][CnwlUndoRouteWin32]::GetWindowText($hWnd,$sb,$sb.Capacity)
      $title=$sb.ToString()
      if(-not [string]::IsNullOrWhiteSpace($title)){
        $script:rows += [pscustomobject]@{Handle=$hWnd;Title=$title}
      }
    }
    return $true
  }
  [void][CnwlUndoRouteWin32]::EnumWindows($callback,[IntPtr]::Zero)
  return $script:rows
}

$env:CNWL_TIMELINE_UNDO_PUBLIC_OUTPUT=$OutputDir
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  $limit=[DateTime]::UtcNow.AddSeconds(90)
  $result=Join-Path $OutputDir 'result.json'
  while([DateTime]::UtcNow-lt$limit -and -not$p.HasExited -and -not(Test-Path $result)){
    foreach($w in (Get-CnwlWindows)){
      "$([DateTime]::UtcNow.ToString('O')) title=$($w.Title)" | Add-Content (Join-Path $OutputDir 'host-windows.txt')
      if($w.Title -like '*Check for updates*' -or $w.Title -like '*About YukkuriMovieMaker*'){
        [void][CnwlUndoRouteWin32]::PostMessage($w.Handle,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
      } elseif($w.Title -eq 'Confirm'){
        [void][CnwlUndoRouteWin32]::PostMessage($w.Handle,0x0100,[IntPtr]0x0D,[IntPtr]::Zero)
        [void][CnwlUndoRouteWin32]::PostMessage($w.Handle,0x0101,[IntPtr]0x0D,[IntPtr]::Zero)
      }
    }
    Start-Sleep -Milliseconds 350
  }
  if(-not(Test-Path $result)){throw 'No native result'}

  $r=Get-Content -Raw $result|ConvertFrom-Json
  Get-Content $result
  foreach($name in @('progress.txt','host-windows.txt','observation.json')){
    $path=Join-Path $OutputDir $name
    if(Test-Path $path){Write-Output "--- $name ---"; Get-Content $path}
  }

  if($r.schema-ne'cnwl.timeline-tool-undo-manager-public-route.v1' -or
     $r.status-ne'PASS_TIMELINE_TOOL_UNDO_MANAGER_PUBLIC_ROUTE' -or
     $r.host-ne'4.56.1.0 Lite' -or
     $r.sourceHead-ne$env:GITHUB_SHA -or
     $r.error){throw 'Native result rejected'}

  $req=@(
    'host_set_timeline_tool_info_called',
    'timeline_info_timeline_nonnull',
    'timeline_info_undo_manager_nonnull',
    'undo_manager_add_command_public',
    'undo_manager_record_public',
    'undo_manager_undo_async_public',
    'undo_manager_redo_async_public'
  )
  if($r.requirements.Count-ne$req.Count){throw "Wrong requirement count: $($r.requirements.Count)"}
  foreach($id in $req){
    $found=@($r.requirements|Where-Object {$_.id-eq$id})
    if($found.Count-ne1-or$found[0].passed-cne$true){throw "Missing/failed $id"}
  }

  Write-Output "PASS_TIMELINE_TOOL_UNDO_MANAGER_PUBLIC_ROUTE_E2E"
}
finally {
  if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_TIMELINE_UNDO_PUBLIC_OUTPUT -ErrorAction SilentlyContinue
}
