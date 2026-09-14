param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$hostLog=Join-Path $OutputDir 'host-windows.txt';Remove-Item $hostLog -Force -ErrorAction SilentlyContinue

Add-Type -TypeDefinition @'
using System;using System.Text;using System.Runtime.InteropServices;
public static class CnwlTemplateIdWin32{
 public delegate bool EnumWindowsProc(IntPtr hWnd,IntPtr lParam);
 [DllImport("user32.dll")]public static extern bool EnumWindows(EnumWindowsProc callback,IntPtr lParam);
 [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr hWnd);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int GetWindowText(IntPtr hWnd,StringBuilder text,int maxCount);
 [DllImport("user32.dll")]public static extern bool PostMessage(IntPtr hWnd,uint msg,IntPtr wParam,IntPtr lParam);
}
'@
function Get-CnwlWindows{
 $script:rows=@();$cb=[CnwlTemplateIdWin32+EnumWindowsProc]{param([IntPtr]$h,[IntPtr]$l)
  if([CnwlTemplateIdWin32]::IsWindowVisible($h)){$s=New-Object System.Text.StringBuilder 1024;[void][CnwlTemplateIdWin32]::GetWindowText($h,$s,$s.Capacity);$t=$s.ToString();if(-not[string]::IsNullOrWhiteSpace($t)){$script:rows+=[pscustomobject]@{Handle=$h;Title=$t}}};return $true};
 [void][CnwlTemplateIdWin32]::EnumWindows($cb,[IntPtr]::Zero);return $script:rows
}
function Run-Phase([string]$Phase,[string]$Expected){
 $phaseResult=Join-Path $OutputDir "result-$Phase.txt";Remove-Item $phaseResult -Force -ErrorAction SilentlyContinue
 $env:CNWL_YMM4_TEMPLATE_ID_PHASE=$Phase
 $p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
 try{
  for($i=0;$i -lt 120 -and -not(Test-Path $phaseResult) -and -not $p.HasExited;$i++){
   foreach($w in(Get-CnwlWindows)){
    "phase=$Phase tick=$i title=$($w.Title)"|Add-Content $hostLog
    if($w.Title -like '*Check for updates*' -or $w.Title -like '*About YukkuriMovieMaker*'){[void][CnwlTemplateIdWin32]::PostMessage($w.Handle,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)}
    elseif($w.Title -eq 'Confirm'){[void][CnwlTemplateIdWin32]::PostMessage($w.Handle,0x0100,[IntPtr]0x0D,[IntPtr]::Zero);[void][CnwlTemplateIdWin32]::PostMessage($w.Handle,0x0101,[IntPtr]0x0D,[IntPtr]::Zero)}
   }
   Start-Sleep -Milliseconds 500
  }
  if(-not(Test-Path $phaseResult)){throw "Template identity phase '$Phase' produced no result."}
  $lines=Get-Content $phaseResult;if($lines -notcontains "status=$Expected"){throw "Template identity phase '$Phase' failed:`n$($lines -join "`n")"}
  return $lines
 }finally{
  if(-not $p.HasExited){Stop-Process $p.Id -Force -ErrorAction SilentlyContinue}
  $p.WaitForExit();Start-Sleep -Milliseconds 750
 }
}

$env:CNWL_YMM4_TEMPLATE_ID_DIR=$OutputDir
try{
 $write=Run-Phase 'write' 'PASS_WRITE_SAVED'
 $read=Run-Phase 'read' 'PASS_RESTART_AMBIGUITY'
 foreach($required in @('read_count=2','same_name_after_restart=True','same_scene_id_after_restart=True','same_path_after_restart=True','content_recovered=True','only_public_guid_property_is_scene_id=True')){if($read -notcontains $required){throw "Missing restart identity assertion: $required"}}
 @('status=PASS_TEMPLATE_IDENTITY_RESTART_AMBIGUITY') + $write + $read | Set-Content (Join-Path $OutputDir 'result.txt') -Encoding utf8
}finally{
 Remove-Item Env:CNWL_YMM4_TEMPLATE_ID_DIR -ErrorAction SilentlyContinue
 Remove-Item Env:CNWL_YMM4_TEMPLATE_ID_PHASE -ErrorAction SilentlyContinue
}
