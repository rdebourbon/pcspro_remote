using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web.Shared;
using Radzen;
using System.Collections.Specialized;

namespace PcsRemote.Web.Tests.Shared;

[TestClass]
public class RefreshScoreboardButtonTests
{
    private static readonly byte[] SampleImage = [0xFF, 0xD8, 0xFF, 0x01, 0x02];

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (
        IRenderedComponent<RefreshScoreboardButton> Cut,
        Mock<IScoreboardService> ScoreMock,
        Mock<IPcsProAutomationService> AutoMock,
        NotificationService NotificationSvc,
        List<NotificationMessage> Notifications,
        BunitContext Ctx)
    Build(PcsProState initialState, ILogger<RefreshScoreboardButton>? logger = null, bool manualModeActive = false)
    {
        var scoreMock = new Mock<IScoreboardService>();
        scoreMock.Setup(s => s.CurrentImage).Returns((byte[]?)null);

        var autoMock = new Mock<IPcsProAutomationService>();
        autoMock.Setup(a => a.CurrentState).Returns(initialState);

        var manualModeMock = new Mock<IManualModeService>();
        manualModeMock.Setup(s => s.IsManualModeActive).Returns(manualModeActive);

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
        ctx.Services.AddSingleton(notificationSvc);
        ctx.Services.AddSingleton(logger ?? NullLogger<RefreshScoreboardButton>.Instance);

        var cut = ctx.Render<RefreshScoreboardButton>();
        return (cut, scoreMock, autoMock, notificationSvc, notifications, ctx);
    }

    // ── TC-1: Button enabled in MatchLoaded state ─────────────────────────────

    [TestMethod]
    public void MatchLoaded_ButtonEnabled()
    {
        var (cut, _, _, _, _, ctx) = Build(PcsProState.MatchLoaded);
        using (ctx)
        {
            cut.Find(".refresh-scoreboard-button").HasAttribute("disabled").Should().BeFalse();
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
        var (cut, _, _, _, _, ctx) = Build(state);
        using (ctx)
        {
            cut.Find(".refresh-scoreboard-button").HasAttribute("disabled").Should().BeTrue();
        }
    }

    // ── TC-3: Button disabled while refresh is in progress ────────────────────

    [TestMethod]
    public void RefreshInProgress_ButtonDisabled()
    {
        var tcs = new TaskCompletionSource();
        var (cut, scoreMock, _, _, _, ctx) = Build(PcsProState.MatchLoaded);
        using (ctx)
        {
            scoreMock.Setup(s => s.ForceRefreshAsync(It.IsAny<CancellationToken>()))
                .Returns(tcs.Task);

            cut.Find(".refresh-scoreboard-button").Click();

            cut.WaitForAssertion(() =>
                cut.Find(".refresh-scoreboard-button").HasAttribute("disabled").Should().BeTrue());
        }
    }

    // ── TC-4: Button click calls ForceRefreshAsync once ───────────────────────

    [TestMethod]
    public void ButtonClick_CallsForceRefreshAsyncOnce()
    {
        var (cut, scoreMock, _, _, _, ctx) = Build(PcsProState.MatchLoaded);
        using (ctx)
        {
            scoreMock.Setup(s => s.ForceRefreshAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            cut.Find(".refresh-scoreboard-button").Click();

            cut.WaitForAssertion(() =>
                scoreMock.Verify(s => s.ForceRefreshAsync(It.IsAny<CancellationToken>()), Times.Once));
        }
    }

    // ── TC-5: RefreshCompleted event shows success notification ───────────────

    [TestMethod]
    public void RefreshCompleted_ShowsSuccessNotification()
    {
        var (cut, scoreMock, _, _, notifications, ctx) = Build(PcsProState.MatchLoaded);
        using (ctx)
        {
            scoreMock.Raise(s => s.RefreshCompleted += null, scoreMock.Object, EventArgs.Empty);

            cut.WaitForAssertion(() =>
                notifications.Should().ContainSingle(n =>
                    n.Summary == "Scoreboard refreshed" && n.Severity == NotificationSeverity.Success));
        }
    }

    // ── TC-6: Exception shows error notification, re-enables button, logs ─────

    [TestMethod]
    public void ForceRefreshAsyncThrows_ErrorNotification_ButtonReenables_Logs()
    {
        var thrown = new InvalidOperationException("capture error");
        var loggerMock = new Mock<ILogger<RefreshScoreboardButton>>();
        var (cut, scoreMock, _, _, notifications, ctx) = Build(PcsProState.MatchLoaded, loggerMock.Object);
        using (ctx)
        {
            scoreMock.Setup(s => s.ForceRefreshAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(thrown);

            cut.Find(".refresh-scoreboard-button").Click();

            cut.WaitForAssertion(() =>
            {
                // Error notification emitted
                notifications.Should().ContainSingle(n =>
                    n.Summary == "Refresh failed" && n.Severity == NotificationSeverity.Error);

                // Button re-enabled after exception (finally block)
                cut.Find(".refresh-scoreboard-button").HasAttribute("disabled").Should().BeFalse();

                // Logger received a LogError call
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

    // ── TC-7: Button re-enables after successful refresh ──────────────────────

    [TestMethod]
    public void ForceRefreshAsyncCompletes_ButtonReenables()
    {
        var (cut, scoreMock, _, _, _, ctx) = Build(PcsProState.MatchLoaded);
        using (ctx)
        {
            scoreMock.Setup(s => s.ForceRefreshAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            cut.Find(".refresh-scoreboard-button").Click();

            cut.WaitForAssertion(() =>
                cut.Find(".refresh-scoreboard-button").HasAttribute("disabled").Should().BeFalse());
        }
    }

    // ── TC-8: StateChanged away from MatchLoaded disables button ─────────────

    [TestMethod]
    public void StateChanged_AwayFromMatchLoaded_DisablesButton()
    {
        var (cut, _, autoMock, _, _, ctx) = Build(PcsProState.MatchLoaded);
        using (ctx)
        {
            cut.Find(".refresh-scoreboard-button").HasAttribute("disabled").Should().BeFalse();

            autoMock.Raise(a => a.StateChanged += null, autoMock.Object, PcsProState.MatchSelection);

            cut.WaitForAssertion(() =>
                cut.Find(".refresh-scoreboard-button").HasAttribute("disabled").Should().BeTrue());
        }
    }

    // ── TC-9: Disposal unsubscribes from both events ──────────────────────────

    [TestMethod]
    public void Dispose_UnsubscribesFromBothEvents()
    {
        var (cut, scoreMock, autoMock, _, _, ctx) = Build(PcsProState.NotRunning);
        using (ctx)
        {
            // Direct disposal pattern (established project convention — see ScoreboardPreviewTests.cs)
            cut.Instance.Dispose();

            scoreMock.VerifyRemove(
                s => s.RefreshCompleted -= It.IsAny<EventHandler>(),
                Times.Once);
            autoMock.VerifyRemove(
                a => a.StateChanged -= It.IsAny<EventHandler<PcsProState>>(),
                Times.Once);
        }
    }

    // ── TC-19 (S-003): Button disabled when manual mode is active ─────────────

    [TestMethod]
    public void ManualModeActive_ButtonDisabled()
    {
        var (cut, _, _, _, _, ctx) = Build(PcsProState.MatchLoaded, manualModeActive: true);
        using (ctx)
        {
            cut.Find(".refresh-scoreboard-button").HasAttribute("disabled").Should().BeTrue();
        }
    }

    // ── TC-20 (S-003): Click rejected with guard notification while manual mode active ─

    [TestMethod]
    public void ManualModeActive_Click_RejectedWithNotification()
    {
        var (cut, scoreMock, _, _, notifications, ctx) =
            Build(PcsProState.MatchLoaded, manualModeActive: true);
        using (ctx)
        {
            cut.Find(".refresh-scoreboard-button").Click();

            cut.WaitForAssertion(() =>
            {
                notifications.Should().ContainSingle(n =>
                    n.Summary == "Automation is paused — disable manual mode before issuing commands" &&
                    n.Severity == NotificationSeverity.Warning);
                scoreMock.Verify(
                    s => s.ForceRefreshAsync(It.IsAny<CancellationToken>()),
                    Times.Never);
            });
        }
    }

    // ── TC-20b (S-003): ManualModeChanged(false) → button re-enables ─────────

    [TestMethod]
    public void ManualModeChanged_ToFalse_ReEnablesButton()
    {
        var manualModeMock = new Mock<IManualModeService>();
        manualModeMock.Setup(s => s.IsManualModeActive).Returns(true);

        var scoreMock = new Mock<IScoreboardService>();
        scoreMock.Setup(s => s.CurrentImage).Returns((byte[]?)null);
        var autoMock = new Mock<IPcsProAutomationService>();
        autoMock.Setup(a => a.CurrentState).Returns(PcsProState.MatchLoaded);
        var notificationSvc = new NotificationService();

        var ctx = new BunitContext();
        ctx.Services.AddSingleton(scoreMock.Object);
        ctx.Services.AddSingleton(autoMock.Object);
        ctx.Services.AddSingleton(manualModeMock.Object);
        ctx.Services.AddSingleton(notificationSvc);
        ctx.Services.AddSingleton<ILogger<RefreshScoreboardButton>>(NullLogger<RefreshScoreboardButton>.Instance);

        using (ctx)
        {
            var cut = ctx.Render<RefreshScoreboardButton>();
            cut.Find(".refresh-scoreboard-button").HasAttribute("disabled").Should().BeTrue();

            manualModeMock.Setup(s => s.IsManualModeActive).Returns(false);
            manualModeMock.Raise(s => s.ManualModeChanged += null, manualModeMock.Object, false);

            cut.WaitForAssertion(() =>
                cut.Find(".refresh-scoreboard-button").HasAttribute("disabled").Should().BeFalse());
        }
    }

    // ── TC-11: Multi-component integration — button click updates ScoreboardPreview ─

    [TestMethod]
    public void ButtonClick_UpdatesScoreboardPreview_AndShowsSuccessNotification()
    {
        var scoreMock = new Mock<IScoreboardService>();
        scoreMock.Setup(s => s.CurrentImage).Returns((byte[]?)null);

        var autoMock = new Mock<IPcsProAutomationService>();
        autoMock.Setup(a => a.CurrentState).Returns(PcsProState.MatchLoaded);

        var notificationSvc = new NotificationService();
        var notifications = new List<NotificationMessage>();
        notificationSvc.Messages.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (NotificationMessage msg in e.NewItems)
                    notifications.Add(msg);
        };

        scoreMock.Setup(s => s.ForceRefreshAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                // ScoreboardUpdated fires before RefreshCompleted (S-SC-11 ordering)
                scoreMock.Raise(s => s.ScoreboardUpdated += null, scoreMock.Object, SampleImage);
                scoreMock.Raise(s => s.RefreshCompleted += null, scoreMock.Object, EventArgs.Empty);
                return Task.CompletedTask;
            });

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(scoreMock.Object);
        ctx.Services.AddSingleton(autoMock.Object);
        ctx.Services.AddSingleton<IManualModeService>(new Mock<IManualModeService>().Object);
        ctx.Services.AddSingleton(notificationSvc);
        ctx.Services.AddSingleton<ILogger<RefreshScoreboardButton>>(NullLogger<RefreshScoreboardButton>.Instance);

        // Render both components as siblings in the same context
        var cut = ctx.Render(builder =>
        {
            builder.OpenComponent<RefreshScoreboardButton>(0);
            builder.CloseComponent();
            builder.OpenComponent<ScoreboardPreview>(1);
            builder.CloseComponent();
        });

        cut.Find(".refresh-scoreboard-button").Click();

        cut.WaitForAssertion(() =>
        {
            // ScoreboardPreview updated — scoreboard-image present with JPEG data URI
            var src = cut.Find(".scoreboard-image").GetAttribute("src")!;
            src.Should().StartWith("data:image/jpeg;base64,");

            // Success notification emitted
            notifications.Should().ContainSingle(n =>
                n.Summary == "Scoreboard refreshed" && n.Severity == NotificationSeverity.Success);
        });
    }
}
