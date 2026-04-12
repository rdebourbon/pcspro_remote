using FluentAssertions;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web;

namespace PcsRemote.Web.Tests;

[TestClass]
public sealed class ScoreboardServiceTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static readonly byte[] SampleImage = [0x01, 0x02, 0x03];
    private static readonly byte[] DifferentImage = [0x04, 0x05, 0x06];

    private static (ScoreboardService Service, Mock<IPcsProAutomationService> Mock) Build()
    {
        var mock = new Mock<IPcsProAutomationService>();
        var svc = new ScoreboardService(mock.Object);
        return (svc, mock);
    }

    // ── CaptureAndBroadcastAsync ─────────────────────────────────────────────

    [TestMethod]
    public async Task CaptureAndBroadcastAsync_NewImage_FiresScoreboardUpdated()
    {
        var (svc, mock) = Build();
        mock.Setup(s => s.CaptureScoreboardImageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleImage);

        byte[]? received = null;
        svc.ScoreboardUpdated += (_, img) => received = img;

        await svc.CaptureAndBroadcastAsync();

        received.Should().Equal(SampleImage);
    }

    [TestMethod]
    public async Task CaptureAndBroadcastAsync_SameImageTwice_FiresEventOnce()
    {
        var (svc, mock) = Build();
        mock.Setup(s => s.CaptureScoreboardImageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleImage);

        int fireCount = 0;
        svc.ScoreboardUpdated += (_, _) => fireCount++;

        await svc.CaptureAndBroadcastAsync();
        await svc.CaptureAndBroadcastAsync();

        fireCount.Should().Be(1);
    }

    [TestMethod]
    public async Task CaptureAndBroadcastAsync_ChangedImage_FiresEventAgain()
    {
        var (svc, mock) = Build();
        mock.SetupSequence(s => s.CaptureScoreboardImageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleImage)
            .ReturnsAsync(DifferentImage);

        var received = new List<byte[]>();
        svc.ScoreboardUpdated += (_, img) => received.Add(img);

        await svc.CaptureAndBroadcastAsync();
        await svc.CaptureAndBroadcastAsync();

        received.Should().HaveCount(2);
        received[0].Should().Equal(SampleImage);
        received[1].Should().Equal(DifferentImage);
    }

    [TestMethod]
    public async Task CaptureAndBroadcastAsync_NewImage_UpdatesCurrentImage()
    {
        var (svc, mock) = Build();
        mock.Setup(s => s.CaptureScoreboardImageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleImage);

        await svc.CaptureAndBroadcastAsync();

        svc.CurrentImage.Should().Equal(SampleImage);
    }

    [TestMethod]
    public async Task CaptureAndBroadcastAsync_PassesCancellationTokenToAutomationService()
    {
        var (svc, mock) = Build();
        using var cts = new CancellationTokenSource();
        CancellationToken captured = default;

        mock.Setup(s => s.CaptureScoreboardImageAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(ct => captured = ct)
            .ReturnsAsync(SampleImage);

        await svc.CaptureAndBroadcastAsync(cts.Token);

        captured.Should().Be(cts.Token);
    }

    // ── ForceRefreshAsync ────────────────────────────────────────────────────

    [TestMethod]
    public async Task ForceRefreshAsync_SameImageAsPrevious_StillFiresScoreboardUpdatedAndRefreshCompleted()
    {
        var (svc, mock) = Build();
        mock.Setup(s => s.CaptureScoreboardImageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleImage);

        // Prime the cache with the same image.
        await svc.CaptureAndBroadcastAsync();

        int updatedCount = 0;
        int completedCount = 0;
        svc.ScoreboardUpdated += (_, _) => updatedCount++;
        svc.RefreshCompleted += (_, _) => completedCount++;

        await svc.ForceRefreshAsync();

        updatedCount.Should().Be(1);
        completedCount.Should().Be(1);
    }

    [TestMethod]
    public async Task ForceRefreshAsync_FiresScoreboardUpdatedBeforeRefreshCompleted()
    {
        var (svc, mock) = Build();
        mock.Setup(s => s.CaptureScoreboardImageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleImage);

        var order = new List<string>();
        svc.ScoreboardUpdated += (_, _) => order.Add("ScoreboardUpdated");
        svc.RefreshCompleted += (_, _) => order.Add("RefreshCompleted");

        await svc.ForceRefreshAsync();

        order.Should().Equal("ScoreboardUpdated", "RefreshCompleted");
    }

    // ── ClearCache ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ClearCache_ResetsCurrentImageToNull()
    {
        var (svc, mock) = Build();
        mock.Setup(s => s.CaptureScoreboardImageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleImage);

        await svc.CaptureAndBroadcastAsync();
        svc.ClearCache();

        svc.CurrentImage.Should().BeNull();
    }

    [TestMethod]
    public async Task ClearCache_AllowsNextCaptureToFireEvent()
    {
        var (svc, mock) = Build();
        mock.Setup(s => s.CaptureScoreboardImageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleImage);

        // Prime cache then clear.
        await svc.CaptureAndBroadcastAsync();
        svc.ClearCache();

        int fireCount = 0;
        svc.ScoreboardUpdated += (_, _) => fireCount++;

        // Same bytes should fire because cache was cleared.
        await svc.CaptureAndBroadcastAsync();

        fireCount.Should().Be(1);
    }

    // ── Null / empty guard ───────────────────────────────────────────────────

    [TestMethod]
    public async Task CaptureAndBroadcastAsync_NullBytesFromCapture_DoesNotFireEventAndDoesNotUpdateCache()
    {
        var (svc, mock) = Build();
        mock.Setup(s => s.CaptureScoreboardImageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[])null!); // defensive null test — interface is non-nullable but runtime can still return null

        int fireCount = 0;
        svc.ScoreboardUpdated += (_, _) => fireCount++;

        Func<Task> act = () => svc.CaptureAndBroadcastAsync();

        await act.Should().NotThrowAsync();
        fireCount.Should().Be(0);
        svc.CurrentImage.Should().BeNull();
    }

    [TestMethod]
    public async Task CaptureAndBroadcastAsync_EmptyBytesFromCapture_DoesNotFireEventAndDoesNotUpdateCache()
    {
        var (svc, mock) = Build();
        mock.Setup(s => s.CaptureScoreboardImageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<byte>());

        int fireCount = 0;
        svc.ScoreboardUpdated += (_, _) => fireCount++;

        Func<Task> act = () => svc.CaptureAndBroadcastAsync();

        await act.Should().NotThrowAsync();
        fireCount.Should().Be(0);
        svc.CurrentImage.Should().BeNull();
    }

    // ── ForceRefreshAsync null / empty guard ─────────────────────────────────

    [TestMethod]
    public async Task ForceRefreshAsync_NullBytesFromCapture_DoesNotFireEventsAndDoesNotUpdateCache()
    {
        var (svc, mock) = Build();
        mock.Setup(s => s.CaptureScoreboardImageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[])null!); // defensive null test — interface is non-nullable but runtime can still return null

        int updatedCount = 0;
        int completedCount = 0;
        svc.ScoreboardUpdated += (_, _) => updatedCount++;
        svc.RefreshCompleted += (_, _) => completedCount++;

        Func<Task> act = () => svc.ForceRefreshAsync();

        await act.Should().NotThrowAsync();
        updatedCount.Should().Be(0);
        completedCount.Should().Be(0);
        svc.CurrentImage.Should().BeNull();
    }

    [TestMethod]
    public async Task ForceRefreshAsync_EmptyBytesFromCapture_DoesNotFireEventsAndDoesNotUpdateCache()
    {
        var (svc, mock) = Build();
        mock.Setup(s => s.CaptureScoreboardImageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<byte>());

        int updatedCount = 0;
        int completedCount = 0;
        svc.ScoreboardUpdated += (_, _) => updatedCount++;
        svc.RefreshCompleted += (_, _) => completedCount++;

        Func<Task> act = () => svc.ForceRefreshAsync();

        await act.Should().NotThrowAsync();
        updatedCount.Should().Be(0);
        completedCount.Should().Be(0);
        svc.CurrentImage.Should().BeNull();
    }

    [TestMethod]
    public async Task ForceRefreshAsync_PassesCancellationTokenToAutomationService()
    {
        var (svc, mock) = Build();
        using var cts = new CancellationTokenSource();
        CancellationToken captured = default;

        mock.Setup(s => s.CaptureScoreboardImageAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(ct => captured = ct)
            .ReturnsAsync(SampleImage);

        await svc.ForceRefreshAsync(cts.Token);

        captured.Should().Be(cts.Token);
    }

    // ── ClearCache fires no events ────────────────────────────────────────────

    [TestMethod]
    public async Task ClearCache_DoesNotFireAnyEvents()
    {
        var (svc, mock) = Build();
        mock.Setup(s => s.CaptureScoreboardImageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleImage);

        await svc.CaptureAndBroadcastAsync();

        int updatedCount = 0;
        int completedCount = 0;
        svc.ScoreboardUpdated += (_, _) => updatedCount++;
        svc.RefreshCompleted += (_, _) => completedCount++;

        svc.ClearCache();

        updatedCount.Should().Be(0);
        completedCount.Should().Be(0);
    }

    // ── Exception propagation ─────────────────────────────────────────────────

    [TestMethod]
    public async Task CaptureAndBroadcastAsync_AutomationServiceThrows_PropagatesException()
    {
        var (svc, mock) = Build();
        mock.Setup(s => s.CaptureScoreboardImageAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("automation failure"));

        Func<Task> act = () => svc.CaptureAndBroadcastAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("automation failure");
    }

    [TestMethod]
    public async Task ForceRefreshAsync_AutomationServiceThrows_PropagatesException()
    {
        var (svc, mock) = Build();
        mock.Setup(s => s.CaptureScoreboardImageAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("automation failure"));

        Func<Task> act = () => svc.ForceRefreshAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("automation failure");
    }
}
