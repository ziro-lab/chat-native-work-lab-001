param([Parameter(Mandatory=$true)][string]$Ymm4Dir,[Parameter(Mandatory=$true)][string]$OutputDir)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDir|Out-Null
$result=Join-Path $OutputDir 'result.json'
$hostLog=Join-Path $OutputDir 'host-log.txt'
$fixture=Join-Path $OutputDir 'fixture'
Remove-Item $result,$hostLog -Force -ErrorAction SilentlyContinue
Remove-Item $fixture -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $fixture|Out-Null

$anim=Join-Path $fixture 'animation'
New-Item -ItemType Directory -Force $anim|Out-Null
$presetIni=@'
[CNWL_ANIM_NEUTRAL]
眉=neutral.png
目=neutral.png
口=neutral.png
[CNWL_ANIM_SMILE]
眉=smile.png
目=smile.png
口=smile.png
'@
Set-Content (Join-Path $anim 'preset.ini') $presetIni -Encoding utf8

$png=[Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=')
foreach($sub in @('眉','目','口')){
  $d=Join-Path $anim $sub
  New-Item -ItemType Directory -Force $d|Out-Null
  [IO.File]::WriteAllBytes((Join-Path $d 'neutral.png'),$png)
  [IO.File]::WriteAllBytes((Join-Path $d 'smile.png'),$png)
}

$psdDir=Join-Path $fixture 'psd'
New-Item -ItemType Directory -Force $psdDir|Out-Null
$psd=Join-Path $psdDir '1layer.psd'
$psdUrl='https://raw.githubusercontent.com/psd-tools/psd-tools/5fe6781f32c8d9af39ed9a3786934dd1c26ce212/tests/psd_files/1layer.psd'
curl.exe -L --fail --retry 3 --silent --show-error -o $psd $psdUrl
if($LASTEXITCODE-ne0){throw 'PSD fixture download failed'}
$psdHash=(Get-FileHash $psd -Algorithm SHA256).Hash.ToLowerInvariant()
$psdSize=(Get-Item $psd).Length
if($psdSize-ne6476){throw "PSD fixture size mismatch: $psdSize"}
$ymmJson=@'
{
  "Presets": [
    { "Name": "CNWL_PSD_ON", "Layers": ["n1"] },
    { "Name": "CNWL_PSD_OFF", "Layers": [] }
  ],
  "MouthAnimations": [],
  "MouthVowelAnimations": [],
  "EyeAnimations": []
}
'@
Set-Content (Join-Path $psdDir '1layer-ymm.json') $ymmJson -Encoding utf8

[ordered]@{
 schema='cnwl.tachie-preset-product-bridge-fixture.v1'
 psd_source_commit='5fe6781f32c8d9af39ed9a3786934dd1c26ce212'
 psd_source_path='tests/psd_files/1layer.psd'
 psd_blob_sha='2420ee826ae9c2271cd80a0b01ec394d26b38c07'
 psd_sha256=$psdHash
 psd_size=$psdSize
}|ConvertTo-Json|Set-Content (Join-Path $OutputDir 'fixture.json')

Add-Type -TypeDefinition @'
using System; using System.Text; using System.Runtime.InteropServices;
public static class CnwlTachiePresetBridgeWin32 {
 public delegate bool EnumWindowsProc(IntPtr h,IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb,IntPtr l);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
}
'@
function Windows {
 $script:rows=@()
 $cb=[CnwlTachiePresetBridgeWin32+EnumWindowsProc]{param([IntPtr]$h,[IntPtr]$l)
  if([CnwlTachiePresetBridgeWin32]::IsWindowVisible($h)){
   $s=New-Object Text.StringBuilder 1024
   [void][CnwlTachiePresetBridgeWin32]::GetWindowText($h,$s,1024)
   if($s.Length){$script:rows += [pscustomobject]@{Handle=$h;Title=$s.ToString()}}
  };return $true}
 [void][CnwlTachiePresetBridgeWin32]::EnumWindows($cb,[IntPtr]::Zero)
 $script:rows
}

$env:CNWL_TACHIE_PRESET_BRIDGE_OUTPUT=$OutputDir
$env:CNWL_TACHIE_PRESET_BRIDGE_FIXTURE=$fixture
$p=Start-Process (Join-Path $Ymm4Dir 'YukkuriMovieMaker.exe') -WorkingDirectory $Ymm4Dir -PassThru
try{
 for($i=0;$i-lt300;$i++){
  if(Test-Path $result){break}
  if($p.HasExited){break}
  foreach($w in Windows){
   "tick=$i handle=$($w.Handle) title=$($w.Title)"|Add-Content $hostLog
   if($w.Title -like '*Check for updates*' -or $w.Title -like '*About YukkuriMovieMaker*'){
    [void][CnwlTachiePresetBridgeWin32]::PostMessage($w.Handle,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
   }elseif($w.Title -eq 'Confirm'){
    [void][CnwlTachiePresetBridgeWin32]::PostMessage($w.Handle,0x0100,[IntPtr]0x0D,[IntPtr]::Zero)
    [void][CnwlTachiePresetBridgeWin32]::PostMessage($w.Handle,0x0101,[IntPtr]0x0D,[IntPtr]::Zero)
   }
  }
  Start-Sleep -Milliseconds 500
 }
 if(-not(Test-Path $result)){
  if($p.HasExited){throw "No result; YMM4 exited $($p.ExitCode)"}
  throw 'No Tachie Preset product bridge result'
 }
 $r=Get-Content -Raw $result|ConvertFrom-Json
 Get-Content $result
 if($r.schema-cne'cnwl.tachie-preset-product-bridge.v1'-or$r.status-cne'PASS_TACHIE_PRESET_PRODUCT_BRIDGE_P0'-or$r.host-cne'4.55.1.1 Lite'){
  throw 'Tachie Preset product bridge P0 rejected'
 }
 foreach($name in @(
   'animationCharacterResolution','psdCharacterResolution','modernItemPropertyBinding',
   'animationFingerprintStable','psdFingerprintStable',
   'animationItemRoundtrip','psdItemRoundtrip',
   'animationCleanupCancellation','psdCleanupCancellation'
 )){
   if(-not$r.assertions.$name){throw "P0 assertion failed: $name"}
 }
 Write-Output "PASS_TACHIE_PRESET_PRODUCT_BRIDGE_P0"
}finally{
 if(-not$p.HasExited){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue}
 Remove-Item Env:CNWL_TACHIE_PRESET_BRIDGE_OUTPUT -ErrorAction SilentlyContinue
 Remove-Item Env:CNWL_TACHIE_PRESET_BRIDGE_FIXTURE -ErrorAction SilentlyContinue
}
