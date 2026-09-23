param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$env:CNWL_SUBTITLE_PRELAYOUT_OUTPUT=$OutputDir
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  $limit=[DateTime]::UtcNow.AddSeconds(90)
  $result=Join-Path $OutputDir 'result.json'
  while([DateTime]::UtcNow -lt $limit -and -not$p.HasExited -and -not(Test-Path $result)) {
    Start-Sleep -Milliseconds 350
  }
  if(-not(Test-Path $result)){throw 'No native result'}
  $r=Get-Content -Raw $result|ConvertFrom-Json
  Get-Content $result
  if(Test-Path (Join-Path $OutputDir 'surface.json')){Get-Content (Join-Path $OutputDir 'surface.json')}

  if($r.schema-ne'cnwl.subtitle-prelayout-surface.v1' -or
     $r.status-ne'PASS_SUBTITLE_PRELAYOUT_SURFACE_INVENTORY' -or
     $r.host-ne'4.56.1.0 Lite' -or
     $r.sourceHead-ne$env:GITHUB_SHA -or
     $r.error){throw 'Behavior result rejected'}

  foreach($f in $r.requirements){
    if($f.passed-cne$true){throw "Missing/failed $($f.id)"}
  }
  Write-Output "PASS_SUBTITLE_PRELAYOUT_SURFACE_INVENTORY: $($r.requirements.Count) required assertions"
}
finally {
  if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_SUBTITLE_PRELAYOUT_OUTPUT -ErrorAction SilentlyContinue
}
