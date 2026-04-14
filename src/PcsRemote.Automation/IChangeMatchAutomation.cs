namespace PcsRemote.Automation;

/// <summary>
/// Abstracts the FlaUI interaction sequence required to close the current loaded match
/// and return the application to the match selection state.
/// </summary>
/// <remarks>
/// Methods are classified as either <em>probe-like</em> (never throw; safe defaults) or
/// <em>interaction</em> (may throw FlaUI exceptions). Service-level catch blocks handle
/// interaction exceptions; see SPEC-S-006 §4.3.
/// The exact FlaUI sequence (I-U-6) is discovered with Inspect.exe during the garage PC
/// development session.
/// </remarks>
internal interface IChangeMatchAutomation
{
    /// <summary>
    /// Executes the I-U-6 FlaUI sequence to close the current loaded match and navigate
    /// back to the match selection dialog.
    /// </summary>
    /// <remarks><b>Interaction method — may throw.</b></remarks>
    void ExecuteChangeMatchSequence();

    /// <summary>
    /// Returns <see langword="true"/> when an unexpected dialog is present. Never throws.
    /// </summary>
    bool IsUnexpectedDialogPresent();

    /// <summary>
    /// Attempts to dismiss the unexpected dialog. Best-effort: does not throw if close fails
    /// or if no dialog is found.
    /// </summary>
    void TryCloseUnexpectedDialog();
}
