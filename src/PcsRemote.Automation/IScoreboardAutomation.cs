namespace PcsRemote.Automation;

/// <summary>
/// Abstracts FlaUI and Win32 interactions required for the scoreboard refresh and capture phase.
/// Covers both <c>RefreshScoreboardAsync</c> and <c>CaptureScoreboardImageAsync</c>, which share
/// the scoreboard UI domain.
/// </summary>
/// <remarks>
/// Methods are classified as either <em>probe-like</em> (never throw; safe defaults) or
/// <em>interaction</em> (may throw FlaUI/Win32 exceptions). Service-level catch blocks handle
/// interaction exceptions; see SPEC-S-006 §4.
/// AutomationId, HelpText, and ClassName constants (I-U-5) are resolved via Inspect.exe
/// during the garage PC development session.
/// </remarks>
internal interface IScoreboardAutomation
{
    /// <summary>
    /// Finds the settings cog element by HelpText and clicks it to open the popup menu.
    /// </summary>
    /// <remarks><b>Interaction method — may throw.</b></remarks>
    void ClickSettingsCog();

    /// <summary>
    /// Finds the "Refresh All Scoreboards" menu item in the opened popup and clicks it.
    /// Uses <c>GetAllTopLevelWindows()</c> fallback if the popup renders outside the main
    /// window tree (I-U-4).
    /// </summary>
    /// <remarks><b>Interaction method — may throw.</b></remarks>
    void ClickRefreshAllScoreboards();

    /// <summary>
    /// Locates the scoreboard tool window (preferring the <c>ReplayScreenPreview</c> content element),
    /// captures it via <see cref="FlaUI.Core.Capturing.Capture.Rectangle(System.Drawing.Rectangle)"/>
    /// (DPI-aware), encodes as JPEG at <see cref="ScoreboardOptions.JpegQuality"/>, and returns
    /// the encoded bytes.
    /// </summary>
    /// <remarks><b>Interaction method — may throw.</b></remarks>
    byte[] CaptureScoreboardImage();

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
