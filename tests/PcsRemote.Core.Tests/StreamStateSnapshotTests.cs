using FluentAssertions;
using PcsRemote.Core;

namespace PcsRemote.Core.Tests;

[TestClass]
public sealed class StreamStateSnapshotTests
{
    [TestMethod]
    public void Constructor_WithAllValues_SetsProperties()
    {
        var broadcast = new LiveBroadcastInfo("id", "title", "url");
        var sut = new StreamStateSnapshot(LiveStreamStatus.Live, broadcast, null);

        sut.Status.Should().Be(LiveStreamStatus.Live);
        sut.CurrentBroadcast.Should().Be(broadcast);
        sut.ErrorMessage.Should().BeNull();
    }

    [TestMethod]
    public void Constructor_WithNullBroadcastAndError_PreservesNulls()
    {
        var sut = new StreamStateSnapshot(LiveStreamStatus.Idle, null, null);

        sut.CurrentBroadcast.Should().BeNull();
        sut.ErrorMessage.Should().BeNull();
    }

    [TestMethod]
    public void Constructor_WithErrorMessage_PreservesMessage()
    {
        var sut = new StreamStateSnapshot(LiveStreamStatus.Error, null, "API quota exceeded");

        sut.Status.Should().Be(LiveStreamStatus.Error);
        sut.ErrorMessage.Should().Be("API quota exceeded");
    }
}
