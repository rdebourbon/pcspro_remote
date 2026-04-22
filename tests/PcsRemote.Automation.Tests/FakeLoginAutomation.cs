using PcsRemote.Automation;

namespace PcsRemote.Automation.Tests;

/// <summary>
/// Test double for <see cref="ILoginAutomation"/>.
/// Provides configurable behavior for all login phase interactions and records calls
/// for assertion in unit tests.
/// </summary>
internal sealed class FakeLoginAutomation : ILoginAutomation
{
    /// <summary>
    /// Controls whether <see cref="IsLoginDialogVisible"/> returns <see langword="true"/>.
    /// Defaults to <see langword="true"/> (dialog immediately visible — happy path).
    /// </summary>
    public bool LoginDialogVisible { get; set; } = true;

    /// <summary>
    /// Controls whether <see cref="IsMatchSelectionVisible"/> returns <see langword="true"/>.
    /// Defaults to <see langword="true"/> (match selection immediately visible after submit).
    /// </summary>
    public bool MatchSelectionVisible { get; set; } = true;

    /// <summary>
    /// Controls whether <see cref="IsUnexpectedDialogPresent"/> returns <see langword="true"/>.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool UnexpectedDialogPresent { get; set; }

    /// <summary>
    /// When <see langword="true"/>, <see cref="EnterPassword"/> and <see cref="ClickSubmit"/>
    /// throw <see cref="InvalidOperationException"/> to simulate element-not-found failures.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool ThrowOnInteraction { get; set; }

    /// <summary>The password captured by the most recent <see cref="EnterPassword"/> call.</summary>
    public string? CapturedPassword { get; private set; }

    /// <summary><see langword="true"/> after <see cref="ClickSubmit"/> has been called.</summary>
    public bool SubmitClicked { get; private set; }

    /// <summary><see langword="true"/> after <see cref="TryCloseUnexpectedDialog"/> has been called.</summary>
    public bool CloseDialogAttempted { get; private set; }

    /// <summary>
    /// Controls the value returned by <see cref="ReadUsername"/>.
    /// Defaults to <see langword="null"/> (field not found / not yet visible).
    /// </summary>
    public string? UsernameValue { get; set; }

    /// <summary>The username captured by the most recent <see cref="EnterUsername"/> call.</summary>
    public string? CapturedUsername { get; private set; }

    /// <summary><see langword="true"/> after <see cref="ClickSwitchUser"/> has been called.</summary>
    public bool SwitchUserClicked { get; private set; }

    /// <inheritdoc/>
    public bool IsLoginDialogVisible() => LoginDialogVisible;

    /// <inheritdoc/>
    public void EnterPassword(string password)
    {
        if (ThrowOnInteraction)
            throw new InvalidOperationException("FakeLoginAutomation: element not found (ThrowOnInteraction = true)");
        CapturedPassword = password;
    }

    /// <inheritdoc/>
    public void ClickSubmit()
    {
        if (ThrowOnInteraction)
            throw new InvalidOperationException("FakeLoginAutomation: element not found (ThrowOnInteraction = true)");
        SubmitClicked = true;
    }

    /// <inheritdoc/>
    public bool IsMatchSelectionVisible() => MatchSelectionVisible;

    /// <inheritdoc/>
    public bool IsUnexpectedDialogPresent() => UnexpectedDialogPresent;

    /// <inheritdoc/>
    public string? GetUnexpectedDialogName() => UnexpectedDialogPresent ? "FakeUnexpectedDialog" : null;

    /// <inheritdoc/>
    public void TryCloseUnexpectedDialog() => CloseDialogAttempted = true;

    /// <inheritdoc/>
    public string? ReadUsername() => UsernameValue;

    /// <inheritdoc/>
    public void EnterUsername(string username)
    {
        if (ThrowOnInteraction)
            throw new InvalidOperationException("FakeLoginAutomation: element not found (ThrowOnInteraction = true)");
        CapturedUsername = username;
        // Simulate the username field being updated after entry
        UsernameValue = username;
    }

    /// <inheritdoc/>
    public void ClickSwitchUser()
    {
        if (ThrowOnInteraction)
            throw new InvalidOperationException("FakeLoginAutomation: element not found (ThrowOnInteraction = true)");
        SwitchUserClicked = true;
    }
}
