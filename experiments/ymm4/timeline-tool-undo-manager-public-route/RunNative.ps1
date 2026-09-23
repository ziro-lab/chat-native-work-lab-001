param(
  [Parameter(Mandatory=$true)][string]$Ymm4Dir,
  [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$env:CNWL_TIMELINE_UNDO_PUBLIC_OUTPUT=$OutputDir

$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  $limit=[DateTime]::UtcNow.AddSeconds(90)
  $result=Join-Path $OutputDir 'result.json'
  while([DateTime]::UtcNow-lt$limit -and -not$p.HasExited -and -not(Test-Path $result)){
    Start-Sleep -Milliseconds 350
  }
  if(-not(Test-Path $result)){throw 'No native result'}

  $r=Get-Content -Raw $result|ConvertFrom-Json
  Get-Content $result
  foreach($name in @('progress.txt','menu-item-surface.json','observation.json')){
    $path=Join-Path $OutputDir $name
    if(Test-Path $path){Write-Output "--- $name ---"; Get-Content $path}
  }

  if($r.schema-ne'cnwl.timeline-tool-undo-manager-public-route.v1' -or
     $r.status-ne'PASS_TIMELINE_TOOL_UNDO_MANAGER_PUBLIC_ROUTE' -or
     $r.host-ne'4.56.1.0 Lite' -or
     $r.sourceHead-ne$env:GITHUB_SHA -or
     $r.error){throw 'Native result rejected'}

  $req=@(
    'tool_menu_item_found',
    'tool_menu_open_invoked',
    'host_set_timeline_tool_info_called',
    'timeline_info_timeline_nonnull',
    'timeline_info_undo_manager_nonnull',
    'undo_manager_add_command_public',
    'undo_manager_record_public',
    'undo_manager_undo_async_public',
    'undo_manager_redo_async_public'
  )
  if($r.requirements.Count-ne$req.Count){throw "Wrong requirement count: $($r.requirements.Count)"}
  foreach($id in $req){
    $found=@($r.requirements|Where-Object {$_.id-eq$id})
    if($found.Count-ne1-or$found[0].passed-cne$true){throw "Missing/failed $id"}
  }

  Write-Output "PASS_TIMELINE_TOOL_UNDO_MANAGER_PUBLIC_ROUTE_E2E"
}
finally {
  if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_TIMELINE_UNDO_PUBLIC_OUTPUT -ErrorAction SilentlyContinue
}
