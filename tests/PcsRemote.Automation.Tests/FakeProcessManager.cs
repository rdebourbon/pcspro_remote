using System.Diagnostics;

namespace PcsRemote.Automation.Tests;

/// <summary>
/// Test double for <see cref="IProcessManager"/>.
/// Returns a configurable list of <see cref="FakeProcessHandle"/> for <c>GetByName</c>
/// and a single handle for <c>Start</c>.
/// </summary>
internal sealed class FakeProcessManager : IProcessManager
{
    /// <summary>
    /// Processes returned by <see cref="GetByName"/>. Represents pre-existing
    /// cricket.exe processes that should be killed before launching a new one.
    /// </summary>
    public List<FakeProcessHandle> ExistingProcesses { get; } = new();

    /// <summary>Handle returned by <see cref="Start"/> when <see cref="StartedHandleQueue"/> is empty. Must be set before calling <see cref="Start"/>.</summary>
    public FakeProcessHandle? StartedHandle { get; set; }

    /// <summary>
    /// Queue of handles returned by sequential <see cref="Start"/> calls. When non-empty,
    /// handles are dequeued in order. Falls back to <see cref="StartedHandle"/> when the
    /// queue is empty. Supports multi-start scenarios such as <see cref="IPcsProAutomationService.RetryAsync"/>.
    /// </summary>
    public Queue<FakeProcessHandle> StartedHandleQueue { get; } = new();

    /// <summary>Number of times <see cref="Start"/> has been called.</summary>
    public int StartCallCount { get; private set; }

    /// <summary>When true, <see cref="Start"/> throws <see cref="System.ComponentModel.Win32Exception"/>.</summary>
    public bool ShouldThrowOnStart { get; set; }

    /// <summary>Captures the <see cref="ProcessStartInfo"/> passed to <see cref="Start"/> for assertions.</summary>
    public ProcessStartInfo? CapturedStartInfo { get; private set; }

    public IReadOnlyList<IProcessHandle> GetByName(string executableName) =>
        ExistingProcesses;

    public IProcessHandle Start(ProcessStartInfo startInfo)
    {
        CapturedStartInfo = startInfo;
        StartCallCount++;
        if (ShouldThrowOnStart)
            throw new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified.");
        if (StartedHandleQueue.Count > 0)
            return StartedHandleQueue.Dequeue();
        return StartedHandle
            ?? throw new InvalidOperationException("FakeProcessManager.StartedHandle was not configured.");
    }
}
