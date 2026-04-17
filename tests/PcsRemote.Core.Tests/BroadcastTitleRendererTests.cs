using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PcsRemote.Core.Tests;

[TestClass]
public class BroadcastTitleRendererTests
{
    private readonly BroadcastTitleRenderer _renderer =
        new(NullLogger<BroadcastTitleRenderer>.Instance);

    private readonly MatchInfo _match = new(
        MatchId: "12345",
        HomeTeam: "High Halstow",
        AwayTeam: "Cobham",
        MatchType: "Club T20",
        MatchDate: new DateOnly(2026, 7, 12));

    [TestMethod]
    public void Render_HomeTeamToken_SubstitutedCorrectly()
    {
        var result = _renderer.Render("{HomeTeam}", _match);

        result.Should().Be("High Halstow");
    }

    [TestMethod]
    public void Render_AwayTeamToken_SubstitutedCorrectly()
    {
        var result = _renderer.Render("{AwayTeam}", _match);

        result.Should().Be("Cobham");
    }

    [TestMethod]
    public void Render_MatchTypeToken_SubstitutedCorrectly()
    {
        var result = _renderer.Render("{MatchType}", _match);

        result.Should().Be("Club T20");
    }

    [TestMethod]
    public void Render_DateTokenNoFormat_UsesShortDate()
    {
        var result = _renderer.Render("{Date}", _match);

        var expected = new DateOnly(2026, 7, 12).ToString("d");
        result.Should().Be(expected);
    }

    [TestMethod]
    public void Render_DateTokenWithValidFormat_FormatsCorrectly()
    {
        var result = _renderer.Render("{Date:yyyy-MM-dd}", _match);

        result.Should().Be("2026-07-12");
    }

    [TestMethod]
    public void Render_DateTokenWithTimeFormat_FallsBackToShortDate()
    {
        var result = _renderer.Render("{Date:HHmm}", _match);

        var expected = new DateOnly(2026, 7, 12).ToString("d");
        result.Should().Be(expected);
    }

    [TestMethod]
    public void Render_UnknownToken_RetainedLiterally()
    {
        var result = _renderer.Render("{Venue}", _match);

        result.Should().Be("{Venue}");
    }

    [TestMethod]
    public void Render_EmptyMatchField_SubstitutedAsEmpty()
    {
        var match = new MatchInfo(
            MatchId: "12345",
            HomeTeam: "",
            AwayTeam: "Cobham");

        var result = _renderer.Render("{HomeTeam} vs {AwayTeam}", match);

        result.Should().Be(" vs Cobham");
    }

    [TestMethod]
    public void Render_LongTitle_TruncatedWithEllipsis()
    {
        var match = new MatchInfo(
            MatchId: "1",
            HomeTeam: new string('A', 60),
            AwayTeam: new string('B', 60));

        var result = _renderer.Render("{HomeTeam} vs {AwayTeam}", match);

        result.Should().HaveLength(100);
        result.Should().EndWith("...");
    }

    [TestMethod]
    public void Render_ExactlyMaxLength_NotTruncated()
    {
        // "X vs Y" = 96 chars for home + " vs " (4) + away = 100
        var match = new MatchInfo(
            MatchId: "1",
            HomeTeam: new string('A', 48),
            AwayTeam: new string('B', 48));

        var result = _renderer.Render("{HomeTeam} vs {AwayTeam}", match);

        result.Should().HaveLength(100);
        result.Should().NotEndWith("...");
    }

    [TestMethod]
    public void Render_EmptyTemplate_UsesDefault()
    {
        var result = _renderer.Render("", _match);

        result.Should().Be("High Halstow vs Cobham");
    }

    [TestMethod]
    public void Render_NullTemplate_UsesDefault()
    {
        var result = _renderer.Render(null, _match);

        result.Should().Be("High Halstow vs Cobham");
    }

    [TestMethod]
    public void Render_WhitespaceTemplate_UsesDefault()
    {
        var result = _renderer.Render("   ", _match);

        result.Should().Be("High Halstow vs Cobham");
    }

    [TestMethod]
    public void Render_MultipleTokens_AllSubstituted()
    {
        var result = _renderer.Render(
            "{HomeTeam} vs {AwayTeam} - {MatchType} on {Date:yyyy-MM-dd}",
            _match);

        result.Should().Be("High Halstow vs Cobham - Club T20 on 2026-07-12");
    }

    [TestMethod]
    public void Render_DateTokenWithTimeFormat_LogsWarning()
    {
        var logger = new CapturingLogger<BroadcastTitleRenderer>();
        var renderer = new BroadcastTitleRenderer(logger);

        renderer.Render("{Date:HHmm}", _match);

        logger.Entries.Should().ContainSingle()
            .Which.LogLevel.Should().Be(LogLevel.Warning);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }

        public record LogEntry(LogLevel LogLevel, string Message);
    }
}
