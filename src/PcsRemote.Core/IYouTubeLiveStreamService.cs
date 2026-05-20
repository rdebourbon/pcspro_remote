namespace PcsRemote.Core;

/// <summary>
/// Defines the contract for managing YouTube live stream broadcasts.
/// Implementations handle broadcast creation, transition to live, stopping, and error recovery.
/// </summary>
public interface IYouTubeLiveStreamService
{
    /// <summary>
    /// Gets the current availability/readiness state of the YouTube service.
    /// A value of <see cref="YouTubeAvailability.Ready"/> means streaming operations
    /// can proceed; any other value means YouTube features are degraded or unavailable.
    /// </summary>
    YouTubeAvailability Availability { get; }

    /// <summary>
    /// Raised whenever the service's authentication or availability state changes.
    /// This is separate from <see cref="StatusChanged"/>, which tracks broadcast lifecycle.
    /// Fires on initialisation failures and when auth is restored via
    /// <see cref="RunOAuthSetupAsync"/>.
    /// </summary>
    event EventHandler<YouTubeAuthStatusSnapshot> AuthStatusChanged;

    /// <summary>
    /// Raised when the OAuth refresh token is approaching expiry and the operator should
    /// run YouTube Setup to re-authorise before the next match. This event is advisory only —
    /// no availability state change occurs when it fires.
    /// </summary>
    event EventHandler? TokenExpiryApproaching;

    /// <summary>
    /// Gets the current lifecycle status of the live stream.
    /// </summary>
    LiveStreamStatus CurrentStatus { get; }

    /// <summary>
    /// Gets the currently active broadcast, or <see langword="null"/> when no broadcast is active.
    /// </summary>
    LiveBroadcastInfo? CurrentBroadcast { get; }

    /// <summary>
    /// Raised whenever the live stream status changes. The event argument carries an
    /// immutable <see cref="StreamStateSnapshot"/> with the new status, broadcast, and error info.
    /// </summary>
    event EventHandler<StreamStateSnapshot> StatusChanged;

    /// <summary>
    /// Performs one-time initialisation (e.g., token validation). If no YouTube token
    /// is configured, logs an informational message and returns without error.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates a broadcast and transitions the stream to <see cref="LiveStreamStatus.Live"/>.
    /// </summary>
    Task StartStreamAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops the current broadcast. Works in both <see cref="LiveStreamStatus.Live"/> and
    /// <see cref="LiveStreamStatus.Starting"/> states.
    /// </summary>
    Task StopStreamAsync(CancellationToken ct = default);

    /// <summary>
    /// Resets the service from <see cref="LiveStreamStatus.Error"/> back to
    /// <see cref="LiveStreamStatus.Idle"/>.
    /// </summary>
    Task ResetAsync(CancellationToken ct = default);

    /// <summary>
    /// Runs the Google OAuth2 consent flow interactively (opens the default browser),
    /// stores the resulting token, and re-initialises the service. Returns <see langword="true"/>
    /// on success (including partial success where the token is stored but configuration
    /// validation fails), or <see langword="false"/> if client credentials are not configured.
    /// </summary>
    Task<bool> RunOAuthSetupAsync(CancellationToken ct = default);

    /// <summary>
    /// Requests a proactive Google credential refresh when the service is in the ready state.
    /// When not ready, this method is a no-op. On token failure, transitions to AuthFailed
    /// and fires <see cref="AuthStatusChanged"/>. This method is the entry point for the
    /// background refresh scheduler (S-004).
    /// </summary>
    Task RunProactiveRefreshAsync(CancellationToken ct = default);
}
