using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web;
using PcsRemote.Web.Shared;

namespace PcsRemote.Web.Tests.Shared;

[TestClass]
public class StreamingControlsTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (
        IRenderedComponent<StreamingControls> Cut,
        Mock<IYouTubeLiveStreamService> StreamMock,
        Mock<IOperationCoordinatorService> CoordinatorMock,
        Mock<IConfirmDialogService> DialogMock,
        BunitContext Ctx)
    Build(
        LiveStreamStatus initialStatus = LiveStreamStatus.Idle,
        LiveBroadcastInfo? broadcast = null,
        bool operationInProgress = false,
        bool? confirmResult = true)
    {
        var streamMock = new Mock<IYouTubeLiveStreamService>();
        streamMock.Setup(s => s.CurrentStatus).Returns(initialStatus);
        streamMock.Setup(s => s.CurrentBroadcast).Returns(broadcast);

        var coordinatorMock = new Mock<IOperationCoordinatorService>();
        coordinatorMock.Setup(c => c.IsOperationInProgress).Returns(operationInProgress);
        coordinatorMock
            .Setup(c => c.BeginOperation(It.IsAny<string?>()))
            .Callback(() => coordinatorMock.Raise(
                c => c.OperationInProgressChanged += null, coordinatorMock.Object, true))
            .Returns(true);
        coordinatorMock
            .Setup(c => c.MarkComplete())
            .Raises(c => c.OperationInProgressChanged += null, coordinatorMock.Object, false);

        var dialogMock = new Mock<IConfirmDialogService>();
        dialogMock.Setup(d => d.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(confirmResult);

        var ctx = new BunitContext();
        ctx.Services.AddSingleton(streamMock.Object);
        ctx.Services.AddSingleton(coordinatorMock.Object);
        ctx.Services.AddSingleton(dialogMock.Object);

        var cut = ctx.Render<StreamingControls>();
        return (cut, streamMock, coordinatorMock, dialogMock, ctx);
    }

    private static readonly LiveBroadcastInfo TestBroadcast = new(
        "broadcast-1", "HHCC 1st XI vs Away Team", "https://youtube.com/watch?v=abc123");

    // ── AC-2: Start button visible and enabled when Idle ─────────────────────

    [TestMethod]
    public void StartButton_WhenIdle_IsEnabled()
    {
        var (cut, _, _, _, ctx) = Build(LiveStreamStatus.Idle);
        using (ctx)
        {
            var btn = cut.Find(".streaming-controls__btn--start");
            btn.HasAttribute("disabled").Should().BeFalse();
        }
    }

    // ── AC-3: Stop button disabled when Idle ─────────────────────────────────

    [TestMethod]
    public void StopButton_WhenIdle_IsDisabled()
    {
        var (cut, _, _, _, ctx) = Build(LiveStreamStatus.Idle);
        using (ctx)
        {
            var btn = cut.Find(".streaming-controls__btn--stop");
            btn.HasAttribute("disabled").Should().BeTrue();
        }
    }

    // ── AC-4: Start button disabled when Live ────────────────────────────────

    [TestMethod]
    public void StartButton_WhenLive_IsDisabled()
    {
        var (cut, _, _, _, ctx) = Build(LiveStreamStatus.Live, TestBroadcast);
        using (ctx)
        {
            cut.Find(".streaming-controls__btn--start").HasAttribute("disabled").Should().BeTrue();
        }
    }

    // ── AC-5: Stop button enabled when Live ──────────────────────────────────

    [TestMethod]
    public void StopButton_WhenLive_IsEnabled()
    {
        var (cut, _, _, _, ctx) = Build(LiveStreamStatus.Live, TestBroadcast);
        using (ctx)
        {
            cut.Find(".streaming-controls__btn--stop").HasAttribute("disabled").Should().BeFalse();
        }
    }

    // ── AC-6: Click Start calls StartStreamAsync ─────────────────────────────

    [TestMethod]
    public void OnStartClickedAsync_WhenIdle_CallsStartStreamAsync()
    {
        var (cut, streamMock, _, _, ctx) = Build(LiveStreamStatus.Idle);
        using (ctx)
        {
            streamMock.Setup(s => s.StartStreamAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            cut.Find(".streaming-controls__btn--start").Click();

            cut.WaitForAssertion(() =>
                streamMock.Verify(s => s.StartStreamAsync(It.IsAny<CancellationToken>()), Times.Once));
        }
    }

    // ── AC-7: Click Stop calls StopStreamAsync ───────────────────────────────

    [TestMethod]
    public void OnStopClickedAsync_WhenLive_CallsStopStreamAsync()
    {
        var (cut, streamMock, _, _, ctx) = Build(LiveStreamStatus.Live, TestBroadcast);
        using (ctx)
        {
            streamMock.Setup(s => s.StopStreamAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            cut.Find(".streaming-controls__btn--stop").Click();

            cut.WaitForAssertion(() =>
                streamMock.Verify(s => s.StopStreamAsync(It.IsAny<CancellationToken>()), Times.Once));
        }
    }

    // ── AC-8: Cancel button visible during Starting ──────────────────────────

    [TestMethod]
    public void CancelButton_WhenStarting_IsVisible()
    {
        var (cut, _, _, _, ctx) = Build(LiveStreamStatus.Starting);
        using (ctx)
        {
            cut.Find(".streaming-controls__btn--cancel").Should().NotBeNull();
        }
    }

    // ── AC-9: Cancel button NOT visible when Idle ────────────────────────────

    [TestMethod]
    public void CancelButton_WhenIdle_IsNotPresent()
    {
        var (cut, _, _, _, ctx) = Build(LiveStreamStatus.Idle);
        using (ctx)
        {
            cut.FindAll(".streaming-controls__btn--cancel").Should().BeEmpty();
        }
    }

    // ── AC-10: Error state shows error message and dismiss button ────────────

    [TestMethod]
    public void ErrorArea_WhenError_ShowsMessageAndDismissButton()
    {
        var streamMock = new Mock<IYouTubeLiveStreamService>();
        streamMock.Setup(s => s.CurrentStatus).Returns(LiveStreamStatus.Error);
        streamMock.Setup(s => s.CurrentBroadcast).Returns((LiveBroadcastInfo?)null);

        var coordinatorMock = new Mock<IOperationCoordinatorService>();
        coordinatorMock.Setup(c => c.IsOperationInProgress).Returns(false);

        var dialogMock = new Mock<IConfirmDialogService>();

        var ctx = new BunitContext();
        ctx.Services.AddSingleton(streamMock.Object);
        ctx.Services.AddSingleton(coordinatorMock.Object);
        ctx.Services.AddSingleton(dialogMock.Object);

        using (ctx)
        {
            // Simulate error state by raising StatusChanged after render
            var cut = ctx.Render<StreamingControls>();
            streamMock.Raise(s => s.StatusChanged += null, streamMock.Object,
                new StreamStateSnapshot(LiveStreamStatus.Error, null, "Something went wrong"));

            cut.WaitForAssertion(() =>
            {
                cut.Find(".streaming-controls__error-message").TextContent.Should().Be("Something went wrong");
                cut.Find(".streaming-controls__btn--dismiss").Should().NotBeNull();
            });
        }
    }

    // ── AC-11: Dismiss calls ResetAsync ──────────────────────────────────────

    [TestMethod]
    public void OnDismissClickedAsync_WhenError_CallsResetAsync()
    {
        var streamMock = new Mock<IYouTubeLiveStreamService>();
        streamMock.Setup(s => s.CurrentStatus).Returns(LiveStreamStatus.Error);
        streamMock.Setup(s => s.CurrentBroadcast).Returns((LiveBroadcastInfo?)null);
        streamMock.Setup(s => s.ResetAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var coordinatorMock = new Mock<IOperationCoordinatorService>();
        coordinatorMock.Setup(c => c.IsOperationInProgress).Returns(false);

        var dialogMock = new Mock<IConfirmDialogService>();

        var ctx = new BunitContext();
        ctx.Services.AddSingleton(streamMock.Object);
        ctx.Services.AddSingleton(coordinatorMock.Object);
        ctx.Services.AddSingleton(dialogMock.Object);

        using (ctx)
        {
            var cut = ctx.Render<StreamingControls>();
            streamMock.Raise(s => s.StatusChanged += null, streamMock.Object,
                new StreamStateSnapshot(LiveStreamStatus.Error, null, "err"));

            cut.WaitForAssertion(() =>
                cut.Find(".streaming-controls__btn--dismiss").Should().NotBeNull());

            cut.Find(".streaming-controls__btn--dismiss").Click();

            cut.WaitForAssertion(() =>
                streamMock.Verify(s => s.ResetAsync(It.IsAny<CancellationToken>()), Times.Once));
        }
    }

    // ── AC-12: Watch URL shown when Live ─────────────────────────────────────

    [TestMethod]
    public void WatchLink_WhenLive_IsVisible()
    {
        var (cut, _, _, _, ctx) = Build(LiveStreamStatus.Live, TestBroadcast);
        using (ctx)
        {
            var link = cut.Find(".streaming-controls__watch-link");
            link.GetAttribute("href").Should().Be("https://youtube.com/watch?v=abc123");
            link.GetAttribute("target").Should().Be("_blank");
        }
    }

    // ── AC-12b: Watch URL hidden when Idle ───────────────────────────────────

    [TestMethod]
    public void WatchLink_WhenIdle_IsNotPresent()
    {
        var (cut, _, _, _, ctx) = Build(LiveStreamStatus.Idle);
        using (ctx)
        {
            cut.FindAll(".streaming-controls__watch-link").Should().BeEmpty();
        }
    }

    // ── AC-13: StatusChanged updates UI ──────────────────────────────────────

    [TestMethod]
    public void OnStatusChanged_TransitionsToLive_UpdatesUI()
    {
        var (cut, streamMock, _, _, ctx) = Build(LiveStreamStatus.Idle);
        using (ctx)
        {
            streamMock.Raise(s => s.StatusChanged += null, streamMock.Object,
                new StreamStateSnapshot(LiveStreamStatus.Live, TestBroadcast, null));

            cut.WaitForAssertion(() =>
            {
                cut.Find(".streaming-controls__badge").TextContent.Trim().Should().Contain("Live");
                cut.Find(".streaming-controls__watch-link").Should().NotBeNull();
            });
        }
    }

    // ── AC-14: Dispose unsubscribes from StatusChanged ───────────────────────

    [TestMethod]
    public void Dispose_UnsubscribesFromStatusChanged()
    {
        var (cut, streamMock, _, _, ctx) = Build(LiveStreamStatus.Idle);
        using (ctx)
        {
            cut.Instance.Dispose();

            streamMock.VerifyRemove(
                s => s.StatusChanged -= It.IsAny<EventHandler<StreamStateSnapshot>>(),
                Times.Once);
        }
    }

    // ── AC-14b: Dispose unsubscribes from coordinator ────────────────────────

    [TestMethod]
    public void Dispose_UnsubscribesFromCoordinatorEvent()
    {
        var (cut, _, coordinatorMock, _, ctx) = Build(LiveStreamStatus.Idle);
        using (ctx)
        {
            cut.Instance.Dispose();

            coordinatorMock.VerifyRemove(
                c => c.OperationInProgressChanged -= It.IsAny<EventHandler<bool>>(),
                Times.Once);
        }
    }

    // ── AC-15: BeginOperation/MarkComplete lifecycle ─────────────────────────

    [TestMethod]
    public void OnStartClickedAsync_BeginOperationCalledBeforeStartStream()
    {
        var (cut, streamMock, coordinatorMock, _, ctx) = Build(LiveStreamStatus.Idle);
        using (ctx)
        {
            var callOrder = new List<string>();
            coordinatorMock
                .Setup(c => c.BeginOperation(It.IsAny<string?>()))
                .Callback(() => callOrder.Add("BeginOperation"))
                .Returns(true);
            streamMock.Setup(s => s.StartStreamAsync(It.IsAny<CancellationToken>()))
                .Callback(() => callOrder.Add("StartStreamAsync"))
                .Returns(Task.CompletedTask);

            cut.Find(".streaming-controls__btn--start").Click();

            cut.WaitForAssertion(() =>
            {
                callOrder.Should().ContainInOrder("BeginOperation", "StartStreamAsync");
                coordinatorMock.Verify(c => c.MarkComplete(), Times.Once);
            });
        }
    }

    // ── AC-16: OperationInProgress disables Start ────────────────────────────

    [TestMethod]
    public void StartButton_WhenOperationInProgress_IsDisabled()
    {
        var (cut, _, _, _, ctx) = Build(LiveStreamStatus.Idle, operationInProgress: true);
        using (ctx)
        {
            cut.Find(".streaming-controls__btn--start").HasAttribute("disabled").Should().BeTrue();
        }
    }

    // ── AC-16b: OperationInProgress disables Stop ────────────────────────────

    [TestMethod]
    public void StopButton_WhenOperationInProgress_IsDisabled()
    {
        var (cut, _, _, _, ctx) = Build(LiveStreamStatus.Live, TestBroadcast, operationInProgress: true);
        using (ctx)
        {
            cut.Find(".streaming-controls__btn--stop").HasAttribute("disabled").Should().BeTrue();
        }
    }

    // ── AC-17: DI wiring — MockYouTubeLiveStreamService registered ──────────

    [TestMethod]
    public void Render_WithMockStreamService_RendersSuccessfully()
    {
        // Verified by all other tests: the mock is accepted at the
        // IYouTubeLiveStreamService DI slot. This test explicitly
        // confirms a real Mock<IYouTubeLiveStreamService> resolves.
        var (cut, _, _, _, ctx) = Build(LiveStreamStatus.Idle);
        using (ctx)
        {
            cut.Markup.Should().Contain("streaming-controls");
        }
    }

    // ── AC-18: BeginOperation returns false → Start not called ───────────────

    [TestMethod]
    public void OnStartClickedAsync_BeginOperationFails_StartStreamNotCalled()
    {
        var (cut, streamMock, coordinatorMock, _, ctx) = Build(LiveStreamStatus.Idle);
        using (ctx)
        {
            coordinatorMock.Setup(c => c.BeginOperation(It.IsAny<string?>())).Returns(false);

            cut.Find(".streaming-controls__btn--start").Click();

            cut.WaitForAssertion(() =>
            {
                streamMock.Verify(s => s.StartStreamAsync(It.IsAny<CancellationToken>()), Times.Never);
                coordinatorMock.Verify(c => c.MarkComplete(), Times.Never);
            });
        }
    }

    // ── AC-19: Exception in StartStreamAsync → MarkComplete still called ─────

    [TestMethod]
    public void OnStartClickedAsync_ServiceThrows_MarkCompleteCalledInFinally()
    {
        var (cut, streamMock, coordinatorMock, _, ctx) = Build(LiveStreamStatus.Idle);
        using (ctx)
        {
            streamMock.Setup(s => s.StartStreamAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

            cut.Find(".streaming-controls__btn--start").Click();

            cut.WaitForAssertion(() =>
                coordinatorMock.Verify(c => c.MarkComplete(), Times.Once));
        }
    }

    // ── AC-19b: Exception in StartStreamAsync → error state surfaced ─────────

    [TestMethod]
    public void OnStartClickedAsync_ServiceThrows_TransitionsToErrorState()
    {
        var (cut, streamMock, _, _, ctx) = Build(LiveStreamStatus.Idle);
        using (ctx)
        {
            streamMock.Setup(s => s.StartStreamAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("stream failed"));

            cut.Find(".streaming-controls__btn--start").Click();

            cut.WaitForAssertion(() =>
            {
                cut.Find(".streaming-controls__error-message").TextContent.Should().Be("stream failed");
                cut.Find(".streaming-controls__badge").ClassList.Should().Contain("streaming-controls__badge--error");
            });
        }
    }

    // ── AC-19c: Exception in StopStreamAsync → MarkComplete still called ─────

    [TestMethod]
    public void OnStopClickedAsync_ServiceThrows_MarkCompleteCalledInFinally()
    {
        var (cut, streamMock, coordinatorMock, _, ctx) = Build(LiveStreamStatus.Live, TestBroadcast);
        using (ctx)
        {
            streamMock.Setup(s => s.StopStreamAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("stop failed"));

            cut.Find(".streaming-controls__btn--stop").Click();

            cut.WaitForAssertion(() =>
                coordinatorMock.Verify(c => c.MarkComplete(), Times.Once));
        }
    }

    // ── AC-19d: Exception in StopStreamAsync → error state surfaced ──────────

    [TestMethod]
    public void OnStopClickedAsync_ServiceThrows_TransitionsToErrorState()
    {
        var (cut, streamMock, _, _, ctx) = Build(LiveStreamStatus.Live, TestBroadcast);
        using (ctx)
        {
            streamMock.Setup(s => s.StopStreamAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("stop failed"));

            cut.Find(".streaming-controls__btn--stop").Click();

            cut.WaitForAssertion(() =>
            {
                cut.Find(".streaming-controls__error-message").TextContent.Should().Be("stop failed");
                cut.Find(".streaming-controls__badge").ClassList.Should().Contain("streaming-controls__badge--error");
            });
        }
    }

    // ── AC-20: Status badge text and CSS class per status ───────────────────

    [TestMethod]
    [DataRow(LiveStreamStatus.Idle, "Idle", "streaming-controls__badge--idle")]
    [DataRow(LiveStreamStatus.Starting, "Starting", "streaming-controls__badge--starting")]
    [DataRow(LiveStreamStatus.Live, "Live", "streaming-controls__badge--live")]
    [DataRow(LiveStreamStatus.Stopping, "Stopping", "streaming-controls__badge--stopping")]
    [DataRow(LiveStreamStatus.Error, "Error", "streaming-controls__badge--error")]
    public void StatusBadge_ForEachStatus_ShowsCorrectTextAndClass(LiveStreamStatus status, string expectedText, string expectedClass)
    {
        var broadcast = status == LiveStreamStatus.Live ? TestBroadcast : null;
        var (cut, _, _, _, ctx) = Build(status, broadcast);
        using (ctx)
        {
            var badge = cut.Find(".streaming-controls__badge");
            badge.TextContent.Trim().Should().Contain(expectedText);
            badge.ClassList.Should().Contain(expectedClass);
        }
    }

    // ── AC-21: Coordinator fires in-progress change → UI updates ─────────────

    [TestMethod]
    public void OnOperationInProgressChanged_FiredTrue_DisablesStartButton()
    {
        var (cut, _, coordinatorMock, _, ctx) = Build(LiveStreamStatus.Idle);
        using (ctx)
        {
            cut.Find(".streaming-controls__btn--start").HasAttribute("disabled").Should().BeFalse();

            coordinatorMock.Raise(c => c.OperationInProgressChanged += null, coordinatorMock.Object, true);

            cut.WaitForAssertion(() =>
                cut.Find(".streaming-controls__btn--start").HasAttribute("disabled").Should().BeTrue());
        }
    }

    // ── AC-22: Cancel click cancels the CancellationToken ────────────────────

    [TestMethod]
    public void OnCancelClicked_DuringStarting_CancelsCancellationToken()
    {
        CancellationToken capturedToken = default;

        var streamMock = new Mock<IYouTubeLiveStreamService>();
        streamMock.Setup(s => s.CurrentStatus).Returns(LiveStreamStatus.Idle);
        streamMock.Setup(s => s.CurrentBroadcast).Returns((LiveBroadcastInfo?)null);
        streamMock.Setup(s => s.StartStreamAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(ct =>
            {
                capturedToken = ct;
                // Simulate the service transitioning to Starting
                streamMock.Raise(s => s.StatusChanged += null, streamMock.Object,
                    new StreamStateSnapshot(LiveStreamStatus.Starting, null, null));
            })
            .Returns(() => Task.Delay(Timeout.Infinite, capturedToken));

        var coordinatorMock = new Mock<IOperationCoordinatorService>();
        coordinatorMock.Setup(c => c.IsOperationInProgress).Returns(false);
        coordinatorMock.Setup(c => c.BeginOperation(It.IsAny<string?>())).Returns(true);

        var dialogMock = new Mock<IConfirmDialogService>();
        dialogMock.Setup(d => d.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var ctx = new BunitContext();
        ctx.Services.AddSingleton(streamMock.Object);
        ctx.Services.AddSingleton(coordinatorMock.Object);
        ctx.Services.AddSingleton(dialogMock.Object);

        using (ctx)
        {
            var cut = ctx.Render<StreamingControls>();
            cut.Find(".streaming-controls__btn--start").Click();

            cut.WaitForAssertion(() =>
                cut.FindAll(".streaming-controls__btn--cancel").Should().NotBeEmpty());

            cut.Find(".streaming-controls__btn--cancel").Click();

            capturedToken.IsCancellationRequested.Should().BeTrue();
        }
    }

    // ── Consent confirmation dialog tests ────────────────────────────────────

    [TestMethod]
    public void OnStartClicked_ShowsConsentConfirmDialog()
    {
        var (cut, _, _, dialogMock, ctx) = Build(LiveStreamStatus.Idle, confirmResult: false);
        using (ctx)
        {
            cut.Find(".streaming-controls__btn--start").Click();

            cut.WaitForAssertion(() =>
                dialogMock.Verify(
                    d => d.ConfirmAsync(
                        It.Is<string>(msg => msg.Contains("consents and approvals")),
                        "Start Live Stream"),
                    Times.Once));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(null)]
    public void OnStartClicked_ConsentDeclined_DoesNotCallStartStream(bool? confirmResult)
    {
        var (cut, streamMock, _, _, ctx) = Build(LiveStreamStatus.Idle, confirmResult: confirmResult);
        using (ctx)
        {
            cut.Find(".streaming-controls__btn--start").Click();

            cut.WaitForAssertion(() =>
                streamMock.Verify(
                    s => s.StartStreamAsync(It.IsAny<CancellationToken>()), Times.Never));
        }
    }

    [TestMethod]
    public void OnStartClicked_ConsentConfirmed_ProceedsToStartStream()
    {
        var (cut, streamMock, _, _, ctx) = Build(LiveStreamStatus.Idle, confirmResult: true);
        using (ctx)
        {
            streamMock.Setup(s => s.StartStreamAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            cut.Find(".streaming-controls__btn--start").Click();

            cut.WaitForAssertion(() =>
                streamMock.Verify(
                    s => s.StartStreamAsync(It.IsAny<CancellationToken>()), Times.Once));
        }
    }

    [TestMethod]
    public void OnStartClicked_ConsentDeclined_MarkCompleteReleasesLock()
    {
        var (cut, _, coordinatorMock, _, ctx) = Build(LiveStreamStatus.Idle, confirmResult: false);
        using (ctx)
        {
            cut.Find(".streaming-controls__btn--start").Click();

            cut.WaitForAssertion(() =>
            {
                coordinatorMock.Verify(c => c.BeginOperation(It.IsAny<string?>()), Times.Once);
                coordinatorMock.Verify(c => c.MarkComplete(), Times.Once);
            });
        }
    }
}
