namespace PcsRemote.Web;

/// <summary>
/// Configuration options for the debug/advanced section.
/// Bound to the <c>DebugSection</c> configuration section.
/// </summary>
public sealed class DebugSectionOptions
{
    /// <summary>
    /// Optional PIN to gate access to the debug section.
    /// When <see langword="null"/> or empty, the section is freely accessible.
    /// </summary>
    public string? Pin { get; set; }
}
