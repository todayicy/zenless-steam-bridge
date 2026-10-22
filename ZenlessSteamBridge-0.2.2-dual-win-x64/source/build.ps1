$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Requires Windows x64 with .NET Framework 4.8.' }
$output = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$common = @('/nologo','/target:winexe','/platform:x64','/optimize+','/utf8output','/codepage:65001', '/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll','/reference:System.Management.dll', "/resource:$PSScriptRoot\LICENSE,ZenlessSteamBridge.License", "/resource:$PSScriptRoot\assets\zenless.png,ZenlessSteamBridge.ZenlessArt", "/resource:$PSScriptRoot\assets\starrail.png,ZenlessSteamBridge.StarRailArt")
# Steam starts this small asInvoker entry point; only the scheduled helper elevates.
& $compiler @common /define:BRIDGE_FRONTEND "/win32manifest:$PSScriptRoot\bridge.manifest" "/out:$output\ZenlessBridge.exe" @sources
if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
# Ship one manager EXE; extract its embedded frontend when enabling the bridge.
& $compiler @common  "/win32manifest:$PSScriptRoot\app.manifest" "/resource:$output\ZenlessBridge.exe,ZenlessSteamBridge.Frontend" "/out:$output\ZenlessSteamBridge.exe" @sources
if ($LASTEXITCODE -ne 0) { throw 'Manager build failed.' }
Write-Output "Built $output\ZenlessSteamBridge.exe"