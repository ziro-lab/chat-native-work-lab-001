param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null

$env:CNWL_VQA_AUDIO_EFFECT_OUTPUT=$OutputDir
$process=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  $result=Join-Path $OutputDir 'result.json'
  $limit=[DateTime]::UtcNow.AddSeconds(180)
  while([DateTime]::UtcNow-lt$limit -and -not$process.HasExited -and -not(Test-Path $result)){
    Start-Sleep -Milliseconds 350
  }

  if(-not(Test-Path $result)){throw 'No native audio-effect surface result'}

  $r=Get-Content -Raw $result|ConvertFrom-Json
  Get-Content $result

  foreach($name in @('progress.txt','observation.json','ui-text.txt')){
    $path=Join-Path $OutputDir $name
    if(Test-Path $path){
      Write-Output "--- $name ---"
      Get-Content $path
    }
  }

  if($r.schema-ne'cnwl.vqa-audio-effect-surface.v1' -or
     $r.status-ne'PASS_VQA_AUDIO_EFFECT_SURFACE' -or
     $r.host-ne'4.56.1.0 Lite' -or
     $r.sourceHead-ne$env:GITHUB_SHA -or
     $null-ne$r.error){
    throw 'Native audio-effect surface result rejected'
  }

  $required=@(
    'custom_audio_effect_host_loaded',
    'voice_audio_collection_public_enumerable',
    'audio_effect_public_attach',
    'audio_effect_not_in_subtitle_collection',
    'effect_enabled_notification',
    'effect_custom_property_notification',
    'pass_through_sample_identity',
    'audio_effect_membership_undo_redo',
    'audio_effect_visible_in_item_editor',
    'audio_effect_property_editors_visible',
    'audio_effect_settings_save_reload',
    'audio_effect_public_enumeration_after_reload',
    'audio_effect_public_remove',
    'remove_preserves_ordinary_voice_source'
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

  Write-Output 'PASS_VQA_AUDIO_EFFECT_SURFACE_E2E'
}
finally {
  if(-not$process.HasExited){
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
  }
  Remove-Item Env:CNWL_VQA_AUDIO_EFFECT_OUTPUT -ErrorAction SilentlyContinue
}
