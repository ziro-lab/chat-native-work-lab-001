param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$result=Join-Path $OutputDir 'result.txt'
$env:CNWL_LAYERPATAN_PATCH_SURFACE_DIR=$OutputDir
$p=Start-Process -FilePath (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try{
  for($i=0;$i -lt 160;$i++){
    if(Test-Path $result){break}
    if($p.HasExited){break}
    Start-Sleep -Milliseconds 500
  }
  if(-not(Test-Path $result)){throw 'no result.txt'}
  Get-Content $result
  if(-not((Get-Content $result)-contains 'status=PASS_LAYERPATAN_PATCH_SURFACE')){throw 'probe failed'}
}finally{
  if(-not $p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_LAYERPATAN_PATCH_SURFACE_DIR -ErrorAction SilentlyContinue
}