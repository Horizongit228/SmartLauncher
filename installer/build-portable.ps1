[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'SmartLauncher.UI.csproj'

dotnet publish $projectFile -p:PublishProfile=portable-win-x64
if ($LASTEXITCODE -ne 0) {
    throw 'Portable publish failed.'
}

Write-Host 'Portable EXE is ready in dist\portable-win-x64.'
