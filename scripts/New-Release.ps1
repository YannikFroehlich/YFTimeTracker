[CmdletBinding()]
param(
    [string]$Version = '0.1.0',
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') {
    throw "Version '$Version' is not a valid semantic version."
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solutionPath = Join-Path $repositoryRoot 'YFTimeTracker.slnx'
$projectPath = Join-Path $repositoryRoot 'YFTimeTracker.App\YFTimeTracker.App.csproj'
$nugetConfigPath = Join-Path $repositoryRoot 'NuGet.config'
$artifactRoot = Join-Path $repositoryRoot 'artifacts\release'
$releaseName = "YFTimeTracker-v$Version-$Runtime"
$releaseDirectory = Join-Path $artifactRoot $releaseName
$zipPath = Join-Path $artifactRoot "$releaseName.zip"
$checksumPath = "$zipPath.sha256"

function Invoke-DotNet {
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Remove-ReleaseItem {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $allowedPrefix = [System.IO.Path]::GetFullPath($artifactRoot) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside the release artifact directory: $fullPath"
    }

    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

Write-Host "Generating YFTimeTracker $Version release assets..."
& (Join-Path $PSScriptRoot 'Generate-AppAssets.ps1')

Write-Host 'Restoring dependencies...'
Invoke-DotNet -Arguments @(
    'restore',
    $solutionPath,
    '--configfile',
    $nugetConfigPath)

if (-not $SkipTests) {
    Write-Host 'Building and running tests...'
    Invoke-DotNet -Arguments @(
        'test',
        $solutionPath,
        '--configuration',
        'Release',
        '--no-restore')
}

New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
Remove-ReleaseItem -Path $releaseDirectory
Remove-ReleaseItem -Path $zipPath
Remove-ReleaseItem -Path $checksumPath

Write-Host 'Publishing self-contained Windows build...'
Invoke-DotNet -Arguments @(
    'publish',
    $projectPath,
    '--configuration',
    'Release',
    '--runtime',
    $Runtime,
    '--no-restore',
    '--self-contained',
    'true',
    "-p:Version=$Version",
    '-p:WindowsPackageType=None',
    '-p:DebugSymbols=false',
    '-p:DebugType=None',
    '--output',
    $releaseDirectory)

Get-ChildItem -LiteralPath $releaseDirectory -Filter '*.pdb' -File -Recurse |
    Remove-Item -Force

Copy-Item `
    -LiteralPath (Join-Path $repositoryRoot 'packaging\PORTABLE-README.txt') `
    -Destination (Join-Path $releaseDirectory 'LIESMICH.txt')

Write-Host 'Creating ZIP archive...'
Compress-Archive -Path (Join-Path $releaseDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([System.IO.Path]::GetFileName($zipPath))" |
    Set-Content -LiteralPath $checksumPath -Encoding ascii

$zipFile = Get-Item -LiteralPath $zipPath
Write-Host ''
Write-Host 'Release created successfully:'
Write-Host "  ZIP:      $($zipFile.FullName)"
Write-Host "  Size:     $([Math]::Round($zipFile.Length / 1MB, 1)) MB"
Write-Host "  SHA-256:  $hash"
