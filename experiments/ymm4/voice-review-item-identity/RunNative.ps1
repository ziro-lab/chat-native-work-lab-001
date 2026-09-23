param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$env:CNWL_VOICE_REVIEW_IDENTITY_OUTPUT=$OutputDir
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  $limit=[DateTime]::UtcNow.AddSeconds(90)
  $result=Join-Path $OutputDir 'result.json'
  while([DateTime]::UtcNow-lt$limit -and -not$p.HasExited -and -not(Test-Path $result)){
    Start-Sleep -Milliseconds 350
  }
  if(-not(Test-Path $result)){throw 'No native result'}
  $r=Get-Content -Raw $result|ConvertFrom-Json
  Get-Content $result
  if(Test-Path (Join-Path $OutputDir 'behavior.json')){Get-Content (Join-Path $OutputDir 'behavior.json')}
  if($r.schema-ne'cnwl.voice-review-item-identity.v1' -or
     $r.status-ne'PASS_VOICE_REVIEW_ITEM_IDENTITY' -or
     $r.host-ne'4.56.1.0 Lite' -or
     $r.sourceHead-ne$env:GITHUB_SHA -or $r.error){throw 'Behavior result rejected'}

  $req=@(
    'timeline_resolved',
    'identity_surface_inventoried',
    'voice_added_to_real_timeline',
    'observable_identity_stable_after_timeline_add',
    'serialized_guid_observed_unique',
    'serialized_guid_stable_across_basic_edits'
  )
  if($r.requirements.Count-ne$req.Count){throw "Wrong requirement count: $($r.requirements.Count)"}
  foreach($id in $req){
    $f=@($r.requirements|Where-Object {$_.id-eq$id})
    if($f.Count-ne1-or$f[0].passed-cne$true){throw "Missing/failed $id"}
  }
  Write-Output "PASS_VOICE_REVIEW_ITEM_IDENTITY: $($req.Count) required assertions"
}
finally {
  if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_VOICE_REVIEW_IDENTITY_OUTPUT -ErrorAction SilentlyContinue
}
