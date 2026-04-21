namespace PcsRemote.Automation.Tests;

/// <summary>
/// Test double for <see cref="IStreamingAutomation"/>.
/// Provides configurable behaviour for all streaming automation interactions and
/// records calls for assertion in unit tests.
/// </summary>
internal sealed class FakeStreamingAutomation : IStreamingAutomation
{
    // ---- Configuration ---------------------------------------------------

    /// <summary>
    /// When <see langword="true"/>, <see cref="ClickStartLiveStream"/> throws
    /// <see cref="InvalidOperationException"/> to simulate a FlaUI failure.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool ThrowOnClickStartLiveStream { get; set; }

    /// <summary>
    /// When <see langword="true"/>, <see cref="HandleConsentDialogs"/> throws
    /// <see cref="InvalidOperationException"/> to simulate a consent dialog failure.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool ThrowOnHandleConsentDialogs { get; set; }

    /// <summary>
    /// When <see langword="true"/>, <see cref="ClickStopLiveStream"/> throws
    /// <see cref="InvalidOperationException"/> to simulate a FlaUI failure.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool ThrowOnClickStopLiveStream { get; set; }

    /// <summary>
    /// Controls the return value of <see cref="IsStreamingActive"/>.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool StreamingActive { get; set; }

    /// <summary>
    /// Controls whether <see cref="IsUnexpectedDialogPresent"/> returns <see langword="true"/>.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool UnexpectedDialogPresent { get; set; }

    // ---- Captured call state ---------------------------------------------

    /// <summary><see langword="true"/> after <see cref="ClickStartLiveStream"/> has been called.</summary>
    public bool ClickStartLiveStreamCalled { get; private set; }

    /// <summary><see langword="true"/> after <see cref="HandleConsentDialogs"/> has been called.</summary>
    public bool HandleConsentDialogsCalled { get; private set; }

    /// <summary><see langword="true"/> after <see cref="ClickStopLiveStream"/> has been called.</summary>
    public bool ClickStopLiveStreamCalled { get; private set; }

    /// <summary><see langword="true"/> after <see cref="TryCloseUnexpectedDialog"/> has been called.</summary>
    public bool CloseUnexpectedDialogAttempted { get; private set; }

    // ---- Interface implementation ----------------------------------------

    /// <inheritdoc/>
    public void ClickStartLiveStream()
    {
        ClickStartLiveStreamCalled = true;
        if (ThrowOnClickStartLiveStream)
            throw new InvalidOperationException(
                "FakeStreamingAutomation: ClickStartLiveStream failed (ThrowOnClickStartLiveStream = true)");
    }

    /// <inheritdoc/>
    public void HandleConsentDialogs()
    {
        HandleConsentDialogsCalled = true;
        if (ThrowOnHandleConsentDialogs)
            throw new InvalidOperationException(
                "FakeStreamingAutomation: HandleConsentDialogs failed (ThrowOnHandleConsentDialogs = true)");
    }

    /// <inheritdoc/>
    public void ClickStopLiveStream()
    {
        ClickStopLiveStreamCalled = true;
        if (ThrowOnClickStopLiveStream)
            throw new InvalidOperationException(
                "FakeStreamingAutomation: ClickStopLiveStream failed (ThrowOnClickStopLiveStream = true)");
    }

    /// <inheritdoc/>
    public bool IsStreamingActive() => StreamingActive;

    /// <inheritdoc/>
    public bool IsUnexpectedDialogPresent() => UnexpectedDialogPresent;

    /// <inheritdoc/>
    public void TryCloseUnexpectedDialog() => CloseUnexpectedDialogAttempted = true;
}
