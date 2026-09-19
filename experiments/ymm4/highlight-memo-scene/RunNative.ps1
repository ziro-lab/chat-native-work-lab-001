param(
 [Parameter(Mandatory=$true)][string]$Ymm4Dir,
 [Parameter(Mandatory=$true)][string]$OutputDir,
 [Parameter(Mandatory=$true)][string]$MediaPath
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$env:CNWL_HIGHLIGHT_MEMO_OUTPUT=$OutputDir
$env:CNWL_HIGHLIGHT_MEMO_MEDIA=$MediaPath
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try{
 $result=Join-Path $OutputDir 'result.json'
 $limit=[DateTime]::UtcNow.AddSeconds(100)
 while([DateTime]::UtcNow-lt$limit -and -not$p.HasExited -and -not(Test-Path $result)){Start-Sleep -Milliseconds 400}
 if(-not(Test-Path $result)){throw 'No native result'}
 $r=Get-Content -Raw $result|ConvertFrom-Json
 Get-Content $result
 if(Test-Path (Join-Path $OutputDir 'memo-layout.json')){Get-Content (Join-Path $OutputDir 'memo-layout.json')}
 if($r.schema-ne'cnwl.highlight-memo-scene.v1'-or$r.status-ne'PASS_HIGHLIGHT_MEMO_SCENE'-or$r.host-ne'4.56.1.0 Lite'-or$r.sourceHead-ne$env:GITHUB_SHA-or$r.error){throw 'Native memo result rejected'}
 $req=@(
  'fixture_exists','fps_positive','public_scene_surfaces','main_source_insert',
  'memo_scene_created_once','memo_scene_id_nonempty','main_reselected','memo_scene_reused','memo_scene_name_unique',
  'memo_add_1','main_stays_active_1','memo_add_2','main_stays_active_2','memo_add_3','main_stays_active_3',
  'memo_count_three','memo_all_frame_zero','memo_layers_consecutive','memo_durations_configurable','memo_offsets_preserved',
  'memo_remarks_preserved','memo_paths_preserved','memo_rates_preserved','main_item_count_unchanged','main_source_same_reference',
  'main_source_properties_unchanged','project_saved','reload_memo_single','reload_memo_id_stable','reload_memo_count_three',
  'reload_memo_frame_zero','reload_memo_layers','reload_memo_lengths','reload_memo_offsets','reload_memo_remarks','reload_memo_paths',
  'reload_main_source_semantics'
 )
 if($r.requirements.Count-ne$req.Count){throw "Wrong requirement count: $($r.requirements.Count) != $($req.Count)"}
 foreach($id in $req){
  $found=@($r.requirements|Where-Object{$_.id-eq$id})
  if($found.Count-ne1-or$found[0].passed-cne$true){throw "Missing/failed: $id"}
 }
 Write-Output "PASS_HIGHLIGHT_MEMO_SCENE: $($req.Count) assertions"
}finally{
 if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
 Remove-Item Env:CNWL_HIGHLIGHT_MEMO_OUTPUT,Env:CNWL_HIGHLIGHT_MEMO_MEDIA -ErrorAction SilentlyContinue
}
