using FluentAssertions;
using PcsRemote.Core;

namespace PcsRemote.Core.Tests;

[TestClass]
public sealed class DateSelectionServiceTests
{
    private static DateSelectionService CreateSut() => new();

    // ──────────────────────────────────────────────────────────────────────
    // TC-1  Fresh instance — IsDateSelectionEnabled returns false
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void IsDateSelectionEnabled_FreshInstance_ReturnsFalse()
    {
        var sut = CreateSut();
        sut.IsDateSelectionEnabled.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-2  Enable() on disabled service
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Enable_WhenDisabled_BecomesEnabledAndFiresEventOnceWithTrue()
    {
        var sut = CreateSut();
        var eventArgs = new List<bool>();
        sut.DateSelectionEnabledChanged += (_, v) => eventArgs.Add(v);

        sut.Enable();

        sut.IsDateSelectionEnabled.Should().BeTrue();
        eventArgs.Should().ContainSingle().Which.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-3  Enable() called twice — idempotent
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Enable_WhenAlreadyEnabled_IsNoOpAndFiresEventOnceTotal()
    {
        var sut = CreateSut();
        var eventCount = 0;
        sut.DateSelectionEnabledChanged += (_, _) => eventCount++;

        sut.Enable();
        sut.Enable();

        sut.IsDateSelectionEnabled.Should().BeTrue();
        eventCount.Should().Be(1);
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-4  Disable() on enabled service
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Disable_WhenEnabled_BecomesDisabledAndFiresEventOnceWithFalse()
    {
        var sut = CreateSut();
        sut.Enable();

        var eventArgs = new List<bool>();
        sut.DateSelectionEnabledChanged += (_, v) => eventArgs.Add(v);

        sut.Disable();

        sut.IsDateSelectionEnabled.Should().BeFalse();
        eventArgs.Should().ContainSingle().Which.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-5  Disable() on already-disabled service — does not fire
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Disable_WhenAlreadyDisabled_DoesNotFireEvent()
    {
        var sut = CreateSut();
        var eventCount = 0;
        sut.DateSelectionEnabledChanged += (_, _) => eventCount++;

        sut.Disable();

        sut.IsDateSelectionEnabled.Should().BeFalse();
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
        sut.DateSelectionEnabledChanged += (_, _) => Interlocked.Increment(ref eventCount);

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

        sut.IsDateSelectionEnabled.Should().BeTrue();
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
        sut.DateSelectionEnabledChanged += (_, _) => Interlocked.Increment(ref eventCount);

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

        sut.IsDateSelectionEnabled.Should().BeFalse();
        eventCount.Should().Be(1);
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-8  Mixed concurrent Enable() + Disable() from N threads each
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void EnableAndDisable_ConcurrentMixedCalls_NoLostUpdateOrExtraEvents()
    {
        const int threadCount = 20;
        var sut = CreateSut();

        var eventArgs = new System.Collections.Concurrent.ConcurrentQueue<bool>();
        sut.DateSelectionEnabledChanged += (_, v) => eventArgs.Enqueue(v);

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
        _ = sut.IsDateSelectionEnabled;

        var events = eventArgs.ToArray();
        var trueCount = events.Count(v => v);
        var falseCount = events.Count(v => !v);
        Math.Abs(trueCount - falseCount).Should().BeLessOrEqualTo(1,
            because: "Enable and Disable wins must be balanced (state machine alternation invariant)");

        events.Length.Should().BeInRange(0, 2 * threadCount);
    }
}
