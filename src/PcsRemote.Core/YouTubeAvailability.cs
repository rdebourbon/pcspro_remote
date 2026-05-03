namespace PcsRemote.Core;

/// <summary>
/// Describes the YouTube service's readiness state after initialisation.
/// </summary>
public enum YouTubeAvailability
{
    /// <summary>The service is authenticated, configured, and ready to stream.</summary>
    Ready = 0,

    /// <summary>No token is stored — the operator has not yet authorised YouTube.</summary>
    NotConfigured,

    /// <summary>The stored token is permanently invalid and has been deleted.
    /// The operator must re-authorise via the tray icon → YouTube Setup.</summary>
    AuthFailed,

    /// <summary>The token is valid but a configuration problem prevents streaming
    /// (e.g., the configured LiveStreamId does not exist).</summary>
    ConfigError,

    /// <summary>A transient API error occurred during initialisation.
    /// The token is preserved; the issue may resolve on its own.</summary>
    TransientError,
}
