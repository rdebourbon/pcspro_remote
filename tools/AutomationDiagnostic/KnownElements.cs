namespace AutomationDiagnostic;

/// <summary>
/// Known AutomationId, ClassName, Name, and HelpText values for PCS Pro UI elements.
/// These are discovered iteratively on the garage PC using this diagnostic tool.
/// When a value is confirmed working, update the constant here AND in the corresponding
/// FlaUI automation class in src/PcsRemote.Automation/.
/// </summary>
internal static class KnownElements
{
    // ── Main Window ───────────────────────────────────────────────────────
    public const string MainWindowTitlePrefix = "Play-Cricket Scorer Pro";
    public const string MainWindowAutomationId = "LayoutRoot";

    // ── Step 1: Login Screen ──────────────────────────────────────────────
    public const string LoginDialogAutomationId = "window";
    public static string LoginUsernameFieldAutomationId = "txtUsername";
    public static string LoginPasswordFieldAutomationId = "txtPassword";
    public static string LoginSubmitButtonAutomationId = "btnLogin";
    public static string LoginCancelButtonAutomationId = "btnCancel";
    public static string LoginSwitchUserText = "Switch User";

    // ── Navigation: File → Open Match ────────────────────────────────────
    public static string OpenMatchMenuItemName = "Open Match...";

    // ── Step 2: Match Selection (Open Match dialog) ───────────────────────
    // The Open Match dialog has no Search button — Enter key triggers search
    // after filling filter controls.
    public static string MatchSelectionDialogAutomationId = "window";
    public static string MatchSelectionDialogName = "Open Match";
    public static string MatchDataGridAutomationId = "gridMatches";
    public static string OpenReadOnlyButtonAutomationId = "btnAdditionalCancel";
    public static string OpenForScoringButtonAutomationId = "multiAction";
    public static string MatchSelectionCancelButtonAutomationId = "btnCancel";
    public static string TeamFilterAutomationId = "cboTeamMultiple";
    public static string CompetitionFilterAutomationId = "cboCompetitionMultiple";
    public static string StateFilterAutomationId = "cboState";
    public static string MatchSearchButtonAutomationId = "NONE_USE_ENTER_KEY";

    // ── Match Selection Grid Columns (0-based) ─────────────────────────
    // Date(0), Team 1(1), Team 2(2), Competition(3), Match Type(4),
    // Venue(5), State(6), Video?(7), Source(8), Live Scorer(9), Dwnld Reqd?(10)
    public const int GridColumnTeam1 = 1;
    public const int GridColumnTeam2 = 2;

    // ── Match Loaded State ───────────────────────────────────────────────
    public static string ScoreSummaryPaneAutomationId = "twdScoreSummary";
    public static string PlayControlPaneAutomationId = "twdPlayControl";
    public static string ScoringPaneAutomationId = "twdScoring";
    public static string StatusBarClassName = "ScorePanelStatusBar";

    // ── Step 3: Team Names (via Scoring → Match Details/Teams...) ────────
    public static string ScoringMenuAutomationId = "mnuScoring";
    public static string MatchDetailsMenuItemName = "Match Details/Teams...";
    // The following are discovered from the Match Details dialog — initially TODO
    public static string MatchDetailsDialogName = "TODO";
    public static string HomeTeamElementAutomationId = "TODO";
    public static string AwayTeamElementAutomationId = "TODO";

    // ── Step 4: Scoreboard ────────────────────────────────────────────────
    public static string StreamingOverlayTabAutomationId =
        "dockSite.PART_DockHost.RightAutoHideTabGroup[0].AutoHideTabItem[0]";
    public static string SettingsCogHelpText = "TODO";
    public static string ScoreboardWindowClassName = "TODO";
    public static string ScoreboardWindowName = "TODO";

    // ── Step 5: Change Match (File → Open Match... re-use) ───────────────
    public static string FileMenuAutomationId = "mnuFile";
    public static string ChangeMatchElementAutomationId = "TODO";
}
