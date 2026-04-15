<#
.SYNOPSIS
    Builds the PCS Remote self-contained publish artefact.

.DESCRIPTION
    Runs `dotnet publish` for PcsRemote.TrayHost targeting win-x64 as a self-contained
    single-directory deployment. Output is placed in publish/ at the repository root
    (git-ignored). Run this script before executing Deploy-PcsRemote.ps1.

.NOTES
    Verified artefact set (approximate sizes, .NET 8 Release win-x64):
    ┌──────────────────────────────────────┬───────────┐
    │ File                                 │ Size      │
    ├──────────────────────────────────────┼───────────┤
    │ PcsRemote.TrayHost.exe               │   148 KB  │
    │ PcsRemote.Web.dll                    │    76 KB  │
    │ PcsRemote.Core.dll                   │    12 KB  │
    │ PcsRemote.Automation.dll             │    65 KB  │
    │ PcsRemote.Automation.Mock.dll        │    21 KB  │
    │ appsettings.json                     │    <1 KB  │
    │ appsettings.Development.json         │    <1 KB  │
    │ coreclr.dll  (CLR — bundled runtime) │ 4,878 KB  │
    │ clrjit.dll                           │ 1,737 KB  │
    │ System.Private.CoreLib.dll           │12,866 KB  │
    │ ... (409 files total)                │           │
    └──────────────────────────────────────┴───────────┘
    The presence of coreclr.dll confirms the CLR is bundled (self-contained).
    No .NET runtime installation is required on the target machine.
#>

#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot   = $PSScriptRoot | Split-Path -Parent
$outputDir  = Join-Path $repoRoot "publish"
$projectPath = Join-Path $repoRoot "src\PcsRemote.TrayHost\PcsRemote.TrayHost.csproj"

Write-Host "=== PCS Remote Publish ===" -ForegroundColor Cyan
Write-Host "Project : $projectPath"
Write-Host "Output  : $outputDir"
Write-Host ""

dotnet publish $projectPath -c Release -r win-x64 --self-contained -o $outputDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "=== Artefact verification ===" -ForegroundColor Cyan

$requiredFiles = @(
    "PcsRemote.TrayHost.exe",
    "coreclr.dll",
    "appsettings.json",
    "appsettings.Development.json"
)

$missing = @()
foreach ($file in $requiredFiles) {
    $path = Join-Path $outputDir $file
    if (Test-Path $path) {
        $size = [math]::Round((Get-Item $path).Length / 1KB, 0)
        Write-Host "  [OK] $file ($size KB)"
    }
    else {
        Write-Host "  [MISSING] $file" -ForegroundColor Red
        $missing += $file
    }
}

if ($missing.Count -gt 0) {
    Write-Error "Publish artefact is incomplete. Missing files: $($missing -join ', ')"
    exit 1
}

$totalFiles = (Get-ChildItem -File $outputDir -Recurse | Measure-Object).Count
Write-Host ""
Write-Host "Publish complete. $totalFiles files in $outputDir" -ForegroundColor Green
Write-Host "Run scripts\Deploy-PcsRemote.ps1 to deploy to the garage PC."
