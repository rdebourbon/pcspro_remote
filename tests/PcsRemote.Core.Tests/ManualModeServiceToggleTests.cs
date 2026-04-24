using FluentAssertions;
using PcsRemote.Core;

namespace PcsRemote.Core.Tests;

[TestClass]
public sealed class ManualModeServiceToggleTests
{
    private static ManualModeService CreateSut() => new();

    // ──────────────────────────────────────────────────────────────────────
    // T1  Toggle() inactive → active
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Toggle_WhenInactive_BecomesActiveAndFiresEventWithTrue()
    {
        var sut = CreateSut();
        var eventArgs = new List<bool>();
        sut.ManualModeChanged += (_, v) => eventArgs.Add(v);

        sut.Toggle();

        sut.IsManualModeActive.Should().BeTrue();
        eventArgs.Should().ContainSingle().Which.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────────
    // T2  Toggle() active → inactive
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Toggle_WhenActive_BecomesInactiveAndFiresEventWithFalse()
    {
        var sut = CreateSut();
        sut.Enable();

        var eventArgs = new List<bool>();
        sut.ManualModeChanged += (_, v) => eventArgs.Add(v);

        sut.Toggle();

        sut.IsManualModeActive.Should().BeFalse();
        eventArgs.Should().ContainSingle().Which.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────
    // T3  Toggle() round-trip
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Toggle_CalledTwice_ReturnsToOriginalStateWithAlternatingEvents()
    {
        var sut = CreateSut();
        var eventArgs = new List<bool>();
        sut.ManualModeChanged += (_, v) => eventArgs.Add(v);

        sut.Toggle();
        sut.Toggle();

        sut.IsManualModeActive.Should().BeFalse();
        eventArgs.Should().HaveCount(2);
        eventArgs[0].Should().BeTrue();
        eventArgs[1].Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────
    // T4  Thread-safety smoke
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Toggle_ConcurrentCallsFromMultipleThreads_DeterministicStateAndEventCount()
    {
        const int threadCount = 50;
        var sut = CreateSut();
        var eventCount = 0;
        sut.ManualModeChanged += (_, _) => Interlocked.Increment(ref eventCount);

        var barrier = new Barrier(threadCount);
        var threads = Enumerable
            .Range(0, threadCount)
            .Select(_ => new Thread(() =>
            {
                barrier.SignalAndWait();
                sut.Toggle();
            }))
            .ToList();

        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        // Even number of toggles → back to original (inactive).
        sut.IsManualModeActive.Should().BeFalse(
            because: "even number of toggles returns to original state");

        // Each toggle retries until it wins, so every call fires exactly one event.
        eventCount.Should().Be(threadCount,
            because: "each Toggle() call completes exactly one state flip");
    }
}
