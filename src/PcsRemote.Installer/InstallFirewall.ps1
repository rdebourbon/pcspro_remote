# PCS Remote - Firewall Install Custom Action
# Creates the PcsRemote-HTTP inbound TCP firewall rule for the configured port.
# Reads port from registry transport (HKLM:\SOFTWARE\PcsRemote\Install).

$ErrorActionPreference = 'Stop'

try
{
    $regPath = 'HKLM:\SOFTWARE\PcsRemote\Install'
    $reg = Get-ItemProperty -Path $regPath

    $installDir = $reg.InstallFolder
    $port = $reg.Port

    Remove-NetFirewallRule -Name 'PcsRemote-HTTP' -ErrorAction SilentlyContinue
    New-NetFirewallRule `
        -Name 'PcsRemote-HTTP' `
        -DisplayName 'PCS Remote HTTP' `
        -Direction Inbound `
        -Protocol TCP `
        -LocalPort $port `
        -Profile Any `
        -Action Allow | Out-Null
}
catch
{
    try
    {
        $logDir = if ($installDir) { $installDir } else { $env:TEMP }
        $logPath = Join-Path $logDir 'install-firewall-ca.log'
        "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ERROR: $_" |
            Out-File $logPath -Append -Encoding utf8
    }
    catch
    {
        try { "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ERROR: $_" |
            Out-File (Join-Path $env:TEMP 'install-firewall-ca.log') -Append } catch {}
    }
}

exit 0
