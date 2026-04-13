using System.Diagnostics;

namespace PcsRemote.Automation;

/// <summary>
/// Abstracts OS-level process management to allow deterministic unit testing.
/// The real implementation delegates to <see cref="Process"/> static members;
/// test fakes return controlled <see cref="IProcessHandle"/> instances.
/// </summary>
internal interface IProcessManager
{
    /// <summary>
    /// Returns handles for all running processes whose executable name matches
    /// <paramref name="executableName"/> (case-insensitive, without file extension).
    /// </summary>
    IReadOnlyList<IProcessHandle> GetByName(string executableName);

    /// <summary>
    /// Starts a new process with the supplied <paramref name="startInfo"/> and returns
    /// a handle to it. Throws if the process cannot be started.
    /// </summary>
    IProcessHandle Start(ProcessStartInfo startInfo);
}
