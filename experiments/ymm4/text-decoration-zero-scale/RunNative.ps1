param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$env:CNWL_TEXT_DECORATION_ZERO_OUTPUT=$OutputDir
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  $limit=[DateTime]::UtcNow.AddSeconds(90)
  $result=Join-Path $OutputDir 'result.json'
  while([DateTime]::UtcNow -lt $limit -and -not $p.HasExited -and -not(Test-Path $result)) {
    Start-Sleep -Milliseconds 350
  }
  if(-not(Test-Path $result)){throw 'No native result'}
  $r=Get-Content -Raw $result|ConvertFrom-Json
  Get-Content $result
  if(Test-Path (Join-Path $OutputDir 'behavior.json')){Get-Content (Join-Path $OutputDir 'behavior.json')}

  if($r.schema-ne'cnwl.text-decoration-zero-scale.v1' -or
     $r.status-ne'PASS_TEXT_DECORATION_ZERO_SCALE' -or
     $r.host-ne'4.56.1.0 Lite' -or
     $r.sourceHead-ne$env:GITHUB_SHA -or
     $r.error){throw 'Behavior result rejected'}

  $req=@(
    'baseline_rendered',
    'visible_marker_rendered',
    'zero_scale_marker_rendered',
    'visible_marker_adds_width',
    'zero_scale_collapses_marker_width',
    'zero_scale_approximately_matches_baseline'
  )
  if($r.requirements.Count-ne$req.Count){throw "Wrong requirement count: $($r.requirements.Count) != $($req.Count)"}
  foreach($id in $req){
    $f=@($r.requirements|Where-Object { $_.id -eq $id })
    if($f.Count-ne1-or$f[0].passed-cne$true){throw "Missing/failed $id"}
  }
  Write-Output "PASS_TEXT_DECORATION_ZERO_SCALE: $($req.Count) required assertions"
}
finally {
  if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_TEXT_DECORATION_ZERO_OUTPUT -ErrorAction SilentlyContinue
}
