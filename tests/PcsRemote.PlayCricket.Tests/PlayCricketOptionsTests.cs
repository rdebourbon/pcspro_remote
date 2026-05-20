using FluentAssertions;
using Microsoft.Extensions.Configuration;
using PcsRemote.PlayCricket;

namespace PcsRemote.PlayCricket.Tests;

[TestClass]
public sealed class PlayCricketOptionsTests
{
    // TC-1: PlayCricketOptions bound from in-memory IConfiguration has correct defaults.
    [TestMethod]
    public void PlayCricketOptions_BoundFromConfig_DefaultsAreCorrect()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlayCricket:UseMock"] = "false",
                ["PlayCricket:PollingIntervalSeconds"] = "90",
                ["PlayCricket:CountdownDurationSeconds"] = "300",
                ["PlayCricket:ApiKey"] = "",
            })
            .Build();

        var options = new PlayCricketOptions();
        config.GetSection("PlayCricket").Bind(options);

        options.PollingIntervalSeconds.Should().Be(90);
        options.CountdownDurationSeconds.Should().Be(300);
        options.SiteIds.Should().BeEmpty();
        options.ApiKey.Should().Be("");
        options.UseMock.Should().BeFalse();
    }
}
