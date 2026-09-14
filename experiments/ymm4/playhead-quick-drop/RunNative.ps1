param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$result=Join-Path $OutputDir 'result.txt'
Remove-Item $result -Force -ErrorAction SilentlyContinue
$env:CNWL_YMM4_PLAYHEAD_DIR=$OutputDir
$exe=Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe'
$p=Start-Process -FilePath $exe -WorkingDirectory $Ymm4Dir -PassThru
try {
  for($i=0;$i -lt 120;$i++) {
    if(Test-Path $result){break}
    if($p.HasExited){break}
    Start-Sleep -Milliseconds 500
  }
  if(-not(Test-Path $result)){throw 'Playhead probe did not produce result.txt.'}
  $lines=Get-Content $result
  $status=$lines | Where-Object {$_ -like 'status=*'} | Select-Object -First 1
  if($status -notin @('status=PASS_PLAYHEAD_QUICK_DROP','status=DISCOVERY_NO_PUBLIC_FRAME','status=DISCOVERY_PUBLIC_FRAME_READ_ONLY')) {
    throw "Unexpected playhead result:`n$($lines -join "`n")"
  }
} finally {
  if(-not $p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
  Remove-Item Env:CNWL_YMM4_PLAYHEAD_DIR -ErrorAction SilentlyContinue
}
