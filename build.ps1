# Builds bin\ClaudeUsageWidget.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
#
# Needs the WebView2 SDK (NuGet package Microsoft.Web.WebView2) in .\lib:
#   lib\Microsoft.Web.WebView2.Core.dll   (from lib\net462\ in the package)
#   lib\WebView2Loader.dll                (from runtimes\win-x64\native\ in the package)
# Or pass -WebView2Dir with a folder that holds both files.

param([string]$WebView2Dir = (Join-Path $PSScriptRoot 'lib'))

$ErrorActionPreference = 'Stop'
$fw   = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc  = Join-Path $fw 'csc.exe'
$wpf  = Join-Path $fw 'WPF'
$core = Join-Path $WebView2Dir 'Microsoft.Web.WebView2.Core.dll'
$load = Join-Path $WebView2Dir 'WebView2Loader.dll'

foreach ($f in $csc, $core, $load) {
  if (-not (Test-Path $f)) { throw "Not found: $f (see the comment at the top of build.ps1)" }
}

$bin = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null
$out = Join-Path $bin 'ClaudeUsageWidget.exe'

& $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$out" `
  "/r:$wpf\PresentationFramework.dll" "/r:$wpf\PresentationCore.dll" "/r:$wpf\WindowsBase.dll" `
  "/r:$fw\System.Xaml.dll" "/r:$fw\System.Web.Extensions.dll" "/r:$fw\System.Drawing.dll" `
  "/r:$core" (Join-Path $PSScriptRoot 'app\ClaudeUsageWidget.cs')
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }

Copy-Item $core, $load $bin -Force
Write-Host "Built $out"
