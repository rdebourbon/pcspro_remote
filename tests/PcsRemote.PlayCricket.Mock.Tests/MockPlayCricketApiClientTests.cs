using FluentAssertions;
using Microsoft.Extensions.Options;
using PcsRemote.Core;
using PcsRemote.PlayCricket.Mock;

namespace PcsRemote.PlayCricket.Mock.Tests;

[TestClass]
public class MockPlayCricketApiClientTests
{
    private static MockPlayCricketApiClient CreateClient(MockPlayCricketOptions options) =>
        new(Options.Create(options));

    // TC-1: Returns configured fixtures for a known site ID
    [TestMethod]
    public async Task GetFixturesAsync_KnownSiteId_ReturnsConfiguredFixtures()
    {
        var fixture1 = new PlayCricketFixture(101, "HHCC 1st XI", "Visitors CC", "Result", new DateOnly(2025, 6, 15));
        var fixture2 = new PlayCricketFixture(102, "HHCC 2nd XI", "Away CC",     "Playing", new DateOnly(2025, 6, 22));

        var options = new MockPlayCricketOptions
        {
            Fixtures = new Dictionary<int, List<PlayCricketFixture>>
            {
                [42] = [fixture1, fixture2],
            },
        };

        var client = CreateClient(options);

        var result = await client.GetFixturesAsync(42);

        result.Should().HaveCount(2);
        result[0].FixtureId.Should().Be(101);
        result[0].HomeTeam.Should().Be("HHCC 1st XI");
        result[0].AwayTeam.Should().Be("Visitors CC");
        result[0].Status.Should().Be("Result");
        result[0].MatchDate.Should().Be(new DateOnly(2025, 6, 15));
        result[1].FixtureId.Should().Be(102);
    }

    // TC-2: Returns empty list for unknown site ID without throwing
    [TestMethod]
    public async Task GetFixturesAsync_UnknownSiteId_ReturnsEmptyList()
    {
        var options = new MockPlayCricketOptions
        {
            Fixtures = new Dictionary<int, List<PlayCricketFixture>>
            {
                [42] = [new PlayCricketFixture(101)],
            },
        };

        var client = CreateClient(options);

        var result = await client.GetFixturesAsync(99);

        result.Should().BeEmpty();
    }

    // TC-3: Returns empty list when no fixtures are configured
    [TestMethod]
    public async Task GetFixturesAsync_NoFixturesConfigured_ReturnsEmptyList()
    {
        var client = CreateClient(new MockPlayCricketOptions());

        var result = await client.GetFixturesAsync(42);

        result.Should().BeEmpty();
    }

    // TC-4: Already-cancelled token throws OperationCanceledException
    [TestMethod]
    public async Task GetFixturesAsync_AlreadyCancelledToken_ThrowsOperationCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var client = CreateClient(new MockPlayCricketOptions());

        var act = async () => await client.GetFixturesAsync(42, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
