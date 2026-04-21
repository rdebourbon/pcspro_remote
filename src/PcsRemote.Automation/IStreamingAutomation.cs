namespace PcsRemote.Automation;

/// <summary>
/// Abstracts the FlaUI interaction sequences required for starting and stopping
/// PCS Pro's live streaming feature.
/// </summary>
/// <remarks>
/// All methods are synchronous (FlaUI is a synchronous API). Action methods
/// throw <see cref="InvalidOperationException"/> when required UI elements are
/// not found or interactions fail. Probe-like methods return safe defaults and
/// never throw.
/// Ported from diagnostic tool Step 11 (<c>Step11_StartStopLiveStream</c>).
/// </remarks>
internal interface IStreamingAutomation
{
    /// <summary>
    /// Finds and clicks the "Start Live Stream" button in the Video Display ToolWindow.
    /// The button has no Name — identification uses <c>FindButtonByChildText</c> to match
    /// a child TextBlock containing "Start Live Stream".
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the Video Display ToolWindow, LiveStreamingControls container, or the
    /// start button cannot be located, or if the button invoke fails.
    /// </exception>
    void ClickStartLiveStream();

    /// <summary>
    /// Handles the mandatory Video Consent dialog (clicks "Video Consented") and the
    /// optional "Add Live Stream to Match Centre?" dialog (clicks "No").
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the mandatory Video Consent dialog does not appear within the timeout
    /// or the consent button cannot be clicked.
    /// </exception>
    void HandleConsentDialogs();

    /// <summary>
    /// Finds and clicks the "Stop Live Stream" button. This is the same physical button
    /// as start — its child TextBlock label toggles to "Stop Live" when streaming is active.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the Video Display ToolWindow or the stop button cannot be located,
    /// or if the button invoke fails.
    /// </exception>
    void ClickStopLiveStream();

    /// <summary>
    /// Returns <see langword="true"/> when a button with child text containing "Stop Live"
    /// is present in the Video Display ToolWindow, indicating active streaming.
    /// Returns <see langword="false"/> if the Video Display ToolWindow cannot be found
    /// or if any exception is caught. Never throws.
    /// </summary>
    bool IsStreamingActive();

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
