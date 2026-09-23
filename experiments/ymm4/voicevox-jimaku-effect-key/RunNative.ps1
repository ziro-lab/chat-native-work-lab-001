param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$env:CNWL_JIMAKU_EFFECT_KEY_OUTPUT=$OutputDir
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  $limit=[DateTime]::UtcNow.AddSeconds(120)
  $result=Join-Path $OutputDir 'result.json'
  while([DateTime]::UtcNow -lt $limit -and -not $p.HasExited -and -not(Test-Path $result)) {
    Start-Sleep -Milliseconds 400
  }
  if(-not(Test-Path $result)){throw 'No native result'}
  $r=Get-Content -Raw $result|ConvertFrom-Json
  Get-Content $result
  if(Test-Path (Join-Path $OutputDir 'behavior.json')){Get-Content (Join-Path $OutputDir 'behavior.json')}

  if($r.schema-ne'cnwl.jimaku-effect-key.v1' -or
     $r.status-ne'PASS_JIMAKU_EFFECT_KEY' -or
     $r.host-ne'4.56.1.0 Lite' -or
     $r.sourceHead-ne$env:GITHUB_SHA -or
     $r.error){throw 'Behavior result rejected'}

  $req=@(
    'timeline_resolved',
    'detached_voiceitem_constructed',
    'detached_voice_added_to_timeline',
    'jimaku_effects_public_readable',
    'custom_effect_stored_in_jimaku',
    'custom_effect_detectable_by_type',
    'enabled_effect_is_active_key',
    'disabled_effect_is_not_active_key',
    'reenabled_effect_is_active_key',
    'voice_selected_in_timeline',
    'effect_property_editor_bound',
    'effect_editor_owner_is_custom_effect',
    'effect_editor_received_editor_info',
    'effect_editor_received_voice_edit_service',
    'standard_voice_regeneration_started',
    'standard_voice_regeneration_invoked'
  )
  if($r.requirements.Count-ne$req.Count){throw "Wrong requirement count: $($r.requirements.Count) != $($req.Count)"}
  foreach($id in $req){
    $f=@($r.requirements|Where-Object { $_.id -eq $id })
    if($f.Count-ne1-or$f[0].passed-cne$true){throw "Missing/failed $id"}
  }
  Write-Output "PASS_JIMAKU_EFFECT_KEY: $($req.Count) required assertions"
}
finally {
  if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_JIMAKU_EFFECT_KEY_OUTPUT -ErrorAction SilentlyContinue
}
