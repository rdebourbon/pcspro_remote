namespace PcsRemote.Core;

/// <summary>
/// Models the lifecycle state of a YouTube live stream managed by the application.
/// </summary>
public enum LiveStreamStatus
{
    /// <summary>No stream is active or pending.</summary>
    Idle = 0,

    /// <summary>A broadcast is being created and the stream is transitioning to live.</summary>
    Starting,

    /// <summary>The broadcast is live and streaming.</summary>
    Live,

    /// <summary>The broadcast is being stopped.</summary>
    Stopping,

    /// <summary>An error occurred during a streaming operation.</summary>
    Error,
}
