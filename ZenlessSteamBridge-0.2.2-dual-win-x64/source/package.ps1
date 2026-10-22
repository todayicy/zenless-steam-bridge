$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'test.ps1')
$versionSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\Models.cs') -Raw
if ($versionSource -notmatch 'const string Version = "([0-9.]+)"') { throw 'Version missing.' }
$version = $Matches[1]
$stage = Join-Path ([IO.Path]::GetTempPath()) ('ZenlessBridge-package-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'dist\ZenlessSteamBridge.exe') -Destination $stage
    foreach ($name in @('README.md','LICENSE','使用说明.txt','VALIDATION.txt')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $stage }
    $source = Join-Path $stage 'source'
    New-Item -ItemType Directory -Path $source | Out-Null
    foreach ($name in @('src','tests','assets','app.manifest','bridge.manifest','build.ps1','build-mono.py','test.ps1','package.ps1','README.md','LICENSE','使用说明.txt','CHANGELOG.md','VALIDATION.txt','.gitignore','ZenlessSteamBridge.csproj')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $source -Recurse
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = Join-Path $PSScriptRoot "dist\ZenlessSteamBridge-$version-dual-win-x64.zip"
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
    Get-FileHash -LiteralPath $zip -Algorithm SHA256
}
finally { Remove-Item -LiteralPath $stage -Recurse -Force }
