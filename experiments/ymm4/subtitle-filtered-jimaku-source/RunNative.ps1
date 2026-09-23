param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$env:CNWL_FILTERED_JIMAKU_OUTPUT=$OutputDir
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  $limit=[DateTime]::UtcNow.AddSeconds(90)
  $result=Join-Path $OutputDir 'result.json'
  while([DateTime]::UtcNow -lt $limit -and -not$p.HasExited -and -not(Test-Path $result)){Start-Sleep -Milliseconds 350}
  if(-not(Test-Path $result)){throw 'No native result'}
  $r=Get-Content -Raw $result|ConvertFrom-Json
  Get-Content $result
  if(Test-Path (Join-Path $OutputDir 'behavior.json')){Get-Content (Join-Path $OutputDir 'behavior.json')}
  if($r.schema-ne'cnwl.filtered-jimaku-source.v1' -or
     $r.status-ne'PASS_FILTERED_JIMAKU_SOURCE' -or
     $r.host-ne'4.56.1.0 Lite' -or
     $r.sourceHead-ne$env:GITHUB_SHA -or
     $r.error){throw 'Behavior result rejected'}
  $req=@(
    'baseline_rendered',
    'visible_marker_rendered',
    'filtered_effect_rendered',
    'effect_processor_created',
    'effect_processor_updated',
    'effect_received_rendered_input',
    'stored_serif_unchanged',
    'temporary_serif_filtered',
    'visible_marker_adds_width',
    'filtered_output_collapses_marker_width',
    'filtered_output_matches_baseline'
  )
  if($r.requirements.Count-ne$req.Count){throw "Wrong requirement count"}
  foreach($id in $req){
    $f=@($r.requirements|Where-Object{$_.id-eq$id})
    if($f.Count-ne1-or$f[0].passed-cne$true){throw "Missing/failed $id"}
  }
  Write-Output "PASS_FILTERED_JIMAKU_SOURCE: $($req.Count) required assertions"
}
finally {
  if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_FILTERED_JIMAKU_OUTPUT -ErrorAction SilentlyContinue
}
