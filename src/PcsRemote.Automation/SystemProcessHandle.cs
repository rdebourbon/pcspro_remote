using System.ComponentModel;
using System.Diagnostics;

namespace PcsRemote.Automation;

/// <summary>
/// Wraps a <see cref="Process"/> instance to implement <see cref="IProcessHandle"/>.
/// </summary>
internal sealed class SystemProcessHandle : IProcessHandle
{
    private readonly Process _process;

    internal SystemProcessHandle(Process process)
    {
        _process = process;
    }

    public int Id => _process.Id;

    public bool HasExited
    {
        get
        {
            try { return _process.HasExited; }
            // Process may have already exited and been released by the OS — treat as exited.
            catch (InvalidOperationException) { return true; }
            catch (Win32Exception) { return true; }
        }
    }

    public Task WaitForExitAsync(CancellationToken ct) =>
        _process.WaitForExitAsync(ct);

    public bool CloseMainWindow()
    {
        try { return _process.CloseMainWindow(); }
        // Process may have already exited between the HasExited check and this call.
        catch (InvalidOperationException) { return false; }
        catch (Win32Exception) { return false; }
    }

    public void Kill()
    {
        try { _process.Kill(); }
        // Process may have already exited — killing a dead process is a no-op.
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    public object? TryGetMainWindow()
    {
        try
        {
            _process.Refresh();
            var handle = _process.MainWindowHandle;
            return handle != IntPtr.Zero ? handle : null;
        }
        // Process may have exited mid-refresh — treat as window not visible.
        catch (InvalidOperationException) { return null; }
        catch (Win32Exception) { return null; }
    }

    public void Dispose() => _process.Dispose();
}
