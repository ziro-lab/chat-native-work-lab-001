param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir)
$ErrorActionPreference='Stop'; New-Item -ItemType Directory -Force $OutputDir | Out-Null
$result=Join-Path $OutputDir 'result.txt'; Remove-Item $result -Force -ErrorAction SilentlyContinue
$env:CNWL_YMM4_LAYER_DIR=$OutputDir; $p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  for($i=0;$i -lt 120 -and -not(Test-Path $result) -and -not $p.HasExited;$i++){Start-Sleep -Milliseconds 500}
  if(-not(Test-Path $result)){throw 'Layer probe produced no result.'}
  $lines=Get-Content $result
  if($lines -notcontains 'status=PASS_CHARACTER_LAYER_PLACEMENT'){throw "Layer proof failed:`n$($lines -join "`n")"}
} finally {if(-not $p.HasExited){Stop-Process $p.Id -Force -ErrorAction SilentlyContinue};Remove-Item Env:CNWL_YMM4_LAYER_DIR -ErrorAction SilentlyContinue}
