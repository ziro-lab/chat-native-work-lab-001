param(
    [Parameter(Mandatory=$true)][string]$Ymm4Dir,
    [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$resultPath=Join-Path $OutputDir 'result.txt'
$hostLog=Join-Path $OutputDir 'host-log.txt'
Remove-Item $resultPath,$hostLog -Force -ErrorAction SilentlyContinue

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class CnwlCommandTileWin32 {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
'@

function Get-CnwlWindows {
    $script:rows=@()
    $callback=[CnwlCommandTileWin32+EnumWindowsProc]{
        param([IntPtr]$hWnd,[IntPtr]$lParam)
        if([CnwlCommandTileWin32]::IsWindowVisible($hWnd)){
            $sb=New-Object System.Text.StringBuilder 1024
            [void][CnwlCommandTileWin32]::GetWindowText($hWnd,$sb,$sb.Capacity)
            $title=$sb.ToString()
            if(-not [string]::IsNullOrWhiteSpace($title)){
                $script:rows += [pscustomobject]@{Handle=$hWnd;Title=$title}
            }
        }
        return $true
    }
    [void][CnwlCommandTileWin32]::EnumWindows($callback,[IntPtr]::Zero)
    return $script:rows
}

$env:CNWL_YMM4_COMMAND_TILE_DIR=$OutputDir
$p=Start-Process -FilePath (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  for($i=0;$i -lt 180;$i++){
    if(Test-Path $resultPath){ break }
    if($p.HasExited){ break }

    foreach($w in (Get-CnwlWindows)){
      "tick=$i handle=$($w.Handle) title=$($w.Title)" | Add-Content $hostLog
      if($w.Title -like '*Check for updates*' -or $w.Title -like '*About YukkuriMovieMaker*'){
        [void][CnwlCommandTileWin32]::PostMessage($w.Handle,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
      } elseif($w.Title -eq 'Confirm'){
        [void][CnwlCommandTileWin32]::PostMessage($w.Handle,0x0100,[IntPtr]0x0D,[IntPtr]::Zero)
        [void][CnwlCommandTileWin32]::PostMessage($w.Handle,0x0101,[IntPtr]0x0D,[IntPtr]::Zero)
      }
    }
    Start-Sleep -Milliseconds 500
  }

  if(-not(Test-Path $resultPath)){
    if($p.HasExited){ throw "No result.txt; YMM4 exited with code $($p.ExitCode). See host-log.txt." }
    throw 'No result.txt; probe timed out. See host-log.txt.'
  }

  $result=Get-Content $resultPath
  $result
  if(-not($result -contains 'status=PASS_STANDARD_COMMAND_TILE_ROUTE')){ throw 'Standard command tile route probe did not pass' }

  foreach($required in @(
    'all_required_commands_routed=True',
    'undo_can_execute=True',
    'undo_removed_fixture=True',
    'redo_can_execute=True',
    'redo_restored_fixture=True',
    'seek_roundtrip_worked=True',
    'split_can_execute=True',
    'split_worked=True',
    'split_undo_worked=True',
    'midpoint_is_routed_command=True'
  )){
    if(-not($result -contains $required)){ throw "Missing $required" }
  }
} finally {
  if(-not $p.HasExited){ Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
  Remove-Item Env:CNWL_YMM4_COMMAND_TILE_DIR -ErrorAction SilentlyContinue
}
