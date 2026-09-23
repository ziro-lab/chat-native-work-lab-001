param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null

$port=50123
$serverScript=Join-Path $PSScriptRoot 'fake_voicevox.py'
$server=Start-Process python -ArgumentList @($serverScript,'--port',$port,'--output',$OutputDir) -PassThru -WindowStyle Hidden
try {
  $ready=Join-Path $OutputDir 'fake-server-ready.txt'
  $deadline=[DateTime]::UtcNow.AddSeconds(20)
  while([DateTime]::UtcNow-lt$deadline -and -not(Test-Path $ready) -and -not$server.HasExited){
    Start-Sleep -Milliseconds 200
  }
  if(-not(Test-Path $ready)){throw 'Fake VOICEVOX server did not start'}

  $env:CNWL_VOICEVOX_DIRECT_OUTPUT=$OutputDir
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
    if(Test-Path (Join-Path $OutputDir 'plugin-observation.json')){Get-Content (Join-Path $OutputDir 'plugin-observation.json')}
    if($r.schema-ne'cnwl.voicevox-direct-synthesis.v1' -or
       $r.status-ne'PASS_VOICEVOX_DIRECT_SYNTHESIS_PLUGIN' -or
       $r.host-ne'4.56.1.0 Lite' -or
       $r.sourceHead-ne$env:GITHUB_SHA -or $r.error){throw 'Plugin result rejected'}

    $req=@(
      'engine_points_to_fake_backend',
      'builtin_speaker_constructed',
      'supplied_pause_is_zero',
      'query_has_no_errors',
      'pronounce_has_no_errors',
      'parameter_has_no_errors',
      'create_voice_async_completed',
      'returned_voicevox_pronounce',
      'engine_direct_invoked',
      'engine_direct_completed',
      'engine_direct_wav_written'
    )
    $requestsPath=Join-Path $OutputDir 'fake-server-requests.jsonl'
    if(Test-Path $requestsPath){
      Write-Output '--- fake server requests ---'
      Get-Content $requestsPath
    } else {
      Write-Output '--- fake server requests: NONE ---'
    }

    if($r.requirements.Count-ne$req.Count){throw "Wrong plugin requirement count: $($r.requirements.Count)"}
    foreach($id in $req){
      $f=@($r.requirements|Where-Object {$_.id-eq$id})
      if($f.Count-ne1-or$f[0].passed-cne$true){throw "Missing/failed $id"}
    }

    if(-not(Test-Path $requestsPath)){throw 'Fake server received no requests'}
    $requests=@(Get-Content $requestsPath|ForEach-Object { $_|ConvertFrom-Json })
    $synth=@($requests|Where-Object {$_.method-eq'POST' -and $_.path-eq'/synthesis'})
    $audioQuery=@($requests|Where-Object {$_.method-eq'POST' -and $_.path-eq'/audio_query'})
    if($synth.Count-lt1){throw 'No /synthesis request observed'}
    if($audioQuery.Count-ne0){throw "Unexpected /audio_query count: $($audioQuery.Count)"}

    $bodyPath=Join-Path $OutputDir 'synthesis-body.json'
    if(-not(Test-Path $bodyPath)){throw 'No synthesis body captured'}
    $body=Get-Content -Raw $bodyPath|ConvertFrom-Json
    $pause=$body.accent_phrases[0].pause_mora.vowel_length
    if([double]$pause-ne0.0){throw "Pause vowel_length was not zero: $pause"}

    $engineWav=Join-Path $OutputDir 'engine-direct.wav'
    if(-not(Test-Path $engineWav)){throw 'Engine-direct WAV missing'}

    @{
      schema='cnwl.voicevox-direct-synthesis-e2e.v2'
      synthesis_count=$synth.Count
      audio_query_count=$audioQuery.Count
      received_pause_vowel_length=[double]$pause
      engine_direct_wav_length=(Get-Item $engineWav).Length
      public_speaker_wav_exists=(Test-Path (Join-Path $OutputDir 'direct.wav'))
    }|ConvertTo-Json|Set-Content (Join-Path $OutputDir 'e2e.json')

    Write-Output "PASS_VOICEVOX_DIRECT_SYNTHESIS_E2E"
  }
  finally {
    if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  }
}
finally {
  if(-not$server.HasExited){Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_VOICEVOX_DIRECT_OUTPUT -ErrorAction SilentlyContinue
  Remove-Item Env:CNWL_FAKE_VOICEVOX_URL -ErrorAction SilentlyContinue
}
