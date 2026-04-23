using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using Microsoft.Extensions.Logging;

namespace PcsRemote.Automation;

/// <summary>
/// Reusable FlaUI interaction helpers extracted from the diagnostic tool
/// (<c>tools/AutomationDiagnostic/DiagnosticRunner.cs</c>).
/// All methods are stateless — required FlaUI types are accepted as parameters.
/// </summary>
internal static class UIAutomationHelpers
{
    /// <summary>
    /// Safe wrapper for <see cref="AutomationElement.FindFirstDescendant(ConditionBase)"/>.
    /// Returns <c>null</c> on COMException or element-stale errors instead of throwing.
    /// </summary>
    internal static AutomationElement? FindDescendant(AutomationElement parent, ConditionBase condition)
    {
        try
        {
            return parent.FindFirstDescendant(condition);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Safe wrapper for <see cref="AutomationElement.FindAllDescendants(ConditionBase)"/>.
    /// Returns an empty array on COMException or element-stale errors instead of throwing.
    /// </summary>
    internal static AutomationElement[] FindAllDescendants(AutomationElement parent, ConditionBase condition)
    {
        try
        {
            return parent.FindAllDescendants(condition);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Locates a <see cref="ControlType.Button"/> element by matching search text against
    /// either the button's <c>Name</c> or the <c>Name</c> of child <c>Text</c> elements.
    /// </summary>
    /// <remarks>
    /// WPF buttons often render their label via a child <c>TextBlock</c> rather than setting
    /// the <c>Button.Name</c> property directly. This method handles both patterns.
    /// Uses case-insensitive partial matching via <see cref="string.Contains(string, StringComparison)"/>.
    /// </remarks>
    /// <returns>The first matching button, or <c>null</c> if no match is found.</returns>
    internal static AutomationElement? FindButtonByChildText(
        AutomationElement parent,
        string searchText,
        ConditionFactory cf,
        ILogger? logger = null)
    {
        var buttons = FindAllDescendants(parent, cf.ByControlType(ControlType.Button));
        foreach (var btn in buttons)
        {
            var btnName = SafeGetName(btn);
            if (btnName.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            {
                logger?.LogDebug(
                    "Found button by Name: {ButtonName}",
                    btnName);
                return btn;
            }

            // Check child Text elements (TextBlock in WPF)
            var childTexts = FindAllDescendants(btn, cf.ByControlType(ControlType.Text));
            foreach (var txt in childTexts)
            {
                var txtName = SafeGetName(txt);
                if (txtName.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                {
                    logger?.LogDebug(
                        "Found button by child text: {ChildTextName}",
                        txtName);
                    return btn;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Safely invokes a button using a multi-strategy fallback:
    /// (1) check <c>IsEnabled</c>, (2) try <c>InvokePattern</c>,
    /// (3) fall back to <c>.Click()</c>.
    /// </summary>
    /// <returns>
    /// <c>null</c> on success, or a descriptive error message on failure.
    /// </returns>
    internal static string? InvokeButtonSafely(AutomationElement button, ILogger? logger = null)
    {
        try
        {
            bool enabled = button.Properties.IsEnabled.ValueOrDefault;
            if (!enabled)
            {
                return "Button is disabled (IsEnabled=false) — likely no row is selected.";
            }

            try
            {
                button.AsButton().Invoke();
                return null;
            }
            catch
            {
                // InvokePattern failed — try mouse click as fallback
                logger?.LogDebug("InvokePattern failed, falling back to Click");
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
    /// Polls for an element using the provided search delegate within a timeout.
    /// </summary>
    /// <param name="searchFunc">
    /// A delegate that performs the element search and returns the element or <c>null</c>.
    /// </param>
    /// <param name="timeoutMs">Timeout in milliseconds. Default is 2000ms.</param>
    /// <param name="cancellationToken">
    /// Cancellation token. When cancellation is requested, the method returns <c>null</c>
    /// immediately (does not throw).
    /// </param>
    /// <returns>The found element, or <c>null</c> on timeout or cancellation.</returns>
    internal static AutomationElement? WaitForElement(
        Func<AutomationElement?> searchFunc,
        int timeoutMs = 2000,
        CancellationToken cancellationToken = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            var element = searchFunc();
            if (element != null)
            {
                return element;
            }

            Thread.Sleep(200);
        }

        return null;
    }

    /// <summary>
    /// Walks the automation tree upward from a given element using <c>RawViewWalker</c>,
    /// returning the first ancestor whose <c>ClassName</c> matches.
    /// Caps traversal at 10 parent steps to prevent infinite loops.
    /// </summary>
    /// <returns>The matching ancestor, or <c>null</c> if not found within 10 steps.</returns>
    internal static AutomationElement? WalkUpToClassName(AutomationElement element, string className)
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
                    if (current.ClassName == className)
                    {
                        return current;
                    }
                }
                catch
                {
                    // Some parent elements may throw when accessing ClassName
                }

                current = walker.GetParent(current);
            }
        }
        catch
        {
            // Tree walker may be unavailable
        }

        return null;
    }

    /// <summary>
    /// Activates a ToolWindow pane so its title bar buttons become interactive.
    /// Attempts four strategies in order:
    /// (1) find parent <c>ToolWindowContainer</c> tab header and click it,
    /// (2) <c>SelectionItemPattern.Select()</c>,
    /// (3) <c>Focus()</c>,
    /// (4) <c>Click()</c>.
    /// </summary>
    /// <remarks>
    /// FlaUI cannot reliably detect whether activation actually succeeded.
    /// All 6 diagnostic tool call sites use fire-and-forget semantics.
    /// </remarks>
    internal static void ActivateToolWindow(
        AutomationElement toolWindow,
        ConditionFactory cf,
        ILogger? logger = null)
    {
        var toolWindowName = SafeGetName(toolWindow);

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
                    var tabName = SafeGetName(tab);
                    if (tabName.Contains(toolWindowName, StringComparison.OrdinalIgnoreCase))
                    {
                        tab.Click();
                        logger?.LogDebug(
                            "Activated ToolWindow via tab header: {TabName}",
                            tabName);
                        return;
                    }
                }

                // No TabItem — look for a clickable header with matching text
                var headers = FindAllDescendants(container, cf.ByControlType(ControlType.Header));
                foreach (var header in headers)
                {
                    var headerName = SafeGetName(header);
                    if (headerName.Contains(toolWindowName, StringComparison.OrdinalIgnoreCase))
                    {
                        header.Click();
                        logger?.LogDebug(
                            "Activated ToolWindow via header: {HeaderName}",
                            headerName);
                        return;
                    }
                }

                logger?.LogDebug(
                    "No matching tab header found for ToolWindow {ToolWindowName}",
                    toolWindowName);
            }
        }
        catch
        {
            // Fall through to other strategies
        }

        // Strategy 2: SelectionItemPattern (TabItem-like selection)
        try
        {
            if (toolWindow.Patterns.SelectionItem.IsSupported)
            {
                toolWindow.Patterns.SelectionItem.Pattern.Select();
                logger?.LogDebug("Activated ToolWindow via SelectionItemPattern");
                return;
            }
        }
        catch
        {
            // Fall through
        }

        // Strategy 3: Focus
        try
        {
            toolWindow.Focus();
            logger?.LogDebug("Activated ToolWindow via Focus");
        }
        catch
        {
            // Focus may fail
        }

        // Strategy 4: Click as last resort
        try
        {
            toolWindow.Click();
            logger?.LogDebug("Activated ToolWindow via Click");
        }
        catch
        {
            // Click may fail
        }
    }

    /// <summary>
    /// Safely gets the <c>Name</c> property of an automation element.
    /// Returns an empty string if the property is unavailable or throws.
    /// </summary>
    internal static string SafeGetName(AutomationElement element)
    {
        try
        {
            return element.Name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Returns <c>true</c> if the element's <c>Name</c> matches any of the provided known dialog names
    /// (using ordinal comparison) or if it contains a password field (login dialog detection).
    /// Must not throw — probe semantics.
    /// </summary>
    internal static bool IsKnownDialog(AutomationElement childWindow, ConditionFactory cf)
    {
        try
        {
            var name = childWindow.Name;
            if (string.Equals(name, KnownElements.MatchSelectionDialogName, StringComparison.Ordinal) ||
                string.Equals(name, KnownElements.MatchDetailsDialogName, StringComparison.Ordinal))
            {
                return true;
            }
        }
        catch
        {
            // Name may throw on stale element
        }

        try
        {
            var passwordField = FindDescendant(
                childWindow,
                cf.ByAutomationId(KnownElements.LoginPasswordFieldAutomationId));
            return passwordField != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Finds and closes the first unexpected (non-known) dialog attached to the main window.
    /// Tries button close order: Cancel → Close → OK → any button. Best-effort, never throws.
    /// </summary>
    internal static void TryCloseFirstUnexpectedDialog(
        AutomationElement window,
        ConditionFactory cf,
        ILogger? logger = null)
    {
        try
        {
            var childWindows = FindAllDescendants(window, cf.ByControlType(ControlType.Window));

            foreach (var childWindow in childWindows)
            {
                if (IsKnownDialog(childWindow, cf))
                {
                    continue;
                }

                logger?.LogWarning(
                    "Attempting to close unexpected dialog: {DialogName}",
                    SafeGetName(childWindow));

                var closeBtn = FindButtonByChildText(childWindow, "Cancel", cf, logger)
                    ?? FindButtonByChildText(childWindow, "Close", cf, logger)
                    ?? FindButtonByChildText(childWindow, "OK", cf, logger)
                    ?? FindDescendant(childWindow, cf.ByControlType(ControlType.Button));

                if (closeBtn != null)
                {
                    InvokeButtonSafely(closeBtn, logger);
                }

                return;
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "TryCloseFirstUnexpectedDialog failed with exception");
        }
    }

    /// <summary>
    /// Returns <c>true</c> if any non-known, visible dialog is present as a child window.
    /// Filters out offscreen/invisible WPF internal elements (adorner layers, popup hosts)
    /// that expose <c>ControlType.Window</c> but are not user-facing dialogs.
    /// Must not throw — probe semantics.
    /// </summary>
    internal static bool HasUnexpectedDialog(AutomationElement window, ConditionFactory cf)
    {
        try
        {
            var childWindows = FindAllDescendants(window, cf.ByControlType(ControlType.Window));

            foreach (var childWindow in childWindows)
            {
                if (IsOffscreenOrInvisible(childWindow))
                    continue;

                if (!IsKnownDialog(childWindow, cf))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns <c>true</c> if the element is offscreen or has a zero-area bounding rectangle.
    /// Used to filter out invisible WPF internal child windows. Never throws.
    /// </summary>
    private static bool IsOffscreenOrInvisible(AutomationElement element)
    {
        try
        {
            if (element.Properties.IsOffscreen.IsSupported &&
                element.Properties.IsOffscreen.ValueOrDefault)
            {
                return true;
            }

            var rect = element.Properties.BoundingRectangle.ValueOrDefault;
            return rect.Width <= 0 || rect.Height <= 0;
        }
        catch
        {
            return false;
        }
    }

    // ─── Win32 foreground window ────────────────────────────────────────

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    /// <summary>
    /// Brings a window to the foreground so that <see cref="FlaUI.Core.Input.Keyboard"/>
    /// input is delivered to it. Must be called before any <c>Keyboard.Type()</c> or
    /// <c>Keyboard.TypeSimultaneously()</c> call.
    /// </summary>
    /// <remarks>
    /// Uses <c>ShowWindow(SW_RESTORE)</c> followed by <c>SetForegroundWindow</c>.
    /// The restore step handles minimised windows; the foreground call activates it.
    /// Never throws — returns <c>false</c> on failure.
    /// </remarks>
    internal static bool BringToForeground(IntPtr handle, ILogger? logger = null)
    {
        try
        {
            if (handle == IntPtr.Zero)
            {
                logger?.LogWarning("BringToForeground: handle is zero");
                return false;
            }

            ShowWindow(handle, SW_RESTORE);
            var result = SetForegroundWindow(handle);
            if (!result)
            {
                logger?.LogDebug("SetForegroundWindow returned false — window may already be foreground");
            }

            return result;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "BringToForeground failed");
            return false;
        }
    }

    internal static bool BringToForeground(AutomationElement window, ILogger? logger = null)
    {
        var handle = window.Properties.NativeWindowHandle.ValueOrDefault;
        return BringToForeground(handle, logger);
    }
}
