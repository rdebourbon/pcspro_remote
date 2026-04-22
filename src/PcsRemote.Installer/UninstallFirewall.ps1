# PCS Remote - Firewall Uninstall Custom Action
# Removes the PcsRemote-HTTP firewall rule.
# Uses hardcoded rule name - no registry dependency.

$ErrorActionPreference = 'Stop'

try
{
    Remove-NetFirewallRule -Name 'PcsRemote-HTTP' -ErrorAction SilentlyContinue
}
catch
{
    try
    {
        $logDir = if ($PSScriptRoot) { $PSScriptRoot } else { $env:TEMP }
        $logPath = Join-Path $logDir 'uninstall-firewall-ca.log'
        "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ERROR: $_" |
            Out-File $logPath -Append -Encoding utf8
    }
    catch
    {
        try { "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ERROR: $_" |
            Out-File (Join-Path $env:TEMP 'uninstall-firewall-ca.log') -Append } catch {}
    }
}

exit 0
