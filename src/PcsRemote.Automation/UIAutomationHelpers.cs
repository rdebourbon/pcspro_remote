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
    private static string SafeGetName(AutomationElement element)
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
}
