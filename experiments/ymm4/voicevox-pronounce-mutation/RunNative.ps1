param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$env:CNWL_VOICEVOX_PRONOUNCE_OUTPUT=$OutputDir
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  $limit=[DateTime]::UtcNow.AddSeconds(90)
  $result=Join-Path $OutputDir 'result.json'
  while([DateTime]::UtcNow -lt $limit -and -not $p.HasExited -and -not(Test-Path $result)) {
    Start-Sleep -Milliseconds 350
  }
  if(-not(Test-Path $result)){throw 'No native result'}
  $r=Get-Content -Raw $result | ConvertFrom-Json
  Get-Content $result
  if(Test-Path (Join-Path $OutputDir 'surface.json')) { Get-Content (Join-Path $OutputDir 'surface.json') }
  if($r.schema-ne'cnwl.voicevox-pronounce-mutation.v1' -or
     $r.status-ne'PASS_VOICEVOX_PRONOUNCE_MUTATION_SURFACE' -or
     $r.host-ne'4.56.1.0 Lite' -or
     $r.sourceHead-ne$env:GITHUB_SHA -or
     $r.error) { throw 'Behavior result rejected' }

  $req=@(
    'voiceitem_pronounce_public_readwrite',
    'voicevox_pronounce_type_found',
    'voicevox_audioquery_public',
    'audioquery_accentphrases_public',
    'accentphrase_pausemora_public',
    'mora_vowellength_public_writable',
    'pause_duration_accepts_zero',
    'nested_pronounce_zero_observable',
    'voiceitem_edit_service_inventoried'
  )
  if($r.requirements.Count-ne$req.Count){throw "Wrong requirement count: $($r.requirements.Count) != $($req.Count)"}
  foreach($id in $req){
    $f=@($r.requirements|Where-Object { $_.id -eq $id })
    if($f.Count-ne1-or$f[0].passed-cne$true){throw "Missing/failed $id"}
  }
  Write-Output "PASS_VOICEVOX_PRONOUNCE_MUTATION_SURFACE: $($req.Count) required assertions"
}
finally {
  if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_VOICEVOX_PRONOUNCE_OUTPUT -ErrorAction SilentlyContinue
}
