namespace PcsRemote.Automation;

/// <summary>
/// Aggregates all per-operation automation interfaces required by
/// <see cref="PcsProAutomationService"/>. Injected as a single constructor parameter
/// to keep the constructor parameter count within acceptable limits (≤ 7).
/// </summary>
/// <param name="LoginAutomation">FlaUI interactions for the login phase (S-003).</param>
/// <param name="MatchSelectionAutomation">FlaUI interactions for match search and load (S-004).</param>
/// <param name="TeamNamesAutomation">FlaUI interactions for team name extraction (S-005).</param>
/// <param name="ScoreboardAutomation">FlaUI and Win32 interactions for scoreboard refresh and capture (S-006).</param>
/// <param name="ChangeMatchAutomation">FlaUI interactions for changing the loaded match (S-006).</param>
internal sealed record AutomationDependencies(
    ILoginAutomation LoginAutomation,
    IMatchSelectionAutomation MatchSelectionAutomation,
    ITeamNamesAutomation TeamNamesAutomation,
    IScoreboardAutomation ScoreboardAutomation,
    IChangeMatchAutomation ChangeMatchAutomation);
