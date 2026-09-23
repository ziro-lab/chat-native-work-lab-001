param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null

$port=50127
$serverScript=Join-Path $PSScriptRoot 'fake_voicevox.py'
$server=Start-Process python -ArgumentList @($serverScript,'--port',$port,'--output',$OutputDir) -PassThru -WindowStyle Hidden
try {
  $ready=Join-Path $OutputDir 'fake-server-ready.txt'
  $deadline=[DateTime]::UtcNow.AddSeconds(20)
  while([DateTime]::UtcNow-lt$deadline -and -not(Test-Path $ready) -and -not$server.HasExited){
    Start-Sleep -Milliseconds 200
  }
  if(-not(Test-Path $ready)){throw 'Fake VOICEVOX server did not start'}

  $env:CNWL_ASSIST_LIFECYCLE_OUTPUT=$OutputDir
  $env:CNWL_FAKE_VOICEVOX_URL="http://127.0.0.1:$port"

  $p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
  try {
    $limit=[DateTime]::UtcNow.AddSeconds(120)
    $result=Join-Path $OutputDir 'result.json'
    while([DateTime]::UtcNow-lt$limit -and -not$p.HasExited -and -not(Test-Path $result)){
      Start-Sleep -Milliseconds 350
    }
    if(-not(Test-Path $result)){throw 'No native result'}

    $r=Get-Content -Raw $result|ConvertFrom-Json
    Get-Content $result

    $obsPath=Join-Path $OutputDir 'assist-lifecycle-observation.json'
    if(Test-Path $obsPath){Write-Output '--- assist lifecycle observation ---'; Get-Content $obsPath}
    $requestsPath=Join-Path $OutputDir 'fake-server-requests.jsonl'
    if(Test-Path $requestsPath){Write-Output '--- fake server requests ---'; Get-Content $requestsPath}

    if($r.schema-ne'cnwl.voiceitem-assist-disable-remove.v1' -or
       $r.status-ne'PASS_VOICEITEM_ASSIST_DISABLE_REMOVE' -or
       $r.host-ne'4.56.1.0 Lite' -or
       $r.sourceHead-ne$env:GITHUB_SHA -or
       $r.error){throw 'Native result rejected'}

    $req=@(
      'timeline_resolved',
      'fake_engine_character_resolved',
      'builtin_voicevox_speaker_constructed',
      'fake_engine_registered',
      'voice_added_to_real_timeline',
      'voice_present_in_real_timeline',
      'assist_initially_active',
      'real_voiceitem_file_path_available',
      'enabled_applies_correction',
      'enabled_corrected_wav',
      'disable_notification_observed',
      'assist_inactive_when_disabled',
      'disable_restores_baseline_pronounce',
      'disable_restores_different_wav',
      'reenable_notification_observed',
      'assist_active_after_reenable',
      'reenable_reapplies_correction',
      'reenable_restores_same_corrected_wav',
      'remove_notification_observed',
      'assist_inactive_when_removed',
      'remove_restores_baseline_pronounce',
      'remove_restores_same_baseline_wav',
      'corrected_and_baseline_hashes_distinct'
    )

    if($r.requirements.Count-ne$req.Count){throw "Wrong requirement count: $($r.requirements.Count)"}
    foreach($id in $req){
      $found=@($r.requirements|Where-Object {$_.id-eq$id})
      if($found.Count-ne1-or$found[0].passed-cne$true){throw "Missing/failed $id"}
    }

    if(-not(Test-Path $obsPath)){throw 'Observation missing'}
    $obs=Get-Content -Raw $obsPath|ConvertFrom-Json
    $states=@($obs.states)
    if($states.Count-ne4){throw "Expected four states, got $($states.Count)"}
    if([double]$states[0].Pause-ne0.0 -or [double]$states[2].Pause-ne0.0){throw 'Corrected states were not zero pause'}
    if([double]$states[1].Pause-le0.0 -or [double]$states[3].Pause-le0.0){throw 'Baseline states were not non-zero pause'}
    if($states[0].WavSha256-ne$states[2].WavSha256){throw 'Corrected WAV hashes differed'}
    if($states[1].WavSha256-ne$states[3].WavSha256){throw 'Baseline WAV hashes differed'}
    if($states[0].WavSha256-eq$states[1].WavSha256){throw 'Corrected/baseline WAV hashes were equal'}

    Write-Output "PASS_VOICEITEM_ASSIST_DISABLE_REMOVE_E2E"
  }
  finally {
    if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  }
}
finally {
  if(-not$server.HasExited){Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_ASSIST_LIFECYCLE_OUTPUT -ErrorAction SilentlyContinue
  Remove-Item Env:CNWL_FAKE_VOICEVOX_URL -ErrorAction SilentlyContinue
}
