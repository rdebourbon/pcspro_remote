using PcsRemote.Core;

namespace PcsRemote.PlayCricket.Mock;

/// <summary>
/// Configuration options for the mock Play-Cricket API client.
/// </summary>
public sealed class MockPlayCricketOptions
{
    /// <summary>
    /// Gets or sets the fixture lists to return per site ID.
    /// Keys are site IDs; values are the fixture lists for that site.
    /// Defaults to an empty dictionary (no fixtures for any site).
    /// </summary>
    public Dictionary<int, List<PlayCricketFixture>> Fixtures { get; set; } = new();
}
