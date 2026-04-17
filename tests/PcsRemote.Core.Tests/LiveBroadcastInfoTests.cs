using FluentAssertions;
using PcsRemote.Core;

namespace PcsRemote.Core.Tests;

[TestClass]
public sealed class LiveBroadcastInfoTests
{
    [TestMethod]
    public void Constructor_SetsAllProperties()
    {
        var sut = new LiveBroadcastInfo("broadcast-123", "HHCC vs Visitors", "https://youtu.be/abc");

        sut.BroadcastId.Should().Be("broadcast-123");
        sut.Title.Should().Be("HHCC vs Visitors");
        sut.WatchUrl.Should().Be("https://youtu.be/abc");
    }

    [TestMethod]
    public void Equality_TwoRecordsWithSameValues_AreEqual()
    {
        var a = new LiveBroadcastInfo("id-1", "Title", "https://example.com");
        var b = new LiveBroadcastInfo("id-1", "Title", "https://example.com");

        a.Should().Be(b);
    }

    [TestMethod]
    public void Equality_TwoRecordsWithDifferentValues_AreNotEqual()
    {
        var a = new LiveBroadcastInfo("id-1", "Title A", "https://example.com");
        var b = new LiveBroadcastInfo("id-2", "Title B", "https://example.com");

        a.Should().NotBe(b);
    }
}
