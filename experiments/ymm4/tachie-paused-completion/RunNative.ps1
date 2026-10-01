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
  Start-Sleep -Milliseconds 1000 # Read only after accessible peers have had time to initialize.
  $root=[System.Windows.Automation.AutomationElement]::FromHandle($Window)
  $elements=$root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
  $text=@();$controls=@()
  foreach($element in $elements){
   if($controls.Count -lt 24){$controls+=@{type=$element.Current.ControlType.ProgrammaticName;name=$element.Current.Name}}
   if($element.Current.Name){$text+=$element.Current.Name}
   $pattern=$null
   if($element.TryGetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern,[ref]$pattern)){$value=$pattern.DocumentRange.GetText(2048);if($value){$text+=$value}}
   $pattern=$null
   if($element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern,[ref]$pattern)){$value=$pattern.Current.Value;if($value){$text+=$value}}
   if($text.Count -ge 24){break}
  }
  return @{available=$true;descendantCount=$elements.Count;text=@($text|Select-Object -Unique);controls=$controls}
 }catch{return @{available=$false;error=$_.Exception.Message;text=@()}}
}
function Decline-Association([IntPtr]$Window,$Message) {
 $body=$Message.text -join "`n"
 if(-not $Message.available -or $body -notmatch 'The extension for YMM4 is not associated with YUUKURI MovieMaker4' -or $body -notmatch 'Do you want to associate the following extensions\?' -or $body -notmatch '\.ymmp: Project file' -or $body -notmatch '\.ymmt: Template file' -or $body -notmatch '\.ymme: Plugin file'){return $false}
 try{
  $root=[System.Windows.Automation.AutomationElement]::FromHandle($Window)
  $button=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'No'))
  if($null -eq $button){return $false}
  $pattern=$null
  if($button.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern,[ref]$pattern)){
   $pattern.Invoke();'DECLINED_KNOWN_FILE_ASSOCIATION: exact body matched; public No InvokePattern; before baseline'|Add-Content -LiteralPath (Join-Path $OutputDir 'dialog-decisions.log');return $true
  }
  $handle=[IntPtr]$button.Current.NativeWindowHandle
  $class=[Text.StringBuilder]::new(64);[void][PausedNoticeWindows]::GetClassName($handle,$class,$class.Capacity)
  if($handle -ne [IntPtr]::Zero -and $class.ToString() -eq 'Button'){
   [void][PausedNoticeWindows]::PostMessage($handle,0x00F5,[IntPtr]::Zero,[IntPtr]::Zero)
   'DECLINED_KNOWN_FILE_ASSOCIATION: exact body matched; exact public No Button BM_CLICK; before baseline'|Add-Content -LiteralPath (Join-Path $OutputDir 'dialog-decisions.log');return $true
  }
 }catch{}
 return $false
}
Add-Type -TypeDefinition @'
using System;using System.Text;using System.Runtime.InteropServices;
public static class PausedNoticeWindows {
 public delegate bool Visit(IntPtr window,IntPtr parameter);
 [DllImport("user32.dll")]public static extern bool EnumWindows(Visit visit,IntPtr parameter);
 [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int GetClassName(IntPtr window,StringBuilder text,int count);
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
   elseif($title -match '^(Confirm|確認|利用規約|License|Terms|Security|セキュリティ|アクセス許可)$'){
    $message=Read-DialogText $window
    if($Observing -or -not(Decline-Association $window $message)){$script:unknown+=@{title=$title;message=$message}}
   }
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
