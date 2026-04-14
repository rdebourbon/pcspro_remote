using PcsRemote.Automation;
using PcsRemote.Core;

namespace PcsRemote.Automation.Tests;

/// <summary>
/// Test double for <see cref="IMatchSelectionAutomation"/>.
/// Provides configurable behaviour for all match selection phase interactions and
/// records calls for assertion in unit tests.
/// </summary>
internal sealed class FakeMatchSelectionAutomation : IMatchSelectionAutomation
{
    /// <summary>
    /// Controls whether <see cref="IsSpinnerVisible"/> returns <see langword="true"/>.
    /// Defaults to <see langword="false"/> (spinner not visible — spinner already gone).
    /// </summary>
    public bool SpinnerVisible { get; set; }

    /// <summary>
    /// Controls whether <see cref="IsUnexpectedDialogPresent"/> returns <see langword="true"/>.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool UnexpectedDialogPresent { get; set; }

    /// <summary>
    /// Controls whether <see cref="IsMatchLoaded"/> returns <see langword="true"/>.
    /// Defaults to <see langword="true"/> (match immediately loaded — happy path).
    /// </summary>
    public bool MatchLoaded { get; set; } = true;

    /// <summary>
    /// The row texts returned by <see cref="ReadDataGridRowTexts"/>.
    /// Defaults to an empty list; set in tests that need parseable rows.
    /// </summary>
    public IReadOnlyList<string> RowTexts { get; set; } = [];

    /// <summary>
    /// When <see langword="true"/>, <see cref="OpenMatchDialogAndSearch"/>,
    /// <see cref="SelectAndOpenMatch"/>, and <see cref="ReadDataGridRowTexts"/> all throw
    /// <see cref="InvalidOperationException"/> to simulate FlaUI element-not-found failures.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool ThrowOnInteraction { get; set; }

    // ---- Captured call state -----------------------------------------------

    /// <summary><see langword="true"/> after <see cref="OpenMatchDialogAndSearch"/> has been called.</summary>
    public bool SearchTriggered { get; private set; }

    /// <summary>The <see cref="MatchInfo"/> most recently passed to <see cref="SelectAndOpenMatch"/>.</summary>
    public MatchInfo? OpenAttemptedFor { get; private set; }

    /// <summary><see langword="true"/> after <see cref="TryCloseUnexpectedDialog"/> has been called.</summary>
    public bool CloseDialogAttempted { get; private set; }

    // ---- Interface implementation -------------------------------------------

    /// <inheritdoc/>
    public void OpenMatchDialogAndSearch()
    {
        if (ThrowOnInteraction)
            throw new InvalidOperationException(
                "FakeMatchSelectionAutomation: element not found (ThrowOnInteraction = true)");
        SearchTriggered = true;
    }

    /// <inheritdoc/>
    public bool IsSpinnerVisible() => SpinnerVisible;

    /// <inheritdoc/>
    public bool IsUnexpectedDialogPresent() => UnexpectedDialogPresent;

    /// <inheritdoc/>
    public void TryCloseUnexpectedDialog() => CloseDialogAttempted = true;

    /// <inheritdoc/>
    public IReadOnlyList<string> ReadDataGridRowTexts()
    {
        if (ThrowOnInteraction)
            throw new InvalidOperationException(
                "FakeMatchSelectionAutomation: element not found (ThrowOnInteraction = true)");
        return RowTexts;
    }

    /// <inheritdoc/>
    public void SelectAndOpenMatch(MatchInfo match)
    {
        if (ThrowOnInteraction)
            throw new InvalidOperationException(
                "FakeMatchSelectionAutomation: element not found (ThrowOnInteraction = true)");
        OpenAttemptedFor = match;
    }

    /// <inheritdoc/>
    public bool IsMatchLoaded() => MatchLoaded;
}
