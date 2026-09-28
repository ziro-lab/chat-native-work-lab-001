param(
    [Parameter(Mandatory=$true)][string]$Ymm4Dir,
    [Parameter(Mandatory=$true)][string]$SourceExe,
    [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$result=Join-Path $OutputDir 'p7-external-filedrop-result.txt'

Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class P7DropWin {
    public delegate bool E(IntPtr h,IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(E c,IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h,StringBuilder t,int m);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
}
'@

function Get-Windows {
    $script:windows=@()
    $cb=[P7DropWin+E]{
        param([IntPtr]$h,[IntPtr]$l)
        if([P7DropWin]::IsWindowVisible($h)){
            $s=New-Object System.Text.StringBuilder 1024
            [void][P7DropWin]::GetWindowText($h,$s,$s.Capacity)
            if($s.Length){$script:windows += [pscustomobject]@{H=$h;T=$s.ToString()}}
        }
        return $true
    }
    [void][P7DropWin]::EnumWindows($cb,[IntPtr]::Zero)
    $script:windows
}

$env:CNWL_P4_HANDS_ON_DIAG_DIR=$OutputDir
$env:CNWL_P4_HANDS_ON_SMOKE_CREATE_PROJECT='1'
$env:CNWL_P7_EXTERNAL_FILEDROP_SMOKE='1'
$env:CNWL_P7_EXTERNAL_FILEDROP_SOURCE_EXE=(Resolve-Path $SourceExe).Path

$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
    for($i=0;$i-lt180;$i++){
        if(Test-Path $result){break}
        foreach($w in (Get-Windows)){
            if($w.T -like '*Check for updates*' -or $w.T -like '*About YukkuriMovieMaker*'){
                [void][P7DropWin]::PostMessage($w.H,0x10,[IntPtr]::Zero,[IntPtr]::Zero)
            }
            elseif($w.T -eq 'Confirm'){
                [void][P7DropWin]::PostMessage($w.H,0x100,[IntPtr]0x0D,[IntPtr]::Zero)
                [void][P7DropWin]::PostMessage($w.H,0x101,[IntPtr]0x0D,[IntPtr]::Zero)
            }
        }
        Start-Sleep -Milliseconds 500
    }
    if(-not(Test-Path $result)){throw 'P7 full external FileDrop smoke produced no result'}
    Get-Content $result
    if(-not((Get-Content $result)-contains 'PASS_P7_EXTERNAL_FILEDROP_FULL')){
        if(Test-Path (Join-Path $OutputDir 'runtime.log')){
            Write-Host '=== runtime.log ==='
            Get-Content (Join-Path $OutputDir 'runtime.log')
        }
        throw 'P7 full external FileDrop smoke failed'
    }
}
finally {
    if(-not $p.HasExited){Stop-Process $p.Id -Force -ErrorAction SilentlyContinue}
    Remove-Item Env:CNWL_P4_HANDS_ON_DIAG_DIR -ErrorAction SilentlyContinue
    Remove-Item Env:CNWL_P4_HANDS_ON_SMOKE_CREATE_PROJECT -ErrorAction SilentlyContinue
    Remove-Item Env:CNWL_P7_EXTERNAL_FILEDROP_SMOKE -ErrorAction SilentlyContinue
    Remove-Item Env:CNWL_P7_EXTERNAL_FILEDROP_SOURCE_EXE -ErrorAction SilentlyContinue
}
