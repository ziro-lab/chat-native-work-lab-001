param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null

$port=50128
$serverScript=Join-Path $PSScriptRoot 'fake_voicevox.py'
$server=Start-Process python -ArgumentList @($serverScript,'--port',$port,'--output',$OutputDir) -PassThru -WindowStyle Hidden
try {
  $ready=Join-Path $OutputDir 'fake-server-ready.txt'
  $deadline=[DateTime]::UtcNow.AddSeconds(20)
  while([DateTime]::UtcNow-lt$deadline -and -not(Test-Path $ready) -and -not$server.HasExited){
    Start-Sleep -Milliseconds 200
  }
  if(-not(Test-Path $ready)){throw 'Fake VOICEVOX server did not start'}

  $env:CNWL_W0_VOICE_PATH_OUTPUT=$OutputDir
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

    $obsPath=Join-Path $OutputDir 'voice-path-observation.json'
    if(Test-Path $obsPath){
      Write-Output '--- voice path observation ---'
      Get-Content $obsPath
    }

    if($r.schema-ne'cnwl.voiceitem-w0-voice-path.v1' -or
       $r.status-ne'PASS_W0_VOICE_PATH_OBSERVATION' -or
       $r.host-ne'4.56.1.0 Lite' -or
       $r.sourceHead-ne$env:GITHUB_SHA -or
       $r.error){throw 'Native w0 observation rejected'}

    $req=@(
      'timeline_resolved',
      'fake_engine_registered',
      'baseline_generation_succeeded',
      'serif_tagged_generation_succeeded',
      'serif_tagged_boundary_position_1',
      'hatsuon_tagged_attempt_completed',
      'three_cases_recorded'
    )
    if($r.requirements.Count-ne$req.Count){throw "Wrong requirement count: $($r.requirements.Count)"}
    foreach($id in $req){
      $found=@($r.requirements|Where-Object {$_.id-eq$id})
      if($found.Count-ne1-or$found[0].passed-cne$true){throw "Missing/failed $id"}
    }

    $requestsPath=Join-Path $OutputDir 'fake-server-requests.jsonl'
    if(-not(Test-Path $requestsPath)){throw 'Fake server request log missing'}
    $requests=@(Get-Content $requestsPath|ForEach-Object { $_|ConvertFrom-Json })

    function Case-Slice([string]$name) {
      $startName="$name-start"
      $endName="$name-end"
      $start=-1
      $end=-1
      for($i=0;$i-lt$requests.Count;$i++){
        $q=$requests[$i].query
        $marker=$null
        if($requests[$i].method-eq'GET' -and $requests[$i].path-eq'/cnwl_case' -and $null-ne$q.name){
          $marker=[string]$q.name[0]
        }
        if($marker-eq$startName){$start=$i}
        if($marker-eq$endName -and $start-ge0){$end=$i;break}
      }
      if($start-lt0-or$end-lt0-or$end-le$start){throw "Case markers missing: $name"}
      $slice=@()
      if($end-$start-gt1){$slice=@($requests[($start+1)..($end-1)])}
      $posts=@($slice|Where-Object {$_.method-eq'POST'})
      return [pscustomobject]@{
        name=$name
        request_count=$slice.Count
        post_count=$posts.Count
        audio_query_count=@($posts|Where-Object {$_.path-eq'/audio_query'}).Count
        accent_phrases_count=@($posts|Where-Object {$_.path-eq'/accent_phrases'}).Count
        synthesis_count=@($posts|Where-Object {$_.path-eq'/synthesis'}).Count
        paths=@($slice|ForEach-Object {$_.path})
        requests=$slice
      }
    }

    $baseline=Case-Slice 'baseline'
    $serifTagged=Case-Slice 'serif-tagged'
    $hatsuonTagged=Case-Slice 'hatsuon-tagged'

    if($baseline.synthesis_count-lt1){throw 'Baseline synthesis missing'}
    if($serifTagged.synthesis_count-lt1){throw 'Serif-tagged synthesis missing'}

    @{
      schema='cnwl.voiceitem-w0-http-cases.v1'
      baseline=$baseline
      serif_tagged=$serifTagged
      hatsuon_tagged=$hatsuonTagged
    }|ConvertTo-Json -Depth 12|Set-Content (Join-Path $OutputDir 'http-cases.json')

    Write-Output '--- case trace summary ---'
    Get-Content (Join-Path $OutputDir 'http-cases.json')
    Write-Output 'PASS_W0_VOICE_PATH_HTTP_OBSERVATION'
  }
  finally {
    if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  }
}
finally {
  if(-not$server.HasExited){Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_W0_VOICE_PATH_OUTPUT -ErrorAction SilentlyContinue
  Remove-Item Env:CNWL_FAKE_VOICEVOX_URL -ErrorAction SilentlyContinue
}
