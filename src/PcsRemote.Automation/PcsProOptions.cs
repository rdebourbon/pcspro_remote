namespace PcsRemote.Automation;

/// <summary>
/// Configuration for launching and connecting to PlayCricket Scorer Pro.
/// Bound to the <c>PcsPro:</c> configuration section.
/// </summary>
internal sealed class PcsProOptions
{
    /// <summary>
    /// Full path to the PCS Pro executable (cricket.exe).
    /// </summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// Working directory for the PCS Pro process, or empty to use the executable's directory.
    /// </summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// PCS Pro login password. Must not be committed to source control — supply via
    /// gitignored configuration, environment variable, or .NET User Secrets.
    /// </summary>
    public string Password { get; set; } = string.Empty;
}
