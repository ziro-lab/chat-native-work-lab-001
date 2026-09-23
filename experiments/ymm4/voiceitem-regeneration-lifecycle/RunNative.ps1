param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$env:CNWL_VOICEITEM_REGEN_OUTPUT=$OutputDir

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

  $surface=Join-Path $OutputDir 'surface.json'
  if(Test-Path $surface){
    Write-Output '--- lifecycle surface ---'
    Get-Content $surface
  }

  if($r.schema-ne'cnwl.voiceitem-regeneration-lifecycle.v1' -or
     $r.status-ne'PASS_VOICEITEM_REGENERATION_SURFACE_INVENTORY' -or
     $r.host-ne'4.56.1.0 Lite' -or
     $r.sourceHead-ne$env:GITHUB_SHA -or
     $r.error){throw 'Native result rejected'}

  $req=@(
    'timeline_resolved',
    'voice_added_to_real_timeline',
    'edit_service_interface_loaded',
    'voice_description_surface_inventoried',
    'voiceitem_surface_inventoried',
    'edit_service_implementors_inventoried',
    'edit_service_factories_inventoried',
    'reachable_host_objects_inventoried',
    'voice_present_in_timeline'
  )

  if($r.requirements.Count-ne$req.Count){throw "Wrong requirement count: $($r.requirements.Count)"}
  foreach($id in $req){
    $f=@($r.requirements|Where-Object {$_.id-eq$id})
    if($f.Count-ne1-or$f[0].passed-cne$true){throw "Missing/failed $id"}
  }

  Write-Output "PASS_VOICEITEM_REGENERATION_SURFACE_INVENTORY: $($req.Count) required assertions"
}
finally {
  if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_VOICEITEM_REGEN_OUTPUT -ErrorAction SilentlyContinue
}
