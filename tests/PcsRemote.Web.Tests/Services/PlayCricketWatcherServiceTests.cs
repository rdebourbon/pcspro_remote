using FluentAssertions;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web.Services;

namespace PcsRemote.Web.Tests.Services;

[TestClass]
public sealed class PlayCricketWatcherServiceTests
{
    private static (PlayCricketWatcherService Sut, Mock<IPcsProAutomationService> AutomationMock) CreateSut()
    {
        var mock = new Mock<IPcsProAutomationService>();
        mock.SetupAdd(m => m.StateChanged += It.IsAny<EventHandler<PcsProState>>());
        mock.SetupRemove(m => m.StateChanged -= It.IsAny<EventHandler<PcsProState>>());
        return (new PlayCricketWatcherService(mock.Object), mock);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-1  Enable when disabled sets enabled and fires event
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Enable_WhenDisabled_SetsEnabledAndFiresEvent()
    {
        var (sut, _) = CreateSut();
        var events = new List<AutoWatchEnabledChangedSnapshot>();
        sut.AutoWatchEnabledChanged += (_, s) => events.Add(s);

        sut.Enable();

        sut.IsEnabled.Should().BeTrue();
        events.Should().ContainSingle().Which.IsEnabled.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-2  Enable when already enabled is a no-op
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Enable_WhenAlreadyEnabled_IsNoOp()
    {
        var (sut, _) = CreateSut();
        var eventCount = 0;
        sut.AutoWatchEnabledChanged += (_, _) => eventCount++;

        sut.Enable();
        sut.Enable();

        eventCount.Should().Be(1);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-3  Enable clears dismissed fixtures
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Enable_ClearsDismissedFixtures()
    {
        var (sut, _) = CreateSut();
        sut.DismissFixture(42);

        sut.Enable();

        sut.IsFixtureDismissed(42).Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-4  Disable when enabled sets disabled and fires event
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Disable_WhenEnabled_SetsDisabledAndFiresEvent()
    {
        var (sut, _) = CreateSut();
        var events = new List<AutoWatchEnabledChangedSnapshot>();
        sut.Enable();
        sut.AutoWatchEnabledChanged += (_, s) => events.Add(s);

        sut.Disable();

        sut.IsEnabled.Should().BeFalse();
        events.Should().ContainSingle().Which.IsEnabled.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-5  Disable when already disabled is a no-op
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Disable_WhenAlreadyDisabled_IsNoOp()
    {
        var (sut, _) = CreateSut();
        var eventCount = 0;
        sut.AutoWatchEnabledChanged += (_, _) => eventCount++;

        sut.Disable();
        sut.Disable();

        eventCount.Should().Be(0);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-6  Disable with active countdown cancels it then fires disabled
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Disable_WithActiveCountdown_CancelsCountdownThenFiresDisabled()
    {
        var (sut, _) = CreateSut();
        var order = new List<string>();
        sut.CountdownCancelled += (_, _) => order.Add("cancelled");
        sut.AutoWatchEnabledChanged += (_, s) => order.Add($"enabled={s.IsEnabled}");

        sut.Enable();
        sut.SetCurrentFixtureId(42);
        sut.StartCountdown(TimeSpan.FromMinutes(5));
        sut.Disable();

        sut.CountdownRemaining.Should().BeNull();
        sut.IsFixtureDismissed(42).Should().BeTrue();
        order.Should().Equal("cancelled", "enabled=False");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-7  Disable with no countdown fires no cancel event and no dismissal
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Disable_WithNoCountdown_NoCancelEvent()
    {
        var (sut, _) = CreateSut();
        var cancelFired = false;
        var enabledEvents = new List<AutoWatchEnabledChangedSnapshot>();
        sut.CountdownCancelled += (_, _) => cancelFired = true;
        sut.AutoWatchEnabledChanged += (_, s) => enabledEvents.Add(s);

        sut.Enable();
        sut.SetCurrentFixtureId(42);
        sut.Disable();

        cancelFired.Should().BeFalse();
        enabledEvents.Should().ContainSingle().Which.IsEnabled.Should().BeFalse();
        sut.IsFixtureDismissed(42).Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-8  StartCountdown when no countdown active starts and fires event (null fixture)
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void StartCountdown_WhenNoCountdownActive_StartsAndFiresEvent()
    {
        var (sut, _) = CreateSut();
        var snapshots = new List<CountdownStartedSnapshot>();
        sut.CountdownStarted += (_, s) => snapshots.Add(s);

        sut.StartCountdown(TimeSpan.FromMinutes(5));

        sut.CountdownRemaining.Should().Be(TimeSpan.FromMinutes(5));
        snapshots.Should().ContainSingle();
        snapshots[0].InitialDuration.Should().Be(TimeSpan.FromMinutes(5));
        snapshots[0].FixtureId.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-9  StartCountdown when countdown already active is a no-op
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void StartCountdown_WhenCountdownAlreadyActive_IsNoOp()
    {
        var (sut, _) = CreateSut();
        var eventCount = 0;
        sut.CountdownStarted += (_, _) => eventCount++;

        sut.StartCountdown(TimeSpan.FromMinutes(5));
        sut.StartCountdown(TimeSpan.FromMinutes(3));

        sut.CountdownRemaining.Should().Be(TimeSpan.FromMinutes(5));
        eventCount.Should().Be(1);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-10  TickCountdown with active countdown decrements and returns true
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void TickCountdown_WithActiveCountdown_DecrementsAndReturnsTrue()
    {
        var (sut, _) = CreateSut();
        sut.StartCountdown(TimeSpan.FromSeconds(10));

        var result = sut.TickCountdown();

        result.Should().BeTrue();
        sut.CountdownRemaining.Should().Be(TimeSpan.FromSeconds(9));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-11  TickCountdown with no countdown returns false
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void TickCountdown_WithNoCountdown_ReturnsFalse()
    {
        var (sut, _) = CreateSut();

        var result = sut.TickCountdown();

        result.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-12  TickCountdown when countdown expires fires event, clears remaining, does not dismiss
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void TickCountdown_WhenCountdownExpires_FiresExpiredEventAndClearsRemaining()
    {
        var (sut, _) = CreateSut();
        var expiredFired = false;
        sut.CountdownExpired += (_, _) => expiredFired = true;

        sut.SetCurrentFixtureId(42);
        sut.StartCountdown(TimeSpan.FromSeconds(1));
        var result = sut.TickCountdown();

        result.Should().BeTrue();
        expiredFired.Should().BeTrue();
        sut.CountdownRemaining.Should().BeNull();
        sut.IsFixtureDismissed(42).Should().BeFalse("expiry does not dismiss the fixture");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-13  TickCountdown crossing T-60 fires warning when eligible
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void TickCountdown_CrossesT60_FiresWarningWhenEligible()
    {
        var (sut, _) = CreateSut();
        var t60Count = 0;
        sut.AutoCloseT60Warning += (_, _) => t60Count++;

        sut.StartCountdown(TimeSpan.FromSeconds(62));
        sut.TickCountdown(); // 61s remaining
        sut.TickCountdown(); // 60s remaining — crosses threshold

        t60Count.Should().Be(1);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-14  TickCountdown T-60 suppressed when duration at or below 60 seconds
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void TickCountdown_T60SuppressedWhenDurationAtOrBelow60()
    {
        var (sut, _) = CreateSut();
        var t60Fired = false;
        sut.AutoCloseT60Warning += (_, _) => t60Fired = true;

        sut.StartCountdown(TimeSpan.FromSeconds(60));
        sut.TickCountdown(); // 59s remaining

        t60Fired.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-15  CancelCountdown with active countdown clears remaining and fires event
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CancelCountdown_WithActiveCountdown_ClearsAndFiresEvent()
    {
        var (sut, _) = CreateSut();
        var cancelFired = false;
        sut.CountdownCancelled += (_, _) => cancelFired = true;

        sut.StartCountdown(TimeSpan.FromMinutes(5));
        sut.CancelCountdown();

        sut.CountdownRemaining.Should().BeNull();
        cancelFired.Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-16  CancelCountdown dismisses current fixture
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CancelCountdown_DismissesCurrentFixture()
    {
        var (sut, _) = CreateSut();
        sut.SetCurrentFixtureId(42);
        sut.StartCountdown(TimeSpan.FromMinutes(5));

        sut.CancelCountdown();

        sut.IsFixtureDismissed(42).Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-17  CancelCountdown with no countdown is a no-op
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CancelCountdown_WithNoCountdown_IsNoOp()
    {
        var (sut, _) = CreateSut();
        var cancelFired = false;
        sut.CountdownCancelled += (_, _) => cancelFired = true;

        sut.CancelCountdown();

        cancelFired.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-18  RecordAutoLoadSuppression then lookup returns true
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void RecordAutoLoadSuppression_ThenLookup_ReturnsTrue()
    {
        var (sut, _) = CreateSut();

        sut.RecordAutoLoadSuppression("match-1");

        sut.IsAutoLoadSuppressed("match-1").Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-19  IsAutoLoadSuppressed for unknown match ID returns false
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void IsAutoLoadSuppressed_UnknownMatchId_ReturnsFalse()
    {
        var (sut, _) = CreateSut();

        sut.IsAutoLoadSuppressed("match-1").Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-20  ClearAutoLoadSuppressions clears all records
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ClearAutoLoadSuppressions_ClearsAllRecords()
    {
        var (sut, _) = CreateSut();
        sut.RecordAutoLoadSuppression("match-1");

        sut.ClearAutoLoadSuppressions();

        sut.IsAutoLoadSuppressed("match-1").Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-21  DismissFixture then lookup returns true
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void DismissFixture_ThenLookup_ReturnsTrue()
    {
        var (sut, _) = CreateSut();

        sut.DismissFixture(99);

        sut.IsFixtureDismissed(99).Should().BeTrue();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-22  IsFixtureDismissed for unknown fixture returns false
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void IsFixtureDismissed_UnknownFixture_ReturnsFalse()
    {
        var (sut, _) = CreateSut();

        sut.IsFixtureDismissed(99).Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-23  SetCurrentFixtureId fires FixtureIdChanged event
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void SetCurrentFixtureId_FiresFixtureIdChangedEvent()
    {
        var (sut, _) = CreateSut();
        var snapshots = new List<FixtureIdChangedSnapshot>();
        sut.FixtureIdChanged += (_, s) => snapshots.Add(s);

        sut.SetCurrentFixtureId(42);

        sut.CurrentFixtureId.Should().Be(42);
        snapshots.Should().ContainSingle().Which.FixtureId.Should().Be(42);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-24  SetCurrentFixtureId null clears fixture ID
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void SetCurrentFixtureId_Null_ClearsFixtureId()
    {
        var (sut, _) = CreateSut();
        var snapshots = new List<FixtureIdChangedSnapshot>();
        sut.SetCurrentFixtureId(42);
        sut.FixtureIdChanged += (_, s) => snapshots.Add(s);

        sut.SetCurrentFixtureId(null);

        sut.CurrentFixtureId.Should().BeNull();
        snapshots.Should().ContainSingle().Which.FixtureId.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-25  StateChanged away from MatchLoaded clears fixture ID
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void StateChanged_AwayFromMatchLoaded_ClearsFixtureId()
    {
        var (sut, mock) = CreateSut();
        sut.SetCurrentFixtureId(42);
        var snapshots = new List<FixtureIdChangedSnapshot>();
        sut.FixtureIdChanged += (_, s) => snapshots.Add(s);

        mock.Raise(m => m.StateChanged += null, PcsProState.NotRunning);

        sut.CurrentFixtureId.Should().BeNull();
        snapshots.Should().ContainSingle().Which.FixtureId.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-26  StateChanged to MatchLoaded does not clear fixture ID
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void StateChanged_ToMatchLoaded_DoesNotClearFixtureId()
    {
        var (sut, mock) = CreateSut();
        sut.SetCurrentFixtureId(42);
        var fixtureChangedFired = false;
        sut.FixtureIdChanged += (_, _) => fixtureChangedFired = true;

        mock.Raise(m => m.StateChanged += null, PcsProState.MatchLoaded);

        sut.CurrentFixtureId.Should().Be(42);
        fixtureChangedFired.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-27  Concurrent Enable/Disable events alternate — no duplicate-value transitions
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ConcurrentEnableDisable_DoesNotLoseEvents()
    {
        var (sut, _) = CreateSut();
        bool? lastValue = null;
        var inconsistencyDetected = false;
        var lockObj = new object();

        sut.AutoWatchEnabledChanged += (_, snapshot) =>
        {
            lock (lockObj)
            {
                if (lastValue.HasValue && lastValue.Value == snapshot.IsEnabled)
                    inconsistencyDetected = true;
                lastValue = snapshot.IsEnabled;
            }
        };

        var barrier = new Barrier(10);
        var threads = Enumerable.Range(0, 10).Select(_ => new Thread(() =>
        {
            barrier.SignalAndWait();
            sut.Enable();
            sut.Disable();
        })).ToList();

        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        inconsistencyDetected.Should().BeFalse("events must only fire on actual state transitions");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-28  SetCurrentFixtureId same value is a no-op
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void SetCurrentFixtureId_SameValueAsExisting_IsNoOp()
    {
        var (sut, _) = CreateSut();
        var eventCount = 0;
        sut.FixtureIdChanged += (_, _) => eventCount++;

        sut.SetCurrentFixtureId(42);
        sut.SetCurrentFixtureId(42);

        eventCount.Should().Be(1);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-29  CancelCountdown with no fixture ID clears remaining without dismiss
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CancelCountdown_WithActiveCountdown_WhenNoFixtureId_ClearsAndFiresEventWithoutDismiss()
    {
        var (sut, _) = CreateSut();
        var cancelFired = false;
        sut.CountdownCancelled += (_, _) => cancelFired = true;

        sut.StartCountdown(TimeSpan.FromMinutes(5));
        sut.CancelCountdown();

        sut.CountdownRemaining.Should().BeNull();
        cancelFired.Should().BeTrue();
        // Any fixture ID that might have been set is not dismissed — fixture ID was never set
        sut.IsFixtureDismissed(1).Should().BeFalse();
        sut.IsFixtureDismissed(0).Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-30  Concurrent tick and cancel — exactly one terminal event fires
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ConcurrentTickAndCancel_DoesNotCorruptState()
    {
        var (sut, _) = CreateSut();
        var cancelledCount = 0;
        var expiredCount = 0;
        sut.CountdownCancelled += (_, _) => Interlocked.Increment(ref cancelledCount);
        sut.CountdownExpired += (_, _) => Interlocked.Increment(ref expiredCount);

        sut.StartCountdown(TimeSpan.FromSeconds(5));

        var tickThreads = Enumerable.Range(0, 5).Select(_ => new Thread(() =>
        {
            for (var i = 0; i < 20; i++) sut.TickCountdown();
        })).ToList();
        var cancelThreads = Enumerable.Range(0, 3).Select(_ => new Thread(() =>
        {
            sut.CancelCountdown();
        })).ToList();

        var allThreads = tickThreads.Concat(cancelThreads).ToList();
        allThreads.ForEach(t => t.Start());
        allThreads.ForEach(t => t.Join());

        sut.CountdownRemaining.Should().BeNull();
        cancelledCount.Should().BeLessThanOrEqualTo(1);
        expiredCount.Should().BeLessThanOrEqualTo(1);
        (cancelledCount + expiredCount).Should().Be(1, "exactly one terminal event must fire");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-31  SetCurrentFixtureId null when already null is a no-op
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void SetCurrentFixtureId_NullWhenAlreadyNull_IsNoOp()
    {
        var (sut, _) = CreateSut();
        var fixtureChangedFired = false;
        sut.FixtureIdChanged += (_, _) => fixtureChangedFired = true;

        sut.SetCurrentFixtureId(null);

        fixtureChangedFired.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-32  Dispose unsubscribes from StateChanged
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Dispose_UnsubscribesFromStateChanged()
    {
        var (sut, mock) = CreateSut();
        sut.SetCurrentFixtureId(42);
        var fixtureChangedFired = false;
        sut.FixtureIdChanged += (_, _) => fixtureChangedFired = true;

        sut.Dispose();
        mock.Raise(m => m.StateChanged += null, PcsProState.NotRunning);

        sut.CurrentFixtureId.Should().Be(42, "Dispose must unsubscribe from StateChanged");
        fixtureChangedFired.Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-33  StartCountdown snapshot contains fixture ID when set
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void StartCountdown_WithFixtureIdSet_SnapshotContainsFixtureId()
    {
        var (sut, _) = CreateSut();
        var snapshots = new List<CountdownStartedSnapshot>();
        sut.CountdownStarted += (_, s) => snapshots.Add(s);

        sut.SetCurrentFixtureId(42);
        sut.StartCountdown(TimeSpan.FromMinutes(5));

        snapshots.Should().ContainSingle();
        snapshots[0].InitialDuration.Should().Be(TimeSpan.FromMinutes(5));
        snapshots[0].FixtureId.Should().Be(42);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TC-34  SetCurrentFixtureId different non-null value fires event with new value
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void SetCurrentFixtureId_DifferentNonNullValue_FiresEventWithNewValue()
    {
        var (sut, _) = CreateSut();
        var snapshots = new List<FixtureIdChangedSnapshot>();
        sut.FixtureIdChanged += (_, s) => snapshots.Add(s);

        sut.SetCurrentFixtureId(99);
        sut.SetCurrentFixtureId(42);

        snapshots.Should().HaveCount(2);
        snapshots[1].FixtureId.Should().Be(42);
        sut.CurrentFixtureId.Should().Be(42);
    }
}
