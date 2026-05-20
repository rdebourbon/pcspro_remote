namespace PcsRemote.YouTube;

/// <summary>
/// Persists the timestamp of the last Google OAuth re-consent so that the service
/// can determine whether the stored token is approaching staleness.
/// </summary>
public interface IStalenessPersistence
{
    /// <summary>
    /// Returns the UTC timestamp of the last re-consent, or <see langword="null"/>
    /// when no marker is present (e.g. first run or marker was never written).
    /// </summary>
    Task<DateTimeOffset?> GetLastConsentAtAsync();

    /// <summary>
    /// Writes the re-consent marker, overwriting any previously stored value.
    /// </summary>
    Task SetLastConsentAtAsync(DateTimeOffset value);
}
