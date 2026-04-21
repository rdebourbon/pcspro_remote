using PcsRemote.Core;

namespace PcsRemote.Automation.Tests;

/// <summary>
/// Test double for <see cref="ITeamNamesAutomation"/>.
/// Provides configurable behaviour for all team names phase interactions and
/// records calls for assertion in unit tests.
/// </summary>
internal sealed class FakeTeamNamesAutomation : ITeamNamesAutomation
{
    /// <summary>
    /// The club name returned for the home team.
    /// Defaults to <c>"Home CC"</c>.
    /// </summary>
    public string HomeClubName { get; set; } = "Home CC";

    /// <summary>
    /// The team name returned for the home team.
    /// Defaults to <c>"Home XI"</c>.
    /// </summary>
    public string HomeTeamName { get; set; } = "Home XI";

    /// <summary>
    /// The club name returned for the away team.
    /// Defaults to <c>"Away CC"</c>.
    /// </summary>
    public string AwayClubName { get; set; } = "Away CC";

    /// <summary>
    /// The team name returned for the away team.
    /// Defaults to <c>"Away XI"</c>.
    /// </summary>
    public string AwayTeamName { get; set; } = "Away XI";

    /// <summary>
    /// Controls whether <see cref="IsUnexpectedDialogPresent"/> returns <see langword="true"/>.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool UnexpectedDialogPresent { get; set; }

    /// <summary>
    /// When <see langword="true"/>, <see cref="OpenTeamsDialog"/> throws
    /// <see cref="InvalidOperationException"/> to simulate a FlaUI element-not-found failure.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool ThrowOnOpenTeamsDialog { get; set; }

    /// <summary>
    /// When <see langword="true"/>, <see cref="ReadHomeTeamName"/> throws
    /// <see cref="InvalidOperationException"/> to simulate a FlaUI read failure.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool ThrowOnReadHomeTeamName { get; set; }

    /// <summary>
    /// When <see langword="true"/>, <see cref="ReadAwayTeamName"/> throws
    /// <see cref="InvalidOperationException"/> to simulate a FlaUI read failure.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool ThrowOnReadAwayTeamName { get; set; }

    // ---- Captured call state -----------------------------------------------

    /// <summary><see langword="true"/> after <see cref="OpenTeamsDialog"/> has been called.</summary>
    public bool OpenDialogAttempted { get; private set; }

    /// <summary><see langword="true"/> after <see cref="TryCloseTeamsDialog"/> has been called.</summary>
    public bool CloseTeamsDialogAttempted { get; private set; }

    /// <summary><see langword="true"/> after <see cref="TryCloseUnexpectedDialog"/> has been called.</summary>
    public bool CloseUnexpectedDialogAttempted { get; private set; }

    // ---- Interface implementation -------------------------------------------

    /// <inheritdoc/>
    public void OpenTeamsDialog()
    {
        OpenDialogAttempted = true;
        if (ThrowOnOpenTeamsDialog)
            throw new InvalidOperationException(
                "FakeTeamNamesAutomation: element not found (ThrowOnOpenTeamsDialog = true)");
    }

    /// <inheritdoc/>
    public TeamNameInfo ReadHomeTeamName()
    {
        if (ThrowOnReadHomeTeamName)
            throw new InvalidOperationException(
                "FakeTeamNamesAutomation: element not found (ThrowOnReadHomeTeamName = true)");
        return new TeamNameInfo(HomeClubName, HomeTeamName);
    }

    /// <inheritdoc/>
    public TeamNameInfo ReadAwayTeamName()
    {
        if (ThrowOnReadAwayTeamName)
            throw new InvalidOperationException(
                "FakeTeamNamesAutomation: element not found (ThrowOnReadAwayTeamName = true)");
        return new TeamNameInfo(AwayClubName, AwayTeamName);
    }

    /// <inheritdoc/>
    public void TryCloseTeamsDialog() => CloseTeamsDialogAttempted = true;

    /// <inheritdoc/>
    public bool IsUnexpectedDialogPresent() => UnexpectedDialogPresent;

    /// <inheritdoc/>
    public void TryCloseUnexpectedDialog() => CloseUnexpectedDialogAttempted = true;
}
