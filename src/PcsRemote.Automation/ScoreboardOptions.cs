namespace PcsRemote.Automation;

/// <summary>
/// Configuration for scoreboard capture.
/// Bound to the <c>Scoreboard:</c> configuration section.
/// </summary>
internal sealed class ScoreboardOptions
{
    /// <summary>
    /// JPEG quality (1–100) used when capturing the scoreboard image.
    /// Defaults to 85. Must be read from configuration — not hardcoded.
    /// </summary>
    public int JpegQuality { get; set; } = 85;
}
