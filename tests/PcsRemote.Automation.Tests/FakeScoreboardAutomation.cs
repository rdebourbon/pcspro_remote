namespace PcsRemote.Automation.Tests;

/// <summary>
/// Test double for <see cref="IScoreboardAutomation"/>.
/// Provides configurable behaviour for all scoreboard automation interactions and
/// records calls for assertion in unit tests.
/// </summary>
internal sealed class FakeScoreboardAutomation : IScoreboardAutomation
{
    // ---- Configuration ---------------------------------------------------

    /// <summary>
    /// When <see langword="true"/>, <see cref="ClickSettingsCog"/> throws
    /// <see cref="InvalidOperationException"/> to simulate a FlaUI element-not-found failure.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool ThrowOnClickSettingsCog { get; set; }

    /// <summary>
    /// When <see langword="true"/>, <see cref="ClickRefreshAllScoreboards"/> throws
    /// <see cref="InvalidOperationException"/> to simulate a FlaUI element-not-found failure.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool ThrowOnClickRefreshAllScoreboards { get; set; }

    /// <summary>
    /// When <see langword="true"/>, <see cref="CaptureScoreboardImage"/> throws
    /// <see cref="InvalidOperationException"/> to simulate a Win32/FlaUI capture failure.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool ThrowOnCaptureScoreboardImage { get; set; }

    /// <summary>
    /// The bytes returned by <see cref="CaptureScoreboardImage"/>.
    /// Defaults to an arbitrary non-empty byte array — no valid JPEG structure required.
    /// </summary>
    public byte[] CapturedImageBytes { get; set; } = new byte[] { 0xFF, 0xD8 };

    /// <summary>
    /// Controls whether <see cref="IsUnexpectedDialogPresent"/> returns <see langword="true"/>.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool UnexpectedDialogPresent { get; set; }

    // ---- Captured call state ---------------------------------------------

    /// <summary><see langword="true"/> after <see cref="ClickSettingsCog"/> has been called.</summary>
    public bool ClickCogAttempted { get; private set; }

    /// <summary><see langword="true"/> after <see cref="ClickRefreshAllScoreboards"/> has been called.</summary>
    public bool ClickRefreshAttempted { get; private set; }

    /// <summary><see langword="true"/> after <see cref="CaptureScoreboardImage"/> has been called.</summary>
    public bool CaptureAttempted { get; private set; }

    /// <summary><see langword="true"/> after <see cref="TryCloseUnexpectedDialog"/> has been called.</summary>
    public bool CloseUnexpectedDialogAttempted { get; private set; }

    // ---- Interface implementation ----------------------------------------

    /// <inheritdoc/>
    public void ClickSettingsCog()
    {
        ClickCogAttempted = true;
        if (ThrowOnClickSettingsCog)
            throw new InvalidOperationException(
                "FakeScoreboardAutomation: element not found (ThrowOnClickSettingsCog = true)");
    }

    /// <inheritdoc/>
    public void ClickRefreshAllScoreboards()
    {
        ClickRefreshAttempted = true;
        if (ThrowOnClickRefreshAllScoreboards)
            throw new InvalidOperationException(
                "FakeScoreboardAutomation: element not found (ThrowOnClickRefreshAllScoreboards = true)");
    }

    /// <inheritdoc/>
    public byte[] CaptureScoreboardImage()
    {
        CaptureAttempted = true;
        if (ThrowOnCaptureScoreboardImage)
            throw new InvalidOperationException(
                "FakeScoreboardAutomation: capture failed (ThrowOnCaptureScoreboardImage = true)");
        return CapturedImageBytes;
    }

    /// <inheritdoc/>
    public bool IsUnexpectedDialogPresent() => UnexpectedDialogPresent;

    /// <inheritdoc/>
    public void TryCloseUnexpectedDialog() => CloseUnexpectedDialogAttempted = true;
}
