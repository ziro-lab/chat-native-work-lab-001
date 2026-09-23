param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null

$port=50126
$serverScript=Join-Path $PSScriptRoot 'fake_voicevox.py'
$server=Start-Process python -ArgumentList @($serverScript,'--port',$port,'--output',$OutputDir) -PassThru -WindowStyle Hidden
try {
  $ready=Join-Path $OutputDir 'fake-server-ready.txt'
  $deadline=[DateTime]::UtcNow.AddSeconds(20)
  while([DateTime]::UtcNow-lt$deadline -and -not(Test-Path $ready) -and -not$server.HasExited){
    Start-Sleep -Milliseconds 200
  }
  if(-not(Test-Path $ready)){throw 'Fake VOICEVOX server did not start'}

  $env:CNWL_RELOAD_REAPPLY_OUTPUT=$OutputDir
  $env:CNWL_FAKE_VOICEVOX_URL="http://127.0.0.1:$port"

  $p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
  try {
    $limit=[DateTime]::UtcNow.AddSeconds(180)
    $result=Join-Path $OutputDir 'result.json'
    while([DateTime]::UtcNow-lt$limit -and -not$p.HasExited -and -not(Test-Path $result)){
      Start-Sleep -Milliseconds 350
    }

    if(-not(Test-Path $result)){throw 'No native result'}
    $r=Get-Content -Raw $result|ConvertFrom-Json
    Get-Content $result

    $obsPath=Join-Path $OutputDir 'reload-reapply-observation.json'
    if(Test-Path $obsPath){
      Write-Output '--- reload reapply observation ---'
      Get-Content $obsPath
    }

    $requestsPath=Join-Path $OutputDir 'fake-server-requests.jsonl'
    if(Test-Path $requestsPath){
      Write-Output '--- fake server requests ---'
      Get-Content $requestsPath
    }

    if($r.schema-ne'cnwl.voiceitem-reload-reapply.v1' -or
       $r.status-ne'PASS_VOICEITEM_RELOAD_REAPPLY' -or
       $r.host-ne'4.56.1.0 Lite' -or
       $r.sourceHead-ne$env:GITHUB_SHA -or
       $r.error){throw 'Native reload-reapply result rejected'}

    $req=@(
      'timeline_resolved',
      'fake_engine_character_resolved',
      'builtin_voicevox_speaker_constructed',
      'fake_engine_registered',
      'voice_added_to_real_timeline',
      'durable_source_present_before_save',
      'initial_voice_file_generated',
      'synthetic_speaker_detached_before_save',
      'project_a_saved',
      'project_b_saved',
      'reloaded_is_new_object',
      'durable_source_survives_reload',
      'fixture_speaker_rebound_for_reapply',
      'reloaded_voice_parameter_available',
      'reloaded_voice_file_available',
      'controller_reapply_condition_true',
      'fresh_pronounce_reanalyzed_after_reload',
      'persisted_correction_resolved_to_zero',
      'reapplied_pronounce_attached',
      'reapplied_pause_zero',
      'reapplied_voice_file_exists'
    )

    if($r.requirements.Count-ne$req.Count){throw "Wrong requirement count: $($r.requirements.Count)"}
    foreach($id in $req){
      $found=@($r.requirements|Where-Object {$_.id-eq$id})
      if($found.Count-ne1-or$found[0].passed-cne$true){throw "Missing/failed $id"}
    }

    if(-not(Test-Path $obsPath)){throw 'Reload reapply observation missing'}
    $obs=Get-Content -Raw $obsPath|ConvertFrom-Json
    if([double]$obs.reapply.freshPause-le0){throw 'Fresh analysis pause was not non-zero'}
    if([double]$obs.reapply.finalPause-ne0.0){throw 'Final reapplied pause was not zero'}

    if(-not(Test-Path $requestsPath)){throw 'Fake server received no requests'}
    $requests=@(Get-Content $requestsPath|ForEach-Object { $_|ConvertFrom-Json })
    $posts=@($requests|Where-Object {$_.method-eq'POST'})
    $synthIndexes=@()
    for($i=0;$i-lt$posts.Count;$i++){
      if($posts[$i].path-eq'/synthesis'){$synthIndexes+=$i}
    }
    if($synthIndexes.Count-lt2){throw "Expected at least two synthesis requests, got $($synthIndexes.Count)"}

    $lastSynthIndex=$synthIndexes[-1]
    $prevSynthIndex=$synthIndexes[-2]
    $betweenAudio=@()
    if($lastSynthIndex-$prevSynthIndex-gt1){
      $betweenAudio=@($posts[($prevSynthIndex+1)..($lastSynthIndex-1)]|Where-Object {$_.path-eq'/audio_query'})
    }
    if($betweenAudio.Count-ne0){throw 'Unexpected /audio_query between baseline and corrected final syntheses'}

    $lastBody=$posts[$lastSynthIndex].body|ConvertFrom-Json
    $lastPause=[double]$lastBody.accent_phrases[0].pause_mora.vowel_length
    if($lastPause-ne0.0){throw "Final synthesis pause was not zero: $lastPause"}

    @{
      schema='cnwl.voiceitem-reload-reapply-e2e.v1'
      post_count=$posts.Count
      synthesis_count=$synthIndexes.Count
      final_pause_vowel_length=$lastPause
      audio_query_between_final_syntheses=$betweenAudio.Count
      voice_file_length=[int64]$obs.reapply.voiceFileLength
    }|ConvertTo-Json|Set-Content (Join-Path $OutputDir 'e2e.json')

    Write-Output "PASS_VOICEITEM_RELOAD_REAPPLY_E2E"
  }
  finally {
    if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  }
}
finally {
  if(-not$server.HasExited){Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_RELOAD_REAPPLY_OUTPUT -ErrorAction SilentlyContinue
  Remove-Item Env:CNWL_FAKE_VOICEVOX_URL -ErrorAction SilentlyContinue
}
