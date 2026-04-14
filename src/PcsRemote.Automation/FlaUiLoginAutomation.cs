namespace PcsRemote.Automation;

/// <summary>
/// FlaUI-based implementation of <see cref="ILoginAutomation"/>.
/// </summary>
/// <remarks>
/// <para>
/// The AutomationId constants below (I-U-1) are discovered on the garage PC via
/// Inspect.exe during S-003 delivery and must be populated before this class can perform
/// real interactions. All methods throw <see cref="NotImplementedException"/> until the
/// garage PC session is complete.
/// </para>
/// <para>
/// Once I-U-1 values are known, replace each <c>TODO_REPLACE_ON_GARAGE_PC</c> placeholder
/// and implement each method body using <c>FlaUI.Core</c> / <c>FlaUI.UIA3</c>.
/// </para>
/// </remarks>
internal sealed class FlaUiLoginAutomation : ILoginAutomation
{
    // I-U-1: AutomationId values discovered on the garage PC via Inspect.exe during S-003 delivery.
    // Replace the placeholder strings with the actual values found using Inspect.exe.
    private const string LoginPasswordFieldAutomationId = "TODO_REPLACE_ON_GARAGE_PC";
    private const string LoginSubmitButtonAutomationId = "TODO_REPLACE_ON_GARAGE_PC";

    // TODO: Implement using FlaUI Application.Attach(Process.GetProcessesByName("cricket").First())
    // and window.FindFirstDescendant(cf => cf.ByAutomationId(...)) patterns.

    /// <inheritdoc/>
    /// <remarks>Returns <see langword="false"/> until the garage PC implementation is available (I-U-1).</remarks>
    public bool IsLoginDialogVisible() => false;

    /// <inheritdoc/>
    public void EnterPassword(string password) =>
        throw new NotImplementedException(
            "FlaUiLoginAutomation.EnterPassword is not yet implemented. " +
            $"Password field AutomationId: '{LoginPasswordFieldAutomationId}'.");

    /// <inheritdoc/>
    public void ClickSubmit() =>
        throw new NotImplementedException(
            "FlaUiLoginAutomation.ClickSubmit is not yet implemented. " +
            $"Submit button AutomationId: '{LoginSubmitButtonAutomationId}'.");

    /// <inheritdoc/>
    /// <remarks>Returns <see langword="false"/> until the garage PC implementation is available (I-U-1).</remarks>
    public bool IsMatchSelectionVisible() => false;

    /// <inheritdoc/>
    /// <remarks>Returns <see langword="false"/> until the garage PC implementation is available (I-U-1).</remarks>
    public bool IsUnexpectedDialogPresent() => false;

    /// <inheritdoc/>
    /// <remarks>No-op until the garage PC implementation is available (I-U-1).</remarks>
    public void TryCloseUnexpectedDialog() { }
}
