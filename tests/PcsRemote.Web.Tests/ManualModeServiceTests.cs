using FluentAssertions;
using PcsRemote.Web.Services;

namespace PcsRemote.Web.Tests;

[TestClass]
public sealed class ManualModeServiceTests
{
    private static ManualModeService CreateSut() => new();

    // ──────────────────────────────────────────────────────────────────────
    // TC-1  Fresh instance — IsManualModeActive returns false
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void IsManualModeActive_FreshInstance_ReturnsFalse()
    {
        var sut = CreateSut();
        sut.IsManualModeActive.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-2  Enable() on inactive service
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Enable_WhenInactive_BecomesActiveAndFiresEventOnceWithTrue()
    {
        var sut = CreateSut();
        var eventArgs = new List<bool>();
        sut.ManualModeChanged += (_, v) => eventArgs.Add(v);

        sut.Enable();

        sut.IsManualModeActive.Should().BeTrue();
        eventArgs.Should().ContainSingle().Which.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-3  Enable() called twice — idempotent
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Enable_WhenAlreadyActive_IsNoOpAndFiresEventOnceTotal()
    {
        var sut = CreateSut();
        var eventCount = 0;
        sut.ManualModeChanged += (_, _) => eventCount++;

        sut.Enable();
        sut.Enable();

        sut.IsManualModeActive.Should().BeTrue();
        eventCount.Should().Be(1);
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-4  Disable() on active service
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Disable_WhenActive_BecomesInactiveAndFiresEventOnceWithFalse()
    {
        var sut = CreateSut();
        sut.Enable();

        var eventArgs = new List<bool>();
        sut.ManualModeChanged += (_, v) => eventArgs.Add(v);

        sut.Disable();

        sut.IsManualModeActive.Should().BeFalse();
        eventArgs.Should().ContainSingle().Which.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-5  Disable() on already-inactive service — does not fire
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Disable_WhenAlreadyInactive_DoesNotFireEvent()
    {
        var sut = CreateSut();
        var eventCount = 0;
        sut.ManualModeChanged += (_, _) => eventCount++;

        sut.Disable();

        sut.IsManualModeActive.Should().BeFalse();
        eventCount.Should().Be(0);
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-6  Concurrent Enable() calls from N threads
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Enable_ConcurrentCallsFromMultipleThreads_FiresEventExactlyOnce()
    {
        const int threadCount = 20;
        var sut = CreateSut();
        var eventCount = 0;
        sut.ManualModeChanged += (_, _) => Interlocked.Increment(ref eventCount);

        var barrier = new Barrier(threadCount);
        var threads = Enumerable
            .Range(0, threadCount)
            .Select(_ => new Thread(() =>
            {
                barrier.SignalAndWait();
                sut.Enable();
            }))
            .ToList();

        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        sut.IsManualModeActive.Should().BeTrue();
        eventCount.Should().Be(1);
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-7  Concurrent Disable() calls from N threads
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Disable_ConcurrentCallsFromMultipleThreads_FiresEventExactlyOnce()
    {
        const int threadCount = 20;
        var sut = CreateSut();
        sut.Enable();

        var eventCount = 0;
        sut.ManualModeChanged += (_, _) => Interlocked.Increment(ref eventCount);

        var barrier = new Barrier(threadCount);
        var threads = Enumerable
            .Range(0, threadCount)
            .Select(_ => new Thread(() =>
            {
                barrier.SignalAndWait();
                sut.Disable();
            }))
            .ToList();

        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        sut.IsManualModeActive.Should().BeFalse();
        eventCount.Should().Be(1);
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-7b  Mixed concurrent Enable() + Disable() from N threads each
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void EnableAndDisable_ConcurrentMixedCalls_NoLostUpdateOrExtraEvents()
    {
        const int threadCount = 20;
        var sut = CreateSut();

        var eventArgs = new System.Collections.Concurrent.ConcurrentQueue<bool>();
        sut.ManualModeChanged += (_, v) => eventArgs.Enqueue(v);

        var barrier = new Barrier(threadCount * 2);
        var enableThreads = Enumerable
            .Range(0, threadCount)
            .Select(_ => new Thread(() =>
            {
                barrier.SignalAndWait();
                sut.Enable();
            }))
            .ToList();

        var disableThreads = Enumerable
            .Range(0, threadCount)
            .Select(_ => new Thread(() =>
            {
                barrier.SignalAndWait();
                sut.Disable();
            }))
            .ToList();

        var allThreads = enableThreads.Concat(disableThreads).ToList();
        allThreads.ForEach(t => t.Start());
        allThreads.ForEach(t => t.Join());

        // Final state must be accessible without corruption.
        _ = sut.IsManualModeActive;

        // Key invariant: total enable-wins and disable-wins must be balanced (±1).
        // The CAS guarantees each transition fires exactly once, and the state machine
        // requires Enable and Disable wins to alternate. Therefore:
        // |trueCount - falseCount| ≤ 1 (at most one direction has one extra win).
        var events = eventArgs.ToArray();
        var trueCount = events.Count(v => v);
        var falseCount = events.Count(v => !v);
        Math.Abs(trueCount - falseCount).Should().BeLessOrEqualTo(1,
            because: "Enable and Disable wins must be balanced (state machine alternation invariant)");

        // Upper bound: cannot exceed N transitions in each direction.
        events.Length.Should().BeInRange(0, 2 * threadCount);
    }
}
