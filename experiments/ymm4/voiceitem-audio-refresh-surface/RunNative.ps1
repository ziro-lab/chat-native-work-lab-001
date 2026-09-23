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

  $env:CNWL_VOICEITEM_AUDIO_REFRESH_OUTPUT=$OutputDir
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

    $obsPath=Join-Path $OutputDir 'audio-refresh-observation.json'
    if(Test-Path $obsPath){
      Write-Output '--- audio refresh observation ---'
      Get-Content $obsPath
    }
    $requestsPath=Join-Path $OutputDir 'fake-server-requests.jsonl'
    if(Test-Path $requestsPath){
      Write-Output '--- fake server requests ---'
      Get-Content $requestsPath
    }

    if($r.schema-ne'cnwl.voiceitem-audio-refresh-surface.v1' -or
       $r.status-ne'PASS_VOICEITEM_AUDIO_REFRESH_SURFACE' -or
       $r.host-ne'4.56.1.0 Lite' -or
       $r.sourceHead-ne$env:GITHUB_SHA -or
       $r.error){throw 'Native audio refresh result rejected'}

    $req=@(
      'timeline_resolved',
      'fake_engine_character_resolved',
      'builtin_voicevox_speaker_constructed',
      'fake_engine_registered',
      'voice_added_to_real_timeline',
      'voice_present_in_real_timeline',
      'real_voiceitem_file_path_available',
      'fresh_analysis_pause_nonzero',
      'corrected_wav_replaced_real_file',
      'clear_voice_cache_public',
      'voice_cache_property_public',
      'voice_cache_sentinel_seeded',
      'voice_cache_nonempty_before_clear',
      'voice_cache_empty_after_clear',
      'clear_cache_preserves_corrected_wav',
      'corrected_pronounce_attached',
      'timeline_currentframe_propertychanged_observed',
      'plugin_refresh_surface_inventoried'
    )
    if($r.requirements.Count-ne$req.Count){throw "Wrong requirement count: $($r.requirements.Count)"}
    foreach($id in $req){
      $found=@($r.requirements|Where-Object {$_.id-eq$id})
      if($found.Count-ne1-or$found[0].passed-cne$true){throw "Missing/failed $id"}
    }

    if(-not(Test-Path $obsPath)){throw 'Audio refresh observation missing'}
    $obs=Get-Content -Raw $obsPath|ConvertFrom-Json
    if($obs.audio.baselineSha256-eq$obs.audio.correctedSha256){throw 'Baseline and corrected WAV hashes are equal'}
    if([double]$obs.audio.correctedPause-ne0.0){throw 'Corrected Pronounce pause was not zero'}
    if($obs.cache.afterClear.length-ne0){throw 'VoiceCache was not empty after ClearVoiceCache'}
    if($obs.notifications.currentFrameEventCount-lt2){throw 'CurrentFrame PropertyChanged signal missing'}

    Write-Output "PASS_VOICEITEM_AUDIO_REFRESH_SURFACE_E2E"
  }
  finally {
    if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  }
}
finally {
  if(-not$server.HasExited){Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_VOICEITEM_AUDIO_REFRESH_OUTPUT -ErrorAction SilentlyContinue
  Remove-Item Env:CNWL_FAKE_VOICEVOX_URL -ErrorAction SilentlyContinue
}
