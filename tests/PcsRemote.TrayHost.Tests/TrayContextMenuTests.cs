using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace PcsRemote.TrayHost.Tests;

/// <summary>
/// Unit tests for <see cref="TrayApplicationContext"/> context menu behaviour.
/// Tests that require a Win32 message queue are marked [Ignore] pending a
/// test harness capable of running STA WinForms pumps in CI.
/// Pure logic tests (e.g. URL resolution) are exercised directly.
/// Per SPEC-S-007-ContextMenu.md §Test Cases.
/// </summary>
[TestClass]
[DoNotParallelize]
public class TrayContextMenuTests
{
    // TC-1: Toggle text updates to "Switch to Manual Mode" when automation mode is active.
    [TestMethod]
    [Ignore("Requires STA Win32 message queue — cannot run in headless CI.")]
    public void Toggle_WhenAutomationMode_SetsTextToSwitchToManualMode()
    {
        // Arrange: construct context with IManualModeService reporting IsManualModeActive=false.
        // Act: read _toggleItem.Text immediately after construction.
        // Assert: text equals "Switch to Manual Mode".
        true.Should().BeTrue("stub — see SPEC-S-007-ContextMenu.md TC-1");
    }

    // TC-2: Toggle text updates to "Resume Automation" when manual mode is active.
    [TestMethod]
    [Ignore("Requires STA Win32 message queue — cannot run in headless CI.")]
    public void Toggle_WhenManualMode_SetsTextToResumeAutomation()
    {
        // Arrange: construct context with IManualModeService reporting IsManualModeActive=true.
        // Act: read _toggleItem.Text immediately after construction.
        // Assert: text equals "Resume Automation".
        true.Should().BeTrue("stub — see SPEC-S-007-ContextMenu.md TC-2");
    }

    // TC-3: ManualModeChanged raised on a non-STA thread updates the toggle text on the STA thread.
    [TestMethod]
    [Ignore("Requires STA Win32 message queue — cannot run in headless CI.")]
    public void ManualModeChanged_WhenFiredFromNonStaThread_UpdatesToggleOnStaThread()
    {
        // Arrange: construct context; fire ManualModeChanged from a thread-pool thread.
        // Act: pump STA message queue briefly.
        // Assert: _toggleItem.Text updated; no cross-thread exception thrown.
        true.Should().BeTrue("stub — see SPEC-S-007-ContextMenu.md TC-3");
    }

    // TC-4: ResolveApplicationUrl replaces wildcard bind addresses with localhost.
    // ResolveApplicationUrl is a pure string transformation; no Win32 infrastructure needed.
    [TestMethod]
    [DataRow("http://0.0.0.0:5000",        "http://localhost:5000",        DisplayName = "IPv4 wildcard replaced")]
    [DataRow("http://[::]:5000",            "http://localhost:5000",        DisplayName = "IPv6 bracket wildcard replaced")]
    [DataRow("http://localhost:5000",       "http://localhost:5000",        DisplayName = "Localhost unchanged")]
    [DataRow("http://192.168.1.1:5000",     "http://192.168.1.1:5000",      DisplayName = "Specific IP unchanged")]
    [DataRow("http://[::1]:5000",           "http://[::1]:5000",            DisplayName = "IPv6 loopback not corrupted")]
    [DataRow("http://0.0.0.0:5000;http://0.0.0.0:5001", "http://localhost:5000", DisplayName = "First of semicolon-separated values")]
    [DataRow("  http://0.0.0.0:5000  ",    "http://localhost:5000",        DisplayName = "Whitespace trimmed")]
    public void ResolveApplicationUrl_WithVariousInputs_ReturnsCorrectUrl(
        string configuredUrl,
        string expectedUrl)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kestrel:Endpoints:Http:Url"] = configuredUrl
            })
            .Build();

        var result = TrayApplicationContext.ResolveApplicationUrl(config);

        result.Should().Be(expectedUrl);
    }

    [TestMethod]
    public void ResolveApplicationUrl_WhenNoConfigKey_ReturnsFallback()
    {
        var config = new ConfigurationBuilder().Build();

        var result = TrayApplicationContext.ResolveApplicationUrl(config);

        result.Should().Be("http://localhost:5000");
    }

    [TestMethod]
    public void ResolveApplicationUrl_WhenUrlsKeyPresent_UsesUrlsOverFallback()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["urls"] = "http://0.0.0.0:7000"
            })
            .Build();

        var result = TrayApplicationContext.ResolveApplicationUrl(config);

        result.Should().Be("http://localhost:7000");
    }

    // TC-5: Exit handler calls Application.Exit even when StopAsync throws.
    [TestMethod]
    [Ignore("Requires STA Win32 message queue — cannot run in headless CI.")]
    public void Exit_WhenStopAsyncThrows_StillCallsApplicationExit()
    {
        // Arrange: automation service stub that throws on StopAsync.
        // Act: invoke OnExitClicked; await completion.
        // Assert: Application.Exit was called; exception was swallowed and logged.
        true.Should().BeTrue("stub — see SPEC-S-007-ContextMenu.md TC-5");
    }
}
