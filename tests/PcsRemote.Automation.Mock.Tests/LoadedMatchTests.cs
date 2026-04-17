using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PcsRemote.Automation.Mock;
using PcsRemote.Core;

namespace PcsRemote.Automation.Mock.Tests;

[TestClass]
public sealed class LoadedMatchTests
{
    private static MockPcsProAutomationService CreateSut(
        Action<MockPcsProOptions>? configure = null)
    {
        var opts = new MockPcsProOptions();
        configure?.Invoke(opts);
        return new MockPcsProAutomationService(
            Options.Create(opts),
            NullLogger<MockPcsProAutomationService>.Instance);
    }

    [TestMethod]
    public void LoadedMatch_BeforeLoadMatchAsync_ReturnsNull()
    {
        var sut = CreateSut();

        sut.LoadedMatch.Should().BeNull();
    }

    [TestMethod]
    public async Task LoadedMatch_AfterLoadMatchAsync_ReturnsMatch()
    {
        var sut = CreateSut();
        await sut.LaunchAndLoginAsync();

        var match = new MatchInfo("match-1", "HHCC", "Visitors", "Club T20", DateOnly.FromDateTime(DateTime.Today));
        await sut.LoadMatchAsync(match);

        sut.LoadedMatch.Should().Be(match);
    }

    [TestMethod]
    public async Task LoadedMatch_AfterChangeMatchAsync_ReturnsNull()
    {
        var sut = CreateSut();
        await sut.LaunchAndLoginAsync();

        var match = new MatchInfo("match-1");
        await sut.LoadMatchAsync(match);
        sut.LoadedMatch.Should().NotBeNull();

        await sut.ChangeMatchAsync();

        sut.LoadedMatch.Should().BeNull();
    }

    [TestMethod]
    public async Task LoadedMatch_AfterStopAsync_ReturnsNull()
    {
        var sut = CreateSut();
        await sut.LaunchAndLoginAsync();

        var match = new MatchInfo("match-1");
        await sut.LoadMatchAsync(match);
        sut.LoadedMatch.Should().NotBeNull();

        await sut.StopAsync();

        sut.LoadedMatch.Should().BeNull();
    }
}
