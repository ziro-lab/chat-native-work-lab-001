param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$env:CNWL_VOICEITEM_OBSERVER_OUTPUT=$OutputDir
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

  if($r.schema-ne'cnwl.voiceitem-pronunciation-observer.v1' -or
     $r.status-ne'PASS_VOICEITEM_PRONUNCIATION_OBSERVER' -or
     $r.host-ne'4.56.1.0 Lite' -or
     $r.sourceHead-ne$env:GITHUB_SHA -or
     $r.error){throw 'Behavior result rejected'}

  $req=@(
    'timeline_resolved',
    'voice_added_to_timeline',
    'voiceitem_is_inotifypropertychanged',
    'effect_is_inotifypropertychanged',
    'serif_change_notified',
    'hatsuon_change_notified',
    'jimaku_effects_change_notified',
    'effect_enabled_change_notified',
    'key_still_detectable_after_notifications',
    'voice_present_in_real_timeline'
  )
  if($r.requirements.Count-ne$req.Count){throw "Wrong requirement count: $($r.requirements.Count) != $($req.Count)"}
  foreach($id in $req){
    $f=@($r.requirements|Where-Object { $_.id -eq $id })
    if($f.Count-ne1-or$f[0].passed-cne$true){throw "Missing/failed $id"}
  }
  Write-Output "PASS_VOICEITEM_PRONUNCIATION_OBSERVER: $($req.Count) required assertions"
}
finally {
  if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_VOICEITEM_OBSERVER_OUTPUT -ErrorAction SilentlyContinue
}
