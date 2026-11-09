<#
.SYNOPSIS
    Publishes Ciel as a portable, self-contained build and installs it locally.

.DESCRIPTION
    Release builds are NativeAOT and take a few minutes. The published files are written
    directly into the destination folder, so the terminal helpers already present there
    (conpty.dll, arm64\, x64\) are preserved.

    The running instance is only stopped after an explicit confirmation.

.EXAMPLE
    ./scripts/deploy.ps1

.EXAMPLE
    ./scripts/deploy.ps1 -Backup -Launch

.EXAMPLE
    ./scripts/deploy.ps1 -NoAot
#>
[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [string]$Destination,
    [switch]$Backup,
    [switch]$NoAot,
    [switch]$Launch
)

$ErrorActionPreference = 'Stop'

$onWindows = $env:OS -eq 'Windows_NT'
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = if ($onWindows) {
        Join-Path $env:LOCALAPPDATA 'Programs\ciel'
    } else {
        Join-Path $HOME '.local/share/ciel'
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src/SourceGit.csproj'
$version = (Get-Content (Join-Path $repoRoot 'VERSION') -Raw).Trim()
$commit = 'unknown'
try {
    $commit = (& git -C $repoRoot rev-parse --short HEAD 2>$null)
    if ($LASTEXITCODE -ne 0) { $commit = 'unknown' }
} catch {
    # git is optional here
}

if (-not (Test-Path $project)) {
    throw "Can not find '$project'. Run this script from a checkout of the repository."
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet was not found in PATH. Install the .NET 10 SDK first.'
}

Write-Host ''
Write-Host "Ciel local deployment" -ForegroundColor Cyan
Write-Host "  Version      : $version ($commit)"
Write-Host "  Runtime      : $Runtime"
Write-Host "  Destination  : $Destination"
Write-Host "  NativeAOT    : $(-not $NoAot)"
Write-Host ""

$executableName = if ($Runtime.StartsWith('win-')) { 'ciel.exe' } else { 'ciel' }
$exePath = Join-Path $Destination $executableName
$running = @(Get-Process -Name ciel -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and $_.Path.Equals($exePath, [StringComparison]::OrdinalIgnoreCase)
})

if ($running.Count -gt 0) {
    $answer = Read-Host "Ciel is running from '$Destination'. Stop it now? [y/N]"
    if ($answer -notmatch '^(y|yes)$') {
        Write-Host 'Deployment aborted, nothing was changed.' -ForegroundColor Yellow
        exit 1
    }

    $running | Stop-Process -Force
    Start-Sleep -Seconds 2
    Write-Host 'Stopped the running instance.' -ForegroundColor Yellow
}

if ($Backup -and (Test-Path $Destination)) {
    $backupDir = "$Destination.bak.$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Write-Host "Backing up to '$backupDir' ..."
    Copy-Item $Destination $backupDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $Destination | Out-Null

$arguments = @(
    'publish', $project,
    '-c', 'Release',
    '-r', $Runtime,
    '--self-contained', 'true',
    '-o', $Destination
)

if ($NoAot) {
    $arguments += '-p:DisableAOT=true'
}

Write-Host 'Publishing (this can take a few minutes with NativeAOT) ...' -ForegroundColor Cyan
& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Get-ChildItem $Destination -Filter '*.pdb' -File -ErrorAction SilentlyContinue | Remove-Item -Force

$deployed = Get-Item (Join-Path $Destination $executableName) -ErrorAction SilentlyContinue
if (-not $deployed) {
    throw "Deployment failed: '$Destination' does not contain $executableName."
}

Write-Host ''
Write-Host 'Deployment complete.' -ForegroundColor Green
Write-Host "  Executable : $($deployed.FullName)"
Write-Host "  Size       : $([math]::Round($deployed.Length / 1MB, 1)) MB"
Write-Host "  Built      : $($deployed.LastWriteTime)"
Write-Host ''

if ($Launch) {
    Write-Host 'Starting Ciel ...'
    Start-Process $deployed.FullName
}
