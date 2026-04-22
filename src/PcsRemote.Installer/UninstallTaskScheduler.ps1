# PCS Remote — Task Scheduler Uninstall Custom Action
# Stops and removes the PcsRemote scheduled task.
# Uses hardcoded task name — no registry dependency.

$ErrorActionPreference = 'Stop'

try
{
    Stop-ScheduledTask -TaskName 'PcsRemote' -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName 'PcsRemote' -Confirm:$false -ErrorAction SilentlyContinue
}
catch
{
    try
    {
        $logDir = if ($PSScriptRoot) { $PSScriptRoot } else { $env:TEMP }
        $logPath = Join-Path $logDir 'uninstall-taskscheduler-ca.log'
        "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ERROR: $_" |
            Out-File $logPath -Append -Encoding utf8
    }
    catch
    {
        try { "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ERROR: $_" |
            Out-File (Join-Path $env:TEMP 'uninstall-taskscheduler-ca.log') -Append } catch {}
    }
}

exit 0
