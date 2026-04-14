using PcsRemote.Core;

namespace PcsRemote.Automation;

/// <summary>
/// FlaUI-based implementation of <see cref="IMatchSelectionAutomation"/>.
/// </summary>
/// <remarks>
/// <para>
/// The AutomationId constants below (I-U-5) must be discovered on the garage PC via
/// Inspect.exe during S-004 delivery and replaced before this class can perform real
/// interactions. Interaction methods throw <see cref="NotImplementedException"/> until
/// the garage PC session is complete.
/// </para>
/// <para>
/// Once I-U-5 values are known, replace each <c>TODO_REPLACE_ON_GARAGE_PC</c> placeholder
/// and implement each method body using <c>FlaUI.Core</c> / <c>FlaUI.UIA3</c>.
/// </para>
/// </remarks>
internal sealed class FlaUiMatchSelectionAutomation : IMatchSelectionAutomation
{
    // I-U-5: AutomationId values discovered on the garage PC via Inspect.exe.
    // Replace the placeholder strings with the actual values found using Inspect.exe.
    private const string MatchSearchButtonAutomationId = "TODO_REPLACE_ON_GARAGE_PC";
    private const string MatchDataGridAutomationId = "TODO_REPLACE_ON_GARAGE_PC";

    /// <inheritdoc/>
    public void OpenMatchDialogAndSearch() =>
        throw new NotImplementedException(
            "FlaUiMatchSelectionAutomation.OpenMatchDialogAndSearch is not yet implemented. " +
            $"Search button AutomationId: '{MatchSearchButtonAutomationId}'.");

    /// <inheritdoc/>
    /// <remarks>Returns <see langword="false"/> until the garage PC implementation is available (I-U-5).</remarks>
    public bool IsSpinnerVisible() => false;

    /// <inheritdoc/>
    /// <remarks>Returns <see langword="false"/> until the garage PC implementation is available (I-U-5).</remarks>
    public bool IsUnexpectedDialogPresent() => false;

    /// <inheritdoc/>
    /// <remarks>No-op until the garage PC implementation is available (I-U-5).</remarks>
    public void TryCloseUnexpectedDialog() { }

    /// <inheritdoc/>
    public IReadOnlyList<string> ReadDataGridRowTexts() =>
        throw new NotImplementedException(
            "FlaUiMatchSelectionAutomation.ReadDataGridRowTexts is not yet implemented. " +
            $"DataGrid AutomationId: '{MatchDataGridAutomationId}'.");

    /// <inheritdoc/>
    public void SelectAndOpenMatch(MatchInfo match) =>
        throw new NotImplementedException(
            "FlaUiMatchSelectionAutomation.SelectAndOpenMatch is not yet implemented. " +
            $"DataGrid AutomationId: '{MatchDataGridAutomationId}'.");

    /// <inheritdoc/>
    /// <remarks>Returns <see langword="false"/> until the garage PC implementation is available (I-U-5).</remarks>
    public bool IsMatchLoaded() => false;
}
