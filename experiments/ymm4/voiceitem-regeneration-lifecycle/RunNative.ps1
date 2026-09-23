param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null

$port=50124
$serverScript=Join-Path $PSScriptRoot 'fake_voicevox.py'
$server=Start-Process python -ArgumentList @($serverScript,'--port',$port,'--output',$OutputDir) -PassThru -WindowStyle Hidden
try {
  $ready=Join-Path $OutputDir 'fake-server-ready.txt'
  $deadline=[DateTime]::UtcNow.AddSeconds(20)
  while([DateTime]::UtcNow-lt$deadline -and -not(Test-Path $ready) -and -not$server.HasExited){
    Start-Sleep -Milliseconds 200
  }
  if(-not(Test-Path $ready)){throw 'Fake VOICEVOX server did not start'}

  $env:CNWL_VOICEITEM_REGEN_OUTPUT=$OutputDir
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

    $observation=Join-Path $OutputDir 'lifecycle-observation.json'
    if(Test-Path $observation){
      Write-Output '--- lifecycle observation ---'
      Get-Content $observation
    }

    if($r.schema-ne'cnwl.voiceitem-regeneration-lifecycle.v2' -or
       $r.status-ne'PASS_VOICEITEM_REGENERATION_LIFECYCLE' -or
       $r.host-ne'4.56.1.0 Lite' -or
       $r.sourceHead-ne$env:GITHUB_SHA -or
       $r.error){throw 'Native lifecycle result rejected'}

    $req=@(
      'timeline_resolved',
      'fake_engine_character_resolved',
      'builtin_voicevox_speaker_constructed',
      'fake_engine_registered',
      'voice_description_binds_speaker',
      'voice_added_to_real_timeline',
      'voice_present_in_real_timeline',
      'voice_uses_fake_character',
      'initial_voiceitem_generation_completed',
      'initial_voicevox_pronounce_created',
      'initial_pause_from_audio_query_nonzero',
      'patched_pause_is_zero',
      'public_voiceitem_regeneration_completed',
      'patched_pause_survives_regeneration'
    )

    if($r.requirements.Count-ne$req.Count){throw "Wrong requirement count: $($r.requirements.Count)"}
    foreach($id in $req){
      $found=@($r.requirements|Where-Object {$_.id-eq$id})
      if($found.Count-ne1-or$found[0].passed-cne$true){throw "Missing/failed $id"}
    }

    $requestsPath=Join-Path $OutputDir 'fake-server-requests.jsonl'
    if(-not(Test-Path $requestsPath)){throw 'Fake server received no requests'}
    Write-Output '--- fake server requests ---'
    Get-Content $requestsPath

    $requests=@(Get-Content $requestsPath|ForEach-Object { $_|ConvertFrom-Json })
    $synth=@($requests|Where-Object {$_.method-eq'POST' -and $_.path-eq'/synthesis'})
    $audioQuery=@($requests|Where-Object {$_.method-eq'POST' -and $_.path-eq'/audio_query'})

    if($synth.Count-lt2){throw "Expected at least two /synthesis requests, observed $($synth.Count)"}
    if($audioQuery.Count-ne1){throw "Expected exactly one /audio_query, observed $($audioQuery.Count)"}

    $firstBody=$synth[0].body|ConvertFrom-Json
    $lastBody=$synth[-1].body|ConvertFrom-Json
    $firstPause=[double]$firstBody.accent_phrases[0].pause_mora.vowel_length
    $lastPause=[double]$lastBody.accent_phrases[0].pause_mora.vowel_length

    if($firstPause-le0.0){throw "Initial synthesis pause was not non-zero: $firstPause"}
    if($lastPause-ne0.0){throw "Regenerated synthesis pause was not zero: $lastPause"}

    @{
      schema='cnwl.voiceitem-regeneration-e2e.v1'
      audio_query_count=$audioQuery.Count
      synthesis_count=$synth.Count
      initial_pause_vowel_length=$firstPause
      regenerated_pause_vowel_length=$lastPause
    }|ConvertTo-Json|Set-Content (Join-Path $OutputDir 'e2e.json')

    Write-Output "PASS_VOICEITEM_REGENERATION_E2E"
  }
  finally {
    if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  }
}
finally {
  if(-not$server.HasExited){Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_VOICEITEM_REGEN_OUTPUT -ErrorAction SilentlyContinue
  Remove-Item Env:CNWL_FAKE_VOICEVOX_URL -ErrorAction SilentlyContinue
}
