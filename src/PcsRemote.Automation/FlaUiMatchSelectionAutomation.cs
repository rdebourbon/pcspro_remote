using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PcsRemote.Core;

namespace PcsRemote.Automation;

/// <summary>
/// FlaUI-based implementation of <see cref="IMatchSelectionAutomation"/>.
/// Ported from diagnostic tool Steps 4–6.
/// </summary>
internal sealed class FlaUiMatchSelectionAutomation : IMatchSelectionAutomation
{
    private const int SpinnerPollIntervalMs = 300;
    private const int SpinnerTimeoutMs = 30_000;
    private const int DialogPollTimeoutMs = 15_000;
    private const int KeyboardPauseMs = 200;

    private readonly PcsProWindowLocator _locator;
    private readonly ILogger<FlaUiMatchSelectionAutomation> _logger;
    private readonly PcsProOptions _options;
    public FlaUiMatchSelectionAutomation(
        PcsProWindowLocator locator,
        ILogger<FlaUiMatchSelectionAutomation> logger,
        IOptions<PcsProOptions> options)
    {
        _locator = locator;
        _logger = logger;
        _options = options.Value;
    }

    /// <inheritdoc/>
    public void OpenMatchDialogAndSearch(DateOnly searchDate)
    {
        var window = _locator.FindMainWindow()
            ?? throw new InvalidOperationException("PCS Pro main window not found.");

        var cf = _locator.Automation.ConditionFactory;

        // Check if the match selection dialog is already open (e.g. after ChangeMatch).
        var dialog = UIAutomationHelpers.FindDescendant(
            window,
            cf.ByName(KnownElements.MatchSelectionDialogName));

        if (dialog != null)
        {
            _logger.LogDebug("Match selection dialog already open — skipping menu navigation");
        }
        else
        {
            NavigateToOpenMatchDialog(window, cf);
            dialog = WaitForMatchSelectionDialog(window, cf);
        }

        ClickClearFilters(dialog, cf);

        if (!string.IsNullOrEmpty(_options.SiteName))
        {
            SetSiteFilter(dialog, cf, window);
            WaitForSpinnerIdle(dialog, cf, "site selection");
        }

        SetDateFilter(dialog, cf, isDateFrom: true, searchDate);
        WaitForSpinnerIdle(dialog, cf, "Date From");

        SetDateFilter(dialog, cf, isDateFrom: false, searchDate);
    }

    /// <inheritdoc/>
    public bool IsSpinnerVisible()
    {
        try
        {
            var window = _locator.FindMainWindow();
            if (window == null)
            {
                return false;
            }

            var cf = _locator.Automation.ConditionFactory;
            var dialog = UIAutomationHelpers.FindDescendant(
                window,
                cf.ByName(KnownElements.MatchSelectionDialogName));

            if (dialog == null)
            {
                return false;
            }

            var spinner = UIAutomationHelpers.FindDescendant(
                dialog,
                cf.ByClassName(KnownElements.LoaderSpinnerClassName));

            return spinner != null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "IsSpinnerVisible failed with exception");
            return false;
        }
    }

    /// <inheritdoc/>
    public bool IsUnexpectedDialogPresent(DialogProbeContext? probeContext = null)
    {
        try
        {
            var window = _locator.FindMainWindow();
            if (window == null)
            {
                return false;
            }

            return probeContext != null
                ? UIAutomationHelpers.HasUnexpectedDialog(window, _locator.Automation.ConditionFactory, probeContext)
                : UIAutomationHelpers.HasUnexpectedDialog(window, _locator.Automation.ConditionFactory);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "IsUnexpectedDialogPresent failed with exception");
            return false;
        }
    }

    /// <inheritdoc/>
    public void TryCloseUnexpectedDialog()
    {
        try
        {
            var window = _locator.FindMainWindow();
            if (window == null)
            {
                return;
            }

            UIAutomationHelpers.TryCloseFirstUnexpectedDialog(
                window, _locator.Automation.ConditionFactory, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TryCloseUnexpectedDialog failed with exception");
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> ReadDataGridRowTexts()
    {
        var window = _locator.FindMainWindow()
            ?? throw new InvalidOperationException("PCS Pro main window not found.");

        var cf = _locator.Automation.ConditionFactory;
        var dialog = UIAutomationHelpers.FindDescendant(
            window,
            cf.ByName(KnownElements.MatchSelectionDialogName))
            ?? throw new InvalidOperationException("Open Match dialog not found.");

        var grid = UIAutomationHelpers.FindDescendant(
            dialog,
            cf.ByAutomationId(KnownElements.MatchDataGridAutomationId))
            ?? throw new InvalidOperationException(
                $"DataGrid not found (AutomationId=\"{KnownElements.MatchDataGridAutomationId}\").");

        var rows = UIAutomationHelpers.FindAllDescendants(grid, cf.ByControlType(ControlType.DataItem));
        var result = new List<string>(rows.Length);

        foreach (var row in rows)
        {
            var textElements = UIAutomationHelpers.FindAllDescendants(row, cf.ByControlType(ControlType.Text));
            var cellValues = textElements.Select(t => UIAutomationHelpers.SafeGetName(t));
            result.Add(string.Join("|", cellValues));
        }

        _logger.LogDebug("Read {RowCount} DataGrid row(s)", result.Count);
        return result;
    }

    /// <inheritdoc/>
    public void SelectAndOpenMatch(MatchInfo match)
    {
        var window = _locator.FindMainWindow()
            ?? throw new InvalidOperationException("PCS Pro main window not found.");

        var cf = _locator.Automation.ConditionFactory;
        var dialog = UIAutomationHelpers.FindDescendant(
            window,
            cf.ByName(KnownElements.MatchSelectionDialogName))
            ?? throw new InvalidOperationException("Open Match dialog not found.");

        var grid = UIAutomationHelpers.FindDescendant(
            dialog,
            cf.ByAutomationId(KnownElements.MatchDataGridAutomationId))
            ?? throw new InvalidOperationException("DataGrid not found.");

        var targetRow = FindMatchingRow(grid, match, cf);

        SelectRow(targetRow);
        Thread.Sleep(KeyboardPauseMs);

        var openBtn = UIAutomationHelpers.FindDescendant(
            dialog,
            cf.ByAutomationId(KnownElements.OpenReadOnlyButtonAutomationId))
            ?? throw new InvalidOperationException(
                $"'Open Read Only' button not found (AutomationId=\"{KnownElements.OpenReadOnlyButtonAutomationId}\").");

        var error = UIAutomationHelpers.InvokeButtonSafely(openBtn, _logger);
        if (error != null)
        {
            throw new InvalidOperationException($"Open Read Only invocation failed: {error}");
        }

        _logger.LogInformation(
            "Selected and opened match: {HomeTeam} vs {AwayTeam} ({MatchType})",
            match.HomeTeam,
            match.AwayTeam,
            match.MatchType);
    }

    /// <inheritdoc/>
    public bool IsMatchLoaded()
    {
        try
        {
            var window = _locator.FindMainWindow();
            if (window == null)
            {
                return false;
            }

            var cf = _locator.Automation.ConditionFactory;
            var scoreSummary = UIAutomationHelpers.FindDescendant(
                window,
                cf.ByAutomationId(KnownElements.ScoreSummaryPaneAutomationId));

            return scoreSummary != null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "IsMatchLoaded failed with exception");
            return false;
        }
    }

    /// <inheritdoc/>
    public bool IsMainWindowPresent()
    {
        try
        {
            return _locator.FindMainWindow() is not null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "IsMainWindowPresent failed with exception");
            return false;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> ClearFiltersAndReadMatches()
    {
        var window = _locator.FindMainWindow()
            ?? throw new InvalidOperationException("PCS Pro main window not found.");

        var cf = _locator.Automation.ConditionFactory;
        var dialog = UIAutomationHelpers.FindDescendant(
            window,
            cf.ByName(KnownElements.MatchSelectionDialogName))
            ?? throw new InvalidOperationException("Open Match dialog not found.");

        ClickClearFilters(dialog, cf);
        return ReadDataGridRowTexts();
    }

    // ── Private helpers ──────────────────────────────────────────────────

    private void NavigateToOpenMatchDialog(AutomationElement window, FlaUI.Core.Conditions.ConditionFactory cf)
    {
        var fileMenu = UIAutomationHelpers.FindDescendant(
            window,
            cf.ByAutomationId(KnownElements.FileMenuAutomationId))
            ?? throw new InvalidOperationException("File menu not found.");

        fileMenu.Click();
        Thread.Sleep(KeyboardPauseMs);

        var openMatchItem = UIAutomationHelpers.WaitForElement(
            () => UIAutomationHelpers.FindDescendant(
                window,
                cf.ByAutomationId(KnownElements.OpenMatchMenuItemAutomationId)),
            timeoutMs: 2000);

        if (openMatchItem == null)
        {
            throw new InvalidOperationException("'Open Match...' menu item not found.");
        }

        UIAutomationHelpers.InvokeButtonSafely(openMatchItem, _logger);
        _logger.LogDebug("File → Open Match... clicked");
    }

    private AutomationElement WaitForMatchSelectionDialog(
        AutomationElement window,
        FlaUI.Core.Conditions.ConditionFactory cf)
    {
        var dialog = UIAutomationHelpers.WaitForElement(
            () => UIAutomationHelpers.FindDescendant(
                window,
                cf.ByName(KnownElements.MatchSelectionDialogName)),
            timeoutMs: DialogPollTimeoutMs);

        return dialog
            ?? throw new InvalidOperationException(
                $"Open Match dialog did not appear within {DialogPollTimeoutMs}ms.");
    }

    private void ClickClearFilters(AutomationElement dialog, FlaUI.Core.Conditions.ConditionFactory cf)
    {
        var clearFilters = UIAutomationHelpers.FindDescendant(
            dialog,
            cf.ByName(KnownElements.ClearFiltersLinkName))
            ?? throw new InvalidOperationException("'Clear Filters' link not found.");

        clearFilters.Click();
        Thread.Sleep(KeyboardPauseMs);
        _logger.LogDebug("Clear Filters clicked");
    }

    private void SetSiteFilter(
        AutomationElement dialog,
        FlaUI.Core.Conditions.ConditionFactory cf,
        AutomationElement window)
    {
        var comboBoxes = UIAutomationHelpers.FindAllDescendants(
            dialog,
            cf.ByControlType(ControlType.ComboBox));

        AutomationElement? siteCombo = null;
        foreach (var combo in comboBoxes)
        {
            try
            {
                var autoId = combo.AutomationId;
                if (string.IsNullOrEmpty(autoId))
                {
                    siteCombo = combo;
                    break;
                }
            }
            catch
            {
                siteCombo = combo;
                break;
            }
        }

        if (siteCombo == null)
        {
            throw new InvalidOperationException("Site ComboBox not found.");
        }

        siteCombo.Click();
        Thread.Sleep(KeyboardPauseMs);

        // Site dropdown items may appear as children of the main window (popup)
        var siteItem = UIAutomationHelpers.FindDescendant(window, cf.ByName(_options.SiteName))
            ?? throw new InvalidOperationException(
                $"Site '{_options.SiteName}' not found in dropdown.");

        siteItem.Click();
        Thread.Sleep(KeyboardPauseMs);

        _logger.LogDebug("Site filter set to {SiteName}", _options.SiteName);
    }

    private void SetDateFilter(
        AutomationElement dialog,
        FlaUI.Core.Conditions.ConditionFactory cf,
        bool isDateFrom,
        DateOnly searchDate)
    {
        var datePickers = UIAutomationHelpers.FindAllDescendants(
            dialog,
            cf.ByClassName(KnownElements.DatePickerClassName));

        if (datePickers.Length < 2)
        {
            throw new InvalidOperationException(
                $"Expected 2 DatePicker controls, found {datePickers.Length}.");
        }

        int index = isDateFrom ? 0 : 1;
        var label = isDateFrom ? "Date From" : "Date To";

        var textBox = UIAutomationHelpers.FindDescendant(
            datePickers[index],
            cf.ByAutomationId(KnownElements.DatePickerTextBoxAutomationId))
            ?? throw new InvalidOperationException(
                $"{label}: PART_TextBox not found inside DatePicker.");

        textBox.Click();
        Thread.Sleep(KeyboardPauseMs);

        UIAutomationHelpers.BringToForeground(dialog, _logger);

        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        string dateText = searchDate.ToString("dd/MM/yyyy");
        Keyboard.Type(dateText);
        Thread.Sleep(KeyboardPauseMs);

        Keyboard.Press(VirtualKeyShort.TAB);
        Thread.Sleep(KeyboardPauseMs);

        _logger.LogDebug("{DateLabel} set to {DateValue}", label, dateText);
    }

    private void WaitForSpinnerIdle(
        AutomationElement dialog,
        FlaUI.Core.Conditions.ConditionFactory cf,
        string context)
    {
        Thread.Sleep(SpinnerPollIntervalMs);

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < SpinnerTimeoutMs)
        {
            var spinner = UIAutomationHelpers.FindDescendant(
                dialog,
                cf.ByClassName(KnownElements.LoaderSpinnerClassName));

            if (spinner == null)
            {
                _logger.LogDebug("Spinner cleared after {Context}", context);
                return;
            }

            Thread.Sleep(SpinnerPollIntervalMs);
        }

        _logger.LogWarning(
            "Spinner did not clear within {TimeoutMs}ms after {Context}",
            SpinnerTimeoutMs,
            context);

        throw new InvalidOperationException(
            $"Spinner did not clear within {SpinnerTimeoutMs}ms after {context}.");
    }

    private AutomationElement FindMatchingRow(
        AutomationElement grid,
        MatchInfo match,
        FlaUI.Core.Conditions.ConditionFactory cf)
    {
        var rows = UIAutomationHelpers.FindAllDescendants(grid, cf.ByControlType(ControlType.DataItem));

        foreach (var row in rows)
        {
            var textElements = UIAutomationHelpers.FindAllDescendants(row, cf.ByControlType(ControlType.Text));
            if (textElements.Length < 5)
            {
                continue;
            }

            string team1 = UIAutomationHelpers.SafeGetName(textElements[KnownElements.GridColumnTeam1]);
            string team2 = UIAutomationHelpers.SafeGetName(textElements[KnownElements.GridColumnTeam2]);
            string matchType = UIAutomationHelpers.SafeGetName(textElements[KnownElements.GridColumnMatchType]);

            if (string.Equals(team1, match.HomeTeam, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(team2, match.AwayTeam, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(matchType, match.MatchType, StringComparison.OrdinalIgnoreCase))
            {
                return row;
            }
        }

        throw new InvalidOperationException(
            $"No DataGrid row matched: {match.HomeTeam} vs {match.AwayTeam} ({match.MatchType}).");
    }

    private static void SelectRow(AutomationElement row)
    {
        try
        {
            if (row.Patterns.SelectionItem.IsSupported)
            {
                row.Patterns.SelectionItem.Pattern.Select();
                return;
            }
        }
        catch
        {
            // Fall through to Click
        }

        row.Click();
    }
}
