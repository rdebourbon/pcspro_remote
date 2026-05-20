namespace PcsRemote.PlayCricket;

/// <summary>
/// Strongly-typed options bound to the <c>PlayCricket</c> configuration section.
/// </summary>
public sealed class PlayCricketOptions
{
    /// <summary>
    /// Play-Cricket API key. Must not be committed to source control (HLPS-021 C-2).
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Play-Cricket site identifiers to query. Resolution queries all sites in parallel (S-006).
    /// </summary>
    public List<int> SiteIds { get; set; } = [];

    /// <summary>
    /// Interval in seconds between poll ticks, shared by both the auto-load (FlaUI) and
    /// auto-close (API) polling loops. Default: 90. Minimum 60 is enforced at the service
    /// level in S-005/S-007 — not in this class.
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 90;

    /// <summary>
    /// Duration in seconds for the auto-close countdown shown in the web UI. Default: 300 (5 minutes).
    /// </summary>
    public int CountdownDurationSeconds { get; set; } = 300;

    /// <summary>
    /// Club name prefix used to normalise Play-Cricket team names for fixture ID resolution (IS-021 S-006).
    /// When non-empty, the prefix is stripped from both Play-Cricket and PCS Pro team names before comparison,
    /// matching the same club-name prefix logic used by <c>TeamNameFormatter</c>.
    /// </summary>
    public string ClubName { get; set; } = string.Empty;

    /// <summary>
    /// When <see langword="true"/>, registers <c>MockPlayCricketApiClient</c>
    /// instead of the real HTTP implementation.
    /// </summary>
    public bool UseMock { get; set; }
}
