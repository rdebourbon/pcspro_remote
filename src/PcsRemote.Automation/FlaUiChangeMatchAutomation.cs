namespace PcsRemote.Automation;

/// <summary>
/// FlaUI-based implementation of <see cref="IChangeMatchAutomation"/>.
/// </summary>
/// <remarks>
/// <para>
/// The exact FlaUI sequence to close the current loaded match (I-U-6) must be discovered
/// on the garage PC via Inspect.exe during S-006 delivery. The interaction method throws
/// <see cref="NotImplementedException"/> until the garage PC session is complete.
/// </para>
/// <para>
/// Once I-U-6 is resolved, replace the <c>TODO_REPLACE_ON_GARAGE_PC</c> placeholder and
/// implement <see cref="ExecuteChangeMatchSequence"/> using <c>FlaUI.Core</c> / <c>FlaUI.UIA3</c>.
/// </para>
/// </remarks>
internal sealed class FlaUiChangeMatchAutomation : IChangeMatchAutomation
{
    // I-U-6: UI element identifier for the change-match sequence, discovered on the garage PC.
    private const string ChangeMatchElementAutomationId = "TODO_REPLACE_ON_GARAGE_PC";

    /// <inheritdoc/>
    public void ExecuteChangeMatchSequence() =>
        throw new NotImplementedException(
            "FlaUiChangeMatchAutomation.ExecuteChangeMatchSequence is not yet implemented. " +
            $"Change-match element AutomationId: '{ChangeMatchElementAutomationId}'. " +
            "Resolve I-U-6 via Inspect.exe on the garage PC.");

    /// <inheritdoc/>
    /// <remarks>Returns <see langword="false"/> until the garage PC implementation is available (I-U-6).</remarks>
    public bool IsUnexpectedDialogPresent() => false;

    /// <inheritdoc/>
    /// <remarks>No-op until the garage PC implementation is available (I-U-6).</remarks>
    public void TryCloseUnexpectedDialog() { }
}
