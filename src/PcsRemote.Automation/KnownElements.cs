namespace PcsRemote.Automation;

/// <summary>
/// Canonical source of all known AutomationId, ClassName, Name, and HelpText values
/// for PCS Pro UI elements. These identifiers were discovered iteratively on the garage PC
/// using the diagnostic tool (<c>tools/AutomationDiagnostic</c>) and are the authoritative
/// reference for all FlaUI automation classes in this project.
/// </summary>
/// <remarks>
/// Subsequent IS-010 steps may add identifiers discovered inline in
/// <c>DiagnosticRunner.cs</c> that were not elevated to the diagnostic
/// <c>KnownElements.cs</c> (e.g., <c>ReplayScreenPreview</c>,
/// <c>LiveStreamingControls</c>).
/// </remarks>
internal static class KnownElements
{
    // ── Main Window ───────────────────────────────────────────────────────
    public const string MainWindowTitlePrefix = "Play-Cricket Scorer Pro";
    public const string MainWindowAutomationId = "LayoutRoot";

    // ── Step 1: Login Screen ──────────────────────────────────────────────
    public const string LoginDialogAutomationId = "window";
    public const string LoginUsernameFieldAutomationId = "txtUsername";
    public const string LoginPasswordFieldAutomationId = "txtPassword";
    public const string LoginSubmitButtonAutomationId = "btnLogin";
    public const string LoginCancelButtonAutomationId = "btnCancel";
    public const string LoginSwitchUserText = "Switch User";

    // ── Navigation: File → Open Match ────────────────────────────────────
    public const string OpenMatchMenuItemName = "Open Match...";
    public const string OpenMatchMenuItemAutomationId = "btnOpenMatch";

    // ── Step 2: Match Selection (Open Match dialog) ───────────────────────
    // The Open Match dialog has no Search button — Enter key triggers search
    // after filling filter controls.
    public const string MatchSelectionDialogAutomationId = "window";
    public const string MatchSelectionDialogName = "Open Match";
    public const string MatchDataGridAutomationId = "gridMatches";
    public const string OpenReadOnlyButtonAutomationId = "btnAdditionalCancel";
    public const string OpenForScoringButtonAutomationId = "multiAction";
    public const string MatchSelectionCancelButtonAutomationId = "btnCancel";
    public const string TeamFilterAutomationId = "cboTeamMultiple";
    public const string CompetitionFilterAutomationId = "cboCompetitionMultiple";
    public const string StateFilterAutomationId = "cboState";
    public const string MatchSearchButtonAutomationId = "NONE_USE_ENTER_KEY";

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
    public const string ScoreSummaryPaneAutomationId = "twdScoreSummary";
    public const string PlayControlPaneAutomationId = "twdPlayControl";
    public const string ScoringPaneAutomationId = "twdScoring";
    public const string StatusBarClassName = "ScorePanelStatusBar";

    // ── Step 3: Team Names (via Scoring → Match Details/Teams...) ────────
    public const string ScoringMenuAutomationId = "mnuScoring";
    public const string MatchDetailsMenuItemName = "Match Details/Teams...";
    // Match Details dialog: AutomationId="window", Name="Match Details/Teams"
    public const string MatchDetailsDialogName = "Match Details/Teams";
    public const string MatchDetailsOkButtonAutomationId = "btnAction";
    // Each team is a MatchTeamView (AutomationId="matchTeamView") inside an
    // ItemsControl. First = home, second = away. Both have same AutomationId.
    public const string MatchTeamViewAutomationId = "matchTeamView";
    public const string MatchTeamViewClassName = "MatchTeamView";
    public const string ClubComboBoxAutomationId = "cboClub";
    public const string TeamComboBoxAutomationId = "cbxTeam";
    public const string PlayersGridAutomationId = "dgrPlayers";

    // ── Step 4: Main Scoreboard (ToolWindow within DockSite) ────────────
    // The "Main Scoreboard" is a ToolWindow pane inside a ToolWindowContainer.
    // Its parent container has a TitleBarPanel (PART_TitleBar) with an options
    // button (image child) that opens a popup menu containing "Refresh all
    // Scoreboards". This is NOT the streaming overlay — it is the in-app
    // scoreboard panel for screen-grabbing.
    public const string MainScoreboardToolWindowName = "Main Scoreboard";
    public const string RefreshAllScoreboardsMenuItemName = "Refresh all Scoreboards";

    // ── Streaming Overlay (auto-hide tab on the right) ───────────────────
    public const string StreamingOverlayTabAutomationId =
        "dockSite.PART_DockHost.RightAutoHideTabGroup[0].AutoHideTabItem[0]";

    // ── Step 5: Change Match (File → Open Match... re-use) ───────────────
    // Change match uses File → Open Match... (same as Step 2). No separate element needed.
    public const string FileMenuAutomationId = "mnuFile";
    public const string ChangeMatchElementAutomationId = "btnOpenMatch";
}
