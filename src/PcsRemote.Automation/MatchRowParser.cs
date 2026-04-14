using PcsRemote.Core;

namespace PcsRemote.Automation;

/// <summary>
/// Parses raw DataGrid row text strings from PCS Pro into <see cref="MatchInfo"/> records.
/// All methods are pure functions with no side effects or clock dependencies.
/// </summary>
/// <remarks>
/// The exact DataGrid row text format is unknown until garage PC discovery (I-U-5).
/// The <c>TODO_REPLACE_ON_GARAGE_PC</c> format constant must be updated once the actual
/// format is discovered via Inspect.exe.
/// </remarks>
internal static class MatchRowParser
{
    // I-U-5: The exact row text format must be discovered on the garage PC via Inspect.exe.
    // Replace this constant with the actual delimiter / format pattern once known.
    private const string RowFormatPattern = "TODO_REPLACE_ON_GARAGE_PC";

    /// <summary>
    /// Attempts to parse a raw DataGrid row text string into a <see cref="MatchInfo"/> record.
    /// </summary>
    /// <param name="rowText">The raw text content of a DataGrid row.</param>
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
            return false;

        // TODO_REPLACE_ON_GARAGE_PC: implement parsing once the actual row text format
        // is discovered via Inspect.exe. Until then, this method always returns false so
        // that the service correctly handles "zero parseable rows" in stub mode.
        _ = RowFormatPattern; // suppress unused-constant warning until format is known
        return false;
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
