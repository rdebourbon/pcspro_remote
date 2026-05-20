using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web;
using PcsRemote.Web.Shared;

namespace PcsRemote.Web.Tests.Shared;

[TestClass]
public sealed class PlayCricketCountdownBannerTests
{
    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static Mock<IPlayCricketWatcherService> BuildWatcherMock(TimeSpan? remaining)
    {
        var mock = new Mock<IPlayCricketWatcherService>();
        mock.Setup(w => w.CountdownRemaining).Returns(remaining);
        return mock;
    }

    private static (IRenderedComponent<PlayCricketCountdownBanner> Cut, BunitContext Ctx)
    Render(Mock<IPlayCricketWatcherService> watcherMock, FakePeriodicTimer? fakeTimer = null)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(watcherMock.Object);

        fakeTimer ??= new FakePeriodicTimer();
        ctx.Services.AddSingleton<Func<IPeriodicTimer>>(_ => () => fakeTimer);

        var cut = ctx.Render<PlayCricketCountdownBanner>();
        return (cut, ctx);
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-8 — Banner absent on mount when CountdownRemaining is null
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Render_NoActiveCountdown_BannerAbsentFromDom()
    {
        var watcherMock = BuildWatcherMock(null);
        var (cut, ctx) = Render(watcherMock);
        using (ctx)
        {
            cut.FindAll(".play-cricket-countdown-banner").Should().BeEmpty();
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-9 — Banner visible on mount when countdown already active (late-join)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Render_CountdownAlreadyActive_BannerVisibleWithFormattedTime()
    {
        var watcherMock = BuildWatcherMock(TimeSpan.FromMinutes(5));
        var (cut, ctx) = Render(watcherMock);
        using (ctx)
        {
            var banner = cut.Find(".play-cricket-countdown-banner");
            banner.Should().NotBeNull();
            cut.Find(".play-cricket-countdown-time").TextContent.Should().Be("05:00");
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-10 — CountdownStarted event makes banner appear with correct time
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CountdownStarted_BannerAppearsWithFormattedTime()
    {
        var watcherMock = BuildWatcherMock(null);
        var (cut, ctx) = Render(watcherMock);
        using (ctx)
        {
            cut.FindAll(".play-cricket-countdown-banner").Should().BeEmpty();

            // Simulate service transitioning to active countdown before raising the event.
            watcherMock.Setup(w => w.CountdownRemaining).Returns(TimeSpan.FromSeconds(90));
            watcherMock.Raise(
                w => w.CountdownStarted += null,
                this,
                new CountdownStartedSnapshot(TimeSpan.FromSeconds(90), null));

            cut.WaitForAssertion(() =>
            {
                cut.Find(".play-cricket-countdown-banner").Should().NotBeNull();
                cut.Find(".play-cricket-countdown-time").TextContent.Should().Be("01:30");
            });
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-11 — Timer tick updates the displayed remaining time
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task TimerTick_UpdatesDisplayedTime()
    {
        var fakeTimer = new FakePeriodicTimer();
        var watcherMock = BuildWatcherMock(TimeSpan.FromSeconds(300));
        var (cut, ctx) = Render(watcherMock, fakeTimer);
        using (ctx)
        {
            // Wait until the loop is ready for a tick.
            await fakeTimer.WaitingForTickAsync();

            // Simulate one second elapsed.
            watcherMock.Setup(w => w.CountdownRemaining).Returns(TimeSpan.FromSeconds(299));
            fakeTimer.TriggerTick();

            cut.WaitForAssertion(() =>
                cut.Find(".play-cricket-countdown-time").TextContent.Should().Be("04:59"));
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-12 — Timer tick when CountdownRemaining returns null hides the banner
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task TimerTick_NullRemaining_BannerHides()
    {
        var fakeTimer = new FakePeriodicTimer();
        var watcherMock = BuildWatcherMock(TimeSpan.FromSeconds(10));
        var (cut, ctx) = Render(watcherMock, fakeTimer);
        using (ctx)
        {
            await fakeTimer.WaitingForTickAsync();

            watcherMock.Setup(w => w.CountdownRemaining).Returns((TimeSpan?)null);
            fakeTimer.TriggerTick();

            cut.WaitForAssertion(() =>
                cut.FindAll(".play-cricket-countdown-banner").Should().BeEmpty());
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-13 — CountdownCancelled event hides banner immediately
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CountdownCancelled_HidesBannerImmediately()
    {
        var watcherMock = BuildWatcherMock(TimeSpan.FromSeconds(30));
        var (cut, ctx) = Render(watcherMock);
        using (ctx)
        {
            cut.Find(".play-cricket-countdown-banner").Should().NotBeNull();

            watcherMock.Raise(w => w.CountdownCancelled += null, this, EventArgs.Empty);

            cut.WaitForAssertion(() =>
                cut.FindAll(".play-cricket-countdown-banner").Should().BeEmpty());
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-14 — CountdownExpired event hides banner immediately
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CountdownExpired_HidesBannerImmediately()
    {
        var watcherMock = BuildWatcherMock(TimeSpan.FromSeconds(30));
        var (cut, ctx) = Render(watcherMock);
        using (ctx)
        {
            cut.Find(".play-cricket-countdown-banner").Should().NotBeNull();

            watcherMock.Raise(w => w.CountdownExpired += null, this, EventArgs.Empty);

            cut.WaitForAssertion(() =>
                cut.FindAll(".play-cricket-countdown-banner").Should().BeEmpty());
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-15 — Cancel button calls CancelCountdown() on the watcher service
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CancelButton_Click_CallsCancelCountdownOnce()
    {
        var watcherMock = BuildWatcherMock(TimeSpan.FromSeconds(30));
        var (cut, ctx) = Render(watcherMock);
        using (ctx)
        {
            cut.Find(".play-cricket-countdown-cancel").Click();

            watcherMock.Verify(w => w.CancelCountdown(), Times.Once);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-16 — Dispose stops the timer and unsubscribes from all three events
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Dispose_StopsTimerAndUnsubscribesAllEvents()
    {
        var fakeTimer = new FakePeriodicTimer();
        var watcherMock = BuildWatcherMock(TimeSpan.FromSeconds(60));
        var (cut, ctx) = Render(watcherMock, fakeTimer);
        using (ctx)
        {
            // Wait for the timer loop to enter its wait state.
            await fakeTimer.WaitingForTickAsync();

            cut.Instance.Dispose();

            // Timer must be disposed (via await using in the loop) after CTS cancellation.
            await fakeTimer.WhenDisposed.WaitAsync(TimeSpan.FromSeconds(5));

            watcherMock.VerifyRemove(
                m => m.CountdownStarted -= It.IsAny<EventHandler<CountdownStartedSnapshot>>(),
                Times.Once);
            watcherMock.VerifyRemove(
                m => m.CountdownCancelled -= It.IsAny<EventHandler>(),
                Times.Once);
            watcherMock.VerifyRemove(
                m => m.CountdownExpired -= It.IsAny<EventHandler>(),
                Times.Once);

            // Post-disposal events must not throw.
            var act = () => watcherMock.Raise(w => w.CountdownCancelled += null, this, EventArgs.Empty);
            act.Should().NotThrow();
        }
    }
}
