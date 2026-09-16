param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$resultPath=Join-Path $OutputDir 'result.txt'
Remove-Item $resultPath -Force -ErrorAction SilentlyContinue
$exe=Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe'
$pluginDll=Join-Path $Ymm4Dir 'user\plugin\Ymm4DetachedJsonSaveProbe\Ymm4DetachedJsonSaveProbe.dll'
if(-not(Test-Path $exe)){throw "YMM4 executable not found: $exe"}
if(-not(Test-Path $pluginDll)){throw "Probe DLL not found: $pluginDll"}
$env:CNWL_YMM4_DETACHED_JSON_SAVE_DIR=$OutputDir
$p=Start-Process -FilePath $exe -WorkingDirectory $Ymm4Dir -PassThru
try{
  for($i=0;$i -lt 160;$i++){
    if(Test-Path $resultPath){break}
    if($p.HasExited){break}
    Start-Sleep -Milliseconds 500
  }
  if(-not(Test-Path $resultPath)){throw 'Detached Json.Save probe did not produce result.txt.'}
  $result=Get-Content $resultPath
  if($result -notcontains 'status=PASS_DETACHED_PROJECT_JSON_SAVE_ROUNDTRIP'){throw "Detached Project Json.Save roundtrip did not pass.`n$($result -join "`n")"}
  foreach($required in @('archive_exists=True','live_state_untouched=True','source_byte_unchanged=True','json_reload_marker=True','host_reload_marker=True')){
    if($result -notcontains $required){throw "Missing required invariant: $required"}
  }
}finally{
  if(-not $p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_YMM4_DETACHED_JSON_SAVE_DIR -ErrorAction SilentlyContinue
}
