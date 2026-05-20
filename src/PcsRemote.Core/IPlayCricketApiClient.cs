namespace PcsRemote.Core;

/// <summary>
/// Contract for querying Play-Cricket fixture data for a given site.
/// Implementations must return an empty list (not throw) on HTTP error or
/// deserialisation failure; error handling and logging are implementation concerns.
/// </summary>
public interface IPlayCricketApiClient
{
    /// <summary>
    /// Returns all fixtures for the specified Play-Cricket site.
    /// Returns an empty list when the site has no fixtures, the API is unreachable,
    /// or the response cannot be deserialised.
    /// </summary>
    /// <param name="siteId">Play-Cricket site identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<PlayCricketFixture>> GetFixturesAsync(int siteId, CancellationToken ct = default);
}
