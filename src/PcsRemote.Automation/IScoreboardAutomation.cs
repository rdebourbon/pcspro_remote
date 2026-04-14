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
    /// Locates the scoreboard dockable tool window by ClassName and Name, captures it via
    /// <c>PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT)</c>, crops to the element's
    /// <c>BoundingRectangle</c> (screen coordinates translated to bitmap-relative origin at
    /// window top-left), encodes as JPEG at <c>ScoreboardOptions.JpegQuality</c>, and returns
    /// the encoded bytes. Crop and HWND strategy are verified during the garage PC session.
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
