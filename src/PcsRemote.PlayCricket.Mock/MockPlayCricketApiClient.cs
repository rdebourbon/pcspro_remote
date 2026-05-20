using Microsoft.Extensions.Options;
using PcsRemote.Core;

namespace PcsRemote.PlayCricket.Mock;

/// <summary>
/// In-memory mock implementation of <see cref="IPlayCricketApiClient"/>.
/// Returns configurable fixture lists per site ID; makes no network calls.
/// </summary>
public sealed class MockPlayCricketApiClient : IPlayCricketApiClient
{
    private readonly MockPlayCricketOptions _options;

    public MockPlayCricketApiClient(IOptions<MockPlayCricketOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<PlayCricketFixture>> GetFixturesAsync(
        int siteId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (_options.Fixtures.TryGetValue(siteId, out var fixtures))
            return Task.FromResult<IReadOnlyList<PlayCricketFixture>>(fixtures.AsReadOnly());

        return Task.FromResult<IReadOnlyList<PlayCricketFixture>>(Array.Empty<PlayCricketFixture>());
    }
}
