param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$result=Join-Path $OutputDir 'result.txt'
$surface=Join-Path $OutputDir 'surface.txt'
Remove-Item $result,$surface -Force -ErrorAction SilentlyContinue
$env:CNWL_YMM4_TP_UX_SURFACE_DIR=$OutputDir
$p=Start-Process -FilePath (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try {
  for($i=0;$i -lt 120 -and -not(Test-Path $result) -and -not $p.HasExited;$i++){ Start-Sleep -Milliseconds 500 }
  if(-not(Test-Path $result)){ throw 'UX host surface probe did not report a result.' }
  $rows=Get-Content $result
  if($rows -notcontains 'status=PASS_TEMPLATE_PLACER_UX_HOST_SURFACE'){ throw "UX host surface probe failed.`n$($rows -join "`n")" }
  if(-not(Test-Path $surface)){ throw 'surface.txt missing.' }
} finally {
  if(-not $p.HasExited){ Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
  Remove-Item Env:CNWL_YMM4_TP_UX_SURFACE_DIR -ErrorAction SilentlyContinue
}
