using FluentAssertions;
using PcsRemote.Core;

namespace PcsRemote.Core.Tests;

[TestClass]
public sealed class LiveStreamStatusTests
{
    [TestMethod]
    public void Enum_HasExactlyFiveMembers()
    {
        Enum.GetValues<LiveStreamStatus>().Should().HaveCount(5);
    }

    [TestMethod]
    public void DefaultValue_IsIdle()
    {
        default(LiveStreamStatus).Should().Be(LiveStreamStatus.Idle);
    }

    [TestMethod]
    [DataRow(LiveStreamStatus.Idle,     0, DisplayName = "Idle = 0")]
    [DataRow(LiveStreamStatus.Starting, 1, DisplayName = "Starting = 1")]
    [DataRow(LiveStreamStatus.Live,     2, DisplayName = "Live = 2")]
    [DataRow(LiveStreamStatus.Stopping, 3, DisplayName = "Stopping = 3")]
    [DataRow(LiveStreamStatus.Error,    4, DisplayName = "Error = 4")]
    public void Member_HasExpectedOrdinal(LiveStreamStatus member, int expected)
    {
        ((int)member).Should().Be(expected);
    }
}
