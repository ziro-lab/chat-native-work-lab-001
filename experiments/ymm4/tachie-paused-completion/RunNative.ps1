param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir,[Parameter(Mandatory=$true)][string]$WorkDir)
$ErrorActionPreference='Stop'
$OutputDir=[IO.Path]::GetFullPath($OutputDir);$WorkDir=[IO.Path]::GetFullPath($WorkDir)
New-Item -ItemType Directory -Path $OutputDir,$WorkDir -Force | Out-Null
$exe=Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe'
$script:uiaAvailable=$false;$script:uiaError=''
try{Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes -ErrorAction Stop;$script:uiaAvailable=$true}
catch{$script:uiaError=$_.Exception.GetType().Name}
Add-Type -TypeDefinition @'
using System;using System.Text;using System.Runtime.InteropServices;
public static class PausedNoticeWindows {
 public delegate bool Visit(IntPtr window,IntPtr parameter);
 [DllImport("user32.dll")]public static extern bool EnumWindows(Visit visit,IntPtr parameter);
 [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")]public static extern bool IsWindowEnabled(IntPtr window);
 [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int GetClassName(IntPtr window,StringBuilder text,int count);
 [DllImport("user32.dll")]public static extern IntPtr GetWindow(IntPtr window,uint command);
 [DllImport("user32.dll")]public static extern bool PostMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
}
'@
function Window-Inventory([int]$TaskProcess) {
 $script:windowRows=@()
 $visit=[PausedNoticeWindows+Visit]{param([IntPtr]$window,[IntPtr]$parameter)
  $nativeProcess=[uint32]0;$thread=[PausedNoticeWindows]::GetWindowThreadProcessId($window,[ref]$nativeProcess)
  if($nativeProcess -eq $TaskProcess -and [PausedNoticeWindows]::IsWindowVisible($window)){
   $text=[Text.StringBuilder]::new(512);[void][PausedNoticeWindows]::GetWindowText($window,$text,$text.Capacity)
   $class=[Text.StringBuilder]::new(256);[void][PausedNoticeWindows]::GetClassName($window,$class,$class.Capacity)
   $script:windowRows+=@{handle=$window.ToInt64();owner=[PausedNoticeWindows]::GetWindow($window,4).ToInt64();processId=$nativeProcess;threadId=$thread;title=$text.ToString();class=$class.ToString();enabled=[PausedNoticeWindows]::IsWindowEnabled($window)}
  };return $true
 }
 [void][PausedNoticeWindows]::EnumWindows($visit,[IntPtr]::Zero)
 return $script:windowRows
}
function Read-DialogText([IntPtr]$Window) {
 if(-not $script:uiaAvailable){return @{available=$false;error=$script:uiaError;text=@()}}
 try{
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
 }catch{return @{available=$false;error=$_.Exception.GetType().Name;text=@()}}
}
function Decline-Association([IntPtr]$Window,$Message) {
 $body=$Message.text -join "`n"
 $english=$Message.available -and $body -match 'The extension for YMM4 is not associated with YUUKURI MovieMaker4' -and $body -match 'Do you want to associate the following extensions\?' -and $body -match '\.ymmp: Project file' -and $body -match '\.ymmt: Template file' -and $body -match '\.ymme: Plugin file'
 $japaneseExpected='YMM4用の拡張子がゆっくりMovieMaker4に関連付けられていません。 以下の拡張子を関連付けしますか？ - .ymmp: プロジェクトファイル - .ymmt: テンプレートファイル - .ymme: プラグインファイル 関連付けると、各ファイルをダブルクリックしてYMM4を起動できるようになります。'
 $japanese=$Message.available -and @($Message.text|Where-Object {($_ -replace '\s+',' ').Trim() -ceq $japaneseExpected}).Count -gt 0
 if(-not $english -and -not $japanese){return $false}
 $noName=if($japanese){'いいえ(N)'}else{'No'}
 try{
  $root=[System.Windows.Automation.AutomationElement]::FromHandle($Window)
  $button=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$noName))
  if($null -eq $button){return $false}
  $handle=[IntPtr]$button.Current.NativeWindowHandle
  $class=[Text.StringBuilder]::new(64);[void][PausedNoticeWindows]::GetClassName($handle,$class,$class.Capacity)
  if($handle -ne [IntPtr]::Zero -and $class.ToString() -eq 'Button'){
   [void][PausedNoticeWindows]::PostMessage($handle,0x00F5,[IntPtr]::Zero,[IntPtr]::Zero);return $true
  }
  $pattern=$null
  if($button.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern,[ref]$pattern)){$pattern.Invoke();return $true}
 }catch{}
 return $false
}
function Close-KnownAssociationInformation([IntPtr]$Window,$Message) {
 $expected='If you want to associate in the future, please click Help (H) → Associate extension for YMM4 → Register to register the file.'
 $japaneseExpected='今後、関連付けしたい場合は ヘルプ(H) → YMM4用拡張子の関連付け → 登録する を実行してください。'
 $matched=@($Message.text|Where-Object {$normalized=($_ -replace '\s+',' ').Trim();$normalized -ceq $expected -or $normalized -ceq $japaneseExpected}).Count -gt 0
 if(-not $Message.available -or -not $matched){return $false}
 try{
  $root=[System.Windows.Automation.AutomationElement]::FromHandle($Window)
  $button=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,'OK'))
  if($null -eq $button){return $false}
  $handle=[IntPtr]$button.Current.NativeWindowHandle
  $class=[Text.StringBuilder]::new(64);[void][PausedNoticeWindows]::GetClassName($handle,$class,$class.Capacity)
  if($handle -ne [IntPtr]::Zero -and $class.ToString() -eq 'Button'){
   [void][PausedNoticeWindows]::PostMessage($handle,0x00F5,[IntPtr]::Zero,[IntPtr]::Zero);return $true
  }
 }catch{}
 return $false
}
function Run-Phase([string]$Phase) {
 $destination=Join-Path $OutputDir $Phase;New-Item -ItemType Directory -Path $destination -Force | Out-Null
 # Start every phase from the pristine official host plus this probe; imported character/settings state is never shared.
 $phaseHost=Join-Path $WorkDir ('host-'+$Phase)
 if(Test-Path $phaseHost){throw 'Phase host already exists; refusing to reuse modified settings'}
 Copy-Item -LiteralPath $Ymm4Dir -Destination $phaseHost -Recurse
 $phaseExe=Join-Path $phaseHost 'YukkuriMovieMaker.exe'
 if((Get-FileHash -LiteralPath $phaseExe).Hash -ne (Get-FileHash -LiteralPath $exe).Hash){throw 'Phase official host hash mismatch'}
 $env:LAB_PAUSED_OUTPUT=$destination;$env:LAB_PAUSED_WORK=$WorkDir;$env:LAB_PAUSED_PHASE=$Phase
 $arguments=@();if($Phase -ne 'seed'){$arguments=@('"'+(Join-Path $WorkDir 'synthetic.ymmp')+'"')}
 $started=[DateTimeOffset]::UtcNow;$clock=[Diagnostics.Stopwatch]::StartNew();$snapshots=@();$dialogs=@();$actions=@();$knownHandles=@{};$lastWindowSignature='';$lastEventCount=-1;$quietSince=$null;$boundary=''
 $result=Join-Path $destination 'result.json';$eventsPath=Join-Path $destination 'plugin-events.jsonl';$barrier=Join-Path $destination 'baseline-permitted.json';$windows=@();$events=@()
 # Visible windows are required for real desktop preview evidence on this isolated CI desktop.
 if($arguments.Count){$process=Start-Process -FilePath $phaseExe -ArgumentList $arguments -WorkingDirectory $phaseHost -WindowStyle Normal -PassThru}
 else{$process=Start-Process -FilePath $phaseExe -WorkingDirectory $phaseHost -WindowStyle Normal -PassThru}
 try{
  while($clock.Elapsed.TotalSeconds -lt 120){
   $process.Refresh();$windows=@(Window-Inventory $process.Id)
   $events=@();if(Test-Path $eventsPath){$events=@(Get-Content -LiteralPath $eventsPath|ForEach-Object{try{$_|ConvertFrom-Json}catch{}})}
   $signature=$windows|ConvertTo-Json -Compress -Depth 4
   if($signature -ne $lastWindowSignature -or $events.Count -ne $lastEventCount){
    if($snapshots.Count -lt 160){$snapshots+=@{utc=[DateTimeOffset]::UtcNow.ToString('o');elapsedMs=[int64]$clock.Elapsed.TotalMilliseconds;windows=$windows;pluginEventCount=$events.Count;pluginStage=($events|Select-Object -Last 1).name;syntheticProjectExists=(Test-Path (Join-Path $WorkDir 'synthetic.ymmp'));processExited=$process.HasExited}}
    $lastWindowSignature=$signature;$lastEventCount=$events.Count
   }
   if(Test-Path $result){$boundary='plugin-result';break}
   if($process.HasExited){$boundary='host-exited-before-result';break}
   $locked=(Test-Path $barrier)
   $popups=@($windows|Where-Object {$_.title -and $_.title -notmatch '^(YukkuriMovieMaker|ゆっくりMovieMaker) v' -and $_.title -ne 'Lab paused observer'})
   foreach($w in $popups){
    $key=[string]$w.handle
    if($locked){$boundary='popup-after-baseline-permission';@{status='BLOCKED';phase=$Phase;reason='Popup after baseline permission; no UI action';windows=$windows}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $result -Encoding utf8;break}
    if(-not $knownHandles.ContainsKey($key)){
     Start-Sleep -Milliseconds 400
     if($w.title -like '*About YukkuriMovieMaker*' -or $w.title -eq 'ゆっくりMovieMakerについて' -or $w.title -like '*Check for updates*' -or $w.class -eq 'YMM4SplashWindow'){$message=@{available=$true;text=@();purpose='known startup/information title; no full changelog extraction'}}
     else{$message=Read-DialogText ([IntPtr]$w.handle)}
     $dialog=@{utc=[DateTimeOffset]::UtcNow.ToString('o');elapsedMs=[int64]$clock.Elapsed.TotalMilliseconds;origin=@{processId=$w.processId;threadId=$w.threadId;handle=$w.handle;owner=$w.owner;class=$w.class;title=$w.title;exe='YukkuriMovieMaker.exe'};message=$message}
     $dialogs+=$dialog;$knownHandles[$key]=$message;$decision='observed-only'
     if($w.title -like '*Check for updates*' -or $w.title -like '*About YukkuriMovieMaker*' -or $w.title -eq 'ゆっくりMovieMakerについて'){
      [void][PausedNoticeWindows]::PostMessage([IntPtr]$w.handle,0x0010,[IntPtr]::Zero,[IntPtr]::Zero);$decision='close-known-information-before-permission'
     }elseif($w.title -match '^(Confirm|確認)$' -and (Decline-Association ([IntPtr]$w.handle) $message)){$decision='decline-exact-file-association-No-before-permission'}
     elseif($w.title -match '^(Notification|通知)$' -and (Close-KnownAssociationInformation ([IntPtr]$w.handle) $message)){$decision='close-exact-future-association-information-OK-before-permission'}
     elseif($w.title -match '^(Confirm|確認|利用規約|License|Terms|Security|セキュリティ|アクセス許可)$'){
      $boundary='unknown-consent';@{status='BLOCKED';phase=$Phase;reason='Unrecognized consent/Confirm dialog; no response sent';dialog=$dialog}|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $result -Encoding utf8
     }
     $actions+=@{utc=[DateTimeOffset]::UtcNow.ToString('o');elapsedMs=[int64]$clock.Elapsed.TotalMilliseconds;handle=$w.handle;title=$w.title;decision=$decision;baselinePermissionExists=$locked}
     $actions[-1]|ConvertTo-Json -Compress|Add-Content -LiteralPath (Join-Path $OutputDir 'dialog-decisions.log')
    }
   }
   if(Test-Path $result){if(-not $boundary){$boundary='plugin-result'};break}
   $main=@($windows|Where-Object title -match '^(YukkuriMovieMaker|ゆっくりMovieMaker) v')
   $clear=$popups.Count -eq 0 -and $main.Count -eq 1 -and $main[0].enabled
   if($clear){if($null -eq $quietSince){$quietSince=$clock.Elapsed.TotalSeconds}}else{$quietSince=$null}
   if($Phase -ne 'seed' -and -not $locked -and (Test-Path (Join-Path $destination 'baseline-requested.txt')) -and $null -ne $quietSince -and $clock.Elapsed.TotalSeconds-$quietSince -ge 3){
    @{utc=[DateTimeOffset]::UtcNow.ToString('o');elapsedMs=[int64]$clock.Elapsed.TotalMilliseconds;quietSeconds=$clock.Elapsed.TotalSeconds-$quietSince;processId=$process.Id;mainHandle=$main[0].handle;mainEnabled=$true;visiblePopupCount=0;noFurtherUiActions=$true}|ConvertTo-Json|Set-Content -LiteralPath $barrier -Encoding utf8
   }
   Start-Sleep -Milliseconds 250
  }
  $process.Refresh();$exitedBeforeCleanup=$process.HasExited;$exitCode=if($exitedBeforeCleanup){$process.ExitCode}else{$null}
  if(Test-Path $eventsPath){$events=@(Get-Content -LiteralPath $eventsPath|ForEach-Object{try{$_|ConvertFrom-Json}catch{}})}
  if(-not(Test-Path $result)){
   if($exitedBeforeCleanup){$boundary='host-exited-before-result';$reason='Host exited before plugin result'}else{$boundary='phase-deadline-host-alive';$reason='Phase deadline reached while host remained alive'}
   @{status='BLOCKED';reason=$reason;phase=$Phase;exitCode=$exitCode;lastPluginStage=($events|Select-Object -Last 1).name;windows=$windows}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $result -Encoding utf8
  }
  return Get-Content -LiteralPath $result -Raw|ConvertFrom-Json
 }finally{
  $process.Refresh();$exitedBeforeCleanup=$process.HasExited;$exitCode=if($exitedBeforeCleanup){$process.ExitCode}else{$null}
  @{schema='lab.paused-tachie-startup.v1';freshHostPerPhase=$true;hostExeSha256=(Get-FileHash -LiteralPath $phaseExe).Hash;phase=$Phase;sourceHead=$env:SOURCE_HEAD;runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;startedUtc=$started.ToString('o');finishedUtc=[DateTimeOffset]::UtcNow.ToString('o');elapsedMs=[int64]$clock.Elapsed.TotalMilliseconds;processId=$process.Id;boundary=$boundary;exitedBeforeCleanup=$exitedBeforeCleanup;exitCodeBeforeCleanup=$exitCode;forcedCleanup=(-not $exitedBeforeCleanup);pluginConstructorSeen=(@($events|Where-Object name -eq 'plugin-constructed').Count -gt 0);cultureCallbackSeen=(@($events|Where-Object name -eq 'plugin-set-culture').Count -gt 0);dispatcherEntered=(@($events|Where-Object name -eq 'plugin-dispatch-enter').Count -gt 0);lastPluginStage=($events|Select-Object -Last 1).name;projectFileExists=(Test-Path (Join-Path $WorkDir 'synthetic.ymmp'));snapshots=$snapshots;dialogs=$dialogs;actions=$actions}|ConvertTo-Json -Depth 14|Set-Content -LiteralPath (Join-Path $destination 'startup.json') -Encoding utf8
  if(-not $exitedBeforeCleanup){Stop-Process -Id $process.Id -Force};[void]$process.WaitForExit(5000)
 }
}
$rows=@()
try{
 $seed=Run-Phase 'seed';$rows+=$seed
 if($seed.status -eq 'SEEDED'){
  foreach($phase in @('control-start','param-replace','item-replace','add-remove')){
   Start-Sleep -Seconds 1;$row=Run-Phase $phase;$rows+=$row
   if($row.status -in @('BLOCKED','FAIL','OBSERVED_CONTROL_REPAINT')){break}
  }
 }
 $valid=@('OBSERVED_ITEM_REPAINT_CLEAN','OBSERVED_ITEM_REPAINT_WITH_SIDE_EFFECTS','OBSERVED_NO_REPAINT')
 $status=if($rows.status -contains 'BLOCKED'){'BLOCKED'}elseif($rows.status -contains 'FAIL' -or $rows.Count -lt 2 -or $rows[1].status -ne 'PASS_CONTROL_NO_REPAINT'){'INCONCLUSIVE_OR_FAILED'}elseif(@($rows|Select-Object -Skip 2|Where-Object {$_.status -notin $valid}).Count -gt 0){'INCONCLUSIVE_OR_FAILED'}else{'OBSERVED_ITEM_REFRESH_MATRIX'}
 @{schema='lab.paused-item-refresh-matrix.v1';status=$status;sourceHead=$env:SOURCE_HEAD;runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;phases=@($rows|ForEach-Object{@{phase=$_.phase;status=$_.status;reason=$_.reason;observation=$_.observation}});routes=@('same Item + ready parameter clone','same-content TachieItem clone replacement','temporary ready clone Item add/remove');notificationUsed=$false;frameMoved=$false;dialogFreeBarrierRequired=$true;detachedFallback=$false;audioMeasured=$false;liveDirtyFlagMeasured=$false;artifactPolicy='literal JSON/minimal log/synthetic preview PNG allowlist'}|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $OutputDir 'summary.json') -Encoding utf8
 Get-Content -LiteralPath (Join-Path $OutputDir 'summary.json') -Raw
 if($status -ne 'OBSERVED_ITEM_REFRESH_MATRIX'){throw "Probe outcome: $status"}
}finally{Remove-Item Env:LAB_PAUSED_OUTPUT,Env:LAB_PAUSED_WORK,Env:LAB_PAUSED_PHASE -ErrorAction SilentlyContinue}
