using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web;
using PcsRemote.Web.Shared;

namespace PcsRemote.Web.Tests.Shared;

[TestClass]
public sealed class DebugSectionTests
{
    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static (
        IRenderedComponent<DebugSection> Cut,
        Mock<IPcsProAutomationService> AutomationMock,
        Mock<IYouTubeLiveStreamService> StreamMock,
        Mock<IConfirmDialogService> ConfirmMock,
        BunitContext Ctx)
    Render(
        string? pin = null,
        Mock<IAutomationLogService>? logMock = null,
        LiveStreamStatus streamStatus = LiveStreamStatus.Idle,
        PcsProState automationState = PcsProState.NotRunning)
    {
        var ctx = new BunitContext();
        var options = Options.Create(new DebugSectionOptions { Pin = pin });
        ctx.Services.AddSingleton(options);
        logMock ??= new Mock<IAutomationLogService>();
        logMock.Setup(s => s.GetRecentEntries())
            .Returns(Array.Empty<AutomationLogEntry>());
        ctx.Services.AddSingleton(logMock.Object);

        var automationMock = new Mock<IPcsProAutomationService>();
        automationMock.Setup(a => a.CurrentState).Returns(automationState);
        ctx.Services.AddSingleton(automationMock.Object);

        var streamMock = new Mock<IYouTubeLiveStreamService>();
        streamMock.Setup(s => s.CurrentStatus).Returns(streamStatus);
        ctx.Services.AddSingleton(streamMock.Object);

        var confirmMock = new Mock<IConfirmDialogService>();
        ctx.Services.AddSingleton(confirmMock.Object);

        var cut = ctx.Render<DebugSection>();
        return (cut, automationMock, streamMock, confirmMock, ctx);
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-1  Default state — collapsed, no content visible
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void DefaultState_CollapsedNoContentVisible()
    {
        var (cut, _, _, _, ctx) = Render();
        using (ctx)
        {
            cut.Find(".debug-section-toggle").TextContent.Should().Contain("Debug");
            cut.FindAll(".debug-section-content").Should().BeEmpty();
            cut.FindAll(".debug-section-pin-prompt").Should().BeEmpty();
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-2  No PIN configured — toggle expands freely
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void NoPinConfigured_ToggleExpandsFreely()
    {
        var (cut, _, _, _, ctx) = Render(pin: null);
        using (ctx)
        {
            cut.Find(".debug-section-toggle").Click();

            cut.FindAll(".debug-section-content").Should().ContainSingle();
            cut.FindAll(".debug-section-pin-prompt").Should().BeEmpty();
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-3  PIN configured — toggle shows PIN prompt
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void PinConfigured_ToggleShowsPinPrompt()
    {
        var (cut, _, _, _, ctx) = Render(pin: "1234");
        using (ctx)
        {
            cut.Find(".debug-section-toggle").Click();

            cut.FindAll(".debug-section-pin-prompt").Should().ContainSingle();
            cut.FindAll(".debug-section-content").Should().BeEmpty();
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-4  Correct PIN — unlocks and expands
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CorrectPin_UnlocksAndExpands()
    {
        var (cut, _, _, _, ctx) = Render(pin: "1234");
        using (ctx)
        {
            cut.Find(".debug-section-toggle").Click();
            cut.Find(".debug-section-pin-input").Input("1234");
            cut.Find(".debug-section-pin-submit").Click();

            cut.FindAll(".debug-section-content").Should().ContainSingle();
            cut.FindAll(".debug-section-pin-prompt").Should().BeEmpty();
            cut.FindAll(".debug-section-pin-error").Should().BeEmpty();
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-5  Incorrect PIN — error shown, section stays locked
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void IncorrectPin_ShowsError_StaysLocked()
    {
        var (cut, _, _, _, ctx) = Render(pin: "1234");
        using (ctx)
        {
            cut.Find(".debug-section-toggle").Click();
            cut.Find(".debug-section-pin-input").Input("wrong");
            cut.Find(".debug-section-pin-submit").Click();

            cut.FindAll(".debug-section-pin-error").Should().ContainSingle();
            cut.FindAll(".debug-section-content").Should().BeEmpty();
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-6  Unlock persists across re-renders
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void UnlockPersistsAcrossReRenders()
    {
        var (cut, _, _, _, ctx) = Render(pin: "1234");
        using (ctx)
        {
            cut.Find(".debug-section-toggle").Click();
            cut.Find(".debug-section-pin-input").Input("1234");
            cut.Find(".debug-section-pin-submit").Click();

            // Force re-render
            cut.Render();

            cut.FindAll(".debug-section-content").Should().ContainSingle();
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-7  Collapse and re-expand does not re-prompt for PIN
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CollapseAndReExpand_DoesNotRePromptForPin()
    {
        var (cut, _, _, _, ctx) = Render(pin: "1234");
        using (ctx)
        {
            // Unlock
            cut.Find(".debug-section-toggle").Click();
            cut.Find(".debug-section-pin-input").Input("1234");
            cut.Find(".debug-section-pin-submit").Click();

            // Collapse
            cut.Find(".debug-section-toggle").Click();
            cut.FindAll(".debug-section-content").Should().BeEmpty();

            // Re-expand
            cut.Find(".debug-section-toggle").Click();
            cut.FindAll(".debug-section-content").Should().ContainSingle();
            cut.FindAll(".debug-section-pin-prompt").Should().BeEmpty();
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // S-007: Reset Automation button tests
    // ──────────────────────────────────────────────────────────────────────

    // TC-1: Button visible when expanded and unlocked
    [TestMethod]
    public void ResetButton_WhenExpandedAndUnlocked_IsVisible()
    {
        var (cut, _, _, _, ctx) = Render();
        using (ctx)
        {
            cut.Find(".debug-section-toggle").Click();
            cut.Find(".debug-section-reset-btn").TextContent.Should().Contain("Reset Automation");
        }
    }

    // TC-2: Button hidden when collapsed
    [TestMethod]
    public void ResetButton_WhenCollapsed_IsNotVisible()
    {
        var (cut, _, _, _, ctx) = Render();
        using (ctx)
        {
            cut.FindAll(".debug-section-reset-btn").Should().BeEmpty();
        }
    }

    // TC-3: Confirmation dialog shown on click, cancel aborts
    [TestMethod]
    public void ResetButton_WhenCancelled_NoActionTaken()
    {
        var (cut, automationMock, streamMock, confirmMock, ctx) = Render();
        using (ctx)
        {
            confirmMock.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);

            cut.Find(".debug-section-toggle").Click();
            cut.Find(".debug-section-reset-btn").Click();

            cut.WaitForAssertion(() =>
            {
                confirmMock.Verify(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
                streamMock.Verify(s => s.StopStreamAsync(It.IsAny<CancellationToken>()), Times.Never);
                automationMock.Verify(a => a.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
                automationMock.Verify(a => a.LaunchAndLoginAsync(It.IsAny<CancellationToken>()), Times.Never);
            });
        }
    }

    // TC-4: Full reset sequence executes on confirm
    [TestMethod]
    public void ResetButton_WhenConfirmed_ExecutesFullSequence()
    {
        var callOrder = new List<string>();
        var (cut, automationMock, streamMock, confirmMock, ctx) = Render(streamStatus: LiveStreamStatus.Live);
        using (ctx)
        {
            confirmMock.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            streamMock.Setup(s => s.StopStreamAsync(It.IsAny<CancellationToken>()))
                .Callback(() => callOrder.Add("StopStream"))
                .Returns(Task.CompletedTask);
            automationMock.Setup(a => a.StopAsync(It.IsAny<CancellationToken>()))
                .Callback(() => callOrder.Add("Stop"))
                .Returns(Task.CompletedTask);
            automationMock.Setup(a => a.LaunchAndLoginAsync(It.IsAny<CancellationToken>()))
                .Callback(() => callOrder.Add("Launch"))
                .Returns(Task.CompletedTask);

            cut.Find(".debug-section-toggle").Click();
            cut.Find(".debug-section-reset-btn").Click();

            cut.WaitForAssertion(() =>
            {
                callOrder.Should().ContainInOrder("StopStream", "Stop", "Launch");
                cut.Find(".debug-section-reset-btn").HasAttribute("disabled").Should().BeFalse();
            });
        }
    }

    // TC-5: Stream stop failure does not abort reset
    [TestMethod]
    public void ResetButton_StreamStopFails_ContinuesReset()
    {
        var (cut, automationMock, streamMock, confirmMock, ctx) = Render(streamStatus: LiveStreamStatus.Live);
        using (ctx)
        {
            confirmMock.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            streamMock.Setup(s => s.StopStreamAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Stream stop failed"));
            automationMock.Setup(a => a.StopAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            automationMock.Setup(a => a.LaunchAndLoginAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            cut.Find(".debug-section-toggle").Click();
            cut.Find(".debug-section-reset-btn").Click();

            cut.WaitForAssertion(() =>
            {
                streamMock.Verify(s => s.StopStreamAsync(It.IsAny<CancellationToken>()), Times.Once);
                automationMock.Verify(a => a.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
                automationMock.Verify(a => a.LaunchAndLoginAsync(It.IsAny<CancellationToken>()), Times.Once);
            });
        }
    }

    // TC-6: StopAsync failure shows error with PcsProState and allows retry
    [TestMethod]
    public void ResetButton_StopAsyncFails_ShowsErrorWithState()
    {
        var (cut, automationMock, _, confirmMock, ctx) = Render(
            automationState: PcsProState.MatchLoaded);
        using (ctx)
        {
            confirmMock.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            automationMock.Setup(a => a.StopAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Kill failed"));

            cut.Find(".debug-section-toggle").Click();
            cut.Find(".debug-section-reset-btn").Click();

            cut.WaitForAssertion(() =>
            {
                var status = cut.Find(".debug-section-reset-status");
                status.TextContent.Should().Contain("Kill failed");
                status.TextContent.Should().Contain("MatchLoaded");
                cut.Find(".debug-section-reset-btn").HasAttribute("disabled").Should().BeFalse();
                automationMock.Verify(a => a.LaunchAndLoginAsync(It.IsAny<CancellationToken>()), Times.Never);
            });
        }
    }

    // TC-7: Button disabled during reset
    [TestMethod]
    public void ResetButton_DuringReset_IsDisabled()
    {
        var tcs = new TaskCompletionSource();
        var (cut, automationMock, _, confirmMock, ctx) = Render();
        using (ctx)
        {
            confirmMock.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            automationMock.Setup(a => a.StopAsync(It.IsAny<CancellationToken>()))
                .Returns(tcs.Task);

            cut.Find(".debug-section-toggle").Click();
            cut.Find(".debug-section-reset-btn").Click();

            cut.WaitForAssertion(() =>
                cut.Find(".debug-section-reset-btn").HasAttribute("disabled").Should().BeTrue());

            tcs.SetResult();
        }
    }

    // TC-8: Skips stream stop when Idle
    [TestMethod]
    public void ResetButton_WhenIdle_SkipsStreamStop()
    {
        var (cut, automationMock, streamMock, confirmMock, ctx) = Render(streamStatus: LiveStreamStatus.Idle);
        using (ctx)
        {
            confirmMock.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            automationMock.Setup(a => a.StopAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            automationMock.Setup(a => a.LaunchAndLoginAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            cut.Find(".debug-section-toggle").Click();
            cut.Find(".debug-section-reset-btn").Click();

            cut.WaitForAssertion(() =>
            {
                streamMock.Verify(s => s.StopStreamAsync(It.IsAny<CancellationToken>()), Times.Never);
                automationMock.Verify(a => a.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
                automationMock.Verify(a => a.LaunchAndLoginAsync(It.IsAny<CancellationToken>()), Times.Once);
            });
        }
    }

    // TC-9: Skips stream stop when Error
    [TestMethod]
    public void ResetButton_WhenStreamError_SkipsStreamStop()
    {
        var (cut, automationMock, streamMock, confirmMock, ctx) = Render(streamStatus: LiveStreamStatus.Error);
        using (ctx)
        {
            confirmMock.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            automationMock.Setup(a => a.StopAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            automationMock.Setup(a => a.LaunchAndLoginAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            cut.Find(".debug-section-toggle").Click();
            cut.Find(".debug-section-reset-btn").Click();

            cut.WaitForAssertion(() =>
            {
                streamMock.Verify(s => s.StopStreamAsync(It.IsAny<CancellationToken>()), Times.Never);
                automationMock.Verify(a => a.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
            });
        }
    }

    // TC-10: LaunchAndLoginAsync failure shows error with PcsProState
    [TestMethod]
    public void ResetButton_LaunchFails_ShowsErrorWithState()
    {
        var (cut, automationMock, _, confirmMock, ctx) = Render(
            automationState: PcsProState.NotRunning);
        using (ctx)
        {
            confirmMock.Setup(c => c.ConfirmAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            automationMock.Setup(a => a.StopAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            automationMock.Setup(a => a.LaunchAndLoginAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Launch hung"));

            cut.Find(".debug-section-toggle").Click();
            cut.Find(".debug-section-reset-btn").Click();

            cut.WaitForAssertion(() =>
            {
                var status = cut.Find(".debug-section-reset-status");
                status.TextContent.Should().Contain("Launch hung");
                status.TextContent.Should().Contain("NotRunning");
                cut.Find(".debug-section-reset-btn").HasAttribute("disabled").Should().BeFalse();
                automationMock.Verify(a => a.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
            });
        }
    }

    // TC-11: Button hidden when PIN-locked
    [TestMethod]
    public void ResetButton_WhenPinLocked_IsNotVisible()
    {
        var (cut, _, _, _, ctx) = Render(pin: "1234");
        using (ctx)
        {
            cut.Find(".debug-section-toggle").Click();
            // PIN prompt visible, but not unlocked
            cut.FindAll(".debug-section-pin-prompt").Should().ContainSingle();
            cut.FindAll(".debug-section-reset-btn").Should().BeEmpty();
        }
    }
}
