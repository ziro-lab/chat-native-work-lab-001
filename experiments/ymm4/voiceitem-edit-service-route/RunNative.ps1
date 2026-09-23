param(
 [Parameter(Mandatory=$true)][string]$Ymm4Dir,
 [Parameter(Mandatory=$true)][string]$OutputDir
)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$env:CNWL_VOICEITEM_EDIT_ROUTE_OUTPUT=$OutputDir
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try{
 $limit=[DateTime]::UtcNow.AddSeconds(90)
 $result=Join-Path $OutputDir 'result.json'
 while([DateTime]::UtcNow-lt$limit -and -not$p.HasExited -and -not(Test-Path $result)){Start-Sleep -Milliseconds 350}
 if(-not(Test-Path $result)){throw 'No native result'}
 $r=Get-Content -Raw $result|ConvertFrom-Json
 Get-Content $result
 if(Test-Path (Join-Path $OutputDir 'surface.json')){Get-Content (Join-Path $OutputDir 'surface.json')}
 if($r.schema-ne'cnwl.voiceitem-edit-service-route.v1' -or $r.status-ne'PASS_VOICEITEM_EDIT_SERVICE_ROUTE_INVENTORY' -or $r.error){throw 'Result rejected'}
 foreach($id in @('interface_public','implementation_found')){
  $f=@($r.requirements|Where-Object{$_.id-eq$id})
  if($f.Count-ne1-or$f[0].passed-cne$true){throw "Missing/failed $id"}
 }
 Write-Output 'PASS_VOICEITEM_EDIT_SERVICE_ROUTE_INVENTORY'
}finally{
 if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
 Remove-Item Env:CNWL_VOICEITEM_EDIT_ROUTE_OUTPUT -ErrorAction SilentlyContinue
}
