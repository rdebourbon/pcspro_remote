# PCS Remote — Environment Variable Install Custom Action
# Sets the PcsPro__Password system environment variable from the
# wizard-entered password, read from registry transport.
# The password value is NEVER logged, echoed, or exposed.

$ErrorActionPreference = 'Stop'

try
{
    $regPath = 'HKLM:\SOFTWARE\PcsRemote\Install'
    $reg = Get-ItemProperty -Path $regPath

    $installDir = $reg.InstallFolder
    $password = $reg.Password

    if ([string]::IsNullOrEmpty($password))
    {
        $logDir = if ($installDir) { $installDir } else { $env:TEMP }
        $logPath = Join-Path $logDir 'install-envvar-ca.log'
        "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') INFO: Password is empty — skipping env var write to preserve existing value." |
            Out-File $logPath -Append -Encoding utf8
    }
    else
    {
        [System.Environment]::SetEnvironmentVariable('PcsPro__Password', $password, 'Machine')
    }
}
catch
{
    # Do NOT include exception details that might contain the password value
    try
    {
        $logDir = if ($installDir) { $installDir } else { $env:TEMP }
        $logPath = Join-Path $logDir 'install-envvar-ca.log'
        "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ERROR: Environment variable CA failed." |
            Out-File $logPath -Append -Encoding utf8
    }
    catch
    {
        try { "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ERROR: Environment variable CA failed." |
            Out-File (Join-Path $env:TEMP 'install-envvar-ca.log') -Append } catch {}
    }
}
finally
{
    $password = $null
    $reg = $null
}

exit 0
