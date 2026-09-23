param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null

$port=50125
$serverScript=Join-Path $PSScriptRoot 'fake_voicevox.py'
$server=Start-Process python -ArgumentList @($serverScript,'--port',$port,'--output',$OutputDir) -PassThru -WindowStyle Hidden
try {
  $ready=Join-Path $OutputDir 'fake-server-ready.txt'
  $deadline=[DateTime]::UtcNow.AddSeconds(20)
  while([DateTime]::UtcNow-lt$deadline -and -not(Test-Path $ready) -and -not$server.HasExited){
    Start-Sleep -Milliseconds 200
  }
  if(-not(Test-Path $ready)){throw 'Fake VOICEVOX server did not start'}

  $env:CNWL_VOICEITEM_UNDO_OUTPUT=$OutputDir
  $env:CNWL_FAKE_VOICEVOX_URL="http://127.0.0.1:$port"

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

    $progressPath=Join-Path $OutputDir 'progress.txt'
    if(Test-Path $progressPath){
      Write-Output '--- progress ---'
      Get-Content $progressPath
    }

    $obsPath=Join-Path $OutputDir 'undo-redo-observation.json'
    if(Test-Path $obsPath){
      Write-Output '--- undo redo observation ---'
      Get-Content $obsPath
    }

    $requestsPath=Join-Path $OutputDir 'fake-server-requests.jsonl'
    if(Test-Path $requestsPath){
      Write-Output '--- fake server requests ---'
      Get-Content $requestsPath
    }

    if($r.schema-ne'cnwl.voiceitem-correction-undo-redo.v1' -or
       $r.status-ne'PASS_VOICEITEM_CORRECTION_UNDO_REDO' -or
       $r.host-ne'4.56.1.0 Lite' -or
       $r.sourceHead-ne$env:GITHUB_SHA -or
       $r.error){throw 'Native Undo/Redo result rejected'}

    $req=@(
      'timeline_resolved',
      'host_undo_manager_acquired',
      'fake_engine_character_resolved',
      'builtin_voicevox_speaker_constructed',
      'fake_engine_registered',
      'voice_added_to_real_timeline',
      'voice_present_in_real_timeline',
      'real_voiceitem_file_path_available',
      'baseline_pause_nonzero',
      'corrected_pause_zero_before_synthesis',
      'corrected_synthesis_kept_pause_zero',
      'baseline_and_corrected_wav_differ',
      'baseline_snapshot_attached',
      'correction_committed_as_single_record',
      'corrected_state_after_apply',
      'timeline_view_found_for_history_input',
      'manager_public_undo_available',
      'manager_public_redo_available',
      'one_user_undo_event',
      'one_plugin_undo_callback',
      'undo_restores_baseline_pronounce',
      'undo_restores_baseline_wav',
      'one_user_redo_event',
      'one_plugin_redo_callback',
      'redo_restores_corrected_pronounce',
      'redo_restores_corrected_wav',
      'second_undo_restores_baseline'
    )

    if($r.requirements.Count-ne$req.Count){throw "Wrong requirement count: $($r.requirements.Count)"}
    foreach($id in $req){
      $found=@($r.requirements|Where-Object {$_.id-eq$id})
      if($found.Count-ne1-or$found[0].passed-cne$true){throw "Missing/failed $id"}
    }

    if(-not(Test-Path $obsPath)){throw 'Undo/Redo observation missing'}
    $obs=Get-Content -Raw $obsPath|ConvertFrom-Json
    if($obs.baseline.wavSha256-eq$obs.corrected.wavSha256){throw 'Baseline/corrected WAV hashes unexpectedly equal'}
    if([double]$obs.baseline.pauseVowelLength-le0){throw 'Baseline pause was not non-zero'}
    if([double]$obs.corrected.pauseVowelLength-ne0){throw 'Corrected pause was not zero'}
    if($obs.history.recorded-ne1){throw "Expected one correction record, got $($obs.history.recorded)"}
    if($obs.history.undoed-ne2){throw "Expected two undo events, got $($obs.history.undoed)"}
    if($obs.history.redoed-ne1){throw "Expected one redo event, got $($obs.history.redoed)"}
    if($obs.history.undoCallbacks-ne2){throw "Expected two undo callbacks, got $($obs.history.undoCallbacks)"}
    if($obs.history.redoCallbacks-ne1){throw "Expected one redo callback, got $($obs.history.redoCallbacks)"}
    if($obs.history.finalWavSha256-ne$obs.baseline.wavSha256){throw 'Final WAV did not return to baseline'}
    if([double]$obs.history.finalPause-ne[double]$obs.baseline.pauseVowelLength){throw 'Final Pronounce did not return to baseline'}

    Write-Output "PASS_VOICEITEM_CORRECTION_UNDO_REDO_E2E"
  }
  finally {
    if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  }
}
finally {
  if(-not$server.HasExited){Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_VOICEITEM_UNDO_OUTPUT -ErrorAction SilentlyContinue
  Remove-Item Env:CNWL_FAKE_VOICEVOX_URL -ErrorAction SilentlyContinue
}
