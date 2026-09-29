param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null

$port=50142
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
  if(-not(Test-Path $ready)){throw 'Fake VOICEVOX server did not start'}

  $env:CNWL_VQA_FORCED_BOUNDARY_OUTPUT=$OutputDir
  $env:CNWL_FAKE_VOICEVOX_URL="http://127.0.0.1:$port"

  $process=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
  try {
    $result=Join-Path $OutputDir 'result.json'
    $limit=[DateTime]::UtcNow.AddSeconds(180)
    while([DateTime]::UtcNow-lt$limit -and -not$process.HasExited -and -not(Test-Path $result)){
      Start-Sleep -Milliseconds 350
    }
    if(-not(Test-Path $result)){throw 'No native forced-boundary result'}

    $r=Get-Content -Raw $result|ConvertFrom-Json
    Get-Content $result

    $observation=Join-Path $OutputDir 'forced-boundary-observation.json'
    if(Test-Path $observation){
      Write-Output '--- forced boundary observation ---'
      Get-Content $observation
    }

    $requestsPath=Join-Path $OutputDir 'fake-server-requests.jsonl'
    if(Test-Path $requestsPath){
      Write-Output '--- fake VOICEVOX requests ---'
      Get-Content $requestsPath
    }

    if($r.schema-ne'cnwl.vqa-forced-boundary-transient-comma.v1' -or
       $r.status-ne'PASS_VQA_FORCED_BOUNDARY_TRANSIENT_COMMA' -or
       $r.host-ne'4.56.1.0 Lite' -or
       $r.sourceHead-ne$env:GITHUB_SHA -or
       $null-ne$r.error){
      throw 'Native forced-boundary result rejected'
    }

    $required=@(
      'official_parser_marker_position',
      'transient_input_injects_comma',
      'analysis_adds_phrase_boundary',
      'source_comma_independent',
      'injected_pause_unique',
      'only_injected_pause_zero',
      'source_comma_pause_preserved',
      'final_public_synthesis_succeeds',
      'durable_serif_preserved',
      'durable_hatsuon_has_no_injected_marker',
      'multiple_markers_distinct_zero_pauses',
      'ambiguous_mapping_fails_closed',
      'helper_coexists_with_boundary_identity',
      'save_reload_rederives_boundary'
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

    if(-not(Test-Path $requestsPath)){throw 'Fake server request log missing'}
    $requests=@(Get-Content $requestsPath|ForEach-Object {$_|ConvertFrom-Json})

    $forcedText='これは、テスト音声です実、験のために生成しました。'
    $audioQuery=@($requests|Where-Object {
      if($_.method-ne'POST' -or $_.path-ne'/audio_query'){ return $false }
      $textValues=@($_.query.text)
      return $textValues.Count-gt0 -and $textValues[0]-eq$forcedText
    })
    if($audioQuery.Count-lt1){throw 'Transient forced-boundary text never reached /audio_query'}

    $synth=@($requests|Where-Object {$_.method-eq'POST' -and $_.path-eq'/synthesis'})
    $forcedCorrect=@()
    $multiCorrect=@()
    $helperCorrect=@()
    $ambiguousBaseline=@()

    foreach($request in $synth){
      $body=$request.body|ConvertFrom-Json
      $phrases=@($body.accent_phrases)

      if($phrases.Count-eq3 -and
         $null-ne$phrases[0].pause_mora -and
         $null-ne$phrases[1].pause_mora){
        $p0=[double]$phrases[0].pause_mora.vowel_length
        $p1=[double]$phrases[1].pause_mora.vowel_length
        $texts=($phrases|ForEach-Object {($_.moras|ForEach-Object {$_.text}) -join ''}) -join '|'
        if($texts-like'*ヌ*'){
          $allMoras=@($phrases | ForEach-Object { @($_.moras) })
          $helperZero=@($allMoras|Where-Object {
            $_.text-eq'ヌ' -and [double]$_.vowel_length-eq0.0
          }).Count
          if($p0-gt0.0 -and $p1-eq0.0 -and $helperZero-eq1){$helperCorrect+=,$body}
        } elseif($texts-eq'曖|、|昧境界'){
          if($p0-gt0.0 -and $p1-gt0.0){$ambiguousBaseline+=,$body}
        } elseif($p0-gt0.0 -and $p1-eq0.0){
          $forcedCorrect+=,$body
        }
      }

      if($phrases.Count-eq4 -and
         $null-ne$phrases[0].pause_mora -and
         $null-ne$phrases[1].pause_mora -and
         $null-ne$phrases[2].pause_mora){
        if([double]$phrases[0].pause_mora.vowel_length-gt0.0 -and
           [double]$phrases[1].pause_mora.vowel_length-eq0.0 -and
           [double]$phrases[2].pause_mora.vowel_length-eq0.0){
          $multiCorrect+=,$body
        }
      }
    }

    if($forcedCorrect.Count-lt1){throw 'No final synthesis preserved source comma while zeroing injected comma'}
    if($multiCorrect.Count-lt1){throw 'No final synthesis proved two injected zero pauses'}
    if($helperCorrect.Count-lt1){throw 'No final synthesis proved helper + forced boundary coexistence'}
    if($ambiguousBaseline.Count-lt1){throw 'Ambiguous fixture did not remain unmutated'}

    @{
      schema='cnwl.vqa-forced-boundary-transient-comma-e2e.v1'
      transient_audio_query_count=$audioQuery.Count
      synthesis_count=$synth.Count
      forced_corrected_count=$forcedCorrect.Count
      multi_corrected_count=$multiCorrect.Count
      helper_corrected_count=$helperCorrect.Count
      ambiguous_unmutated_count=$ambiguousBaseline.Count
    }|ConvertTo-Json|Set-Content (Join-Path $OutputDir 'e2e.json')

    Write-Output 'PASS_VQA_FORCED_BOUNDARY_TRANSIENT_COMMA_E2E'
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
  Remove-Item Env:CNWL_VQA_FORCED_BOUNDARY_OUTPUT -ErrorAction SilentlyContinue
  Remove-Item Env:CNWL_FAKE_VOICEVOX_URL -ErrorAction SilentlyContinue
}
