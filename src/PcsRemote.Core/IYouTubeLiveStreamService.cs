namespace PcsRemote.Core;

/// <summary>
/// Defines the contract for managing YouTube live stream broadcasts.
/// Implementations handle broadcast creation, transition to live, stopping, and error recovery.
/// </summary>
public interface IYouTubeLiveStreamService
{
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
}
