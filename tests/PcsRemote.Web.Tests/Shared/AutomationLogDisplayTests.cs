using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PcsRemote.Core;
using PcsRemote.Web.Shared;

namespace PcsRemote.Web.Tests.Shared;

[TestClass]
public sealed class AutomationLogDisplayTests
{
    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static Mock<IAutomationLogService> BuildMock(
        IReadOnlyList<AutomationLogEntry>? entries = null)
    {
        var mock = new Mock<IAutomationLogService>();
        mock.Setup(s => s.GetRecentEntries())
            .Returns(entries ?? Array.Empty<AutomationLogEntry>());
        return mock;
    }

    private static IRenderedComponent<AutomationLogDisplay> Render(
        Mock<IAutomationLogService> mock)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(mock.Object);
        return ctx.Render<AutomationLogDisplay>();
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-8  Initial load — entries from GetRecentEntries displayed
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void InitialLoad_DisplaysEntriesFromGetRecentEntries()
    {
        var entries = new List<AutomationLogEntry>
        {
            new(DateTimeOffset.UtcNow.AddMinutes(-2), "Launching PCS Pro", AutomationLogOutcome.Info),
            new(DateTimeOffset.UtcNow.AddMinutes(-1), "Login succeeded", AutomationLogOutcome.Success)
        };
        var mock = BuildMock(entries.AsReadOnly());
        var cut = Render(mock);

        var items = cut.FindAll(".automation-log-entry");
        items.Should().HaveCount(2);
        items[0].TextContent.Should().Contain("Launching PCS Pro");
        items[1].TextContent.Should().Contain("Login succeeded");
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-9  Real-time entry — new entry appears when EntryAdded fires
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void EntryAdded_NewEntryAppearsInDisplay()
    {
        var mock = BuildMock();
        var cut = Render(mock);

        cut.FindAll(".automation-log-entry").Should().BeEmpty();

        var entry = new AutomationLogEntry(
            DateTimeOffset.UtcNow, "New action", AutomationLogOutcome.Success);

        cut.InvokeAsync(() =>
            mock.Raise(s => s.EntryAdded += null, mock.Object, entry));

        cut.FindAll(".automation-log-entry").Should().ContainSingle();
        cut.Find(".automation-log-entry").TextContent.Should().Contain("New action");
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-10  Entry format — timestamp, action, outcome
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void EntryFormat_ShowsTimestampActionOutcome()
    {
        var timestamp = new DateTimeOffset(2025, 6, 15, 14, 30, 45, TimeSpan.Zero);
        var entries = new List<AutomationLogEntry>
        {
            new(timestamp, "Match loaded", AutomationLogOutcome.Success)
        };
        var mock = BuildMock(entries.AsReadOnly());
        var cut = Render(mock);

        var entry = cut.Find(".automation-log-entry");
        cut.Find(".automation-log-timestamp").Should().NotBeNull();
        cut.Find(".automation-log-action").TextContent.Should().Contain("Match loaded");
        cut.Find(".automation-log-outcome").TextContent.Should().Contain("Success");
    }

    // ──────────────────────────────────────────────────────────────────────
    // TC-11  Display cap — at 200, adding 201st removes oldest
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void DisplayCap_At200_Adding201stRemovesOldest()
    {
        var entries = Enumerable.Range(0, 200)
            .Select(i => new AutomationLogEntry(
                DateTimeOffset.UtcNow, $"Entry-{i}", AutomationLogOutcome.Info))
            .ToList();
        var mock = BuildMock(entries.AsReadOnly());
        var cut = Render(mock);

        cut.FindAll(".automation-log-entry").Should().HaveCount(200);

        var newEntry = new AutomationLogEntry(
            DateTimeOffset.UtcNow, "Entry-200", AutomationLogOutcome.Success);

        cut.InvokeAsync(() =>
            mock.Raise(s => s.EntryAdded += null, mock.Object, newEntry));

        var items = cut.FindAll(".automation-log-entry");
        items.Should().HaveCount(200);
        items[0].TextContent.Should().Contain("Entry-1");
        items[199].TextContent.Should().Contain("Entry-200");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Empty state — shows "no entries" message
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void EmptyState_ShowsNoEntriesMessage()
    {
        var mock = BuildMock();
        var cut = Render(mock);

        cut.FindAll(".automation-log-empty").Should().ContainSingle();
        cut.FindAll(".automation-log-entry").Should().BeEmpty();
    }
}
