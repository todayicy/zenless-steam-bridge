$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1')
& (Join-Path $PSScriptRoot 'tests\payload.test.ps1') -Manager (Join-Path $PSScriptRoot 'dist\ZenlessSteamBridge.exe') -Frontend (Join-Path $PSScriptRoot 'dist\ZenlessBridge.exe')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$testSources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.cs' | ForEach-Object FullName)
$testExe = Join-Path $PSScriptRoot 'dist\CoreTests.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /utf8output /codepage:65001 /main:ZenlessSteamBridge.CoreTests /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Management.dll "/out:$testExe" @sources @testSources
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
foreach ($file in @('ZenlessSteamBridge.exe','ZenlessBridge.exe')) {
    & $testExe --api-compat (Join-Path $PSScriptRoot ('dist\' + $file))
    if ($LASTEXITCODE -ne 0) { throw 'Framework API compatibility regression.' }
}
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }

& $testExe --payload (Join-Path $PSScriptRoot 'dist\ZenlessSteamBridge.exe') (Join-Path $PSScriptRoot 'dist\ZenlessBridge.exe')
if ($LASTEXITCODE -ne 0) { throw 'Embedded artwork tests failed.' }
& $testExe --ui (Join-Path $PSScriptRoot 'dist\ZenlessSteamBridge.exe')
if ($LASTEXITCODE -ne 0) { throw 'Game selection UI tests failed.' }
