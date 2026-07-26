[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'SmartLauncher.UI.csproj'
$publishOutput = Join-Path $env:LOCALAPPDATA 'SmartLauncher\Publish\portable-win-x64\SmartLauncher.exe'
$distributionDirectory = Join-Path $projectRoot 'dist\portable-win-x64'
$distributionOutput = Join-Path $distributionDirectory 'SmartLauncher.exe'

dotnet publish $projectFile -p:PublishProfile=portable-win-x64
if ($LASTEXITCODE -ne 0) {
    throw 'Portable publish failed.'
}

if (-not (Test-Path -LiteralPath $publishOutput)) {
    throw "Portable executable was not found: $publishOutput"
}

New-Item -ItemType Directory -Path $distributionDirectory -Force |
    Out-Null
Copy-Item -LiteralPath $publishOutput -Destination $distributionOutput -Force

Write-Host 'Portable EXE is ready in dist\portable-win-x64.'
