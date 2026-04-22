# PCS Remote - Configuration Write Custom Action
# Modifies appsettings.json with wizard-collected values on fresh install.
# Reads values from registry (HKLM:\SOFTWARE\PcsRemote\Install) to avoid
# command-line quoting/injection issues with special characters.
# Called by the MSI deferred CA after WriteRegistryValues.

$ErrorActionPreference = 'Stop'

try
{
    $regPath = 'HKLM:\SOFTWARE\PcsRemote\Install'
    $reg = Get-ItemProperty -Path $regPath

    $installDir = $reg.InstallFolder
    $exePath = $reg.ExePath
    $port = $reg.Port
    $clientId = $reg.ClientId
    $clientSecret = $reg.ClientSecret
    $liveStreamId = $reg.LiveStreamId

    $configPath = Join-Path $installDir 'appsettings.json'
    $cfg = Get-Content $configPath -Raw | ConvertFrom-Json

    # Kestrel port
    $cfg.Kestrel.Endpoints.Http.Url = "http://0.0.0.0:$port"

    # PCS Pro settings
    $cfg.PcsPro.ExecutablePath = $exePath
    $cfg.PcsPro.WorkingDirectory = if ($exePath) { Split-Path $exePath -Parent } else { '' }
    $cfg.PcsPro.AutoLaunch = $true
    $cfg.PcsPro.UseMock = $false

    # YouTube settings - add keys not present in the template
    $cfg.YouTube | Add-Member -NotePropertyName 'ClientId' -NotePropertyValue $clientId -Force
    $cfg.YouTube | Add-Member -NotePropertyName 'ClientSecret' -NotePropertyValue $clientSecret -Force
    $cfg.YouTube | Add-Member -NotePropertyName 'LiveStreamId' -NotePropertyValue $liveStreamId -Force
    $cfg.YouTube.UseMock = $false

    # Write UTF-8 without BOM (required for Microsoft.Extensions.Configuration.Json)
    $json = $cfg | ConvertTo-Json -Depth 32
    [System.IO.File]::WriteAllText($configPath, $json, (New-Object System.Text.UTF8Encoding $false))
}
catch
{
    # Fail-forward (HLPS I-C-3): log the error, exit 0 so the install continues.
    # Guard the log write itself - if INSTALLFOLDER is inaccessible, fall back to %TEMP%.
    try
    {
        $logDir = if ($installDir) { $installDir } else { $env:TEMP }
        $logPath = Join-Path $logDir 'install-config-ca.log'
        "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ERROR: $_" |
            Out-File $logPath -Append -Encoding utf8
    }
    catch
    {
        try { "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ERROR: $_" |
            Out-File (Join-Path $env:TEMP 'install-config-ca.log') -Append } catch {}
    }
}

exit 0
