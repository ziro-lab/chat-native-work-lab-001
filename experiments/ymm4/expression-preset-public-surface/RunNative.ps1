param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$result=Join-Path $OutputDir 'result.json'
Remove-Item $result -Force -ErrorAction SilentlyContinue
$env:CNWL_YMM4_EXPRESSION_PRESET_DIR=$OutputDir
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try{
 for($i=0;$i-lt240;$i++){
  if(Test-Path $result){break}
  if($p.HasExited){break}
  Start-Sleep -Milliseconds 500
 }
 if(-not(Test-Path $result)){
  if($p.HasExited){throw "No result; YMM4 exited $($p.ExitCode)"}
  throw 'No expression-preset surface result'
 }
 $r=Get-Content -Raw $result|ConvertFrom-Json
 Get-Content $result
 if($r.schema-cne'cnwl.expression-preset-surface.v1'-or$r.status-cne'PASS_EXPRESSION_PRESET_PUBLIC_SURFACE_DISCOVERY'-or$r.host-cne'4.55.1.1 Lite'){throw 'Expression preset discovery rejected'}
 Write-Output "PASS_EXPRESSION_PRESET_PUBLIC_SURFACE_DISCOVERY bare_face=$($r.bareFaceConstructed) complete_candidate=$($r.completeCandidateRoute) preset_types=$(@($r.presetTypes).Count) containers=$(@($r.presetContainers).Count) apply_candidates=$(@($r.presetApplicationCandidates).Count)"
}finally{
 if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
 Remove-Item Env:CNWL_YMM4_EXPRESSION_PRESET_DIR -ErrorAction SilentlyContinue
}
