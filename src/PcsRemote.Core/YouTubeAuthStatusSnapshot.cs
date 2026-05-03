namespace PcsRemote.Core;

/// <summary>
/// Immutable snapshot carried by <see cref="IYouTubeLiveStreamService.AuthStatusChanged"/> events.
/// Captures the current availability state and an optional diagnostic message.
/// </summary>
/// <param name="Availability">The current <see cref="YouTubeAvailability"/>.</param>
/// <param name="DiagnosticMessage">A human-readable description for operators, or <see langword="null"/> when ready.</param>
public record YouTubeAuthStatusSnapshot(
    YouTubeAvailability Availability,
    string? DiagnosticMessage);
