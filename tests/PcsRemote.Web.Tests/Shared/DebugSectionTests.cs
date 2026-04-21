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

    private static IRenderedComponent<DebugSection> Render(
        string? pin = null,
        Mock<IAutomationLogService>? logMock = null)
    {
        var ctx = new BunitContext();
        var options = Options.Create(new DebugSectionOptions { Pin = pin });
        ctx.Services.AddSingleton(options);
        logMock ??= new Mock<IAutomationLogService>();
        logMock.Setup(s => s.GetRecentEntries())
            .Returns(Array.Empty<AutomationLogEntry>());
        ctx.Services.AddSingleton(logMock.Object);
        return ctx.Render<DebugSection>();
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-1  Default state — collapsed, no content visible
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void DefaultState_CollapsedNoContentVisible()
    {
        var cut = Render();

        cut.Find(".debug-section-toggle").TextContent.Should().Contain("Debug");
        cut.FindAll(".debug-section-content").Should().BeEmpty();
        cut.FindAll(".debug-section-pin-prompt").Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-2  No PIN configured — toggle expands freely
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void NoPinConfigured_ToggleExpandsFreely()
    {
        var cut = Render(pin: null);

        cut.Find(".debug-section-toggle").Click();

        cut.FindAll(".debug-section-content").Should().ContainSingle();
        cut.FindAll(".debug-section-pin-prompt").Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-3  PIN configured — toggle shows PIN prompt
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void PinConfigured_ToggleShowsPinPrompt()
    {
        var cut = Render(pin: "1234");

        cut.Find(".debug-section-toggle").Click();

        cut.FindAll(".debug-section-pin-prompt").Should().ContainSingle();
        cut.FindAll(".debug-section-content").Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-4  Correct PIN — unlocks and expands
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CorrectPin_UnlocksAndExpands()
    {
        var cut = Render(pin: "1234");

        cut.Find(".debug-section-toggle").Click();
        cut.Find(".debug-section-pin-input").Input("1234");
        cut.Find(".debug-section-pin-submit").Click();

        cut.FindAll(".debug-section-content").Should().ContainSingle();
        cut.FindAll(".debug-section-pin-prompt").Should().BeEmpty();
        cut.FindAll(".debug-section-pin-error").Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-5  Incorrect PIN — error shown, section stays locked
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void IncorrectPin_ShowsError_StaysLocked()
    {
        var cut = Render(pin: "1234");

        cut.Find(".debug-section-toggle").Click();
        cut.Find(".debug-section-pin-input").Input("wrong");
        cut.Find(".debug-section-pin-submit").Click();

        cut.FindAll(".debug-section-pin-error").Should().ContainSingle();
        cut.FindAll(".debug-section-content").Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-6  Unlock persists across re-renders
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void UnlockPersistsAcrossReRenders()
    {
        var cut = Render(pin: "1234");

        cut.Find(".debug-section-toggle").Click();
        cut.Find(".debug-section-pin-input").Input("1234");
        cut.Find(".debug-section-pin-submit").Click();

        // Force re-render
        cut.Render();

        cut.FindAll(".debug-section-content").Should().ContainSingle();
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-7  Collapse and re-expand does not re-prompt for PIN
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CollapseAndReExpand_DoesNotRePromptForPin()
    {
        var cut = Render(pin: "1234");

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
