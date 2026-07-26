[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallerUrl,

    [string]$Version = '1.0.0',

    [string]$ReleaseNotes = 'Smart Launcher 1.0 - First release.'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$installerPath = Join-Path $projectRoot "dist\installer\SmartLauncher-Setup-$Version.exe"
$outputPath = Join-Path $projectRoot 'dist\installer\update-manifest.json'

if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer was not found: $installerPath"
}

$manifest = [ordered]@{
    version = $Version
    installerUrl = $InstallerUrl
    sha256 = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
    releaseNotes = $ReleaseNotes
    publishedAtUtc = [DateTime]::UtcNow.ToString('o')
}

$manifest |
    ConvertTo-Json |
    Set-Content -LiteralPath $outputPath -Encoding utf8

Write-Host "Manifest is ready: $outputPath"
