using Microsoft.Extensions.Options;

namespace PcsRemote.YouTube;

/// <summary>
/// Persists the last-consent timestamp as an ISO-8601 file on disk, co-located
/// with the Google credential store but in a separate file so it does not interfere
/// with the credential store's own bookkeeping.
/// </summary>
public sealed class FileStalenessPersistence : IStalenessPersistence
{
    private const string MarkerFileName = "token-staleness-marker";

    private readonly string _markerFilePath;

    /// <summary>
    /// Initialises a new instance, resolving the marker file path from
    /// <see cref="YouTubeOptions.GetEffectiveTokenStorePath"/>.
    /// </summary>
    public FileStalenessPersistence(IOptions<YouTubeOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _markerFilePath = Path.Combine(
            options.Value.GetEffectiveTokenStorePath(),
            MarkerFileName);
    }

    /// <inheritdoc/>
    public async Task<DateTimeOffset?> GetLastConsentAtAsync()
    {
        if (!File.Exists(_markerFilePath))
        {
            return null;
        }

        var text = await File.ReadAllTextAsync(_markerFilePath).ConfigureAwait(false);

        if (DateTimeOffset.TryParse(
                text.Trim(),
                null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }

    /// <inheritdoc/>
    public async Task SetLastConsentAtAsync(DateTimeOffset value)
    {
        var directory = Path.GetDirectoryName(_markerFilePath)!;
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(_markerFilePath, value.ToUniversalTime().ToString("O"))
            .ConfigureAwait(false);
    }
}
