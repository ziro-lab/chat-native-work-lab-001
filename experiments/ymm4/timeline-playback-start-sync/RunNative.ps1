param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$result=Join-Path $OutputDir 'result.json'
$env:CNWL_YMM4_PLAYBACK_SYNC_DIR=$OutputDir
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try{
 $limit=[DateTime]::UtcNow.AddSeconds(140)
 while([DateTime]::UtcNow-lt$limit -and -not$p.HasExited -and -not(Test-Path $result)){Start-Sleep -Milliseconds 400}
 if(-not(Test-Path $result)){throw 'No playback-start-sync result'}
 $r=Get-Content -Raw $result|ConvertFrom-Json
 Get-Content $result
 if($r.schema-cne'cnwl.timeline-playback-start-sync.v1'-or$r.status-cne'PASS_PLAYBACK_START_SYNC_OBSERVATION'-or$r.host-cne'4.55.1.1 Lite'){throw 'Playback sync observation rejected'}
 if(@($r.programmatic.playbackFrames).Count-lt1 -or @($r.ruler.playbackFrames).Count-lt1){throw 'Playback trace missing'}
 Write-Output "PASS_PLAYBACK_START_SYNC_OBSERVATION programmatic_near=$($r.programmatic.startedNearDisplayedFrame) ruler_near=$($r.ruler.startedNearDisplayedFrame)"
}finally{
 if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
 Remove-Item Env:CNWL_YMM4_PLAYBACK_SYNC_DIR -ErrorAction SilentlyContinue
}
