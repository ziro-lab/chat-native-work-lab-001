param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$resultPath=Join-Path $OutputDir 'result.txt'
$createResultPath=Join-Path $OutputDir 'template-create-result.txt'
$motionResultPath=Join-Path $OutputDir 'charactor-motion-result.txt'
$hostLog=Join-Path $OutputDir 'host-windows.txt'
Remove-Item $resultPath,$createResultPath,$motionResultPath,$hostLog -Force -ErrorAction SilentlyContinue
$exe=Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe'
$pluginDll=Join-Path $Ymm4Dir 'user\plugin\Ymm4TemplateCloneProbe\Ymm4TemplateCloneProbe.dll'
if(-not(Test-Path $exe)){throw "YMM4 executable not found: $exe"}
if(-not(Test-Path $pluginDll)){throw "Probe DLL not found: $pluginDll"}
Add-Type -TypeDefinition @'
using System; using System.Text; using System.Runtime.InteropServices;
public static class CnwlTemplateWin32 {
 public delegate bool EnumWindowsProc(IntPtr hWnd,IntPtr lParam);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback,IntPtr lParam);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd,StringBuilder text,int maxCount);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd,uint msg,IntPtr wParam,IntPtr lParam);
}
'@
function Get-CnwlWindows {
 $script:rows=@()
 $callback=[CnwlTemplateWin32+EnumWindowsProc]{
  param([IntPtr]$hWnd,[IntPtr]$lParam)
  if([CnwlTemplateWin32]::IsWindowVisible($hWnd)){
   $sb=New-Object System.Text.StringBuilder 1024
   [void][CnwlTemplateWin32]::GetWindowText($hWnd,$sb,$sb.Capacity)
   $t=$sb.ToString()
   if(-not[string]::IsNullOrWhiteSpace($t)){$script:rows += [pscustomobject]@{Handle=$hWnd;Title=$t}}
  }
  return $true
 }
 [void][CnwlTemplateWin32]::EnumWindows($callback,[IntPtr]::Zero)
 return $script:rows
}
$env:CNWL_YMM4_TEMPLATE_CLONE_DIR=$OutputDir
$p=Start-Process -FilePath $exe -WorkingDirectory $Ymm4Dir -PassThru
try{
  for($i=0;$i -lt 180 -and (-not(Test-Path $resultPath) -or -not(Test-Path $createResultPath) -or -not(Test-Path $motionResultPath)) -and -not $p.HasExited;$i++){
    foreach($w in (Get-CnwlWindows)){
      "tick=$i handle=$($w.Handle) title=$($w.Title)" | Add-Content $hostLog
      if($w.Title -like '*Check for updates*' -or $w.Title -like '*About YukkuriMovieMaker*'){
        [void][CnwlTemplateWin32]::PostMessage($w.Handle,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
      } elseif($w.Title -eq 'Confirm') {
        [void][CnwlTemplateWin32]::PostMessage($w.Handle,0x0100,[IntPtr]0x0D,[IntPtr]::Zero)
        [void][CnwlTemplateWin32]::PostMessage($w.Handle,0x0101,[IntPtr]0x0D,[IntPtr]::Zero)
      }
    }
    Start-Sleep -Milliseconds 500
  }
  if(-not(Test-Path $resultPath)){throw 'Template clone fidelity probe did not produce result.txt.'}
  if(-not(Test-Path $createResultPath)){throw 'Template create fidelity probe did not produce template-create-result.txt.'}
  if(-not(Test-Path $motionResultPath)){throw 'CharactorMotion fidelity probe did not produce charactor-motion-result.txt.'}
  $result=Get-Content $resultPath
  if($result -notcontains 'status=PASS_TEMPLATE_CLONE_FIDELITY_DISCOVERY'){throw "Template clone fidelity probe did not pass.`n$($result -join "`n")"}
  $createResult=Get-Content $createResultPath
  if($createResult -notcontains 'status=PASS_TEMPLATE_CREATE_FIDELITY'){throw "Template create fidelity probe did not pass.`n$($createResult -join "`n")"}
  $motionResult=Get-Content $motionResultPath
  if($motionResult -notcontains 'status=PASS_CHARACTOR_MOTION_FIDELITY'){throw "CharactorMotion fidelity probe did not pass.`n$($motionResult -join "`n")"}
}finally{
  if(-not $p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_YMM4_TEMPLATE_CLONE_DIR -ErrorAction SilentlyContinue
}
