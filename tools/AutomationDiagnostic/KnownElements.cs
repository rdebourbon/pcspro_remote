namespace AutomationDiagnostic;

/// <summary>
/// Known AutomationId, ClassName, Name, and HelpText values for PCS Pro UI elements.
/// These are discovered iteratively on the garage PC using this diagnostic tool.
/// When a value is confirmed working, update the constant here AND in the corresponding
/// FlaUI automation class in src/PcsRemote.Automation/.
/// </summary>
internal static class KnownElements
{
    // ── Step 1: Login Screen ──────────────────────────────────────────────
    public static string LoginPasswordFieldAutomationId = "TODO";
    public static string LoginSubmitButtonAutomationId = "TODO";

    // ── Step 2: Match Selection ───────────────────────────────────────────
    public static string MatchSearchButtonAutomationId = "TODO";
    public static string MatchDataGridAutomationId = "TODO";

    // ── Step 3: Team Names ────────────────────────────────────────────────
    public static string ScoringMenuAutomationId = "TODO";
    public static string HomeTeamComboBoxAutomationId = "TODO";
    public static string AwayTeamComboBoxAutomationId = "TODO";

    // ── Step 4: Scoreboard ────────────────────────────────────────────────
    public static string SettingsCogHelpText = "TODO";
    public static string ScoreboardWindowClassName = "TODO";
    public static string ScoreboardWindowName = "TODO";

    // ── Step 5: Change Match ──────────────────────────────────────────────
    public static string ChangeMatchElementAutomationId = "TODO";
}
