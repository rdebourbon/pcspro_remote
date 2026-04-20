using System.Diagnostics;
using System.Drawing.Imaging;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
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
    private const int LoginTransitionTimeoutSeconds = 30;
    private const int SearchTimeoutSeconds = 20;
    private const int MatchLoadTimeoutSeconds = 30;

    private readonly string _password;
    private readonly string _expectedUsername;
    private readonly string? _executablePath;
    private readonly string _siteName;
    private readonly string _searchDate;
    private readonly string _outputDirectory;
    private readonly bool _captureUiTree;
    private readonly UIA3Automation _automation;
    private Application? _app;
    private Window? _mainWindow;
    private string? _titleBeforeMatchOpen;

    public DiagnosticRunner(string password, string expectedUsername, string? executablePath,
        string siteName, string searchDate, string outputDirectory, bool captureUiTree = false)
    {
        _password = password;
        _expectedUsername = expectedUsername;
        _executablePath = executablePath;
        _siteName = siteName;
        _searchDate = searchDate;
        _outputDirectory = outputDirectory;
        _captureUiTree = captureUiTree;
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

    /// <summary>
    /// Spinner discovery mode. Runs Steps 0-4 to reach the Open Match dialog,
    /// then performs filter actions with UI tree dumps before, during, and after
    /// each action to identify the loading spinner element.
    /// </summary>
    public void RunSpinnerDiscovery()
    {
        var logPath = Path.Combine(_outputDirectory,
            $"pcs-spinner-discovery-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var originalOut = Console.Out;
        using var fileWriter = new StreamWriter(logPath, false, System.Text.Encoding.UTF8)
        {
            AutoFlush = true
        };
        using var dual = new DualWriter(originalOut, fileWriter);
        Console.SetOut(dual);

        try
        {
            RunSpinnerDiscoverySteps(logPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"  💥 UNHANDLED EXCEPTION: {ex.GetType().FullName}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.WriteLine($"\n  Spinner discovery log saved to: {logPath}");
        }
    }

    private void RunSpinnerDiscoverySteps(string logPath)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  🔍 SPINNER DISCOVERY MODE                                  ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine($"  Log: {logPath}");
        Console.WriteLine($"  Config: site=\"{_siteName}\" date=\"{_searchDate}\"");
        Console.WriteLine();

        // Reuse Steps 0-4 to reach the Open Match dialog
        if (!Step0_AttachToProcess()) return;
        if (!Step1_WaitForMainWindow()) return;
        if (!Step2_FindLoginElements()) return;
        if (!Step3_SubmitCredentials()) return;
        if (!Step4_WaitForMatchSelection()) return;

        var cf = _automation.ConditionFactory;
        RefreshMainWindow();

        // Find the Open Match dialog
        var dialog = FindDescendant(_mainWindow!,
            cf.ByName(KnownElements.MatchSelectionDialogName));
        if (dialog == null)
        {
            Console.WriteLine("  ❌ Open Match dialog not found — cannot proceed.");
            return;
        }

        Console.WriteLine("\n══ PHASE 1: BASELINE (before any filter change) ══");
        DumpDialogWithIsOffscreen(dialog, cf, "BASELINE");

        // Find site combo
        var siteComboBoxes = FindAllDescendants(dialog, cf.ByControlType(ControlType.ComboBox));
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
            Console.WriteLine("  ❌ Site ComboBox not found.");
            return;
        }

        // Select site — dump tree IMMEDIATELY after click (before it settles)
        Console.WriteLine($"\n══ PHASE 2: SELECT SITE '{_siteName}' ══");
        Console.WriteLine($"  [{Timestamp()}] Clicking site dropdown...");
        siteCombo.Click();
        Thread.Sleep(500);

        var siteItem = FindDescendant(_mainWindow!, cf.ByName(_siteName));
        if (siteItem == null)
        {
            Console.WriteLine($"  ❌ Site '{_siteName}' not found in dropdown.");
            return;
        }

        Console.WriteLine($"  [{Timestamp()}] Clicking site item '{_siteName}'...");
        siteItem.Click();

        // Dump IMMEDIATELY — spinner should be visible now
        Console.WriteLine($"  [{Timestamp()}] Dumping tree 100ms after site selection...");
        Thread.Sleep(100);
        DumpDialogWithIsOffscreen(dialog, cf, "SITE_SELECTED_100ms");

        // Dump again at 500ms
        Thread.Sleep(400);
        Console.WriteLine($"  [{Timestamp()}] Dumping tree 500ms after site selection...");
        DumpDialogWithIsOffscreen(dialog, cf, "SITE_SELECTED_500ms");

        // Dump again at 2s
        Thread.Sleep(1500);
        Console.WriteLine($"  [{Timestamp()}] Dumping tree 2s after site selection...");
        DumpDialogWithIsOffscreen(dialog, cf, "SITE_SELECTED_2000ms");

        // Wait for it to settle and dump once more
        Thread.Sleep(3000);
        Console.WriteLine($"  [{Timestamp()}] Dumping tree 5s after site selection (should be settled)...");
        DumpDialogWithIsOffscreen(dialog, cf, "SITE_SELECTED_5000ms");

        // Now set Date From
        Console.WriteLine($"\n══ PHASE 3: SET DATE FROM '{_searchDate}' ══");
        var datePickers = FindAllDescendants(dialog, cf.ByClassName("DatePicker"));
        if (datePickers.Length < 2)
        {
            Console.WriteLine("  ❌ Could not find 2 DatePickers.");
            return;
        }

        var dateFromTextBox = FindDescendant(datePickers[0], cf.ByAutomationId("PART_TextBox"));
        if (dateFromTextBox == null)
        {
            Console.WriteLine("  ❌ Date From TextBox not found.");
            return;
        }

        Console.WriteLine($"  [{Timestamp()}] Clicking Date From and typing '{_searchDate}'...");
        dateFromTextBox.Click();
        Thread.Sleep(200);
        FlaUI.Core.Input.Keyboard.TypeSimultaneously(
            FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL,
            FlaUI.Core.WindowsAPI.VirtualKeyShort.KEY_A);
        FlaUI.Core.Input.Keyboard.Type(_searchDate);
        Thread.Sleep(200);

        Console.WriteLine($"  [{Timestamp()}] Pressing Tab to commit Date From...");
        FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.TAB);

        // Dump IMMEDIATELY after Tab — spinner should appear
        Thread.Sleep(100);
        Console.WriteLine($"  [{Timestamp()}] Dumping tree 100ms after Date From Tab...");
        DumpDialogWithIsOffscreen(dialog, cf, "DATE_FROM_TAB_100ms");

        Thread.Sleep(400);
        Console.WriteLine($"  [{Timestamp()}] Dumping tree 500ms after Date From Tab...");
        DumpDialogWithIsOffscreen(dialog, cf, "DATE_FROM_TAB_500ms");

        Thread.Sleep(1500);
        Console.WriteLine($"  [{Timestamp()}] Dumping tree 2s after Date From Tab...");
        DumpDialogWithIsOffscreen(dialog, cf, "DATE_FROM_TAB_2000ms");

        Thread.Sleep(3000);
        Console.WriteLine($"  [{Timestamp()}] Dumping tree 5s after Date From Tab (should be settled)...");
        DumpDialogWithIsOffscreen(dialog, cf, "DATE_FROM_TAB_5000ms");

        Console.WriteLine("\n══ SPINNER DISCOVERY COMPLETE ══");
        Console.WriteLine("Review the dumps above. Look for elements whose IsOffscreen");
        Console.WriteLine("property changes between the _100ms and _5000ms dumps.");
    }

    /// <summary>
    /// Dumps all descendants of the dialog, including IsOffscreen property,
    /// to help discover the spinner element.
    /// </summary>
    private static void DumpDialogWithIsOffscreen(
        AutomationElement dialog, ConditionFactory cf, string label)
    {
        Console.WriteLine($"\n  ── DUMP: {label} ──");

        void DumpElement(AutomationElement el, int depth)
        {
            if (depth > 6) return;

            AutomationElement[] children;
            try { children = el.FindAllChildren(); }
            catch { return; }

            var indent = new string(' ', (depth + 1) * 2);
            foreach (var child in children)
            {
                var automationId = SafeGet(() => child.AutomationId);
                var name = SafeGet(() => child.Name);
                var className = SafeGet(() => child.ClassName);
                var controlType = SafeGet(() => child.ControlType.ToString());
                bool isOffscreen = false;
                try { isOffscreen = child.Properties.IsOffscreen.ValueOrDefault; }
                catch { /* ignore */ }

                var hasId = !string.IsNullOrWhiteSpace(automationId);
                var hasName = !string.IsNullOrWhiteSpace(name);
                var marker = hasId ? "★" : "·";
                var offscreenFlag = isOffscreen ? " [OFFSCREEN]" : "";

                Console.Write($"{indent}{marker} [{controlType}]");
                if (hasId) Console.Write($"  AutomationId=\"{automationId}\"");
                if (hasName) Console.Write($"  Name=\"{(name!.Length > 60 ? name[..60] + "…" : name)}\"");
                Console.Write($"  ClassName=\"{className}\"");
                Console.Write($"  IsOffscreen={isOffscreen}{offscreenFlag}");
                Console.WriteLine();

                DumpElement(child, depth + 1);
            }
        }

        DumpElement(dialog, 0);
        Console.WriteLine($"  ── END: {label} ──");
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

        // Step 10: Change match (select 2nd match)
        if (!Step10_FindChangeMatchElement()) return;

        // Step 11: Start/Stop Live Stream
        if (!Step11_StartStopLiveStream()) return;

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
                DumpIfEnabled($"step1-window-{SafeName(w.Title)}", w, maxDepth: 3);
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
            DumpIfEnabled("step2-no-login-dialog", _mainWindow!, maxDepth: 4);
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
            DumpIfEnabled("step2-missing-elements", _mainWindow!, maxDepth: 6);
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

            // Wait for login to complete — poll until dialog disappears or error appears
            Console.WriteLine("  Waiting for login response...");
            Thread.Sleep(1000);

            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < 30)
            {
                var loginDialog = FindDescendant(_mainWindow!, cf.ByAutomationId(KnownElements.LoginDialogAutomationId));
                if (loginDialog == null)
                {
                    Console.WriteLine($"  ✓ Login dialog closed [{sw.Elapsed:mm\\:ss}]");
                    PrintPass();
                    return PauseForUser();
                }

                // Check for error text while dialog is still visible
                var allText = FindAllDescendants(loginDialog, cf.ByControlType(ControlType.Text));
                var errorText = allText.FirstOrDefault(t =>
                    SafeGet(() => t.Name)?.Contains("incorrect", StringComparison.OrdinalIgnoreCase) == true ||
                    SafeGet(() => t.Name)?.Contains("error", StringComparison.OrdinalIgnoreCase) == true);

                if (errorText != null)
                {
                    PrintFail($"Login failed — PCS Pro says: \"{SafeGet(() => errorText.Name)}\"");
                    return false;
                }

                Thread.Sleep(PollIntervalMs);
            }

            PrintFail("Login dialog did not close within 30s.");
            return false;
        }
        catch (Exception ex)
        {
            PrintFail($"Interaction error: {ex.Message}");
            DumpIfEnabled("step3-login-interact", _mainWindow!, maxDepth: 4);
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
            AutomationElement? fileMenu = null;
            var menuSw = Stopwatch.StartNew();
            while (menuSw.Elapsed.TotalSeconds < 10)
            {
                fileMenu = FindDescendant(_mainWindow!, cf.ByAutomationId(KnownElements.FileMenuAutomationId));
                if (fileMenu != null) break;
                Thread.Sleep(PollIntervalMs);
            }

            if (fileMenu == null)
            {
                PrintFail("File menu not found");
                DumpAndSave("step4-no-file-menu");
                return false;
            }

            Console.WriteLine("  File menu clicked — looking for Open Match...");
            var openMatch = ClickMenuItem(fileMenu, KnownElements.OpenMatchMenuItemAutomationId, cf);

            if (openMatch == null)
            {
                Console.WriteLine("  ⚠ 'Open Match...' not found — dumping menu tree for discovery.");
                var dump = TreeDumper.Dump(fileMenu, maxDepth: 4);
                Console.WriteLine($"\n  ── UI Tree: step4-file-menu-expanded ──");
                Console.WriteLine(dump);
                Console.WriteLine($"  ── End ──");
                PrintWait("Review the file menu dump — look for the Open Match menu item name.");
                return false;
            }

            PrintElement("  Found: ", openMatch);
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

            // Poll for the Open Match dialog — search child Windows only (fast)
            // rather than deep FindDescendant by Name (slow on WPF trees)
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < LoginTransitionTimeoutSeconds)
            {
                var childWindows = FindAllDescendants(_mainWindow!,
                    cf.ByControlType(ControlType.Window));
                foreach (var w in childWindows)
                {
                    var name = SafeGet(() => w.Name);
                    if (name == KnownElements.MatchSelectionDialogName)
                    {
                        Console.WriteLine($"  ✓ Open Match dialog detected");
                        PrintPass();
                        return PauseForUser();
                    }
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
            Thread.Sleep(500);
            Console.WriteLine($"  ✓ Site set to '{_siteName}'");

            // Wait for spinner to clear after site selection (it triggers async search)
            Console.WriteLine("  Waiting for search spinner to clear after site selection...");
            if (!WaitForSpinnerIdle(dialog, cf))
            {
                PrintFail("Search spinner did not clear after site selection.");
                DumpAndSave("step5-spinner-timeout-site");
                return false;
            }

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

                // Tab out to commit the value and trigger search
                FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.TAB);
                Thread.Sleep(200);

                Console.WriteLine($"  ✓ {label} set to '{_searchDate}'");

                // Wait for spinner to clear after each date change
                Console.WriteLine($"  Waiting for search spinner to clear after {label}...");
                if (!WaitForSpinnerIdle(dialog, cf))
                {
                    PrintFail($"Search spinner did not clear after setting {label}.");
                    DumpAndSave($"step5-spinner-timeout-{label.ToLower().Replace(' ', '-')}");
                    return false;
                }
            }

            // 4. Spinner cleared after last date entry — read results
            Console.WriteLine("  Spinner cleared — reading grid results...");

            var grid = FindDescendant(dialog,
                cf.ByAutomationId(KnownElements.MatchDataGridAutomationId));
            if (grid != null)
            {
                var rows = FindAllDescendants(grid, cf.ByControlType(ControlType.DataItem));
                Console.WriteLine($"  ✓ DataGrid has {rows.Length} row(s)");
                foreach (var row in rows.Take(5))
                    Console.WriteLine($"    Row: \"{SafeGet(() => row.Name)}\"");

                if (rows.Length > 0)
                {
                    PrintPass();
                    return PauseForUser();
                }
            }

            PrintFail("Grid is empty after search completed — no matches found for the date.");
            DumpAndSave("step5-search-empty", maxDepth: 8);
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
            Console.WriteLine($"  [{Timestamp()}] Selecting row: \"{SafeGet(() => firstRow.Name)}\"");

            // Use SelectionItemPattern for reliable WPF DataGrid selection
            if (!SelectDataGridRow(firstRow))
            {
                PrintFail("Could not select row via SelectionItemPattern or Click.");
                DumpAndSave("step6-select-failed");
                return false;
            }
            Console.WriteLine($"  [{Timestamp()}] ✓ Row selected");
            Thread.Sleep(500);

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
            DumpIfEnabled("step6-selected-row", firstRow, maxDepth: 4);

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
                Console.WriteLine($"  [{Timestamp()}] IsEnabled={openReadOnly.Properties.IsEnabled.ValueOrDefault}");

                var invokeError = InvokeButtonSafely(openReadOnly);
                if (invokeError != null)
                {
                    Console.WriteLine($"  ⚠ InvokeButtonSafely failed: {invokeError}");
                    Console.WriteLine("  Falling back to double-click on row...");
                    firstRow.DoubleClick();
                    Console.WriteLine("  ✓ Double-clicked first row as fallback");
                }
                else
                {
                    Console.WriteLine($"  [{Timestamp()}] ✓ 'Open Read Only' invoked");
                }
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

            // Check sync status once dialog is gone — don't require title change first,
            // because loading the same match type produces an identical abbreviated title.
            if (dialogGone && !syncReady)
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

            if (dialogGone && syncReady)
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

        if (!syncReady)
        {
            PrintFail("Sync status never reached 'Up to Date' within timeout.");
            DumpAndSave("step7-sync-timeout");
            return false;
        }

        if (!titleChanged)
        {
            Console.WriteLine($"  [{Timestamp()}] ⚠ Title unchanged (same match type) — sync confirmed load");
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

        // Dump for analysis (reduced depth — full tree dump is expensive)
        if (_captureUiTree)
            DumpAndSave("step7-match-loaded", maxDepth: 3);

        PrintPass(titleChanged ? "Match loaded — title changed" : "Match loaded — sync confirmed");
        return PauseForUser();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // STEP 8: Find Team Name Elements
    // ═══════════════════════════════════════════════════════════════════════

    private bool Step8_FindTeamNameElements()
    {
        PrintStep(8, "Extract team names from Match Details/Teams dialog");
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
            Console.WriteLine("  Clicking Scoring menu → Match Details/Teams...");
            var matchDetailsItem = ClickMenuItemByName(
                scoringMenu, KnownElements.MatchDetailsMenuItemName, cf);

            if (matchDetailsItem == null)
            {
                Console.WriteLine("  ⚠ 'Match Details/Teams...' not found — dumping menu items...");
                scoringMenu.Click();
                Thread.Sleep(300);
                var menuItems = FindAllDescendants(_mainWindow!, cf.ByControlType(ControlType.MenuItem));
                Console.WriteLine($"  Found {menuItems.Length} MenuItem(s):");
                foreach (var mi in menuItems)
                    PrintElement("    ", mi);

                DumpAndSave("step8-scoring-menu-items", maxDepth: 8);
                PrintFail("Match Details/Teams menu item not found.");
                return false;
            }

            Console.WriteLine($"  ✓ Found and clicked: \"{SafeGet(() => matchDetailsItem.Name)}\"");
            Thread.Sleep(1000);

            // Find the Match Details dialog
            RefreshMainWindow();
            var dialog = WaitForElement(_mainWindow!,
                cf.ByName(KnownElements.MatchDetailsDialogName), 5000);

            if (dialog == null)
            {
                PrintFail("Match Details/Teams dialog did not appear.");
                DumpAndSave("step8-no-dialog", maxDepth: 8);
                return false;
            }

            Console.WriteLine("  ✓ Match Details/Teams dialog found");

            // Find both MatchTeamView elements
            var teamViews = FindAllDescendants(dialog,
                cf.ByClassName(KnownElements.MatchTeamViewClassName));

            Console.WriteLine($"  Found {teamViews.Length} MatchTeamView(s)");

            if (teamViews.Length < 2)
            {
                PrintFail($"Expected 2 MatchTeamViews, found {teamViews.Length}.");
                DumpAndSave("step8-missing-teamviews", maxDepth: 8);
                CloseMatchDetailsDialog(dialog, cf);
                return false;
            }

            // Extract club and team from each MatchTeamView
            string[] labels = ["Team 1 (grid order)", "Team 2 (grid order)"];
            for (int i = 0; i < 2; i++)
            {
                var teamView = teamViews[i];
                var clubCombo = FindDescendant(teamView,
                    cf.ByAutomationId(KnownElements.ClubComboBoxAutomationId));
                var teamCombo = FindDescendant(teamView,
                    cf.ByAutomationId(KnownElements.TeamComboBoxAutomationId));

                var clubValue = ReadComboBoxValue(clubCombo);
                var teamValue = ReadComboBoxValue(teamCombo);

                Console.WriteLine($"  {labels[i]}:");
                Console.WriteLine($"    Club: \"{clubValue}\"");
                Console.WriteLine($"    Team: \"{teamValue}\"");
            }

            // Close the dialog
            CloseMatchDetailsDialog(dialog, cf);

            PrintPass("Team data extracted and dialog closed");
            return PauseForUser();
        }
        catch (Exception ex)
        {
            PrintFail($"Error in Match Details: {ex.Message}");
            DumpAndSave("step8-error");
            return false;
        }
    }

    private void CloseMatchDetailsDialog(AutomationElement dialog, ConditionFactory cf)
    {
        var okButton = FindDescendant(dialog,
            cf.ByAutomationId(KnownElements.MatchDetailsOkButtonAutomationId));

        if (okButton != null)
        {
            Console.WriteLine("  Clicking OK to close Match Details dialog...");
            var err = InvokeButtonSafely(okButton);
            if (err != null)
                Console.WriteLine($"  ⚠ OK button invoke issue: {err}");
            else
                Console.WriteLine("  ✓ OK clicked — dialog closing");

            Thread.Sleep(500);
        }
        else
        {
            Console.WriteLine("  ⚠ OK button not found — dialog may remain open");
        }
    }

    private static string ReadComboBoxValue(AutomationElement? comboBox)
    {
        if (comboBox == null) return "<not found>";

        // Try Value pattern first (most reliable for WPF ComboBox selected text)
        try
        {
            if (comboBox.Patterns.Value.IsSupported)
            {
                var val = comboBox.Patterns.Value.Pattern.Value.Value;
                if (!string.IsNullOrEmpty(val)) return val;
            }
        }
        catch { /* fall through */ }

        // Try reading the Name property
        var name = SafeGet(() => comboBox.Name);
        if (!string.IsNullOrWhiteSpace(name) && name != "<error>") return name;

        // Try SelectedItem from the ComboBox wrapper
        try
        {
            var combo = comboBox.AsComboBox();
            var selected = combo.SelectedItem;
            if (selected != null)
            {
                var selectedName = SafeGet(() => selected.Name);
                if (!string.IsNullOrWhiteSpace(selectedName)) return selectedName;
            }
        }
        catch { /* fall through */ }

        return "<could not read value>";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // STEP 9: Find Scoreboard Elements
    // ═══════════════════════════════════════════════════════════════════════

    private bool Step9_FindScoreboardElements()
    {
        PrintStep(9, "Find Main Scoreboard and trigger Refresh All Scoreboards");
        var cf = _automation.ConditionFactory;
        RefreshMainWindow();

        // Search for the "Main Scoreboard" tool window among all ToolWindow panes
        Console.WriteLine("  Searching for Main Scoreboard tool window...");
        var allToolWindows = FindAllDescendants(_mainWindow!,
            cf.ByClassName("ToolWindow"));

        AutomationElement? scoreboardPane = null;
        Console.WriteLine($"  Found {allToolWindows.Length} ToolWindow pane(s):");
        foreach (var tw in allToolWindows)
        {
            var twName = SafeGet(() => tw.Name);
            var twAutoId = SafeGet(() => tw.AutomationId);
            Console.WriteLine($"    Name=\"{twName}\" AutomationId=\"{twAutoId}\"");

            if (twName.Contains("Scoreboard", StringComparison.OrdinalIgnoreCase) &&
                twName.Contains("Main", StringComparison.OrdinalIgnoreCase))
            {
                scoreboardPane = tw;
                Console.WriteLine("    ↑ ✓ MATCHED as Main Scoreboard");
            }
        }

        if (scoreboardPane == null)
        {
            // Broader search — any tool window with "Scoreboard" in the name
            foreach (var tw in allToolWindows)
            {
                var twName = SafeGet(() => tw.Name);
                if (twName.Contains("Scoreboard", StringComparison.OrdinalIgnoreCase))
                {
                    scoreboardPane = tw;
                    Console.WriteLine($"  ✓ Fallback match: \"{twName}\"");
                    break;
                }
            }
        }

        if (scoreboardPane == null)
        {
            Console.WriteLine("  ⚠ No scoreboard ToolWindow found — dumping all ToolWindows for analysis...");
            foreach (var tw in allToolWindows)
            {
                var dump = TreeDumper.Dump(tw, maxDepth: 4);
                Console.WriteLine($"\n  ── ToolWindow: \"{SafeGet(() => tw.Name)}\" ──");
                Console.WriteLine(dump);
                Console.WriteLine("  ── End ──");
            }

            PrintFail("Main Scoreboard tool window not found.");
            return false;
        }

        Console.WriteLine($"  ✓ Main Scoreboard found: \"{SafeGet(() => scoreboardPane.Name)}\"");

        // Navigate up to the parent ToolWindowContainer to find the title bar
        var container = WalkUpToClassName(scoreboardPane, "ToolWindowContainer");
        if (container == null)
        {
            Console.WriteLine("  ⚠ Could not find parent ToolWindowContainer — searching from pane...");
            container = scoreboardPane;
        }
        else
        {
            Console.WriteLine($"  ✓ Parent ToolWindowContainer: AutomationId=\"{SafeGet(() => container.AutomationId)}\"");
        }

        // Find the PART_TitleBar in the container
        var titleBar = FindDescendant(container, cf.ByAutomationId("PART_TitleBar"));
        if (titleBar == null)
        {
            Console.WriteLine("  ⚠ PART_TitleBar not found in container — searching broader...");
            titleBar = FindDescendant(container, cf.ByClassName("TitleBarPanel"));
        }

        if (titleBar == null)
        {
            Console.WriteLine("  ⚠ No title bar found — dumping container for analysis...");
            var dump = TreeDumper.Dump(container, maxDepth: 5);
            Console.WriteLine(dump);
            PrintFail("Title bar not found in scoreboard container.");
            return false;
        }

        Console.WriteLine("  ✓ Title bar found");

        // Ensure the Main Scoreboard tab is active/selected before interacting
        // with its title bar. The ToolWindowContainer might have multiple tabs.
        Console.WriteLine("  Ensuring Main Scoreboard tab is active...");
        ActivateToolWindow(scoreboardPane);
        Thread.Sleep(300);

        // Find the Settings PopupButton in the title bar
        var titleBarButtons = FindAllDescendants(titleBar, cf.ByControlType(ControlType.Button));
        Console.WriteLine($"  Found {titleBarButtons.Length} button(s) in title bar:");
        foreach (var btn in titleBarButtons)
            PrintElement("    ", btn);

        // Match by ClassName="PopupButton" and HelpText="Settings" (confirmed from log)
        AutomationElement? settingsButton = null;
        foreach (var btn in titleBarButtons)
        {
            var className = SafeGet(() => btn.ClassName);
            var helpText = SafeGet(() => btn.HelpText);
            if (className == "PopupButton" &&
                helpText.Contains("Settings", StringComparison.OrdinalIgnoreCase))
            {
                settingsButton = btn;
                Console.WriteLine($"  ✓ Settings PopupButton identified: HelpText=\"{helpText}\"");
                break;
            }
        }

        if (settingsButton == null)
        {
            Console.WriteLine("  ⚠ No Settings PopupButton found — dumping title bar tree...");
            var dump = TreeDumper.Dump(titleBar, maxDepth: 4);
            Console.WriteLine(dump);
            PrintFail("Settings PopupButton not found in scoreboard title bar.");
            return false;
        }

        // Try multiple click strategies — PopupButton may not respond to simple Click()
        Console.WriteLine("  Opening Settings popup menu...");
        var popupOpened = TryOpenPopupButton(settingsButton, cf);

        if (!popupOpened)
        {
            Console.WriteLine("  ⚠ Popup menu did not appear after all click strategies.");
            // Dump what we can see for diagnosis
            var titleDump = TreeDumper.Dump(titleBar, maxDepth: 5);
            Console.WriteLine($"\n  ── Title bar after click attempts ──");
            Console.WriteLine(titleDump);
            Console.WriteLine("  ── End ──");
            PrintFail("Could not open Settings popup menu.");
            return false;
        }

        // Search for "Refresh all Scoreboards" — WPF popups may appear at desktop level
        Console.WriteLine("  Searching for Refresh all Scoreboards...");
        var refreshItem = FindPopupMenuItem(
            KnownElements.RefreshAllScoreboardsMenuItemName, cf);

        if (refreshItem == null)
        {
            // Dump what's visible for discovery
            Console.WriteLine("  ⚠ 'Refresh all Scoreboards' not found — dumping popup discovery...");
            DumpPopupDiscovery(cf);
            PrintFail("Refresh all Scoreboards menu item not found in popup.");
            return false;
        }

        Console.WriteLine($"  ✓ Found: \"{SafeGet(() => refreshItem.Name)}\"");
        Console.WriteLine("  Clicking Refresh all Scoreboards...");
        refreshItem.Click();
        Thread.Sleep(1000);

        // Dump the scoreboard pane contents for screen-grab analysis
        Console.WriteLine("\n  Dumping Main Scoreboard contents for screen-grab analysis...");
        RefreshMainWindow();

        // Re-find the scoreboard pane (reference may be stale after refresh)
        var freshScoreboard = FindDescendant(_mainWindow!,
            cf.ByAutomationId("twdReplayScreen"));

        if (freshScoreboard != null)
        {
            if (_captureUiTree)
            {
                var dump2 = TreeDumper.Dump(freshScoreboard, maxDepth: 6);
                Console.WriteLine($"\n  ── UI Tree: step9-scoreboard-contents ──");
                Console.WriteLine(dump2);
                Console.WriteLine($"  ── End ──");
            }
        }
        else
        {
            DumpAndSave("step9-scoreboard-after-refresh", maxDepth: 6);
        }

        // Capture a screenshot of the scoreboard content (ReplayScreenPreview),
        // not the ToolWindow pane which includes title bar chrome.
        // Re-activate the tab — the Refresh click may have shifted focus.
        Console.WriteLine("\n  Capturing scoreboard screenshot...");
        var captureTarget = freshScoreboard ?? scoreboardPane;
        ActivateToolWindow(captureTarget);
        Thread.Sleep(2000);

        var previewElement = FindDescendant(captureTarget,
            cf.ByAutomationId("ReplayScreenPreview"));

        if (previewElement != null)
        {
            Console.WriteLine($"  ✓ ReplayScreenPreview found — using as capture target");
            PrintElement("    ", previewElement);

            // Dump the preview element subtree to discover any inner canvas/image controls
            DumpIfEnabled("ReplayScreenPreview internals", previewElement, maxDepth: 8);

            CaptureScoreboardImage(previewElement);
        }
        else
        {
            Console.WriteLine("  ⚠ ReplayScreenPreview not found — falling back to ToolWindow pane");
            CaptureScoreboardImage(captureTarget);
        }

        PrintPass("Scoreboard found, refreshed, and captured");
        return PauseForUser();
    }

    /// <summary>
    /// Captures a screenshot of the scoreboard element and saves it as a PNG
    /// to the output directory. This validates the screen-grab approach that
    /// will be used in the production application.
    /// Handles DPI scaling by comparing UIA coordinates with screen DPI.
    /// </summary>
    private void CaptureScoreboardImage(AutomationElement scoreboardElement)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var fileName = $"scoreboard-capture-{timestamp}.png";
            var filePath = Path.Combine(_outputDirectory, fileName);

            var bounds = scoreboardElement.BoundingRectangle;
            Console.WriteLine($"    UIA BoundingRect: {bounds.Width}x{bounds.Height} at ({bounds.X},{bounds.Y})");

            // Process is DPI-aware (SetProcessDPIAware called at startup) so UIA
            // coordinates and CopyFromScreen both use physical pixels consistently.
            var image = Capture.Rectangle(bounds);
            image.ToFile(filePath);

            var fileInfo = new FileInfo(filePath);
            Console.WriteLine($"  ✓ Scoreboard screenshot saved: {fileName} ({fileInfo.Length / 1024}KB)");
            Console.WriteLine($"    Path: {filePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ⚠ Screenshot capture failed: {ex.Message}");
            Console.WriteLine($"    This may occur if the element is off-screen or not visible.");
        }
    }

    /// <summary>
    /// Walks up the automation tree from <paramref name="element"/> until an
    /// ancestor with the given <paramref name="className"/> is found.
    /// Returns null if the root is reached without a match.
    /// </summary>
    private static AutomationElement? WalkUpToClassName(AutomationElement element, string className)
    {
        try
        {
            var walker = element.Automation.TreeWalkerFactory.GetRawViewWalker();
            var current = walker.GetParent(element);
            int maxSteps = 10;
            while (current != null && maxSteps-- > 0)
            {
                try
                {
                    if (current.ClassName == className) return current;
                }
                catch { /* some parents may throw */ }

                current = walker.GetParent(current);
            }
        }
        catch { /* tree walker unavailable */ }
        return null;
    }

    /// <summary>
    /// Attempts to activate a ToolWindow pane so its title bar buttons become
    /// interactive. Tries SelectionItemPattern first, then Focus, then Click.
    /// </summary>
    /// <summary>
    /// Activates a ToolWindow tab by finding its tab header in the parent
    /// ToolWindowContainer and clicking it. Falls back to Focus/Click on
    /// the pane itself if the tab header cannot be found.
    /// </summary>
    private void ActivateToolWindow(AutomationElement toolWindow)
    {
        var cf = _automation.ConditionFactory;
        var toolWindowName = SafeGet(() => toolWindow.Name);

        // Strategy 1: Find the tab header in the parent ToolWindowContainer
        // and click it to switch tabs visually.
        try
        {
            var container = WalkUpToClassName(toolWindow, "ToolWindowContainer");
            if (container != null)
            {
                // Look for TabItem elements in the container's tab strip
                var tabItems = FindAllDescendants(container, cf.ByControlType(ControlType.TabItem));
                foreach (var tab in tabItems)
                {
                    var tabName = SafeGet(() => tab.Name);
                    if (tabName.Contains(toolWindowName, StringComparison.OrdinalIgnoreCase))
                    {
                        tab.Click();
                        Console.WriteLine($"    ✓ Clicked tab header: \"{tabName}\"");
                        return;
                    }
                }

                // No TabItem — look for a clickable header with matching text
                var headers = FindAllDescendants(container, cf.ByControlType(ControlType.Header));
                foreach (var header in headers)
                {
                    var headerName = SafeGet(() => header.Name);
                    if (headerName.Contains(toolWindowName, StringComparison.OrdinalIgnoreCase))
                    {
                        header.Click();
                        Console.WriteLine($"    ✓ Clicked header: \"{headerName}\"");
                        return;
                    }
                }

                Console.WriteLine($"    ⚠ No matching tab header found for \"{toolWindowName}\"");
            }
        }
        catch { /* fall through to other strategies */ }

        // Strategy 2: SelectionItemPattern (TabItem-like selection)
        try
        {
            if (toolWindow.Patterns.SelectionItem.IsSupported)
            {
                toolWindow.Patterns.SelectionItem.Pattern.Select();
                Console.WriteLine("    ✓ Activated via SelectionItemPattern");
                return;
            }
        }
        catch { /* fall through */ }

        // Strategy 3: Focus
        try
        {
            toolWindow.Focus();
            Console.WriteLine("    ✓ Focused");
        }
        catch { /* focus may fail */ }

        // Strategy 4: Click as last resort
        try
        {
            toolWindow.Click();
            Console.WriteLine("    ✓ Clicked to activate");
        }
        catch { /* click may fail */ }
    }

    /// <summary>
    /// Tries multiple strategies to open an ActiproSoftware PopupButton:
    ///   1. ExpandCollapsePattern.Expand()
    ///   2. InvokePattern.Invoke()
    ///   3. Focus + Click at center
    ///   4. Mouse.Click at bounding rect center
    /// Returns true if any strategy caused a popup to appear.
    /// </summary>
    private bool TryOpenPopupButton(AutomationElement button, ConditionFactory cf)
    {
        // Strategy 1: ExpandCollapse pattern
        try
        {
            if (button.Patterns.ExpandCollapse.IsSupported)
            {
                Console.WriteLine("    Strategy 1: ExpandCollapsePattern.Expand()...");
                button.Patterns.ExpandCollapse.Pattern.Expand();
                Thread.Sleep(500);
                if (IsPopupVisible(cf))
                {
                    Console.WriteLine("    ✓ Popup appeared via ExpandCollapse");
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    Strategy 1 failed: {ex.Message}");
        }

        // Strategy 2: Invoke pattern
        try
        {
            if (button.Patterns.Invoke.IsSupported)
            {
                Console.WriteLine("    Strategy 2: InvokePattern.Invoke()...");
                button.Patterns.Invoke.Pattern.Invoke();
                Thread.Sleep(500);
                if (IsPopupVisible(cf))
                {
                    Console.WriteLine("    ✓ Popup appeared via Invoke");
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    Strategy 2 failed: {ex.Message}");
        }

        // Strategy 3: Focus then Click
        try
        {
            Console.WriteLine("    Strategy 3: Focus + Click...");
            button.Focus();
            Thread.Sleep(100);
            button.Click();
            Thread.Sleep(500);
            if (IsPopupVisible(cf))
            {
                Console.WriteLine("    ✓ Popup appeared via Focus+Click");
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    Strategy 3 failed: {ex.Message}");
        }

        // Strategy 4: Direct mouse click at bounding rect center
        try
        {
            Console.WriteLine("    Strategy 4: Mouse.Click at bounding rect center...");
            var rect = button.BoundingRectangle;
            var centerX = (int)(rect.X + rect.Width / 2);
            var centerY = (int)(rect.Y + rect.Height / 2);
            Console.WriteLine($"    Clicking at ({centerX}, {centerY})...");
            FlaUI.Core.Input.Mouse.Click(new System.Drawing.Point(centerX, centerY));
            Thread.Sleep(500);
            if (IsPopupVisible(cf))
            {
                Console.WriteLine("    ✓ Popup appeared via Mouse.Click");
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    Strategy 4 failed: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Checks whether a popup/context menu has appeared anywhere in the
    /// automation tree — searches both the main window and the Desktop.
    /// </summary>
    private bool IsPopupVisible(ConditionFactory cf)
    {
        // Check main window for new Menu/ContextMenu elements
        RefreshMainWindow();
        var menuInWindow = FindDescendant(_mainWindow!,
            cf.ByName(KnownElements.RefreshAllScoreboardsMenuItemName));
        if (menuInWindow != null) return true;

        // Check Desktop for popup windows (WPF popups appear here)
        try
        {
            var desktop = _automation.GetDesktop();
            var popupItem = FindDescendant(desktop,
                cf.ByName(KnownElements.RefreshAllScoreboardsMenuItemName));
            if (popupItem != null) return true;
        }
        catch { /* desktop access may fail */ }

        return false;
    }

    /// <summary>
    /// Searches for a popup menu item by name — checks main window, Desktop,
    /// and top-level windows (WPF popups can appear in any of these).
    /// </summary>
    private AutomationElement? FindPopupMenuItem(string name, ConditionFactory cf)
    {
        // Search in main window
        RefreshMainWindow();
        var item = FindDescendant(_mainWindow!, cf.ByName(name));
        if (item != null) return item;

        // Search at Desktop level (WPF context menus render as top-level popups)
        try
        {
            var desktop = _automation.GetDesktop();
            item = FindDescendant(desktop, cf.ByName(name));
            if (item != null) return item;
        }
        catch { /* desktop access may fail */ }

        // Search top-level windows from the app
        var topWindows = GetTopLevelWindows();
        foreach (var tw in topWindows)
        {
            item = FindDescendant(tw, cf.ByName(name));
            if (item != null) return item;
        }

        return null;
    }

    /// <summary>
    /// Dumps all available context menus, popups, and menu items from the
    /// main window, Desktop, and top-level windows for popup discovery.
    /// </summary>
    private void DumpPopupDiscovery(ConditionFactory cf)
    {
        // Main window menu items
        var allMenuItems = FindAllDescendants(_mainWindow!, cf.ByControlType(ControlType.MenuItem));
        Console.WriteLine($"  Main window: {allMenuItems.Length} MenuItem(s)");
        foreach (var mi in allMenuItems)
        {
            var miName = SafeGet(() => mi.Name);
            if (!string.IsNullOrWhiteSpace(miName) &&
                !new[] { "File", "Scoring", "Video", "Statistics", "Live", "Tools", "View", "Help", "System", "Live Scorer" }
                    .Contains(miName))
            {
                PrintElement("    ", mi);
            }
        }

        // Desktop-level search for popup/context menu elements
        try
        {
            var desktop = _automation.GetDesktop();
            var desktopMenus = FindAllDescendants(desktop, cf.ByControlType(ControlType.Menu));
            Console.WriteLine($"  Desktop: {desktopMenus.Length} Menu element(s)");
            foreach (var m in desktopMenus)
            {
                PrintElement("    ", m);
                var children = FindAllDescendants(m, cf.ByControlType(ControlType.MenuItem));
                foreach (var c in children)
                    PrintElement("      ", c);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Desktop search failed: {ex.Message}");
        }

        // Top-level windows
        var topWindows = GetTopLevelWindows();
        Console.WriteLine($"  Top-level windows: {topWindows.Length}");
        foreach (var tw in topWindows)
        {
            Console.WriteLine($"    \"{tw.Title}\" ClassName=\"{tw.ClassName}\"");
            if (tw.Title != _mainWindow!.Title)
            {
                var dump = TreeDumper.Dump(tw, maxDepth: 4);
                Console.WriteLine(dump);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // STEP 10: Change Match — Open 2nd match to validate change-match flow
    // ═══════════════════════════════════════════════════════════════════════

    private bool Step10_FindChangeMatchElement()
    {
        PrintStep(10, "Change match — select 2nd match from grid and verify load");
        var cf = _automation.ConditionFactory;

        try
        {
            // --- 10a: Open File → Open Match... ---
            Console.WriteLine("  Opening File → Open Match...");
            var fileMenu = FindDescendant(_mainWindow!, cf.ByAutomationId(KnownElements.FileMenuAutomationId));
            if (fileMenu == null)
            {
                PrintFail("File menu not found.");
                DumpAndSave("step10-no-file-menu");
                return false;
            }

            var openMatch = ClickMenuItem(fileMenu, KnownElements.OpenMatchMenuItemAutomationId, cf);
            if (openMatch == null)
            {
                PrintFail("'Open Match...' menu item not found.");
                DumpAndSave("step10-no-open-match");
                return false;
            }
            Console.WriteLine("  ✓ Open Match clicked");
            Thread.Sleep(500);
            RefreshMainWindow();

            // --- 10b: Wait for dialog to appear ---
            Console.WriteLine("  Waiting for Open Match dialog...");
            var dialog = WaitForElement(_mainWindow!,
                cf.ByName(KnownElements.MatchSelectionDialogName), LoginTransitionTimeoutSeconds * 1000);

            if (dialog == null)
            {
                PrintFail("Open Match dialog did not appear.");
                DumpAndSave("step10-no-dialog");
                return false;
            }
            Console.WriteLine("  ✓ Open Match dialog found");

            // --- 10c: Wait for spinner to clear (filters should still be set from first search) ---
            Console.WriteLine("  Waiting for search to complete (filters should be retained)...");
            if (!WaitForSpinnerIdle(dialog, cf))
            {
                PrintFail("Search spinner did not clear.");
                DumpAndSave("step10-spinner-timeout");
                return false;
            }

            // --- 10d: Find the grid and select 2nd row ---
            var grid = FindDescendant(dialog,
                cf.ByAutomationId(KnownElements.MatchDataGridAutomationId));
            if (grid == null)
            {
                PrintFail("DataGrid not found in Open Match dialog.");
                DumpAndSave("step10-no-grid");
                return false;
            }

            var rows = FindAllDescendants(grid, cf.ByControlType(ControlType.DataItem));
            Console.WriteLine($"  Grid has {rows.Length} row(s)");

            if (rows.Length < 2)
            {
                PrintFail($"Need at least 2 rows to test change-match, found {rows.Length}.");
                DumpAndSave("step10-not-enough-rows");
                return false;
            }

            // Log what we're selecting
            for (int i = 0; i < Math.Min(rows.Length, 3); i++)
                Console.WriteLine($"    Row[{i}]: \"{SafeGet(() => rows[i].Name)}\"");

            var secondRow = rows[1];
            Console.WriteLine($"  [{Timestamp()}] Selecting row[1]: \"{SafeGet(() => secondRow.Name)}\"");

            if (!SelectDataGridRow(secondRow))
            {
                PrintFail("Could not select row[1].");
                DumpAndSave("step10-select-failed");
                return false;
            }
            Console.WriteLine($"  [{Timestamp()}] ✓ Row[1] selected");
            Thread.Sleep(500);

            // --- 10e: Capture title before opening, then click "Open Read Only" ---
            _titleBeforeMatchOpen = SafeGet(() => _mainWindow!.Title);

            var openReadOnly = FindDescendant(dialog,
                cf.ByAutomationId(KnownElements.OpenReadOnlyButtonAutomationId))
                ?? FindDescendant(_mainWindow!,
                    cf.ByAutomationId(KnownElements.OpenReadOnlyButtonAutomationId));

            if (openReadOnly == null)
            {
                Console.WriteLine("  ⚠ 'Open Read Only' button not found — using double-click fallback");
                secondRow.DoubleClick();
                Console.WriteLine("  ✓ Double-clicked row[1]");
            }
            else
            {
                var invokeError = InvokeButtonSafely(openReadOnly);
                if (invokeError != null)
                {
                    Console.WriteLine($"  ⚠ InvokeButtonSafely failed: {invokeError} — using double-click");
                    secondRow.DoubleClick();
                }
                else
                {
                    Console.WriteLine($"  [{Timestamp()}] ✓ 'Open Read Only' invoked");
                }
            }

            // --- 10f: Wait for match to load (reuse Step 7 logic inline) ---
            Console.WriteLine($"  [{Timestamp()}] Waiting for match load...");
            var sw = Stopwatch.StartNew();
            bool dialogGone = false;
            bool titleChanged = false;
            bool syncReady = false;

            while (sw.Elapsed.TotalSeconds < MatchLoadTimeoutSeconds)
            {
                RefreshMainWindow();

                if (!dialogGone)
                {
                    var matchDialog = FindDescendant(_mainWindow!,
                        cf.ByName(KnownElements.MatchSelectionDialogName));
                    if (matchDialog == null)
                    {
                        dialogGone = true;
                        Console.WriteLine($"  [{Timestamp()}] ✓ Dialog closed [{sw.Elapsed:mm\\:ss}]");
                    }
                }

                if (dialogGone && !titleChanged)
                {
                    var currentTitle = SafeGet(() => _mainWindow!.Title);
                    if (currentTitle != _titleBeforeMatchOpen)
                    {
                        titleChanged = true;
                        Console.WriteLine($"  [{Timestamp()}] ✓ Title changed [{sw.Elapsed:mm\\:ss}]");
                        Console.WriteLine($"    New: \"{SafeGet(() => _mainWindow!.Title)}\"");
                    }
                }

                if (dialogGone && !syncReady)
                {
                    var syncBar = FindDescendant(_mainWindow!, cf.ByClassName(KnownElements.StatusBarClassName));
                    if (syncBar != null)
                    {
                        var syncText = FindAllDescendants(syncBar, cf.ByControlType(ControlType.Text))
                            .FirstOrDefault(t => SafeGet(() => t.Name) == "Up to Date");
                        if (syncText != null)
                        {
                            syncReady = true;
                            Console.WriteLine($"  [{Timestamp()}] ✓ Sync: Up to Date [{sw.Elapsed:mm\\:ss}]");
                        }
                    }
                }

                if (dialogGone && syncReady)
                    break;

                var status = $"dialog={(!dialogGone ? "open" : "closed")} title={(!titleChanged ? "unchanged" : "changed")} sync={(!syncReady ? "pending" : "ready")}";
                Console.Write($"\r  [{sw.Elapsed:mm\\:ss}] Waiting... {status}");
                Thread.Sleep(PollIntervalMs);
            }

            Console.WriteLine();

            if (!dialogGone)
            {
                PrintFail("Open Match dialog did not close within timeout.");
                DumpAndSave("step10-dialog-stuck");
                return false;
            }

            if (!syncReady)
            {
                PrintFail("Sync status never reached 'Up to Date' within timeout.");
                DumpAndSave("step10-sync-timeout");
                return false;
            }

            // Allow panels to settle
            Thread.Sleep(2000);
            RefreshMainWindow();

            Console.WriteLine($"  Window title: \"{SafeGet(() => _mainWindow!.Title)}\"");

            // --- 10g: Re-extract team names to confirm different match loaded ---
            Console.WriteLine("\n  Re-extracting team names to confirm match change...");
            var teamData = ExtractTeamNames(cf);
            if (teamData != null)
            {
                Console.WriteLine($"  Team 1: Club=\"{teamData.Value.club1}\" Team=\"{teamData.Value.team1}\"");
                Console.WriteLine($"  Team 2: Club=\"{teamData.Value.club2}\" Team=\"{teamData.Value.team2}\"");
            }
            else
            {
                Console.WriteLine("  ⚠ Could not extract team names (non-fatal)");
            }

            // --- 10h: Capture scoreboard for the new match ---
            Console.WriteLine("\n  Capturing scoreboard for new match...");
            RefreshMainWindow();
            var scoreboard = FindDescendant(_mainWindow!,
                cf.ByAutomationId("twdReplayScreen"));
            if (scoreboard != null)
            {
                // Activate the scoreboard tab (it may be tabbed with Video Display)
                ActivateToolWindow(scoreboard);
                Thread.Sleep(2000);

                var preview = FindDescendant(scoreboard,
                    cf.ByAutomationId("ReplayScreenPreview"));
                CaptureScoreboardImage(preview ?? scoreboard);
            }
            else
            {
                Console.WriteLine("  ⚠ Main Scoreboard not found for capture (non-fatal)");
            }

            PrintPass(titleChanged ? "Match changed — title changed" : "Match changed — sync confirmed");
            return PauseForUser();
        }
        catch (Exception ex)
        {
            PrintFail($"Error in change-match flow: {ex.Message}");
            DumpAndSave("step10-error");
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // STEP 11: Start/Stop Live Stream — discovery and interaction test
    // ═══════════════════════════════════════════════════════════════════════

    private bool Step11_StartStopLiveStream()
    {
        PrintStep(11, "Start Live Stream, handle consent dialogs, verify streaming, then Stop");
        var cf = _automation.ConditionFactory;
        RefreshMainWindow();

        try
        {
            // --- 11a: Find the Video Display tool window ---
            Console.WriteLine("  Searching for Video Display tool window...");
            var allToolWindows = FindAllDescendants(_mainWindow!,
                cf.ByClassName("ToolWindow"));

            AutomationElement? videoPane = null;
            foreach (var tw in allToolWindows)
            {
                var twName = SafeGet(() => tw.Name);
                var twAutoId = SafeGet(() => tw.AutomationId);
                if (twAutoId == "twdVideoCapture" ||
                    twName.Contains("Video Display", StringComparison.OrdinalIgnoreCase))
                {
                    videoPane = tw;
                    Console.WriteLine($"  ✓ Found: Name=\"{twName}\" AutomationId=\"{twAutoId}\"");
                    break;
                }
            }

            if (videoPane == null)
            {
                PrintFail("Video Display tool window not found.");
                DumpAndSave("step11-no-video-pane");
                return false;
            }

            // Activate the Video Display tab
            ActivateToolWindow(videoPane);
            Thread.Sleep(1000);
            RefreshMainWindow();

            // --- 11b: Find the Start Live Stream button ---
            Console.WriteLine("  Searching for Start Live Stream button...");

            // Search broadly — button text may be Name or part of it
            AutomationElement? startButton = null;
            var allButtons = FindAllDescendants(videoPane, cf.ByControlType(ControlType.Button));
            Console.WriteLine($"  Found {allButtons.Length} button(s) in Video Display:");
            foreach (var btn in allButtons)
            {
                var btnName = SafeGet(() => btn.Name);
                var btnAutoId = SafeGet(() => btn.AutomationId);
                Console.WriteLine($"    Name=\"{btnName}\" AutomationId=\"{btnAutoId}\"");

                if (btnName.Contains("Start Live Stream", StringComparison.OrdinalIgnoreCase))
                {
                    startButton = btn;
                    Console.WriteLine("    ↑ ✓ MATCHED as Start Live Stream");
                }
            }

            if (startButton == null)
            {
                // Broader search — any clickable element with matching text
                var allElements = FindAllDescendants(videoPane,
                    cf.ByControlType(ControlType.Hyperlink));
                foreach (var el in allElements)
                {
                    var elName = SafeGet(() => el.Name);
                    Console.WriteLine($"    [Hyperlink] Name=\"{elName}\"");
                    if (elName.Contains("Start Live Stream", StringComparison.OrdinalIgnoreCase))
                    {
                        startButton = el;
                        Console.WriteLine("    ↑ ✓ MATCHED as Start Live Stream (Hyperlink)");
                    }
                }
            }

            if (startButton == null)
            {
                Console.WriteLine("  ⚠ Start Live Stream button not found — dumping Video Display tree...");
                var dump = TreeDumper.Dump(videoPane, maxDepth: 6);
                Console.WriteLine(dump);
                PrintFail("Start Live Stream button not found in Video Display.");
                return false;
            }

            // --- 11c: Click Start Live Stream ---
            Console.WriteLine("  Clicking Start Live Stream...");
            var invokeError = InvokeButtonSafely(startButton);
            if (invokeError != null)
            {
                PrintFail($"Could not click Start Live Stream: {invokeError}");
                return false;
            }
            Thread.Sleep(2000);

            // --- 11d: Handle Video Consent dialog ---
            Console.WriteLine("  Waiting for Video Consent dialog...");
            RefreshMainWindow();

            AutomationElement? consentDialog = null;
            var topWindows = GetTopLevelWindows();
            foreach (var w in topWindows)
            {
                var wTitle = SafeGet(() => w.Title);
                if (wTitle.Contains("Video Consent", StringComparison.OrdinalIgnoreCase))
                {
                    consentDialog = w;
                    Console.WriteLine($"  ✓ Found consent dialog: \"{wTitle}\"");
                    break;
                }
            }

            if (consentDialog == null)
            {
                // Try as child window of main window
                consentDialog = WaitForElement(_mainWindow!,
                    cf.ByName("Video Consent"), 5000);
            }

            if (consentDialog == null)
            {
                Console.WriteLine("  ⚠ No Video Consent dialog appeared — stream may have started directly or button was not active");
                Console.WriteLine("  Dumping all top-level windows for analysis...");
                foreach (var w in GetTopLevelWindows())
                {
                    Console.WriteLine($"    \"{SafeGet(() => w.Title)}\" ClassName=\"{SafeGet(() => w.ClassName)}\"");
                }
                DumpAndSave("step11-no-consent-dialog");
                PrintFail("Video Consent dialog did not appear after clicking Start Live Stream.");
                return false;
            }

            // Dump consent dialog for discovery
            Console.WriteLine("\n  ── Consent Dialog Contents ──");
            var consentDump = TreeDumper.Dump(consentDialog, maxDepth: 4);
            Console.WriteLine(consentDump);

            // Find and click "Video Consented" button
            Console.WriteLine("  Looking for 'Video Consented' button...");
            var consentButton = FindDescendant(consentDialog,
                cf.ByName("Video Consented"));
            if (consentButton == null)
            {
                // Try by AutomationId — user reported btnAction might be the ID
                consentButton = FindDescendant(consentDialog,
                    cf.ByAutomationId("btnAction"));
            }

            if (consentButton == null)
            {
                PrintFail("'Video Consented' button not found in consent dialog.");
                DumpAndSave("step11-no-consent-button");
                return false;
            }

            Console.WriteLine($"  ✓ Found: Name=\"{SafeGet(() => consentButton.Name)}\" " +
                              $"AutomationId=\"{SafeGet(() => consentButton.AutomationId)}\"");
            Console.WriteLine("  Clicking Video Consented...");
            invokeError = InvokeButtonSafely(consentButton);
            if (invokeError != null)
            {
                PrintFail($"Could not click Video Consented: {invokeError}");
                return false;
            }
            Thread.Sleep(2000);

            // --- 11e: Handle "Add Live Stream to Match Centre?" dialog (click No) ---
            Console.WriteLine("  Checking for 'Add Live Stream to Match Centre?' dialog...");
            RefreshMainWindow();

            AutomationElement? matchCentreDialog = null;
            topWindows = GetTopLevelWindows();
            foreach (var w in topWindows)
            {
                var wTitle = SafeGet(() => w.Title);
                if (wTitle.Contains("Match Centre", StringComparison.OrdinalIgnoreCase) ||
                    wTitle.Contains("Live Stream", StringComparison.OrdinalIgnoreCase))
                {
                    matchCentreDialog = w;
                    Console.WriteLine($"  ✓ Found Match Centre dialog: \"{wTitle}\"");
                    break;
                }
            }

            if (matchCentreDialog == null)
            {
                // Also check child windows
                matchCentreDialog = FindDescendant(_mainWindow!,
                    cf.ByName("Add Live Stream to Match Centre?"));
            }

            if (matchCentreDialog != null)
            {
                Console.WriteLine("\n  ── Match Centre Dialog Contents ──");
                var mcDump = TreeDumper.Dump(matchCentreDialog, maxDepth: 4);
                Console.WriteLine(mcDump);

                // Click No
                var noButton = FindDescendant(matchCentreDialog, cf.ByName("No"));
                if (noButton == null)
                {
                    // Try looking for a standard No button by AutomationId
                    var mcButtons = FindAllDescendants(matchCentreDialog, cf.ByControlType(ControlType.Button));
                    foreach (var btn in mcButtons)
                    {
                        var btnName = SafeGet(() => btn.Name);
                        Console.WriteLine($"    Button: \"{btnName}\"");
                        if (btnName.Equals("No", StringComparison.OrdinalIgnoreCase))
                        {
                            noButton = btn;
                            break;
                        }
                    }
                }

                if (noButton != null)
                {
                    Console.WriteLine("  Clicking No...");
                    invokeError = InvokeButtonSafely(noButton);
                    if (invokeError != null)
                        Console.WriteLine($"  ⚠ Could not click No: {invokeError}");
                    else
                        Console.WriteLine("  ✓ Clicked No");
                    Thread.Sleep(1500);
                }
                else
                {
                    Console.WriteLine("  ⚠ No 'No' button found — dialog may need manual dismissal");
                }
            }
            else
            {
                Console.WriteLine("  (No Match Centre dialog appeared — continuing)");
            }

            // --- 11f: Verify stream is running — look for Stop Live Stream button ---
            Console.WriteLine("\n  Verifying stream started — looking for Stop Live Stream...");
            Thread.Sleep(3000);
            RefreshMainWindow();

            // Re-find the Video Display pane (may have refreshed)
            var freshVideoPane = FindDescendant(_mainWindow!, cf.ByAutomationId("twdVideoCapture"));
            if (freshVideoPane == null)
            {
                freshVideoPane = videoPane; // fall back to original reference
                Console.WriteLine("  (Using original videoPane reference)");
            }

            ActivateToolWindow(freshVideoPane);
            Thread.Sleep(1000);

            AutomationElement? stopButton = null;
            var freshButtons = FindAllDescendants(freshVideoPane, cf.ByControlType(ControlType.Button));
            Console.WriteLine($"  Found {freshButtons.Length} button(s) in Video Display after start:");
            foreach (var btn in freshButtons)
            {
                var btnName = SafeGet(() => btn.Name);
                var btnAutoId = SafeGet(() => btn.AutomationId);
                Console.WriteLine($"    Name=\"{btnName}\" AutomationId=\"{btnAutoId}\"");

                if (btnName.Contains("Stop Live", StringComparison.OrdinalIgnoreCase))
                {
                    stopButton = btn;
                    Console.WriteLine("    ↑ ✓ MATCHED as Stop Live Stream");
                }
            }

            // Also check for hyperlinks (UI may use hyperlink style)
            if (stopButton == null)
            {
                var hyperlinks = FindAllDescendants(freshVideoPane, cf.ByControlType(ControlType.Hyperlink));
                foreach (var hl in hyperlinks)
                {
                    var hlName = SafeGet(() => hl.Name);
                    Console.WriteLine($"    [Hyperlink] Name=\"{hlName}\"");
                    if (hlName.Contains("Stop Live", StringComparison.OrdinalIgnoreCase))
                    {
                        stopButton = hl;
                        Console.WriteLine("    ↑ ✓ MATCHED as Stop Live Stream (Hyperlink)");
                    }
                    if (hlName.Contains("Hide Stream", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("    ↑ ✓ NOTED: Hide Stream Output link present");
                    }
                }
            }

            if (stopButton == null)
            {
                Console.WriteLine("  ⚠ Stop Live Stream not found — dumping Video Display tree...");
                var dump = TreeDumper.Dump(freshVideoPane, maxDepth: 6);
                Console.WriteLine(dump);
                PrintFail("Stream may not have started — Stop Live Stream button not found.");
                return false;
            }

            Console.WriteLine("  ✓ Stream is LIVE — Stop button found");

            // Look for the streaming timer / duration text
            var allText = FindAllDescendants(freshVideoPane, cf.ByControlType(ControlType.Text));
            foreach (var txt in allText)
            {
                var txtName = SafeGet(() => txt.Name);
                if (!string.IsNullOrWhiteSpace(txtName))
                    Console.WriteLine($"    [Text] \"{txtName}\"");
            }

            // --- 11g: Wait a few seconds then stop the stream ---
            Console.WriteLine("\n  Waiting 5 seconds before stopping stream...");
            Thread.Sleep(5000);

            Console.WriteLine("  Clicking Stop Live Stream...");
            invokeError = InvokeButtonSafely(stopButton);
            if (invokeError != null)
            {
                PrintFail($"Could not click Stop Live Stream: {invokeError}");
                return false;
            }
            Thread.Sleep(3000);

            // --- 11h: Verify stream stopped — Start button should reappear ---
            Console.WriteLine("  Verifying stream stopped...");
            RefreshMainWindow();

            var postStopVideoPane = FindDescendant(_mainWindow!, cf.ByAutomationId("twdVideoCapture"))
                                    ?? freshVideoPane;
            ActivateToolWindow(postStopVideoPane);
            Thread.Sleep(1000);

            var postStopButtons = FindAllDescendants(postStopVideoPane, cf.ByControlType(ControlType.Button));
            bool startButtonReappeared = false;
            Console.WriteLine($"  Post-stop buttons ({postStopButtons.Length}):");
            foreach (var btn in postStopButtons)
            {
                var btnName = SafeGet(() => btn.Name);
                var btnAutoId = SafeGet(() => btn.AutomationId);
                Console.WriteLine($"    Name=\"{btnName}\" AutomationId=\"{btnAutoId}\"");
                if (btnName.Contains("Start Live Stream", StringComparison.OrdinalIgnoreCase))
                    startButtonReappeared = true;
            }

            if (startButtonReappeared)
            {
                Console.WriteLine("  ✓ Start Live Stream button reappeared — stream stopped successfully");
            }
            else
            {
                Console.WriteLine("  ⚠ Start Live Stream button not found after stop — stream may still be running");
                Console.WriteLine("  Dumping Video Display tree for analysis...");
                var dump = TreeDumper.Dump(postStopVideoPane, maxDepth: 6);
                Console.WriteLine(dump);
            }

            PrintPass(startButtonReappeared
                ? "Start → Consent → Live → Stop — full cycle complete"
                : "Start → Consent → Live — stop may not have completed (check log)");
            return PauseForUser();
        }
        catch (Exception ex)
        {
            PrintFail($"Error in live stream flow: {ex.Message}");
            Console.WriteLine($"  {ex.GetType().FullName}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            DumpAndSave("step11-error");
            return false;
        }
    }

    /// <summary>
    /// Opens Scoring → Match Details/Teams, extracts club and team names
    /// from both MatchTeamViews, then closes the dialog. Returns the data
    /// or null if extraction fails.
    /// </summary>
    private (string club1, string team1, string club2, string team2)? ExtractTeamNames(ConditionFactory cf)
    {
        var scoringMenu = FindDescendant(_mainWindow!, cf.ByAutomationId(KnownElements.ScoringMenuAutomationId));
        if (scoringMenu == null)
        {
            Console.WriteLine("    ⚠ Scoring menu not found");
            return null;
        }

        var matchDetailsItem = ClickMenuItemByName(
            scoringMenu, KnownElements.MatchDetailsMenuItemName, cf);
        if (matchDetailsItem == null)
        {
            Console.WriteLine("    ⚠ Match Details/Teams menu item not found");
            return null;
        }

        Thread.Sleep(1000);
        RefreshMainWindow();

        var dialog = WaitForElement(_mainWindow!,
            cf.ByName(KnownElements.MatchDetailsDialogName), 5000);
        if (dialog == null)
        {
            Console.WriteLine("    ⚠ Match Details dialog did not appear");
            return null;
        }

        var teamViews = FindAllDescendants(dialog,
            cf.ByClassName(KnownElements.MatchTeamViewClassName));
        if (teamViews.Length < 2)
        {
            Console.WriteLine($"    ⚠ Expected 2 MatchTeamViews, found {teamViews.Length}");
            CloseMatchDetailsDialog(dialog, cf);
            return null;
        }

        var club1 = ReadComboBoxValue(FindDescendant(teamViews[0], cf.ByAutomationId(KnownElements.ClubComboBoxAutomationId)));
        var team1 = ReadComboBoxValue(FindDescendant(teamViews[0], cf.ByAutomationId(KnownElements.TeamComboBoxAutomationId)));
        var club2 = ReadComboBoxValue(FindDescendant(teamViews[1], cf.ByAutomationId(KnownElements.ClubComboBoxAutomationId)));
        var team2 = ReadComboBoxValue(FindDescendant(teamViews[1], cf.ByAutomationId(KnownElements.TeamComboBoxAutomationId)));

        CloseMatchDetailsDialog(dialog, cf);
        return (club1, team1, club2, team2);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers — Safe Automation Primitives
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Polls for a descendant matching <paramref name="condition"/> to appear,
    /// retrying up to <paramref name="timeoutMs"/> milliseconds.
    /// </summary>
    private static AutomationElement? WaitForElement(
        AutomationElement parent, ConditionBase condition, int timeoutMs = 2000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var element = FindDescendant(parent, condition);
            if (element != null)
                return element;
            Thread.Sleep(200);
        }
        return null;
    }

    /// <summary>
    /// Clicks a menu item by expanding the parent and searching by AutomationId
    /// with retry (menus may take time to expand in the automation tree).
    /// </summary>
    private static AutomationElement? ClickMenuItem(
        AutomationElement menuParent, string automationId, ConditionFactory cf, int retries = 5)
    {
        menuParent.Click();
        Thread.Sleep(300);

        for (int attempt = 0; attempt < retries; attempt++)
        {
            var item = FindDescendant(menuParent, cf.ByAutomationId(automationId));
            if (item != null)
            {
                item.Click();
                return item;
            }
            Thread.Sleep(200);
        }
        return null;
    }

    /// <summary>
    /// Clicks a menu item by expanding the parent and searching by Name
    /// with retry (menus may take time to expand in the automation tree).
    /// </summary>
    private static AutomationElement? ClickMenuItemByName(
        AutomationElement menuParent, string name, ConditionFactory cf, int retries = 5)
    {
        menuParent.Click();
        Thread.Sleep(300);

        for (int attempt = 0; attempt < retries; attempt++)
        {
            var item = FindDescendant(menuParent, cf.ByName(name));
            if (item != null)
            {
                item.Click();
                return item;
            }
            Thread.Sleep(200);
        }
        return null;
    }

    /// <summary>
    /// Selects a DataGrid row using SelectionItemPattern (reliable WPF selection),
    /// falling back to Click() if the pattern is unavailable.
    /// </summary>
    private static bool SelectDataGridRow(AutomationElement row)
    {
        try
        {
            if (row.Patterns.SelectionItem.IsSupported)
            {
                row.Patterns.SelectionItem.Pattern.Select();
                return true;
            }
        }
        catch
        {
            // Pattern might claim support but throw — fall through to Click
        }

        try
        {
            row.Click();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Invokes a button safely: checks IsEnabled, tries InvokePattern, falls back to Click.
    /// Returns a descriptive error message on failure, or null on success.
    /// </summary>
    private static string? InvokeButtonSafely(AutomationElement button)
    {
        try
        {
            bool enabled = button.Properties.IsEnabled.ValueOrDefault;
            if (!enabled)
                return "Button is disabled (IsEnabled=false) — likely no row is selected.";

            try
            {
                button.AsButton().Invoke();
                return null;
            }
            catch
            {
                // InvokePattern failed — try mouse click
                button.Click();
                return null;
            }
        }
        catch (Exception ex)
        {
            return $"Button invocation failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Waits for the search spinner inside the Open Match dialog to become idle.
    /// Tries multiple detection strategies:
    ///   1. ClassName="LoaderSpinner" (from PRD research)
    ///   2. ClassName="FontAwesomeSpinner" inside the dialog (not status bar)
    ///   3. Falls back to grid row-count stabilization if no spinner found
    /// Returns true if search is idle, false on timeout.
    /// </summary>
    private static bool WaitForSpinnerIdle(
        AutomationElement dialog, ConditionFactory cf, int timeoutMs = 30000)
    {
        // Discovery finding: LoaderSpinner is dynamically added/removed from
        // the tree — it does NOT toggle IsOffscreen. When present, a search is
        // in progress. When absent, the search is complete.
        // It appears as a direct child of the dialog window (sibling of
        // AllMatchesView), with a companion TextBlock "Retrieving Matches on
        // Server...".

        // Brief delay so spinner can appear before we start polling
        Thread.Sleep(300);

        var sw = Stopwatch.StartNew();
        bool spinnerSeen = false;

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var spinner = FindDescendant(dialog,
                cf.ByClassName(KnownElements.LoaderSpinnerClassName));

            if (spinner != null)
            {
                if (!spinnerSeen)
                {
                    Console.WriteLine($"  [{Timestamp()}] LoaderSpinner detected — search in progress");
                    spinnerSeen = true;
                }

                Console.Write($"\r  [{Timestamp()}] LoaderSpinner present — waiting for removal...  ");
                Thread.Sleep(300);
            }
            else
            {
                if (spinnerSeen)
                {
                    Console.WriteLine($"\r  [{Timestamp()}] LoaderSpinner removed — search complete          ");
                }
                else
                {
                    Console.WriteLine($"  [{Timestamp()}] No LoaderSpinner found — search not active or already complete");
                }
                return true;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"  [{Timestamp()}] ⚠ LoaderSpinner still present after {timeoutMs}ms timeout");
        return false;
    }

    /// <summary>
    /// Waits for a DataGrid's row count to stabilize (same count for 2 consecutive polls).
    /// Returns the stable row count, or -1 on timeout.
    /// </summary>
    private static int WaitForGridStable(
        AutomationElement grid, ConditionFactory cf, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        int lastCount = -1;
        int stablePolls = 0;

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var rows = FindAllDescendants(grid, cf.ByControlType(ControlType.DataItem));
            int count = rows.Length;

            if (count == lastCount && count >= 0)
            {
                stablePolls++;
                if (stablePolls >= 2)
                    return count;
            }
            else
            {
                stablePolls = 0;
                lastCount = count;
            }

            Thread.Sleep(500);
        }

        return lastCount;
    }

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

    private void DumpIfEnabled(string label, AutomationElement element, int maxDepth = 6)
    {
        if (!_captureUiTree) return;
        var dump = TreeDumper.Dump(element, maxDepth: maxDepth);
        Console.WriteLine($"\n  ── UI Tree: {label} ──");
        Console.WriteLine(dump);
        Console.WriteLine($"  ── End ──");
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
