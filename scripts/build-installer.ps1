<#
.SYNOPSIS
    Builds the PCS Remote MSI installer.

.DESCRIPTION
    Orchestrates the full build pipeline:
    1. Invokes scripts/publish.ps1 to produce the self-contained TrayHost artefact
    2. Builds the WiX installer project in Release configuration
    3. Copies the MSI to artifacts/PcsRemote-Setup.msi

.PARAMETER Version
    Optional MSI product version (e.g., 1.2.0.0). When omitted, the .wixproj
    <Version> default (1.0.0.0) is used. MSI ProductVersion effectively uses
    only the first three fields; the fourth (revision) is silently ignored by
    the Windows Installer engine for upgrade comparison.

.EXAMPLE
    .\build-installer.ps1
    .\build-installer.ps1 -Version 1.2.0.0
#>

[CmdletBinding()]
param(
    [string]$Version
)

#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot       = $PSScriptRoot | Split-Path -Parent
$installerProj  = Join-Path $repoRoot "src\PcsRemote.Installer\PcsRemote.Installer.wixproj"
$artifactsDir   = Join-Path $repoRoot "artifacts"
$publishScript  = Join-Path $PSScriptRoot "publish.ps1"

# --- Step 1: Publish TrayHost ---
Write-Host "=== Step 1: Publish TrayHost ===" -ForegroundColor Cyan
& $publishScript

$publishOutput = Join-Path $repoRoot "publish\PcsRemote.TrayHost.exe"
if (-not (Test-Path $publishOutput))
{
    Write-Error "publish.ps1 did not produce expected output at $publishOutput."
    exit 1
}

# --- Step 2: Build MSI ---
Write-Host ""
Write-Host "=== Step 2: Build MSI Installer ===" -ForegroundColor Cyan

$buildArgs = @($installerProj, "-c", "Release", "--no-incremental")

if ($Version)
{
    if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$')
    {
        Write-Error "Invalid version format '$Version'. Expected major.minor.build[.revision] (e.g., 1.2.0.0)."
        exit 1
    }

    $parts = $Version.Split('.')
    if ($parts.Count -ge 4 -and [int]$parts[3] -ne 0)
    {
        Write-Warning "MSI ProductVersion ignores the revision field ($($parts[3])). Only the first three fields (major.minor.build) affect upgrade detection."
    }

    $buildArgs += "-p:Version=$Version"
    Write-Host "Version : $Version"
}
else
{
    Write-Host "Version : (default from .wixproj)"
}

Write-Host "Project : $installerProj"
Write-Host ""

dotnet build @buildArgs
if ($LASTEXITCODE -ne 0)
{
    Write-Error "WiX build failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

# --- Step 3: Copy MSI to artifacts/ ---
Write-Host ""
Write-Host "=== Step 3: Copy MSI to artifacts/ ===" -ForegroundColor Cyan

$msiSource = Join-Path $repoRoot "src\PcsRemote.Installer\bin\Release\PcsRemote.Installer.msi"
if (-not (Test-Path $msiSource))
{
    Write-Error "MSI not found at expected path: $msiSource"
    exit 1
}

if (-not (Test-Path $artifactsDir))
{
    New-Item -ItemType Directory -Path $artifactsDir | Out-Null
}

$msiDest = Join-Path $artifactsDir "PcsRemote-Setup.msi"
Copy-Item -Path $msiSource -Destination $msiDest -Force

# --- Step 4: Verification ---
Write-Host ""
Write-Host "=== Verification ===" -ForegroundColor Cyan

if (Test-Path $msiDest)
{
    $size = [math]::Round((Get-Item $msiDest).Length / 1KB, 0)
    Write-Host "  [OK] PcsRemote-Setup.msi ($size KB)" -ForegroundColor Green
}
else
{
    Write-Error "MSI copy failed - file not found at $msiDest"
    exit 1
}

Write-Host ""
Write-Host "Build complete. MSI at: $msiDest" -ForegroundColor Green
