param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir,[Parameter(Mandatory=$true)][string]$MediaPath)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$env:CNWL_EDIT_REBIND_OUTPUT=$OutputDir
$env:CNWL_EDIT_REBIND_MEDIA=$MediaPath
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try{
 $limit=[DateTime]::UtcNow.AddSeconds(100);$result=Join-Path $OutputDir 'result.json'
 while([DateTime]::UtcNow -lt $limit -and -not $p.HasExited -and -not(Test-Path $result)){Start-Sleep -Milliseconds 400}
 if(-not(Test-Path $result)){throw 'No native result'}
 $r=Get-Content -Raw $result|ConvertFrom-Json
 Get-Content $result
 if(Test-Path (Join-Path $OutputDir 'behavior.json')){Get-Content (Join-Path $OutputDir 'behavior.json')}
 if($r.schema-ne'cnwl.edit-rebinding.v1'-or$r.status-ne'PASS_EDIT_REBINDING'-or$r.host-ne'4.56.1.0 Lite'-or$r.sourceHead-ne$env:GITHUB_SHA-or$r.error){throw 'Behavior result rejected'}
 $req=@(
 'fixture_exists','fps_positive','public_edit_surfaces',
 'head_trim_insert','head_trim_same_reference','head_trim_timeline','head_trim_source',
 'tail_trim_insert','tail_trim_same_reference','tail_trim_timeline','tail_trim_source',
 'move_insert','move_split_two','move_same_reference','move_timeline_changed','move_source_unchanged',
 'duplicate_insert','duplicate_two_occurrences','duplicate_same_source_range','duplicate_different_timeline_positions',
 'undo_insert','keyboard_split_created','keyboard_undo_restores_one_piece','keyboard_redo_restores_two_pieces'
 )
 if($r.requirements.Count-ne$req.Count){throw "Wrong requirement count: $($r.requirements.Count) != $($req.Count)"}
 foreach($id in $req){
   $f=@($r.requirements|Where-Object { $_.id -eq $id })
   if($f.Count-ne1-or$f[0].passed-cne$true){throw "Missing/failed $id"}
 }
 Write-Output "PASS_EDIT_REBINDING: $($req.Count) required assertions"
}finally{
 if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
 Remove-Item Env:CNWL_EDIT_REBIND_OUTPUT,Env:CNWL_EDIT_REBIND_MEDIA -ErrorAction SilentlyContinue
}
