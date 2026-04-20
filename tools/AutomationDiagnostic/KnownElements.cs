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

    // ── Step 2: Match Selection ───────────────────────────────────────────
    public static string MatchSearchButtonAutomationId = "TODO";
    public static string MatchDataGridAutomationId = "TODO";

    // ── Step 3: Team Names ────────────────────────────────────────────────
    public static string ScoringMenuAutomationId = "mnuScoring";
    public static string HomeTeamComboBoxAutomationId = "TODO";
    public static string AwayTeamComboBoxAutomationId = "TODO";

    // ── Step 4: Scoreboard ────────────────────────────────────────────────
    public static string SettingsCogHelpText = "TODO";
    public static string ScoreboardWindowClassName = "TODO";
    public static string ScoreboardWindowName = "TODO";

    // ── Step 5: Change Match ──────────────────────────────────────────────
    public static string FileMenuAutomationId = "mnuFile";
    public static string ChangeMatchElementAutomationId = "TODO";
}
