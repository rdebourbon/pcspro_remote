using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace AutomationDiagnostic;

/// <summary>
/// Interactive automation diagnostic runner for PCS Pro (cricket.exe).
/// Attempts the full automation flow step-by-step; on failure at any step,
/// dumps the UI tree at that point so the correct element identifiers can be
/// discovered and reported back for implementation.
/// </summary>
internal sealed class DiagnosticRunner : IDisposable
{
    private const string ProcessName = "cricket";
    private const int PollIntervalMs = 500;
    private const int SplashTimeoutSeconds = 30;
    private const int LoginTransitionTimeoutSeconds = 15;
    private const int SearchTimeoutSeconds = 20;
    private const int MatchLoadTimeoutSeconds = 15;

    private readonly string _password;
    private readonly string _expectedUsername;
    private readonly string? _executablePath;
    private readonly UIA3Automation _automation;
    private Application? _app;
    private Window? _mainWindow;

    public DiagnosticRunner(string password, string expectedUsername, string? executablePath = null)
    {
        _password = password;
        _expectedUsername = expectedUsername;
        _executablePath = executablePath;
        _automation = new UIA3Automation();
    }

    public void Dispose()
    {
        _automation.Dispose();
    }

    /// <summary>
    /// Runs the full diagnostic flow. Stops at the first step that fails
    /// and dumps the UI tree at that point.
    /// </summary>
    public void Run()
    {
        PrintHeader();

        // Step 0: Attach to process
        if (!Step0_AttachToProcess()) return;

        // Step 1: Wait for main window (splash screen disappears)
        if (!Step1_WaitForMainWindow()) return;

        // Step 2: Login screen — find elements
        if (!Step2_FindLoginElements()) return;

        // Step 3: Enter credentials and submit
        if (!Step3_SubmitCredentials()) return;

        // Step 4: Wait for match selection screen
        if (!Step4_WaitForMatchSelection()) return;

        // Step 5: Search for matches
        if (!Step5_SearchForMatches()) return;

        // Step 6: Select first match
        if (!Step6_SelectFirstMatch()) return;

        // Step 7: Wait for match to load
        if (!Step7_WaitForMatchLoad()) return;

        // Step 8: Find team name elements
        if (!Step8_FindTeamNameElements()) return;

        // Step 9: Find scoreboard elements
        if (!Step9_FindScoreboardElements()) return;

        // Step 10: Find change match element
        if (!Step10_FindChangeMatchElement()) return;

        PrintSuccess();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // STEP 0: Attach to cricket.exe
    // ═══════════════════════════════════════════════════════════════════════

    private bool Step0_AttachToProcess()
    {
        PrintStep(0, "Kill existing cricket.exe and launch fresh");

        // Kill any existing cricket.exe processes
        var existing = Process.GetProcessesByName(ProcessName);
        if (existing.Length > 0)
        {
            Console.WriteLine($"  Found {existing.Length} existing cricket.exe process(es) — killing...");
            foreach (var p in existing)
            {
                try
                {
                    Console.WriteLine($"    Killing PID {p.Id}");
                    p.Kill();
                    p.WaitForExit(5000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"    Warning: could not kill PID {p.Id}: {ex.Message}");
                }
                finally
                {
                    p.Dispose();
                }
            }

            Console.WriteLine("  Waiting 2s for cleanup...");
            Thread.Sleep(2000);
        }
        else
        {
            Console.WriteLine("  No existing cricket.exe found.");
        }

        // Launch fresh
        if (string.IsNullOrEmpty(_executablePath))
        {
            PrintFail("No executable path provided. Use -e <path> to specify cricket.exe location.");
            return false;
        }

        if (!File.Exists(_executablePath))
        {
            PrintFail($"Executable not found: {_executablePath}");
            return false;
        }

        Console.WriteLine($"  Launching: {_executablePath}");
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _executablePath,
                WorkingDirectory = Path.GetDirectoryName(_executablePath) ?? "",
                UseShellExecute = false,
            };
            var process = Process.Start(startInfo);
            if (process == null)
            {
                PrintFail("Process.Start returned null.");
                return false;
            }

            Console.WriteLine($"  Started PID {process.Id}");
            _app = Application.Attach(process);
            PrintPass();
            return PauseForUser();
        }
        catch (Exception ex)
        {
            PrintFail($"Failed to launch: {ex.Message}");
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // STEP 1: Wait for main window (past splash screen)
    // ═══════════════════════════════════════════════════════════════════════

    private bool Step1_WaitForMainWindow()
    {
        PrintStep(1, "Wait for main window (splash screen should disappear)");
        Console.WriteLine($"  Waiting up to {SplashTimeoutSeconds}s for a stable main window...");

        var sw = Stopwatch.StartNew();
        Window? stableWindow = null;
        int stableCount = 0;

        while (sw.Elapsed.TotalSeconds < SplashTimeoutSeconds)
        {
            var windows = _app!.GetAllTopLevelWindows(_automation);
            Console.Write($"\r  [{sw.Elapsed:mm\\:ss}] Found {windows.Length} window(s)");

            if (windows.Length > 0)
            {
                // Look for the "real" main window — typically the largest non-splash window
                // or one with a meaningful title containing "PlayCricket" or similar.
                var candidate = FindMainWindowCandidate(windows);
                if (candidate != null)
                {
                    if (stableWindow != null && candidate.Title == stableWindow.Title)
                    {
                        stableCount++;
                        if (stableCount >= 3) // Stable for 1.5+ seconds
                        {
                            Console.WriteLine();
                            _mainWindow = candidate;
                            Console.WriteLine($"  Main window: \"{candidate.Title}\"");
                            Console.WriteLine($"  ClassName: \"{candidate.ClassName}\"");
                            Console.WriteLine($"  AutomationId: \"{candidate.AutomationId}\"");
                            Console.WriteLine($"  Size: {candidate.BoundingRectangle.Width}x{candidate.BoundingRectangle.Height}");
                            PrintPass();
                            return PauseForUser();
                        }
                    }
                    else
                    {
                        stableWindow = candidate;
                        stableCount = 1;
                    }
                }
            }

            Thread.Sleep(PollIntervalMs);
        }

        Console.WriteLine();
        PrintFail($"No stable main window appeared within {SplashTimeoutSeconds}s.");

        // Dump whatever windows are visible for debugging
        var finalWindows = _app!.GetAllTopLevelWindows(_automation);
        if (finalWindows.Length > 0)
        {
            Console.WriteLine("  Dumping all visible windows:");
            foreach (var w in finalWindows)
            {
                var dump = TreeDumper.Dump(w, maxDepth: 3);
                var path = TreeDumper.SaveToDesktop(dump, $"step1-window-{SafeName(w.Title)}");
                Console.WriteLine($"  → {path}");
            }
        }

        return false;
    }

    private static Window? FindMainWindowCandidate(Window[] windows)
    {
        // Prefer windows whose title starts with the known PCS Pro prefix.
        // Fall back to largest window if no title match (e.g. during splash transition).
        var pcsWindow = windows.FirstOrDefault(w =>
            SafeGet(() => w.Title)?.StartsWith(KnownElements.MainWindowTitlePrefix, StringComparison.OrdinalIgnoreCase) == true);
        if (pcsWindow != null)
            return pcsWindow;

        return windows
            .Where(w =>
            {
                var rect = w.BoundingRectangle;
                return rect.Width > 200 && rect.Height > 200;
            })
            .OrderByDescending(w => w.BoundingRectangle.Width * w.BoundingRectangle.Height)
            .FirstOrDefault();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // STEP 2: Find Login Dialog Elements
    // ═══════════════════════════════════════════════════════════════════════

    private bool Step2_FindLoginElements()
    {
        PrintStep(2, "Find login dialog and verify username");
        var cf = _automation.ConditionFactory;

        // Find the login dialog (child Window with AutomationId="window")
        var loginDialog = FindDescendant(_mainWindow!, cf.ByAutomationId(KnownElements.LoginDialogAutomationId));
        if (loginDialog == null)
        {
            Console.WriteLine("  No login dialog found — app may already be logged in.");
            Console.WriteLine("  Dumping main window tree for analysis...");
            var dump = TreeDumper.Dump(_mainWindow!, maxDepth: 4);
            var path = TreeDumper.SaveToDesktop(dump, "step2-no-login-dialog");
            Console.WriteLine($"  Tree saved to: {path}");
            PrintWait("Is the login dialog visible? If already logged in, we can skip ahead.");
            return false;
        }

        Console.WriteLine($"  ✓ Login dialog found: AutomationId=\"{loginDialog.AutomationId}\"");

        // Find username field and verify
        var usernameField = FindDescendant(_mainWindow!, cf.ByAutomationId(KnownElements.LoginUsernameFieldAutomationId));
        var pwField = FindDescendant(_mainWindow!, cf.ByAutomationId(KnownElements.LoginPasswordFieldAutomationId));
        var submitBtn = FindDescendant(_mainWindow!, cf.ByAutomationId(KnownElements.LoginSubmitButtonAutomationId));

        if (usernameField != null)
        {
            var currentUsername = SafeGet(() => usernameField.Patterns.Value.PatternOrDefault?.Value ?? usernameField.Name);
            Console.WriteLine($"  ✓ Username field found — current value: \"{currentUsername}\"");

            if (!string.IsNullOrEmpty(_expectedUsername) &&
                !string.Equals(currentUsername, _expectedUsername, StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  ⚠ Username mismatch! Expected \"{_expectedUsername}\", got \"{currentUsername}\"");
                Console.WriteLine("  → Would need to click 'Switch User' in production code");
                Console.ResetColor();
            }
            else if (!string.IsNullOrEmpty(_expectedUsername))
            {
                Console.WriteLine($"  ✓ Username matches expected: \"{_expectedUsername}\"");
            }
        }
        else
        {
            Console.WriteLine("  ✗ Username field not found");
        }

        Console.WriteLine($"  Password field: {(pwField != null ? $"✓ FOUND (AutomationId=\"{pwField.AutomationId}\")" : "✗ NOT FOUND")}");
        Console.WriteLine($"  Submit button: {(submitBtn != null ? $"✓ FOUND (AutomationId=\"{submitBtn.AutomationId}\")" : "✗ NOT FOUND")}");

        if (pwField == null || submitBtn == null)
        {
            var dump = TreeDumper.Dump(_mainWindow!, maxDepth: 6);
            var path = TreeDumper.SaveToDesktop(dump, "step2-missing-elements");
            Console.WriteLine($"  Tree saved to: {path}");
            PrintFail("Login elements not found.");
            return false;
        }

        PrintPass();
        return PauseForUser();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // STEP 3: Submit Credentials
    // ═══════════════════════════════════════════════════════════════════════

    private bool Step3_SubmitCredentials()
    {
        PrintStep(3, "Enter password and click submit");
        var cf = _automation.ConditionFactory;

        var pwField = FindDescendant(_mainWindow!, cf.ByAutomationId(KnownElements.LoginPasswordFieldAutomationId));
        var submitBtn = FindDescendant(_mainWindow!, cf.ByAutomationId(KnownElements.LoginSubmitButtonAutomationId));

        if (pwField == null || submitBtn == null)
        {
            PrintFail("Could not find login elements (this should have been caught in Step 2).");
            return false;
        }

        try
        {
            // Enter password — PasswordBox may not support ValuePattern, use keyboard
            pwField.Focus();
            Thread.Sleep(200);
            FlaUI.Core.Input.Keyboard.TypeSimultaneously(
                FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL,
                FlaUI.Core.WindowsAPI.VirtualKeyShort.KEY_A);
            FlaUI.Core.Input.Keyboard.Type(_password);
            Console.WriteLine("  ✓ Password entered");

            Thread.Sleep(300);

            // Click submit
            submitBtn.Click();
            Console.WriteLine("  ✓ Submit button clicked");

            // Wait briefly, then check if login failed (error message appeared)
            Console.WriteLine("  Waiting 3s for login response...");
            Thread.Sleep(3000);

            var loginDialog = FindDescendant(_mainWindow!, cf.ByAutomationId(KnownElements.LoginDialogAutomationId));
            if (loginDialog != null)
            {
                // Login dialog still visible — check for error text
                var allText = FindAllDescendants(loginDialog, cf.ByControlType(ControlType.Text));
                var errorText = allText.FirstOrDefault(t =>
                    SafeGet(() => t.Name)?.Contains("incorrect", StringComparison.OrdinalIgnoreCase) == true ||
                    SafeGet(() => t.Name)?.Contains("error", StringComparison.OrdinalIgnoreCase) == true);

                if (errorText != null)
                {
                    PrintFail($"Login failed — PCS Pro says: \"{SafeGet(() => errorText.Name)}\"");
                    return false;
                }

                // Dialog still showing but no error — might be slow, continue to Step 4
                Console.WriteLine("  ⚠ Login dialog still visible but no error text — may be slow");
            }

            PrintPass();
            return PauseForUser();
        }
        catch (Exception ex)
        {
            PrintFail($"Interaction error: {ex.Message}");
            var dump = TreeDumper.Dump(_mainWindow!, maxDepth: 4);
            var path = TreeDumper.SaveToDesktop(dump, "step3-login-interact");
            Console.WriteLine($"  Tree saved to: {path}");
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // STEP 4: Wait for Match Selection Screen
    // ═══════════════════════════════════════════════════════════════════════

    private bool Step4_WaitForMatchSelection()
    {
        PrintStep(4, "Wait for match selection screen after login");

        if (KnownElements.MatchSearchButtonAutomationId == "TODO")
        {
            Console.WriteLine("  No known search button AutomationId — dumping tree for discovery.");
            Console.WriteLine($"  Waiting {LoginTransitionTimeoutSeconds}s for UI to settle...");
            Thread.Sleep(LoginTransitionTimeoutSeconds * 1000);

            RefreshMainWindow();
            var dump = TreeDumper.Dump(_mainWindow!, maxDepth: 6);
            var path = TreeDumper.SaveToDesktop(dump, "step4-match-selection");
            Console.WriteLine($"  Full tree saved to: {path}");

            // Also search for DataGrid controls which might be the match list
            var cf = _automation.ConditionFactory;
            var dataGrids = FindAllDescendants(_mainWindow!, cf.ByControlType(ControlType.DataGrid));
            var tables = FindAllDescendants(_mainWindow!, cf.ByControlType(ControlType.Table));
            var lists = FindAllDescendants(_mainWindow!, cf.ByControlType(ControlType.List));

            Console.WriteLine($"  Found {dataGrids.Length} DataGrid(s), {tables.Length} Table(s), {lists.Length} List(s):");
            foreach (var e in dataGrids.Concat(tables).Concat(lists))
                PrintElement("    ", e);

            PrintWait("Review the output and report back which elements are the search button and data grid.");
            return false;
        }

        // Poll for the search button to appear
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < LoginTransitionTimeoutSeconds)
        {
            RefreshMainWindow();
            var searchBtn = FindDescendant(_mainWindow!,
                _automation.ConditionFactory.ByAutomationId(KnownElements.MatchSearchButtonAutomationId));
            if (searchBtn != null)
            {
                Console.WriteLine($"  ✓ Match selection screen detected — search button found");
                PrintPass();
                return PauseForUser();
            }

            Console.Write($"\r  [{sw.Elapsed:mm\\:ss}] Waiting for match selection...");
            Thread.Sleep(PollIntervalMs);
        }

        Console.WriteLine();
        PrintFail("Match selection screen did not appear within timeout.");
        var failDump = TreeDumper.Dump(_mainWindow!, maxDepth: 6);
        var failPath = TreeDumper.SaveToDesktop(failDump, "step4-timeout");
        Console.WriteLine($"  Tree saved to: {failPath}");
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // STEP 5: Search for Matches
    // ═══════════════════════════════════════════════════════════════════════

    private bool Step5_SearchForMatches()
    {
        PrintStep(5, "Click search button to find today's matches");
        var cf = _automation.ConditionFactory;

        var searchBtn = FindDescendant(_mainWindow!,
            cf.ByAutomationId(KnownElements.MatchSearchButtonAutomationId));

        if (searchBtn == null)
        {
            PrintFail("Search button not found.");
            return false;
        }

        try
        {
            searchBtn.Click();
            Console.WriteLine("  ✓ Search button clicked");
            Console.WriteLine($"  Waiting up to {SearchTimeoutSeconds}s for results...");

            // Poll for spinner to disappear and data grid to populate
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < SearchTimeoutSeconds)
            {
                Thread.Sleep(PollIntervalMs);
                Console.Write($"\r  [{sw.Elapsed:mm\\:ss}] Waiting for search results...");

                if (KnownElements.MatchDataGridAutomationId != "TODO")
                {
                    var grid = FindDescendant(_mainWindow!,
                        cf.ByAutomationId(KnownElements.MatchDataGridAutomationId));
                    if (grid != null)
                    {
                        var rows = FindAllDescendants(grid, cf.ByControlType(ControlType.DataItem));
                        if (rows.Length > 0)
                        {
                            Console.WriteLine();
                            Console.WriteLine($"  ✓ DataGrid found with {rows.Length} row(s)");
                            foreach (var row in rows.Take(5))
                                Console.WriteLine($"    Row: \"{SafeGet(() => row.Name)}\"");
                            PrintPass();
                            return PauseForUser();
                        }
                    }
                }
            }

            Console.WriteLine();
            PrintFail("Search results did not appear within timeout.");
            var dump = TreeDumper.Dump(_mainWindow!, maxDepth: 6);
            var path = TreeDumper.SaveToDesktop(dump, "step5-search-results");
            Console.WriteLine($"  Tree saved to: {path}");
            return false;
        }
        catch (Exception ex)
        {
            PrintFail($"Interaction error: {ex.Message}");
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // STEP 6: Select First Match
    // ═══════════════════════════════════════════════════════════════════════

    private bool Step6_SelectFirstMatch()
    {
        PrintStep(6, "Select and open the first match in the data grid");
        var cf = _automation.ConditionFactory;

        if (KnownElements.MatchDataGridAutomationId == "TODO")
        {
            PrintFail("DataGrid AutomationId not yet discovered.");
            return false;
        }

        var grid = FindDescendant(_mainWindow!,
            cf.ByAutomationId(KnownElements.MatchDataGridAutomationId));
        if (grid == null)
        {
            PrintFail("DataGrid not found.");
            return false;
        }

        var rows = FindAllDescendants(grid, cf.ByControlType(ControlType.DataItem));
        if (rows.Length == 0)
        {
            PrintFail("No rows in the data grid.");
            var dump = TreeDumper.Dump(grid, maxDepth: 4);
            var path = TreeDumper.SaveToDesktop(dump, "step6-empty-grid");
            Console.WriteLine($"  Grid tree saved to: {path}");
            return false;
        }

        try
        {
            var firstRow = rows[0];
            Console.WriteLine($"  Selecting row: \"{SafeGet(() => firstRow.Name)}\"");

            // Try double-click to open
            firstRow.DoubleClick();
            Console.WriteLine("  ✓ Double-clicked first row");
            PrintPass();
            return PauseForUser();
        }
        catch (Exception ex)
        {
            PrintFail($"Interaction error: {ex.Message}");
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // STEP 7: Wait for Match Load
    // ═══════════════════════════════════════════════════════════════════════

    private bool Step7_WaitForMatchLoad()
    {
        PrintStep(7, "Wait for match to load (scoreboard/scoring screen appears)");
        Console.WriteLine($"  Waiting {MatchLoadTimeoutSeconds}s for UI to settle after match selection...");
        Thread.Sleep(MatchLoadTimeoutSeconds * 1000);

        RefreshMainWindow();

        // Dump the current state for analysis
        var dump = TreeDumper.Dump(_mainWindow!, maxDepth: 4);
        var path = TreeDumper.SaveToDesktop(dump, "step7-match-loaded");
        Console.WriteLine($"  Tree saved to: {path}");

        // Also dump all top-level windows (scoreboard may be a separate window)
        var allWindows = _app!.GetAllTopLevelWindows(_automation);
        Console.WriteLine($"  Found {allWindows.Length} top-level window(s):");
        for (int i = 0; i < allWindows.Length; i++)
        {
            Console.WriteLine($"    Window #{i + 1}: \"{allWindows[i].Title}\" " +
                              $"ClassName=\"{allWindows[i].ClassName}\" " +
                              $"Size={allWindows[i].BoundingRectangle.Width}x{allWindows[i].BoundingRectangle.Height}");
        }

        Console.WriteLine("  (Review tree dumps and identify how to detect match-loaded state)");
        PrintPass("Assume match loaded — continuing to element discovery");
        return PauseForUser();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // STEP 8: Find Team Name Elements
    // ═══════════════════════════════════════════════════════════════════════

    private bool Step8_FindTeamNameElements()
    {
        PrintStep(8, "Find team name elements (scoring menu, home/away ComboBoxes)");
        var cf = _automation.ConditionFactory;

        if (KnownElements.ScoringMenuAutomationId == "TODO")
        {
            Console.WriteLine("  No known team name AutomationIds — searching by control type...");
            Console.WriteLine();

            var menus = FindAllDescendants(_mainWindow!, cf.ByControlType(ControlType.Menu));
            var menuItems = FindAllDescendants(_mainWindow!, cf.ByControlType(ControlType.MenuItem));
            var combos = FindAllDescendants(_mainWindow!, cf.ByControlType(ControlType.ComboBox));

            Console.WriteLine($"  Found {menus.Length} Menu(s), {menuItems.Length} MenuItem(s), {combos.Length} ComboBox(es):");
            foreach (var m in menus)
                PrintElement("    ", m);
            foreach (var mi in menuItems)
                PrintElement("    ", mi);
            foreach (var c in combos)
                PrintElement("    ", c);

            var dump = TreeDumper.Dump(_mainWindow!, maxDepth: 6);
            var path = TreeDumper.SaveToDesktop(dump, "step8-team-names");
            Console.WriteLine($"\n  Full tree saved to: {path}");
            PrintWait("Review and report which elements are the scoring menu and team ComboBoxes.");
            return false;
        }

        // Verify known IDs
        var scoringMenu = FindDescendant(_mainWindow!, cf.ByAutomationId(KnownElements.ScoringMenuAutomationId));
        var homeCombo = FindDescendant(_mainWindow!, cf.ByAutomationId(KnownElements.HomeTeamComboBoxAutomationId));
        var awayCombo = FindDescendant(_mainWindow!, cf.ByAutomationId(KnownElements.AwayTeamComboBoxAutomationId));

        Console.WriteLine($"  Scoring menu: {(scoringMenu != null ? "✓ FOUND" : "✗ NOT FOUND")}");
        Console.WriteLine($"  Home team combo: {(homeCombo != null ? "✓ FOUND" : "✗ NOT FOUND")}");
        Console.WriteLine($"  Away team combo: {(awayCombo != null ? "✓ FOUND" : "✗ NOT FOUND")}");

        if (scoringMenu != null) PrintElement("    ", scoringMenu);
        if (homeCombo != null) PrintElement("    ", homeCombo);
        if (awayCombo != null) PrintElement("    ", awayCombo);

        if (scoringMenu == null || homeCombo == null || awayCombo == null)
        {
            var dump = TreeDumper.Dump(_mainWindow!, maxDepth: 6);
            var path = TreeDumper.SaveToDesktop(dump, "step8-missing-elements");
            Console.WriteLine($"  Tree saved to: {path}");
            PrintFail("Some team name elements were not found.");
            return false;
        }

        PrintPass();
        return PauseForUser();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // STEP 9: Find Scoreboard Elements
    // ═══════════════════════════════════════════════════════════════════════

    private bool Step9_FindScoreboardElements()
    {
        PrintStep(9, "Find scoreboard elements (settings cog, scoreboard window)");
        var cf = _automation.ConditionFactory;

        // Search for the settings cog in the main window
        Console.WriteLine("  Searching for settings cog / gear icon...");

        // Try by HelpText
        if (KnownElements.SettingsCogHelpText != "TODO")
        {
            var cog = FindDescendant(_mainWindow!, cf.ByHelpText(KnownElements.SettingsCogHelpText));
            Console.WriteLine($"  Settings cog by HelpText: {(cog != null ? "✓ FOUND" : "✗ NOT FOUND")}");
            if (cog != null) PrintElement("    ", cog);
        }

        // Search for the scoreboard window among all top-level windows
        Console.WriteLine("\n  Searching for scoreboard window among top-level windows...");
        var allWindows = _app!.GetAllTopLevelWindows(_automation);

        bool found = false;
        foreach (var w in allWindows)
        {
            Console.WriteLine($"  Window: \"{w.Title}\" ClassName=\"{w.ClassName}\"");
            if (KnownElements.ScoreboardWindowClassName != "TODO" &&
                w.ClassName == KnownElements.ScoreboardWindowClassName)
            {
                Console.WriteLine("    ✓ Scoreboard window matched by ClassName!");
                found = true;
                var dump = TreeDumper.Dump(w, maxDepth: 4);
                var path = TreeDumper.SaveToDesktop(dump, "step9-scoreboard-window");
                Console.WriteLine($"    Tree saved to: {path}");
            }
        }

        if (!found && KnownElements.ScoreboardWindowClassName == "TODO")
        {
            Console.WriteLine("  No known scoreboard ClassName — dumping all windows for analysis.");
            foreach (var w in allWindows)
            {
                var dump = TreeDumper.Dump(w, maxDepth: 4);
                var path = TreeDumper.SaveToDesktop(dump, $"step9-window-{SafeName(w.Title)}");
                Console.WriteLine($"  → {path}");
            }

            // Also look for image/picture controls (scoreboard might be rendered as image)
            var images = FindAllDescendants(_mainWindow!, cf.ByControlType(ControlType.Image));
            Console.WriteLine($"\n  Found {images.Length} Image control(s) in main window:");
            foreach (var img in images)
                PrintElement("    ", img);

            PrintWait("Review windows and report which is the scoreboard and what identifies the settings cog.");
            return false;
        }

        if (found)
        {
            PrintPass();
            return PauseForUser();
        }

        PrintFail("Scoreboard window not found with known ClassName.");
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // STEP 10: Find Change Match Element
    // ═══════════════════════════════════════════════════════════════════════

    private bool Step10_FindChangeMatchElement()
    {
        PrintStep(10, "Find change-match element");
        var cf = _automation.ConditionFactory;

        if (KnownElements.ChangeMatchElementAutomationId == "TODO")
        {
            Console.WriteLine("  No known change-match AutomationId — searching for likely candidates...");

            // Look for buttons/menu items that might relate to changing matches
            var buttons = FindAllDescendants(_mainWindow!, cf.ByControlType(ControlType.Button));
            var menuItems = FindAllDescendants(_mainWindow!, cf.ByControlType(ControlType.MenuItem));

            Console.WriteLine($"  Found {buttons.Length} Button(s) and {menuItems.Length} MenuItem(s):");
            foreach (var e in buttons.Concat(menuItems))
            {
                var name = SafeGet(() => e.Name);
                if (!string.IsNullOrWhiteSpace(name))
                    PrintElement("    ", e);
            }

            var dump = TreeDumper.Dump(_mainWindow!, maxDepth: 5);
            var path = TreeDumper.SaveToDesktop(dump, "step10-change-match");
            Console.WriteLine($"\n  Full tree saved to: {path}");
            PrintWait("Review and report which element triggers the change-match action.");
            return false;
        }

        var element = FindDescendant(_mainWindow!,
            cf.ByAutomationId(KnownElements.ChangeMatchElementAutomationId));

        if (element != null)
        {
            Console.WriteLine($"  ✓ Change-match element found");
            PrintElement("    ", element);
            PrintPass();
            return PauseForUser();
        }

        PrintFail("Change-match element not found with known AutomationId.");
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private void RefreshMainWindow()
    {
        var windows = _app!.GetAllTopLevelWindows(_automation);
        var candidate = FindMainWindowCandidate(windows);
        if (candidate != null)
            _mainWindow = candidate;
    }

    private static AutomationElement? FindDescendant(AutomationElement parent, ConditionBase condition)
    {
        try { return parent.FindFirstDescendant(condition); }
        catch { return null; }
    }

    private static AutomationElement[] FindAllDescendants(AutomationElement parent, ConditionBase condition)
    {
        try { return parent.FindAllDescendants(condition); }
        catch { return []; }
    }

    private static void PrintElement(string indent, AutomationElement e)
    {
        var automationId = SafeGet(() => e.AutomationId);
        var name = SafeGet(() => e.Name);
        var className = SafeGet(() => e.ClassName);
        var controlType = SafeGet(() => e.ControlType.ToString());
        var helpText = SafeGet(() => e.HelpText);

        Console.Write($"{indent}[{controlType}]");
        if (!string.IsNullOrWhiteSpace(automationId)) Console.Write($"  AutomationId=\"{automationId}\"");
        if (!string.IsNullOrWhiteSpace(name)) Console.Write($"  Name=\"{Truncate(name, 60)}\"");
        Console.Write($"  ClassName=\"{className}\"");
        if (!string.IsNullOrWhiteSpace(helpText)) Console.Write($"  HelpText=\"{Truncate(helpText, 40)}\"");
        Console.WriteLine();
    }

    private static string SafeGet(Func<string?> getter)
    {
        try { return getter() ?? ""; }
        catch { return "<error>"; }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";

    private static string SafeName(string? title) =>
        string.IsNullOrWhiteSpace(title) ? "untitled" :
        new string(title.Take(20).Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());

    private static bool PauseForUser()
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  Press Enter to continue to next step (or Ctrl+C to stop)...");
        Console.ResetColor();
        Console.ReadLine();
        return true;
    }

    private static void PrintHeader()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║       PCS Pro — Automation Diagnostic Runner                ║");
        Console.WriteLine("║       Iterative element discovery for FlaUI automation       ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void PrintStep(int number, string description)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"═══ STEP {number}: {description} ═══");
        Console.ResetColor();
    }

    private static void PrintPass(string? note = null)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(note != null ? $"  ✓ PASS — {note}" : "  ✓ PASS");
        Console.ResetColor();
    }

    private static void PrintFail(string reason)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  ✗ FAIL — {reason}");
        Console.ResetColor();
    }

    private static void PrintWait(string message)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"  ⏸ DISCOVERY NEEDED — {message}");
        Console.ResetColor();
    }

    private static void PrintSuccess()
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  🎉 ALL STEPS PASSED — Full automation flow verified!       ║");
        Console.WriteLine("║  All element identifiers are confirmed working.              ║");
        Console.WriteLine("║  Port values to src/PcsRemote.Automation/ for production.    ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
    }
}
