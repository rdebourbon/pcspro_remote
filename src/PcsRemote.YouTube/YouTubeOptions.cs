namespace PcsRemote.YouTube;

/// <summary>
/// Strongly-typed options bound to the <c>YouTube</c> configuration section.
/// </summary>
public sealed class YouTubeOptions
{
    /// <summary>OAuth2 desktop application client ID.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>OAuth2 desktop application client secret.</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// YouTube <c>liveStream</c> resource ID bound to the club's PCS Pro stream key.
    /// Required for production; fail-fast at startup if absent.
    /// </summary>
    public string LiveStreamId { get; set; } = "";

    /// <summary>
    /// Title template with <c>{Token}</c> placeholders.
    /// Null or empty falls back to the default <c>"{HomeTeam} vs {AwayTeam}"</c>.
    /// </summary>
    public string? BroadcastTitleTemplate { get; set; }

    /// <summary>
    /// Broadcast privacy setting. Only <c>"public"</c> is supported and tested.
    /// </summary>
    public string BroadcastPrivacy { get; set; } = "public";

    /// <summary>
    /// Timeout in seconds waiting for PCS Pro to report stream as active.
    /// </summary>
    public int StreamReadyTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Interval in seconds between PCS Pro stream health poll requests.
    /// </summary>
    public int StreamPollIntervalSeconds { get; set; } = 3;

    /// <summary>
    /// DPAPI-encrypted token file directory. Defaults to
    /// <c>%AppData%\PcsRemote\GoogleTokens</c> when empty.
    /// </summary>
    public string? TokenStorePath { get; set; }

    /// <summary>
    /// When <see langword="true"/>, registers <c>MockYouTubeLiveStreamService</c>
    /// instead of the real implementation.
    /// </summary>
    public bool UseMock { get; set; }

    /// <summary>
    /// Returns the effective token store path, falling back to the AppData default.
    /// </summary>
    public string GetEffectiveTokenStorePath() =>
        string.IsNullOrWhiteSpace(TokenStorePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PcsRemote",
                "GoogleTokens")
            : TokenStorePath;
}
