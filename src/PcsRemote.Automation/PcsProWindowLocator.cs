using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace PcsRemote.Automation;

/// <summary>
/// Singleton that owns the <see cref="UIA3Automation"/> COM bridge and provides
/// per-call main window lookup for PCS Pro. All FlaUi* classes depend on this
/// to access the automation tree.
/// </summary>
/// <remarks>
/// <para>
/// Per HLPS element lifetime rules, <see cref="FindMainWindow"/> re-finds the
/// process and window on every call — no caching of <see cref="Window"/> or
/// <see cref="AutomationElement"/> references.
/// </para>
/// <para>
/// Thread safety: all FlaUI operations are serialized via
/// <see cref="PcsProAutomationService"/>'s operation lock, so a single
/// <see cref="UIA3Automation"/> instance is safe under serialized access.
/// </para>
/// </remarks>
internal sealed class PcsProWindowLocator : IDisposable
{
    private const string PcsProProcessName = "cricket";

    /// <summary>
    /// The shared UIA3 automation instance. FlaUi* classes use
    /// <see cref="Automation"/> to access the <see cref="UIA3Automation.ConditionFactory"/>.
    /// </summary>
    public UIA3Automation Automation { get; } = new();

    /// <summary>
    /// Finds the PCS Pro main window by locating the <c>cricket</c> process,
    /// attaching via <see cref="Application.Attach(Process)"/>, and returning
    /// the main window.
    /// </summary>
    /// <returns>
    /// The PCS Pro main <see cref="Window"/>, or <c>null</c> if the process
    /// is not found, is starting up, or the main window is not yet available.
    /// </returns>
    /// <remarks>
    /// Never throws — all failures (process not found, attach failure, no main
    /// window) are returned as <c>null</c>. All <see cref="Process"/> objects
    /// from <see cref="Process.GetProcessesByName(string)"/> are disposed after
    /// PID extraction to avoid handle leaks under repeated polling.
    /// </remarks>
    public Window? FindMainWindow()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(PcsProProcessName);
        }
        catch
        {
            return null;
        }

        if (processes.Length == 0)
        {
            return null;
        }

        int pid = processes[0].Id;

        foreach (var p in processes)
        {
            p.Dispose();
        }

        try
        {
            var app = Application.Attach(pid);
            return app.GetMainWindow(Automation);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        Automation.Dispose();
    }
}
