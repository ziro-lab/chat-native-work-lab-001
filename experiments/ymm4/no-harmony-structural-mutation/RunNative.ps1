param(
  [Parameter(Mandatory)][string]$Ymm4Dir,
  [Parameter(Mandatory)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$result=Join-Path $OutputDir 'result.txt'
if(Test-Path $result){throw 'Refuse stale result file'}

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class StructuralWindow {
 public delegate bool Callback(IntPtr w, IntPtr p);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Callback cb,IntPtr p);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr w);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr w,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr w,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr w,uint m,IntPtr wp,IntPtr lp);
}
'@

$env:CNWL_STRUCTURAL_MUTATION_DIR=$OutputDir
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  $script:hostPid=$p.Id
  $cb=[StructuralWindow+Callback]{
    param([IntPtr]$w,[IntPtr]$unused)
    [uint32]$owner=0
    [void][StructuralWindow]::GetWindowThreadProcessId($w,[ref]$owner)
    if($owner -eq $script:hostPid -and [StructuralWindow]::IsWindowVisible($w)){
      $s=New-Object System.Text.StringBuilder 1024
      [void][StructuralWindow]::GetWindowText($w,$s,1024)
      $title=$s.ToString()
      if($title -like '*Check for updates*' -or $title -like '*About YukkuriMovieMaker*'){
        [void][StructuralWindow]::PostMessage($w,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
      }elseif($title -eq 'Confirm'){
        [void][StructuralWindow]::PostMessage($w,0x0100,[IntPtr]0x0D,[IntPtr]::Zero)
        [void][StructuralWindow]::PostMessage($w,0x0101,[IntPtr]0x0D,[IntPtr]::Zero)
      }
    }
    return $true
  }

  $deadline=[DateTime]::UtcNow.AddSeconds(150)
  while([DateTime]::UtcNow -lt $deadline -and -not $p.HasExited -and -not(Test-Path $result)){
    [void][StructuralWindow]::EnumWindows($cb,[IntPtr]::Zero)
    Start-Sleep -Milliseconds 500
  }

  if(-not(Test-Path $result)){
    'status=FAIL_STRUCTURAL_TIMEOUT_OR_HOST_EXIT'|Set-Content (Join-Path $OutputDir 'runner-failure.txt')
    throw 'No structural mutation result'
  }

  $lines=Get-Content $result
  $lines|Write-Host
  if(-not($lines -contains 'status=PASS_STRUCTURAL_MUTATION_SEMANTICS')){throw 'Structural mutation assertions failed'}
  if($lines|Where-Object{$_ -match '=False$'}){throw 'False structural assertion'}
  if(@($lines|Where-Object{$_ -match '=True$'}).Count -lt 15){throw 'Insufficient structural assertions'}
} finally {
  if(-not $p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_STRUCTURAL_MUTATION_DIR -ErrorAction SilentlyContinue
}
