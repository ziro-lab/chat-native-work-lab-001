param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$resultPath=Join-Path $OutputDir 'result.txt'
Remove-Item $resultPath -Force -ErrorAction SilentlyContinue
$exe=Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe'
$pluginDll=Join-Path $Ymm4Dir 'user\plugin\Ymm4SourceTimeIlProbe\Ymm4SourceTimeIlProbe.dll'
if(-not(Test-Path $exe)){throw "YMM4 executable not found: $exe"}
if(-not(Test-Path $pluginDll)){throw "Probe DLL not found: $pluginDll"}
$env:CNWL_YMM4_SOURCE_TIME_IL_DIR=$OutputDir
$p=Start-Process -FilePath $exe -WorkingDirectory $Ymm4Dir -PassThru
try{
  for($i=0;$i -lt 160;$i++){
    if(Test-Path $resultPath){break}
    if($p.HasExited){break}
    Start-Sleep -Milliseconds 500
  }
  if(-not(Test-Path $resultPath)){throw 'Source-time IL probe did not produce result.txt.'}
  $result=Get-Content $resultPath
  if($result -notcontains 'status=PASS_SOURCE_TIME_IL_SURFACE'){throw "Source-time IL surface did not pass.`n$($result -join "`n")"}
}finally{
  if(-not $p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_YMM4_SOURCE_TIME_IL_DIR -ErrorAction SilentlyContinue
}
