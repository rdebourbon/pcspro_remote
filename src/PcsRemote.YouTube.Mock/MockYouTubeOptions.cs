namespace PcsRemote.YouTube.Mock;

/// <summary>
/// Configuration options for <see cref="MockYouTubeLiveStreamService"/>.
/// </summary>
public class MockYouTubeOptions
{
    /// <summary>Simulated delay (ms) between Starting and Live.</summary>
    public int StartDelayMs { get; set; } = 1500;

    /// <summary>Simulated delay (ms) between Stopping and Idle.</summary>
    public int StopDelayMs { get; set; } = 500;

    /// <summary>When true, StartStreamAsync transitions to Error instead of Live.</summary>
    public bool SimulateStartFailure { get; set; }

    /// <summary>When true, InitializeAsync reconciles to Live with a synthetic broadcast.</summary>
    public bool SimulateActiveOnStartup { get; set; }
}
