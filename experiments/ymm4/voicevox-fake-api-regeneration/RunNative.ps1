param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$env:CNWL_VOICEVOX_FAKE_API_OUTPUT=$OutputDir
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  $limit=[DateTime]::UtcNow.AddSeconds(120)
  $result=Join-Path $OutputDir 'result.json'
  while([DateTime]::UtcNow -lt $limit -and -not $p.HasExited -and -not(Test-Path $result)) { Start-Sleep -Milliseconds 350 }
  if(-not(Test-Path $result)){throw 'No native result'}
  $r=Get-Content -Raw $result|ConvertFrom-Json
  Get-Content $result
  if(Test-Path (Join-Path $OutputDir 'behavior.json')){Get-Content (Join-Path $OutputDir 'behavior.json')}
  if($r.schema-ne'cnwl.voicevox-fake-api-regeneration.v1' -or
     $r.status-ne'PASS_VOICEVOX_FAKE_API_REGENERATION' -or
     $r.host-ne'4.56.1.0 Lite' -or
     $r.sourceHead-ne$env:GITHUB_SHA -or $r.error){throw 'Behavior result rejected'}

  $req=@(
    'engine_points_to_fake_api',
    'engine_execution_disabled',
    'character_constructed',
    'character_has_style',
    'builtin_speaker_constructed',
    'voice_parameter_constructed',
    'voice_parameter_uses_style_1',
    'first_create_voice_returns_pronounce',
    'first_wave_written',
    'first_call_hits_audio_query',
    'first_call_hits_synthesis',
    'pause_mutated_to_zero',
    'second_create_voice_returns_pronounce',
    'second_wave_written',
    'second_call_reuses_pronounce_without_audio_query',
    'second_call_hits_synthesis_once',
    'modified_zero_reaches_synthesis',
    'returned_pronounce_keeps_zero'
  )
  if($r.requirements.Count-ne$req.Count){throw "Wrong requirement count: $($r.requirements.Count) != $($req.Count)"}
  foreach($id in $req){
    $f=@($r.requirements|Where-Object {$_.id-eq$id})
    if($f.Count-ne1-or$f[0].passed-cne$true){throw "Missing/failed $id"}
  }
  Write-Output "PASS_VOICEVOX_FAKE_API_REGENERATION: $($req.Count) required assertions"
}
finally {
  if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_VOICEVOX_FAKE_API_OUTPUT -ErrorAction SilentlyContinue
}
