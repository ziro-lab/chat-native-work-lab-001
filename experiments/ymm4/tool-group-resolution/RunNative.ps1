param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$env:CNWL_YMM4_GROUP_DIR=$OutputDir
$p=Start-Process -FilePath (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  for($i=0;$i -lt 120;$i++){ if(Test-Path (Join-Path $OutputDir 'result.txt')){break}; if($p.HasExited){break}; Start-Sleep -Milliseconds 500 }
  $r=Join-Path $OutputDir 'result.txt'
  if(-not(Test-Path $r)){throw 'No result.txt'}
  Get-Content $r
  if(-not((Get-Content $r) -contains 'status=PASS_TOOL_GROUP_OBSERVED')){throw 'Tool group observation did not pass'}
} finally {
  if(-not $p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_YMM4_GROUP_DIR -ErrorAction SilentlyContinue
}
