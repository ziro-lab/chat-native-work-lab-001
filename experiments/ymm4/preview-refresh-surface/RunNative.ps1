param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir)
$ErrorActionPreference='Stop'; New-Item -ItemType Directory -Force $OutputDir|Out-Null
$env:CNWL_YMM4_PREVIEW_DIR=$OutputDir
$p=Start-Process -FilePath (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try{
 for($i=0;$i -lt 140;$i++){if(Test-Path (Join-Path $OutputDir 'result.txt')){break};if($p.HasExited){break};Start-Sleep -Milliseconds 500}
 $r=Join-Path $OutputDir 'result.txt';if(-not(Test-Path $r)){throw 'No result.txt'};Get-Content $r
 if(-not((Get-Content $r)-contains 'status=PASS_PREVIEW_REFRESH_OBSERVATION')){throw 'Preview observation failed'}
}finally{if(-not $p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue};Remove-Item Env:CNWL_YMM4_PREVIEW_DIR -ErrorAction SilentlyContinue}
