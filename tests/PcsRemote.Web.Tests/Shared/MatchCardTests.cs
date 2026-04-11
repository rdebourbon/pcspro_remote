using Bunit;
using FluentAssertions;
using PcsRemote.Core;
using PcsRemote.Web.Shared;

namespace PcsRemote.Web.Tests.Shared;

[TestClass]
public class MatchCardTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // AC-1: All data fields render with correct values and date format
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Render_ShowsAllMatchDataFields()
    {
        var match = TestMatch();
        using var ctx = new BunitContext();

        var cut = ctx.Render<MatchCard>(p => p
            .Add(c => c.Match, match)
            .Add(c => c.IsInteractive, true));

        cut.Find(".match-card-home").TextContent.Trim().Should().Be("High Halstow CC");
        cut.Find(".match-card-away").TextContent.Trim().Should().Be("Visitors CC");
        cut.Find(".match-card-type").TextContent.Trim().Should().Be("League");
        // AC-1: date must render as "d MMM yyyy" with InvariantCulture → "20 Jun 2026"
        cut.Find(".match-card-date").TextContent.Trim().Should().Be("20 Jun 2026");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-2: Interactive — root has match-card only (no disabled modifier)
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Render_Interactive_HasMatchCardClassWithoutDisabled()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<MatchCard>(p => p
            .Add(c => c.Match, TestMatch())
            .Add(c => c.IsInteractive, true));

        var card = cut.Find(".match-card");
        card.ClassList.Should().Contain("match-card");
        card.ClassList.Should().NotContain("match-card--disabled");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-3: Non-interactive — root has both match-card and match-card--disabled
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Render_NonInteractive_HasBothMatchCardClasses()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<MatchCard>(p => p
            .Add(c => c.Match, TestMatch())
            .Add(c => c.IsInteractive, false));

        var card = cut.Find(".match-card");
        card.ClassList.Should().Contain("match-card");
        card.ClassList.Should().Contain("match-card--disabled");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-4: Click on interactive card raises OnSelected with correct MatchInfo
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Click_WhenInteractive_RaisesOnSelectedWithMatch()
    {
        MatchInfo? raised = null;
        var match = TestMatch();
        using var ctx = new BunitContext();

        var cut = ctx.Render<MatchCard>(p => p
            .Add(c => c.Match, match)
            .Add(c => c.IsInteractive, true)
            .Add(c => c.OnSelected, (MatchInfo m) => { raised = m; }));

        cut.Find(".match-card").Click();

        cut.WaitForAssertion(() => raised.Should().Be(match));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-5: Non-interactive card has no onclick handler — bUnit raises
    //       MissingEventHandlerException, confirming OnSelected cannot fire
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Click_WhenNonInteractive_DoesNotRaiseOnSelected()
    {
        MatchInfo? raised = null;
        using var ctx = new BunitContext();

        var cut = ctx.Render<MatchCard>(p => p
            .Add(c => c.Match, TestMatch())
            .Add(c => c.IsInteractive, false)
            .Add(c => c.OnSelected, (MatchInfo m) => { raised = m; }));

        // bUnit throws MissingEventHandlerException when no @onclick is present,
        // confirming the non-interactive branch omits the handler entirely.
        Action click = () => cut.Find(".match-card").Click();
        click.Should().Throw<Bunit.MissingEventHandlerException>();
        raised.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static MatchInfo TestMatch() =>
        new("1", "High Halstow CC", "Visitors CC", "League", new DateOnly(2026, 6, 20));
}
