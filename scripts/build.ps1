param([string]$Dotnet = "dotnet", [switch]$SkipTests)
$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
& $Dotnet build (Join-Path $projectRoot 'src/EveryWifi/EveryWifi.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Application build failed." }
if (-not $SkipTests) {
    & $Dotnet build (Join-Path $projectRoot 'tests/EveryWifi.Tests.csproj') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Test build failed." }
    & (Join-Path $projectRoot 'tests/bin/Release/net48/EveryWifi.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw "Checks failed." }
}
$packageDirectory = Join-Path $projectRoot 'artifacts/EveryWifi-Windows'
New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null
Copy-Item -Path (Join-Path $projectRoot 'src/EveryWifi/bin/Release/net48/*') -Destination $packageDirectory -Recurse -Force
Get-ChildItem -LiteralPath $packageDirectory -Filter '*.pdb' | Remove-Item
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $packageDirectory 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $packageDirectory -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/UPSTREAM_MIT_LICENSE.txt') -Destination $packageDirectory -Force
$archive = Join-Path $projectRoot 'artifacts/EveryWifi-Windows.zip'
Compress-Archive -Path (Join-Path $packageDirectory '*') -DestinationPath $archive -Force
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $projectRoot 'artifacts/SHA256SUMS.txt'), "$hash  EveryWifi-Windows.zip`n", [Text.UTF8Encoding]::new($false))
New-Item -ItemType Directory -Force -Path (Join-Path $projectRoot 'site/dist/downloads') | Out-Null
Copy-Item -LiteralPath $archive -Destination (Join-Path $projectRoot 'site/dist/downloads/EveryWifi-Windows.zip') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'artifacts/SHA256SUMS.txt') -Destination (Join-Path $projectRoot 'site/dist/downloads/SHA256SUMS.txt') -Force
Write-Output "Package: $archive"
Write-Output "SHA256: $hash"
