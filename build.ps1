$ErrorActionPreference='Stop'
$packages=Join-Path $PSScriptRoot '.packages'
function Restore-Package($id,$version){
 $folder=Join-Path $packages "$id.$version"
 if(!(Test-Path $folder)){
  New-Item -ItemType Directory -Path $packages -Force|Out-Null
  $zip=Join-Path $packages "$id.$version.zip"
  [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
  Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/$id/$version/$id.$version.nupkg" -OutFile $zip -UseBasicParsing
  Expand-Archive -LiteralPath $zip -DestinationPath $folder
 }
 return $folder
}
$sdk=Restore-Package 'microsoft.web.webview2' '1.0.2903.40'
$hid=Restore-Package 'hidlibrary' '3.3.40'
$out=Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $out -Force | Out-Null
Copy-Item "$sdk\lib\net462\Microsoft.Web.WebView2.Core.dll","$sdk\lib\net462\Microsoft.Web.WebView2.WinForms.dll","$sdk\runtimes\win-x64\native\WebView2Loader.dll" $out
$html=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'ui.html'))
$html=$html.Replace("if(!capturing)return;e.preventDefault();", "if(window.chrome&&window.chrome.webview)return;if(!capturing)return;e.preventDefault();")
$bridge=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'desktop.js'))
$bridge+=[Environment]::NewLine+[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'hardware-codec.js'))
$bridge+=[Environment]::NewLine+[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'hardware.js'))
$bridge+=[Environment]::NewLine+[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'easy.js'))
$bridge+=[Environment]::NewLine+[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'direct.js'))
$html=$html.Replace('</body>',('<script>'+$bridge+'</script></body>'))
[IO.File]::WriteAllText((Join-Path $out 'index.html'),$html,[Text.Encoding]::UTF8)
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
Copy-Item (Get-ChildItem "$hid\lib" -Recurse -Filter HidLibrary.dll|Select-Object -First 1).FullName $out
Copy-Item "$PSScriptRoot\Update.ps1" $out
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /out:"$out\Keypad.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Xml.Linq.dll /reference:"$out\HidLibrary.dll" /reference:"$out\Microsoft.Web.WebView2.Core.dll" /reference:"$out\Microsoft.Web.WebView2.WinForms.dll" "$PSScriptRoot\Keypad.cs" "$PSScriptRoot\Hardware.cs"
if($LASTEXITCODE -ne 0){throw 'Build failed'}
