# PCS Remote - Task Scheduler Install Custom Action
# Creates the PcsRemote scheduled task for auto-start on user logon.
# Reads install directory and logon user from registry transport
# (HKLM:\SOFTWARE\PcsRemote\Install, written by WriteRegistryValues).

$ErrorActionPreference = 'Stop'

try
{
    $regPath = 'HKLM:\SOFTWARE\PcsRemote\Install'
    $reg = Get-ItemProperty -Path $regPath

    $installDir = $reg.InstallFolder
    $logonUser = $reg.LogonUser

    $exePath = Join-Path $installDir 'PcsRemote.TrayHost.exe'

    $action = New-ScheduledTaskAction -Execute $exePath -WorkingDirectory $installDir
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $logonUser
    $settings = New-ScheduledTaskSettingsSet `
        -ExecutionTimeLimit (New-TimeSpan -Seconds 0) `
        -MultipleInstances IgnoreNew `
        -RestartCount 999 `
        -RestartInterval (New-TimeSpan -Minutes 1)
    $principal = New-ScheduledTaskPrincipal -UserId $logonUser -LogonType Interactive -RunLevel Limited

    Register-ScheduledTask `
        -TaskName 'PcsRemote' `
        -Action $action `
        -Trigger $trigger `
        -Settings $settings `
        -Principal $principal `
        -Force | Out-Null
}
catch
{
    try
    {
        $logDir = if ($installDir) { $installDir } else { $env:TEMP }
        $logPath = Join-Path $logDir 'install-taskscheduler-ca.log'
        "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ERROR: $_" |
            Out-File $logPath -Append -Encoding utf8
    }
    catch
    {
        try { "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ERROR: $_" |
            Out-File (Join-Path $env:TEMP 'install-taskscheduler-ca.log') -Append } catch {}
    }
}

exit 0
