namespace PcsRemote.Automation;

/// <summary>
/// Abstracts FlaUI element interactions required for the PCS Pro login phase.
/// Allows <see cref="PcsProAutomationService"/> to be tested without a live FlaUI session.
/// All methods are designed to be called on the polling thread; implementors must not block
/// indefinitely. Detection methods return <see langword="false"/> when not yet visible rather
/// than throwing.
/// </summary>
internal interface ILoginAutomation
{
    /// <summary>
    /// Returns <see langword="true"/> when the PCS Pro login dialog is currently visible.
    /// Returns <see langword="false"/> when the dialog has not yet appeared or is not found.
    /// Does not throw.
    /// </summary>
    bool IsLoginDialogVisible();

    /// <summary>
    /// Enters the given password into the login dialog password field.
    /// </summary>
    /// <exception cref="Exception">Thrown if the password field element cannot be located.</exception>
    void EnterPassword(string password);

    /// <summary>
    /// Clicks the login submit button.
    /// </summary>
    /// <exception cref="Exception">Thrown if the submit button element cannot be located.</exception>
    void ClickSubmit();

    /// <summary>
    /// Returns <see langword="true"/> when the match selection dialog has appeared, indicating
    /// that login was accepted. Returns <see langword="false"/> when not yet visible.
    /// Does not throw.
    /// </summary>
    bool IsMatchSelectionVisible();

    /// <summary>
    /// Returns <see langword="true"/> when a dialog other than the expected login dialog or
    /// match selection dialog is present. Does not throw.
    /// </summary>
    bool IsUnexpectedDialogPresent();

    /// <summary>
    /// Returns the name of the first unexpected dialog, or <see langword="null"/> if none found.
    /// Used for diagnostic logging. Does not throw.
    /// </summary>
    string? GetUnexpectedDialogName();

    /// <summary>
    /// Attempts to close the unexpected dialog. Best-effort: does not throw if close fails
    /// or if no dialog is found.
    /// </summary>
    void TryCloseUnexpectedDialog();

    /// <summary>
    /// Reads the current text content of the username field in the login dialog.
    /// Returns <see langword="null"/> when the field cannot be located or the main window
    /// is not found. Does not throw.
    /// </summary>
    string? ReadUsername();

    /// <summary>
    /// Enters the given username into the login dialog username field.
    /// </summary>
    /// <exception cref="Exception">Thrown if the username field element cannot be located.</exception>
    void EnterUsername(string username);

    /// <summary>
    /// Clicks the "Switch User" hyperlink in the login dialog.
    /// </summary>
    /// <exception cref="Exception">Thrown if the switch-user element cannot be located.</exception>
    void ClickSwitchUser();
}
