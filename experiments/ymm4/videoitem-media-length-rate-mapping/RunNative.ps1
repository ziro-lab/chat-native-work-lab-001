param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir,[Parameter(Mandatory=$true)][string]$MediaPath)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$resultPath=Join-Path $OutputDir 'result.txt'
Remove-Item $resultPath -Force -ErrorAction SilentlyContinue
$exe=Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe'
$pluginDll=Join-Path $Ymm4Dir 'user\plugin\Ymm4MediaLengthRateProbe\Ymm4MediaLengthRateProbe.dll'
if(-not(Test-Path $exe)){throw "YMM4 executable not found: $exe"}
if(-not(Test-Path $pluginDll)){throw "Probe DLL not found: $pluginDll"}
if(-not(Test-Path $MediaPath)){throw "Media fixture not found: $MediaPath"}
$env:CNWL_YMM4_MEDIA_LENGTH_RATE_DIR=$OutputDir
$env:CNWL_YMM4_MEDIA_FIXTURE=(Resolve-Path $MediaPath).Path
$p=Start-Process -FilePath $exe -WorkingDirectory $Ymm4Dir -PassThru
try{
  for($i=0;$i -lt 180;$i++){
    if(Test-Path $resultPath){break}
    if($p.HasExited){break}
    Start-Sleep -Milliseconds 500
  }
  if(-not(Test-Path $resultPath)){throw 'Media length rate probe did not produce result.txt.'}
  $result=Get-Content $resultPath
  if($result -notcontains 'status=PASS_MEDIA_LENGTH_RATE_MAPPING'){throw "Media length rate mapping did not pass.`n$($result -join "`n")"}
}finally{
  if(-not $p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_YMM4_MEDIA_LENGTH_RATE_DIR -ErrorAction SilentlyContinue
  Remove-Item Env:CNWL_YMM4_MEDIA_FIXTURE -ErrorAction SilentlyContinue
}
