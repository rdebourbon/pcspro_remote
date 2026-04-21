using PcsRemote.Core;

namespace PcsRemote.Automation;

/// <summary>
/// Abstracts FlaUI element interactions required for the PCS Pro match selection phase.
/// Allows <see cref="PcsProAutomationService"/> to be tested without a live FlaUI session.
/// </summary>
/// <remarks>
/// Methods are classified as either <em>probe</em> (never throw; return safe defaults) or
/// <em>interaction</em> (may throw FlaUI exceptions — COMException, ElementNotAvailableException, etc.).
/// Service-level catch blocks handle interaction exceptions; see SPEC-S-004 §4.6 and §5.5.
/// </remarks>
internal interface IMatchSelectionAutomation
{
    /// <summary>
    /// Opens the match selection dialog in PCS Pro and triggers a search for today's matches.
    /// AutomationId placeholders must be replaced during garage PC development (I-U-5).
    /// </summary>
    /// <remarks><b>Interaction method — may throw.</b></remarks>
    void OpenMatchDialogAndSearch();

    /// <summary>
    /// Returns <see langword="true"/> when the search-in-progress spinner element is visible.
    /// Returns <see langword="false"/> when not found or not visible.
    /// Does not throw.
    /// </summary>
    bool IsSpinnerVisible();

    /// <summary>
    /// Returns <see langword="true"/> when a dialog other than the expected match selection
    /// dialog is present. Does not throw.
    /// </summary>
    bool IsUnexpectedDialogPresent();

    /// <summary>
    /// Attempts to dismiss the unexpected dialog. Best-effort: does not throw if close fails
    /// or if no dialog is found.
    /// </summary>
    void TryCloseUnexpectedDialog();

    /// <summary>
    /// Returns the text content of each DataGrid row as a list of strings.
    /// An empty list means a successful read with zero rows; any FlaUI failure throws.
    /// AutomationId placeholder must be replaced during garage PC development (I-U-5).
    /// </summary>
    /// <remarks><b>Interaction method — may throw.</b></remarks>
    IReadOnlyList<string> ReadDataGridRowTexts();

    /// <summary>
    /// Locates the DataGrid row whose team names and match type match the given
    /// <see cref="MatchInfo"/> and clicks "Open Read-Only."
    /// The row is matched by <see cref="MatchInfo.HomeTeam"/>, <see cref="MatchInfo.AwayTeam"/>,
    /// and <see cref="MatchInfo.MatchType"/> — not by <see cref="MatchInfo.MatchId"/> (which is a
    /// synthesized composite key not present in the grid).
    /// </summary>
    /// <remarks><b>Interaction method — may throw.</b></remarks>
    void SelectAndOpenMatch(MatchInfo match);

    /// <summary>
    /// Returns <see langword="true"/> when the match has loaded (dialog closed / match view visible).
    /// Returns <see langword="false"/> when not yet loaded. Does not throw.
    /// </summary>
    bool IsMatchLoaded();
}
