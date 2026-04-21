using System.Globalization;
using PcsRemote.Core;

namespace PcsRemote.Automation;

/// <summary>
/// Parses raw DataGrid row text strings from PCS Pro into <see cref="MatchInfo"/> records.
/// All methods are pure functions with no side effects or clock dependencies.
/// </summary>
/// <remarks>
/// Row text is pipe-delimited, produced by <see cref="FlaUiMatchSelectionAutomation.ReadDataGridRowTexts"/>.
/// Column order: Date(0) | Team 1(1) | Team 2(2) | Competition(3) | Match Type(4) | ...
/// </remarks>
internal static class MatchRowParser
{
    private const char Delimiter = '|';
    private const int MinSegmentCount = 5;
    private const int DateColumnIndex = 0;
    private const int MatchTypeColumnIndex = 4;
    private const string DateFormat = "dd/MM/yyyy";
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    /// <summary>
    /// Attempts to parse a raw DataGrid row text string into a <see cref="MatchInfo"/> record.
    /// </summary>
    /// <param name="rowText">The pipe-delimited text content of a DataGrid row.</param>
    /// <param name="result">
    /// When this method returns <see langword="true"/>, contains the parsed <see cref="MatchInfo"/>.
    /// When this method returns <see langword="false"/>, contains <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the row text matched the expected format and all fields were
    /// populated; <see langword="false"/> otherwise. Never throws.
    /// </returns>
    public static bool TryParse(string rowText, out MatchInfo? result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(rowText))
        {
            return false;
        }

        var segments = rowText.Split(Delimiter);
        if (segments.Length < MinSegmentCount)
        {
            return false;
        }

        var dateText = segments[DateColumnIndex].Trim();
        if (!DateOnly.TryParseExact(dateText, DateFormat, EnGb, DateTimeStyles.None, out var matchDate))
        {
            return false;
        }

        var homeTeam = segments[KnownElements.GridColumnTeam1].Trim();
        var awayTeam = segments[KnownElements.GridColumnTeam2].Trim();
        var matchType = segments[MatchTypeColumnIndex].Trim();
        var matchId = $"{matchDate:yyyy-MM-dd}_{homeTeam}_{awayTeam}_{matchType}";

        result = new MatchInfo(matchId, homeTeam, awayTeam, matchType, matchDate);
        return true;
    }

    /// <summary>
    /// Filters a collection of <see cref="MatchInfo"/> records to those whose
    /// <see cref="MatchInfo.MatchDate"/> equals <paramref name="today"/>.
    /// </summary>
    /// <param name="matches">The full collection of parsed match records.</param>
    /// <param name="today">
    /// The date to filter on. Callers derive this from <c>TimeProvider</c> to keep
    /// this function pure and testable.
    /// </param>
    /// <returns>A new list containing only the matches for <paramref name="today"/>.</returns>
    public static IReadOnlyList<MatchInfo> FilterToday(
        IEnumerable<MatchInfo> matches,
        DateOnly today)
    {
        return matches.Where(m => m.MatchDate == today).ToList();
    }
}
