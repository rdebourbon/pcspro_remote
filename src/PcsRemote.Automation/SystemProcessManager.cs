using System.Diagnostics;

namespace PcsRemote.Automation;

/// <summary>
/// Implements <see cref="IProcessManager"/> using <see cref="Process"/> static members.
/// </summary>
internal sealed class SystemProcessManager : IProcessManager
{
    public IReadOnlyList<IProcessHandle> GetByName(string executableName)
    {
        var processes = Process.GetProcessesByName(executableName);
        return processes.Select(p => (IProcessHandle)new SystemProcessHandle(p)).ToList();
    }

    public IProcessHandle Start(ProcessStartInfo startInfo)
    {
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"Process.Start returned null for '{startInfo.FileName}'.");
        return new SystemProcessHandle(process);
    }
}
