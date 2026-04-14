namespace PcsRemote.Automation.Tests;

/// <summary>
/// Test double for <see cref="IChangeMatchAutomation"/>.
/// Provides configurable behaviour for all change-match automation interactions and
/// records calls for assertion in unit tests.
/// </summary>
internal sealed class FakeChangeMatchAutomation : IChangeMatchAutomation
{
    // ---- Configuration ---------------------------------------------------

    /// <summary>
    /// When <see langword="true"/>, <see cref="ExecuteChangeMatchSequence"/> throws
    /// <see cref="InvalidOperationException"/> to simulate a FlaUI sequence failure.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool ThrowOnExecuteChangeMatchSequence { get; set; }

    /// <summary>
    /// Controls whether <see cref="IsUnexpectedDialogPresent"/> returns <see langword="true"/>.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool UnexpectedDialogPresent { get; set; }

    // ---- Captured call state ---------------------------------------------

    /// <summary><see langword="true"/> after <see cref="ExecuteChangeMatchSequence"/> has been called.</summary>
    public bool ExecuteChangeMatchAttempted { get; private set; }

    /// <summary><see langword="true"/> after <see cref="TryCloseUnexpectedDialog"/> has been called.</summary>
    public bool CloseUnexpectedDialogAttempted { get; private set; }

    // ---- Interface implementation ----------------------------------------

    /// <inheritdoc/>
    public void ExecuteChangeMatchSequence()
    {
        ExecuteChangeMatchAttempted = true;
        if (ThrowOnExecuteChangeMatchSequence)
            throw new InvalidOperationException(
                "FakeChangeMatchAutomation: sequence failed (ThrowOnExecuteChangeMatchSequence = true)");
    }

    /// <inheritdoc/>
    public bool IsUnexpectedDialogPresent() => UnexpectedDialogPresent;

    /// <inheritdoc/>
    public void TryCloseUnexpectedDialog() => CloseUnexpectedDialogAttempted = true;
}
