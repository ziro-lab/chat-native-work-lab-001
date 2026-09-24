param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null

$port=50129
$serverScript=Join-Path $PSScriptRoot 'fake_voicevox.py'
$server=Start-Process python -ArgumentList @($serverScript,'--port',$port,'--output',$OutputDir) -PassThru -WindowStyle Hidden
try {
  $ready=Join-Path $OutputDir 'fake-server-ready.txt'
  $deadline=[DateTime]::UtcNow.AddSeconds(20)
  while([DateTime]::UtcNow-lt$deadline -and -not(Test-Path $ready) -and -not$server.HasExited){
    Start-Sleep -Milliseconds 200
  }
  if(-not(Test-Path $ready)){throw 'Fake VOICEVOX server did not start'}

  $env:CNWL_READING_PREFIX_OUTPUT=$OutputDir
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

    $obs=Join-Path $OutputDir 'reading-prefix-observation.json'
    if(Test-Path $obs){
      Write-Output '--- reading prefix observation ---'
      Get-Content $obs
    }

    $requestsPath=Join-Path $OutputDir 'fake-server-requests.jsonl'
    if(Test-Path $requestsPath){
      Write-Output '--- fake server requests ---'
      Get-Content $requestsPath
    }

    if($r.schema-ne'cnwl.voice-speaker-reading-prefix-map.v1' -or
       $r.status-ne'PASS_VOICE_SPEAKER_READING_PREFIX_MAP' -or
       $r.host-ne'4.56.1.0 Lite' -or
       $r.sourceHead-ne$env:GITHUB_SHA -or
       $r.error){throw 'Native reading prefix result rejected'}

    $req=@(
      'builtin_voicevox_speaker_constructed',
      'fake_engine_registered',
      'convert_kanji_to_yomi_public',
      'full_conversion_completed',
      'prefix_conversion_completed',
      'full_reading_nonempty',
      'prefix_reading_nonempty',
      'normalized_prefix_is_full_prefix',
      'full_reading_pronounce_created',
      'full_reading_matches_mora_stream',
      'prefix_maps_to_unique_phrase_end',
      'mapped_phrase_has_pause_mora'
    )

    if($r.requirements.Count-ne$req.Count){throw "Wrong requirement count: $($r.requirements.Count)"}
    foreach($id in $req){
      $found=@($r.requirements|Where-Object {$_.id-eq$id})
      if($found.Count-ne1-or$found[0].passed-cne$true){throw "Missing/failed $id"}
    }

    if(-not(Test-Path $requestsPath)){throw 'Fake server received no requests'}
    $requests=@(Get-Content $requestsPath|ForEach-Object { $_|ConvertFrom-Json })
    $posts=@($requests|Where-Object {$_.method-eq'POST'})

    @{
      schema='cnwl.voice-speaker-reading-prefix-http.v1'
      post_count=$posts.Count
      audio_query_count=@($posts|Where-Object {$_.path-eq'/audio_query'}).Count
      accent_phrases_count=@($posts|Where-Object {$_.path-eq'/accent_phrases'}).Count
      synthesis_count=@($posts|Where-Object {$_.path-eq'/synthesis'}).Count
      post_paths=@($posts|ForEach-Object {$_.path})
    }|ConvertTo-Json|Set-Content (Join-Path $OutputDir 'http-summary.json')

    Write-Output '--- http summary ---'
    Get-Content (Join-Path $OutputDir 'http-summary.json')
    Write-Output "PASS_VOICE_SPEAKER_READING_PREFIX_MAP_E2E"
  }
  finally {
    if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  }
}
finally {
  if(-not$server.HasExited){Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_READING_PREFIX_OUTPUT -ErrorAction SilentlyContinue
  Remove-Item Env:CNWL_FAKE_VOICEVOX_URL -ErrorAction SilentlyContinue
}
