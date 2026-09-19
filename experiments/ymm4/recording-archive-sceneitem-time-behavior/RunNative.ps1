param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir,
  [Parameter(Mandatory=$true)][string]$Fixture
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$result=Join-Path $OutputDir 'result.txt'
Remove-Item $result -Force -ErrorAction SilentlyContinue
$env:CNWL_YMM4_SCENEITEM_BEHAVIOR_DIR=$OutputDir
$env:CNWL_YMM4_SCENEITEM_BEHAVIOR_FIXTURE=(Resolve-Path $Fixture).Path
$p=Start-Process -FilePath (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try{
  for($i=0;$i -lt 220;$i++){
    if(Test-Path $result){break}
    if($p.HasExited){break}
    Start-Sleep -Milliseconds 500
  }
  if(-not(Test-Path $result)){throw 'SceneItem behavior probe did not report a result.'}
  $rows=Get-Content $result
  if($rows -notcontains 'status=PASS_SCENEITEM_TIME_BEHAVIOR'){throw "SceneItem behavior probe failed.`n$($rows -join "`n")"}
}finally{
  if(-not $p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_YMM4_SCENEITEM_BEHAVIOR_DIR,Env:CNWL_YMM4_SCENEITEM_BEHAVIOR_FIXTURE -ErrorAction SilentlyContinue
}
