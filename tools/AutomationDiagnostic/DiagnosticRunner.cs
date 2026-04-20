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
    private const int SplashTimeoutSeconds = 90;
    private const int LoginTransitionTimeoutSeconds = 15;
    private const int SearchTimeoutSeconds = 20;
    private const int MatchLoadTimeoutSeconds = 30;

    private readonly string _password;
    private readonly string _expectedUsername;
    private readonly string? _executablePath;
    private readonly string _siteName;
    private readonly string _searchDate;
    private readonly string _outputDirectory;
    private readonly UIA3Automation _automation;
    private Application? _app;
    private Window? _mainWindow;
    private string? _titleBeforeMatchOpen;

    public DiagnosticRunner(string password, string expectedUsername, string? executablePath,
        string siteName, string searchDate, string outputDirectory)
    {
        _password = password;
        _expectedUsername = expectedUsername;
        _executablePath = executablePath;
        _siteName = siteName;
        _searchDate = searchDate;
        _outputDirectory = outputDirectory;
        _automation = new UIA3Automation();

        Directory.CreateDirectory(_outputDirectory);
    }

    public void Dispose()
    {
        _automation.Dispose();
    }

    /// <summary>
    /// Runs the full diagnostic flow. Stops at the first step that fails
    /// and dumps the UI tree at that point. All console output is mirrored
    /// to a single execution log file in the output directory.
    /// </summary>
    public void Run()
    {
        var logPath = Path.Combine(_outputDirectory,
            $"pcs-diag-run-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var originalOut = Console.Out;
        using var fileWriter = new StreamWriter(logPath, false, System.Text.Encoding.UTF8)
        {
            AutoFlush = true
        };
        using var dual = new DualWriter(originalOut, fileWriter);
        Console.SetOut(dual);

        try
        {
            RunSteps(logPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  💥 UNHANDLED EXCEPTION                                     ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine($"  {ex.GetType().FullName}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.WriteLine($"\n  Execution log saved to: {logPath}");
        }
    }

    private void RunSteps(string logPath)
    {
        PrintHeader();
        Console.WriteLine($"  Log: {logPath}");
        Console.WriteLine($"  Config: site=\"{_siteName}\" date=\"{_searchDate}\"");
        Console.WriteLine();

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
            var windows = GetTopLevelWindows();
            if (windows.Length == 0)
            {
                Console.Write($"\r  [{sw.Elapsed:mm\\:ss}] Waiting for windows...          ");
                Thread.Sleep(PollIntervalMs);
                continue;
            }

            Console.Write($"\r  [{sw.Elapsed:mm\\:ss}] Found {windows.Length} window(s)");

            // Look for the "real" main window — typically the largest non-splash window
            // or one with a meaningful title containing "PlayCricket" or similar.
            var candidate = FindMainWindowCandidate(windows);
            if (candidate != null)
            {
                if (stableWindow != null && candidate.Title == stableWindow.Title)
                {
                    stableCount++;
                    if (stableCount >= 2) // Stable across 2 polls
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

            Thread.Sleep(PollIntervalMs);
        }

        Console.WriteLine();
        PrintFail($"No stable main window appeared within {SplashTimeoutSeconds}s.");

        // Dump whatever windows are visible for debugging
        var finalWindows = GetTopLevelWindows();
        if (finalWindows.Length > 0)
        {
            Console.WriteLine("  Dumping all visible windows:");
            foreach (var w in finalWindows)
            {
                var dump = TreeDumper.Dump(w, maxDepth: 3);
                Console.WriteLine($"\n  ── UI Tree: step1-window-{SafeName(w.Title)} ──");
                Console.WriteLine(dump);
                Console.WriteLine($"  ── End ──");
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
            Console.WriteLine($"\n  ── UI Tree: step2-no-login-dialog ──");
            Console.WriteLine(dump);
            Console.WriteLine($"  ── End ──");
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
            Console.WriteLine($"\n  ── UI Tree: step2-missing-elements ──");
            Console.WriteLine(dump);
            Console.WriteLine($"  ── End ──");
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
            Console.WriteLine("  Waiting for login response...");
            Thread.Sleep(1000);

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
            Console.WriteLine($"\n  ── UI Tree: step3-login-interact ──");
            Console.WriteLine(dump);
            Console.WriteLine($"  ── End ──");
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // STEP 4: Navigate to Match Selection via File → Open Match...
    // ═══════════════════════════════════════════════════════════════════════

    private bool Step4_WaitForMatchSelection()
    {
        PrintStep(4, "Navigate to match selection via File → Open Match...");

        try
        {
            var cf = _automation.ConditionFactory;

            // PCS Pro reopens the last match after login — match selection doesn't auto-appear.
            // We must use File → Open Match... to get the selection dialog.

            Console.WriteLine("  Opening File menu...");
            var fileMenu = FindDescendant(_mainWindow!, cf.ByAutomationId(KnownElements.FileMenuAutomationId));
            if (fileMenu == null)
            {
                PrintFail("File menu not found");
                DumpAndSave("step4-no-file-menu");
                return false;
            }

            fileMenu.Click();
            Thread.Sleep(200);

            // Find the "Open Match..." menu item within the expanded File menu
            Console.WriteLine("  File menu clicked — looking for Open Match...");
            var openMatch = FindDescendant(fileMenu,
                cf.ByName(KnownElements.OpenMatchMenuItemName));

            if (openMatch == null)
            {
                // Dump tree to discover what menu items are available
                Console.WriteLine("  ⚠ 'Open Match...' not found by Name — dumping menu tree for discovery.");
                var dump = TreeDumper.Dump(fileMenu, maxDepth: 4);
                Console.WriteLine($"\n  ── UI Tree: step4-file-menu-expanded ──");
                Console.WriteLine(dump);
                Console.WriteLine($"  ── End ──");
                PrintWait("Review the file menu dump — look for the Open Match menu item name.");
                return false;
            }

            PrintElement("  Found: ", openMatch);
            Console.WriteLine("  Clicking 'Open Match...'...");
            openMatch.Click();
            Console.WriteLine("  ✓ Open Match clicked");

            // Wait for match selection dialog to appear
            Console.WriteLine($"  Waiting up to {LoginTransitionTimeoutSeconds}s for match selection dialog...");
            Thread.Sleep(500); // brief pause before polling

            RefreshMainWindow();

            if (KnownElements.MatchSearchButtonAutomationId == "TODO")
            {
                Console.WriteLine("  No known search button AutomationId — dumping tree for discovery.");
                var dump = TreeDumper.Dump(_mainWindow!, maxDepth: 8);
                Console.WriteLine($"\n  ── UI Tree: step4-match-selection ──");
                Console.WriteLine(dump);
                Console.WriteLine($"  ── End ──");

                // Also search for DataGrid controls which might be the match list
                var dataGrids = FindAllDescendants(_mainWindow!, cf.ByControlType(ControlType.DataGrid));
                var tables = FindAllDescendants(_mainWindow!, cf.ByControlType(ControlType.Table));
                var lists = FindAllDescendants(_mainWindow!, cf.ByControlType(ControlType.List));

                Console.WriteLine($"  Found {dataGrids.Length} DataGrid(s), {tables.Length} Table(s), {lists.Length} List(s):");
                foreach (var e in dataGrids.Concat(tables).Concat(lists))
                    PrintElement("    ", e);

                // Search for any child Window elements (match selection might be a dialog)
                var childWindows = FindAllDescendants(_mainWindow!, cf.ByControlType(ControlType.Window));
                if (childWindows.Length > 0)
                {
                    Console.WriteLine($"  Found {childWindows.Length} child Window(s):");
                    foreach (var w in childWindows)
                        PrintElement("    ", w);
                }

                PrintWait("Review the output and report back which elements are the search button and data grid.");
                return false;
            }

            // Poll for the Open Match dialog or DataGrid to appear
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < LoginTransitionTimeoutSeconds)
            {
                RefreshMainWindow();

                // Check for the Open Match dialog by name
                var openMatchDialog = FindDescendant(_mainWindow!,
                    cf.ByName(KnownElements.MatchSelectionDialogName));
                if (openMatchDialog != null)
                {
                    Console.WriteLine($"  ✓ Open Match dialog detected");
                    PrintPass();
                    return PauseForUser();
                }

                Console.Write($"\r  [{sw.Elapsed:mm\\:ss}] Waiting for match selection...");
                Thread.Sleep(PollIntervalMs);
            }

            Console.WriteLine();
            PrintFail("Match selection screen did not appear within timeout.");
            DumpAndSave("step4-timeout");
            return false;
        }
        catch (Exception ex)
        {
            PrintFail($"Error navigating to match selection: {ex.Message}");
            DumpAndSave("step4-error");
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // STEP 5: Set Filters and Search for Matches
    // ═══════════════════════════════════════════════════════════════════════

    private bool Step5_SearchForMatches()
    {
        PrintStep(5, "Set filters and search for matches");
        var cf = _automation.ConditionFactory;

        try
        {
            // Find the Open Match dialog
            var dialog = FindDescendant(_mainWindow!, cf.ByName(KnownElements.MatchSelectionDialogName));
            if (dialog == null)
            {
                PrintFail("Open Match dialog not found.");
                DumpAndSave("step5-no-dialog");
                return false;
            }

            // Safety check: verify "Connected to Server" label
            Console.WriteLine("  Checking server connection status...");
            var connectedText = FindDescendant(dialog, cf.ByName("Connected to Server"));
            if (connectedText == null)
            {
                // Check for alternative states
                var notConnected = FindDescendant(dialog, cf.ByName("Not Connected to Server"));
                if (notConnected != null)
                {
                    PrintFail("PCS Pro is NOT connected to server — cannot search online matches.");
                    return false;
                }

                // Dump to see what the actual status text says
                Console.WriteLine("  ⚠ 'Connected to Server' label not found — dumping to investigate...");
                DumpAndSave("step5-connection-unknown", maxDepth: 8);
                PrintFail("Unable to verify server connection status.");
                return false;
            }
            Console.WriteLine("  ✓ Connected to Server confirmed");

            // 1. Click "Clear Filters"
            Console.WriteLine("  Clicking 'Clear Filters'...");
            var clearFilters = FindDescendant(dialog, cf.ByName("Clear Filters"));
            if (clearFilters == null)
            {
                PrintFail("'Clear Filters' link not found.");
                DumpAndSave("step5-no-clear-filters");
                return false;
            }
            clearFilters.Click();
            Thread.Sleep(500);
            Console.WriteLine("  ✓ Filters cleared");

            // 2. Select site from Site dropdown
            Console.WriteLine($"  Setting Site to '{_siteName}'...");
            var siteComboBoxes = FindAllDescendants(dialog, cf.ByControlType(ControlType.ComboBox));
            // The Site combobox is the first ComboBox without an AutomationId (or with no specific ID)
            // From the tree dump: it's the ComboBox right after the "Site:" label
            AutomationElement? siteCombo = null;
            foreach (var combo in siteComboBoxes)
            {
                var autoId = SafeGet(() => combo.AutomationId);
                if (string.IsNullOrEmpty(autoId) || autoId == "<error>")
                {
                    siteCombo = combo;
                    break;
                }
            }

            if (siteCombo == null)
            {
                PrintFail("Site ComboBox not found.");
                DumpAndSave("step5-no-site-combo");
                return false;
            }

            // Expand the combo and look for matching item
            siteCombo.Click();
            Thread.Sleep(500);

            var siteItem = FindDescendant(_mainWindow!, cf.ByName(_siteName));
            if (siteItem == null)
            {
                Console.WriteLine($"  ⚠ Site '{_siteName}' not found in dropdown — dumping...");
                DumpAndSave("step5-site-not-found", maxDepth: 8);
                PrintFail($"Site '{_siteName}' not found in Site dropdown.");
                return false;
            }
            siteItem.Click();
            Thread.Sleep(300);
            Console.WriteLine($"  ✓ Site set to '{_siteName}'");

            // 3. Set Date From and Date To
            Console.WriteLine($"  Setting date range to '{_searchDate}'...");
            var datePickers = FindAllDescendants(dialog,
                cf.ByClassName("DatePicker"));

            if (datePickers.Length < 2)
            {
                PrintFail($"Expected 2 DatePicker controls, found {datePickers.Length}.");
                DumpAndSave("step5-date-pickers");
                return false;
            }

            // Set both Date From and Date To to the search date
            for (int i = 0; i < 2; i++)
            {
                var label = i == 0 ? "Date From" : "Date To";
                var textBox = FindDescendant(datePickers[i],
                    cf.ByAutomationId("PART_TextBox"));
                if (textBox == null)
                {
                    PrintFail($"{label}: TextBox not found inside DatePicker.");
                    return false;
                }

                textBox.Click();
                Thread.Sleep(200);

                // Select all existing text and replace with search date
                FlaUI.Core.Input.Keyboard.TypeSimultaneously(
                    FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL,
                    FlaUI.Core.WindowsAPI.VirtualKeyShort.KEY_A);
                FlaUI.Core.Input.Keyboard.Type(_searchDate);
                Thread.Sleep(200);

                // Tab out to commit the value
                FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.TAB);
                Thread.Sleep(200);

                Console.WriteLine($"  ✓ {label} set to '{_searchDate}'");
            }

            // 4. Press Enter to trigger search
            Console.WriteLine("  Pressing Enter to trigger search...");
            dialog.Focus();
            Thread.Sleep(200);
            FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ENTER);
            Thread.Sleep(500);

            Console.WriteLine($"  Waiting up to {SearchTimeoutSeconds}s for results...");

            // Poll for data grid to populate
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < SearchTimeoutSeconds)
            {
                Thread.Sleep(PollIntervalMs);
                Console.Write($"\r  [{sw.Elapsed:mm\\:ss}] Waiting for search results...");

                RefreshMainWindow();
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

            Console.WriteLine();
            PrintFail("Search results did not appear within timeout. Grid may be empty or spinner still active.");
            DumpAndSave("step5-search-timeout", maxDepth: 8);
            return false;
        }
        catch (Exception ex)
        {
            PrintFail($"Interaction error: {ex.Message}");
            DumpAndSave("step5-error");
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // STEP 6: Select First Match and Open Read Only
    // ═══════════════════════════════════════════════════════════════════════

    private bool Step6_SelectFirstMatch()
    {
        PrintStep(6, "Select first match in grid, capture team names, and click 'Open Read Only'");
        var cf = _automation.ConditionFactory;

        var grid = FindDescendant(_mainWindow!,
            cf.ByAutomationId(KnownElements.MatchDataGridAutomationId));
        if (grid == null)
        {
            PrintFail("DataGrid not found.");
            DumpAndSave("step6-no-grid");
            return false;
        }

        var rows = FindAllDescendants(grid, cf.ByControlType(ControlType.DataItem));
        if (rows.Length == 0)
        {
            PrintFail("No rows in the data grid.");
            DumpAndSave("step6-empty-grid", maxDepth: 8);
            return false;
        }

        try
        {
            var firstRow = rows[0];
            Console.WriteLine($"  Selecting row: \"{SafeGet(() => firstRow.Name)}\"");
            firstRow.Click();
            Console.WriteLine("  ✓ First row selected");
            Thread.Sleep(300);

            // Extract team names from the selected row's cells
            var cells = FindAllDescendants(firstRow, cf.ByControlType(ControlType.Text));
            Console.WriteLine($"  Row has {cells.Length} Text element(s):");
            for (int i = 0; i < cells.Length; i++)
                Console.WriteLine($"    Cell[{i}]: \"{SafeGet(() => cells[i].Name)}\"");

            // Also try getting cells via Custom/Edit control types (WPF DataGridCell)
            var customCells = FindAllDescendants(firstRow, cf.ByControlType(ControlType.Custom));
            if (customCells.Length > 0)
            {
                Console.WriteLine($"  Row has {customCells.Length} Custom element(s):");
                for (int i = 0; i < customCells.Length; i++)
                    PrintElement($"    [{i}] ", customCells[i]);
            }

            // Dump the row subtree for full visibility of cell structure
            var rowDump = TreeDumper.Dump(firstRow, maxDepth: 4);
            Console.WriteLine($"\n  ── UI Tree: step6-selected-row ──");
            Console.WriteLine(rowDump);
            Console.WriteLine($"  ── End ──");

            // Click "Open Read Only" — this instance must always open readonly
            // Re-find the button fresh (UI tree may have shifted during enumeration)
            var dialog = FindDescendant(_mainWindow!, cf.ByName(KnownElements.MatchSelectionDialogName));
            var openReadOnly = dialog != null
                ? FindDescendant(dialog, cf.ByAutomationId(KnownElements.OpenReadOnlyButtonAutomationId))
                : FindDescendant(_mainWindow!, cf.ByAutomationId(KnownElements.OpenReadOnlyButtonAutomationId));
            // Capture window title before opening — Step 7 will wait for it to change
            _titleBeforeMatchOpen = SafeGet(() => _mainWindow!.Title);

            if (openReadOnly == null)
            {
                Console.WriteLine("  ⚠ 'Open Read Only' button not found — trying double-click as fallback");
                firstRow.DoubleClick();
                Console.WriteLine("  ✓ Double-clicked first row");
            }
            else
            {
                PrintElement("  Found: ", openReadOnly);
                // Use InvokePattern for reliable button activation (mouse Click can miss in WPF)
                openReadOnly.AsButton().Invoke();
                Console.WriteLine("  ✓ 'Open Read Only' invoked");
            }

            PrintPass();
            return PauseForUser();
        }
        catch (Exception ex)
        {
            PrintFail($"Interaction error: {ex.Message}");
            DumpAndSave("step6-error");
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // STEP 7: Wait for Match Load
    // ═══════════════════════════════════════════════════════════════════════

    private bool Step7_WaitForMatchLoad()
    {
        PrintStep(7, "Wait for match to load (Open Match dialog closes, sync status = Up to Date)");
        var cf = _automation.ConditionFactory;
        var sw = Stopwatch.StartNew();
        bool dialogGone = false;
        bool titleChanged = false;
        bool syncReady = false;

        Console.WriteLine($"  [{Timestamp()}] Previous title: \"{_titleBeforeMatchOpen}\"");

        while (sw.Elapsed.TotalSeconds < MatchLoadTimeoutSeconds)
        {
            RefreshMainWindow();

            if (!dialogGone)
            {
                var dialog = FindDescendant(_mainWindow!,
                    cf.ByName(KnownElements.MatchSelectionDialogName));
                if (dialog == null)
                {
                    dialogGone = true;
                    Console.WriteLine($"  [{Timestamp()}] ✓ Open Match dialog closed [{sw.Elapsed:mm\\:ss}]");
                }
            }

            if (dialogGone && !titleChanged)
            {
                var currentTitle = SafeGet(() => _mainWindow!.Title);
                if (currentTitle != _titleBeforeMatchOpen)
                {
                    titleChanged = true;
                    Console.WriteLine($"  [{Timestamp()}] ✓ Window title changed [{sw.Elapsed:mm\\:ss}]");
                    Console.WriteLine($"  [{Timestamp()}]   New: \"{currentTitle}\"");
                }
            }

            if (dialogGone && titleChanged && !syncReady)
            {
                var syncBar = FindDescendant(_mainWindow!, cf.ByClassName(KnownElements.StatusBarClassName));
                if (syncBar != null)
                {
                    var syncText = FindAllDescendants(syncBar, cf.ByControlType(ControlType.Text))
                        .FirstOrDefault(t => SafeGet(() => t.Name) == "Up to Date");
                    if (syncText != null)
                    {
                        syncReady = true;
                        Console.WriteLine($"  [{Timestamp()}] ✓ Scoring Sync Status: Up to Date [{sw.Elapsed:mm\\:ss}]");
                    }
                }
            }

            if (dialogGone && titleChanged && syncReady)
                break;

            var status = $"dialog={(!dialogGone ? "open" : "closed")} title={(!titleChanged ? "unchanged" : "changed")} sync={(!syncReady ? "pending" : "ready")}";
            Console.Write($"\r  [{sw.Elapsed:mm\\:ss}] Waiting... {status}");
            Thread.Sleep(PollIntervalMs);
        }

        Console.WriteLine();

        if (!dialogGone)
        {
            PrintFail("Open Match dialog did not close within timeout.");
            DumpAndSave("step7-dialog-stuck");
            return false;
        }

        if (!titleChanged)
        {
            PrintFail("Window title did not change — match may not have loaded.");
            Console.WriteLine($"  [{Timestamp()}] Title still: \"{SafeGet(() => _mainWindow!.Title)}\"");
            DumpAndSave("step7-title-unchanged");
            return false;
        }

        if (!syncReady)
        {
            Console.WriteLine($"  [{Timestamp()}] ⚠ Sync status not 'Up to Date' within timeout — continuing anyway");
        }

        // Allow extra settle time for panels to fully render
        Thread.Sleep(2000);
        RefreshMainWindow();

        // Report window title (contains short team names)
        Console.WriteLine($"  Window title: \"{SafeGet(() => _mainWindow!.Title)}\"");

        // Report status bar info
        var statusBar = FindDescendant(_mainWindow!, cf.ByClassName(KnownElements.StatusBarClassName));
        if (statusBar != null)
        {
            var statusTexts = FindAllDescendants(statusBar, cf.ByControlType(ControlType.Text));
            Console.WriteLine($"  Status bar ({statusTexts.Length} text elements):");
            foreach (var t in statusTexts)
                Console.WriteLine($"    \"{SafeGet(() => t.Name)}\"");
        }

        // Report role verification
        var roleText = FindDescendant(_mainWindow!, cf.ByName("Read Only"));
        Console.WriteLine($"  Role 'Read Only': {(roleText != null ? "✓ confirmed" : "⚠ not found")}");

        // Dump for analysis
        DumpAndSave("step7-match-loaded");

        PrintPass("Match loaded — title changed");
        return PauseForUser();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // STEP 8: Find Team Name Elements
    // ═══════════════════════════════════════════════════════════════════════

    private bool Step8_FindTeamNameElements()
    {
        PrintStep(8, "Open Match Details/Teams dialog via Scoring menu");
        var cf = _automation.ConditionFactory;

        // Click Scoring menu
        var scoringMenu = FindDescendant(_mainWindow!, cf.ByAutomationId(KnownElements.ScoringMenuAutomationId));
        if (scoringMenu == null)
        {
            PrintFail("Scoring menu not found.");
            DumpAndSave("step8-no-scoring-menu");
            return false;
        }

        try
        {
            Console.WriteLine("  Clicking Scoring menu...");
            scoringMenu.Click();
            Thread.Sleep(200);

            // Find "Match Details/Teams..." within the expanded Scoring menu
            var matchDetailsItem = FindDescendant(scoringMenu,
                cf.ByName(KnownElements.MatchDetailsMenuItemName));
            if (matchDetailsItem == null)
            {
                // Dump the expanded menu to see what items are available
                Console.WriteLine("  ⚠ 'Match Details/Teams...' not found — dumping menu items...");
                var menuItems = FindAllDescendants(_mainWindow!, cf.ByControlType(ControlType.MenuItem));
                Console.WriteLine($"  Found {menuItems.Length} MenuItem(s):");
                foreach (var mi in menuItems)
                    PrintElement("    ", mi);

                DumpAndSave("step8-scoring-menu-items", maxDepth: 8);
                PrintWait("Review menu items and report the correct name for Match Details/Teams.");
                return false;
            }

            Console.WriteLine($"  Found: \"{SafeGet(() => matchDetailsItem.Name)}\"");
            matchDetailsItem.Click();
            Console.WriteLine("  ✓ 'Match Details/Teams...' clicked");
            Thread.Sleep(1000);

            // Look for the Match Details dialog
            RefreshMainWindow();
            var allChildWindows = FindAllDescendants(_mainWindow!, cf.ByControlType(ControlType.Window));
            Console.WriteLine($"  Found {allChildWindows.Length} child Window(s) after click:");
            foreach (var w in allChildWindows)
                PrintElement("    ", w);

            // Dump the dialog (or entire tree if no dialog found)
            if (allChildWindows.Length > 0)
            {
                foreach (var w in allChildWindows)
                {
                    var name = SafeGet(() => w.Name);
                    var dump = TreeDumper.Dump(w, maxDepth: 8);
                    Console.WriteLine($"\n  ── UI Tree: step8-dialog-{SafeName(name)} ──");
                    Console.WriteLine(dump);
                    Console.WriteLine($"  ── End ──");
                }
            }
            else
            {
                DumpAndSave("step8-no-dialog", maxDepth: 8);
            }

            // Also dump from top-level windows in case dialog is a separate window
            var topWindows = GetTopLevelWindows();
            if (topWindows.Length > 1)
            {
                Console.WriteLine($"  Found {topWindows.Length} top-level window(s):");
                foreach (var tw in topWindows)
                {
                    Console.WriteLine($"    \"{SafeGet(() => tw.Title)}\" ClassName=\"{SafeGet(() => tw.ClassName)}\"");
                    if (tw.Title != _mainWindow!.Title)
                    {
                        var dump = TreeDumper.Dump(tw, maxDepth: 8);
                        Console.WriteLine($"\n  ── UI Tree: step8-topwindow-{SafeName(tw.Title)} ──");
                        Console.WriteLine(dump);
                        Console.WriteLine($"  ── End ──");
                    }
                }
            }

            PrintWait("Review dialog dump and report Home Team / Away Team element identifiers + OK button.");
            return false;
        }
        catch (Exception ex)
        {
            PrintFail($"Error navigating to Match Details: {ex.Message}");
            DumpAndSave("step8-error");
            return false;
        }
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
        var allWindows = GetTopLevelWindows();

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
                Console.WriteLine($"\n  ── UI Tree: step9-scoreboard-window ──");
                Console.WriteLine(dump);
                Console.WriteLine($"  ── End ──");
            }
        }

        if (!found && KnownElements.ScoreboardWindowClassName == "TODO")
        {
            Console.WriteLine("  No known scoreboard ClassName — dumping all windows for analysis.");
            foreach (var w in allWindows)
            {
                var dump = TreeDumper.Dump(w, maxDepth: 4);
                Console.WriteLine($"\n  ── UI Tree: step9-window-{SafeName(w.Title)} ──");
                Console.WriteLine(dump);
                Console.WriteLine($"  ── End ──");
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
            Console.WriteLine($"\n  ── UI Tree: step10-change-match ──");
            Console.WriteLine(dump);
            Console.WriteLine($"  ── End ──");
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

    private Window[] GetTopLevelWindows()
    {
        try { return _app!.GetAllTopLevelWindows(_automation); }
        catch (System.Runtime.InteropServices.COMException) { return []; }
    }

    private void RefreshMainWindow()
    {
        var candidate = FindMainWindowCandidate(GetTopLevelWindows());
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

    private void DumpAndSave(string label, int maxDepth = 6)
    {
        RefreshMainWindow();
        var dump = TreeDumper.Dump(_mainWindow!, maxDepth: maxDepth);
        Console.WriteLine($"\n  ── UI Tree Dump: {label} ──");
        Console.WriteLine(dump);
        Console.WriteLine($"  ── End: {label} ──");
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

    private static string Timestamp() => DateTime.Now.ToString("HH:mm:ss.fff");

    private static bool PauseForUser()
    {
        Console.WriteLine();
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
        Console.WriteLine($"[{Timestamp()}] ═══ STEP {number}: {description} ═══");
        Console.ResetColor();
    }

    private static void PrintPass(string? note = null)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(note != null ? $"  [{Timestamp()}] ✓ PASS — {note}" : $"  [{Timestamp()}] ✓ PASS");
        Console.ResetColor();
    }

    private static void PrintFail(string reason)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  [{Timestamp()}] ✗ FAIL — {reason}");
        Console.ResetColor();
    }

    private static void PrintWait(string message)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"  [{Timestamp()}] ⏸ DISCOVERY NEEDED — {message}");
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
