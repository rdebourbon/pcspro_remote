using PcsRemote.Core;

namespace PcsRemote.Automation;

/// <summary>
/// Abstracts FlaUI element interactions required for the PCS Pro team name extraction phase.
/// Allows <see cref="PcsProAutomationService"/> to be tested without a live FlaUI session.
/// </summary>
/// <remarks>
/// Methods are classified as either <em>probe-like</em> (never throw; safe defaults) or
/// <em>interaction</em> (may throw FlaUI exceptions — COMException, ElementNotAvailableException, etc.).
/// Service-level catch blocks handle interaction exceptions; see SPEC-S-005 §4.
/// AutomationId constants (I-U-2) and the ComboBox read strategy (I-U-3) are resolved
/// via Inspect.exe during the garage PC development session.
/// </remarks>
internal interface ITeamNamesAutomation
{
    /// <summary>
    /// Navigates to the Scoring menu and opens the Match Details/Teams dialog.
    /// AutomationId placeholders must be replaced during garage PC development (I-U-2).
    /// </summary>
    /// <remarks><b>Interaction method — may throw.</b></remarks>
    void OpenTeamsDialog();

    /// <summary>
    /// Returns the home team's club and team name information using the access pattern
    /// resolved for I-U-3 (ValuePattern, SelectedItem, or element Name).
    /// AutomationId placeholder must be replaced during garage PC development (I-U-2).
    /// </summary>
    /// <remarks><b>Interaction method — may throw.</b></remarks>
    TeamNameInfo ReadHomeTeamName();

    /// <summary>
    /// Returns the away team's club and team name information using the same access pattern
    /// as <see cref="ReadHomeTeamName"/>.
    /// AutomationId placeholder must be replaced during garage PC development (I-U-2).
    /// </summary>
    /// <remarks><b>Interaction method — may throw.</b></remarks>
    TeamNameInfo ReadAwayTeamName();

    /// <summary>
    /// Attempts to close the Match Details/Teams dialog. Best-effort: does not throw if
    /// close fails or if the dialog is no longer present.
    /// </summary>
    void TryCloseTeamsDialog();

    /// <summary>
    /// Returns <see langword="true"/> when a dialog other than the expected teams dialog
    /// is present. Does not throw.
    /// </summary>
    bool IsUnexpectedDialogPresent();

    /// <summary>
    /// Attempts to dismiss the unexpected dialog. Best-effort: does not throw if close fails
    /// or if no dialog is found.
    /// </summary>
    void TryCloseUnexpectedDialog();
}
