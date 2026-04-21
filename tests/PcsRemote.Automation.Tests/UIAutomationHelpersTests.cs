using System.Runtime.CompilerServices;
using FluentAssertions;
using FlaUI.Core.AutomationElements;

namespace PcsRemote.Automation.Tests;

[TestClass]
public sealed class UIAutomationHelpersTests
{
    // -----------------------------------------------------------------------
    // T1 — WaitForElement returns immediately when delegate returns non-null
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WaitForElement_DelegateReturnsImmediately_ReturnsElement()
    {
        // Arrange — create an uninitialized AutomationElement as a sentinel
        var sentinel = CreateSentinelElement();
        int callCount = 0;
        AutomationElement? Search()
        {
            callCount++;
            return sentinel;
        }

        // Act
        var result = UIAutomationHelpers.WaitForElement(Search, timeoutMs: 5000);

        // Assert
        result.Should().BeSameAs(sentinel);
        callCount.Should().Be(1, "search should succeed on first poll");
    }

    // -----------------------------------------------------------------------
    // T2 — WaitForElement returns null on timeout
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WaitForElement_AlwaysNull_ReturnsNullAfterTimeout()
    {
        // Arrange
        int callCount = 0;
        AutomationElement? Search()
        {
            callCount++;
            return null;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Act — use a short timeout so the test is fast
        var result = UIAutomationHelpers.WaitForElement(Search, timeoutMs: 500);

        sw.Stop();

        // Assert
        result.Should().BeNull();
        callCount.Should().BeGreaterThanOrEqualTo(2, "should have polled multiple times");
        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(400, "should wait near the timeout");
    }

    // -----------------------------------------------------------------------
    // T3 — WaitForElement respects cancellation
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WaitForElement_CancellationRequested_ReturnsNullPromptly()
    {
        // Arrange — cancel after a short delay
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        int callCount = 0;
        AutomationElement? Search()
        {
            callCount++;
            return null;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Act — use a long timeout so cancellation is the exit path
        var result = UIAutomationHelpers.WaitForElement(
            Search,
            timeoutMs: 10000,
            cancellationToken: cts.Token);

        sw.Stop();

        // Assert
        result.Should().BeNull("cancellation should cause null return");
        sw.ElapsedMilliseconds.Should().BeLessThan(2000, "should exit promptly on cancellation, not wait for full timeout");
    }

    // -----------------------------------------------------------------------
    // T4 — WaitForElement returns element found on Nth poll
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WaitForElement_FoundOnThirdPoll_ReturnsElement()
    {
        // Arrange
        var sentinel = CreateSentinelElement();
        int callCount = 0;
        AutomationElement? Search()
        {
            callCount++;
            return callCount >= 3 ? sentinel : null;
        }

        // Act
        var result = UIAutomationHelpers.WaitForElement(Search, timeoutMs: 5000);

        // Assert
        result.Should().BeSameAs(sentinel);
        callCount.Should().Be(3, "should find element on the third poll");
    }

    /// <summary>
    /// Creates an uninitialized <see cref="AutomationElement"/> instance to serve as a
    /// sentinel value in tests. This bypasses the sealed constructor via
    /// <see cref="RuntimeHelpers.GetUninitializedObject"/> — the instance is not usable
    /// for any FlaUI operations but is sufficient for identity/reference equality checks.
    /// </summary>
    private static AutomationElement CreateSentinelElement()
    {
        return (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));
    }
}
