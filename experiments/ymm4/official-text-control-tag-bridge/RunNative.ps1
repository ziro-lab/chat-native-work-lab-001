param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$env:CNWL_OFFICIAL_CONTROL_TAG_OUTPUT=$OutputDir
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  $limit=[DateTime]::UtcNow.AddSeconds(120)
  $result=Join-Path $OutputDir 'result.json'
  while([DateTime]::UtcNow -lt $limit -and -not $p.HasExited -and -not(Test-Path $result)) {
    Start-Sleep -Milliseconds 350
  }
  if(-not(Test-Path $result)){throw 'No native result'}
  $r=Get-Content -Raw $result|ConvertFrom-Json
  Get-Content $result
  if(Test-Path (Join-Path $OutputDir 'behavior.json')){Get-Content (Join-Path $OutputDir 'behavior.json')}

  if($r.schema-ne'cnwl.official-control-tag-bridge.v1' -or
     $r.status-ne'PASS_OFFICIAL_CONTROL_TAG_BRIDGE' -or
     $r.host-ne'4.56.1.0 Lite' -or
     $r.sourceHead-ne$env:GITHUB_SHA -or
     $r.error){throw 'Behavior result rejected'}

  $req=@(
    'textsource_baseline_rendered',
    'textsource_valid_tag_rendered',
    'textsource_invalid_tag_rendered',
    'official_tag_is_layout_invisible_textsource',
    'invalid_tag_remains_layout_visible_textsource',
    'jimakusource_available',
    'jimakusource_baseline_rendered',
    'jimakusource_valid_tag_rendered',
    'official_tag_is_layout_invisible_jimaku',
    'voice_serif_retained_after_render'
  )
  if($r.requirements.Count-ne$req.Count){throw "Wrong requirement count: $($r.requirements.Count) != $($req.Count)"}
  foreach($id in $req){
    $f=@($r.requirements|Where-Object { $_.id -eq $id })
    if($f.Count-ne1-or$f[0].passed-cne$true){throw "Missing/failed $id"}
  }
  Write-Output "PASS_OFFICIAL_CONTROL_TAG_BRIDGE: $($req.Count) required assertions"
}
finally {
  if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_OFFICIAL_CONTROL_TAG_OUTPUT -ErrorAction SilentlyContinue
}
