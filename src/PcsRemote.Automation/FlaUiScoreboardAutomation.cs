using Microsoft.Extensions.Options;

namespace PcsRemote.Automation;

/// <summary>
/// FlaUI-based implementation of <see cref="IScoreboardAutomation"/>.
/// </summary>
/// <remarks>
/// <para>
/// The HelpText, ClassName, and Name constants below (I-U-5) must be discovered on the
/// garage PC via Inspect.exe during S-006 delivery and replaced before this class can
/// perform real interactions. Interaction methods throw <see cref="NotImplementedException"/>
/// until the garage PC session is complete.
/// </para>
/// <para>
/// Once I-U-5 values are known, replace each <c>TODO_REPLACE_ON_GARAGE_PC</c> placeholder
/// and implement each method body using <c>FlaUI.Core</c> / <c>FlaUI.UIA3</c> and Win32
/// <c>PrintWindow</c>.
/// </para>
/// </remarks>
internal sealed class FlaUiScoreboardAutomation : IScoreboardAutomation
{
    // I-U-5: UI element identifiers discovered on the garage PC via Inspect.exe.
    private const string SettingsCogHelpText = "TODO_REPLACE_ON_GARAGE_PC";
    private const string ScoreboardWindowClassName = "TODO_REPLACE_ON_GARAGE_PC";
    private const string ScoreboardWindowName = "TODO_REPLACE_ON_GARAGE_PC";

    private readonly ScoreboardOptions _scoreboardOptions;

    public FlaUiScoreboardAutomation(IOptions<ScoreboardOptions> scoreboardOptions)
    {
        _scoreboardOptions = scoreboardOptions.Value;
    }

    /// <inheritdoc/>
    public void ClickSettingsCog() =>
        throw new NotImplementedException(
            "FlaUiScoreboardAutomation.ClickSettingsCog is not yet implemented. " +
            $"Settings cog HelpText: '{SettingsCogHelpText}'.");

    /// <inheritdoc/>
    public void ClickRefreshAllScoreboards() =>
        throw new NotImplementedException(
            "FlaUiScoreboardAutomation.ClickRefreshAllScoreboards is not yet implemented. " +
            "If the popup renders outside the main window tree (I-U-4), use GetAllTopLevelWindows() " +
            "to locate the 'Refresh All Scoreboards' menu item.");

    /// <inheritdoc/>
    public byte[] CaptureScoreboardImage() =>
        throw new NotImplementedException(
            "FlaUiScoreboardAutomation.CaptureScoreboardImage is not yet implemented. " +
            $"Scoreboard window ClassName: '{ScoreboardWindowClassName}', Name: '{ScoreboardWindowName}'. " +
            $"JPEG quality from config: {_scoreboardOptions.JpegQuality}. " +
            "Use PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT) and crop to BoundingRectangle.");

    /// <inheritdoc/>
    /// <remarks>Returns <see langword="false"/> until the garage PC implementation is available (I-U-5).</remarks>
    public bool IsUnexpectedDialogPresent() => false;

    /// <inheritdoc/>
    /// <remarks>No-op until the garage PC implementation is available (I-U-5).</remarks>
    public void TryCloseUnexpectedDialog() { }
}
