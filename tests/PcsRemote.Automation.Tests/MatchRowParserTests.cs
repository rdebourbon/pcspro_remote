using FluentAssertions;
using PcsRemote.Core;

namespace PcsRemote.Automation.Tests;

[TestClass]
public sealed class MatchRowParserTests
{
    // ── TryParse — valid input ────────────────────────────────────────────

    [TestMethod]
    public void TryParse_WhenWellFormedRow_ReturnsTrueAndPopulatesMatchInfo()
    {
        const string row = "15/06/2025|Ashtead CC 1st XI|Reigate CC 1st XI|Surrey Championship|League|The Green|In Progress|Yes|Online|J Smith|No";

        var success = MatchRowParser.TryParse(row, out var result);

        success.Should().BeTrue();
        result.Should().NotBeNull();
        result!.HomeTeam.Should().Be("Ashtead CC 1st XI"); // NotBeNull() above guarantees non-null
        result.AwayTeam.Should().Be("Reigate CC 1st XI");
        result.MatchType.Should().Be("League");
        result.MatchDate.Should().Be(new DateOnly(2025, 6, 15));
    }

    [TestMethod]
    public void TryParse_MatchId_IsDeterministicCompositeKey()
    {
        const string row = "15/06/2025|Home XI|Away XI|Competition|Club T20|Venue|State|No|Online|Scorer|No";

        MatchRowParser.TryParse(row, out var result);

        result.Should().NotBeNull();
        result!.MatchId.Should().Be("2025-06-15_Home XI_Away XI_Club T20"); // NotBeNull() above guarantees non-null
    }

    [TestMethod]
    public void TryParse_WhenMinimumSegments_ReturnsTrueWithCorrectFields()
    {
        const string row = "01/01/2025|Team A|Team B|Comp|Friendly";

        var success = MatchRowParser.TryParse(row, out var result);

        success.Should().BeTrue();
        result.Should().NotBeNull();
        result!.HomeTeam.Should().Be("Team A"); // NotBeNull() above guarantees non-null
        result.AwayTeam.Should().Be("Team B");
        result.MatchType.Should().Be("Friendly");
        result.MatchDate.Should().Be(new DateOnly(2025, 1, 1));
    }

    // ── TryParse — invalid input ──────────────────────────────────────────

    [TestMethod]
    public void TryParse_WhenEmpty_ReturnsFalseAndDoesNotThrow()
    {
        var success = MatchRowParser.TryParse(string.Empty, out var result);

        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [TestMethod]
    public void TryParse_WhenWhitespace_ReturnsFalseAndDoesNotThrow()
    {
        var success = MatchRowParser.TryParse("   ", out var result);

        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [TestMethod]
    public void TryParse_WhenTooFewSegments_ReturnsFalse()
    {
        var success = MatchRowParser.TryParse("15/06/2025|Team A|Team B|Comp", out var result);

        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [TestMethod]
    public void TryParse_WhenInvalidDate_ReturnsFalse()
    {
        var success = MatchRowParser.TryParse("not-a-date|Team A|Team B|Comp|League", out var result);

        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [TestMethod]
    public void TryParse_WhenWrongDateFormat_ReturnsFalse()
    {
        // US date format should fail — expects dd/MM/yyyy
        var success = MatchRowParser.TryParse("06/15/2025|Team A|Team B|Comp|League", out var result);

        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [TestMethod]
    public void TryParse_WhenHomeTeamEmpty_ReturnsFalse()
    {
        var success = MatchRowParser.TryParse("15/06/2025||Away XI|Comp|League", out var result);

        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [TestMethod]
    public void TryParse_WhenAwayTeamEmpty_ReturnsFalse()
    {
        var success = MatchRowParser.TryParse("15/06/2025|Home XI||Comp|League", out var result);

        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [TestMethod]
    public void TryParse_WhenMatchTypeEmpty_ReturnsFalse()
    {
        var success = MatchRowParser.TryParse("15/06/2025|Home XI|Away XI|Comp|", out var result);

        success.Should().BeFalse();
        result.Should().BeNull();
    }

    // ── FilterToday ──────────────────────────────────────────────────────

    [TestMethod]
    public void FilterToday_WhenSomeMatchesToday_ReturnsOnlyTodaysMatches()
    {
        var today = new DateOnly(2025, 6, 15);
        var matches = new List<MatchInfo>
        {
            new MatchInfo("1") with { MatchDate = today },
            new MatchInfo("2") with { MatchDate = today.AddDays(-1) },
            new MatchInfo("3") with { MatchDate = today },
            new MatchInfo("4") with { MatchDate = today.AddDays(1) },
        };

        var result = MatchRowParser.FilterToday(matches, today);

        result.Should().HaveCount(2);
        result.Select(m => m.MatchId).Should().BeEquivalentTo(["1", "3"]);
    }

    [TestMethod]
    public void FilterToday_WhenNoMatchesForToday_ReturnsEmptyList()
    {
        var today = new DateOnly(2025, 6, 15);
        var matches = new List<MatchInfo>
        {
            new MatchInfo("1") with { MatchDate = today.AddDays(-1) },
            new MatchInfo("2") with { MatchDate = today.AddDays(1) },
        };

        var result = MatchRowParser.FilterToday(matches, today);

        result.Should().BeEmpty();
    }

    [TestMethod]
    public void FilterToday_WhenEmptyCollection_ReturnsEmptyList()
    {
        var result = MatchRowParser.FilterToday([], new DateOnly(2025, 6, 15));

        result.Should().BeEmpty();
    }
}
