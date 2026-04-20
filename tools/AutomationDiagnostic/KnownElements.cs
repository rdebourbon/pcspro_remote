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
    public static string OpenMatchMenuItemAutomationId = "btnOpenMatch";

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

    // ── Match Selection Spinner ──────────────────────────────────────────
    // The LoaderSpinner is always in the dialog tree. When a search is
    // running it is on-screen (IsOffscreen=false). When idle it is
    // off-screen (IsOffscreen=true). Detected via ClassName — no AutomationId.
    public const string LoaderSpinnerClassName = "LoaderSpinner";

    // ── Match Selection Grid Columns (0-based) ─────────────────────────
    // Date(0), Team 1(1), Team 2(2), Competition(3), Match Type(4),
    // Venue(5), State(6), Video?(7), Source(8), Live Scorer(9), Dwnld Reqd?(10)
    public const int GridColumnTeam1 = 1;
    public const int GridColumnTeam2 = 2;

    // ── Match Loaded State ───────────────────────────────────────────────
    // twdScoreSummary is a match-loaded detection signal only — it is NOT
    // the streaming scoreboard overlay that needs to be screengrabbed.
    public static string ScoreSummaryPaneAutomationId = "twdScoreSummary";
    public static string PlayControlPaneAutomationId = "twdPlayControl";
    public static string ScoringPaneAutomationId = "twdScoring";
    public static string StatusBarClassName = "ScorePanelStatusBar";

    // ── Step 3: Team Names (via Scoring → Match Details/Teams...) ────────
    public static string ScoringMenuAutomationId = "mnuScoring";
    public static string MatchDetailsMenuItemName = "Match Details/Teams...";
    // Match Details dialog: AutomationId="window", Name="Match Details/Teams"
    public static string MatchDetailsDialogName = "Match Details/Teams";
    public static string MatchDetailsOkButtonAutomationId = "btnAction";
    // Each team is a MatchTeamView (AutomationId="matchTeamView") inside an
    // ItemsControl. First = home, second = away. Both have same AutomationId.
    public static string MatchTeamViewAutomationId = "matchTeamView";
    public static string MatchTeamViewClassName = "MatchTeamView";
    public static string ClubComboBoxAutomationId = "cboClub";
    public static string TeamComboBoxAutomationId = "cbxTeam";
    public static string PlayersGridAutomationId = "dgrPlayers";

    // ── Step 4: Main Scoreboard (ToolWindow within DockSite) ────────────
    // The "Main Scoreboard" is a ToolWindow pane inside a ToolWindowContainer.
    // Its parent container has a TitleBarPanel (PART_TitleBar) with an options
    // button (image child) that opens a popup menu containing "Refresh all
    // Scoreboards". This is NOT the streaming overlay — it is the in-app
    // scoreboard panel for screen-grabbing.
    public static string MainScoreboardToolWindowName = "Main Scoreboard";
    public static string RefreshAllScoreboardsMenuItemName = "Refresh all Scoreboards";

    // ── Streaming Overlay (auto-hide tab on the right) ───────────────────
    public static string StreamingOverlayTabAutomationId =
        "dockSite.PART_DockHost.RightAutoHideTabGroup[0].AutoHideTabItem[0]";

    // ── Legacy/unused scoreboard identifiers (kept for reference) ────────
    public static string SettingsCogHelpText = "TODO";
    public static string ScoreboardWindowClassName = "TODO";
    public static string ScoreboardWindowName = "TODO";

    // ── Step 5: Change Match (File → Open Match... re-use) ───────────────
    public static string FileMenuAutomationId = "mnuFile";
    public static string ChangeMatchElementAutomationId = "TODO";
}
