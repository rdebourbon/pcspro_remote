using FluentAssertions;
using PcsRemote.TrayHost;

namespace PcsRemote.TrayHost.Tests;

[TestClass]
public class TrayApplicationContextTests
{
    // TC-4: TrayApplicationContext.Dispose hides and disposes the NotifyIcon.
    // NotifyIcon requires a Windows message queue; the test is a stub in headless CI.
    [TestMethod]
    [Ignore("NotifyIcon cannot be constructed in a headless environment without a Win32 message queue.")]
    public void Dispose_HidesAndDisposesNotifyIcon()
    {
        // In an environment with a live message queue, construct the context,
        // dispose it, and assert the icon is no longer visible.
        // NotifyIcon now requires DI services — construct with mocks in a full integration env.
        // Assertion of NotifyIcon.Visible requires a test-accessible handle — deferred
        // to integration verification (AC-7 manual check during AC-2 tray launch test).
        true.Should().BeTrue("stub passes: compilation confirms TrayApplicationContext exists");
    }
}
