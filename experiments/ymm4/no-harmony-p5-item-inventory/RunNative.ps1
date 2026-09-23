param(
  [Parameter(Mandatory)][string]$Ymm4Dir,
  [Parameter(Mandatory)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$result=Join-Path $OutputDir 'result.txt'
if(Test-Path $result){throw 'Refuse stale P5 inventory result'}

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class P5InventoryWindow {
 public delegate bool Callback(IntPtr w, IntPtr p);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Callback cb,IntPtr p);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr w);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr w,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr w,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr w,uint m,IntPtr wp,IntPtr lp);
}
'@

$env:CNWL_P5_ITEM_INVENTORY_DIR=$OutputDir
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  $script:hostPid=$p.Id
  $cb=[P5InventoryWindow+Callback]{
    param([IntPtr]$w,[IntPtr]$unused)
    [uint32]$owner=0
    [void][P5InventoryWindow]::GetWindowThreadProcessId($w,[ref]$owner)
    if($owner -eq $script:hostPid -and [P5InventoryWindow]::IsWindowVisible($w)){
      $s=New-Object System.Text.StringBuilder 1024
      [void][P5InventoryWindow]::GetWindowText($w,$s,1024)
      $title=$s.ToString()
      if($title -like '*Check for updates*' -or $title -like '*About YukkuriMovieMaker*'){
        [void][P5InventoryWindow]::PostMessage($w,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
      }elseif($title -eq 'Confirm' -or $title -eq 'Profile'){
        [void][P5InventoryWindow]::PostMessage($w,0x0100,[IntPtr]0x0D,[IntPtr]::Zero)
        [void][P5InventoryWindow]::PostMessage($w,0x0101,[IntPtr]0x0D,[IntPtr]::Zero)
      }
    }
    return $true
  }

  $deadline=[DateTime]::UtcNow.AddSeconds(90)
  while([DateTime]::UtcNow -lt $deadline -and -not $p.HasExited -and -not(Test-Path $result)){
    [void][P5InventoryWindow]::EnumWindows($cb,[IntPtr]::Zero)
    Start-Sleep -Milliseconds 400
  }

  if(-not(Test-Path $result)){
    'status=FAIL_P5_ITEM_INVENTORY_TIMEOUT'|Set-Content (Join-Path $OutputDir 'runner-failure.txt')
    throw 'No P5 item inventory result'
  }

  $lines=Get-Content $result
  $lines|Write-Host
  if(-not($lines -contains 'status=PASS_P5_ITEM_INVENTORY')){
    throw 'P5 item inventory failed'
  }
  if(-not($lines -contains 'no_harmony_loaded=True')){
    throw 'Harmony unexpectedly loaded'
  }
  if(-not(Test-Path (Join-Path $OutputDir 'inventory.json'))){
    throw 'inventory.json missing'
  }
} finally {
  if(-not $p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_P5_ITEM_INVENTORY_DIR -ErrorAction SilentlyContinue
}
