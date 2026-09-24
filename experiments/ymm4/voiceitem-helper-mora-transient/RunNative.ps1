param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)

$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null

$port=50131
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

  $env:CNWL_HELPER_MORA_OUTPUT=$OutputDir
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
      throw 'No native helper-mora result'
    }

    $r=Get-Content -Raw $result|ConvertFrom-Json
    Get-Content $result

    $observation=Join-Path $OutputDir 'helper-mora-observation.json'
    if(Test-Path $observation){
      Write-Output '--- helper mora observation ---'
      Get-Content $observation
    }

    $requestsPath=Join-Path $OutputDir 'fake-server-requests.jsonl'
    if(Test-Path $requestsPath){
      Write-Output '--- fake VOICEVOX requests ---'
      Get-Content $requestsPath
    }

    $rejected=(
      $r.schema-ne'cnwl.voiceitem-helper-mora-transient.v1' -or
      $r.status-ne'PASS_VOICEITEM_HELPER_MORA_TRANSIENT' -or
      $r.host-ne'4.56.1.0 Lite' -or
      $r.sourceHead-ne$env:GITHUB_SHA -or
      $null-ne$r.error
    )
    if($rejected){
      throw 'Native helper-mora result rejected'
    }

    $required=@(
      'timeline_resolved',
      'fake_engine_character_resolved',
      'builtin_voicevox_speaker_constructed',
      'fake_engine_registered',
      'vowel_voice_added_to_timeline',
      'vowel_baseline_file_exists',
      'vowel_helper_mora_shape',
      'vowel_helper_lengths_zero',
      'vowel_nonhelper_lengths_preserved',
      'vowel_helper_zero_survives_synthesis',
      'vowel_persisted_serif_unchanged',
      'vowel_persisted_hatsuon_unchanged',
      'vowel_corrected_real_file_exists',
      'consonant_voice_added_to_timeline',
      'consonant_baseline_file_exists',
      'consonant_helper_mora_shape',
      'consonant_helper_has_consonant',
      'consonant_helper_length_zero',
      'consonant_helper_vowel_preserved',
      'consonant_helper_zero_survives_synthesis',
      'consonant_helper_vowel_survives_synthesis',
      'consonant_persisted_serif_unchanged',
      'consonant_persisted_hatsuon_unchanged',
      'consonant_corrected_real_file_exists'
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

    $vowelObserve=@($observed|Where-Object {$_.kind-eq'vowel-helper'})
    $consonantObserve=@($observed|Where-Object {$_.kind-eq'consonant-helper'})

    if($vowelObserve.Count-lt1){
      throw 'No corrected vowel-helper synthesis observed'
    }
    if($consonantObserve.Count-lt1){
      throw 'No corrected consonant-helper synthesis observed'
    }

    $synth=@($requests|Where-Object {
      $_.method-eq'POST' -and $_.path-eq'/synthesis'
    })

    $vowelBodies=@()
    $consonantBodies=@()

    foreach($request in $synth){
      $body=$request.body|ConvertFrom-Json
      $moras=@($body.accent_phrases[0].moras)
      $texts=($moras|ForEach-Object {$_.text}) -join ''

      if($texts-eq'エウエウエ'){
        $vowelBodies+=,$body
      }

      if($texts-eq'エセエ'){
        $consonantBodies+=,$body
      }
    }

    if($vowelBodies.Count-lt1){
      throw 'No vowel-helper synthesis body found'
    }

    $vowelCorrect=@($vowelBodies|Where-Object {
      [double]$_.accent_phrases[0].moras[1].vowel_length-eq0.0 -and
      [double]$_.accent_phrases[0].moras[3].vowel_length-eq0.0
    })

    if($vowelCorrect.Count-lt1){
      throw 'Vowel helper zero lengths did not reach synthesis'
    }

    if($consonantBodies.Count-lt1){
      throw 'No consonant-helper synthesis body found'
    }

    $consonantCorrect=@($consonantBodies|Where-Object {
      [double]$_.accent_phrases[0].moras[1].consonant_length-eq0.0 -and
      [double]$_.accent_phrases[0].moras[1].vowel_length-gt0.0
    })

    if($consonantCorrect.Count-lt1){
      throw 'Consonant helper zero consonant / preserved vowel did not reach synthesis'
    }

    @{
      schema='cnwl.voiceitem-helper-mora-transient-e2e.v1'
      synthesis_count=$synth.Count
      vowel_helper_corrected_count=$vowelCorrect.Count
      consonant_helper_corrected_count=$consonantCorrect.Count
      vowel_corrected_wav_length=[int]$vowelObserve[-1].wav_length
      consonant_corrected_wav_length=[int]$consonantObserve[-1].wav_length
    }|ConvertTo-Json|Set-Content (
      Join-Path $OutputDir 'e2e.json'
    )

    Write-Output 'PASS_VOICEITEM_HELPER_MORA_TRANSIENT_E2E'
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

  Remove-Item Env:CNWL_HELPER_MORA_OUTPUT -ErrorAction SilentlyContinue
  Remove-Item Env:CNWL_FAKE_VOICEVOX_URL -ErrorAction SilentlyContinue
}
