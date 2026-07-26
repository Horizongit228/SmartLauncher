[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'SmartLauncher.UI.csproj'
$scriptFile = Join-Path $PSScriptRoot 'SmartLauncher.iss'
$compilerOutput = Join-Path $env:LOCALAPPDATA 'SmartLauncher\InstallerBuild'
$distributionOutput = Join-Path $projectRoot 'dist\installer'
$setupFileName = 'SmartLauncher-Setup-1.0.3.exe'

dotnet publish $projectFile -p:PublishProfile=installed-win-x64
if ($LASTEXITCODE -ne 0) {
    throw 'Installed publish failed.'
}

$compilerCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
)
$compiler = $compilerCandidates |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1

if (-not $compiler) {
    throw 'Inno Setup 6 was not found.'
}

New-Item -ItemType Directory -Path $compilerOutput -Force | Out-Null
& $compiler "/O$compilerOutput" $scriptFile
if ($LASTEXITCODE -ne 0) {
    throw 'Installer compilation failed.'
}

$compiledSetup = Join-Path $compilerOutput $setupFileName
if (-not (Test-Path -LiteralPath $compiledSetup)) {
    throw "Compiled installer was not found: $compiledSetup"
}

New-Item -ItemType Directory -Path $distributionOutput -Force | Out-Null
Copy-Item -LiteralPath $compiledSetup -Destination (Join-Path $distributionOutput $setupFileName) -Force

Write-Host 'Installer is ready in dist\installer.'
