using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web;
using PcsRemote.Web.Shared;
using Radzen;
using System.Collections.Specialized;

namespace PcsRemote.Web.Tests.Shared;

[TestClass]
public sealed class ErrorDisplayTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (
        IRenderedComponent<ErrorDisplay> Cut,
        Mock<IPcsProAutomationService> AutoMock,
        Mock<IManualModeService> ManualModeMock,
        Mock<IOperationCoordinatorService> CoordinatorMock,
        NotificationService NotificationSvc,
        List<NotificationMessage> Notifications,
        BunitContext Ctx)
    Build(
        PcsProState initialState,
        string? lastErrorReason = "test error reason",
        bool manualModeActive = false,
        bool operationInProgress = false)
    {
        var autoMock = new Mock<IPcsProAutomationService>();
        autoMock.Setup(a => a.CurrentState).Returns(initialState);
        autoMock.Setup(a => a.LastErrorReason).Returns(lastErrorReason);

        var manualModeMock = new Mock<IManualModeService>();
        manualModeMock.Setup(s => s.IsManualModeActive).Returns(manualModeActive);

        var coordinatorMock = new Mock<IOperationCoordinatorService>();
        coordinatorMock.Setup(c => c.IsOperationInProgress).Returns(operationInProgress);
        coordinatorMock
            .Setup(c => c.BeginOperation())
            .Callback(() => coordinatorMock.Raise(c => c.OperationInProgressChanged += null, coordinatorMock.Object, true))
            .Returns(true);
        coordinatorMock
            .Setup(c => c.MarkComplete())
            .Raises(c => c.OperationInProgressChanged += null, coordinatorMock.Object, false);

        var notificationSvc = new NotificationService();
        var notifications = new List<NotificationMessage>();
        notificationSvc.Messages.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (NotificationMessage msg in e.NewItems)
                    notifications.Add(msg);
        };

        var ctx = new BunitContext();
        ctx.Services.AddSingleton(autoMock.Object);
        ctx.Services.AddSingleton(manualModeMock.Object);
        ctx.Services.AddSingleton(coordinatorMock.Object);
        ctx.Services.AddSingleton(notificationSvc);
        ctx.Services.AddSingleton<ILogger<ErrorDisplay>>(NullLogger<ErrorDisplay>.Instance);

        var cut = ctx.Render<ErrorDisplay>();
        return (cut, autoMock, manualModeMock, coordinatorMock, notificationSvc, notifications, ctx);
    }

    // ── TC-4: Error state renders component with reason string and icon ────────

    [TestMethod]
    public void ErrorState_RendersComponent_WithReasonStringAndIcon()
    {
        var (cut, _, _, _, _, _, ctx) = Build(PcsProState.Error, lastErrorReason: "test reason");
        using (ctx)
        {
            cut.Find(".error-display__reason").TextContent.Should().Contain("test reason");
            cut.Find(".error-display__retry-btn").Should().NotBeNull();
            cut.Find(".error-display--icon").Should().NotBeNull(
                "the red error indicator element must be present in Error state (AC-14)");
        }
    }

    // ── TC-5: Non-Error state renders nothing ─────────────────────────────────

    [TestMethod]
    [DataRow(PcsProState.NotRunning)]
    [DataRow(PcsProState.Launching)]
    [DataRow(PcsProState.LoginScreen)]
    [DataRow(PcsProState.MatchSelection)]
    [DataRow(PcsProState.MatchSelectionSearching)]
    [DataRow(PcsProState.MatchSelectionReady)]
    [DataRow(PcsProState.MatchLoaded)]
    public void NonErrorState_RendersNothing(PcsProState state)
    {
        var (cut, _, _, _, _, _, ctx) = Build(state);
        using (ctx)
        {
            cut.FindAll(".error-display").Should().BeEmpty(
                "ErrorDisplay must render an empty fragment in non-Error states");
        }
    }

    // ── TC-6: Error state with null reason shows fallback string ──────────────

    [TestMethod]
    public void ErrorState_NullReason_ShowsFallback()
    {
        var (cut, _, _, _, _, _, ctx) = Build(PcsProState.Error, lastErrorReason: null);
        using (ctx)
        {
            cut.Find(".error-display__reason").TextContent
                .Should().Contain("An unexpected error occurred.");
        }
    }

    // ── TC-6b: Error state with empty reason shows fallback string ────────────

    [TestMethod]
    public void ErrorState_EmptyReason_ShowsFallback()
    {
        var (cut, _, _, _, _, _, ctx) = Build(PcsProState.Error, lastErrorReason: "");
        using (ctx)
        {
            cut.Find(".error-display__reason").TextContent
                .Should().Contain("An unexpected error occurred.",
                    "an empty LastErrorReason must show the fallback text per AC-2");
        }
    }

    // ── TC-7: Operation in progress disables retry button with correct tooltip ─

    [TestMethod]
    public void OperationInProgress_RetryButtonDisabled_TooltipPresent()
    {
        var (cut, _, _, _, _, _, ctx) = Build(PcsProState.Error, operationInProgress: true);
        using (ctx)
        {
            var btn = cut.Find(".error-display__retry-btn");
            btn.HasAttribute("disabled").Should().BeTrue();
            btn.GetAttribute("title").Should().Be("Automation in progress\u2026");
        }
    }

    // ── TC-7b: _operationInProgress early-return prevents BeginOperation call ─

    [TestMethod]
    public void OperationInProgress_RetryClick_DoesNotCallBeginOperation()
    {
        var (cut, autoMock, _, coordinatorMock, _, _, ctx) = Build(PcsProState.Error, operationInProgress: true);
        using (ctx)
        {
            cut.Find(".error-display__retry-btn").Click();

            cut.WaitForAssertion(() =>
            {
                coordinatorMock.Verify(c => c.BeginOperation(), Times.Never,
                    "BeginOperation must not be called when the cached _operationInProgress guard fires");
                autoMock.Verify(a => a.RetryAsync(It.IsAny<CancellationToken>()), Times.Never);
            });
        }
    }

    // ── TC-8: Manual mode active disables retry button ────────────────────────

    [TestMethod]
    public void ManualModeActive_RetryButtonDisabled()
    {
        var (cut, _, _, _, _, _, ctx) = Build(PcsProState.Error, manualModeActive: true);
        using (ctx)
        {
            var btn = cut.Find(".error-display__retry-btn");
            btn.HasAttribute("disabled").Should().BeTrue();
            btn.GetAttribute("title").Should().Be("Automation is paused");
        }
    }

    // ── TC-9: Retry click when manual mode active rejects with notification ────

    [TestMethod]
    public void RetryClick_ManualModeActive_RejectsWithNotification()
    {
        var (cut, autoMock, manualModeMock, _, _, notifications, ctx) = Build(PcsProState.Error, manualModeActive: true);
        using (ctx)
        {
            // Re-check IsManualModeActive at click time
            manualModeMock.Setup(s => s.IsManualModeActive).Returns(true);

            cut.Find(".error-display__retry-btn").Click();

            cut.WaitForAssertion(() =>
            {
                notifications.Should().ContainSingle(n =>
                    n.Severity == NotificationSeverity.Warning &&
                    n.Summary != null &&
                    n.Summary.Contains("disable manual mode before retrying"));
                autoMock.Verify(a => a.RetryAsync(It.IsAny<CancellationToken>()), Times.Never);
            });
        }
    }

    // ── TC-10: Normal retry click calls RetryAsync, BeginOperation, MarkComplete

    [TestMethod]
    public void RetryClick_Succeeds_CallsRetryAsync()
    {
        var (cut, autoMock, _, coordinatorMock, _, _, ctx) = Build(PcsProState.Error);
        using (ctx)
        {
            autoMock.Setup(a => a.RetryAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            cut.Find(".error-display__retry-btn").Click();

            cut.WaitForAssertion(() =>
            {
                autoMock.Verify(a => a.RetryAsync(It.IsAny<CancellationToken>()), Times.Once);
                coordinatorMock.Verify(c => c.BeginOperation(), Times.Once);
                coordinatorMock.Verify(c => c.MarkComplete(), Times.Once);
            });
        }
    }

    // ── TC-11: BeginOperation returns false → RetryAsync and MarkComplete not called

    [TestMethod]
    public void RetryClick_BeginOperationReturnsFalse_DoesNotCallRetryAsync()
    {
        var (cut, autoMock, _, coordinatorMock, _, _, ctx) = Build(PcsProState.Error);
        using (ctx)
        {
            // Simulate another browser winning the CAS
            coordinatorMock.Setup(c => c.BeginOperation()).Returns(false);

            cut.Find(".error-display__retry-btn").Click();

            cut.WaitForAssertion(() =>
            {
                autoMock.Verify(a => a.RetryAsync(It.IsAny<CancellationToken>()), Times.Never,
                    "RetryAsync must not be called when BeginOperation returned false");
                coordinatorMock.Verify(c => c.MarkComplete(), Times.Never,
                    "MarkComplete must not be called when BeginOperation returned false");
            });
        }
    }

    // ── TC-12: RetryAsync throws → MarkComplete still called in finally ────────

    [TestMethod]
    public void RetryAsyncThrows_MarkCompleteCalledInFinally()
    {
        var (cut, autoMock, _, coordinatorMock, _, _, ctx) = Build(PcsProState.Error);
        using (ctx)
        {
            autoMock.Setup(a => a.RetryAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("retry boom"));

            cut.Find(".error-display__retry-btn").Click();

            cut.WaitForAssertion(() =>
                coordinatorMock.Verify(c => c.MarkComplete(), Times.Once,
                    "MarkComplete must be called in finally even when RetryAsync throws"));
        }
    }

    // ── TC-13: Coordinator fires in-progress = true → retry button disabled ────

    [TestMethod]
    public void CoordinatorFiresInProgress_RetryButtonDisabled()
    {
        var (cut, _, _, coordinatorMock, _, _, ctx) = Build(PcsProState.Error);
        using (ctx)
        {
            coordinatorMock.Raise(c => c.OperationInProgressChanged += null, coordinatorMock.Object, true);

            cut.WaitForAssertion(() =>
                cut.Find(".error-display__retry-btn").HasAttribute("disabled").Should().BeTrue());
        }
    }

    // ── TC-14: Coordinator fires in-progress = false → retry button re-enables ─

    [TestMethod]
    public void CoordinatorFiresComplete_RetryButtonReenables()
    {
        var (cut, _, _, coordinatorMock, _, _, ctx) = Build(PcsProState.Error, operationInProgress: true);
        using (ctx)
        {
            cut.Find(".error-display__retry-btn").HasAttribute("disabled").Should().BeTrue();

            coordinatorMock.Raise(c => c.OperationInProgressChanged += null, coordinatorMock.Object, false);

            cut.WaitForAssertion(() =>
                cut.Find(".error-display__retry-btn").HasAttribute("disabled").Should().BeFalse());
        }
    }

    // ── TC-15: Dispose unsubscribes all three event handlers ──────────────────

    [TestMethod]
    public void Dispose_UnsubscribesAllEvents()
    {
        var (cut, autoMock, manualModeMock, coordinatorMock, _, _, ctx) = Build(PcsProState.Error);
        using (ctx)
        {
            cut.Instance.Dispose();

            autoMock.VerifyRemove(
                a => a.StateChanged -= It.IsAny<EventHandler<PcsProState>>(),
                Times.Once,
                "StateChanged must be unsubscribed on Dispose");

            manualModeMock.VerifyRemove(
                m => m.ManualModeChanged -= It.IsAny<EventHandler<bool>>(),
                Times.Once,
                "ManualModeChanged must be unsubscribed on Dispose");

            coordinatorMock.VerifyRemove(
                c => c.OperationInProgressChanged -= It.IsAny<EventHandler<bool>>(),
                Times.Once,
                "OperationInProgressChanged must be unsubscribed on Dispose");
        }
    }

    // ── TC-16: StateChanged to non-Error → component renders empty ────────────

    [TestMethod]
    public void StateChangedToNonError_ComponentBecomesEmpty()
    {
        var (cut, autoMock, _, _, _, _, ctx) = Build(PcsProState.Error);
        using (ctx)
        {
            cut.FindAll(".error-display").Should().NotBeEmpty("component should be visible in Error state");

            autoMock.Raise(a => a.StateChanged += null, autoMock.Object, PcsProState.NotRunning);

            cut.WaitForAssertion(() =>
                cut.FindAll(".error-display").Should().BeEmpty(
                    "ErrorDisplay must render empty when state leaves Error (H-SC-7 flow)"));
        }
    }

    // ── S-004 TC-1: Error state renders dismiss button ────────────────────────

    [TestMethod]
    public void ErrorState_RendersDismissButton()
    {
        var (cut, _, _, _, _, _, ctx) = Build(PcsProState.Error);
        using (ctx)
        {
            cut.Find(".error-display__dismiss-btn").Should().NotBeNull();
        }
    }

    // ── S-004 TC-2: Non-Error state does not render dismiss button ────────────

    [TestMethod]
    [DataRow(PcsProState.NotRunning)]
    [DataRow(PcsProState.Launching)]
    [DataRow(PcsProState.LoginScreen)]
    [DataRow(PcsProState.MatchSelection)]
    [DataRow(PcsProState.MatchSelectionSearching)]
    [DataRow(PcsProState.MatchSelectionReady)]
    [DataRow(PcsProState.MatchLoaded)]
    public void NonErrorState_DismissButtonNotRendered(PcsProState state)
    {
        var (cut, _, _, _, _, _, ctx) = Build(state);
        using (ctx)
        {
            cut.FindAll(".error-display__dismiss-btn").Should().BeEmpty(
                "Dismiss button must not render in non-Error states");
        }
    }

    // ── S-004 TC-3: Dismiss click calls DismissAsync, no coordinator ──────────

    [TestMethod]
    public void DismissClick_CallsDismissAsync()
    {
        var (cut, autoMock, _, coordinatorMock, _, _, ctx) = Build(PcsProState.Error);
        using (ctx)
        {
            autoMock.Setup(a => a.DismissAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            cut.Find(".error-display__dismiss-btn").Click();

            cut.WaitForAssertion(() =>
            {
                autoMock.Verify(a => a.DismissAsync(It.IsAny<CancellationToken>()), Times.Once);
                coordinatorMock.Verify(c => c.BeginOperation(It.IsAny<string?>()), Times.Never,
                    "Dismiss must not acquire coordinator lock");
                coordinatorMock.Verify(c => c.MarkComplete(), Times.Never,
                    "Dismiss must not call MarkComplete");
            });
        }
    }

    // ── S-004 TC-4: DismissAsync throws → notification shown ──────────────────

    [TestMethod]
    public void DismissClick_DismissAsyncThrows_ShowsNotification()
    {
        var (cut, autoMock, _, _, _, notifications, ctx) = Build(PcsProState.Error);
        using (ctx)
        {
            autoMock.Setup(a => a.DismissAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("dismiss boom"));

            cut.Find(".error-display__dismiss-btn").Click();

            cut.WaitForAssertion(() =>
            {
                notifications.Should().ContainSingle(n =>
                    n.Severity == NotificationSeverity.Error &&
                    n.Summary != null &&
                    n.Summary.Contains("Dismiss failed"));
            });
        }
    }

    // ── S-004 TC-5: Dismiss enabled + works when coordinator locked ───────────

    [TestMethod]
    public void DismissClick_WhenCoordinatorLocked_StillCallsDismissAsync()
    {
        var (cut, autoMock, _, coordinatorMock, _, _, ctx) = Build(PcsProState.Error, operationInProgress: true);
        using (ctx)
        {
            autoMock.Setup(a => a.DismissAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var btn = cut.Find(".error-display__dismiss-btn");
            btn.HasAttribute("disabled").Should().BeFalse(
                "Dismiss must remain enabled regardless of coordinator lock");

            btn.Click();

            cut.WaitForAssertion(() =>
            {
                autoMock.Verify(a => a.DismissAsync(It.IsAny<CancellationToken>()), Times.Once);
                coordinatorMock.Verify(c => c.BeginOperation(It.IsAny<string?>()), Times.Never);
                coordinatorMock.Verify(c => c.MarkComplete(), Times.Never);
            });
        }
    }

    // ── S-004 TC-6: Dismiss enabled + works when manual mode active ───────────

    [TestMethod]
    public void DismissClick_WhenManualModeActive_StillCallsDismissAsync()
    {
        var (cut, autoMock, manualModeMock, _, _, notifications, ctx) = Build(PcsProState.Error, manualModeActive: true);
        using (ctx)
        {
            manualModeMock.Setup(s => s.IsManualModeActive).Returns(true);
            autoMock.Setup(a => a.DismissAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var btn = cut.Find(".error-display__dismiss-btn");
            btn.HasAttribute("disabled").Should().BeFalse(
                "Dismiss must remain enabled regardless of manual mode");

            btn.Click();

            cut.WaitForAssertion(() =>
            {
                autoMock.Verify(a => a.DismissAsync(It.IsAny<CancellationToken>()), Times.Once);
                notifications.Should().BeEmpty(
                    "Dismiss must not show manual-mode warning notification");
            });
        }
    }

    // ── S-004 TC-7: Dismiss does not call LaunchAndLoginAsync ──────────────────

    [TestMethod]
    public void DismissClick_DoesNotCallLaunchAndLogin()
    {
        var (cut, autoMock, _, _, _, _, ctx) = Build(PcsProState.Error);
        using (ctx)
        {
            autoMock.Setup(a => a.DismissAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            cut.Find(".error-display__dismiss-btn").Click();

            cut.WaitForAssertion(() =>
            {
                autoMock.Verify(a => a.LaunchAndLoginAsync(It.IsAny<CancellationToken>()), Times.Never,
                    "Dismiss must not trigger a re-launch — this is the defining distinction from Retry");
            });
        }
    }

    // ── S-004 TC-8: After dismiss + StateChanged, error display disappears ────

    [TestMethod]
    public void DismissSuccess_StateChangesToNotRunning_ErrorDisplayDisappears()
    {
        var (cut, autoMock, _, _, _, _, ctx) = Build(PcsProState.Error);
        using (ctx)
        {
            autoMock.Setup(a => a.DismissAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            cut.Find(".error-display__dismiss-btn").Click();

            // Simulate the state change that DismissAsync causes
            autoMock.Raise(a => a.StateChanged += null, autoMock.Object, PcsProState.NotRunning);

            cut.WaitForAssertion(() =>
                cut.FindAll(".error-display").Should().BeEmpty(
                    "Error display must disappear after successful dismiss"));
        }
    }

    // ── S-004 TC-9: Double-click suppression while in-flight ──────────────────

    [TestMethod]
    public void DismissClick_WhileInFlight_SuppressesSecondCall()
    {
        var (cut, autoMock, _, _, _, _, ctx) = Build(PcsProState.Error);
        using (ctx)
        {
            var tcs = new TaskCompletionSource();
            autoMock.Setup(a => a.DismissAsync(It.IsAny<CancellationToken>()))
                .Returns(tcs.Task);

            cut.Find(".error-display__dismiss-btn").Click();
            cut.Find(".error-display__dismiss-btn").Click();

            tcs.SetResult();

            cut.WaitForAssertion(() =>
                autoMock.Verify(a => a.DismissAsync(It.IsAny<CancellationToken>()), Times.Once,
                    "Second click while dismiss is in-flight must be suppressed by _dismissing guard"));
        }
    }

    // ── S-004 TC-10: Guard resets after failure, allows retry ──────────────────

    [TestMethod]
    public void DismissClick_AfterFailure_GuardResetsAndAllowsRetry()
    {
        var (cut, autoMock, _, _, _, _, ctx) = Build(PcsProState.Error);
        using (ctx)
        {
            // First click throws
            autoMock.Setup(a => a.DismissAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("first attempt fails"));

            cut.Find(".error-display__dismiss-btn").Click();

            // Second click succeeds
            autoMock.Setup(a => a.DismissAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            cut.Find(".error-display__dismiss-btn").Click();

            cut.WaitForAssertion(() =>
                autoMock.Verify(a => a.DismissAsync(It.IsAny<CancellationToken>()), Times.Exactly(2),
                    "Guard must reset after failure so subsequent dismiss attempt succeeds"));
        }
    }
}
