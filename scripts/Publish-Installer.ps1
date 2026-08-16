[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repositoryRoot 'src\EnglishWordbook\EnglishWordbook.csproj'
$installerProject = Join-Path $repositoryRoot 'src\EnglishWordbookInstaller\EnglishWordbookInstaller.csproj'
$payloadPath = Join-Path $repositoryRoot 'src\EnglishWordbookInstaller\payload\EnglishWordbook.exe'
$appPublishDirectory = Join-Path $repositoryRoot 'artifacts\EnglishWordbook-win-x64'
$installerPublishDirectory = Join-Path $repositoryRoot 'artifacts\EnglishWordbookInstaller-win-x64'
$distributionDirectory = Join-Path $repositoryRoot 'dist'

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $payloadPath), $appPublishDirectory, $installerPublishDirectory, $distributionDirectory | Out-Null

$publishOptions = @(
    '-c', $Configuration,
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true'
)

dotnet publish $appProject @publishOptions '-o' $appPublishDirectory
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }

Copy-Item -LiteralPath (Join-Path $appPublishDirectory 'EnglishWordbook.exe') -Destination $payloadPath -Force

dotnet publish $installerProject @publishOptions '-o' $installerPublishDirectory
if ($LASTEXITCODE -ne 0) { throw 'Installer publish failed.' }

Copy-Item -LiteralPath (Join-Path $installerPublishDirectory 'EnglishWordbookInstaller.exe') -Destination (Join-Path $distributionDirectory 'EnglishWordbookInstaller.exe') -Force
Write-Host "Installer created: $(Join-Path $distributionDirectory 'EnglishWordbookInstaller.exe')"
