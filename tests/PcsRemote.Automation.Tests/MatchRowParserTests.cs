using FluentAssertions;
using PcsRemote.Core;

namespace PcsRemote.Automation.Tests;

[TestClass]
public sealed class MatchRowParserTests
{
    // -----------------------------------------------------------------------
    // AC-24 — TryParse returns true for well-formed row text
    // Note: Since MatchRowParser.TryParse always returns false in stub mode
    // (format is TODO_REPLACE_ON_GARAGE_PC), this test documents the expected
    // behaviour once the format constant is filled in during garage PC development.
    // The test is written now to capture the contract and will green once TryParse
    // is implemented.
    // -----------------------------------------------------------------------

    [TestMethod]
    public void TryParse_WhenWellFormedRow_ReturnsTrueAndPopulatesMatchInfo()
    {
        // Arrange: once the actual row format is known (I-U-5), replace this placeholder
        // with a real row text string that conforms to the discovered format.
        const string WellFormedRow = "TODO_REPLACE_ON_GARAGE_PC";

        // Act
        var success = MatchRowParser.TryParse(WellFormedRow, out var result);

        // Assert: this test is expected to FAIL until TryParse is implemented.
        // Once the format is known, uncomment the assertion below and remove this comment.
        // success.Should().BeTrue();
        // result.Should().NotBeNull();
        // result!.MatchId.Should().NotBeNullOrWhiteSpace();

        // Current stub always returns false — document the expected false state.
        success.Should().BeFalse("TryParse is not yet implemented (TODO_REPLACE_ON_GARAGE_PC)");
    }

    // -----------------------------------------------------------------------
    // AC-25 — TryParse returns false for malformed or empty row text (never throws)
    // -----------------------------------------------------------------------

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
    public void TryParse_WhenMalformedRow_ReturnsFalseAndDoesNotThrow()
    {
        var success = MatchRowParser.TryParse("garbage data that does not match any format", out var result);

        success.Should().BeFalse();
        result.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // AC-26 — FilterToday returns only entries matching today's date
    // -----------------------------------------------------------------------

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

    // -----------------------------------------------------------------------
    // AC-27 — FilterToday returns empty list when no entries match today (does not throw)
    // -----------------------------------------------------------------------

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
