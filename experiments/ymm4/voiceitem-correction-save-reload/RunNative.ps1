param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$env:CNWL_VOICEITEM_SAVE_RELOAD_OUTPUT=$OutputDir

$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  $limit=[DateTime]::UtcNow.AddSeconds(150)
  $result=Join-Path $OutputDir 'result.json'
  while([DateTime]::UtcNow-lt$limit -and -not$p.HasExited -and -not(Test-Path $result)){
    Start-Sleep -Milliseconds 350
  }

  if(-not(Test-Path $result)){throw 'No native result'}
  $r=Get-Content -Raw $result|ConvertFrom-Json
  Get-Content $result

  $obsPath=Join-Path $OutputDir 'save-reload-observation.json'
  if(Test-Path $obsPath){
    Write-Output '--- save reload observation ---'
    Get-Content $obsPath
  }

  if($r.schema-ne'cnwl.voiceitem-correction-save-reload.v1' -or
     $r.status-ne'PASS_VOICEITEM_CORRECTION_SAVE_RELOAD' -or
     $r.host-ne'4.56.1.0 Lite' -or
     $r.sourceHead-ne$env:GITHUB_SHA -or
     $r.error){throw 'Native save/reload result rejected'}

  $req=@(
    'timeline_resolved',
    'public_save_project_available',
    'public_open_project_available',
    'voice_added_to_real_timeline',
    'voice_present_before_save',
    'marker_present_before_save',
    'assist_effect_present_before_save',
    'pronounce_zero_before_save',
    'project_a_saved',
    'project_a_contains_marker_source',
    'project_a_contains_assist_probe_tag',
    'live_state_mutated_away_from_a',
    'project_b_saved',
    'project_a_reopened',
    'reloaded_voice_found',
    'reloaded_serif_marker_exact',
    'reloaded_hatsuon_exact',
    'reloaded_assist_effect_found',
    'reloaded_assist_enabled',
    'reloaded_effect_enabled',
    'reloaded_probe_tag_exact',
    'reloaded_pronounce_safe_if_present',
    'durable_reapply_source_survives_reload'
  )

  if($r.requirements.Count-ne$req.Count){throw "Wrong requirement count: $($r.requirements.Count)"}
  foreach($id in $req){
    $found=@($r.requirements|Where-Object {$_.id-eq$id})
    if($found.Count-ne1-or$found[0].passed-cne$true){throw "Missing/failed $id"}
  }

  if(-not(Test-Path $obsPath)){throw 'Save/reload observation missing'}
  $obs=Get-Content -Raw $obsPath|ConvertFrom-Json
  if($obs.projectA.containsMarker-cne$true){throw 'Project A did not embed marker source'}
  if($obs.projectA.containsProbeTag-cne$true){throw 'Project A did not embed assist probe tag'}
  if($obs.reloaded.durableReapplySource-cne$true){throw 'Durable reapply source was not restored'}
  if($obs.reloaded.pronouncePersisted-eq$true -and [double]$obs.reloaded.pauseVowelLength-ne0.0){
    throw "Persisted Pronounce degraded: $($obs.reloaded.pauseVowelLength)"
  }

  Write-Output "PASS_VOICEITEM_CORRECTION_SAVE_RELOAD_E2E"
}
finally {
  if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_VOICEITEM_SAVE_RELOAD_OUTPUT -ErrorAction SilentlyContinue
}
