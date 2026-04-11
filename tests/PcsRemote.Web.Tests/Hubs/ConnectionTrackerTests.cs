using FluentAssertions;
using PcsRemote.Web.Hubs;

namespace PcsRemote.Web.Tests.Hubs;

[TestClass]
public class ConnectionTrackerTests
{
    [TestMethod]
    public void ConnectionCount_StartsAtZero()
    {
        var tracker = new ConnectionTracker();
        tracker.ConnectionCount.Should().Be(0);
    }

    [TestMethod]
    public void Increment_ProducesCountOfOne()
    {
        var tracker = new ConnectionTracker();
        tracker.Increment();
        tracker.ConnectionCount.Should().Be(1);
    }

    [TestMethod]
    public void MultipleIncrements_ProduceCorrectFinalCount()
    {
        var tracker = new ConnectionTracker();
        tracker.Increment();
        tracker.Increment();
        tracker.Increment();
        tracker.ConnectionCount.Should().Be(3);
    }

    [TestMethod]
    public void Decrement_AfterOneIncrement_ReturnsCountToZero()
    {
        var tracker = new ConnectionTracker();
        tracker.Increment();
        tracker.Decrement();
        tracker.ConnectionCount.Should().Be(0);
    }

    [TestMethod]
    public void Decrement_OnZeroCounter_IsNoOp()
    {
        var tracker = new ConnectionTracker();
        tracker.Decrement();
        tracker.ConnectionCount.Should().Be(0);
    }

    [TestMethod]
    public void Increment_RaisesConnectionCountChangedWithNewCount()
    {
        var tracker = new ConnectionTracker();
        int? receivedCount = null;
        tracker.ConnectionCountChanged += count => receivedCount = count;

        tracker.Increment();

        receivedCount.Should().Be(1);
    }

    [TestMethod]
    public void Decrement_RaisesConnectionCountChangedWithNewCount()
    {
        var tracker = new ConnectionTracker();
        int? receivedCount = null;
        tracker.ConnectionCountChanged += count => receivedCount = count;

        tracker.Increment();
        receivedCount = null; // reset after increment notification
        tracker.Decrement();

        receivedCount.Should().Be(0);
    }

    [TestMethod]
    public void Decrement_OnZeroCounter_DoesNotRaiseConnectionCountChanged()
    {
        var tracker = new ConnectionTracker();
        bool notified = false;
        tracker.ConnectionCountChanged += _ => notified = true;

        tracker.Decrement();

        notified.Should().BeFalse();
    }
}
