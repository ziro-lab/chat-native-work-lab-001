param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir,[Parameter(Mandatory=$true)][string]$MediaPath)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$env:CNWL_EDIT_REBIND_OUTPUT=$OutputDir
$env:CNWL_EDIT_REBIND_MEDIA=$MediaPath
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try{
 $limit=[DateTime]::UtcNow.AddSeconds(90);$result=Join-Path $OutputDir 'result.json'
 while([DateTime]::UtcNow -lt $limit -and -not $p.HasExited -and -not(Test-Path $result)){Start-Sleep -Milliseconds 400}
 if(-not(Test-Path $result)){throw 'No native result'}
 $r=Get-Content -Raw $result|ConvertFrom-Json
 Get-Content $result
 if($r.schema-ne'cnwl.edit-rebinding.discovery.v1'-or$r.status-ne'PASS_EDIT_REBIND_DISCOVERY'-or$r.sourceHead-ne$env:GITHUB_SHA-or$r.error){throw 'Discovery result rejected'}
 $req=@('fixture_exists','fps_positive','insert_source','surface_dumped')
 if($r.requirements.Count-ne$req.Count){throw 'Wrong requirement count'}
 foreach($id in $req){$f=@($r.requirements|Where-Object id-eq$id);if($f.Count-ne1-or$f[0].passed-cne$true){throw "Missing/failed $id"}}
 Write-Output 'PASS_EDIT_REBIND_DISCOVERY'
}finally{
 if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
 Remove-Item Env:CNWL_EDIT_REBIND_OUTPUT,Env:CNWL_EDIT_REBIND_MEDIA -ErrorAction SilentlyContinue
}
