param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir)
$ErrorActionPreference='Stop';New-Item -ItemType Directory -Force $OutputDir|Out-Null;$env:CNWL_YMM4_INPUT_DIR=$OutputDir
$p=Start-Process -FilePath (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try{
 for($i=0;$i -lt 180;$i++){if(Test-Path (Join-Path $OutputDir 'result.txt')){break};if($p.HasExited){break};Start-Sleep -Milliseconds 500}
 $r=Join-Path $OutputDir 'result.txt';if(-not(Test-Path $r)){throw 'No result.txt'};Get-Content $r
 if(-not((Get-Content $r)-contains 'status=PASS_TIMELINE_INPUT_ROUTE')){throw 'Input route probe failed'}
 foreach($required in @('item_pointer_route_observed=True','item_reclick_pointer_route_observed=True','blank_pointer_route_observed=True','ruler_pointer_route_observed=True')){if(-not((Get-Content $r)-contains $required)){throw "Missing $required"}}
}finally{if(-not $p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue};Remove-Item Env:CNWL_YMM4_INPUT_DIR -ErrorAction SilentlyContinue}
