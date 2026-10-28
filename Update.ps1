param([Parameter(Mandatory=$true)][string]$CurrentExe,[int]$AppProcessId)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
$next=$CurrentExe
try {
 $headers=@{'User-Agent'='SimpleMacroPad-Updater'}
 $release=Invoke-RestMethod 'https://api.github.com/repos/vtubeadmirer/simple-macropad/releases/latest' -Headers $headers
 if($release.tag_name -notmatch '^v(\d+\.\d+\.\d+)$'){throw '올바르지 않은 버전입니다.'}
 $version=[version]($Matches[1]+'.0')
 $current=[version](Get-Item -LiteralPath $CurrentExe).VersionInfo.FileVersion
 # Migrate the former major-version numbering to 0.x without blocking updates.
 if($current.Major -ge 1 -and $version.Major -eq 0){$current=[version]'0.0.0.0'}
 if($version -le $current){[Windows.Forms.MessageBox]::Show('이미 최신 버전입니다.','키패드 업데이트')|Out-Null}
 else {
  $zipAsset=$release.assets|Where-Object name -eq 'SimpleMacroPad-win-x64.zip'
  $hashAsset=$release.assets|Where-Object name -eq 'SHA256SUMS.txt'
  if(!$zipAsset -or !$hashAsset){throw '배포 파일을 찾지 못했습니다.'}
  $root=Join-Path $env:LOCALAPPDATA 'SimpleMacroPad\Releases'
  $stage=Join-Path $root ([guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Path $stage -Force|Out-Null
  $zip=Join-Path $stage 'release.zip'
  Invoke-WebRequest $zipAsset.browser_download_url -Headers $headers -OutFile $zip -UseBasicParsing
  $hash=(Invoke-WebRequest $hashAsset.browser_download_url -Headers $headers -UseBasicParsing).Content
  if($hash -notmatch '^([a-fA-F0-9]{64})\s+SimpleMacroPad-win-x64.zip\s*$'){throw '체크섬 형식 오류'}
  if((Get-FileHash $zip -Algorithm SHA256).Hash -ne $Matches[1]){throw '다운로드 검증 실패'}
  $dest=Join-Path $stage 'app'
  Expand-Archive -LiteralPath $zip -DestinationPath $dest
  $candidate=Join-Path $dest 'Keypad.exe'
  if(!(Test-Path $candidate) -or [version](Get-Item $candidate).VersionInfo.FileVersion -ne $version){throw '실행 파일 버전 불일치'}
  $settings=Join-Path (Split-Path $CurrentExe) 'update-settings.json'
  if(Test-Path $settings){
   Copy-Item $settings $dest
   $shortcut=(Get-Content $settings -Raw|ConvertFrom-Json).shortcut
   if($shortcut -and (Test-Path -LiteralPath $shortcut)){$w=New-Object -ComObject WScript.Shell;$s=$w.CreateShortcut($shortcut);$s.TargetPath=$candidate;$s.WorkingDirectory=$dest;$s.Save()}
  }
  $next=$candidate
 }
}catch{[Windows.Forms.MessageBox]::Show(('업데이트하지 못했습니다. 기존 버전을 다시 엽니다.'+[Environment]::NewLine+$_.Exception.Message),'키패드 업데이트')|Out-Null}
if($AppProcessId){Wait-Process -Id $AppProcessId -ErrorAction SilentlyContinue}
Start-Process -FilePath $next -WorkingDirectory (Split-Path $next)
