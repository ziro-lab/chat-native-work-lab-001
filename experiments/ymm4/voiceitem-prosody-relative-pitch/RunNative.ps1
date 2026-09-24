param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)

$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null

$port=50132
$serverScript=Join-Path $PSScriptRoot 'fake_voicevox.py'
$server=Start-Process python -ArgumentList @(
  $serverScript,
  '--port',$port,
  '--output',$OutputDir
) -PassThru -WindowStyle Hidden

try {
  $ready=Join-Path $OutputDir 'fake-server-ready.txt'
  $deadline=[DateTime]::UtcNow.AddSeconds(20)

  while([DateTime]::UtcNow-lt$deadline -and -not(Test-Path $ready) -and -not$server.HasExited){
    Start-Sleep -Milliseconds 200
  }

  if(-not(Test-Path $ready)){
    throw 'Fake VOICEVOX server did not start'
  }

  $env:CNWL_PROSODY_OUTPUT=$OutputDir
  $env:CNWL_FAKE_VOICEVOX_URL="http://127.0.0.1:$port"

  $process=Start-Process (
    Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe'
  ) -WorkingDirectory $Ymm4Dir -PassThru

  try {
    $result=Join-Path $OutputDir 'result.json'
    $limit=[DateTime]::UtcNow.AddSeconds(150)

    while([DateTime]::UtcNow-lt$limit -and -not$process.HasExited -and -not(Test-Path $result)){
      Start-Sleep -Milliseconds 350
    }

    if(-not(Test-Path $result)){
      throw 'No native prosody result'
    }

    $r=Get-Content -Raw $result|ConvertFrom-Json
    Get-Content $result

    $observation=Join-Path $OutputDir 'prosody-observation.json'
    if(Test-Path $observation){
      Write-Output '--- prosody observation ---'
      Get-Content $observation
    }

    $requestsPath=Join-Path $OutputDir 'fake-server-requests.jsonl'
    if(Test-Path $requestsPath){
      Write-Output '--- fake VOICEVOX requests ---'
      Get-Content $requestsPath
    }

    $rejected=(
      $r.schema-ne'cnwl.voiceitem-prosody-relative-pitch.v1' -or
      $r.status-ne'PASS_VOICEITEM_PROSODY_RELATIVE_PITCH' -or
      $r.host-ne'4.56.1.0 Lite' -or
      $r.sourceHead-ne$env:GITHUB_SHA -or
      $null-ne$r.error
    )
    if($rejected){
      throw 'Native prosody result rejected'
    }

    $required=@(
      'timeline_resolved',
      'fake_engine_character_resolved',
      'builtin_voicevox_speaker_constructed',
      'fake_engine_registered',
      'rise_voice_added_to_timeline',
      'rise_baseline_file_exists',
      'rise_mora_shape',
      'rise_baseline_pitch_shape',
      'rise_relative_pitch_applied',
      'rise_durations_preserved',
      'rise_pitch_survives_synthesis',
      'rise_persisted_serif_unchanged',
      'rise_persisted_hatsuon_unchanged',
      'rise_corrected_real_file_exists',
      'fall_voice_added_to_timeline',
      'fall_baseline_file_exists',
      'fall_mora_shape',
      'fall_baseline_pitch_shape',
      'fall_relative_pitch_applied',
      'fall_durations_preserved',
      'fall_pitch_survives_synthesis',
      'fall_persisted_serif_unchanged',
      'fall_persisted_hatsuon_unchanged',
      'fall_corrected_real_file_exists'
    )

    if($r.requirements.Count-ne$required.Count){
      throw "Wrong requirement count: $($r.requirements.Count)"
    }

    foreach($id in $required){
      $found=@($r.requirements|Where-Object {$_.id-eq$id})
      if($found.Count-ne1-or$found[0].passed-cne$true){
        throw "Missing/failed requirement: $id"
      }
    }

    if(-not(Test-Path $requestsPath)){
      throw 'Fake server request log missing'
    }

    $requests=@(Get-Content $requestsPath|ForEach-Object { $_|ConvertFrom-Json })
    $observed=@($requests|Where-Object {
      $_.method-eq'OBSERVE' -and $_.path-eq'/synthesis-result'
    })

    $riseObserve=@($observed|Where-Object {$_.kind-eq'relative-rise'})
    $fallObserve=@($observed|Where-Object {$_.kind-eq'relative-fall'})

    if($riseObserve.Count-lt1){ throw 'No corrected rise synthesis observed' }
    if($fallObserve.Count-lt1){ throw 'No corrected fall synthesis observed' }

    $riseExpected=@(4.92,5.04,5.16,5.12,5.08)
    $fallExpected=@(5.08,5.12,5.16,5.04,4.92)

    function Test-Pitches($actual,$expected){
      if($actual.Count-ne$expected.Count){ return $false }
      for($i=0;$i-lt$expected.Count;$i++){
        if([Math]::Abs([double]$actual[$i]-[double]$expected[$i])-gt0.000001){
          return $false
        }
      }
      return $true
    }

    if(-not(Test-Pitches @($riseObserve[-1].pitches) $riseExpected)){
      throw 'Rise pitches did not reach synthesis'
    }

    if(-not(Test-Pitches @($fallObserve[-1].pitches) $fallExpected)){
      throw 'Fall pitches did not reach synthesis'
    }

    $synth=@($requests|Where-Object {
      $_.method-eq'POST' -and $_.path-eq'/synthesis'
    })

    $correctBodies=0
    foreach($request in $synth){
      $body=$request.body|ConvertFrom-Json
      if($body.accent_phrases.Count-lt1){ continue }
      $moras=@($body.accent_phrases[0].moras)
      if($moras.Count-ne5){ continue }

      $lengthsOk=@($moras|Where-Object {
        [Math]::Abs([double]$_.vowel_length-0.12)-lt0.000001
      }).Count-eq5

      $pitches=@($moras|ForEach-Object {[double]$_.pitch})
      if($lengthsOk -and ((Test-Pitches $pitches $riseExpected) -or (Test-Pitches $pitches $fallExpected))){
        $correctBodies++
      }
    }

    if($correctBodies-lt2){
      throw 'Expected rise and fall corrected synthesis bodies'
    }

    @{
      schema='cnwl.voiceitem-prosody-relative-pitch-e2e.v1'
      synthesis_count=$synth.Count
      corrected_body_count=$correctBodies
      rise_wav_length=[int]$riseObserve[-1].wav_length
      fall_wav_length=[int]$fallObserve[-1].wav_length
    }|ConvertTo-Json|Set-Content (
      Join-Path $OutputDir 'e2e.json'
    )

    Write-Output 'PASS_VOICEITEM_PROSODY_RELATIVE_PITCH_E2E'
  }
  finally {
    if(-not$process.HasExited){
      Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
  }
}
finally {
  if(-not$server.HasExited){
    Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
  }

  Remove-Item Env:CNWL_PROSODY_OUTPUT -ErrorAction SilentlyContinue
  Remove-Item Env:CNWL_FAKE_VOICEVOX_URL -ErrorAction SilentlyContinue
}
