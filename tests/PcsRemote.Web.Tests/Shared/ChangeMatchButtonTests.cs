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
public class ChangeMatchButtonTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (
        IRenderedComponent<ChangeMatchButton> Cut,
        Mock<IScoreboardService> ScoreMock,
        Mock<IPcsProAutomationService> AutoMock,
        Mock<IConfirmDialogService> DialogMock,
        Mock<IOperationCoordinatorService> CoordinatorMock,
        NotificationService NotificationSvc,
        List<NotificationMessage> Notifications,
        BunitContext Ctx)
    Build(PcsProState initialState, ILogger<ChangeMatchButton>? logger = null, bool manualModeActive = false, bool operationInProgress = false)
    {
        var scoreMock = new Mock<IScoreboardService>();
        var autoMock = new Mock<IPcsProAutomationService>();
        autoMock.Setup(a => a.CurrentState).Returns(initialState);
        var dialogMock = new Mock<IConfirmDialogService>();
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
        ctx.Services.AddSingleton(scoreMock.Object);
        ctx.Services.AddSingleton(autoMock.Object);
        ctx.Services.AddSingleton(manualModeMock.Object);
        ctx.Services.AddSingleton(coordinatorMock.Object);
        ctx.Services.AddSingleton(dialogMock.Object);
        ctx.Services.AddSingleton(notificationSvc);
        ctx.Services.AddSingleton(logger ?? (ILogger<ChangeMatchButton>)NullLogger<ChangeMatchButton>.Instance);

        var cut = ctx.Render<ChangeMatchButton>();
        return (cut, scoreMock, autoMock, dialogMock, coordinatorMock, notificationSvc, notifications, ctx);
    }

    [TestMethod]
    public void MatchLoaded_ButtonEnabled()
    {
        var (cut, _, _, _, _, _, _, ctx) = Build(PcsProState.MatchLoaded);
        using (ctx)
        {
            cut.Find(".change-match-button").HasAttribute("disabled").Should().BeFalse();
        }
    }

    // ── TC-2: Button disabled in all non-MatchLoaded states ───────────────────

    [TestMethod]
    [DataRow(PcsProState.NotRunning)]
    [DataRow(PcsProState.Launching)]
    [DataRow(PcsProState.LoginScreen)]
    [DataRow(PcsProState.MatchSelection)]
    [DataRow(PcsProState.MatchSelectionSearching)]
    [DataRow(PcsProState.MatchSelectionReady)]
    [DataRow(PcsProState.Error)]
    public void NonMatchLoadedState_ButtonDisabled(PcsProState state)
    {
        var (cut, _, _, _, _, _, _, ctx) = Build(state);
        using (ctx)
        {
            cut.Find(".change-match-button").HasAttribute("disabled").Should().BeTrue();
        }
    }

    // ── TC-3: StateChanged to MatchLoaded enables button ──────────────────────

    [TestMethod]
    public void StateChanged_ToMatchLoaded_EnablesButton()
    {
        var (cut, _, autoMock, _, _, _, _, ctx) = Build(PcsProState.NotRunning);
        using (ctx)
        {
            autoMock.Raise(a => a.StateChanged += null, autoMock.Object, PcsProState.MatchLoaded);

            cut.WaitForAssertion(() =>
                cut.Find(".change-match-button").HasAttribute("disabled").Should().BeFalse());
        }
    }

    // ── TC-4: StateChanged away from MatchLoaded disables button ──────────────

    [TestMethod]
    public void StateChanged_AwayFromMatchLoaded_DisablesButton()
    {
        var (cut, _, autoMock, _, _, _, _, ctx) = Build(PcsProState.MatchLoaded);
        using (ctx)
        {
            autoMock.Raise(a => a.StateChanged += null, autoMock.Object, PcsProState.MatchSelection);

            cut.WaitForAssertion(() =>
                cut.Find(".change-match-button").HasAttribute("disabled").Should().BeTrue());
        }
    }

    // ── TC-5: Click shows confirmation dialog with correct arguments ──────────

    [TestMethod]
    public void Click_ShowsConfirmDialog_WithCorrectArguments()
    {
        var (cut, _, _, dialogMock, _, _, _, ctx) = Build(PcsProState.MatchLoaded);
        using (ctx)
        {
            dialogMock.Setup(d => d.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);

            cut.Find(".change-match-button").Click();

            cut.WaitForAssertion(() =>
                dialogMock.Verify(
                    d => d.ConfirmAsync(
                        "Load a different match? PCS Pro will close and reopen to the match selection screen.",
                        "Change Match"),
                    Times.Once));
        }
    }

    // ── TC-6: Cancel — no ClearCache, no ChangeMatchAsync (false and null) ────

    [TestMethod]
    [DataRow(false)]
    [DataRow(null)]
    public void Cancel_NoClearCache_NoChangeMatchAsync(bool? dialogResult)
    {
        var (cut, scoreMock, autoMock, dialogMock, coordinatorMock, _, _, ctx) = Build(PcsProState.MatchLoaded);
        using (ctx)
        {
            dialogMock.Setup(d => d.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(dialogResult);

            cut.Find(".change-match-button").Click();

            cut.WaitForAssertion(() =>
            {
                scoreMock.Verify(s => s.ClearCache(), Times.Never);
                autoMock.Verify(a => a.ChangeMatchAsync(It.IsAny<CancellationToken>()), Times.Never);
            });
        }
    }

    // ── TC-7: Confirm — ClearCache called before ChangeMatchAsync ─────────────

    [TestMethod]
    public void Confirm_ClearCacheCalledBeforeChangeMatchAsync()
    {
        var (cut, scoreMock, autoMock, dialogMock, _, _, _, ctx) = Build(PcsProState.MatchLoaded);
        using (ctx)
        {
            var callOrder = new List<string>();
            scoreMock.Setup(s => s.ClearCache()).Callback(() => callOrder.Add("ClearCache"));
            autoMock.Setup(a => a.ChangeMatchAsync(It.IsAny<CancellationToken>()))
                .Callback(() => callOrder.Add("ChangeMatchAsync"))
                .Returns(Task.CompletedTask);
            dialogMock.Setup(d => d.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            cut.Find(".change-match-button").Click();

            cut.WaitForAssertion(() =>
            {
                scoreMock.Verify(s => s.ClearCache(), Times.Once);
                autoMock.Verify(a => a.ChangeMatchAsync(It.IsAny<CancellationToken>()), Times.Once);
                callOrder.Should().HaveCount(2);
                callOrder[0].Should().Be("ClearCache");
                callOrder[1].Should().Be("ChangeMatchAsync");
            });
        }
    }

    // ── TC-8: ChangeMatchAsync exception — logs, notifies, button re-enables ──

    [TestMethod]
    public void ChangeMatchAsyncThrows_ErrorNotification_ButtonReenables_Logs()
    {
        var thrown = new InvalidOperationException("automation error");
        var loggerMock = new Mock<ILogger<ChangeMatchButton>>();
        var (cut, scoreMock, autoMock, dialogMock, _, _, notifications, ctx) =
            Build(PcsProState.MatchLoaded, loggerMock.Object);
        using (ctx)
        {
            autoMock.Setup(a => a.ChangeMatchAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(thrown);
            dialogMock.Setup(d => d.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            scoreMock.Setup(s => s.ClearCache());

            cut.Find(".change-match-button").Click();

            cut.WaitForAssertion(() =>
            {
                notifications.Should().ContainSingle(n =>
                    n.Summary == "Change match failed" && n.Severity == NotificationSeverity.Error);

                cut.Find(".change-match-button").HasAttribute("disabled").Should().BeFalse();

                loggerMock.Verify(
                    x => x.Log(
                        LogLevel.Error,
                        It.IsAny<EventId>(),
                        It.IsAny<It.IsAnyType>(),
                        It.Is<Exception>(e => e == thrown),
                        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                    Times.Once);
            });
        }
    }

    // ── TC-9: Button disabled when coordinator fires in-progress = true ────────

    [TestMethod]
    public void CoordinatorFiresInProgress_ButtonDisabled()
    {
        var (cut, _, _, _, coordinatorMock, _, _, ctx) = Build(PcsProState.MatchLoaded);
        using (ctx)
        {
            coordinatorMock.Raise(c => c.OperationInProgressChanged += null, coordinatorMock.Object, true);

            cut.WaitForAssertion(() =>
                cut.Find(".change-match-button").HasAttribute("disabled").Should().BeTrue());
        }
    }

    // ── TC-11: Dispose unsubscribes from StateChanged ─────────────────────────

    [TestMethod]
    public void Dispose_UnsubscribesFromStateChanged()
    {
        var (cut, _, autoMock, _, _, _, _, ctx) = Build(PcsProState.NotRunning);
        using (ctx)
        {
            cut.Instance.Dispose();

            autoMock.VerifyRemove(
                a => a.StateChanged -= It.IsAny<EventHandler<PcsProState>>(),
                Times.Once);
        }
    }

    // ── TC-15: Dispose unsubscribes from coordinator ──────────────────────────

    [TestMethod]
    public void Dispose_UnsubscribesFromCoordinatorEvent()
    {
        var (cut, _, _, _, coordinatorMock, _, _, ctx) = Build(PcsProState.NotRunning);
        using (ctx)
        {
            cut.Instance.Dispose();

            coordinatorMock.VerifyRemove(
                c => c.OperationInProgressChanged -= It.IsAny<EventHandler<bool>>(),
                Times.Once);
        }
    }

    // ── TC-16: Button re-enables when coordinator fires in-progress = false ────

    [TestMethod]
    public void CoordinatorFiresComplete_ButtonReenables()
    {
        var (cut, _, _, _, coordinatorMock, _, _, ctx) = Build(PcsProState.MatchLoaded, operationInProgress: true);
        using (ctx)
        {
            cut.Find(".change-match-button").HasAttribute("disabled").Should().BeTrue();

            coordinatorMock.Raise(c => c.OperationInProgressChanged += null, coordinatorMock.Object, false);

            cut.WaitForAssertion(() =>
                cut.Find(".change-match-button").HasAttribute("disabled").Should().BeFalse());
        }
    }

    // ── TC-17: Tooltip present when button disabled due to coordinator ─────────

    [TestMethod]
    public void OperationInProgress_TooltipAttributePresent()
    {
        var (cut, _, _, _, _, _, _, ctx) = Build(PcsProState.MatchLoaded, operationInProgress: true);
        using (ctx)
        {
            cut.Find(".change-match-button").GetAttribute("title")
                .Should().Be("Automation in progress\u2026");
        }
    }

    // ── TC-18: Tooltip absent when button not disabled by coordinator ──────────

    [TestMethod]
    public void NoOperationInProgress_TooltipAttributeEmpty()
    {
        var (cut, _, _, _, _, _, _, ctx) = Build(PcsProState.MatchLoaded);
        using (ctx)
        {
            var title = cut.Find(".change-match-button").GetAttribute("title") ?? string.Empty;
            title.Should().BeEmpty();
        }
    }

    // ── TC-19: Button re-enables if automation throws (finally guarantees MarkComplete) ─

    [TestMethod]
    public void ChangeMatchAsyncThrows_MarkCompleteCalledInFinally()
    {
        var (cut, scoreMock, autoMock, dialogMock, coordinatorMock, _, _, ctx) = Build(PcsProState.MatchLoaded);
        using (ctx)
        {
            autoMock.Setup(a => a.ChangeMatchAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));
            dialogMock.Setup(d => d.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            scoreMock.Setup(s => s.ClearCache());

            cut.Find(".change-match-button").Click();

            cut.WaitForAssertion(() =>
                coordinatorMock.Verify(c => c.MarkComplete(), Times.Once));
        }
    }

    // ── TC-20: Dialog cancelled → BeginOperation never called ─────────────────

    [TestMethod]
    public void DialogCancelled_BeginOperationNeverCalled()
    {
        var (cut, _, _, dialogMock, coordinatorMock, _, _, ctx) = Build(PcsProState.MatchLoaded);
        using (ctx)
        {
            dialogMock.Setup(d => d.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);

            cut.Find(".change-match-button").Click();

            cut.WaitForAssertion(() =>
                coordinatorMock.Verify(c => c.BeginOperation(), Times.Never));
        }
    }

    // ── TC-21: BeginOperation returns false (lock lost to another browser) → automation not called, MarkComplete never called ─

    [TestMethod]
    public void BeginOperationReturnsFalse_AutomationNotCalled_MarkCompleteNeverCalled()
    {
        var (cut, _, autoMock, dialogMock, coordinatorMock, _, _, ctx) = Build(PcsProState.MatchLoaded);
        using (ctx)
        {
            // Simulate another browser winning the CAS: BeginOperation is a no-op (returns false)
            coordinatorMock.Setup(c => c.BeginOperation()).Returns(false);
            dialogMock.Setup(d => d.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            cut.Find(".change-match-button").Click();

            cut.WaitForAssertion(() =>
            {
                autoMock.Verify(a => a.ChangeMatchAsync(It.IsAny<CancellationToken>()), Times.Never,
                    "automation must not run when this browser did not acquire the coordinator lock");
                coordinatorMock.Verify(c => c.MarkComplete(), Times.Never,
                    "MarkComplete must not be called when BeginOperation returned false");
            });
        }
    }

    // ── TC-16 (S-003): Button disabled when manual mode is active ─────────────

    [TestMethod]
    public void ManualModeActive_ButtonDisabled()
    {
        var (cut, _, _, _, _, _, _, ctx) = Build(PcsProState.MatchLoaded, manualModeActive: true);
        using (ctx)
        {
            cut.Find(".change-match-button").HasAttribute("disabled").Should().BeTrue();
        }
    }

    // ── TC-17 (S-003): Click rejected with guard notification while manual mode active ─

    [TestMethod]
    public void ManualModeActive_Click_RejectedWithNotification()
    {
        var (cut, _, autoMock, dialogMock, _, _, notifications, ctx) =
            Build(PcsProState.MatchLoaded, manualModeActive: true);
        using (ctx)
        {
            cut.Find(".change-match-button").Click();

            cut.WaitForAssertion(() =>
            {
                notifications.Should().ContainSingle(n =>
                    n.Summary == "Automation is paused — disable manual mode before issuing commands" &&
                    n.Severity == NotificationSeverity.Warning);
                dialogMock.Verify(
                    d => d.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()),
                    Times.Never);
                autoMock.Verify(a => a.ChangeMatchAsync(It.IsAny<CancellationToken>()), Times.Never);
            });
        }
    }

    // ── TC-18 (S-003): ManualModeChanged(false) → button re-enables ──────────

    [TestMethod]
    public void ManualModeChanged_ToFalse_ReEnablesButton()
    {
        var manualModeMock = new Mock<IManualModeService>();
        manualModeMock.Setup(s => s.IsManualModeActive).Returns(true);

        var autoMock = new Mock<IPcsProAutomationService>();
        autoMock.Setup(a => a.CurrentState).Returns(PcsProState.MatchLoaded);
        var dialogMock = new Mock<IConfirmDialogService>();
        var coordinatorMock = new Mock<IOperationCoordinatorService>();
        coordinatorMock.Setup(c => c.IsOperationInProgress).Returns(false);
        var notificationSvc = new NotificationService();
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(new Mock<IScoreboardService>().Object);
        ctx.Services.AddSingleton(autoMock.Object);
        ctx.Services.AddSingleton(manualModeMock.Object);
        ctx.Services.AddSingleton(coordinatorMock.Object);
        ctx.Services.AddSingleton(dialogMock.Object);
        ctx.Services.AddSingleton(notificationSvc);
        ctx.Services.AddSingleton<ILogger<ChangeMatchButton>>(NullLogger<ChangeMatchButton>.Instance);

        using (ctx)
        {
            var cut = ctx.Render<ChangeMatchButton>();
            cut.Find(".change-match-button").HasAttribute("disabled").Should().BeTrue();

            manualModeMock.Setup(s => s.IsManualModeActive).Returns(false);
            manualModeMock.Raise(s => s.ManualModeChanged += null, manualModeMock.Object, false);

            cut.WaitForAssertion(() =>
                cut.Find(".change-match-button").HasAttribute("disabled").Should().BeFalse());
        }
    }
}
