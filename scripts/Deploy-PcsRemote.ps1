<#
.SYNOPSIS
    Deploys PCS Remote to the garage PC.

.DESCRIPTION
    Idempotent deployment script: installs or updates PCS Remote in the target
    directory, registers the Task Scheduler auto-start task, opens the Windows
    Firewall rule, and optionally sets the PCS Pro password in the System
    environment. Safe to re-run on every deployment.

.PARAMETER DeployDir
    Target installation directory. Default: C:\PcsRemote\

.PARAMETER AppUser
    Windows account name used for the Task Scheduler ONLOGON trigger.
    Prompted interactively if not supplied.

.PARAMETER Port
    TCP port for the Windows Firewall inbound rule only.
    Default: 5000.
    Note: changing the Kestrel bind port also requires editing
    Kestrel:Endpoints:Http:Url in appsettings.json before running this script.

.PARAMETER SkipPasswordUpdate
    If specified, skip the PCS Pro password prompt.
    Use for code-only re-deployments where the password has not changed.
    A warning is printed if the password is not already set in the System environment.

.EXAMPLE
    # First-time deployment
    .\Deploy-PcsRemote.ps1 -AppUser "GARAGE\scorer"

.EXAMPLE
    # Code-only re-deployment (password unchanged)
    .\Deploy-PcsRemote.ps1 -AppUser "GARAGE\scorer" -SkipPasswordUpdate

.EXAMPLE
    # Non-standard port
    .\Deploy-PcsRemote.ps1 -AppUser "GARAGE\scorer" -Port 8080
#>

#Requires -RunAsAdministrator
#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $DeployDir = 'C:\PcsRemote\',
    [string] $AppUser   = '',
    [int]    $Port      = 5000,
    [switch] $SkipPasswordUpdate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ──────────────────────────────────────────────────────────────────────────────
# Constants
# ──────────────────────────────────────────────────────────────────────────────
$TaskName         = 'PcsRemote'
$FirewallRuleName = 'PcsRemote-HTTP'
$ExeName          = 'PcsRemote.TrayHost.exe'
$EnvVarName       = 'PcsPro__Password'
$SourceDir        = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($PSScriptRoot, '..', 'publish'))

# Track created .new files for the post-deploy warning
$newConfigFiles = [System.Collections.Generic.List[string]]::new()
# Track all actions for the summary
$actions = [System.Collections.Generic.List[string]]::new()

function Write-Step([string]$message) {
    Write-Host ""
    Write-Host ">>> $message" -ForegroundColor Cyan
}

function Record([string]$action) {
    $actions.Add($action)
    Write-Host "    $action"
}

# ──────────────────────────────────────────────────────────────────────────────
# Step 0: Prompt for AppUser if not supplied
# ──────────────────────────────────────────────────────────────────────────────
if ([string]::IsNullOrWhiteSpace($AppUser)) {
    $AppUser = Read-Host "Enter the Windows account name for the Task Scheduler trigger (e.g. GARAGE\scorer)"
    if ([string]::IsNullOrWhiteSpace($AppUser)) {
        Write-Error "AppUser is required. Aborting."
        exit 1
    }
}

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║          PCS Remote Deployment Script                        ║" -ForegroundColor Green
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""
Write-Host "  Deploy dir : $DeployDir"
Write-Host "  App user   : $AppUser"
Write-Host "  Port       : $Port"
Write-Host "  Source     : $SourceDir"
Write-Host ""

# ──────────────────────────────────────────────────────────────────────────────
# Step 1: Pre-flight — verify artefact source exists
# ──────────────────────────────────────────────────────────────────────────────
Write-Step "Pre-flight checks"

$sourceExe = Join-Path $SourceDir $ExeName
if (-not (Test-Path $sourceExe)) {
    Write-Error "Deployment artefact not found: $sourceExe`nRun scripts\publish.ps1 first to produce the publish output."
    exit 1
}
Record "Artefact source verified: $SourceDir"

# ──────────────────────────────────────────────────────────────────────────────
# Step 2: Stop running instance
# ──────────────────────────────────────────────────────────────────────────────
Write-Step "Stopping running instance (if any)"

Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue

$stopDeadline = (Get-Date).AddSeconds(10)
$process = $null
do {
    $process = Get-Process -Name ([System.IO.Path]::GetFileNameWithoutExtension($ExeName)) -ErrorAction SilentlyContinue
    if ($process) { Start-Sleep -Milliseconds 500 }
} while ($process -and (Get-Date) -lt $stopDeadline)

if ($process) {
    Write-Host "    Graceful stop timed out — force-killing PID $($process.Id)..." -ForegroundColor Yellow
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 5
    $process = Get-Process -Name ([System.IO.Path]::GetFileNameWithoutExtension($ExeName)) -ErrorAction SilentlyContinue
    if ($process) {
        Write-Error "Cannot stop $ExeName (PID $($process.Id)). Close it manually and re-run the script."
        exit 1
    }
    Record "Process force-killed"
}
else {
    Record "Process not running (or stopped cleanly)"
}

# ──────────────────────────────────────────────────────────────────────────────
# Step 3: Ensure deploy directory exists
# ──────────────────────────────────────────────────────────────────────────────
if (-not (Test-Path $DeployDir)) {
    New-Item -ItemType Directory -Path $DeployDir -Force | Out-Null
    Record "Created deploy directory: $DeployDir"
}

# ──────────────────────────────────────────────────────────────────────────────
# Step 4: Copy artefacts (with config-merge protection)
# ──────────────────────────────────────────────────────────────────────────────
Write-Step "Copying artefacts to $DeployDir"

$files = Get-ChildItem -File $SourceDir -Recurse
foreach ($file in $files) {
    $relativePath = $file.FullName.Substring($SourceDir.Length).TrimStart('\', '/')
    $destination  = Join-Path $DeployDir $relativePath

    # Ensure sub-directory exists
    $destDir = Split-Path $destination -Parent
    if (-not (Test-Path $destDir)) {
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    }

    # appsettings.Development.json: always silently overwrite (never loaded in production)
    if ($file.Name -eq 'appsettings.Development.json') {
        Copy-Item $file.FullName $destination -Force
        continue
    }

    # Other appsettings*.json: content-hash comparison on re-deployment
    if ($file.Name -like 'appsettings*.json') {
        if (Test-Path $destination) {
            $existingHash = (Get-FileHash $destination -Algorithm SHA256).Hash
            $incomingHash = (Get-FileHash $file.FullName -Algorithm SHA256).Hash
            if ($existingHash -ne $incomingHash) {
                # Different content — place alongside as .new for operator review
                $newPath = "$destination.new"
                Copy-Item $file.FullName $newPath -Force
                $newConfigFiles.Add($newPath)
                Record "Config changed — placed alongside: $newPath"
            }
            else {
                # Identical — silently overwrite (no .new file, auto-start not suppressed)
                Copy-Item $file.FullName $destination -Force
            }
            continue
        }
    }

    Copy-Item $file.FullName $destination -Force
}

Record "Artefact copy complete ($($files.Count) files)"

if ($newConfigFiles.Count -gt 0) {
    Write-Host ""
    Write-Host "  ⚠  Configuration files have changed. Review and merge before starting:" -ForegroundColor Yellow
    foreach ($f in $newConfigFiles) { Write-Host "       $f" -ForegroundColor Yellow }
}

# ──────────────────────────────────────────────────────────────────────────────
# Step 5: Task Scheduler (idempotent — atomic replace via -Force)
# ──────────────────────────────────────────────────────────────────────────────
Write-Step "Registering Task Scheduler task '$TaskName'"

$deployExe = Join-Path $DeployDir $ExeName

$action  = New-ScheduledTaskAction -Execute $deployExe -WorkingDirectory $DeployDir
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $AppUser
$settings = New-ScheduledTaskSettingsSet `
    -ExecutionTimeLimit (New-TimeSpan -Seconds 0) `
    -MultipleInstances  IgnoreNew `
    -RestartCount       999 `
    -RestartInterval    (New-TimeSpan -Seconds 30)
$principal = New-ScheduledTaskPrincipal -UserId $AppUser -LogonType Interactive -RunLevel Limited

# Register-ScheduledTask -Force atomically replaces any existing task named $TaskName.
# Do NOT unregister first: if re-registration fails after deletion, the system is left
# with no auto-start mechanism. -Force is the safe atomic alternative.
Register-ScheduledTask `
    -TaskName  $TaskName `
    -Action    $action `
    -Trigger   $trigger `
    -Settings  $settings `
    -Principal $principal `
    -Force | Out-Null

Record "Task Scheduler task registered (ONLOGON user=$AppUser, restart 30s x999)"

# ──────────────────────────────────────────────────────────────────────────────
# Step 6: Windows Firewall rule
# ──────────────────────────────────────────────────────────────────────────────
Write-Step "Configuring Windows Firewall rule '$FirewallRuleName' (port $Port)"

# Remove-then-recreate is accepted for the firewall rule because New-NetFirewallRule
# has no -Force atomic-replace option. A missing rule is recoverable by re-running
# this script; no application state is lost. This asymmetry is intentional.
Remove-NetFirewallRule -Name $FirewallRuleName -ErrorAction SilentlyContinue
New-NetFirewallRule `
    -Name        $FirewallRuleName `
    -DisplayName 'PCS Remote HTTP' `
    -Direction   Inbound `
    -Protocol    TCP `
    -LocalPort   $Port `
    -Action      Allow | Out-Null

Record "Firewall rule '$FirewallRuleName' created (inbound TCP port $Port)"

# ──────────────────────────────────────────────────────────────────────────────
# Step 7: PCS Pro password
# ──────────────────────────────────────────────────────────────────────────────
Write-Step "PCS Pro password"

if ($SkipPasswordUpdate) {
    $existingPwd = [System.Environment]::GetEnvironmentVariable($EnvVarName, 'Machine')
    if ([string]::IsNullOrEmpty($existingPwd)) {
        Write-Host ""
        Write-Host "  ⚠  -SkipPasswordUpdate specified but $EnvVarName is not set." -ForegroundColor Yellow
        Write-Host "     If real mode is enabled (PcsPro:UseMock=false), PCS Pro login will fail." -ForegroundColor Yellow
        Write-Host "     Set the password manually or re-run without -SkipPasswordUpdate." -ForegroundColor Yellow
        Record "Password skipped — WARNING: not set in System environment"
    }
    else {
        Record "Password update skipped (-SkipPasswordUpdate)"
    }
}
else {
    $existingPwd = [System.Environment]::GetEnvironmentVariable($EnvVarName, 'Machine')
    $setPassword = $true

    if (-not [string]::IsNullOrEmpty($existingPwd)) {
        $answer = Read-Host "Password is already configured. Update it? [y/N]"
        $setPassword = ($answer -eq 'y' -or $answer -eq 'Y')
    }

    if ($setPassword) {
        $securePwd = Read-Host -AsSecureString "Enter PCS Pro password"
        $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePwd)
        try {
            $plain = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
            [System.Environment]::SetEnvironmentVariable($EnvVarName, $plain, 'Machine')
            $plain = $null
        }
        finally {
            [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
        Record "Password set in System environment ($EnvVarName)"
    }
    else {
        Record "Password update declined by operator"
    }
}

# ──────────────────────────────────────────────────────────────────────────────
# Step 8: Restart (conditional on .new config files)
# ──────────────────────────────────────────────────────────────────────────────
Write-Step "Post-deployment start"

if ($newConfigFiles.Count -eq 0) {
    Start-ScheduledTask -TaskName $TaskName
    Record "Task Scheduler task started"
}
else {
    Write-Host ""
    Write-Host "  ⚠  Configuration merge required. The application has NOT been started." -ForegroundColor Yellow
    Write-Host "     Review and merge the following .new files into the existing configuration:" -ForegroundColor Yellow
    foreach ($f in $newConfigFiles) { Write-Host "       $f" -ForegroundColor Yellow }
    Write-Host ""
    Write-Host "     When done, run from an elevated PowerShell prompt:" -ForegroundColor Yellow
    Write-Host "       Start-ScheduledTask -TaskName $TaskName" -ForegroundColor Yellow
    Record "Auto-start suppressed — .new config files require operator review"
}

# ──────────────────────────────────────────────────────────────────────────────
# Step 9: Deployment summary
# ──────────────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║          Deployment Summary                                  ║" -ForegroundColor Green
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""
foreach ($action in $actions) {
    Write-Host "  ✓ $action" -ForegroundColor Green
}
Write-Host ""
if ($newConfigFiles.Count -eq 0) {
    Write-Host "  Deployment complete. PCS Remote is running." -ForegroundColor Green
}
else {
    Write-Host "  Deployment complete. Manual configuration merge required before starting." -ForegroundColor Yellow
}
