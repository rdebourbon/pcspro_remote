using FluentAssertions;
using PcsRemote.Core;
using PcsRemote.Web.Services;

namespace PcsRemote.Web.Tests.Services;

[TestClass]
public sealed class OperationCoordinatorServiceTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // TC-1  Initial state is idle
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void InitialState_IsOperationInProgress_IsFalse()
    {
        var sut = new OperationCoordinatorService();
        sut.IsOperationInProgress.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-2  BeginOperation sets true and fires event once with true
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void BeginOperation_SetsInProgressTrue_FiresEventOnce_ReturnsTrue()
    {
        var sut = new OperationCoordinatorService();
        var events = new List<bool>();
        sut.OperationInProgressChanged += (_, v) => events.Add(v);

        var acquired = sut.BeginOperation();

        acquired.Should().BeTrue("the CAS 0→1 should succeed on first call");
        sut.IsOperationInProgress.Should().BeTrue();
        events.Should().ContainSingle().Which.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-3  MarkComplete after begin sets false and fires event once with false
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void MarkComplete_AfterBegin_SetsIdleFalse_FiresEventOnce()
    {
        var sut = new OperationCoordinatorService();
        sut.BeginOperation();
        var events = new List<bool>();
        sut.OperationInProgressChanged += (_, v) => events.Add(v);

        sut.MarkComplete();

        sut.IsOperationInProgress.Should().BeFalse();
        events.Should().ContainSingle().Which.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-4  Sequential second BeginOperation while already in progress is a no-op
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void BeginOperation_WhenAlreadyInProgress_IsNoOp_NoEventFired_ReturnsFalse()
    {
        var sut = new OperationCoordinatorService();
        sut.BeginOperation();

        var events = new List<bool>();
        sut.OperationInProgressChanged += (_, v) => events.Add(v);

        var acquired = sut.BeginOperation(); // second call — should be no-op

        acquired.Should().BeFalse("the CAS 0→1 should fail when already in progress");
        sut.IsOperationInProgress.Should().BeTrue();
        events.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-5  MarkComplete when not in progress is a no-op
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void MarkComplete_WhenIdle_IsNoOp_NoEventFired()
    {
        var sut = new OperationCoordinatorService();
        var events = new List<bool>();
        sut.OperationInProgressChanged += (_, v) => events.Add(v);

        sut.MarkComplete();

        sut.IsOperationInProgress.Should().BeFalse();
        events.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-6  Concurrent BeginOperation: exactly one thread wins the CAS
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void BeginOperation_Concurrent_ExactlyOneWins_EventFiredOnce()
    {
        var sut = new OperationCoordinatorService();
        int trueFirings = 0;
        sut.OperationInProgressChanged += (_, v) =>
        {
            if (v) Interlocked.Increment(ref trueFirings);
        };

        const int threads = 20;
        var barrier = new Barrier(threads);
        var tasks = Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait();
            sut.BeginOperation();
        })).ToArray();
        Task.WaitAll(tasks);

        trueFirings.Should().Be(1, "exactly one CAS should succeed");
        sut.IsOperationInProgress.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-6b Mixed concurrent BeginOperation + MarkComplete: balance invariant holds
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void BeginAndMarkComplete_Concurrent_BalanceInvariantHolds()
    {
        var sut = new OperationCoordinatorService();
        int trueFirings = 0;
        int falseFirings = 0;
        sut.OperationInProgressChanged += (_, v) =>
        {
            if (v) Interlocked.Increment(ref trueFirings);
            else Interlocked.Increment(ref falseFirings);
        };

        const int threads = 20;
        var barrier = new Barrier(threads);
        var tasks = Enumerable.Range(0, threads).Select(i => Task.Run(() =>
        {
            barrier.SignalAndWait();
            if (i % 2 == 0) sut.BeginOperation();
            else sut.MarkComplete();
        })).ToArray();
        Task.WaitAll(tasks);

        Math.Abs(trueFirings - falseFirings).Should().BeLessThanOrEqualTo(1,
            "true and false firings should be balanced within 1 — the coordinator can only be ahead or behind by at most one transition at termination");
        falseFirings.Should().BeLessThanOrEqualTo(trueFirings,
            "MarkComplete cannot outpace BeginOperation — each false firing requires a prior true firing");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-7  Subscribe-before-snapshot: subscriber always observes consistent state
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task SubscribeBeforeSnapshot_ObservesConsistentState()
    {
        // This test verifies the subscribe-before-snapshot contract at the service level:
        // a subscriber that attaches and then reads IsOperationInProgress will never
        // miss the in-progress state, even if BeginOperation() runs concurrently.
        var sut = new OperationCoordinatorService();
        const int iterations = 100;
        int inconsistencies = 0;

        var tasks = Enumerable.Range(0, iterations).Select(_ => Task.Run(() =>
        {
            bool observedViaEvent = false;

            // Subscribe before reading snapshot (the project contract)
            sut.OperationInProgressChanged += (_, v) =>
            {
                if (v) observedViaEvent = true;
            };
            bool snapshot = sut.IsOperationInProgress;

            // If the snapshot is true OR the event fired, the subscriber is informed.
            // An inconsistency is only possible if neither is true when in-progress.
            if (sut.IsOperationInProgress && !snapshot && !observedViaEvent)
                Interlocked.Increment(ref inconsistencies);
        })).Concat(Enumerable.Range(0, iterations).Select(_ => Task.Run(() =>
        {
            sut.BeginOperation();
            sut.MarkComplete();
        }))).ToArray();

        await Task.WhenAll(tasks);

        inconsistencies.Should().Be(0);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-8  Full two-cycle round-trip fires two pairs of events in order
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void TwoCycleRoundTrip_FiresFourEventsInOrder()
    {
        var sut = new OperationCoordinatorService();
        var events = new List<bool>();
        sut.OperationInProgressChanged += (_, v) => events.Add(v);

        sut.BeginOperation();
        sut.MarkComplete();
        sut.BeginOperation();
        sut.MarkComplete();

        events.Should().Equal(true, false, true, false);
    }
}
