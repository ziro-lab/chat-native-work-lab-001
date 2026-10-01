param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir,[Parameter(Mandatory=$true)][string]$WorkDir)
$ErrorActionPreference='Stop'
$OutputDir=[IO.Path]::GetFullPath($OutputDir);$WorkDir=[IO.Path]::GetFullPath($WorkDir)
New-Item -ItemType Directory -Path $OutputDir,$WorkDir -Force | Out-Null
$exe=Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe'
$script:uiaAvailable=$false;$script:uiaError=''
try{Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes -ErrorAction Stop;$script:uiaAvailable=$true}
catch{$script:uiaError=$_.Exception.Message}
function Read-DialogText([IntPtr]$Window) {
 if(-not $script:uiaAvailable){return @{available=$false;error=$script:uiaError;text=@()}}
 try{
  $root=[System.Windows.Automation.AutomationElement]::FromHandle($Window)
  $elements=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
  $text=@();foreach($element in $elements){if($element.Current.ControlType -eq [System.Windows.Automation.ControlType]::Text -and $element.Current.Name){$text+=$element.Current.Name;if($text.Count -ge 12){break}}}
  return @{available=$true;text=$text}
 }catch{return @{available=$false;error=$_.Exception.Message;text=@()}}
}
Add-Type -TypeDefinition @'
using System;using System.Text;using System.Runtime.InteropServices;
public static class PausedNoticeWindows {
 public delegate bool Visit(IntPtr window,IntPtr parameter);
 [DllImport("user32.dll")]public static extern bool EnumWindows(Visit visit,IntPtr parameter);
 [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
 [DllImport("user32.dll")]public static extern bool PostMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
}
'@
function Inspect-Dialogs([int]$TaskProcess,[bool]$Observing) {
 $script:unknown=@()
 $visit=[PausedNoticeWindows+Visit]{param([IntPtr]$window,[IntPtr]$parameter)
  $nativeProcess=[uint32]0;[void][PausedNoticeWindows]::GetWindowThreadProcessId($window,[ref]$nativeProcess)
  if($nativeProcess -eq $TaskProcess -and [PausedNoticeWindows]::IsWindowVisible($window)){
   $text=[Text.StringBuilder]::new(512);[void][PausedNoticeWindows]::GetWindowText($window,$text,$text.Capacity);$title=$text.ToString()
   # These known informational windows may be dismissed. No Enter, consent button or unknown Confirm is ever accepted.
   if($title -like '*Check for updates*' -or $title -like '*About YukkuriMovieMaker*') {
    if($Observing){$script:unknown+=('Popup during observation; no Close sent: '+$title)}
    else{[void][PausedNoticeWindows]::PostMessage($window,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)}
   }
   elseif($title -match '^(Confirm|確認|利用規約|License|Terms|Security|セキュリティ|アクセス許可)$'){$script:unknown+=@{title=$title;message=(Read-DialogText $window)}}
  };return $true
 }
 [void][PausedNoticeWindows]::EnumWindows($visit,[IntPtr]::Zero)
 return $script:unknown
}
function Run-Phase([string]$Phase) {
 $destination=Join-Path $OutputDir $Phase;New-Item -ItemType Directory -Path $destination -Force | Out-Null
 $env:LAB_PAUSED_OUTPUT=$destination;$env:LAB_PAUSED_WORK=$WorkDir;$env:LAB_PAUSED_PHASE=$Phase
 $arguments=@();if($Phase -ne 'seed'){$arguments=@('"'+(Join-Path $WorkDir 'synthetic.ymmp')+'"')}
 # This dedicated CI desktop must render a visible preview for actual screen-pixel evidence.
 if($arguments.Count){$process=Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $Ymm4Dir -WindowStyle Normal -PassThru}
 else{$process=Start-Process -FilePath $exe -WorkingDirectory $Ymm4Dir -WindowStyle Normal -PassThru}
 try{
  $result=Join-Path $destination 'result.json'
  for($i=0;$i -lt 160;$i++){
   if(Test-Path $result){break}
   $unknown=@(Inspect-Dialogs $process.Id (Test-Path (Join-Path $destination 'observing.txt')))
   if($unknown.Count){@{status='BLOCKED';reason='Unrecognized consent/Confirm dialog; no response sent';dialogs=$unknown;phase=$Phase}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $result -Encoding utf8;break}
   if($process.HasExited){break};Start-Sleep -Milliseconds 250
  }
  if(-not(Test-Path $result)){@{status='BLOCKED';reason='No completed real-player observation before timeout/host exit';phase=$Phase}|ConvertTo-Json|Set-Content -LiteralPath $result -Encoding utf8}
  return Get-Content -LiteralPath $result -Raw|ConvertFrom-Json
 }finally{if(-not $process.HasExited){Stop-Process -Id $process.Id -Force}}
}
$rows=@()
try{
 $seed=Run-Phase 'seed';$rows+=$seed
 if($seed.status -eq 'SEEDED'){
  $controlResult=Run-Phase 'control';$rows+=$controlResult
  if($controlResult.reason -notlike 'Unrecognized consent*'){$rows+=Run-Phase 'notify'}
 }
 $control=$rows|Where-Object phase -eq 'control';$notify=$rows|Where-Object phase -eq 'notify'
 $status=if($control.status -eq 'PASS_CONTROL_NO_REPAINT' -and $notify.status -eq 'PASS_NOTIFY_REPAINT'){'PASS_CAUSAL_PAUSED_REPAINT'}elseif($control.status -eq 'PASS_CONTROL_NO_REPAINT' -and $notify.status -eq 'OBSERVED_NO_REPAINT'){'VALID_NEGATIVE_NO_REPAINT'}elseif($rows.status -contains 'BLOCKED'){'BLOCKED'}else{'INCONCLUSIVE_OR_FAILED'}
 @{schema='lab.paused-tachie-notice.summary.v1';status=$status;sourceHead=$env:SOURCE_HEAD;runId=$env:GITHUB_RUN_ID;phases=@($rows|ForEach-Object{@{phase=$_.phase;status=$_.status;reason=$_.reason}});noPostCompletionInteraction=$true;detachedFallback=$false;artifactPolicy='only JSON/minimal build log/synthetic preview PNG allowlist'}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $OutputDir 'summary.json') -Encoding utf8
 Get-Content -LiteralPath (Join-Path $OutputDir 'summary.json') -Raw
 if($status -notin @('PASS_CAUSAL_PAUSED_REPAINT','VALID_NEGATIVE_NO_REPAINT')){throw "Probe outcome: $status"}
}finally{Remove-Item Env:LAB_PAUSED_OUTPUT,Env:LAB_PAUSED_WORK,Env:LAB_PAUSED_PHASE -ErrorAction SilentlyContinue}
