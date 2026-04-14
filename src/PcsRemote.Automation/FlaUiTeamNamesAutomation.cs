namespace PcsRemote.Automation;

/// <summary>
/// FlaUI-based implementation of <see cref="ITeamNamesAutomation"/>.
/// </summary>
/// <remarks>
/// <para>
/// The AutomationId constants below (I-U-2) and the ComboBox read strategy (I-U-3) must
/// be discovered on the garage PC via Inspect.exe during S-005 delivery and replaced before
/// this class can perform real interactions. Interaction methods throw
/// <see cref="NotImplementedException"/> until the garage PC session is complete.
/// </para>
/// <para>
/// Once I-U-2 and I-U-3 values are known, replace each <c>TODO_REPLACE_ON_GARAGE_PC</c>
/// placeholder and implement each method body using <c>FlaUI.Core</c> / <c>FlaUI.UIA3</c>.
/// </para>
/// </remarks>
internal sealed class FlaUiTeamNamesAutomation : ITeamNamesAutomation
{
    // I-U-2: AutomationId values discovered on the garage PC via Inspect.exe.
    private const string ScoringMenuAutomationId = "TODO_REPLACE_ON_GARAGE_PC";
    private const string HomeTeamComboBoxAutomationId = "TODO_REPLACE_ON_GARAGE_PC";
    private const string AwayTeamComboBoxAutomationId = "TODO_REPLACE_ON_GARAGE_PC";

    /// <inheritdoc/>
    public void OpenTeamsDialog() =>
        throw new NotImplementedException(
            "FlaUiTeamNamesAutomation.OpenTeamsDialog is not yet implemented. " +
            $"Scoring menu AutomationId: '{ScoringMenuAutomationId}'.");

    /// <inheritdoc/>
    public string ReadHomeTeamName() =>
        throw new NotImplementedException(
            "FlaUiTeamNamesAutomation.ReadHomeTeamName is not yet implemented. " +
            $"Home team ComboBox AutomationId: '{HomeTeamComboBoxAutomationId}'. " +
            "I-U-3: use ValuePattern, SelectedItem, or element Name — resolve via Inspect.exe.");

    /// <inheritdoc/>
    public string ReadAwayTeamName() =>
        throw new NotImplementedException(
            "FlaUiTeamNamesAutomation.ReadAwayTeamName is not yet implemented. " +
            $"Away team ComboBox AutomationId: '{AwayTeamComboBoxAutomationId}'. " +
            "I-U-3: use ValuePattern, SelectedItem, or element Name — resolve via Inspect.exe.");

    /// <inheritdoc/>
    /// <remarks>No-op until the garage PC implementation is available (I-U-2).</remarks>
    public void TryCloseTeamsDialog() { }

    /// <inheritdoc/>
    /// <remarks>Returns <see langword="false"/> until the garage PC implementation is available (I-U-2).</remarks>
    public bool IsUnexpectedDialogPresent() => false;

    /// <inheritdoc/>
    /// <remarks>No-op until the garage PC implementation is available (I-U-2).</remarks>
    public void TryCloseUnexpectedDialog() { }
}
