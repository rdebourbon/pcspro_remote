using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PcsRemote.Core;
using PcsRemote.TrayHost;
using PcsRemote.YouTube;

namespace PcsRemote.TrayHost.Tests;

[TestClass]
public sealed class YouTubeTokenRefreshServiceTests
{
    // ── TC-1: Ready → calls refresh on each tick ─────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_WhenReady_CallsRefreshOnEachTick()
    {
        // Arrange
        var mockService = new Mock<IYouTubeLiveStreamService>();
        mockService.Setup(s => s.Availability).Returns(YouTubeAvailability.Ready);
        mockService
            .Setup(s => s.RunProactiveRefreshAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var fakeTimer = new FakePeriodicTimer();

        var sut = new YouTubeTokenRefreshService(
            mockService.Object,
            Options.Create(new YouTubeOptions()),
            NullLogger<YouTubeTokenRefreshService>.Instance,
            () => fakeTimer);

        // Act — fire two ticks, then dispose the timer so the loop exits cleanly.
        var runTask = sut.RunAsync(CancellationToken.None);

        await fakeTimer.WaitingForTickAsync().WaitAsync(TimeSpan.FromSeconds(5));
        fakeTimer.TriggerTick(); // tick 1

        await fakeTimer.WaitingForTickAsync().WaitAsync(TimeSpan.FromSeconds(5));
        fakeTimer.TriggerTick(); // tick 2

        await fakeTimer.WaitingForTickAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await fakeTimer.DisposeAsync(); // no more ticks → WaitForNextTickAsync returns false

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        mockService.Verify(
            s => s.RunProactiveRefreshAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "refresh should be called once per tick when availability is Ready");
    }

    // ── TC-2: Not ready → skips refresh ─────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_WhenNotReady_SkipsRefresh()
    {
        // Arrange
        var mockService = new Mock<IYouTubeLiveStreamService>();
        mockService.Setup(s => s.Availability).Returns(YouTubeAvailability.NotConfigured);

        var fakeTimer = new FakePeriodicTimer();

        var sut = new YouTubeTokenRefreshService(
            mockService.Object,
            Options.Create(new YouTubeOptions()),
            NullLogger<YouTubeTokenRefreshService>.Instance,
            () => fakeTimer);

        // Act — fire one tick (skipped due to NotConfigured), then dispose.
        var runTask = sut.RunAsync(CancellationToken.None);

        await fakeTimer.WaitingForTickAsync().WaitAsync(TimeSpan.FromSeconds(5));
        fakeTimer.TriggerTick(); // tick — availability not ready, body skipped

        await fakeTimer.WaitingForTickAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await fakeTimer.DisposeAsync();

        await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        mockService.Verify(
            s => s.RunProactiveRefreshAsync(It.IsAny<CancellationToken>()),
            Times.Never,
            "refresh must not be called when availability is not Ready");
    }

    // ── TC-3: Cancellation → stops gracefully ───────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_WhenCancelled_StopsGracefully()
    {
        // Arrange
        var mockService = new Mock<IYouTubeLiveStreamService>();
        mockService.Setup(s => s.Availability).Returns(YouTubeAvailability.Ready);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var fakeTimer = new FakePeriodicTimer();
        var sut = new YouTubeTokenRefreshService(
            mockService.Object,
            Options.Create(new YouTubeOptions()),
            NullLogger<YouTubeTokenRefreshService>.Instance,
            () => fakeTimer);

        // Act — OperationCanceledException is an expected / acceptable exit path for
        // a BackgroundService; no unexpected exception should escape.
        Exception? unexpected = null;
        try
        {
            await sut.RunAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected graceful exit — swallow.
        }
        catch (Exception ex)
        {
            unexpected = ex;
        }

        // Assert
        unexpected.Should().BeNull(
            "ExecuteAsync should stop gracefully when the stopping token is cancelled");
    }
}
